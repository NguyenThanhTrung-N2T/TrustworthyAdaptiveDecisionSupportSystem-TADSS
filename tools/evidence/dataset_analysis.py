"""Phân tích mô tả cho dữ liệu thực tế (gói 5.6): RP-01/RP-02 trên D4, PT-02 trên D5; dựng D4/D5 từ file của bước (e); thông tin `datasets` cho manifest.

- `rp_summary(d4_rows, calendar)`: phân bố trạng thái theo kênh; mức cọc trung bình theo hạng và theo khung giờ cao điểm / thấp điểm (lịch lấy từ
  `Tadss:Calendar:PeakWindows` của `src/TADSS.Runtime.Api/appsettings.json`, một nguồn duy nhất, nửa mở `[Start, End)` như `PeakWindow.Contains`); so sánh `STATIC_100` với `TADSS_V2`
  (kết quả runtime và kết quả hiệu chỉnh offline theo approval window bằng lead time gốc, Q-04). Số liệu chính trên tập `ONLINE` (đúng phạm vi BE sẽ gọi TADSS, Q-12) kèm toàn tập.
- `pt_summary(d5_rows)`: phân bố trạng thái và mức cọc; hai bảng chéo (trạng thái × `is_canceled`, mức cọc × `deposit_type` gốc). Chỉ số mô tả, KHÔNG kiểm định hay kết luận nhân quả.
- `build_d4` / `build_d5`: ghép `prepared.jsonl` với `requests.jsonl` theo khóa idempotency (không cần body request) rồi dựng bộ dữ liệu.

Quy ước: dòng `TADSS_V2` không có trạng thái (HTTP lỗi, chưa gửi) là `NO_RESULT`, `REJECTED` đếm riêng (`rejected`); cả hai nằm ngoài mẫu số của tỷ lệ cần duyệt; mức cọc trung bình tính trên các booking CÓ cọc (`APPROVED`).
"""
from __future__ import annotations

import csv
import json
from collections import Counter
from dataclasses import dataclass
from datetime import datetime, time
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Any, Iterable

from evidence import hotel_adapter, replay, shadow_pairing

APPROVED = "APPROVED"
NEED_APPROVAL = replay.NEED_APPROVAL
REJECTED = "REJECTED"  # strategy mặc định 2.1.0 sinh ra khi khách lặp lại việc giữ chỗ hết hạn thanh toán; không có cọc, không phải "chưa có quyết định"
NO_RESULT = "NO_RESULT"
PEAK = "PEAK"
OFFPEAK = "OFFPEAK"
SCOPE_ONLINE = "ONLINE"
SCOPE_ALL = "ALL"
NO_SHOW = "NO_SHOW"
POLICY_ADJUSTED = "TADSS_V2_ADJUSTED"
APPSETTINGS_RELATIVE = Path("src") / "TADSS.Runtime.Api" / "appsettings.json"
DAY_NAMES = ("Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday")


@dataclass(frozen=True)
class PeakWindow:
    """Khung giờ cao điểm `[start, end)` của một ngày trong tuần (`day`: 0 = thứ Hai … 6 = Chủ nhật), theo giờ địa phương của chi nhánh."""

    day: int
    start: time
    end: time


def load_calendar(appsettings: Path) -> list[PeakWindow]:
    """Đọc `Tadss:Calendar:PeakWindows` từ appsettings của runtime (chỉ đọc)."""
    document = json.loads(Path(appsettings).read_text(encoding="utf-8"))
    windows = document["Tadss"]["Calendar"]["PeakWindows"]
    return [PeakWindow(DAY_NAMES.index(item["Day"]), time.fromisoformat(item["Start"]), time.fromisoformat(item["End"])) for item in windows]


def is_peak(start_local: str, calendar: Iterable[PeakWindow]) -> bool:
    """`start_local` là giờ bắt đầu sớm nhất theo giờ địa phương (ISO, không múi giờ). Dời bội số 7 ngày không đổi thứ và giờ nên dùng mốc gốc được."""
    moment = datetime.fromisoformat(start_local)
    return any(window.day == moment.weekday() and window.start <= moment.time() < window.end for window in calendar)


def _decimal(value: Any) -> Decimal | None:
    if value is None or isinstance(value, bool) or value == "":
        return None
    try:
        number = value if isinstance(value, Decimal) else Decimal(str(value))
    except InvalidOperation:
        return None
    return number if number.is_finite() else None


def _truthy(value: Any) -> bool:
    return value is True or str(value).strip().lower() == "true"


def _mean(values: list[Decimal]) -> float | None:
    return float(sum(values) / len(values)) if values else None


def _view(row: dict[str, Any], adjusted: bool) -> tuple[str, Decimal | None, Decimal | None]:
    """(trạng thái, mức cọc, số tiền cọc) của một dòng; `adjusted` dùng `status_window_adjusted`: đổi sang APPROVED thì cọc 100 % (rate 1, amount = totalPrice),
    đổi sang NEED_APPROVAL thì không còn mức cọc."""
    status = str(row.get("status") or "") or NO_RESULT
    rate, amount = _decimal(row.get("deposit_rate")), _decimal(row.get("deposit_amount"))
    if not adjusted or status == NO_RESULT:
        return status, rate, amount
    target = str(row.get("status_window_adjusted") or status)
    if target == status:
        return status, rate, amount
    if target == APPROVED:
        return APPROVED, Decimal(1), _decimal(row.get("total_price"))
    return target, None, None


def _policy_stats(rows: list[dict[str, Any]], adjusted: bool = False) -> dict[str, Any]:
    views = [(row, *_view(row, adjusted)) for row in rows]
    approved = [(row, rate, amount) for row, status, rate, amount in views if status == APPROVED]
    need = sum(1 for _, status, _, _ in views if status == NEED_APPROVAL)
    decided = len(approved) + need
    rejected = sum(1 for _, status, _, _ in views if status == REJECTED)
    no_show = [(row, status, amount) for row, status, _, amount in views if row.get("dev_final_status") == NO_SHOW]
    return {
        "bookings": len(rows), "decided": decided, "approved": len(approved), "need_approval": need, "rejected": rejected,
        "no_decision": len(rows) - decided - rejected,
        "mean_rate": _mean([rate for _, rate, _ in approved if rate is not None]),
        "mean_amount": _mean([amount for _, _, amount in approved if amount is not None]),
        "need_approval_rate": need / decided if decided else None,
        "deposit_total": float(sum((amount for _, _, amount in approved if amount is not None), Decimal(0))),
        "no_show": {
            "bookings": len(no_show),
            "total_price": float(sum((_decimal(row.get("total_price")) or Decimal(0) for row, _, _ in no_show), Decimal(0))),
            "deposit_kept": float(sum((amount or Decimal(0) for _, status, amount in no_show if status == APPROVED), Decimal(0))),
        },
    }


def _breakdown(rows: list[dict[str, Any]]) -> dict[str, Any]:
    """Một nhóm dòng `TADSS_V2` (runtime): số booking, số có/không có cọc, mức cọc trung bình."""
    stats = _policy_stats(rows)
    return {key: stats[key] for key in ("bookings", "approved", "need_approval", "no_decision", "mean_rate", "mean_amount")}


def _scope(pairs: dict[str, list[dict[str, Any]]], calendar: list[PeakWindow]) -> dict[str, Any]:
    static, tadss = pairs[replay.POLICY_STATIC], pairs[replay.POLICY_TADSS]
    by_channel: dict[str, Counter] = {}
    for row in tadss:
        by_channel.setdefault(str(row["channel"]), Counter())[_view(row, False)[0]] += 1
    tiers = [tier for tier in replay.TIERS if any(row["customer_tier"] == tier for row in tadss)]
    return {
        "bookings": len(tadss),
        "window_bias_bookings": sum(1 for row in tadss if _truthy(row.get("window_bias"))),
        "status_by_channel": {channel: dict(sorted(counter.items())) for channel, counter in sorted(by_channel.items())},
        "policies": {replay.POLICY_STATIC: _policy_stats(static), replay.POLICY_TADSS: _policy_stats(tadss), POLICY_ADJUSTED: _policy_stats(tadss, adjusted=True)},
        "adjusted_changes": sum(1 for row in tadss if _view(row, False)[0] != _view(row, True)[0]),
        "by_tier": {tier: _breakdown([row for row in tadss if row["customer_tier"] == tier]) for tier in tiers},
        "by_window": {
            label: _breakdown([row for row in tadss if is_peak(str(row["start_local"]), calendar) is (label == PEAK)])
            for label in (PEAK, OFFPEAK)
        },
    }


def rp_summary(d4_rows: list[dict[str, Any]], calendar: list[PeakWindow]) -> dict[str, Any]:
    """Tổng hợp RP-01/RP-02 từ D4 dạng dài (mỗi booking hai dòng `STATIC_100` và `TADSS_V2`). `main_scope` là `ONLINE` (Q-12), `scopes.ALL` là toàn tập."""
    split = {policy: [row for row in d4_rows if row["policy"] == policy] for policy in (replay.POLICY_STATIC, replay.POLICY_TADSS)}
    online = {policy: [row for row in rows if row["channel"] == SCOPE_ONLINE] for policy, rows in split.items()}
    return {
        "bookings": len(split[replay.POLICY_TADSS]),
        "channels": dict(sorted(Counter(str(row["channel"]) for row in split[replay.POLICY_TADSS]).items())),
        "main_scope": SCOPE_ONLINE,
        "calendar": [{"day": DAY_NAMES[window.day], "start": window.start.strftime("%H:%M"), "end": window.end.strftime("%H:%M")} for window in calendar],
        "scopes": {SCOPE_ONLINE: _scope(online, calendar), SCOPE_ALL: _scope(split, calendar)},
    }


def _rate_label(row: dict[str, Any]) -> str:
    status = str(row.get("status") or "") or NO_RESULT
    if status != APPROVED:
        return status
    rate = _decimal(row.get("deposit_rate"))
    return replay.plain(rate) if rate is not None else APPROVED


def _label_order(label: str) -> tuple[int, Decimal, str]:
    number = _decimal(label)
    if number is not None:
        return 0, number, label
    return (2 if label == NO_RESULT else 1), Decimal(0), label


def pt_summary(d5_rows: list[dict[str, Any]]) -> dict[str, Any]:
    """Tổng hợp PT-02 từ D5: phân bố, bảng chéo trạng thái × `is_canceled` và mức cọc × `deposit_type`. Mô tả, không suy diễn."""
    statuses = [str(row.get("status") or "") or NO_RESULT for row in d5_rows]
    canceled = [str(row.get("is_canceled")) == "1" for row in d5_rows]
    rates = [_decimal(row.get("deposit_rate")) for row, status in zip(d5_rows, statuses) if status == APPROVED]
    rates = [rate for rate in rates if rate is not None]
    labels = [_rate_label(row) for row in d5_rows]

    by_status = []
    for status in sorted(set(statuses), key=lambda item: (item != APPROVED, item != NEED_APPROVAL, item)):
        flags = [flag for flag, value in zip(canceled, statuses) if value == status]
        by_status.append({"status": status, "total": len(flags), "canceled": sum(flags), "not_canceled": len(flags) - sum(flags), "cancel_rate": sum(flags) / len(flags)})

    row_labels = sorted(set(labels), key=_label_order)
    deposit_types = sorted({str(row.get("deposit_type") or "") for row in d5_rows})
    counts = {label: {kind: 0 for kind in deposit_types} for label in row_labels}
    for label, row in zip(labels, d5_rows):
        counts[label][str(row.get("deposit_type") or "")] += 1
    groups = []
    for label in row_labels:
        flags = [flag for flag, value in zip(canceled, labels) if value == label]
        groups.append({"label": label, "total": len(flags), "canceled": sum(flags), "cancel_rate": sum(flags) / len(flags)})

    return {
        "records": len(d5_rows),
        "status_counts": dict(sorted(Counter(statuses).items(), key=lambda item: (item[0] != APPROVED, item[0] != NEED_APPROVAL, item[0]))),
        "rate_distribution": {replay.plain(rate): count for rate, count in sorted(Counter(rates).items())},
        "mean_rate": _mean(rates),
        "status_by_canceled": by_status,
        "canceled_total": sum(canceled),
        "cancel_rate_overall": sum(canceled) / len(canceled) if canceled else None,
        "rate_by_deposit_type": {"rows": row_labels, "columns": deposit_types, "counts": counts},
        "rate_groups": groups,
    }


# --- Dựng D4/D5 từ file của bước (e) ---

def read_jsonl(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    with path.open(encoding="utf-8") as handle:
        return [json.loads(line) for line in handle if line.strip()]


def _paired(run_dir: Path, part: str) -> tuple[list[dict[str, Any]], dict[str, dict[str, Any]]] | None:
    prepared = read_jsonl(run_dir / "raw" / part / "prepared.jsonl")
    if not prepared:
        return None
    return prepared, {row["key"]: row for row in read_jsonl(run_dir / "raw" / part / "requests.jsonl")}


def build_d4(run_dir: Path, d1_index: dict[str, dict[str, Any]]) -> list[dict[str, Any]] | None:
    """D4 (RP-01) từ `raw/replay/{prepared,requests}.jsonl`; None khi bước (e) không chuẩn bị booking nào (RP bị bỏ)."""
    paired = _paired(run_dir, "replay")
    return None if paired is None else replay.d4_rows_from_outcomes(paired[0], paired[1], d1_index)


def build_d5(run_dir: Path, d1_index: dict[str, dict[str, Any]]) -> list[dict[str, Any]] | None:
    """D5 (PT-01) từ `raw/portability/{prepared,requests}.jsonl`; None khi PT không chuẩn bị dòng nào."""
    paired = _paired(run_dir, "portability")
    return None if paired is None else hotel_adapter.d5_rows_from_outcomes(paired[0], paired[1], d1_index)


def run_correlations(run_dir: Path, part: str) -> dict[str, str]:
    """khóa idempotency → correlation_id từ `raw/{part}/requests.jsonl` của lượt (chỉ dòng có correlation_id)."""
    return {row["key"]: row["correlation_id"] for row in read_jsonl(run_dir / "raw" / part / "requests.jsonl") if row.get("correlation_id")}


def build_d5_with_shadow(run_dir: Path, d1_rows: list[dict[str, Any]]) -> tuple[list[dict[str, Any]], shadow_pairing.Pairing] | None:
    """D5 kèm 5 cột shadow (PT-04): ghép PRIMARY/SHADOW của D1 PT theo correlation_id, phạm vi lượt = correlation_id trong requests.jsonl. None khi PT không chuẩn bị dòng nào."""
    paired = _paired(run_dir, "portability")
    if paired is None:
        return None
    prepared, outcomes = paired
    rows = hotel_adapter.d5_rows_from_outcomes(prepared, outcomes, d1_index(d1_rows))
    keys = run_correlations(run_dir, "portability")
    pairing = shadow_pairing.pair(d1_rows, set(keys.values()))
    shadow_pairing.attach(rows, [keys.get(entry["idempotency_key"]) for entry in prepared], pairing)
    return rows, pairing


def write_csv(path: Path, columns: Iterable[str], rows: Iterable[dict[str, Any]]) -> int:
    """Ghi CSV UTF-8 với đúng các cột (thứ tự cố định); trả số dòng dữ liệu. Giá trị luận lý ghi `True`/`False`."""
    path.parent.mkdir(parents=True, exist_ok=True)
    count = 0
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(columns))
        writer.writeheader()
        for row in rows:
            writer.writerow(row)
            count += 1
    return count


def d1_index(d1_rows: Iterable[dict[str, Any]]) -> dict[str, dict[str, Any]]:
    """Tra cứu D1 theo decision_id, chỉ bản PRIMARY. Bản SHADOW có decision_id riêng; ghép với PRIMARY qua shadow_pairing (correlation_id)."""
    return {row["decision_id"]: row for row in d1_rows if row.get("role", "PRIMARY") == "PRIMARY"}


# --- Độ phủ danh mục và thông tin cho manifest ---

REPLAY_INFO_KEYS = ("status", "note", "source_rows", "prepared", "sent", "not_sent", "contract_error_requests", "retries", "rejected", "window_bias_count", "status_counts", "decision_status_counts")
PORTABILITY_INFO_KEYS = (
    "status", "note", "total_rows", "valid_count", "selected", "prepared", "sent", "not_sent", "contract_error_requests", "retries", "rejected", "status_counts", "decision_status_counts",
)


def _read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else None


def dataset_coverage(run_dir: Path) -> tuple[set[str], set[str]]:
    """(mã đạt, mã đỏ) của RP-01, RP-02, PT-01…PT-04 từ file của pack: RP-01/PT-01 đạt khi phần đó đã gửi request và 0 lỗi contract (đỏ khi có lỗi contract;
    PT-01 cũng đỏ khi kiểm mã SC7 không đạt); phần bị bỏ, lỗi hạ tầng hoặc chưa chạy thì không vào đâu. RP-02/PT-02 đạt khi file phân tích tồn tại (phân tích đã hoàn tất).
    PT-03 đạt khi `raw/d5/policy_economics.json` tồn tại. PT-04 đạt khi nhãn đối soát shadow là RECONCILED và không có vi phạm toàn vẹn; đỏ khi nhãn FAILED hoặc có vi phạm;
    còn lại (UNKNOWN, thiếu nhãn) không vào đâu."""
    passed: set[str] = set()
    failed: set[str] = set()
    code_status = (_read_json(run_dir / "raw" / "analysis" / "sc7_code_check.json") or {}).get("status")
    for part, code, analysis_file, analysis_code in (("replay", "RP-01", "rp_summary.json", "RP-02"), ("portability", "PT-01", "pt_summary.json", "PT-02")):
        summary = _read_json(run_dir / "raw" / part / "summary.json") or {}
        if summary.get("status") in ("passed", "failed") and int(summary.get("sent") or 0) > 0:
            errors = int(summary.get("contract_error_requests") or 0)
            (failed if errors or (part == "portability" and code_status == "FAIL") else passed).add(code)
        if (run_dir / "raw" / "analysis" / analysis_file).exists():
            passed.add(analysis_code)
    economics = _read_json(run_dir / "raw" / "d5" / "policy_economics.json")
    if economics is not None:
        passed.add("PT-03")
        shadow = economics.get("shadow") or {}
        label = (shadow.get("reconciliation") or {}).get("label")
        if label == "FAILED" or shadow.get("violations"):
            failed.add("PT-04")
        elif label == "RECONCILED":
            passed.add("PT-04")
    return passed, failed


def dataset_info(run_dir: Path, code_check: dict[str, Any] | None) -> dict[str, Any]:
    """Phần `datasets` của manifest: số liệu gửi của RP và PT (không kèm mẫu lỗi hay định danh), nguồn PT (URL, SHA-256, giấy phép, seed), chế độ lấy mẫu và kết quả kiểm mã."""
    replay_summary = _read_json(run_dir / "raw" / "replay" / "summary.json")
    pt_summary = _read_json(run_dir / "raw" / "portability" / "summary.json")
    source = _read_json(run_dir / "raw" / "portability" / "source.json")
    info: dict[str, Any] = {"replay": None, "portability": None}
    if replay_summary is not None:
        info["replay"] = {key: replay_summary[key] for key in REPLAY_INFO_KEYS if key in replay_summary}
    if pt_summary is not None:
        portability = {key: pt_summary[key] for key in PORTABILITY_INFO_KEYS if key in pt_summary}
        portability["mode"] = pt_summary.get("mode") or (source or {}).get("mode")
        portability["sampleSize"] = pt_summary.get("sample_size", (source or {}).get("sample_size"))
        portability["seed"] = (source or {}).get("seed")
        portability["source"] = source
        portability["codeCheck"] = code_check
        info["portability"] = portability
    return info
