"""Mã thoát 0/1/2 theo O-14."""
from datetime import datetime, timezone

import pytest

from evaluator import results
from evaluator.results import ERROR, FAILED, PASSED, SKIPPED, StepResult

NOW = datetime(2026, 10, 8, tzinfo=timezone.utc)


def _step(status: str) -> StepResult:
    return StepResult("x", status, NOW, NOW)


@pytest.mark.parametrize(
    ("statuses", "expected"),
    [
        ([], 0),
        ([PASSED, PASSED], 0),
        ([PASSED, SKIPPED], 0),
        ([PASSED, FAILED, PASSED], 1),
        ([ERROR, SKIPPED, SKIPPED], 2),
        ([FAILED, ERROR], 2),
    ],
)
def test_exit_codes(statuses: list[str], expected: int) -> None:
    assert results.exit_code([_step(status) for status in statuses]) == expected


def test_step_result_rejects_unknown_status() -> None:
    with pytest.raises(ValueError):
        _step("weird")


def test_step_result_to_dict_has_iso_times_and_duration() -> None:
    step = StepResult("a", PASSED, NOW, NOW.replace(minute=2), notes=["ok"])

    data = step.to_dict()

    assert data["duration_seconds"] == 120.0 and data["started_at"].startswith("2026-10-08T00:00:00") and data["notes"] == ["ok"]
