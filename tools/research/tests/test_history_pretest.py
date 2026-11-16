"""Test cho history_pretest.py trên dữ liệu nhỏ dựng tay (không cần tải bộ dữ liệu lịch khám)."""
from __future__ import annotations

import json
from datetime import date

import pytest

import history_pretest as hp


def _visit(appt: date, booked: date | None = None, y: int = 0) -> dict:
    return {"pid": "p", "booked": booked or appt, "appt": appt, "y": y}


def test_historyAtBooking_onlyCountsVisitsBeforeBookingDay():
    row = _visit(date(2016, 5, 20), booked=date(2016, 5, 10))
    others = [_visit(date(2016, 5, 9), y=1), _visit(date(2016, 5, 10), y=1), _visit(date(2016, 5, 15), y=0), row]
    h = hp.history_at_booking(row, others)
    assert h["n"] == 1 and h["no_show"] == 1  # lần hẹn cùng ngày đặt và sau ngày đặt chưa biết kết cục


def test_historyAtBooking_decaysByHalfLife():
    row = _visit(date(2016, 6, 1), booked=date(2016, 6, 1))
    h = hp.history_at_booking(row, [_visit(date(2016, 5, 18), y=0), _visit(date(2016, 5, 4), y=1)])
    assert h["w_show"] == pytest.approx(0.5)
    assert h["w_no_show"] == pytest.approx(0.25)


def test_lowShare_countsOnlyPeopleWhoShowed():
    test = [{"has_history": True}, {"has_history": True}, {"has_history": True}]
    share, people = hp.low_share([0.0, 1.0, 0.0], test, [0, 0, 1], lambda r: r["has_history"])
    assert (share, people) == (0.5, 2)


def test_stratifiedGap_removesDifferenceExplainedByStratum():
    # Trong mỗi nhóm, người có và chưa có lịch sử được đối xử như nhau; chênh lệch thô chỉ do cơ cấu nhóm
    test = ([{"stratum": "a", "has_history": True}] * 8 + [{"stratum": "a", "has_history": False}] * 2
            + [{"stratum": "b", "has_history": True}] * 2 + [{"stratum": "b", "has_history": False}] * 8)
    deposits = [0.0] * 10 + [1.0] * 10
    assert hp.stratified_gap(deposits, test, [0] * 20) == pytest.approx(0.0)


def test_savedResults_matchCurrentParameters():
    saved = json.loads(hp.DEFAULT_RESULTS.read_text(encoding="utf-8"))
    assert saved["source"]["sha256"] == hp.SOURCE_SHA256
    assert saved["parameters"]["half_life_days"] == hp.HALF_LIFE_DAYS
    assert saved["parameters"]["split_day"] == hp.SPLIT_DAY.isoformat()
