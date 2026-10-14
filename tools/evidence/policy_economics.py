"""Mô phỏng kinh tế các chính sách cọc trên D5 (PT-03, gói 5.7b, spec 4.3). Kết quả là CHỈ SỐ MÔ PHỎNG THEO KỊCH BẢN, không phải kết quả tài chính.

- Nhãn hủy `is_canceled` chỉ dùng cho AUC/Δr; sự kiện giữ cọc `e = reservation_status ∈ {Canceled, No-Show}`; mẫu số `E` chung mọi kịch bản.
- Giờ đến 14:00 `Europe/Lisbon` là GIẢ ĐỊNH (dataset chỉ có ngày đến); ngày hủy chỉ có ngày → `S-SC*` báo hai cận (`low` = hủy lúc 00:00, nhiều giờ, hoàn nhiều;
  `high` = hủy lúc 23:59, ít giờ, hoàn ít).
- Hai biến thể hoàn tiền ngang hàng tới khi Q-6 được chốt; `AS_IS_BE` đo hoàn vượt riêng, không vào xếp hạng. GIỚI HẠN: `AS_IS_BE` chỉ tính với NEED_APPROVAL = 1
  (đơn chờ duyệt coi như cọc 100 % nên không bao giờ hoàn vượt) → rủi ro B-1 trên các đơn NEED_APPROVAL bị ước lượng THẤP.
- `ORACLE`: mức cọc = sự kiện giữ cọc; AUC đối với `is_canceled` bằng 1 chỉ khi `label_mismatch = 0`.
- So sánh chỉ trên dòng `PAIRED`; dữ liệu thiếu/lỗi bị loại có đếm, không thành 0. Bootstrap ghép cặp: mọi chính sách trên cùng tập chỉ số mỗi vòng; hiệu tính trong vòng.
"""
from __future__ import annotations

import math
import random
import time as clock
from collections import Counter
from dataclasses import dataclass
from datetime import date, datetime
from decimal import Decimal, InvalidOperation
from typing import Any
from zoneinfo import ZoneInfo

from evidence import cancel_policy, hotel_adapter, policy_metrics as pm, shadow_pairing as sp

SCHEMA = "policy_economics/1"
BOOTSTRAP_SEED = 20261014
BOOTSTRAP_ITERATIONS = 1000
MIN_CLASS_FOR_CI = 30
BIAS_FLAG_SHARE = 0.01
NO_DEPOSIT, STATIC_100, TADSS_DEFAULT, TADSS_SHADOW_MOCKALT, ORACLE = "NO_DEPOSIT", "STATIC_100", "TADSS_DEFAULT", "TADSS_SHADOW_MOCKALT", "ORACLE"
POLICIES = (NO_DEPOSIT, STATIC_100, TADSS_DEFAULT, TADSS_SHADOW_MOCKALT, ORACLE)
TADSS_POLICIES = (TADSS_DEFAULT, TADSS_SHADOW_MOCKALT)
S_SC, S_SC_X2, S_SC_HALF, S_FULL, S_NOSHOW = "S-SC", "S-SC×2", "S-SC÷2", "S-FULL", "S-NOSHOW"
SCENARIOS = (S_SC, S_SC_X2, S_SC_HALF, S_FULL, S_NOSHOW)
SC_SCENARIOS = (S_SC, S_SC_X2, S_SC_HALF)
BOUNDS = ("low", "high")
NA_MAIN, NA_LOWER = 1.0, 0.0
NA_CONVENTIONS = (NA_MAIN, NA_LOWER)
PROVISIONAL_DEFAULT_VARIANT = pm.REFUND_OF_PAID  # quy ước mô phỏng tạm thời (spec 4.3), KHÔNG phải chính sách tài chính đã xác nhận
CHECK_OUT, CANCELED, NO_SHOW = hotel_adapter.RESERVATION_STATUSES
EVENT_STATUSES = frozenset({CANCELED, NO_SHOW})
EXCLUSION_REASONS = ("no_decision", "invalid_price", "invalid_reservation_status", "unsupported_status", "invalid_rate")
DIFF_KEYS = ("DEFAULT_MINUS_STATIC_100", "MOCKALT_MINUS_DEFAULT")
INSUFFICIENT_DATA = "INSUFFICIENT_DATA"
NOT_APPLICABLE = "NOT_APPLICABLE"
NEED_APPROVAL = "NEED_APPROVAL"
AGGREGATE_NOTE = "Chỉ số mô phỏng theo kịch bản trên dữ liệu proxy khách sạn; không phải kết quả tài chính; giả định hành vi khách không đổi theo mức cọc; không kết luận nhân quả."
SC_LIMIT_NOTE = "Thời điểm hủy trong ngày và giờ đến không quan sát được; phân loại vào mốc hoàn tiền là bất định trong vùng đã nêu."
BIAS_THRESHOLD_NOTE = "99 % là ngưỡng vận hành, không phải ngưỡng thống kê"
# các chiều giả định của xếp hạng (khóa trong mục `by_case*`, nhãn trong câu kết luận)
RANKING_DIMENSIONS = (("scenario", "kịch bản giữ cọc"), ("variant", "biến thể hoàn tiền"), ("bound", "cận độ phân giải ngày"), ("na", "quy ước NEED_APPROVAL"))
_ZONE = ZoneInfo(hotel_adapter.BRANCH_TIME_ZONE)
_LABELS = {"1": 1, "true": 1, "0": 0, "false": 0}

# kịch bản S-SC* → position của dòng Canceled → (tỷ lệ hoàn ở cận low, ở cận high)
Refunds = dict[str, dict[int, tuple[float, float]]]


@dataclass(frozen=True)
class EconRow:
    """Một dòng D5 hợp lệ. `rates[policy]` None = `NEED_APPROVAL` (với dòng không PAIRED, `TADSS_SHADOW_MOCKALT` cũng None và không bao giờ được dùng)."""

    price: float
    canceled: int
    status: str
    start: datetime
    cancel_day: date
    rates: dict[str, float | None]
    paired: bool
    position: int
    weekend: bool

    @property
    def event(self) -> int:
        return 1 if self.status in EVENT_STATUSES else 0


def _text(value: Any) -> str:
    """Ô D5 → chuỗi; chỉ `None` thành rỗng (số 0 / `Decimal("0")` KHÔNG được thành rỗng như `value or ""`)."""
    return "" if value is None else str(value)


def _rate(status: str, text: str) -> tuple[float | None, str | None]:
    """(mức cọc hoặc None = NEED_APPROVAL, lý do loại)."""
    if status == NEED_APPROVAL:
        return None, None
    if status != "APPROVED":
        return None, "unsupported_status"
    try:
        value = Decimal(str(text))
    except InvalidOperation:
        return None, "invalid_rate"
    if not value.is_finite() or not Decimal(0) <= value <= Decimal(1):
        return None, "invalid_rate"
    return float(value), None


def _moments(raw: dict[str, Any]) -> tuple[datetime, date] | None:
    """(giờ bắt đầu giả định 14:00 ngày đến, có múi giờ chi nhánh; ngày hủy). Parse cả hai trong cùng một `try`: lỗi ở bất kỳ ô nào → None."""
    try:
        start = datetime.fromisoformat(str(raw.get("arrival_local")))
        cancel_day = date.fromisoformat(str(raw.get("reservation_status_date")))
    except ValueError:
        return None
    return (start.replace(tzinfo=_ZONE) if start.tzinfo is None else start.astimezone(_ZONE)), cancel_day


def _parse(position: int, raw: dict[str, Any]) -> EconRow | str:
    """Một dòng D5 → `EconRow`, hoặc lý do loại đầu tiên theo thứ tự `EXCLUSION_REASONS`.
    `invalid_reservation_status` gộp mọi nhãn kết quả thiếu/lỗi: `reservation_status`, `reservation_status_date`, `arrival_local`, `is_canceled`."""
    status = str(raw.get("status") or "")
    if not status:
        return "no_decision"
    try:
        price = Decimal(str(raw.get("total_price")))
    except InvalidOperation:
        return "invalid_price"
    if not price.is_finite() or price <= 0:
        return "invalid_price"
    reservation_status = str(raw.get("reservation_status") or "")
    moments, canceled = _moments(raw), _LABELS.get(str(raw.get("is_canceled")).strip().lower())
    if reservation_status not in hotel_adapter.RESERVATION_STATUSES or moments is None or canceled is None:
        return "invalid_reservation_status"
    default_rate, reason = _rate(status, _text(raw.get("deposit_rate")))
    if reason:
        return reason
    paired = raw.get("shadow_pair_state") == sp.PAIRED
    mock_rate: float | None = None
    if paired:  # MockAlternative chỉ dùng khi PAIRED
        mock_rate, reason = _rate(str(raw.get("shadow_status") or ""), _text(raw.get("shadow_deposit_rate")))
        if reason:
            return reason
    start, cancel_day = moments
    event = 1 if reservation_status in EVENT_STATUSES else 0
    rates = {NO_DEPOSIT: 0.0, STATIC_100: 1.0, TADSS_DEFAULT: default_rate, TADSS_SHADOW_MOCKALT: mock_rate, ORACLE: float(event)}
    return EconRow(float(price), canceled, reservation_status, start, cancel_day, rates, paired, position, start.weekday() >= 5)


def prepare(d5_rows: list[dict[str, Any]]) -> tuple[list[EconRow], dict[str, int]]:
    """Mọi dòng hợp lệ (kể cả không PAIRED, để đo bias) và số dòng bị loại theo lý do."""
    rows: list[EconRow] = []
    excluded = {reason: 0 for reason in EXCLUSION_REASONS}
    for position, raw in enumerate(d5_rows):
        parsed = _parse(position, raw)
        if isinstance(parsed, str):
            excluded[parsed] += 1
        else:
            rows.append(parsed)
    return rows, excluded


def _effective(rate: float | None, na: float) -> float:
    return na if rate is None else rate


def _ratio(numerator: float, denominator: float) -> float | None:
    return None if denominator == 0 else numerator / denominator


def _scenario_policies(policy: cancel_policy.CancelPolicy | None) -> dict[str, cancel_policy.CancelPolicy]:
    if policy is None:
        return {}
    return {S_SC: policy, S_SC_X2: cancel_policy.scaled(policy, Decimal(2)), S_SC_HALF: cancel_policy.scaled(policy, Decimal("0.5"))}


def _refund_bounds(rows: list[EconRow], policies: dict[str, cancel_policy.CancelPolicy]) -> Refunds:
    """Tỷ lệ hoàn của mỗi dòng `Canceled` ở hai cận theo từng kịch bản `S-SC*`: low = tại số giờ NHIỀU nhất (hủy 00:00), high = tại số giờ ÍT nhất (23:59).
    Tính trước một lần cho mọi chính sách/biến thể (số giờ không phụ thuộc mức cọc) — tránh lặp phép tính múi giờ trong vòng lặp chỉ số."""
    hours = {row.position: cancel_policy.hours_until_start_bounds(row.start, row.cancel_day) for row in rows if row.status == CANCELED}
    return {scenario: {position: (float(cancel_policy.refund_fraction(policy, most)), float(cancel_policy.refund_fraction(policy, least)))
                       for position, (least, most) in hours.items()} for scenario, policy in policies.items()}


def _phi(scenario: str, variant: str, row: EconRow, r: float, refunds: Refunds) -> tuple[float, float] | None:
    """(cận low, cận high) của φ; None cho Check-Out."""
    if row.status == CHECK_OUT:
        return None
    if row.status == NO_SHOW or scenario == S_FULL:
        return 1.0, 1.0
    if scenario == S_NOSHOW:
        return 0.0, 0.0
    pct_low, pct_high = refunds[scenario][row.position]
    return pm.retained_fraction(variant, r, pct_low), pm.retained_fraction(variant, r, pct_high)


def _retention_columns(rows: list[EconRow], scenario: str, variant: str, policy_name: str, na: float, refunds: Refunds) -> tuple[list[float], list[float]]:
    """Đóng góp từng dòng vào tử số tỷ lệ giữ lại `e·φ·r·P` ở cận low và cận high (0 cho Check-Out)."""
    low: list[float] = []
    high: list[float] = []
    for row in rows:
        r = _effective(row.rates[policy_name], na)
        phi = _phi(scenario, variant, row, r, refunds)
        low.append(0.0 if phi is None else phi[0] * r * row.price)
        high.append(0.0 if phi is None else phi[1] * r * row.price)
    return low, high


def _burden_column(rows: list[EconRow], policy_name: str, na: float) -> list[float]:
    return [0.0 if row.event else _effective(row.rates[policy_name], na) * row.price for row in rows]


def _crosstab(rows: list[EconRow]) -> tuple[dict[str, dict[str, int]], int]:
    """`is_canceled × reservation_status` trên mọi dòng hợp lệ; số dòng lệch nhãn (sự kiện mà c = 0, Check-Out mà c = 1)."""
    table = {label: {status: 0 for status in hotel_adapter.RESERVATION_STATUSES} for label in ("0", "1")}
    for row in rows:
        table[str(row.canceled)][row.status] += 1
    return table, table["0"][CANCELED] + table["0"][NO_SHOW] + table["1"][CHECK_OUT]


def _tier_bucket(policy: cancel_policy.CancelPolicy, row: EconRow) -> str:
    """Mốc của một đơn Canceled theo `S-SC` ở hai cận; cận đã qua giờ bắt đầu ghi `PAST` (tách với mốc < 2 giờ dù cùng hoàn 0 %, spec 4.3)."""
    least, most = cancel_policy.hours_until_start_bounds(row.start, row.cancel_day)

    def label(hours: float) -> str:
        return "PAST" if hours < 0 else f"{float(cancel_policy.refund_fraction(policy, hours)):g}"

    few, many = label(least), label(most)
    if few == many:
        return "PAST_START" if few == "PAST" else f"REFUND_{few}"
    return f"UNRESOLVED_{few}_{many}"


def _tier_distribution(compared: list[EconRow], policies: dict[str, cancel_policy.CancelPolicy]) -> list[dict[str, Any]]:
    """Phân bố giá trị đơn sự kiện theo mốc hoàn tiền của `S-SC` (mô tả, không xếp hạng); chỉ liệt kê bucket có đơn."""
    events = [row for row in compared if row.event]
    total = math.fsum(row.price for row in events)
    base = policies.get(S_SC)
    buckets: dict[str, list[float]] = {}
    for row in events:
        bucket = "NO_SHOW" if row.status == NO_SHOW else INSUFFICIENT_DATA if base is None else _tier_bucket(base, row)
        buckets.setdefault(bucket, []).append(row.price)
    return [{"bucket": bucket, "count": len(prices), "value": math.fsum(prices), "share_of_E": _ratio(math.fsum(prices), total)}
            for bucket, prices in sorted(buckets.items())]


def _point(compared: list[EconRow], refunds: Refunds) -> list[dict[str, Any]]:
    """Điểm ước lượng cho mọi (kịch bản × biến thể × cận × quy ước NEED_APPROVAL × chính sách). E = 0 → None (không phải 0)."""
    events = [row for row in compared if row.event]  # Check-Out không góp vào tử số
    total = math.fsum(row.price for row in events)
    result: list[dict[str, Any]] = []
    for scenario in (*refunds, S_FULL, S_NOSHOW):
        for variant in pm.VARIANTS:
            for na in NA_CONVENTIONS:
                for name in POLICIES:
                    for bound, column in zip(BOUNDS, _retention_columns(events, scenario, variant, name, na, refunds)):
                        kept = math.fsum(column)
                        result.append({"scenario": scenario, "variant": variant, "bound": bound, "na": na, "na_applicable": name in TADSS_POLICIES,
                                       "policy": name, "retention": _ratio(kept, total), "uncovered": None if total == 0 else total - kept})
    return result


def _burden(compared: list[EconRow]) -> list[dict[str, Any]]:
    """Nghĩa vụ ứng trước của khách không có sự kiện (proxy) `Σ (1−e)·r·P / Σ (1−e)·P`; mẫu số 0 → None."""
    denominator = math.fsum((1 - row.event) * row.price for row in compared)
    return [{"policy": name, "na": na, "na_applicable": name in TADSS_POLICIES, "value": _ratio(math.fsum(_burden_column(compared, name, na)), denominator)}
            for name in POLICIES for na in NA_CONVENTIONS]


def _discrimination(compared: list[EconRow]) -> list[dict[str, Any]]:
    """AUC (Mann–Whitney, score = mức cọc với NEED_APPROVAL = NA_MAIN) và Δr theo nhãn `is_canceled`; kèm phân bố mức cọc (NEED_APPROVAL tách riêng)."""
    labels = [row.canceled for row in compared]
    result: list[dict[str, Any]] = []
    for name in POLICIES:
        scores = [_effective(row.rates[name], NA_MAIN) for row in compared]
        found = pm.auc(scores, labels)
        distribution = Counter(NEED_APPROVAL if row.rates[name] is None else f"{row.rates[name]:.10g}" for row in compared)
        result.append({"policy": name, "na": NA_MAIN, "auc": {"value": found.value, "reason": found.reason}, "delta_r": pm.delta_r(scores, labels),
                       "rate_distribution": dict(sorted(distribution.items())), "oracle_by_definition": name == ORACLE})
    return result


def _need_approval(compared: list[EconRow]) -> list[dict[str, Any]]:
    """Bảng vận hành: số và tỷ lệ NEED_APPROVAL của hai chính sách TADSS trên tập so sánh; không quy ra cọc."""
    result: list[dict[str, Any]] = []
    for name in TADSS_POLICIES:
        count = sum(1 for row in compared if row.rates[name] is None)
        result.append({"policy": name, "count": count, "rate": _ratio(count, len(compared))})
    return result


def _bootstrap(rows: list[EconRow], refunds: Refunds, seed: int, iterations: int) -> dict[str, Any]:
    """Bootstrap ghép cặp cho `S-SC`, NA_MAIN, hai biến thể × hai cận: mỗi vòng một tập chỉ số dùng cho MỌI chính sách; hiệu tính trong vòng."""
    n = len(rows)
    if n == 0:
        return {"status": INSUFFICIENT_DATA}
    rng = random.Random(seed)
    event_col = [row.event * row.price for row in rows]
    burden_den = [(1 - row.event) * row.price for row in rows]
    burden_num = {name: _burden_column(rows, name, NA_MAIN) for name in POLICIES}
    labels = [row.canceled for row in rows]
    minority = min(sum(labels), n - sum(labels))
    auc_keys: dict[str, list[int]] = {}
    for name in TADSS_POLICIES:
        scores = [_effective(row.rates[name], NA_MAIN) for row in rows]
        if pm.auc(scores, labels).value is not None and minority >= MIN_CLASS_FOR_CI:
            levels = {score: index for index, score in enumerate(sorted(set(scores)))}
            auc_keys[name] = [levels[score] * 2 + label for score, label in zip(scores, labels)]
    cases: dict[tuple[str, str], dict[str, list[float]]] = {(variant, bound): {} for variant in pm.VARIANTS for bound in BOUNDS}
    for variant in pm.VARIANTS:
        for name in POLICIES:
            for bound, column in zip(BOUNDS, _retention_columns(rows, S_SC, variant, name, NA_MAIN, refunds)):
                cases[(variant, bound)][name] = column
    samples: dict[str, list[float]] = {}
    skipped = 0
    auc_skipped = {name: 0 for name in auc_keys}

    def add(key: str, value: float) -> None:
        samples.setdefault(key, []).append(value)

    population = range(n)
    for _ in range(iterations):
        idx = rng.choices(population, k=n)

        def total(column: list[float]) -> float:
            return math.fsum(map(column.__getitem__, idx))

        e_total, den_total = total(event_col), total(burden_den)
        if e_total == 0 or den_total == 0:  # vòng không có sự kiện hoặc không có khách hoàn tất: bỏ và đếm
            skipped += 1
            continue
        burden = {name: total(column) / den_total for name, column in burden_num.items()}
        for name, value in burden.items():
            add(f"burden|{name}", value)
        add("diff_burden|DEFAULT_MINUS_STATIC_100", burden[TADSS_DEFAULT] - burden[STATIC_100])
        add("diff_burden|MOCKALT_MINUS_DEFAULT", burden[TADSS_SHADOW_MOCKALT] - burden[TADSS_DEFAULT])
        for (variant, bound), columns in cases.items():
            retention = {name: total(column) / e_total for name, column in columns.items()}
            for name, value in retention.items():
                add(f"{variant}|{bound}|retention|{name}", value)
            add(f"{variant}|{bound}|diff|DEFAULT_MINUS_STATIC_100", retention[TADSS_DEFAULT] - retention[STATIC_100])
            add(f"{variant}|{bound}|diff|MOCKALT_MINUS_DEFAULT", retention[TADSS_SHADOW_MOCKALT] - retention[TADSS_DEFAULT])
        for name, keys in auc_keys.items():
            counts = Counter(map(keys.__getitem__, idx))
            positives = {key // 2: value for key, value in counts.items() if key % 2 == 1}
            negatives = {key // 2: value for key, value in counts.items() if key % 2 == 0}
            if positives and negatives and len(set(positives) | set(negatives)) > 1:
                add(f"auc|{name}", pm.auc_from_counts(positives, negatives))
            else:  # mẫu rơi vào một lớp hoặc điểm hằng: bỏ và đếm
                auc_skipped[name] += 1

    def interval(key: str) -> dict[str, float | int | None]:
        return pm.ci95(samples.get(key, []))

    def auc_interval(name: str) -> dict[str, float | int | None] | str:
        if name in auc_keys:
            return interval(f"auc|{name}")
        return NOT_APPLICABLE if name == ORACLE else INSUFFICIENT_DATA  # ORACLE: mức cọc = sự kiện, nhãn = is_canceled → AUC = 1 chỉ khi label_mismatch = 0; không tính CI

    def case(variant: str, bound: str) -> dict[str, Any]:
        return {"retention": {name: interval(f"{variant}|{bound}|retention|{name}") for name in POLICIES},
                "diff_retention": {key: interval(f"{variant}|{bound}|diff|{key}") for key in DIFF_KEYS},
                "burden": {name: interval(f"burden|{name}") for name in POLICIES},
                "diff_burden": {key: interval(f"diff_burden|{key}") for key in DIFF_KEYS},
                "auc": {name: auc_interval(name) for name in POLICIES}}

    result: dict[str, Any] = {variant: {bound: case(variant, bound) for bound in BOUNDS} for variant in pm.VARIANTS}
    result.update({"scenario": S_SC, "na": NA_MAIN, "seed": seed, "iterations": iterations, "skipped": skipped, "auc_skipped": auc_skipped,
                   "min_class_for_ci": MIN_CLASS_FOR_CI})
    return result


def _sign(default: float | None, mock: float | None) -> str | None:
    if default is None or mock is None:
        return None
    if math.isclose(default, mock, rel_tol=1e-12, abs_tol=1e-12):
        return "="
    return ">" if default > mock else "<"


def _contains_zero(interval: dict[str, Any]) -> bool | None:
    low, high = interval.get("low"), interval.get("high")
    return None if low is None or high is None else low <= 0 <= high


def _ranking_cases(point: list[dict[str, Any]], na: float) -> list[dict[str, Any]]:
    """Một mục cho mỗi (kịch bản, biến thể, cận) ở quy ước NEED_APPROVAL `na`: tỷ lệ giữ lại của hai chính sách TADSS và dấu `DEFAULT ? MOCKALT`."""
    values: dict[tuple[str, str, str], dict[str, float | None]] = {}
    for item in point:
        if item["na"] == na and item["policy"] in TADSS_POLICIES:
            values.setdefault((item["scenario"], item["variant"], item["bound"]), {})[item["policy"]] = item["retention"]
    return [{"scenario": scenario, "variant": variant, "bound": bound, "na": na, TADSS_DEFAULT: pair.get(TADSS_DEFAULT),
             TADSS_SHADOW_MOCKALT: pair.get(TADSS_SHADOW_MOCKALT), "sign": _sign(pair.get(TADSS_DEFAULT), pair.get(TADSS_SHADOW_MOCKALT))}
            for (scenario, variant, bound), pair in values.items()]


def _unstable_dimensions(cases: list[dict[str, Any]]) -> list[str]:
    """Các chiều giả định mà chỉ đổi riêng chiều đó (giữ nguyên ba chiều còn lại) đã làm dấu `>`/`<` đổi."""
    found: list[str] = []
    for name, _ in RANKING_DIMENSIONS:
        groups: dict[tuple[Any, ...], set[str]] = {}
        for case in cases:
            if case["sign"] in (">", "<"):
                groups.setdefault(tuple(case[key] for key, _ in RANKING_DIMENSIONS if key != name), set()).add(case["sign"])
        if any(len(signs) > 1 for signs in groups.values()):
            found.append(name)
    return found


def _ranking(point: list[dict[str, Any]], ci: dict[str, Any], compared: int | None = None) -> dict[str, Any]:
    """So `TADSS_DEFAULT` với `TADSS_SHADOW_MOCKALT` theo tỷ lệ giữ lại ở mọi (kịch bản, biến thể, cận, quy ước NEED_APPROVAL); `sign` đọc là `DEFAULT ? MOCKALT`.
    `by_case`: NEED_APPROVAL = 1 (có CI của hiệu cho S-SC); `by_case_na_lower`: NEED_APPROVAL = 0, chỉ ước lượng điểm (`ci_contains_zero` None). Tách hai khóa để
    bảng độ nhạy của báo cáo (đọc `by_case` như một dòng mỗi (kịch bản, biến thể, cận) ở r = 1) không đổi; `stable` xét TRÊN CẢ HAI danh sách.
    `indistinguishable`: có ít nhất một CI 95% của hiệu (S-SC) chứa 0 — hai biến thể và hai cận ngang hàng nên khác biệt phải đứng ở mọi trường hợp; None khi không có CI.
    `compared`: số dòng PAIRED (None = không biết); 0 thì câu kết luận nói rõ là không có gì để so sánh thay vì "E = 0"."""
    by_case = _ranking_cases(point, NA_MAIN)
    for entry in by_case:
        if entry["scenario"] == S_SC and entry["variant"] in ci:
            diff = ci[entry["variant"]][entry["bound"]]["diff_retention"]["MOCKALT_MINUS_DEFAULT"]
            entry.update({"diff_ci": diff, "ci_contains_zero": _contains_zero(diff)})
    lower = [entry | {"ci_contains_zero": None, "ci_note": "CI bootstrap chỉ có cho NEED_APPROVAL = 1"} for entry in _ranking_cases(point, NA_LOWER)]
    cases = by_case + lower
    signs = {entry["sign"] for entry in cases if entry["sign"] not in (None, "=")}
    stable = len(signs) <= 1
    unstable = [] if stable else _unstable_dimensions(cases)
    flags = [entry["ci_contains_zero"] for entry in by_case if entry.get("ci_contains_zero") is not None]
    indistinguishable = any(flags) if flags else None
    return {"compare": f"{TADSS_DEFAULT} ? {TADSS_SHADOW_MOCKALT}", "na": NA_MAIN, "na_conventions": list(NA_CONVENTIONS), "by_case": by_case,
            "by_case_na_lower": lower, "stable": stable, "unstable_dimensions": unstable, "indistinguishable": indistinguishable,
            "statement": _ranking_statement(cases, signs, stable, unstable, indistinguishable, compared)}


def _ranking_statement(cases: list[dict[str, Any]], signs: set[str], stable: bool, unstable: list[str], indistinguishable: bool | None, compared: int | None = None) -> str:
    """Câu kết luận đưa thẳng vào báo cáo. CHỈ nêu thứ tự hai chính sách khi dấu giống nhau ở MỌI trường hợp (kể cả NEED_APPROVAL = 0) VÀ không CI nào
    của hiệu chứa 0. CI chứa 0 → "không phân biệt được trên dữ liệu này"; dấu đổi → "kết luận phụ thuộc giả định" kèm chiều gây đổi dấu.
    Hai trường hợp này không nêu chính sách nào tốt hơn."""
    undecided = "CI 95 % của hiệu (S-SC, NEED_APPROVAL = 1) chứa 0: không phân biệt được trên dữ liệu này."
    no_ci = "Không có CI của hiệu (thiếu chính sách hủy S-SC hoặc không còn mẫu bootstrap hợp lệ)."
    if not any(entry["sign"] for entry in cases):
        if compared == 0:
            return "Không xếp hạng được: không có dòng PAIRED để so sánh."
        return "Không xếp hạng được: tập so sánh không có giá trị đơn sự kiện (E = 0)."
    if not stable:
        labels = ", ".join(label for key, label in RANKING_DIMENSIONS if key in unstable) or "nhiều giả định cùng lúc"
        parts = [f"Thứ hạng giữa hai chính sách TADSS đổi dấu theo {labels}: kết luận phụ thuộc giả định, không nêu chính sách tốt hơn."]
        return " ".join(parts + [undecided] if indistinguishable else parts)
    if indistinguishable:
        same_sign = "Ước lượng điểm giữ cùng dấu ở mọi trường hợp nhưng không đủ để nêu chính sách tốt hơn."
        return f"{undecided} {same_sign}" if signs else undecided
    if signs:
        ranked = (f"Thứ hạng ổn định ở mọi kịch bản, biến thể, cận và quy ước NEED_APPROVAL: TADSS_DEFAULT {next(iter(signs))} TADSS_SHADOW_MOCKALT "
                  "theo tỷ lệ cọc giữ lại mô phỏng.")
        if indistinguishable is None:
            return f"{ranked} {no_ci} Thứ hạng trên chỉ theo ước lượng điểm."
        return f"{ranked} CI 95 % chỉ có cho S-SC với NEED_APPROVAL = 1; các trường hợp khác (kể cả NEED_APPROVAL = 0) so theo ước lượng điểm."
    same = "Hai chính sách TADSS có cùng tỷ lệ cọc giữ lại mô phỏng ở mọi trường hợp."
    return f"{same} {no_ci}" if indistinguishable is None else same


def _as_is_be(compared: list[EconRow], refunds: Refunds) -> list[dict[str, Any]]:
    """Rủi ro B-1 nếu giữ nguyên mã hoàn tiền của BE (`pct` trên giá trị đơn, không chặn): số đơn Canceled và tổng phần hoàn VƯỢT số cọc đã thu,
    theo `S-SC`, NA_MAIN. Không có φ, không vào xếp hạng."""
    canceled = [row for row in compared if row.status == CANCELED]
    result: list[dict[str, Any]] = []
    for name in TADSS_POLICIES:
        for index, bound in enumerate(BOUNDS):
            by_pct: dict[str, list[float]] = {}
            for row in canceled:
                pct = refunds[S_SC][row.position][index]
                over = pm.over_refund(_effective(row.rates[name], NA_MAIN), pct, row.price)
                if over > 0:
                    by_pct.setdefault(f"{pct:g}", []).append(over)
            result.append({"policy": name, "scenario": S_SC, "na": NA_MAIN, "bound": bound, "count": sum(len(values) for values in by_pct.values()),
                           "value": math.fsum(value for values in by_pct.values() for value in values),
                           "by_pct": {pct: [len(values), math.fsum(values)] for pct, values in sorted(by_pct.items())}})
    return result


def _pairing_bias(rows: list[EconRow], total_rows: int) -> dict[str, Any]:
    """So nhóm PAIRED với phần còn lại (trên dòng hợp lệ): tỷ lệ hủy, giá trị đơn, tỷ lệ đến cuối tuần, vị trí trung bình trong thứ tự D5 (`position / số dòng D5`)."""
    paired = [row for row in rows if row.paired]
    others = [row for row in rows if not row.paired]

    def describe(group: list[EconRow]) -> dict[str, Any]:
        n = len(group)
        if n == 0:
            return {"rows": 0, "cancel_rate": None, "mean_price": None, "weekend_share": None, "mean_position_share": None}
        return {"rows": n, "cancel_rate": sum(row.canceled for row in group) / n, "mean_price": math.fsum(row.price for row in group) / n,
                "weekend_share": sum(row.weekend for row in group) / n, "mean_position_share": math.fsum(row.position / total_rows for row in group) / n}

    share = _ratio(len(others), len(rows))
    # reported: có phần không ghép (khối so sánh có nhóm để so) → luôn báo; flag (`pairing_bias_risk`): phần không ghép vượt BIAS_FLAG_SHARE
    return {"reported": bool(others), "flag": share is not None and share > BIAS_FLAG_SHARE, "flag_name": "pairing_bias_risk", "non_paired_share": share,
            "threshold": BIAS_FLAG_SHARE, "threshold_note": BIAS_THRESHOLD_NOTE, "paired": describe(paired), "non_paired": describe(others)}


def summarize(d5_rows: list[dict[str, Any]], policy: cancel_policy.CancelPolicy | None, *, reconciliation: dict[str, Any] | None,
              pairing_counts: dict[str, int], violations: dict[str, int], seed: int = BOOTSTRAP_SEED, iterations: int = BOOTSTRAP_ITERATIONS) -> dict[str, Any]:
    """Toàn bộ chỉ số PT-03 cho `raw/d5/policy_economics.json`. `policy` None (ảnh chụp thiếu/lỗi) → `S-SC*` là INSUFFICIENT_DATA, `S-FULL`/`S-NOSHOW` vẫn chạy."""
    started = clock.monotonic()
    rows, excluded = prepare(d5_rows)
    compared = [row for row in rows if row.paired]
    policies = _scenario_policies(policy)
    refunds = _refund_bounds(compared, policies)
    crosstab, mismatch = _crosstab(rows)
    events = [row for row in compared if row.event]
    result: dict[str, Any] = {
        "schema": SCHEMA, "note": AGGREGATE_NOTE, "s_sc_limit": SC_LIMIT_NOTE,
        "assumptions": {"check_in_local": "14:00", "time_zone": hotel_adapter.BRANCH_TIME_ZONE, "cancel_time_bounds": ["00:00", "23:59"],
                        "bounds": {"low": "hủy lúc 00:00 (nhiều giờ, hoàn nhiều)", "high": "hủy lúc 23:59 (ít giờ, hoàn ít)"},
                        "event": "reservation_status ∈ {Canceled, No-Show}; is_canceled chỉ dùng cho AUC và Δr", "comparison_set": sp.PAIRED,
                        "oracle": "mức cọc = sự kiện giữ cọc (dùng kết quả đã biết), không phải chính sách triển khai được",
                        "need_approval": {"main": NA_MAIN, "lower": NA_LOWER, "applies_to": list(TADSS_POLICIES)}, "variants": list(pm.VARIANTS),
                        "provisional_default_variant": PROVISIONAL_DEFAULT_VARIANT, "scenarios": list(SCENARIOS)},
        "cancel_policy": {"status": INSUFFICIENT_DATA} if policy is None else
                         {"source": policy.source, "retrieved_on": policy.retrieved_on, "sha256": policy.sha256,
                          "tiers": [{"hours_before": t.hours_before, "refund_fraction": str(t.refund_fraction)} for t in policy.tiers],
                          "scenario_hours": {scenario: [t.hours_before for t in scaled.tiers] for scenario, scaled in policies.items()}},
        "counts": {"d5_rows": len(d5_rows), "excluded": excluded, "valid": len(rows), "compared": len(compared), "non_paired": len(rows) - len(compared),
                   "label_mismatch": mismatch,
                   "events": {CANCELED: sum(row.status == CANCELED for row in compared), NO_SHOW: sum(row.status == NO_SHOW for row in compared)},
                   "check_out": sum(row.status == CHECK_OUT for row in compared)},
        "crosstab": crosstab,
        "event_value": {"E": math.fsum(r.price for r in events), "E_canceled": math.fsum(r.price for r in events if r.status == CANCELED),
                        "E_no_show": math.fsum(r.price for r in events if r.status == NO_SHOW)},
        "tier_distribution": _tier_distribution(compared, policies),
        "point": _point(compared, refunds), "burden": _burden(compared), "discrimination": _discrimination(compared),
        "need_approval": _need_approval(compared), "as_is_be": _as_is_be(compared, refunds) if S_SC in refunds else [],
        "pairing_bias": _pairing_bias(rows, len(d5_rows)),
        "shadow": {"reconciliation": reconciliation, "pairing": pairing_counts, "violations": violations},
    }
    result["ci"] = _bootstrap(compared, refunds, seed, iterations) if S_SC in refunds else {"status": INSUFFICIENT_DATA}
    result["ranking"] = _ranking(result["point"], result["ci"], len(compared))
    result["seconds"] = round(clock.monotonic() - started, 2)
    return result
