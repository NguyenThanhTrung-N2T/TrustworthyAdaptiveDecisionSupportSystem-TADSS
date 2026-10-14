"""Adapter Hotel Booking Demand cho kiểm thử tính di động (PT-01, gói 5.6): CSV khách sạn -> request v2 -> bộ dữ liệu D5.

Luồng: `load_rows` (kiểm kiểu từng ô, lỗi kèm số dòng) -> `select` (lọc theo quy tắc Q-08 có đếm theo lý do, rồi lấy mẫu tất định theo Q-07) ->
`build_requests` (ẩn danh bằng `Anonymizer` của lượt chạy, dời bội số 7 ngày theo Q-04, kiểm request theo schema) -> bước gửi -> `d5_rows`.

Nguồn dữ liệu và tham số miền (chốt ở Task 1, spike S-1/S-2) là các hằng ở đầu file, một chỗ duy nhất. `MAX_ITEM_MINUTES` và `HIGH_VALUE_THRESHOLD`
là cấu hình vận hành của instance `eval-tadss-portability` (biến môi trường), không phải mã runtime; adapter chỉ dùng `MAX_ITEM_MINUTES` để lọc.

Ánh xạ (spec 4.6 + Q-08): một item duy nhất từ 14:00 ngày đến tới 12:00 ngày đi (giờ `Europe/Lisbon`); `price = adr × số đêm` làm tròn 2 chữ số
(nửa lên, khớp cách runtime làm tròn tiền cọc); `requestedAt` = ngày đến trừ `lead_time` ngày lúc 10:00 (giờ danh nghĩa vì dataset không có giờ đặt);
`isGuest = (is_repeated_guest = 0)`. Dataset không có định danh khách, nên khách quay lại có `customerId` dựng từ số dòng nguồn. `history` lấy từ `previous_*` cho MỌI dòng
(spec thiết kế 4.6, INTEGRATION_SPEC §4.2 cho phép kèm `history` cả với khách vãng lai): `completedCount = previous_bookings_not_canceled`,
`cancelledCount = previous_cancellations`, các số đếm khác 0; khách `is_repeated_guest = 0` mà `previous_*` > 0 vẫn mang lịch sử đó.
Cột nhãn (`is_canceled`, `reservation_status`, `reservation_status_date`) chỉ vào `meta` để phân tích PT-02/PT-03, KHÔNG vào body request; `country`, `agent`, `company` không dùng.

Quy tắc lọc theo thứ tự (dòng bị loại ở quy tắc đầu tiên khớp, nên các lý do rời nhau): `zero_nights`, `negative_adr`, `invalid_arrival_date`
(năm/tháng/ngày không dựng được ngày, hoặc ngày đặt danh nghĩa tràn phạm vi), `stay_too_long` (thời lượng theo thời điểm tuyệt đối vượt `max_item_minutes`).
Dòng bị loại được đếm đủ trong `Selection.rejected` (mọi lý do đều có khóa, kể cả 0). Thời lượng lọc tính trên mốc GỐC; dời bội số 7 ngày chỉ có thể
đổi thời lượng ±60 phút khi lưu trú cắt ngày đổi giờ, nằm trong lề của `MAX_ITEM_MINUTES`; vì vậy không nên truyền `max_item_minutes` sát biên mà
runtime chấp nhận (runtime sẽ trả 400, bị tính là lỗi contract thay vì "bị loại có lý do").

Lấy mẫu (Q-07): chế độ `quick` lấy `sample_size` dòng hợp lệ bằng `random.Random(seed).sample` trên các dòng hợp lệ đã sắp theo `source_row` (cùng seed cùng tập,
không phụ thuộc thứ tự đầu vào), kết quả sắp lại theo `source_row`; chế độ `full` lấy mọi dòng hợp lệ.
"""
from __future__ import annotations

import csv
import random
from dataclasses import dataclass
from datetime import date, datetime, time, timedelta, timezone
from decimal import ROUND_HALF_UP, Decimal, InvalidOperation
from pathlib import Path
from typing import Any, Iterable
from zoneinfo import ZoneInfo

from evidence import contract_check, replay, time_shift
from evidence.anonymize import Anonymizer, uuid_from_hash
from evidence.replay import RowError

# --- Nguồn dữ liệu (Task 1, spec/PHASE5_REPORT_5.6.md S-1) ---
SOURCE_URL = "https://raw.githubusercontent.com/rfordatascience/tidytuesday/d75aaa0d31596ad6487ae0db20138067d301e031/data/2020/2020-02-11/hotels.csv"
SOURCE_SHA256 = "7c2ae42a7353905ea136e5c2287f17c92c5435826598bfbb8491c6f0c7b1fc06"
EXPECTED_ROW_COUNT = 119390  # số dòng dữ liệu, không tính tiêu đề
EXPECTED_COLUMN_COUNT = 32
LICENSE_LABEL = "CC BY 4.0 theo bài báo gốc; người dùng chưa xác nhận phân phối lại, nên KHÔNG commit dữ liệu gốc và Evidence Pack chỉ chứa D5 (cột đã chọn)"
CITATION = "Antonio, N., de Almeida, A., Nunes, L. (2019). Hotel booking demand datasets. Data in Brief, 22, 41-49. DOI 10.1016/j.dib.2018.11.126 (bản TidyTuesday 2020-02-11)"

# --- Tham số miền của instance eval-tadss-portability (Task 1, S-2); chọn một lần từ phân bố dữ liệu, không chỉnh theo kết quả ---
MAX_ITEM_MINUTES = 100800  # Tadss:Validation:MaxItemMinutes: 70 ngày, phủ lưu trú dài nhất của dataset (69 đêm = 99.240 phút)
HIGH_VALUE_THRESHOLD = Decimal(750)  # Tadss:Strategies:Default:HighValueThreshold tính bằng EUR (p90 giá trị đơn = 729, làm tròn bội số 50)

# --- Lấy mẫu (Q-07) ---
SAMPLE_SEED = 20260907  # seed cố định ghi vào manifest; đổi seed là đổi tập mẫu của bản nhanh
QUICK_SAMPLE_SIZE = 10000
MODE_QUICK = "quick"
MODE_FULL = "full"
MODES = (MODE_QUICK, MODE_FULL)

BRANCH_TIME_ZONE = "Europe/Lisbon"
CURRENCY = "EUR"
CHANNEL = "ONLINE"
CHECK_IN_HOUR = 14
CHECK_OUT_HOUR = 12
REQUESTED_HOUR = 10  # giờ đặt danh nghĩa (dataset không có giờ đặt)
TIER = "STANDARD"
REJECT_REASONS = ("zero_nights", "negative_adr", "invalid_arrival_date", "stay_too_long")
MONTHS = ("January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December")
INT_COLUMNS = (
    "is_canceled", "lead_time", "stays_in_weekend_nights", "stays_in_week_nights", "is_repeated_guest", "previous_cancellations", "previous_bookings_not_canceled",
)
TEXT_COLUMNS = ("hotel", "reserved_room_type", "deposit_type", "market_segment", "distribution_channel", "customer_type")
RESERVATION_STATUSES = ("Check-Out", "Canceled", "No-Show")
STATUS_COLUMNS = ("reservation_status", "reservation_status_date")
REQUIRED_COLUMNS = (*INT_COLUMNS, *TEXT_COLUMNS, *STATUS_COLUMNS, "arrival_date_year", "arrival_date_month", "arrival_date_day_of_month", "adr")
D5_COLUMNS = (
    "source_row", "hotel", "arrival_local", "nights", "room_type", "total_price", "is_repeated_guest", "previous_cancellations", "previous_bookings_not_canceled",
    "status", "deposit_rate", "deposit_amount", "reason_code", "is_canceled",
    "decision_id", "strategy_status", "contract_errors", "shift_days", "lead_time_minutes", "deposit_type", "market_segment", "attempts",
    "reservation_status", "reservation_status_date", "shadow_pair_state", "primary_fallback", "shadow_status", "shadow_deposit_rate", "shadow_deposit_amount",
)

_ZONE = ZoneInfo(BRANCH_TIME_ZONE)
_CENT = Decimal("0.01")


class HotelDataError(ValueError):
    """File CSV khách sạn sai định dạng; mang toàn bộ lỗi theo dòng để người dùng thấy một lần."""

    def __init__(self, errors: list[RowError]) -> None:
        self.errors = errors
        shown = "; ".join(f"dòng {error.line}: {error.message}" for error in errors[: replay.MAX_REPORTED_ERRORS])
        extra = f" (và {len(errors) - replay.MAX_REPORTED_ERRORS} lỗi khác)" if len(errors) > replay.MAX_REPORTED_ERRORS else ""
        super().__init__(f"File dataset khách sạn không hợp lệ: {shown}{extra}")


@dataclass(frozen=True)
class HotelRow:
    """Một dòng dataset đã kiểm kiểu, chỉ giữ cột adapter dùng. `source_row` là số dòng trong file gốc (dòng 1 là tiêu đề).
    `arrival` là None khi năm/tháng/ngày không dựng được ngày (dòng đó bị lọc `invalid_arrival_date`, không phải lỗi định dạng)."""

    source_row: int
    hotel: str
    is_canceled: int
    lead_time: int
    arrival: date | None
    weekend_nights: int
    week_nights: int
    adr: Decimal
    is_repeated_guest: int
    previous_cancellations: int
    previous_bookings_not_canceled: int
    room_type: str
    deposit_type: str
    market_segment: str
    distribution_channel: str
    customer_type: str
    reservation_status: str  # nhãn kết thúc, chỉ dùng phân tích, không vào request
    reservation_status_date: date  # ngày đạt trạng thái kết thúc, chỉ dùng phân tích, không vào request

    @property
    def nights(self) -> int:
        return self.weekend_nights + self.week_nights


@dataclass(frozen=True)
class Selection:
    """Kết quả `select`: `rows` là tập gửi đi (sắp theo `source_row`); `rejected` đếm theo từng lý do trên TOÀN BỘ dòng nguồn (mọi lý do đều có khóa);
    `valid_count` là số dòng hợp lệ trước khi lấy mẫu; `sample_size` là None ở chế độ `full`."""

    mode: str
    seed: int
    sample_size: int | None
    total_rows: int
    valid_count: int
    rejected: dict[str, int]
    rows: list[HotelRow]


def load_rows(path: Path | str) -> list[HotelRow]:
    """Đọc CSV khách sạn; ném `HotelDataError` (kèm số dòng của mọi ô sai) nếu thiếu cột hoặc ô sai kiểu. Không bỏ qua dòng nào âm thầm.

    Ngày đến không dựng được (tên tháng sai, 31/4…) KHÔNG phải lỗi định dạng: dòng vẫn được nạp với `arrival = None` để `select` lọc và đếm."""
    with Path(path).open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        missing = [column for column in REQUIRED_COLUMNS if column not in (reader.fieldnames or [])]
        if missing:
            raise HotelDataError([RowError(1, f"thiếu cột {', '.join(missing)}")])
        rows: list[HotelRow] = []
        errors: list[RowError] = []
        for record in reader:
            problems: list[str] = []
            row = _parse_row(record, reader.line_num, problems)
            if problems:
                errors.extend(RowError(reader.line_num, problem) for problem in problems)
            elif row is not None:
                rows.append(row)
    if errors:
        raise HotelDataError(errors)
    return rows


def reject_reason(row: HotelRow, max_item_minutes: int = MAX_ITEM_MINUTES) -> str | None:
    """Lý do dòng bị loại trước khi gửi (một trong `REJECT_REASONS`, theo thứ tự quy tắc Q-08); None nếu gửi được."""
    if row.nights == 0:
        return "zero_nights"
    if row.adr < 0:
        return "negative_adr"
    moments = _moments(row)
    if moments is None:
        return "invalid_arrival_date"
    _, start, end = moments
    if _minutes_between(start, end) > max_item_minutes:
        return "stay_too_long"
    return None


def select(rows: Iterable[HotelRow], mode: str, seed: int = SAMPLE_SEED, sample_size: int = QUICK_SAMPLE_SIZE, max_item_minutes: int = MAX_ITEM_MINUTES) -> Selection:
    """Lọc theo Q-08 (đếm theo lý do) rồi lấy mẫu theo Q-07: `quick` lấy `sample_size` dòng hợp lệ, `full` lấy hết. Cùng `seed` cho cùng tập."""
    if mode not in MODES:
        raise ValueError(f"mode phải là {' hoặc '.join(MODES)}, nhận '{mode}'")
    rejected = {reason: 0 for reason in REJECT_REASONS}
    valid: list[HotelRow] = []
    total = 0
    for row in rows:
        total += 1
        reason = reject_reason(row, max_item_minutes)
        if reason is None:
            valid.append(row)
        else:
            rejected[reason] += 1
    valid.sort(key=lambda row: row.source_row)
    chosen = valid
    if mode == MODE_QUICK and sample_size < len(valid):
        chosen = sorted(random.Random(seed).sample(valid, sample_size), key=lambda row: row.source_row)
    return Selection(
        mode=mode, seed=seed, sample_size=sample_size if mode == MODE_QUICK else None, total_rows=total, valid_count=len(valid), rejected=rejected, rows=chosen,
    )


def build_requests(rows: Iterable[HotelRow], anonymizer: Anonymizer, run_id: str, now: datetime, max_item_minutes: int = MAX_ITEM_MINUTES) -> list[dict[str, Any]]:
    """Dựng request v2 cho mọi dòng gửi được (dòng có `reject_reason` bị bỏ qua, như replay); giữ thứ tự đầu vào. Mỗi phần tử: `{idempotency_key, body, meta}`.

    `meta` mang dữ liệu cho D5 và phân tích: `source_row`, `hotel`, `arrival_local` (14:00 ngày đến GỐC, naive), `nights`, `room_type`, `total_price`,
    `is_repeated_guest`, `previous_cancellations`, `previous_bookings_not_canceled`, `is_canceled` (nhãn gốc), `deposit_type`, `market_segment`, `reservation_status`, `reservation_status_date` (ISO), `shift_days`, `lead_time_minutes` và `duration_min`
    (cả hai tính trên mốc GỐC, theo thời điểm tuyệt đối) và `request_errors` (lỗi theo schema; rỗng nếu hợp lệ). Khóa idempotency `portability:{run_id}:{source_row}`."""
    return [_build_request(row, anonymizer, run_id, now) for row in rows if reject_reason(row, max_item_minutes) is None]


def d5_rows(prepared: list[dict[str, Any]], results: list[dict[str, Any] | None], d1_index: dict[str, dict[str, Any]] | None = None) -> list[dict[str, Any]]:
    """D5: một dòng cho mỗi request (cột `D5_COLUMNS`), `results` cùng thứ tự `prepared`; quy ước kết quả và "chưa gửi" giống `replay.d4_rows`
    (`results[i]` là None hoặc có `status` None nghĩa là chưa gửi; request vốn lỗi schema chỉ ghi lỗi request). `d1_index` tra `strategy_status` theo `decision_id`."""
    if len(prepared) != len(results):
        raise ValueError(f"prepared ({len(prepared)}) và results ({len(results)}) phải cùng số phần tử, cùng thứ tự")
    index = d1_index or {}
    # cùng cách đọc response/quy ước NOT_SENT với D4 (một nguồn duy nhất)
    return [_d5_row(entry["meta"], replay.tadss_columns(entry, result, index)) for entry, result in zip(prepared, results)]


def d5_rows_from_outcomes(prepared: list[dict[str, Any]], outcomes: dict[str, dict[str, Any]], d1_index: dict[str, dict[str, Any]] | None = None) -> list[dict[str, Any]]:
    """Như `d5_rows` nhưng dựng từ file của bước (e) không cần body request: `prepared` là các dòng `prepared.jsonl` THEO THỨ TỰ ĐẦU VÀO,
    `outcomes` là các dòng `requests.jsonl` tra theo `key` (xem `replay.d4_rows_from_outcomes`)."""
    index = d1_index or {}
    return [_d5_row(entry["meta"], replay.outcome_columns(entry["meta"], outcomes.get(entry["idempotency_key"]), index)) for entry in prepared]


def _d5_row(meta: dict[str, Any], outcome: dict[str, Any]) -> dict[str, Any]:
    return {
        "source_row": meta["source_row"], "hotel": meta["hotel"], "arrival_local": meta["arrival_local"], "nights": meta["nights"], "room_type": meta["room_type"],
        "total_price": replay.plain(meta["total_price"]), "is_repeated_guest": meta["is_repeated_guest"], "previous_cancellations": meta["previous_cancellations"],
        "previous_bookings_not_canceled": meta["previous_bookings_not_canceled"], "status": outcome["status"], "deposit_rate": outcome["deposit_rate"],
        "deposit_amount": outcome["deposit_amount"], "reason_code": outcome["reason_code"], "is_canceled": meta["is_canceled"],
        "decision_id": outcome["decision_id"], "strategy_status": outcome["strategy_status"], "contract_errors": outcome["contract_errors"],
        "shift_days": meta["shift_days"], "lead_time_minutes": meta["lead_time_minutes"],
        "deposit_type": meta["deposit_type"], "market_segment": meta["market_segment"], "attempts": outcome["attempts"],
        # prepared cũ chưa có hai cột trạng thái nên dùng .get; năm cột shadow để rỗng, bước sau điền
        "reservation_status": meta.get("reservation_status", ""), "reservation_status_date": meta.get("reservation_status_date", ""),
        "shadow_pair_state": "", "primary_fallback": "", "shadow_status": "", "shadow_deposit_rate": "", "shadow_deposit_amount": "",
    }


def _moments(row: HotelRow) -> tuple[datetime, datetime, datetime] | None:
    """(ngày đặt danh nghĩa 10:00, vào 14:00, ra 12:00) giờ Lisbon, mốc GỐC; None khi ngày đến không dựng được hoặc phép cộng ngày tràn phạm vi."""
    if row.arrival is None:
        return None
    try:
        requested = datetime.combine(row.arrival - timedelta(days=row.lead_time), time(REQUESTED_HOUR), tzinfo=_ZONE)
        start = datetime.combine(row.arrival, time(CHECK_IN_HOUR), tzinfo=_ZONE)
        end = datetime.combine(row.arrival + timedelta(days=row.nights), time(CHECK_OUT_HOUR), tzinfo=_ZONE)
    except OverflowError:
        return None
    return requested, start, end


def _minutes_between(start: datetime, end: datetime) -> int:
    """Số phút giữa hai thời điểm tuyệt đối (qua UTC; trừ hai datetime cùng múi giờ sẽ bỏ qua đổi giờ mùa hè)."""
    return int((end.astimezone(timezone.utc) - start.astimezone(timezone.utc)) // timedelta(minutes=1))


def _build_request(row: HotelRow, anonymizer: Anonymizer, run_id: str, now: datetime) -> dict[str, Any]:
    moments = _moments(row)
    if moments is None:
        raise ValueError(f"dòng {row.source_row} có ngày đến không hợp lệ; phải qua reject_reason trước khi dựng request")
    requested, start, end = moments
    weeks = time_shift.weeks_to_shift(requested, now)
    price = (row.adr * row.nights).quantize(_CENT, rounding=ROUND_HALF_UP)
    is_guest = row.is_repeated_guest == 0

    body: dict[str, Any] = {
        "reservationId": uuid_from_hash(anonymizer.hash("reservation", str(row.source_row))),
        "branchId": uuid_from_hash(anonymizer.hash("hotel", row.hotel)),
        "branchTimeZone": BRANCH_TIME_ZONE,
        "items": [
            {
                "resourceId": uuid_from_hash(anonymizer.hash("room", f"{row.hotel}|{row.room_type}")),
                "resourceTypeId": uuid_from_hash(anonymizer.hash("room_type", row.room_type)),
                "startTime": time_shift.shift(start, weeks).isoformat(),
                "endTime": time_shift.shift(end, weeks).isoformat(),
                "price": replay.json_number(price),
            }
        ],
        "customer": {
            "customerId": None if is_guest else uuid_from_hash(anonymizer.hash("customer", str(row.source_row))),
            "isGuest": is_guest,
            "tier": TIER,
            "memberSince": None,
            "history": {"completedCount": row.previous_bookings_not_canceled, "noShowCount": 0, "cancelledCount": row.previous_cancellations, "lateCancelledCount": 0, "paymentExpiredCount": 0},
        },
        "totalPrice": replay.json_number(price),
        "currency": CURRENCY,
        "channel": CHANNEL,
        "requestedAt": time_shift.shift(requested, weeks).isoformat(),
        "metadata": {
            "market_segment": row.market_segment, "distribution_channel": row.distribution_channel, "deposit_type": row.deposit_type, "customer_type": row.customer_type,
        },
    }
    meta = {
        "source_row": row.source_row, "hotel": row.hotel, "arrival_local": start.replace(tzinfo=None).isoformat(), "nights": row.nights, "room_type": row.room_type,
        "total_price": price, "is_repeated_guest": row.is_repeated_guest, "previous_cancellations": row.previous_cancellations,
        "previous_bookings_not_canceled": row.previous_bookings_not_canceled, "is_canceled": row.is_canceled,
        "deposit_type": row.deposit_type, "market_segment": row.market_segment,
        "reservation_status": row.reservation_status, "reservation_status_date": row.reservation_status_date.isoformat(),
        "shift_days": weeks * 7, "lead_time_minutes": _minutes_between(requested, start), "duration_min": _minutes_between(start, end),
        "request_errors": contract_check.request_errors(body),
    }
    return {"idempotency_key": f"portability:{run_id}:{row.source_row}", "body": body, "meta": meta}


def _parse_row(record: dict[str, str], line: int, problems: list[str]) -> HotelRow | None:
    def cell(column: str) -> str:
        return (record.get(column) or "").strip()

    numbers = {column: _int_cell(cell(column), column, problems) for column in INT_COLUMNS}
    adr = _adr_cell(cell("adr"), problems)
    texts = {column: cell(column) for column in TEXT_COLUMNS}
    for column in ("hotel", "reserved_room_type"):
        if not texts[column]:
            problems.append(f"cột {column} bắt buộc")
    if numbers["is_repeated_guest"] not in (None, 0, 1):
        problems.append("cột is_repeated_guest phải là 0 hoặc 1")
    status = cell("reservation_status")
    if status not in RESERVATION_STATUSES:
        problems.append(f"cột reservation_status phải là {', '.join(RESERVATION_STATUSES)}, nhận '{status}'")
    try:
        status_date: date | None = date.fromisoformat(cell("reservation_status_date"))
    except ValueError:
        problems.append("cột reservation_status_date phải là ngày YYYY-MM-DD")
        status_date = None
    if problems or adr is None or status_date is None or any(value is None for value in numbers.values()):
        return None
    return HotelRow(
        source_row=line, hotel=texts["hotel"], is_canceled=numbers["is_canceled"] or 0, lead_time=numbers["lead_time"] or 0,
        arrival=_arrival(cell("arrival_date_year"), cell("arrival_date_month"), cell("arrival_date_day_of_month")),
        weekend_nights=numbers["stays_in_weekend_nights"] or 0, week_nights=numbers["stays_in_week_nights"] or 0, adr=adr,
        is_repeated_guest=numbers["is_repeated_guest"] or 0, previous_cancellations=numbers["previous_cancellations"] or 0,
        previous_bookings_not_canceled=numbers["previous_bookings_not_canceled"] or 0, room_type=texts["reserved_room_type"], deposit_type=texts["deposit_type"],
        market_segment=texts["market_segment"], distribution_channel=texts["distribution_channel"], customer_type=texts["customer_type"],
        reservation_status=status, reservation_status_date=status_date,
    )


def _int_cell(text: str, column: str, problems: list[str]) -> int | None:
    """Số nguyên không âm chỉ gồm chữ số ASCII: từ chối `1_0`, dấu `+`/`-`, khoảng trắng bên trong và chữ số Unicode mà `int()` vẫn nhận."""
    if not (text.isascii() and text.isdigit()):
        problems.append(f"cột {column} không phải số nguyên không âm (chỉ chữ số 0-9)")
        return None
    return int(text)


def _adr_cell(text: str, problems: list[str]) -> Decimal | None:
    """`adr` âm vẫn hợp lệ về định dạng (bị lọc `negative_adr` ở `select`); chỉ chuỗi không phải số hữu hạn mới là lỗi."""
    try:
        value = Decimal(text)
    except InvalidOperation:
        problems.append("cột adr không phải số")
        return None
    if not value.is_finite():
        problems.append("cột adr không phải số hữu hạn")
        return None
    return value


def _arrival(year: str, month: str, day: str) -> date | None:
    if month not in MONTHS:
        return None
    try:
        return date(int(year), MONTHS.index(month) + 1, int(day))
    except ValueError:
        return None
