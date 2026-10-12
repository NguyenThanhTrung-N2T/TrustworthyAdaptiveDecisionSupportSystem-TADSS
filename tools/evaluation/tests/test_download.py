"""Tải dataset có kiểm SHA-256 (PT-01, Q-06): dùng lại cache đúng băm, tải mới + đổi tên nguyên tử, sai băm xóa file tạm, mất mạng báo riêng. Không có mạng thật: opener giả,
riêng ca thân bị cắt cụt dùng socket server thật trên 127.0.0.1."""
import hashlib
import io
import socket
import threading
import urllib.error
from pathlib import Path

import pytest

from evaluator.clients import download

CONTENT = b"hotel,adr\nCity Hotel,85.5\n" * 50
GOOD = hashlib.sha256(CONTENT).hexdigest()
URL = "https://example.test/data/hotels.csv"


class FakeOpener:
    """Opener giả: ghi lại các URL được mở; `payload` là thân trả về, `error` (nếu có) được ném khi mở hoặc giữa chừng khi đọc."""

    def __init__(self, payload: bytes = CONTENT, error: Exception | None = None, fail_mid_read: bool = False) -> None:
        self.payload = payload
        self.error = error
        self.fail_mid_read = fail_mid_read
        self.opened: list[str] = []

    def __call__(self, url: str):
        self.opened.append(url)
        if self.error is not None and not self.fail_mid_read:
            raise self.error
        stream = io.BytesIO(self.payload)
        if self.fail_mid_read:
            error = self.error

            class Broken(io.BytesIO):
                def read(self, size: int = -1) -> bytes:
                    chunk = super().read(size)
                    if not chunk or self.tell() >= 10:
                        raise error
                    return chunk

            return Broken(self.payload)
        return stream


def _files(directory: Path) -> list[str]:
    return sorted(path.name for path in directory.iterdir())


def test_download_Theory_downloads_when_cache_empty(tmp_path: Path) -> None:
    opener = FakeOpener()

    path = download.fetch(URL, GOOD, tmp_path / "cache", opener=opener)

    assert path.read_bytes() == CONTENT and path.parent == tmp_path / "cache"
    assert opener.opened == [URL]
    assert _files(tmp_path / "cache") == [path.name], "không để lại file tạm"


def test_download_Theory_reuses_cache_with_correct_hash(tmp_path: Path) -> None:
    first = download.fetch(URL, GOOD, tmp_path, opener=FakeOpener())
    opener = FakeOpener(error=AssertionError("không được tải lại"))

    second = download.fetch(URL, GOOD, tmp_path, opener=opener)

    assert second == first and opener.opened == []


def test_download_Theory_redownloads_when_cache_has_wrong_hash(tmp_path: Path) -> None:
    first = download.fetch(URL, GOOD, tmp_path, opener=FakeOpener())
    first.write_bytes(b"da bi hong")
    opener = FakeOpener()

    again = download.fetch(URL, GOOD, tmp_path, opener=opener)

    assert again.read_bytes() == CONTENT and opener.opened == [URL]


def test_download_Theory_wrong_hash_raises_and_leaves_no_file(tmp_path: Path) -> None:
    with pytest.raises(download.DatasetIntegrityError, match="SHA-256"):
        download.fetch(URL, "0" * 64, tmp_path, opener=FakeOpener())

    assert _files(tmp_path) == []


@pytest.mark.parametrize(
    "error",
    [urllib.error.URLError("khong co mang"), urllib.error.HTTPError(URL, 404, "Not Found", None, None), TimeoutError("het gio"), ConnectionResetError("dut")],
)
def test_download_Theory_network_failure_is_unavailable(tmp_path: Path, error: Exception) -> None:
    with pytest.raises(download.DatasetUnavailableError):
        download.fetch(URL, GOOD, tmp_path, opener=FakeOpener(error=error))

    assert _files(tmp_path) == []


def test_download_Theory_network_failure_mid_stream_removes_partial_file(tmp_path: Path) -> None:
    with pytest.raises(download.DatasetUnavailableError):
        download.fetch(URL, GOOD, tmp_path, opener=FakeOpener(error=ConnectionResetError("dut"), fail_mid_read=True))

    assert _files(tmp_path) == []


def test_download_integrity_and_unavailable_are_distinct() -> None:
    assert not issubclass(download.DatasetIntegrityError, download.DatasetUnavailableError)
    assert not issubclass(download.DatasetUnavailableError, download.DatasetIntegrityError)


def test_sha256_of_matches_hashlib(tmp_path: Path) -> None:
    path = tmp_path / "x.bin"
    path.write_bytes(CONTENT)

    assert download.sha256_of(path) == GOOD


def _truncating_server(declared: int, sent: int):
    """Server socket thật: khai báo `Content-Length: declared`, gửi `sent` byte rồi đóng kết nối. Trả (cổng, luồng)."""
    listener = socket.socket()
    listener.bind(("127.0.0.1", 0))
    listener.listen(1)

    def serve() -> None:
        connection, _ = listener.accept()
        with connection:
            connection.recv(4096)
            connection.sendall(f"HTTP/1.1 200 OK\r\nContent-Length: {declared}\r\nContent-Type: text/csv\r\n\r\n".encode("ascii") + b"x" * sent)
        listener.close()

    thread = threading.Thread(target=serve, daemon=True)
    thread.start()
    return listener.getsockname()[1], thread


def test_download_Theory_truncated_body_is_unavailable_not_integrity(tmp_path: Path) -> None:
    port, thread = _truncating_server(declared=1000, sent=500)

    with pytest.raises(download.DatasetUnavailableError, match="cắt cụt|500|1000|Incomplete"):
        download.fetch(f"http://127.0.0.1:{port}/hotels.csv", GOOD, tmp_path)

    thread.join(5)
    assert _files(tmp_path) == [], "không để lại file tạm"


def test_download_Theory_complete_body_with_wrong_hash_stays_integrity_error(tmp_path: Path) -> None:
    port, thread = _truncating_server(declared=500, sent=500)

    with pytest.raises(download.DatasetIntegrityError):
        download.fetch(f"http://127.0.0.1:{port}/hotels.csv", GOOD, tmp_path)

    thread.join(5)
    assert _files(tmp_path) == []
