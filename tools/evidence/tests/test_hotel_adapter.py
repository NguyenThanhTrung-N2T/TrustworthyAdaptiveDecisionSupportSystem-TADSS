"""Adapter Hotel Booking Demand (PT-01): CSV khách sạn -> lọc Q-08 -> lấy mẫu Q-07 -> request v2 (spec 4.6), kiểm theo schema, dời bội số 7 ngày, D5."""
import csv
import json
import uuid
from datetime import date, datetime, time, timedelta
from decimal import ROUND_HALF_UP, Decimal
from pathlib import Path
from typing import Any
from zoneinfo import ZoneInfo

import pytest

from evidence import contract_check, hotel_adapter
from evidence.anonymize import Anonymizer

SAMPLE = Path(__file__).resolve().parent / "fixtures" / "hotel_sample.csv"
LISBON = ZoneInfo("Europe/Lisbon")
NOW = datetime(2026, 10, 9, 12, 0, tzinfo=LISBON)
WEEK = timedelta(days=7)
LOW_MAX_MINUTES = 43200  # 30 ngày: trong fixture chỉ dòng 40 đêm (dòng 8) vượt ngưỡng này
# Số dòng của fixture (dòng 1 là tiêu đề, dữ liệu bắt đầu ở dòng 2), xem hotel_sample.csv
LINE_ONE_NIGHT, LINE_WEEKEND, LINE_LONG_STAY = 2, 3, 8
LINE_REPEATED_WITH_HISTORY, LINE_REPEATED_NO_HISTORY, LINE_THREE_DECIMALS, LINE_TIE_ROUNDING = 9, 10, 11, 12
LINE_ZERO_PRICE, LINE_BAD_DAY, LINE_DST_START, LINE_GUEST_WITH_PREVIOUS = 13, 20, 15, 17
INVALID_LINES = {5, 6, 7, 19, 20, 21}  # 0 đêm (5, 19, 21), adr âm (6), ngày đến sai (7, 20)


def load() -> list[hotel_adapter.HotelRow]:
    return hotel_adapter.load_rows(SAMPLE)


def build(key: bytes = b"khoa-1", run_id: str = "run-1", now: datetime = NOW) -> list[dict[str, Any]]:
    rows = hotel_adapter.select(load(), "full").rows
    return hotel_adapter.build_requests(rows, Anonymizer(key), run_id, now)


def request_for(requests: list[dict[str, Any]], line: int) -> dict[str, Any]:
    return next(entry for entry in requests if entry["meta"]["source_row"] == line)


def write_dataset(path: Path, overrides: list[dict[str, str]]) -> Path:
    """Ghi CSV từ dòng 2 của fixture (làm mẫu) rồi áp từng bộ ghi đè; dùng để dựng ca biên mà không phụ thuộc fixture chính."""
    with SAMPLE.open(encoding="ascii", newline="") as handle:
        reader = csv.DictReader(handle)
        template = next(reader)
        header = reader.fieldnames or []
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=header, lineterminator="\n")
        writer.writeheader()
        for override in overrides:
            writer.writerow({**template, **override})
    return path


def local(day: date, hour: int) -> datetime:
    return datetime.combine(day, time(hour), tzinfo=LISBON)


def test_hotel_source_constants_pinned() -> None:
    assert hotel_adapter.SOURCE_URL.startswith("https://raw.githubusercontent.com/rfordatascience/tidytuesday/d75aaa0d31596ad6487ae0db20138067d301e031/")
    assert hotel_adapter.SOURCE_SHA256 == "7c2ae42a7353905ea136e5c2287f17c92c5435826598bfbb8491c6f0c7b1fc06"
    assert hotel_adapter.EXPECTED_ROW_COUNT == 119390 and hotel_adapter.EXPECTED_COLUMN_COUNT == 32
    assert hotel_adapter.MAX_ITEM_MINUTES == 100800 and hotel_adapter.HIGH_VALUE_THRESHOLD == Decimal(750)
    assert hotel_adapter.QUICK_SAMPLE_SIZE == 10000 and isinstance(hotel_adapter.SAMPLE_SEED, int)
    assert hotel_adapter.LICENSE_LABEL and hotel_adapter.CITATION


def test_hotel_load_rows_types_and_source_row() -> None:
    rows = load()

    assert len(rows) == 30
    assert [row.source_row for row in rows] == list(range(2, 32)), "source_row là số dòng của file gốc (tiêu đề là dòng 1)"
    first = rows[0]
    assert first.hotel == "City Hotel" and first.adr == Decimal("85.50") and first.arrival == date(2016, 6, 14)
    assert first.lead_time == 10 and first.weekend_nights == 0 and first.week_nights == 1 and first.room_type == "A"
    assert rows[5].arrival is None, "tên tháng sai: không dựng được ngày đến, nhưng dòng vẫn được nạp để bị lọc và đếm"
    assert rows[LINE_BAD_DAY - 2].arrival is None, "31/4 không tồn tại"


def test_hotel_load_rows_reports_every_bad_cell_with_line(tmp_path: Path) -> None:
    path = write_dataset(tmp_path / "bad.csv", [{"lead_time": "abc"}, {}, {"adr": "xyz", "is_repeated_guest": "2"}, {"previous_cancellations": "-1"}])

    with pytest.raises(hotel_adapter.HotelDataError) as error:
        hotel_adapter.load_rows(path)

    messages = [(item.line, item.message) for item in error.value.errors]
    assert any(line == 2 and "lead_time" in text for line, text in messages)
    assert any(line == 4 and "adr" in text for line, text in messages)
    assert any(line == 4 and "is_repeated_guest" in text for line, text in messages)
    assert any(line == 5 and "previous_cancellations" in text for line, text in messages)
    assert not any(line == 3 for line, _ in messages)


def test_hotel_load_rows_requires_columns(tmp_path: Path) -> None:
    path = tmp_path / "missing.csv"
    path.write_text("hotel,adr\nCity Hotel,10\n", encoding="ascii")

    with pytest.raises(hotel_adapter.HotelDataError, match="thiếu cột"):
        hotel_adapter.load_rows(path)


def test_hotel_mapping_fixture() -> None:
    requests = build()
    rows = {row.source_row: row for row in load()}

    assert len(requests) == 24, "30 dòng trừ 6 dòng bị lọc"
    assert {entry["meta"]["source_row"] for entry in requests} == set(rows) - INVALID_LINES
    for entry in requests:
        body, meta = entry["body"], entry["meta"]
        row = rows[meta["source_row"]]
        assert contract_check.request_errors(body) == [] and meta["request_errors"] == [], meta["source_row"]
        assert entry["idempotency_key"] == f"portability:run-1:{row.source_row}"
        assert body["branchTimeZone"] == "Europe/Lisbon" and body["currency"] == "EUR" and body["channel"] == "ONLINE"
        assert "demand" not in body
        assert body["metadata"] == {
            "market_segment": row.market_segment, "distribution_channel": row.distribution_channel, "deposit_type": row.deposit_type, "customer_type": row.customer_type,
        }

        nights = row.weekend_nights + row.week_nights
        assert meta["nights"] == nights and len(body["items"]) == 1
        item = body["items"][0]
        start, end = datetime.fromisoformat(item["startTime"]), datetime.fromisoformat(item["endTime"])
        shifted = timedelta(days=meta["shift_days"])
        assert start.astimezone(LISBON).replace(tzinfo=None) == local(row.arrival + shifted, 14).replace(tzinfo=None), "vào 14:00 giờ Lisbon"
        assert end.astimezone(LISBON).replace(tzinfo=None) == local(row.arrival + shifted + timedelta(days=nights), 12).replace(tzinfo=None), "ra 12:00 giờ Lisbon"
        assert start.utcoffset() == start.astimezone(LISBON).utcoffset(), "offset khớp múi giờ Lisbon của ngày đó (giờ mùa hè/mùa đông)"
        assert end.utcoffset() == end.astimezone(LISBON).utcoffset()
        expected_price = (row.adr * nights).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
        assert Decimal(str(item["price"])) == expected_price and Decimal(str(body["totalPrice"])) == expected_price
        assert meta["total_price"] == expected_price

        requested = datetime.fromisoformat(body["requestedAt"])
        assert requested >= NOW and requested - NOW < WEEK, "δ ∈ [0; 7 ngày) (Q-04)"
        original_requested = local(row.arrival - timedelta(days=row.lead_time), 10)
        shifted_requested = requested.astimezone(LISBON)
        assert shifted_requested.hour == 10 and shifted_requested.minute == 0 and shifted_requested.weekday() == original_requested.weekday()
        assert shifted_requested.date() - original_requested.date() == shifted, "mọi mốc dời cùng k × 7 ngày"
        assert meta["shift_days"] % 7 == 0
        assert body["customer"]["memberSince"] is None

        assert uuid.UUID(body["reservationId"]).version == 4 and uuid.UUID(body["branchId"]).version == 4
        for key in ("resourceId", "resourceTypeId"):
            assert uuid.UUID(item[key]).version == 4


def test_hotel_labels_and_sensitive_fields_not_in_body() -> None:
    requests = build()
    serialized = json.dumps([entry["body"] for entry in requests], ensure_ascii=False)

    for forbidden in ("is_canceled", "reservation_status", "Check-Out", "Canceled", "country", "agent", "company", "PRT", "City Hotel", "Resort Hotel"):
        assert forbidden not in serialized, forbidden
    assert request_for(requests, LINE_ONE_NIGHT)["meta"]["is_canceled"] == 0
    assert request_for(requests, 16)["meta"]["is_canceled"] == 1, "nhãn chỉ nằm trong meta để vào D5, không vào request"


def test_hotel_mapping_ids_stable_per_run_and_distinct_across_runs() -> None:
    first, again, other_key = build(b"khoa-1"), build(b"khoa-1"), build(b"khoa-2")

    assert [e["body"] for e in first] == [e["body"] for e in again]
    assert first[0]["body"]["branchId"] != other_key[0]["body"]["branchId"]
    city = request_for(first, LINE_ONE_NIGHT)["body"]
    resort = request_for(first, LINE_WEEKEND)["body"]
    assert city["branchId"] != resort["branchId"], "hai khách sạn, hai chi nhánh"
    assert city["branchId"] == request_for(first, LINE_REPEATED_WITH_HISTORY)["body"]["branchId"], "cùng khách sạn cùng chi nhánh"
    assert city["items"][0]["resourceTypeId"] == request_for(first, LINE_REPEATED_WITH_HISTORY)["body"]["items"][0]["resourceTypeId"], "cùng loại phòng cùng resourceTypeId"
    assert city["items"][0]["resourceId"] != resort["items"][0]["resourceId"], "resourceId theo cặp (hotel, loại phòng)"
    assert len({entry["body"]["reservationId"] for entry in first}) == len(first)


def test_hotel_price_rounding_and_zero() -> None:
    requests = build()

    assert request_for(requests, LINE_THREE_DECIMALS)["body"]["totalPrice"] == 301.0, "100,333 × 3 = 300,999 -> 301,00"
    assert request_for(requests, LINE_TIE_ROUNDING)["body"]["totalPrice"] == 33.34, "33,335 hòa làm tròn lên (nửa lên, không phải về chẵn)"
    zero = request_for(requests, LINE_ZERO_PRICE)
    assert zero["body"]["totalPrice"] == 0 and zero["body"]["items"][0]["price"] == 0 and zero["meta"]["request_errors"] == []
    assert request_for(requests, LINE_ONE_NIGHT)["body"]["totalPrice"] == 85.5


def test_hotel_dst_stay_duration_in_instants() -> None:
    # Ngày "gửi" trước mọi mốc nên không dời (k = 0); lưu trú 26/3/2016 + 3 đêm đi qua ngày bắt đầu giờ mùa hè 27/3/2016 (mất 1 giờ).
    entry = request_for(build(now=datetime(2016, 1, 1, tzinfo=LISBON)), LINE_DST_START)
    item = entry["body"]["items"][0]
    minutes = (datetime.fromisoformat(item["endTime"]) - datetime.fromisoformat(item["startTime"])) / timedelta(minutes=1)

    assert entry["meta"]["shift_days"] == 0 and entry["meta"]["nights"] == 3
    assert minutes == 3 * 1440 - 120 - 60, "tính theo thời điểm tuyệt đối, không theo giờ treo tường"
    assert entry["meta"]["duration_min"] == 3 * 1440 - 120 - 60
    assert item["startTime"].endswith("+00:00") and item["endTime"].endswith("+01:00")


@pytest.mark.parametrize(
    ("overrides", "reason"),
    [
        ({"stays_in_weekend_nights": "0", "stays_in_week_nights": "0"}, "zero_nights"),
        ({"adr": "-6.38"}, "negative_adr"),
        ({"arrival_date_month": "Smarch"}, "invalid_arrival_date"),
        ({"arrival_date_day_of_month": "31", "arrival_date_month": "April"}, "invalid_arrival_date"),
        ({"arrival_date_year": "20x6"}, "invalid_arrival_date"),
        ({"stays_in_weekend_nights": "0", "stays_in_week_nights": "0", "adr": "-1"}, "zero_nights"),
        ({"stays_in_weekend_nights": "20", "stays_in_week_nights": "21"}, "stay_too_long"),
        ({"stays_in_weekend_nights": "0", "stays_in_week_nights": "1", "adr": "0"}, None),
    ],
)
def test_hotel_filters_Theory(tmp_path: Path, overrides: dict[str, str], reason: str | None) -> None:
    base = {"arrival_date_year": "2016", "arrival_date_month": "June", "arrival_date_day_of_month": "14", "stays_in_weekend_nights": "1", "stays_in_week_nights": "1", "adr": "50.00"}
    rows = hotel_adapter.load_rows(write_dataset(tmp_path / "one.csv", [{**base, **overrides}]))

    assert hotel_adapter.reject_reason(rows[0], LOW_MAX_MINUTES) == reason
    selection = hotel_adapter.select(rows, "full", max_item_minutes=LOW_MAX_MINUTES)
    assert selection.rejected == {**{name: 0 for name in hotel_adapter.REJECT_REASONS}, **({reason: 1} if reason else {})}
    assert len(selection.rows) == (0 if reason else 1) and selection.valid_count == len(selection.rows)


def test_hotel_stay_limit_is_inclusive_on_minutes(tmp_path: Path) -> None:
    rows = hotel_adapter.load_rows(write_dataset(tmp_path / "limit.csv", [{"stays_in_weekend_nights": "0", "stays_in_week_nights": "2"}]))

    assert hotel_adapter.reject_reason(rows[0], 2 * 1440 - 120) is None, "đúng bằng ngưỡng vẫn hợp lệ (runtime nhận 30..Max phút)"
    assert hotel_adapter.reject_reason(rows[0], 2 * 1440 - 121) == "stay_too_long"


def test_hotel_filter_counts_for_fixture() -> None:
    selection = hotel_adapter.select(load(), "full", max_item_minutes=LOW_MAX_MINUTES)

    assert selection.rejected == {"zero_nights": 3, "negative_adr": 1, "invalid_arrival_date": 2, "stay_too_long": 1}, "dòng vừa 0 đêm vừa adr âm chỉ đếm một lần (0 đêm trước)"
    assert selection.total_rows == 30 and selection.valid_count == 23 and len(selection.rows) == 23
    assert LINE_LONG_STAY not in {row.source_row for row in selection.rows}
    default = hotel_adapter.select(load(), "full")
    assert default.rejected["stay_too_long"] == 0 and default.valid_count == 24, "ngưỡng mặc định 100800 phủ cả 40 đêm"


def test_hotel_sample_deterministic() -> None:
    rows = load()
    first = hotel_adapter.select(rows, "quick", seed=7, sample_size=10)
    again = hotel_adapter.select(rows, "quick", seed=7, sample_size=10)
    other = hotel_adapter.select(rows, "quick", seed=8, sample_size=10)
    shuffled = hotel_adapter.select(list(reversed(rows)), "quick", seed=7, sample_size=10)
    full = hotel_adapter.select(rows, "full", seed=7, sample_size=10)

    ids = [row.source_row for row in first.rows]
    assert ids == [row.source_row for row in again.rows] == [row.source_row for row in shuffled.rows], "cùng seed cùng tập, không phụ thuộc thứ tự đầu vào"
    assert ids != [row.source_row for row in other.rows]
    assert len(ids) == 10 == len(set(ids)) and ids == sorted(ids) and set(ids) <= {row.source_row for row in full.rows}
    assert first.valid_count == 24 and first.rejected == full.rejected, "bảng đếm lọc tính trên toàn bộ, không phụ thuộc cỡ mẫu"
    assert (first.mode, first.seed, first.sample_size) == ("quick", 7, 10) and full.mode == "full"

    assert len(full.rows) == 24, "chế độ full lấy hết dòng hợp lệ"
    assert len(hotel_adapter.select(rows, "quick", sample_size=1000).rows) == 24, "mẫu lớn hơn tập hợp lệ thì lấy hết"


def test_hotel_sample_default_size_and_mode_validation() -> None:
    default = hotel_adapter.select(load(), "quick")

    assert default.sample_size == hotel_adapter.QUICK_SAMPLE_SIZE and default.seed == hotel_adapter.SAMPLE_SEED
    with pytest.raises(ValueError, match="mode"):
        hotel_adapter.select(load(), "slow")


def test_hotel_repeated_guest_history() -> None:
    requests = build()
    with_history = request_for(requests, LINE_REPEATED_WITH_HISTORY)["body"]["customer"]
    without_history = request_for(requests, LINE_REPEATED_NO_HISTORY)["body"]["customer"]
    guest = request_for(requests, LINE_ONE_NIGHT)["body"]["customer"]
    guest_with_previous = request_for(requests, LINE_GUEST_WITH_PREVIOUS)["body"]["customer"]

    assert with_history["isGuest"] is False and with_history["tier"] == "STANDARD" and with_history["memberSince"] is None
    assert uuid.UUID(with_history["customerId"]).version == 4
    assert with_history["history"] == {"completedCount": 5, "noShowCount": 0, "cancelledCount": 2, "lateCancelledCount": 0, "paymentExpiredCount": 0}
    assert without_history["isGuest"] is False and without_history["history"]["completedCount"] == 0 and without_history["history"]["cancelledCount"] == 0
    assert with_history["customerId"] != without_history["customerId"], "dataset không có định danh khách: mỗi dòng một customerId"
    for guest_customer in (guest, guest_with_previous):
        assert guest_customer["isGuest"] is True and guest_customer["customerId"] is None and guest_customer["tier"] == "STANDARD" and guest_customer["memberSince"] is None
    assert guest["history"] == {"completedCount": 0, "noShowCount": 0, "cancelledCount": 0, "lateCancelledCount": 0, "paymentExpiredCount": 0}
    assert guest_with_previous["history"] == {"completedCount": 0, "noShowCount": 0, "cancelledCount": 1, "lateCancelledCount": 0, "paymentExpiredCount": 0}, (
        "history lấy từ previous_* cho mọi dòng (spec 4.6), kể cả khách is_repeated_guest = 0"
    )


def test_hotel_history_from_previous_counts_for_non_repeated_guest(tmp_path: Path) -> None:
    rows = hotel_adapter.load_rows(write_dataset(tmp_path / "prev.csv", [{"is_repeated_guest": "0", "previous_cancellations": "3", "previous_bookings_not_canceled": "4"}]))

    customer = hotel_adapter.build_requests(rows, Anonymizer(b"k"), "run-1", NOW)[0]["body"]["customer"]

    assert customer["isGuest"] is True and customer["customerId"] is None
    assert customer["history"] == {"completedCount": 4, "noShowCount": 0, "cancelledCount": 3, "lateCancelledCount": 0, "paymentExpiredCount": 0}


@pytest.mark.parametrize("value", ["1_0", "+3", " 1 2", "٣", "1.0", "-1", "1e2", "²"])
def test_hotel_load_rows_rejects_non_ascii_digit_integers(tmp_path: Path, value: str) -> None:
    path = write_dataset(tmp_path / "digits.csv", [{"lead_time": value}])

    with pytest.raises(hotel_adapter.HotelDataError, match="lead_time"):
        hotel_adapter.load_rows(path)


def test_hotel_meta_for_d5() -> None:
    meta = request_for(build(), LINE_REPEATED_WITH_HISTORY)["meta"]

    assert meta["hotel"] == "City Hotel" and meta["room_type"] == "A" and meta["nights"] == 2
    assert meta["is_repeated_guest"] == 1 and meta["previous_cancellations"] == 2 and meta["previous_bookings_not_canceled"] == 5 and meta["is_canceled"] == 0
    assert meta["arrival_local"] == "2017-03-11T14:00:00"
    assert meta["lead_time_minutes"] == 45 * 1440 + 4 * 60, "lead time GỐC: từ 10:00 ngày đặt danh nghĩa đến 14:00 ngày đến (Q-04)"
    assert meta["duration_min"] == 2 * 1440 - 120
    assert meta["shift_days"] > 0 and meta["shift_days"] % 7 == 0


def full_result(entry: dict[str, Any], rate: Decimal = Decimal("0.5")) -> dict[str, Any]:
    deposit = {"rate": rate, "amount": contract_check.expected_amount(entry["body"]["totalPrice"], rate, "EUR"), "currency": "EUR"}
    return {
        "status": 200, "decision_id": "0f9c7e5a-0000-4000-8000-00000000d001",
        "body": {
            "decisionId": "0f9c7e5a-0000-4000-8000-00000000d001", "reservationId": entry["body"]["reservationId"], "status": "APPROVED", "deposit": deposit,
            "strategyName": "DefaultDecisionStrategy", "strategyVersion": "2.0.0", "executionType": "AUTOMATED", "fallbackUsed": False,
            "reasonCode": "TIER_DEPOSIT", "reasons": [], "recommendations": [], "approvalExpiresAt": None, "evaluatedAt": "2026-10-09T05:00:00.000Z",
        },
    }


def test_hotel_d5_rows() -> None:
    prepared = build()[:3]
    results: list[dict[str, Any] | None] = [full_result(prepared[0]), None, {"status": 503, "body": None}]

    rows = hotel_adapter.d5_rows(prepared, results, {"0f9c7e5a-0000-4000-8000-00000000d001": {"strategy_status": "ACTIVE"}})

    assert [list(row) for row in rows] == [list(hotel_adapter.D5_COLUMNS)] * 3
    first = rows[0]
    assert first["source_row"] == prepared[0]["meta"]["source_row"] == 2 and first["hotel"] == "City Hotel" and first["nights"] == 1
    assert first["arrival_local"] == "2016-06-14T14:00:00" and first["room_type"] == "A" and first["total_price"] == "85.5"
    assert first["is_repeated_guest"] == 0 and first["is_canceled"] == 0
    assert first["status"] == "APPROVED" and first["deposit_rate"] == "0.5" and first["deposit_amount"] == "42.75" and first["reason_code"] == "TIER_DEPOSIT"
    assert first["decision_id"] == "0f9c7e5a-0000-4000-8000-00000000d001" and first["strategy_status"] == "ACTIVE" and first["contract_errors"] == ""
    assert first["shift_days"] == prepared[0]["meta"]["shift_days"]
    assert rows[1]["status"] == "" and rows[1]["contract_errors"].startswith("NOT_SENT")
    assert rows[2]["status"] == "" and "STATUS: HTTP 503" in rows[2]["contract_errors"]
    with pytest.raises(ValueError, match="cùng số phần tử"):
        hotel_adapter.d5_rows(prepared, results[:2])


def test_hotel_d5_request_error_is_not_reported_as_not_sent() -> None:
    prepared = build()[:1]
    prepared[0]["meta"]["request_errors"] = ["SCHEMA: giả lập"]

    row = hotel_adapter.d5_rows(prepared, [None])[0]

    assert row["contract_errors"] == "SCHEMA: giả lập"


def test_hotel_response_contract_check_uses_eur_two_decimals() -> None:
    entry = request_for(build(), LINE_TIE_ROUNDING)
    result = full_result(entry, Decimal("0.5"))

    assert contract_check.response_errors(entry["body"], 200, result["body"]) == []
    assert result["body"]["deposit"]["amount"] == Decimal("16.67"), "33,34 × 0,5 = 16,67"


def test_hotel_build_requests_skips_filtered_rows() -> None:
    requests = hotel_adapter.build_requests(load(), Anonymizer(b"k"), "run-1", NOW)

    assert len(requests) == 24, "build_requests bỏ qua dòng reject_reason khác None, giống replay"


def test_load_rows_reads_reservation_status_and_date() -> None:
    rows = load()

    assert {row.reservation_status for row in rows} <= set(hotel_adapter.RESERVATION_STATUSES)
    assert rows[0].reservation_status == "Check-Out" and rows[0].reservation_status_date.isoformat() == "2017-01-01"


@pytest.mark.parametrize(("column", "value"), [("reservation_status", "Lost"), ("reservation_status_date", "2017-13-01")])
def test_load_rows_rejects_invalid_reservation_status_cells(tmp_path: Path, column: str, value: str) -> None:
    path = write_dataset(tmp_path / "bad.csv", [{column: value}])

    with pytest.raises(hotel_adapter.HotelDataError, match=column):
        hotel_adapter.load_rows(path)


def test_meta_and_d5_carry_reservation_status_and_empty_shadow_columns() -> None:
    rows = load()[:1]
    prepared = hotel_adapter.build_requests(rows, Anonymizer(b"k" * 32), "run", NOW)
    meta = prepared[0]["meta"]

    assert meta["reservation_status"] == "Check-Out" and meta["reservation_status_date"] == "2017-01-01"
    d5 = hotel_adapter.d5_rows_from_outcomes([{"idempotency_key": prepared[0]["idempotency_key"], "meta": meta}], {}, {})
    assert d5[0]["reservation_status"] == "Check-Out" and d5[0]["reservation_status_date"] == "2017-01-01"
    assert all(d5[0][column] == "" for column in ("shadow_pair_state", "primary_fallback", "shadow_status", "shadow_deposit_rate", "shadow_deposit_amount"))
    assert list(d5[0]) == list(hotel_adapter.D5_COLUMNS)


def test_d5_row_tolerates_prepared_meta_without_reservation_status() -> None:
    prepared = hotel_adapter.build_requests(load()[:1], Anonymizer(b"k" * 32), "run", NOW)
    meta = {key: value for key, value in prepared[0]["meta"].items() if key not in hotel_adapter.STATUS_COLUMNS}

    d5 = hotel_adapter.d5_rows_from_outcomes([{"idempotency_key": prepared[0]["idempotency_key"], "meta": meta}], {}, {})

    assert d5[0]["reservation_status"] == "" and d5[0]["reservation_status_date"] == ""
