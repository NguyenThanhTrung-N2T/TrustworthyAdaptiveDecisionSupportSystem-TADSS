"""Replay SmashCourt (RP-01): CSV xuất -> request v2 (INTEGRATION_SPEC §4.2), kiểm theo schema, dời bội số 7 ngày, D4 dạng dài."""
import csv
import json
import re
import uuid
from datetime import datetime, timedelta
from decimal import Decimal
from pathlib import Path
from typing import Any
from zoneinfo import ZoneInfo

import pytest

from evidence import contract_check, replay
from evidence.anonymize import Anonymizer

SAMPLE = Path(__file__).resolve().parent / "fixtures" / "smashcourt_bookings_sample.csv"
VN = ZoneInfo("Asia/Ho_Chi_Minh")
NOW = datetime(2026, 10, 9, 12, 0, tzinfo=VN)
WEEK = timedelta(days=7)
UUID_PATTERN = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")

BOOKING_1 = "11111111-1111-4111-8111-111111111111"
BOOKING_2 = "22222222-2222-4222-8222-222222222222"
BOOKING_5 = "55555555-5555-4555-8555-555555555555"
CUSTOMER_1 = "cccccccc-0000-4000-8000-0000000000c1"


def build(key: bytes = b"khoa-1", run_id: str = "run-1") -> list[dict[str, Any]]:
    return replay.build_requests(replay.load_bookings(SAMPLE), Anonymizer(key), run_id, NOW)


def by_status(requests: list[dict[str, Any]], status: str) -> dict[str, Any]:
    return next(entry for entry in requests if entry["meta"]["dev_final_status"] == status)


def write_export(path: Path, rows: list[dict[str, str]]) -> Path:
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=replay.EXPORT_COLUMNS)
        writer.writeheader()
        writer.writerows(rows)
    return path


def good_row(**overrides: str) -> dict[str, str]:
    item = {"court_id": "aaaaaaaa-0000-4000-8000-0000000000a1", "court_type_id": "dddddddd-0000-4000-8000-0000000000d1", "date": "2026-03-04", "start_time": "18:00:00", "end_time": "19:00:00", "price": 90000.00}
    row = {
        "booking_id": "66666666-6666-4666-8666-666666666666", "branch_id": "bbbbbbbb-0000-4000-8000-000000000001", "customer_id": "", "source": "ONLINE",
        "status": "COMPLETED", "created_at": "2026-03-01T03:30:00.000000Z", "items": json.dumps([item]), "final_total": "90000.00", "tier_rank": "", "member_since": "",
        "history_completed": "0", "history_no_show": "0", "history_cancelled": "0", "history_late_cancelled": "0", "history_payment_expired": "0",
    }
    row.update(overrides)
    return row


def test_replay_maps_fixture() -> None:
    requests = build()

    assert len(requests) == 3, "5 dòng, trừ 1 không hóa đơn và 1 không có sân hoạt động"
    for entry in requests:
        assert contract_check.request_errors(entry["body"]) == [], entry["meta"]["booking_hash"]
        assert entry["meta"]["request_errors"] == []
        assert entry["body"]["branchTimeZone"] == "Asia/Ho_Chi_Minh" and entry["body"]["currency"] == "VND"
        assert entry["body"]["metadata"] == {"source": "replay"}
        assert "demand" not in entry["body"], "demand bỏ trống (Q-12)"
        assert entry["idempotency_key"] == f"replay:run-1:{entry['meta']['booking_hash']}"

    member = by_status(requests, "COMPLETED")["body"]
    assert member["channel"] == "ONLINE" and member["totalPrice"] == 228000
    assert len(member["items"]) == 2 and member["items"][0]["price"] == 120000
    assert member["customer"]["isGuest"] is False and member["customer"]["tier"] == "DIAMOND", "tier rank 7 nằm ngoài 5 hạng nên gộp vào DIAMOND"
    assert uuid.UUID(member["customer"]["customerId"]).version == 4
    for entry in requests:
        body = entry["body"]
        wire_ids = [body["reservationId"], body["branchId"], *(item[key] for item in body["items"] for key in ("resourceId", "resourceTypeId"))]
        if body["customer"]["customerId"]:
            wire_ids.append(body["customer"]["customerId"])
        for wire_id in wire_ids:
            parsed = uuid.UUID(wire_id)
            assert parsed.version == 4 and parsed.variant == uuid.RFC_4122, wire_id
    assert member["customer"]["history"] == {"completedCount": 12, "noShowCount": 1, "cancelledCount": 2, "lateCancelledCount": 1, "paymentExpiredCount": 0}
    assert member["customer"]["memberSince"].endswith("+07:00")

    guest = by_status(requests, "NO_SHOW")["body"]
    assert guest["channel"] == "WALK_IN"
    assert guest["customer"] == {
        "customerId": None, "isGuest": True, "tier": "STANDARD", "memberSince": None,
        "history": {"completedCount": 0, "noShowCount": 0, "cancelledCount": 0, "lateCancelledCount": 0, "paymentExpiredCount": 0},
    }


def test_replay_uuids_are_stable_within_run_and_differ_between_keys() -> None:
    first = build(b"khoa-1")
    again = build(b"khoa-1")
    other = build(b"khoa-2")

    assert first == again, "cùng khóa cho cùng kết quả"
    member = by_status(first, "COMPLETED")["body"]
    cancelled = by_status(first, "CANCELLED_REFUNDED")["body"]
    assert member["customer"]["customerId"] == cancelled["customer"]["customerId"], "cùng khách trong một lượt có cùng UUID"
    assert member["reservationId"] != cancelled["reservationId"]
    assert member["branchId"] == cancelled["branchId"]
    assert member["items"][0]["resourceTypeId"] == member["items"][1]["resourceTypeId"]
    assert member["items"][0]["resourceId"] != member["items"][1]["resourceId"]
    assert member["customer"]["customerId"] != member["branchId"], "khác loại định danh thì khác UUID"
    other_member = by_status(other, "COMPLETED")["body"]
    assert other_member["customer"]["customerId"] != member["customer"]["customerId"]
    assert other_member["reservationId"] != member["reservationId"]
    assert by_status(first, "COMPLETED")["idempotency_key"] != by_status(other, "COMPLETED")["idempotency_key"]


def test_replay_body_has_no_original_identifier() -> None:
    raw_text = SAMPLE.read_text(encoding="utf-8")
    originals = set(UUID_PATTERN.findall(raw_text))
    assert len(originals) >= 8

    for entry in build():
        serialized = json.dumps(entry["body"]) + json.dumps(entry["meta"], default=str) + entry["idempotency_key"]
        assert not (originals & set(UUID_PATTERN.findall(serialized.lower()))), "không UUID gốc nào được lọt vào body, meta hay khóa"


def test_replay_shifts_all_moments_by_the_same_whole_weeks() -> None:
    entry = by_status(build(), "COMPLETED")
    body, meta = entry["body"], entry["meta"]
    original_requested = datetime(2026, 3, 1, 10, 30, tzinfo=VN)
    original_start = datetime(2026, 3, 4, 18, 0, tzinfo=VN)
    original_member = datetime(2025, 3, 14, 8, 0, tzinfo=VN)

    requested = datetime.fromisoformat(body["requestedAt"])
    shift = timedelta(days=meta["shift_days"])
    assert meta["shift_days"] % 7 == 0 and meta["shift_days"] > 0
    assert requested == original_requested + shift
    assert timedelta(0) <= requested - NOW < WEEK
    assert datetime.fromisoformat(body["items"][0]["startTime"]) == original_start + shift
    assert datetime.fromisoformat(body["items"][0]["endTime"]) == original_start + timedelta(hours=2) + shift
    assert datetime.fromisoformat(body["customer"]["memberSince"]) == original_member + shift
    assert datetime.fromisoformat(body["items"][0]["startTime"]).weekday() == original_start.weekday()
    assert meta["lead_time_minutes"] == 4770, "lead time gốc (04/03 18:00 trừ 01/03 10:30 là 3 ngày 7,5 giờ) được giữ nguyên"


def test_replay_window_bias_flag() -> None:
    requests = build()

    short = by_status(requests, "NO_SHOW")["meta"]
    long = by_status(requests, "COMPLETED")["meta"]

    assert short["lead_time_minutes"] == 90 and short["window_bias"] is True
    assert long["lead_time_minutes"] == 4770 and long["window_bias"] is False


@pytest.mark.parametrize(
    "start_time,lead_minutes,bias",
    [("12:29:00", 119, True), ("12:30:00", 120, True), ("12:31:00", 121, False), ("10:30:00", 0, True), ("10:00:00", -30, True)],
)
def test_replay_window_bias_boundaries(tmp_path: Path, start_time: str, lead_minutes: int, bias: bool) -> None:
    """requestedAt gốc 10:30 giờ chi nhánh; lead time = giờ bắt đầu trừ 10:30. window_bias bật khi lead ≤ 120 phút, kể cả lead âm (nhập sau giờ chơi)."""
    item = {"court_id": "aaaaaaaa-0000-4000-8000-0000000000a1", "court_type_id": "dddddddd-0000-4000-8000-0000000000d1", "date": "2026-03-01", "start_time": start_time, "end_time": "23:00:00", "price": 90000.0}
    path = write_export(tmp_path / "lead.csv", [good_row(items=json.dumps([item]))])

    (entry,) = replay.build_requests(replay.load_bookings(path), Anonymizer(b"k"), "run-1", NOW)

    assert entry["meta"]["lead_time_minutes"] == lead_minutes and entry["meta"]["window_bias"] is bias
    assert entry["meta"]["request_errors"] == [], "lead time âm vẫn là request hợp lệ theo schema"


@pytest.mark.parametrize("rank,expected", [(None, "STANDARD"), (1, "STANDARD"), (2, "SILVER"), (3, "GOLD"), (4, "PLATINUM"), (5, "DIAMOND"), (7, "DIAMOND")])
def test_tier_for_rank_Theory(rank: int | None, expected: str) -> None:
    assert replay.tier_for_rank(rank) == expected


def test_replay_meta_carries_d4_fields() -> None:
    meta = by_status(build(), "COMPLETED")["meta"]

    assert meta["channel"] == "ONLINE" and meta["dev_final_status"] == "COMPLETED"
    assert meta["court_count"] == 2 and meta["total_price"] == Decimal("228000.00")
    assert meta["booking_date"] == "2026-03-04" and meta["start_local"] == "2026-03-04T18:00:00" and meta["duration_min"] == 120
    assert meta["customer_tier"] == "DIAMOND" and meta["customer_is_guest"] is False
    assert re.fullmatch(r"[0-9a-f]{32}", meta["booking_hash"]) and re.fullmatch(r"[0-9a-f]{32}", meta["branch_hash"])


def test_replay_rejects_incomplete_rows(tmp_path: Path) -> None:
    bookings = replay.load_bookings(SAMPLE)

    assert replay.count_rejections(bookings) == {"no_invoice": 1, "no_active_courts": 1}
    assert replay.reject_reason(bookings[2]) == "no_invoice" and replay.reject_reason(bookings[3]) == "no_active_courts"
    assert replay.reject_reason(bookings[0]) is None

    bad = write_export(tmp_path / "bad.csv", [good_row(), good_row(source="ONLINE_ONLY"), good_row(created_at="hôm qua"), good_row(booking_id="khong-phai-uuid"), good_row()])
    with pytest.raises(replay.ReplayDataError) as caught:
        replay.load_bookings(bad)
    assert [error.line for error in caught.value.errors] == [3, 4, 5], "số dòng tính cả dòng tiêu đề (dòng 2 là bản ghi đầu tiên)"
    assert "dòng 3" in str(caught.value) and "source" in str(caught.value)


@pytest.mark.parametrize(
    "overrides",
    [
        {"items": "khong-phai-json"},
        {"items": json.dumps([{"court_id": "x"}])},
        {"final_total": "mười nghìn"},
        {"tier_rank": "0"},
        {"history_no_show": "-1"},
        {"member_since": "2025-13-45"},
    ],
)
def test_load_bookings_rejects_malformed_cells(tmp_path: Path, overrides: dict[str, str]) -> None:
    path = write_export(tmp_path / "bad.csv", [good_row(**overrides)])

    with pytest.raises(replay.ReplayDataError) as caught:
        replay.load_bookings(path)

    assert caught.value.errors[0].line == 2


def test_load_bookings_rejects_missing_columns(tmp_path: Path) -> None:
    path = tmp_path / "short.csv"
    path.write_text("booking_id,branch_id\n1,2\n", encoding="utf-8")

    with pytest.raises(replay.ReplayDataError, match="thiếu cột"):
        replay.load_bookings(path)


def test_replay_request_failing_schema_is_kept_and_flagged(tmp_path: Path) -> None:
    items = [{"court_id": "aaaaaaaa-0000-4000-8000-0000000000a1", "court_type_id": "dddddddd-0000-4000-8000-0000000000d1", "date": "2026-03-04", "start_time": "18:00:00", "end_time": "19:00:00", "price": 90000.00}] * 21
    path = write_export(tmp_path / "many.csv", [good_row(items=json.dumps(items))])

    requests = replay.build_requests(replay.load_bookings(path), Anonymizer(b"k"), "run-1", NOW)

    assert len(requests) == 1, "bản ghi lỗi schema không bị loại âm thầm"
    assert requests[0]["meta"]["request_errors"], "phải ghi lỗi contract của request để tính vào phép đếm"


def test_replay_d4_rows_long_format() -> None:
    requests = build()
    results = [
        {
            "status": 200, "attempts": 1, "decision_id": "0f9c7e5a-0000-4000-8000-00000000d001", "correlation_id": "corr-1",
            "body": {
                "decisionId": "0f9c7e5a-0000-4000-8000-00000000d001", "reservationId": entry["body"]["reservationId"], "status": "APPROVED",
                "deposit": {"rate": Decimal("0.5"), "amount": (Decimal(str(entry["body"]["totalPrice"])) * Decimal("0.5")).quantize(Decimal(1)), "currency": "VND"},
                "strategyName": "DefaultDecisionStrategy", "strategyVersion": "2.0.0", "executionType": "AUTOMATED", "fallbackUsed": False,
                "reasonCode": "PEAK_HOUR_DEPOSIT", "reasons": [], "recommendations": [], "approvalExpiresAt": None, "evaluatedAt": "2026-10-09T05:00:00.000Z",
            },
        }
        for entry in requests
    ]
    results[1] = {"status": 503, "attempts": 3, "decision_id": "", "correlation_id": "corr-2", "body": None}
    d1_index = {"0f9c7e5a-0000-4000-8000-00000000d001": {"strategy_status": "ACTIVE"}}

    rows = replay.d4_rows(requests, results, d1_index)

    assert len(rows) == 6 and [row["policy"] for row in rows] == ["STATIC_100", "TADSS_V2"] * 3
    assert set(rows[0]) == set(replay.D4_COLUMNS)
    static, tadss = rows[0], rows[1]
    assert (static["status"], str(static["deposit_rate"]), static["deposit_amount"], static["reason_code"]) == ("APPROVED", "1", "228000", "STATIC_100")
    assert static["decision_id"] == "" and static["contract_errors"] == ""
    assert (tadss["status"], tadss["deposit_rate"], tadss["deposit_amount"], tadss["reason_code"]) == ("APPROVED", "0.5", "114000", "PEAK_HOUR_DEPOSIT")
    assert tadss["decision_id"] == "0f9c7e5a-0000-4000-8000-00000000d001" and tadss["strategy_status"] == "ACTIVE" and tadss["contract_errors"] == ""
    assert tadss["booking_hash"] == static["booking_hash"] and tadss["channel"] == "ONLINE" and tadss["shift_days"] % 7 == 0
    assert tadss["window_bias"] is False and tadss["lead_time_minutes"] == 4770 and tadss["dev_final_status"] == "COMPLETED"
    failed = rows[3]
    assert failed["status"] == "" and failed["deposit_amount"] == "" and "STATUS" in failed["contract_errors"]
    assert rows[2]["booking_hash"] == failed["booking_hash"]
    for row in rows:
        assert not UUID_PATTERN.search(json.dumps({k: v for k, v in row.items() if k != "decision_id"}, default=str)), "D4 chỉ chứa mã băm, không UUID gốc"


def test_replay_reads_the_csv_produced_by_the_export_sql() -> None:
    """File kỳ vọng của verify-export.sh là đầu ra thật của truy vấn xuất, nên đọc được bằng chính bộ đọc của replay."""
    expected = Path(__file__).resolve().parent / "fixtures" / "smashcourt_expected_export.csv"

    bookings = replay.load_bookings(expected)
    requests = replay.build_requests(bookings, Anonymizer(b"khoa-1"), "run-1", NOW)

    assert len(bookings) == 21
    assert replay.count_rejections(bookings) == {"no_invoice": 1}, "chỉ còn booking không có hóa đơn; booking hủy giữ đủ sân nên không còn bị loại no_active_courts"
    cancelled = [booking for booking in bookings if booking.status.startswith("CANCELLED")]
    assert len(cancelled) == 6 and all(booking.items and replay.reject_reason(booking) is None for booking in cancelled), "booking hủy (sân đã tắt is_active) vẫn gửi được"
    assert len(next(booking for booking in cancelled if booking.status == "CANCELLED_REFUNDED").items) == 2, "xuất đủ mọi sân của booking hủy"
    assert len(requests) == 20 and all(entry["meta"]["request_errors"] == [] for entry in requests)
    assert {entry["meta"]["dev_final_status"] for entry in requests} >= {"CANCELLED", "CANCELLED_PENDING_REFUND", "CANCELLED_REFUNDED"}
    target = next(entry for entry in requests if entry["meta"]["dev_final_status"] == "PAID_ONLINE" and entry["meta"]["court_count"] == 2)
    assert target["body"]["totalPrice"] == 228000 and target["body"]["customer"]["history"]["lateCancelledCount"] == 2
    assert target["body"]["customer"]["tier"] == "GOLD", "hạng thứ 3 theo min_points tăng dần ánh xạ GOLD dù tên hạng do Owner đặt"


def _ok_result(entry: dict[str, Any], deposit: Any) -> dict[str, Any]:
    return {
        "status": 200, "decision_id": "0f9c7e5a-0000-4000-8000-00000000d001",
        "body": {
            "decisionId": "0f9c7e5a-0000-4000-8000-00000000d001", "reservationId": entry["body"]["reservationId"], "status": "APPROVED", "deposit": deposit,
            "strategyName": "DefaultDecisionStrategy", "strategyVersion": "2.0.0", "executionType": "AUTOMATED", "fallbackUsed": False,
            "reasonCode": "PEAK_HOUR_DEPOSIT", "reasons": [], "recommendations": [], "approvalExpiresAt": None, "evaluatedAt": "2026-10-09T05:00:00.000Z",
        },
    }


@pytest.mark.parametrize(
    "deposit",
    [{"rate": None, "amount": Decimal("114000"), "currency": "VND"}, {"rate": "abc", "amount": "xyz", "currency": "VND"}, {"rate": Decimal("0.5"), "amount": None, "currency": "VND"}],
)
def test_replay_d4_rows_survive_non_numeric_deposit(deposit: dict[str, Any]) -> None:
    requests = build()[:1]

    rows = replay.d4_rows(requests, [_ok_result(requests[0], deposit)], {})

    tadss = rows[1]
    assert tadss["contract_errors"], "response sai kiểu phải được ghi vào contract_errors, không làm d4_rows sập"
    for column in ("deposit_rate", "deposit_amount"):
        assert tadss[column] == "" or Decimal(tadss[column]) >= 0


def test_replay_d4_rows_not_sent_convention() -> None:
    """Chưa có kết quả gửi (result None hoặc status None): request lỗi schema thì chỉ ghi lỗi request (vốn không được gửi); request hợp lệ mà thiếu kết quả thì ghi NOT_SENT."""
    valid = build()[0]
    broken = build()[1]
    broken["meta"]["request_errors"] = ["SCHEMA: items: [] should be non-empty"]

    rows = replay.d4_rows([valid, broken, valid], [None, {"status": None, "body": None}, {}], {})

    assert rows[1]["contract_errors"].startswith("NOT_SENT") and "HTTP" not in rows[1]["contract_errors"]
    assert rows[3]["contract_errors"] == "SCHEMA: items: [] should be non-empty", "request vốn không được gửi thì không thêm NOT_SENT hay HTTP 0"
    assert rows[5]["contract_errors"].startswith("NOT_SENT")
    assert all(row["status"] == "" and row["deposit_amount"] == "" for row in rows[1::2])


def test_replay_d4_rows_requires_aligned_results() -> None:
    with pytest.raises(ValueError):
        replay.d4_rows(build(), [], {})
