"""Lịch LT-03 (O-16): pha liên tiếp không chồng, pha hồi phục đủ dài cho cấu hình thật của stack đánh giá, khớp modes.json."""
import json
import re
from pathlib import Path

import pytest

from evaluator import chaos_schedule as schedule
from evaluator.steps.load_step import duration_seconds

REPO = Path(__file__).resolve().parents[3]
COMPOSE = (REPO / "docker-compose.yml").read_text(encoding="utf-8")
MODES = json.loads((REPO / "tests" / "load" / "config" / "modes.json").read_text(encoding="utf-8"))


def _default(path: str, name: str) -> int:
    source = (REPO / path).read_text(encoding="utf-8")
    return int(re.search(rf"\b{name}\s*\{{\s*get;\s*set;\s*\}}\s*=\s*(\d+);", source).group(1))


@pytest.mark.parametrize("mode", ["quick", "full"])
def test_schedule_ordered_non_overlapping(mode: str) -> None:
    phases = schedule.schedule_for(mode)

    assert phases[0].start == 0
    for before, after in zip(phases, phases[1:]):
        assert before.end == after.start, "pha liên tiếp, không chồng, không hở"
        assert before.length > 0
    for index, phase in enumerate(phases):
        if phase.role == schedule.FAULT:
            following = phases[index + 1]
            assert (following.role, following.fi_id) == (schedule.RECOVERY, phase.fi_id), "mỗi lỗi có pha hồi phục ngay sau"
    assert [p.fi_id for p in phases if p.role == schedule.FAULT] == ["FI-02", "FI-03", "FI-08", "FI-07"]


@pytest.mark.parametrize("mode,expected", [("quick", 405), ("full", 840)])
def test_total_matches_o16_table_and_k6_profile(mode: str, expected: int) -> None:
    assert schedule.total_seconds(mode) == expected
    profile = MODES[mode]["lt03"]
    assert duration_seconds(profile["warmup"]) == 0, "lịch tính từ lúc k6 bắt đầu: không có warm-up"
    assert duration_seconds(profile["duration"]) == expected, "thời lượng tải khớp tổng lịch"


def test_config_constants_match_source_and_compose() -> None:
    assert schedule.STRATEGY_BREAK_SECONDS == _default("src/TADSS.Infrastructure/Options/ResilienceOptions.cs", "CircuitBreakerBreakDurationSeconds")
    assert schedule.REDIS_BREAK_SECONDS == _default("src/TADSS.Infrastructure/Options/CacheOptions.cs", "RedisBreakerBreakDurationSeconds")
    exports = re.findall(r'Tadss__Telemetry__MetricExportIntervalMs: "(\d+)"', COMPOSE)
    assert exports and all(int(value) == schedule.METRIC_EXPORT_SECONDS * 1000 for value in exports)


@pytest.mark.parametrize("mode", ["quick", "full"])
def test_recovery_phases_cover_required_time(mode: str) -> None:
    for phase in schedule.schedule_for(mode):
        if phase.role == schedule.RECOVERY:
            assert phase.length >= schedule.min_recovery_length(phase.kind), f"{phase.name} ngắn hơn mức O-16"


def test_required_recovery_lengths_match_o16() -> None:
    assert schedule.min_recovery_length(schedule.KIND_STRATEGY_DELAY) == 55
    assert schedule.min_recovery_length(schedule.KIND_STRATEGY_THROW) == 55
    assert schedule.min_recovery_length(schedule.KIND_REDIS_LATENCY) == 70
    assert schedule.min_recovery_length(schedule.KIND_POSTGRES_DOWN) == 40


def test_changing_breaker_time_would_break_schedule(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(schedule, "STRATEGY_BREAK_SECONDS", 60)

    short = [p for p in schedule.schedule_for("quick") if p.role == schedule.RECOVERY and p.kind in schedule.STRATEGY_KINDS]
    assert any(p.length < schedule.min_recovery_length(p.kind) for p in short), "đổi cấu hình làm thiếu thì test đỏ"
