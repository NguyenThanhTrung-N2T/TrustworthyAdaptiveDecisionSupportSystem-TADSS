"""D4/D5 dựng từ `prepared.jsonl` + `requests.jsonl` của bước (e) (chỉ có `outcome`, không có body request) và cột hiệu chỉnh cửa sổ duyệt (Q-04)."""
import json
from datetime import datetime
from decimal import Decimal
from typing import Any
from zoneinfo import ZoneInfo

import pytest

from evidence import hotel_adapter, replay
from evidence.anonymize import Anonymizer
from evidence.tests.test_hotel_adapter import build as build_hotel
from evidence.tests.test_replay_mapping import build as build_replay

VN = ZoneInfo("Asia/Ho_Chi_Minh")


def _as_prepared(requests: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Dạng đọc lại từ prepared.jsonl: chỉ idempotency_key + meta, `Decimal` thành chuỗi."""
    return json.loads(json.dumps([{"idempotency_key": entry["idempotency_key"], "meta": entry["meta"]} for entry in requests], default=str))


def _request_row(key: str, status: int | None, outcome: dict[str, str] | None = None, errors: list[str] | None = None, decision_id: str = "dec-1", attempts: int = 1) -> dict[str, Any]:
    return {"key": key, "status": status, "attempts": attempts, "duration_ms": 5.0, "decision_id": decision_id, "correlation_id": "c",
            "outcome": outcome or {"status": "", "reason_code": "", "deposit_rate": "", "deposit_amount": ""}, "contract_errors": errors or []}


@pytest.mark.parametrize(
    ("strategy", "status", "lead", "expected"),
    [
        ("NEED_APPROVAL", "NEED_APPROVAL", 120, "APPROVED"),  # lead = MinLead: hạn duyệt = 0, không còn kịp
        ("NEED_APPROVAL", "NEED_APPROVAL", 121, "NEED_APPROVAL"),  # còn 1 phút
        ("NEED_APPROVAL", "NEED_APPROVAL", -30, "APPROVED"),  # nhập sau giờ chơi
        ("NEED_APPROVAL", "APPROVED", 600, "NEED_APPROVAL"),  # runtime không kịp do thời gian máy, nhưng lead gốc đủ
        ("APPROVED", "APPROVED", 10, "APPROVED"),  # strategy đã APPROVED thì cửa sổ không áp
        ("", "NEED_APPROVAL", 10, "NEED_APPROVAL"),  # thiếu strategy_status: không hiệu chỉnh được
        ("NEED_APPROVAL", "", 10, ""),  # không có kết quả
    ],
)
def test_adjusted_status_applies_the_approval_window_rule(strategy: str, status: str, lead: int, expected: str) -> None:
    assert replay.adjusted_status(strategy, status, lead) == expected


def test_d4_from_outcomes_matches_pairs_by_key_and_has_adjusted_column() -> None:
    requests = build_replay()
    prepared = _as_prepared(requests)
    keys = [entry["idempotency_key"] for entry in prepared]
    outcomes = {
        keys[0]: _request_row(keys[0], 200, {"status": "APPROVED", "reason_code": "PEAK", "deposit_rate": "0.5", "deposit_amount": "114000"}, decision_id="d-1"),
        keys[1]: _request_row(keys[1], 200, {"status": "NEED_APPROVAL", "reason_code": "HIGH", "deposit_rate": "", "deposit_amount": ""}, decision_id="d-2"),
        keys[2]: _request_row(keys[2], 503, errors=["STATUS: HTTP 503, kỳ vọng 200"], decision_id=None),
    }
    d1_index = {"d-1": {"strategy_status": "APPROVED"}, "d-2": {"strategy_status": "NEED_APPROVAL"}}

    rows = replay.d4_rows_from_outcomes(prepared[:3], outcomes, d1_index)

    assert len(rows) == 6 and [row["policy"] for row in rows] == ["STATIC_100", "TADSS_V2"] * 3
    assert all(set(row) == set(replay.D4_COLUMNS) for row in rows) and "status_window_adjusted" in replay.D4_COLUMNS
    static, first, second, third = rows[0], rows[1], rows[3], rows[5]
    assert static["status"] == "APPROVED" and static["deposit_rate"] == "1" and static["status_window_adjusted"] == "APPROVED" and static["contract_errors"] == ""
    assert (first["status"], first["deposit_rate"], first["deposit_amount"], first["decision_id"], first["strategy_status"]) == ("APPROVED", "0.5", "114000", "d-1", "APPROVED")
    assert first["status_window_adjusted"] == "APPROVED"
    assert second["status"] == "NEED_APPROVAL" and second["strategy_status"] == "NEED_APPROVAL"
    assert second["status_window_adjusted"] == replay.adjusted_status("NEED_APPROVAL", "NEED_APPROVAL", second["lead_time_minutes"])
    assert third["status"] == "" and "HTTP 503" in third["contract_errors"] and third["status_window_adjusted"] == ""


def test_d4_from_outcomes_marks_missing_result_as_not_sent() -> None:
    prepared = _as_prepared(build_replay())[:1]

    rows = replay.d4_rows_from_outcomes(prepared, {}, {})

    assert rows[1]["status"] == "" and rows[1]["contract_errors"].startswith("NOT_SENT")


def test_d4_from_outcomes_keeps_request_errors_of_unsent_request() -> None:
    prepared = _as_prepared(build_replay())[:1]
    key = prepared[0]["idempotency_key"]
    outcomes = {key: _request_row(key, None, errors=["SCHEMA: items: quá nhiều"], decision_id=None, attempts=0)}

    rows = replay.d4_rows_from_outcomes(prepared, outcomes, {})

    assert rows[1]["contract_errors"] == "SCHEMA: items: quá nhiều"


def test_d4_rows_from_bodies_also_have_the_adjusted_column() -> None:
    requests = build_replay()[:1]
    body_ok = {
        "status": 200, "decision_id": "d-9",
        "body": {"decisionId": "d-9", "reservationId": requests[0]["body"]["reservationId"], "status": "NEED_APPROVAL", "deposit": None, "strategyName": "x", "strategyVersion": "1",
                 "executionType": "AUTOMATED", "fallbackUsed": False, "reasonCode": "HIGH", "reasons": [], "recommendations": [], "approvalExpiresAt": "2026-10-09T05:30:00.000Z",
                 "evaluatedAt": "2026-10-09T05:00:00.000Z"},
    }

    rows = replay.d4_rows(requests, [body_ok], {"d-9": {"strategy_status": "NEED_APPROVAL"}})

    assert rows[1]["status_window_adjusted"] == replay.adjusted_status("NEED_APPROVAL", "NEED_APPROVAL", rows[1]["lead_time_minutes"])


def test_d5_from_outcomes_has_requested_columns() -> None:
    requests = build_hotel()
    prepared = _as_prepared(requests)[:3]
    keys = [entry["idempotency_key"] for entry in prepared]
    outcomes = {
        keys[0]: _request_row(keys[0], 200, {"status": "APPROVED", "reason_code": "TIER", "deposit_rate": "0.5", "deposit_amount": "42.75"}, decision_id="p-1", attempts=2),
        keys[1]: _request_row(keys[1], 503, errors=["STATUS: HTTP 503, kỳ vọng 200"], decision_id=None, attempts=3),
    }

    rows = hotel_adapter.d5_rows_from_outcomes(prepared, outcomes, {"p-1": {"strategy_status": "APPROVED"}})

    assert [list(row) for row in rows] == [list(hotel_adapter.D5_COLUMNS)] * 3
    for column in ("decision_id", "deposit_type", "market_segment", "shift_days", "attempts", "contract_errors"):
        assert column in hotel_adapter.D5_COLUMNS
    first = rows[0]
    assert (first["status"], first["deposit_rate"], first["deposit_amount"], first["decision_id"], first["attempts"]) == ("APPROVED", "0.5", "42.75", "p-1", 2)
    assert first["deposit_type"] and first["market_segment"] and first["total_price"] == "85.5" and first["strategy_status"] == "APPROVED"
    assert rows[1]["status"] == "" and rows[1]["attempts"] == 3 and "HTTP 503" in rows[1]["contract_errors"]
    assert rows[2]["contract_errors"].startswith("NOT_SENT") and rows[2]["attempts"] == ""
