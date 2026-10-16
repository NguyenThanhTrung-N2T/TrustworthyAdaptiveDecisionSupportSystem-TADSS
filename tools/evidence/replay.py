"""Replay booking SmashCourt qua TADSS (RP-01, gói 5.6): CSV do `sql/export_smashcourt_bookings.sql` xuất -> request v2 -> bộ dữ liệu D4.

Luồng: `load_bookings` (kiểm kiểu từng ô, lỗi kèm số dòng) -> `build_requests` (ẩn danh bằng `Anonymizer` của lượt chạy, dời bội số 7 ngày theo Q-04,
kiểm request theo schema) -> bước gửi -> `d4_rows` (dạng dài: mỗi booking hai dòng `STATIC_100` và `TADSS_V2`).

Ánh xạ request theo INTEGRATION_SPEC §4.2: `branchTimeZone = Asia/Ho_Chi_Minh`, `currency = VND`, `channel = source`, `totalPrice = final_total`,
mỗi sân trong cột `items` của CSV là một item (sân nào được xuất do export_smashcourt_bookings.sql quyết định), `tier` từ thứ hạng `min_points` (rank > 5 gộp `DIAMOND`, chưa có hạng là `STANDARD`), khách vãng lai không có `customerId`.
`demand` bỏ trống (Q-12), `metadata` chỉ ghi nguồn `replay`. Mọi UUID trong request dựng từ mã băm HMAC của định danh gốc (16 byte đầu, đặt bit version/variant),
nên cùng định danh trong một lượt cho cùng UUID và không UUID gốc nào rời khỏi tiến trình.

Booking không có hóa đơn hoặc có `items` rỗng bị `reject_reason` loại trước khi gửi và được đếm theo lý do (`count_rejections`). Booking
có request không qua schema thì VẪN được giữ, `meta["request_errors"]` ghi lỗi để bước gửi tính là lỗi contract (Q-09), không bị loại âm thầm.
"""
from __future__ import annotations

import csv
import json
import uuid
from dataclasses import dataclass
from datetime import date, datetime, time
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Any, Iterable
from zoneinfo import ZoneInfo

from evidence import contract_check, time_shift
from evidence.anonymize import Anonymizer, uuid_from_hash

EXPORT_COLUMNS = (
    "booking_id", "branch_id", "customer_id", "source", "status", "created_at", "items", "final_total", "tier_rank", "member_since",
    "history_completed", "history_no_show", "history_cancelled", "history_late_cancelled", "history_payment_expired",
)
D4_COLUMNS = (
    "booking_hash", "branch_hash", "court_count", "booking_date", "start_local", "duration_min", "total_price", "customer_tier", "customer_is_guest", "dev_final_status",
    "policy", "status", "deposit_rate", "deposit_amount", "reason_code",
    "decision_id", "channel", "lead_time_minutes", "shift_days", "window_bias", "strategy_status", "status_window_adjusted", "contract_errors",
)

BRANCH_TIME_ZONE = "Asia/Ho_Chi_Minh"
CURRENCY = "VND"
SOURCES = ("ONLINE", "WALK_IN")
BOOKING_STATUSES = (
    "PENDING", "CONFIRMED", "PAID_ONLINE", "IN_PROGRESS", "PENDING_PAYMENT", "COMPLETED", "CANCELLED", "CANCELLED_PENDING_REFUND", "CANCELLED_REFUNDED", "NO_SHOW",
)
TIERS = ("STANDARD", "SILVER", "GOLD", "PLATINUM", "DIAMOND")
MIN_LEAD_MINUTES = 120  # Tadss:Approval:MinLeadMinutes; lead time gốc không quá ngưỡng này thì cờ window_bias bật (Q-04)
WINDOW_MINUTES = 30  # Tadss:Approval:WindowMinutes (INTEGRATION_SPEC §1.1)
NEED_APPROVAL = "NEED_APPROVAL"
POLICY_STATIC = "STATIC_100"
POLICY_TADSS = "TADSS_V2"
MAX_REPORTED_ERRORS = 20

_BRANCH_ZONE = ZoneInfo(BRANCH_TIME_ZONE)


@dataclass(frozen=True)
class RowError:
    """Một lỗi định dạng ở file xuất: số dòng của file CSV (dòng 1 là tiêu đề) và mô tả."""

    line: int
    message: str


class ReplayDataError(ValueError):
    """File CSV xuất sai định dạng; mang toàn bộ lỗi theo dòng để người dùng sửa một lần."""

    def __init__(self, errors: list[RowError]) -> None:
        self.errors = errors
        shown = "; ".join(f"dòng {error.line}: {error.message}" for error in errors[:MAX_REPORTED_ERRORS])
        extra = f" (và {len(errors) - MAX_REPORTED_ERRORS} lỗi khác)" if len(errors) > MAX_REPORTED_ERRORS else ""
        super().__init__(f"File xuất SmashCourt không hợp lệ: {shown}{extra}")


@dataclass(frozen=True)
class Item:
    """Một sân hoạt động của booking; `start`/`end` là giờ địa phương của chi nhánh (naive) đúng như dữ liệu gốc."""

    court_id: str
    court_type_id: str
    start: datetime
    end: datetime
    price: Decimal


@dataclass(frozen=True)
class Booking:
    """Một dòng của file xuất đã kiểm kiểu. `final_total` là None khi booking không có hóa đơn."""

    line: int
    booking_id: str
    branch_id: str
    customer_id: str | None
    source: str
    status: str
    created_at: datetime
    items: tuple[Item, ...]
    final_total: Decimal | None
    tier_rank: int | None
    member_since: datetime | None
    history: tuple[int, ...]  # năm số đếm theo thứ tự: completed, no_show, cancelled, late_cancelled, payment_expired


def load_bookings(path: Path | str) -> list[Booking]:
    """Đọc file CSV xuất; ném `ReplayDataError` (kèm số dòng của mọi dòng sai) nếu thiếu cột hoặc có ô sai kiểu. Không bỏ qua dòng nào âm thầm."""
    with Path(path).open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        missing = [column for column in EXPORT_COLUMNS if column not in (reader.fieldnames or [])]
        if missing:
            raise ReplayDataError([RowError(1, f"thiếu cột {', '.join(missing)}")])
        bookings: list[Booking] = []
        errors: list[RowError] = []
        for row in reader:
            problems: list[str] = []
            booking = _parse_row(row, reader.line_num, problems)
            if problems:
                errors.extend(RowError(reader.line_num, problem) for problem in problems)
            elif booking is not None:
                bookings.append(booking)
    if errors:
        raise ReplayDataError(errors)
    return bookings


def reject_reason(booking: Booking) -> str | None:
    """Lý do booking bị loại trước khi gửi: `no_invoice` (không có hóa đơn) hoặc `no_active_courts` (không có sân để dựng item); None nếu gửi được."""
    if booking.final_total is None:
        return "no_invoice"
    if not booking.items:
        return "no_active_courts"
    return None


def count_rejections(bookings: Iterable[Booking]) -> dict[str, int]:
    """Số booking bị loại theo từng lý do (chỉ các lý do có xuất hiện)."""
    counts: dict[str, int] = {}
    for booking in bookings:
        reason = reject_reason(booking)
        if reason is not None:
            counts[reason] = counts.get(reason, 0) + 1
    return counts


def tier_for_rank(rank: int | None) -> str:
    """Thứ hạng `min_points` tăng dần (1…n) -> STANDARD…DIAMOND; rank > 5 gộp DIAMOND, chưa có hạng là STANDARD (INTEGRATION_SPEC §4.2)."""
    if rank is None or rank < 1:
        return TIERS[0]
    return TIERS[min(rank, len(TIERS)) - 1]


def build_requests(bookings: Iterable[Booking], anonymizer: Anonymizer, run_id: str, now: datetime) -> list[dict[str, Any]]:
    """Dựng request v2 cho mọi booking gửi được; giữ thứ tự đầu vào. Mỗi phần tử: `{idempotency_key, body, meta}`.

    `meta` mang dữ liệu cho D4 và phân tích: `booking_hash`, `branch_hash`, `court_count`, `booking_date`, `start_local`, `duration_min`, `total_price`,
    `customer_tier`, `customer_is_guest`, `dev_final_status`, `channel`, `shift_days`, `lead_time_minutes`, `window_bias` và `request_errors`
    (lỗi theo schema; rỗng nếu hợp lệ). `lead_time_minutes` = giờ bắt đầu sớm nhất trừ `requestedAt`, tính trên mốc GỐC (chưa dời), làm tròn xuống phút;
    có thể ÂM (nhập booking sau giờ chơi, VD khách vãng lai) và vẫn là request hợp lệ. `window_bias` bật khi `lead_time_minutes <= 120` (kể cả âm; 120 bật, 121 tắt). Khóa idempotency `replay:{run_id}:{booking_hash}` (Q-03)."""
    return [_build_request(booking, anonymizer, run_id, now) for booking in bookings if reject_reason(booking) is None]


def d4_rows(prepared: list[dict[str, Any]], results: list[dict[str, Any] | None], d1_index: dict[str, dict[str, Any]]) -> list[dict[str, Any]]:
    """D4 dạng dài: mỗi booking hai dòng liên tiếp `STATIC_100` rồi `TADSS_V2` (cột `D4_COLUMNS`).

    `results` cùng thứ tự `prepared`; mỗi phần tử: `status` (HTTP cuối), `body` (response đã parse, hoặc None), `decision_id` (tùy chọn, mặc định lấy từ body).
    `d1_index` tra `strategy_status` theo `decision_id`. `STATIC_100` là chính sách cọc 100 % tĩnh (cột `decision_id`, `strategy_status`, `contract_errors` để trống).
    Dòng `TADSS_V2` của request không có response 200 để trống `status`, mức cọc và lý do; `contract_errors` ghi lỗi (request cộng response) cách nhau bằng ` | `.
    Response 200 có `deposit.rate`/`amount` rỗng hoặc không phải số thì cột tương ứng để trống và lỗi nằm trong `contract_errors` (không ném).

    Quy ước "chưa gửi": `results[i]` là None, hoặc có `status` None, nghĩa là bước gửi không có kết quả cho request đó. Nếu request vốn lỗi schema
    (`meta["request_errors"]` không rỗng) thì nó không được phép gửi nên chỉ ghi lỗi request; nếu request hợp lệ mà không có kết quả thì ghi thêm
    `NOT_SENT: ...` (khác `STATUS: HTTP n`, không sinh lỗi "HTTP 0"). Kết nối thất bại có status số (VD 0 do bước gửi gán) vẫn là `STATUS: HTTP 0`."""
    if len(prepared) != len(results):
        raise ValueError(f"prepared ({len(prepared)}) và results ({len(results)}) phải cùng số phần tử, cùng thứ tự")
    rows: list[dict[str, Any]] = []
    for entry, result in zip(prepared, results):
        rows.extend(_d4_pair(entry["meta"], entry["body"]["currency"], tadss_columns(entry, result, d1_index)))
    return rows


def d4_rows_from_outcomes(prepared: list[dict[str, Any]], outcomes: dict[str, dict[str, Any]], d1_index: dict[str, dict[str, Any]]) -> list[dict[str, Any]]:
    """Như `d4_rows` nhưng dựng từ file của bước (e) KHÔNG cần body request: `prepared` là các dòng `prepared.jsonl` (`idempotency_key`, `meta`) THEO THỨ TỰ ĐẦU VÀO
    và `outcomes` là các dòng `requests.jsonl` tra theo `key`. Cột hợp đồng lấy từ `outcome` và `contract_errors` mà bước (e) đã ghi (Q-09)."""
    rows: list[dict[str, Any]] = []
    for entry in prepared:
        rows.extend(_d4_pair(entry["meta"], CURRENCY, outcome_columns(entry["meta"], outcomes.get(entry["idempotency_key"]), d1_index)))
    return rows


def _d4_pair(meta: dict[str, Any], currency: str, tadss: dict[str, Any]) -> list[dict[str, Any]]:
    common = {
        "booking_hash": meta["booking_hash"], "branch_hash": meta["branch_hash"], "court_count": meta["court_count"], "booking_date": meta["booking_date"],
        "start_local": meta["start_local"], "duration_min": meta["duration_min"], "total_price": plain(meta["total_price"]),
        "customer_tier": meta["customer_tier"], "customer_is_guest": meta["customer_is_guest"], "dev_final_status": meta["dev_final_status"],
        "channel": meta["channel"], "lead_time_minutes": meta["lead_time_minutes"], "shift_days": meta["shift_days"], "window_bias": meta["window_bias"],
    }
    static = {
        **common, "policy": POLICY_STATIC, "status": "APPROVED", "deposit_rate": "1",
        "deposit_amount": plain(contract_check.expected_amount(meta["total_price"], 1, currency)), "reason_code": POLICY_STATIC,
        "decision_id": "", "strategy_status": "", "status_window_adjusted": "APPROVED", "contract_errors": "",
    }
    return [static, {**common, **{key: value for key, value in tadss.items() if key in D4_COLUMNS}, "policy": POLICY_TADSS}]


def adjusted_status(strategy_status: str, status: str, lead_minutes: int) -> str:
    """Kết quả hiệu chỉnh offline theo approval window (INTEGRATION_SPEC §1.1) dùng lead time GỐC (Q-04).

    Chỉ áp khi strategy trả `NEED_APPROVAL`: hạn duyệt = `min(WindowMinutes, lead − MinLeadMinutes)`; còn dương thì `NEED_APPROVAL`, ngược lại `APPROVED` (cọc 100 %).
    Không có kết quả (`status` rỗng) hoặc thiếu `strategy_status` (không ghép được D1) thì giữ nguyên `status`."""
    if not status or strategy_status != NEED_APPROVAL:
        return status
    return NEED_APPROVAL if min(WINDOW_MINUTES, lead_minutes - MIN_LEAD_MINUTES) > 0 else "APPROVED"


def tadss_columns(entry: dict[str, Any], result: dict[str, Any] | None, d1_index: dict[str, dict[str, Any]]) -> dict[str, Any]:
    result = result or {}
    status = result.get("status")
    body = result.get("body")
    errors = list(entry["meta"]["request_errors"])
    if status is None:
        if not errors:
            errors.append("NOT_SENT: request hợp lệ nhưng bước gửi không có kết quả")
    else:
        errors.extend(contract_check.response_errors(entry["body"], int(status), body))
    columns: dict[str, Any] = {
        "status": "", "deposit_rate": "", "deposit_amount": "", "reason_code": "", "decision_id": result.get("decision_id") or "", "strategy_status": "",
        "contract_errors": " | ".join(errors), "attempts": result.get("attempts", "") if status is not None else "",
    }
    if status == contract_check.STATUS_OK and isinstance(body, dict):
        deposit = body.get("deposit") if isinstance(body.get("deposit"), dict) else {}
        columns.update({
            "status": body.get("status", ""), "reason_code": body.get("reasonCode", ""), "decision_id": columns["decision_id"] or body.get("decisionId", ""),
            "deposit_rate": _plain_or_empty(deposit.get("rate")), "deposit_amount": _plain_or_empty(deposit.get("amount")),
        })
    return _with_strategy(columns, entry["meta"], d1_index)


def outcome_columns(meta: dict[str, Any], request_row: dict[str, Any] | None, d1_index: dict[str, dict[str, Any]]) -> dict[str, Any]:
    """Cột kết quả của dòng `TADSS_V2` (hoặc D5) dựng từ một dòng `requests.jsonl` của bước (e); None nghĩa là bước gửi không có dòng cho request này.
    Cùng quy ước "chưa gửi" với `tadss_columns`; `attempts` là số lần thử (rỗng khi không có dòng)."""
    if request_row is None:
        errors = list(meta["request_errors"]) or ["NOT_SENT: request hợp lệ nhưng bước gửi không có kết quả"]
        outcome: dict[str, Any] = {}
    else:
        errors = list(request_row.get("contract_errors") or [])
        outcome = request_row.get("outcome") or {}
    row = request_row or {}
    columns = {
        "status": outcome.get("status", ""), "deposit_rate": outcome.get("deposit_rate", ""), "deposit_amount": outcome.get("deposit_amount", ""),
        "reason_code": outcome.get("reason_code", ""), "decision_id": row.get("decision_id") or "", "strategy_status": "", "contract_errors": " | ".join(errors),
        "attempts": row.get("attempts", "") if request_row is not None else "",
    }
    return _with_strategy(columns, meta, d1_index)


def _with_strategy(columns: dict[str, Any], meta: dict[str, Any], d1_index: dict[str, dict[str, Any]]) -> dict[str, Any]:
    decision_id = columns["decision_id"]
    columns["strategy_status"] = d1_index.get(decision_id, {}).get("strategy_status", "") if decision_id else ""
    columns["status_window_adjusted"] = adjusted_status(columns["strategy_status"], columns["status"], int(meta["lead_time_minutes"]))
    return columns


def _build_request(booking: Booking, anonymizer: Anonymizer, run_id: str, now: datetime) -> dict[str, Any]:
    total_price = booking.final_total
    if total_price is None:
        raise ValueError(f"booking dòng {booking.line} không có hóa đơn; phải qua reject_reason trước khi dựng request")
    booking_hash = anonymizer.hash("booking", booking.booking_id)
    branch_hash = anonymizer.hash("branch", booking.branch_id)
    requested_at = booking.created_at.astimezone(_BRANCH_ZONE)
    weeks = time_shift.weeks_to_shift(requested_at, now)
    first = min(booking.items, key=lambda item: (item.start, item.court_id))
    earliest_start = first.start.replace(tzinfo=_BRANCH_ZONE)
    is_guest = booking.customer_id is None
    tier = TIERS[0] if is_guest else tier_for_rank(booking.tier_rank)
    lead_minutes = int((earliest_start - requested_at).total_seconds() // 60)
    completed, no_show, cancelled, late_cancelled, payment_expired = booking.history

    body: dict[str, Any] = {
        "reservationId": uuid_from_hash(booking_hash),
        "branchId": uuid_from_hash(branch_hash),
        "branchTimeZone": BRANCH_TIME_ZONE,
        "items": [
            {
                "resourceId": uuid_from_hash(anonymizer.hash("court", item.court_id)),
                "resourceTypeId": uuid_from_hash(anonymizer.hash("court_type", item.court_type_id)),
                "startTime": time_shift.shift(item.start.replace(tzinfo=_BRANCH_ZONE), weeks).isoformat(),
                "endTime": time_shift.shift(item.end.replace(tzinfo=_BRANCH_ZONE), weeks).isoformat(),
                "price": json_number(item.price),
            }
            for item in booking.items
        ],
        "customer": {
            "customerId": None if is_guest else uuid_from_hash(anonymizer.hash("customer", booking.customer_id)),
            "isGuest": is_guest,
            "tier": tier,
            "memberSince": None if is_guest or booking.member_since is None else time_shift.shift(booking.member_since.astimezone(_BRANCH_ZONE), weeks).isoformat(),
            "history": {
                "completedCount": completed, "noShowCount": no_show, "cancelledCount": cancelled,
                "lateCancelledCount": late_cancelled, "paymentExpiredCount": payment_expired,
            },
        },
        "totalPrice": json_number(total_price),
        "currency": CURRENCY,
        "channel": booking.source,
        "requestedAt": time_shift.shift(requested_at, weeks).isoformat(),
        "metadata": {"source": "replay"},
    }
    meta = {
        "booking_hash": booking_hash, "branch_hash": branch_hash, "court_count": len(booking.items),
        "booking_date": earliest_start.date().isoformat(), "start_local": earliest_start.replace(tzinfo=None).isoformat(),
        "duration_min": int((first.end - first.start).total_seconds() // 60), "total_price": total_price,
        "customer_tier": tier, "customer_is_guest": is_guest, "dev_final_status": booking.status, "channel": booking.source,
        "shift_days": weeks * 7, "lead_time_minutes": lead_minutes, "window_bias": lead_minutes <= MIN_LEAD_MINUTES,
        "request_errors": contract_check.request_errors(body),
    }
    return {"idempotency_key": f"replay:{run_id}:{booking_hash}", "body": body, "meta": meta}


def json_number(value: Decimal) -> int | float:
    """Số tiền thành số JSON: nguyên khi không có phần lẻ (VND), ngược lại float."""
    return int(value) if value == value.to_integral_value() else float(value)


def _plain_or_empty(value: Any) -> str:
    """Như `plain` nhưng trả chuỗi rỗng khi giá trị thiếu, null hoặc không phải số hữu hạn (lỗi đã nằm ở `contract_errors`)."""
    if value is None or isinstance(value, bool):
        return ""
    try:
        number = value if isinstance(value, Decimal) else Decimal(str(value))
    except InvalidOperation:
        return ""
    return plain(number) if number.is_finite() else ""


def plain(value: Any) -> str:
    """Số thành chuỗi thập phân thường (không ký hiệu mũ), bỏ số 0 thừa: `114000.0` -> `114000`, `0.50` -> `0.5`."""
    number = value if isinstance(value, Decimal) else Decimal(str(value))
    text = format(number, "f")
    if "." in text:
        text = text.rstrip("0").rstrip(".")
    return text or "0"


def _parse_row(row: dict[str, str], line: int, problems: list[str]) -> Booking | None:
    def cell(column: str) -> str:
        return (row.get(column) or "").strip()

    booking_id = _uuid_cell(cell("booking_id"), "booking_id", problems, required=True)
    branch_id = _uuid_cell(cell("branch_id"), "branch_id", problems, required=True)
    customer_id = _uuid_cell(cell("customer_id"), "customer_id", problems, required=False)
    source = cell("source")
    if source not in SOURCES:
        problems.append(f"cột source phải là {' hoặc '.join(SOURCES)}, nhận '{source}'")
    status = cell("status")
    if status not in BOOKING_STATUSES:
        problems.append(f"cột status '{status}' không thuộc booking_status")
    created_at = _datetime_cell(cell("created_at"), "created_at", problems, required=True)
    member_since = _datetime_cell(cell("member_since"), "member_since", problems, required=False)
    items = _items_cell(cell("items"), problems)
    final_total = _decimal_cell(cell("final_total"), "final_total", problems)
    tier_rank = _int_cell(cell("tier_rank"), "tier_rank", problems, minimum=1, required=False)
    history = tuple(_int_cell(cell(column), column, problems, minimum=0, required=True) or 0 for column in EXPORT_COLUMNS[10:])  # EXPORT_COLUMNS[10:] là năm cột history_*
    if problems or created_at is None or booking_id is None or branch_id is None or items is None:
        return None
    return Booking(
        line=line, booking_id=booking_id, branch_id=branch_id, customer_id=customer_id, source=source, status=status, created_at=created_at,
        items=items, final_total=final_total, tier_rank=tier_rank, member_since=member_since, history=history,
    )


def _uuid_cell(text: str, column: str, problems: list[str], *, required: bool) -> str | None:
    if not text:
        if required:
            problems.append(f"cột {column} bắt buộc")
        return None
    try:
        return str(uuid.UUID(text))
    except ValueError:
        problems.append(f"cột {column} không phải UUID")
        return None


def _datetime_cell(text: str, column: str, problems: list[str], *, required: bool) -> datetime | None:
    if not text:
        if required:
            problems.append(f"cột {column} bắt buộc")
        return None
    try:
        moment = datetime.fromisoformat(text)
    except ValueError:
        problems.append(f"cột {column} không phải thời điểm ISO 8601")
        return None
    if moment.tzinfo is None:
        problems.append(f"cột {column} thiếu múi giờ")
        return None
    return moment


def _decimal_cell(text: str, column: str, problems: list[str]) -> Decimal | None:
    if not text:
        return None
    try:
        value = Decimal(text)
    except InvalidOperation:
        problems.append(f"cột {column} không phải số")
        return None
    if not value.is_finite() or value < 0:
        problems.append(f"cột {column} phải là số không âm")
        return None
    return value


def _int_cell(text: str, column: str, problems: list[str], *, minimum: int, required: bool) -> int | None:
    if not text:
        if required:
            problems.append(f"cột {column} bắt buộc")
        return None
    try:
        value = int(text)
    except ValueError:
        problems.append(f"cột {column} không phải số nguyên")
        return None
    if value < minimum:
        problems.append(f"cột {column} phải ≥ {minimum}")
        return None
    return value


def _items_cell(text: str, problems: list[str]) -> tuple[Item, ...] | None:
    try:
        raw = json.loads(text, parse_float=Decimal)
    except ValueError:
        problems.append("cột items không phải JSON")
        return None
    if not isinstance(raw, list):
        problems.append("cột items phải là mảng JSON")
        return None
    items: list[Item] = []
    for position, element in enumerate(raw, start=1):
        item = _parse_item(element, position, problems)
        if item is not None:
            items.append(item)
    return tuple(items)


def _parse_item(element: Any, position: int, problems: list[str]) -> Item | None:
    label = f"items[{position}]"
    if not isinstance(element, dict):
        problems.append(f"{label} phải là object")
        return None
    missing = [key for key in ("court_id", "court_type_id", "date", "start_time", "end_time", "price") if element.get(key) is None]
    if missing:
        problems.append(f"{label} thiếu {', '.join(missing)}")
        return None
    local_problems: list[str] = []
    court_id = _uuid_cell(str(element["court_id"]), f"{label}.court_id", local_problems, required=True)
    court_type_id = _uuid_cell(str(element["court_type_id"]), f"{label}.court_type_id", local_problems, required=True)
    try:
        day = date.fromisoformat(str(element["date"]))
        start = datetime.combine(day, time.fromisoformat(str(element["start_time"])))
        end = datetime.combine(day, time.fromisoformat(str(element["end_time"])))
        price = Decimal(str(element["price"]))
    except (ValueError, InvalidOperation):
        local_problems.append(f"{label} có date, start_time, end_time hoặc price sai định dạng")
        problems.extend(local_problems)
        return None
    if end <= start:
        local_problems.append(f"{label} có end_time không sau start_time")
    if not price.is_finite() or price < 0:
        local_problems.append(f"{label}.price phải là số không âm")
    if local_problems or court_id is None or court_type_id is None:
        problems.extend(local_problems)
        return None
    return Item(court_id=court_id, court_type_id=court_type_id, start=start, end=end, price=price)
