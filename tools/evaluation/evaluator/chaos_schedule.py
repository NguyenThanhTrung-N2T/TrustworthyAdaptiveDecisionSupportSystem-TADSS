"""Lịch sự cố LT-03 (O-16): bảng cố định theo chế độ, mỗi pha lỗi theo sau bởi pha hồi phục đủ dài cho breaker đóng lại và metric kịp thấy."""
from __future__ import annotations

from dataclasses import dataclass

BASELINE = "baseline"
FAULT = "fault"
RECOVERY = "recovery"

KIND_STRATEGY_DELAY = "strategy_delay"
KIND_STRATEGY_THROW = "strategy_throw"
KIND_POSTGRES_DOWN = "postgres_down"
KIND_REDIS_LATENCY = "redis_latency"
STRATEGY_KINDS = (KIND_STRATEGY_DELAY, KIND_STRATEGY_THROW)

# Giá trị cấu hình đánh giá mà lịch bám theo; test_chaos_schedule đối chiếu với mã nguồn và docker-compose.yml
STRATEGY_BREAK_SECONDS = 15
REDIS_BREAK_SECONDS = 30
METRIC_EXPORT_SECONDS = 5
CLEAN_WINDOW_SECONDS = 10
RECOVERY_REPORT_LATENCY_SECONDS = 10
RECOVERY_RESERVE_SECONDS = 10
REDIS_LATENCY_MS = 200


@dataclass(frozen=True)
class Phase:
    """Một pha của lịch: khoảng [start, end) giây tính từ lúc k6 bắt đầu; `fi_id`/`kind` rỗng ở pha nền."""

    name: str
    role: str
    start: int
    end: int
    fi_id: str = ""
    kind: str = ""

    @property
    def length(self) -> int:
        return self.end - self.start


def _build(rows: list[tuple[str, str, int, str, str]]) -> tuple[Phase, ...]:
    phases, cursor = [], 0
    for name, role, length, fi_id, kind in rows:
        phases.append(Phase(name, role, cursor, cursor + length, fi_id, kind))
        cursor += length
    return tuple(phases)


def _rows(lengths: tuple[int, ...]) -> list[tuple[str, str, int, str, str]]:
    """`lengths` = độ dài (giây) lần lượt: nền, rồi (lỗi, hồi phục) của FI-02, FI-03, FI-08, FI-07."""
    baseline, *rest = lengths
    rows = [("Nền", BASELINE, baseline, "", "")]
    faults = (("FI-02", KIND_STRATEGY_DELAY), ("FI-03", KIND_STRATEGY_THROW), ("FI-08", KIND_POSTGRES_DOWN), ("FI-07", KIND_REDIS_LATENCY))
    for index, (fi_id, kind) in enumerate(faults):
        rows.append((fi_id, FAULT, rest[2 * index], fi_id, kind))
        rows.append((f"Hồi phục {fi_id}", RECOVERY, rest[2 * index + 1], fi_id, kind))
    return rows


SCHEDULES: dict[str, tuple[Phase, ...]] = {
    # Bản nhanh: nền 0:30; FI-02 0:30–1:00 + hồi phục 60 s; FI-03 2:00–2:30 + 60 s; FI-08 3:30–4:00 + 60 s; FI-07 5:00–5:30 + 75 s ⇒ 6:45
    "quick": _build(_rows((30, 30, 60, 30, 60, 30, 60, 30, 75))),
    # Bản đầy đủ: nền 2:00; FI-02 2:00–4:00 + 90 s; FI-03 5:30–7:00 + 90 s; FI-08 8:30–9:30 + 90 s; FI-07 11:00–12:30 + 90 s ⇒ 14:00
    "full": _build(_rows((120, 120, 90, 90, 90, 60, 90, 90, 90))),
}


def schedule_for(mode: str) -> tuple[Phase, ...]:
    return SCHEDULES[mode]


def total_seconds(mode: str) -> int:
    return SCHEDULES[mode][-1].end


def recovery_deadline_seconds(kind: str, export_seconds: int = METRIC_EXPORT_SECONDS) -> int:
    """
    Thời điểm muộn nhất (giây sau khi gỡ lỗi) để cửa sổ sạch 10 s kết thúc: T_hồi_phục + 2 × chu kỳ xuất + độ trễ ghi nhận + cửa sổ sạch (O-16).
    T_hồi_phục = thời gian mở của breaker liên quan (strategy 15 s, Redis 30 s) hoặc 0 khi không có breaker (PostgreSQL).
    """
    recover = {KIND_STRATEGY_DELAY: STRATEGY_BREAK_SECONDS, KIND_STRATEGY_THROW: STRATEGY_BREAK_SECONDS, KIND_REDIS_LATENCY: REDIS_BREAK_SECONDS, KIND_POSTGRES_DOWN: 0}[kind]
    return recover + 2 * export_seconds + RECOVERY_REPORT_LATENCY_SECONDS + CLEAN_WINDOW_SECONDS


def min_recovery_length(kind: str, export_seconds: int = METRIC_EXPORT_SECONDS) -> int:
    """Độ dài tối thiểu của pha hồi phục = hạn chót + 10 s dự phòng (≥ 55 s sau lỗi strategy, ≥ 70 s sau FI-07)."""
    return recovery_deadline_seconds(kind, export_seconds) + RECOVERY_RESERVE_SECONDS
