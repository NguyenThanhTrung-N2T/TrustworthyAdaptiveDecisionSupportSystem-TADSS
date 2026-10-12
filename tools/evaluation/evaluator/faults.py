"""Ghi file profile tiêm lỗi (O-17): JSON trong volume `eval-faults`, ghi file tạm rồi đổi tên để instance không đọc nửa chừng."""
from __future__ import annotations

import json
import os
from pathlib import Path

DEFAULT_FAULT_FILE = "/faults/fault.json"
PROFILE_NONE = "None"
PROFILE_DELAY = "Delay"
PROFILE_THROW = "Throw"
STRATEGY_DELAY_MS = 2000


def write_profile(path: Path, profile: str, delay_ms: int = STRATEGY_DELAY_MS) -> None:
    """Đặt `Tadss:Testing:FaultInjection:{Profile, DelayMs}`; luôn ghi cả hai khóa vì thiếu `Profile` bị coi là nạp lại thất bại."""
    document = {"Tadss": {"Testing": {"FaultInjection": {"Profile": profile, "DelayMs": delay_ms}}}}
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(document), encoding="utf-8")
    os.replace(temporary, path)
