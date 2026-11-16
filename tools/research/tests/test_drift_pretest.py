"""Test cho drift_pretest.py trên dữ liệu nhỏ dựng tay."""
from __future__ import annotations

from datetime import date

import pytest

import drift_pretest as dr


def _order(booked: date, arrival: date, value: float = 1.0) -> dict:
    return {"booked": booked, "arrival": arrival, "value": value, "ym": arrival.year * 100 + arrival.month}


def test_simulate_fixedEps_matchesBudgetRule():
    test = [_order(date(2016, 1, 1), date(2016, 1, 5)), _order(date(2016, 1, 2), date(2016, 1, 6))]
    deposits = dr.simulate([0.05, 0.5], test, [1, 1], eps0=0.02, target=0.01, eta=0.0)
    assert deposits == pytest.approx([1 - 0.02 / 0.05, 1 - 0.02 / 0.5])


def test_simulate_outcomeNotUsedBeforeArrival():
    # Đơn đầu bị bỏ nhưng chỉ đến ngày 10 mới biết; đơn thứ hai đặt ngày 5 nên vẫn dùng ε ban đầu
    test = [_order(date(2016, 1, 1), date(2016, 1, 10)), _order(date(2016, 1, 5), date(2016, 1, 20))]
    deposits = dr.simulate([0.1, 0.1], test, [1, 0], eps0=0.05, target=0.0, eta=1.0)
    assert deposits[1] == pytest.approx(deposits[0])


def test_simulate_lossTightensLaterDecisions():
    test = [_order(date(2016, 1, 1), date(2016, 1, 2)), _order(date(2016, 1, 3), date(2016, 1, 9))]
    deposits = dr.simulate([0.1, 0.1], test, [1, 0], eps0=0.05, target=0.0, eta=1.0)
    assert deposits[1] > deposits[0]


def test_summarize_reportsRatioToTarget():
    test = [_order(date(2016, 1, 1), date(2016, 2, 1)), _order(date(2016, 1, 1), date(2016, 3, 1))]
    result = dr.summarize([0.0, 0.0], test, [1, 0], target=0.25)
    assert result["ratio_to_target"] == pytest.approx(2.0)
    assert result["monthly_ratio_to_target"] == {"201602": pytest.approx(4.0), "201603": pytest.approx(0.0)}


def test_monthlyAbandonShare_weightsByValue():
    rows = [{"ym": 201601, "value": 100.0, "status": "No-Show", "days_before": None, "canceled": 0},
            {"ym": 201601, "value": 300.0, "status": "Check-Out", "days_before": None, "canceled": 0},
            {"ym": 201602, "value": 50.0, "status": "Canceled", "days_before": 30, "canceled": 1}]
    assert dr.monthly_abandon_share(rows) == {"201601": pytest.approx(0.25), "201602": pytest.approx(0.0)}
