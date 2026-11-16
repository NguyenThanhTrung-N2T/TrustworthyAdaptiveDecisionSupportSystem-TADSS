"""Test cho floor_pretest.py trên dữ liệu nhỏ dựng tay."""
from __future__ import annotations

import pytest

import floor_pretest as fp


def _row(status: str, days: int | None = None, value: float = 1.0) -> dict:
    return {"status": status, "days_before": days, "value": value}


@pytest.mark.parametrize(("status", "days", "expected"), [
    ("No-Show", None, "no_show"), ("Canceled", 0, "late"), ("Canceled", 1, "late"), ("Canceled", 5, "mid"),
    ("Canceled", 7, "mid"), ("Canceled", 8, "early"), ("Check-Out", None, None),
])
def test_outcome_mapsDaysToPolicyBands(status, days, expected):
    assert fp.outcome(_row(status, days), (1, 7)) == expected


def test_floorShare_splitsFloorAndControllable():
    rows = [_row("Canceled", 5), _row("Canceled", 30), _row("No-Show"), _row("Check-Out")]
    result = fp.floor_share(rows, (1, 7), {"mid": 0.0, "early": 0.9})
    # sàn: hủy 2–24 giờ (1 − 0,5) + hủy sớm không bán lại được 0,1 × (1 − 0) ; chia 4 đơn
    assert result["floor"] == pytest.approx((0.5 + 0.1) / 4)
    # kiểm soát được: hủy 2–24 giờ 0,5 + không đến 1 ; hủy sớm κ = 0
    assert result["effective_abandon"] == pytest.approx(1.5 / 4)


def test_thresholdPolicy_depositsRiskiestFirst():
    test = [{"value": 1.0} for _ in range(4)]
    result = fp.threshold_policy([0.9, 0.8, 0.1, 0.05], test, [1, 1, 0, 0], target=0.0)
    assert result["deposit"] == pytest.approx(0.5)
    assert result["exposed"] == pytest.approx(0.0)
