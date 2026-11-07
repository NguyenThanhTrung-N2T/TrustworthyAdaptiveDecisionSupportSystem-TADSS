# SP-09 (task 2.26, người dùng chốt 07/11): tiêm lỗi BE ⇄ TADSS dưới tải và kiểm DB sau lượt
import sys
from pathlib import Path

import pytest

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import faults  # noqa: E402


def test_latency_toxic_slows_the_tadss_to_be_direction():
    toxic = faults.toxic_payload("latency", 1500)
    assert toxic["type"] == "latency" and toxic["stream"] == "downstream" and toxic["attributes"] == {"latency": 1500, "jitter": 0}


def test_reset_toxic_resets_the_be_to_tadss_direction_immediately():
    toxic = faults.toxic_payload("reset", 1500)
    assert toxic["type"] == "reset_peer" and toxic["stream"] == "upstream" and toxic["attributes"] == {"timeout": 0}


def test_stopping_tadss_needs_no_toxic():
    with pytest.raises(ValueError):
        faults.toxic_payload("stop-tadss", 1500)


def test_check_window_drops_the_margin_at_both_ends():
    assert faults.check_window(1000.0, 1300.0) == (1002.0, 1298.0)
    with pytest.raises(ValueError):
        faults.check_window(1000.0, 1003.0)


def test_fallback_sql_counts_only_online_bookings_created_inside_the_window():
    sql = faults.fallback_sql(1002.0, 1298.0)
    assert "b.source = 'ONLINE'" in sql and "to_timestamp(1002.0)" in sql and "to_timestamp(1298.0)" in sql
    assert "IS DISTINCT FROM 'FULL_PAYMENT_FALLBACK'" in sql and "'PENDING_APPROVAL'" in sql


def test_judge_passes_only_when_every_booking_fell_back_and_no_decision_is_duplicated():
    assert faults.judge(40, 0, 0, 0)["status"] == "PASS"
    assert faults.judge(40, 1, 0, 0)["status"] == "FAIL"
    assert faults.judge(40, 0, 2, 0)["status"] == "FAIL"
    assert faults.judge(40, 0, 0, 1)["status"] == "FAIL"
    assert faults.judge(0, 0, 0, 0)["status"] == "INSUFFICIENT"
