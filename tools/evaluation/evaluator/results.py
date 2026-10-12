"""Kết quả từng bước và mã thoát của evaluator (O-14)."""
from __future__ import annotations

from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from typing import Any

PASSED = "passed"
FAILED = "failed"
SKIPPED = "skipped"
ERROR = "error"
STATUSES = (PASSED, FAILED, SKIPPED, ERROR)

EXIT_OK = 0
EXIT_CRITERIA_FAILED = 1
EXIT_INFRASTRUCTURE_ERROR = 2


def utc_now() -> datetime:
    """Thời điểm hiện tại theo UTC (một nơi duy nhất để test thay được)."""
    return datetime.now(timezone.utc)


@dataclass
class StepResult:
    """Kết quả một bước: tên, trạng thái, thời gian, tệp sinh ra và ghi chú đọc được."""

    name: str
    status: str
    started_at: datetime
    ended_at: datetime
    artifacts: list[str] = field(default_factory=list)
    notes: list[str] = field(default_factory=list)

    def __post_init__(self) -> None:
        if self.status not in STATUSES:
            raise ValueError(f"Trạng thái bước không hợp lệ: {self.status!r}")

    @property
    def duration_seconds(self) -> float:
        """Thời lượng bước (giây)."""
        return (self.ended_at - self.started_at).total_seconds()

    def to_dict(self) -> dict[str, Any]:
        """Dạng JSON (thời gian ISO 8601 UTC)."""
        data = asdict(self)
        data["started_at"] = self.started_at.isoformat()
        data["ended_at"] = self.ended_at.isoformat()
        data["duration_seconds"] = round(self.duration_seconds, 1)
        return data


def skipped_result(name: str, reason: str) -> StepResult:
    """Bước không chạy (bước trước lỗi hạ tầng, hoặc chưa hiện thực)."""
    now = utc_now()
    return StepResult(name=name, status=SKIPPED, started_at=now, ended_at=now, notes=[reason])


def exit_code(results: list[StepResult]) -> int:
    """
    0: chạy hết và mọi tiêu chí đạt; 1: chạy hết nhưng có tiêu chí không đạt; 2: lỗi hạ tầng hoặc không chạy hết.
    Bước `skipped` do người dùng chọn tập con (EVAL_STEPS) không có trong danh sách nên không ảnh hưởng; `skipped` do lỗi hạ tầng đi kèm một bước `error` nên mã vẫn là 2.
    """
    if any(result.status == ERROR for result in results):
        return EXIT_INFRASTRUCTURE_ERROR
    if any(result.status == FAILED for result in results):
        return EXIT_CRITERIA_FAILED
    return EXIT_OK
