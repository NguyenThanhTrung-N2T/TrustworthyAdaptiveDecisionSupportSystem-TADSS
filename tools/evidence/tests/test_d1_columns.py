"""D1 decision_audit: đủ cột, ẩn danh, đặc trưng request tách đúng, trường tùy chọn để trống."""
import csv
import json
from datetime import datetime, timezone
from pathlib import Path

from evidence import d1_export
from evidence.anonymize import Anonymizer

CREATED = datetime(2026, 10, 9, 3, 0, tzinfo=timezone.utc)
PAYLOAD = {
    "reservationId": "r-1", "branchId": "b-1", "branchTimeZone": "Asia/Ho_Chi_Minh", "totalPrice": 250000, "currency": "VND", "requestedAt": "2026-10-09T03:00:00+00:00",
    "items": [{"startTime": "2026-10-20T11:00:00+07:00", "price": 100000}, {"startTime": "2026-10-20T10:00:00+07:00", "price": 150000}],
    "customer": {"isGuest": False, "tier": "GOLD", "memberSince": "2026-09-09T03:00:00+00:00",
                 "history": {"completedCount": 5, "noShowCount": 1, "cancelledCount": 2, "lateCancelledCount": 1, "paymentExpiredCount": 0}},
    "demand": {"occupancyRate": 0.7, "waitlistCount": 3},
}


def _row(role: str = "PRIMARY", payload: dict | None = None, override: bool = False, outcome: bool = False) -> tuple:
    return (
        "d-1", "res-guid", "branch-guid", role, "corr-1", CREATED, "DefaultDecisionStrategy", "1.0.0", "APPROVED", "APPROVED", 0.3, 75000, "OK", None,
        "AUTOMATED", 12, False, None, False, "h" * 64, json.dumps(payload or PAYLOAD),
        "APPROVE" if override else None, 0.5 if override else None, "manager-01" if override else None, CREATED if override else None,
        "COMPLETED" if outcome else None, CREATED if outcome else None,
    )


def test_d1_columns(tmp_path: Path) -> None:
    path = tmp_path / "d1.csv"
    rows = [_row(), _row("SHADOW", override=True, outcome=True)]

    count = d1_export.export_d1(lambda sql, params: rows, Anonymizer(b"k"), CREATED, path)

    assert count == 2
    parsed = list(csv.DictReader(path.open(encoding="utf-8")))
    assert tuple(parsed[0]) == d1_export.D1_COLUMNS
    first, second = parsed
    assert first["role"] == "PRIMARY" and first["override_action"] == "" and first["outcome"] == "" and first["override_actor_hash"] == ""
    assert second["override_action"] == "APPROVE" and second["outcome"] == "COMPLETED" and len(second["override_actor_hash"]) == 32
    assert "manager-01" not in path.read_text(encoding="utf-8") and "res-guid" not in path.read_text(encoding="utf-8")


def test_request_features_extracted_from_payload() -> None:
    features = d1_export.request_features(json.dumps(PAYLOAD), CREATED)

    assert features["items_count"] == 2 and features["total_price"] == 250000 and features["currency"] == "VND"
    assert features["earliest_start_local"] == "2026-10-20T10:00:00+07:00"
    assert features["lead_time_hours"] == round((datetime(2026, 10, 20, 3, 0, tzinfo=timezone.utc) - CREATED).total_seconds() / 3600, 2)
    assert (features["customer_is_guest"], features["customer_tier"], features["customer_member_days"]) == (False, "GOLD", 30)
    assert (features["history_completed"], features["history_no_show"], features["history_cancelled"], features["history_late_cancelled"], features["history_payment_expired"]) == (5, 1, 2, 1, 0)
    assert (features["demand_occupancy_rate"], features["demand_waitlist_count"]) == (0.7, 3)


def test_optional_fields_stay_empty_when_missing() -> None:
    minimal = {"items": [{"startTime": "2026-10-20T10:00:00+07:00"}], "customer": {"isGuest": True, "tier": "STANDARD"}, "totalPrice": 1, "currency": "VND"}

    features = d1_export.request_features(json.dumps(minimal), CREATED)

    assert features["customer_member_days"] == "" and features["history_completed"] == "" and features["demand_occupancy_rate"] == "" and features["is_peak"] == ""
