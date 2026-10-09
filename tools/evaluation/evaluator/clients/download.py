"""Tải dataset có kiểm SHA-256 vào cache (PT-01, Q-06): cache đúng băm thì dùng lại; chưa có thì tải vào file tạm, kiểm băm rồi đổi tên nguyên tử.

- Sai băm → xóa file tạm, ném `DatasetIntegrityError` (bước (e) báo `failed`).
- Mất mạng, HTTP lỗi, hết giờ hoặc đứt giữa chừng (kể cả thân bị cắt cụt: ít byte hơn `Content-Length`, `IncompleteRead`) → xóa file tạm, ném `DatasetUnavailableError` (bước (e) bỏ qua phần PT, không làm hỏng cả lượt).
Hàm mở URL (`opener`) được truyền vào để test không cần mạng thật; mặc định `urllib.request.urlopen`.
"""
from __future__ import annotations

import hashlib
import http.client
import os
import urllib.error
import urllib.request
import uuid
from pathlib import Path
from typing import Callable, IO
from urllib.parse import urlsplit

CHUNK_BYTES = 1024 * 1024
DEFAULT_TIMEOUT_SECONDS = 60.0

Opener = Callable[[str], IO[bytes]]


class DatasetIntegrityError(Exception):
    """File tải về có SHA-256 khác giá trị đã ghim: dữ liệu không được dùng làm minh chứng."""


class DatasetUnavailableError(Exception):
    """Không tải được dataset (mất mạng, HTTP lỗi, hết giờ): không kết luận gì về tính toàn vẹn của dữ liệu."""


def sha256_of(path: Path) -> str:
    """SHA-256 (hex thường) của file, đọc theo khối."""
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        while chunk := handle.read(CHUNK_BYTES):
            digest.update(chunk)
    return digest.hexdigest()


def _declared_length(source: IO[bytes]) -> int | None:
    """`Content-Length` do server khai báo (None nếu không có hoặc opener giả không có header)."""
    headers = getattr(source, "headers", None)
    value = headers.get("Content-Length") if headers is not None else None
    try:
        return int(value) if value is not None else None
    except ValueError:
        return None


def _default_opener(url: str) -> IO[bytes]:
    return urllib.request.urlopen(url, timeout=DEFAULT_TIMEOUT_SECONDS)


def fetch(url: str, sha256: str, cache_dir: Path, opener: Opener | None = None) -> Path:
    """Đường dẫn file dataset trong `cache_dir` (tên `<sha256>-<tên file của URL>`), đã kiểm SHA-256.

    Cache có file đúng băm → dùng lại, không tải. File cache sai băm (hỏng/bị sửa) → tải lại. Tải vào file tạm cùng thư mục rồi `os.replace` nên không bao giờ để lại file dở dang."""
    expected = sha256.lower()
    cache_dir.mkdir(parents=True, exist_ok=True)
    name = Path(urlsplit(url).path).name or "dataset.csv"
    target = cache_dir / f"{expected}-{name}"
    if target.is_file() and sha256_of(target) == expected:
        return target

    temporary = cache_dir / f".{target.name}.{uuid.uuid4().hex}.part"
    digest = hashlib.sha256()
    received = 0
    try:
        try:
            with (opener or _default_opener)(url) as source, temporary.open("wb") as handle:
                declared = _declared_length(source)
                while chunk := source.read(CHUNK_BYTES):
                    handle.write(chunk)
                    digest.update(chunk)
                    received += len(chunk)
        except (urllib.error.URLError, http.client.HTTPException, OSError, TimeoutError) as error:  # HTTPError là URLError; ConnectionError/timeout là OSError; IncompleteRead là HTTPException
            raise DatasetUnavailableError(f"không tải được {url}: {type(error).__name__}: {error}") from error
        if declared is not None and received != declared:  # urllib không báo lỗi khi server đóng sớm: đọc ngắn/rỗng
            raise DatasetUnavailableError(f"không tải được {url}: thân bị cắt cụt ({received}/{declared} byte)")
        actual = digest.hexdigest()
        if actual != expected:
            raise DatasetIntegrityError(f"SHA-256 của {url} là {actual}, kỳ vọng {expected}")
        os.replace(temporary, target)
        return target
    finally:
        temporary.unlink(missing_ok=True)
