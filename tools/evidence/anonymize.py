"""Ẩn danh hóa định danh bằng HMAC-SHA256 (DATASET_SCHEMA Mục 4). Khóa ngẫu nhiên cho mỗi lượt chạy và KHÔNG được lưu, nên mã băm không đảo ngược được và hai lượt chạy không so khớp được với nhau."""
from __future__ import annotations

import hashlib
import hmac
import os
import uuid

HASH_HEX_LENGTH = 32


class Anonymizer:
    """Cùng một định danh (cùng loại) luôn cho cùng mã băm trong một lượt chạy; loại khác nhau không trùng mã dù cùng giá trị."""

    def __init__(self, key: bytes | None = None) -> None:
        self._key = key if key is not None else os.urandom(32)

    def hash(self, kind: str, value: str | None) -> str:
        """`kind` là loại định danh (`reservation`, `branch`, `actor`…); giá trị rỗng/None → chuỗi rỗng (trường tùy chọn để trống)."""
        if value is None or value == "":
            return ""
        digest = hmac.new(self._key, f"{kind}:{value}".encode("utf-8"), hashlib.sha256).hexdigest()
        return digest[:HASH_HEX_LENGTH]


def uuid_from_hash(hash_hex: str) -> str:
    """16 byte đầu của mã băm `Anonymizer.hash` thành UUID v4 hợp lệ (đặt bit version và variant); dùng chung cho replay SmashCourt và adapter khách sạn."""
    raw = bytearray(bytes.fromhex(hash_hex)[:16])
    raw[6] = (raw[6] & 0x0F) | 0x40
    raw[8] = (raw[8] & 0x3F) | 0x80
    return str(uuid.UUID(bytes=bytes(raw)))
