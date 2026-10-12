"""Quy tắc đạt/không đạt của từng pha LT-03 (O-16, Task 9) tính từ D2 (k6, đúng từng request), bản ghi audit và metric Prometheus. Thuần hàm: không I/O, test bằng dữ liệu mẫu."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timedelta

from evaluator.chaos_schedule import CLEAN_WINDOW_SECONDS, KIND_POSTGRES_DOWN, KIND_REDIS_LATENCY, KIND_STRATEGY_DELAY, KIND_STRATEGY_THROW
from evaluator.clients.postgres import FallbackStats

STATUS_APPROVED = "APPROVED"
TYPE_FALLBACK = "FALLBACK"
TYPE_CACHE_HIT = "CACHE_HIT"
PERSISTENCE_UNAVAILABLE = "PERSISTENCE_UNAVAILABLE"
PIPELINE_BUDGET_MS = 500
ENDPOINT_LIMIT_MS = 700
ENDPOINT_MIN_SHARE = 0.99
# Sau khi bật lỗi cần thời gian để lỗi có hiệu lực (file profile được instance nhận trong ≤ ~5 s theo S-6, request đang bay) nên quan sát bắt đầu muộn hơn
SETTLE_SECONDS = {KIND_STRATEGY_DELAY: 10, KIND_STRATEGY_THROW: 10, KIND_POSTGRES_DOWN: 5, KIND_REDIS_LATENCY: 5}
ALLOWED_REASONS = {
    KIND_STRATEGY_DELAY: {"FALLBACK_STRATEGY_TIMEOUT", "FALLBACK_CIRCUIT_OPEN"},
    KIND_STRATEGY_THROW: {"FALLBACK_STRATEGY_EXCEPTION", "FALLBACK_CIRCUIT_OPEN"},
}


@dataclass(frozen=True)
class Verdict:
    """Kết luận một phần (pha lỗi hoặc pha hồi phục): kỳ vọng, quan sát và đạt hay không."""

    expected: str
    observed: str
    passed: bool


def parse_ts(value: str) -> datetime:
    """`ts` của k6 (`new Date().toISOString()`, hậu tố Z) → datetime có múi giờ."""
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def in_window(entries: list[dict], start: datetime, end: datetime) -> list[dict]:
    return [entry for entry in entries if start <= parse_ts(entry["ts"]) < end]


def _share(count: int, total: int) -> float:
    return count / total if total else 0.0


def evaluate_strategy_fault(kind: str, entries: list[dict], fallback_stats: FallbackStats) -> Verdict:
    """
    FI-02/FI-03: mọi response (trừ CACHE_HIT, không đi qua strategy) là 200 `APPROVED` cọc 100 % `FALLBACK` với lý do tương ứng; pipeline của bản FALLBACK ≤ 500 ms (audit);
    ≥ 99 % response ≤ 700 ms (đo phía client, đã gồm mạng nên chặt hơn đo phía server).
    """
    expected = f"100 % response APPROVED cọc 100 % FALLBACK ({'/'.join(sorted(ALLOWED_REASONS[kind]))}); pipeline FALLBACK ≤ {PIPELINE_BUDGET_MS} ms; ≥ 99 % ≤ {ENDPOINT_LIMIT_MS} ms"
    cached = [entry for entry in entries if entry.get("execution_type") == TYPE_CACHE_HIT]
    judged = [entry for entry in entries if entry.get("execution_type") != TYPE_CACHE_HIT]
    reasons = ALLOWED_REASONS[kind]
    bad = [
        entry for entry in judged
        if not (
            entry["http_status"] == 200
            and entry.get("decision_status") == STATUS_APPROVED
            and entry.get("execution_type") == TYPE_FALLBACK
            and entry.get("deposit_rate") is not None and abs(float(entry["deposit_rate"]) - 1.0) < 1e-9
            and entry.get("reason_code") in reasons
        )
    ]
    fast = sum(1 for entry in entries if entry["duration_ms"] <= ENDPOINT_LIMIT_MS)
    share = _share(fast, len(entries))
    passed = bool(judged) and not bad and fallback_stats.over_budget == 0 and fallback_stats.count > 0 and share >= ENDPOINT_MIN_SHARE
    observed = (
        f"{len(entries)} response ({len(cached)} CACHE_HIT bỏ qua), {len(bad)} không phải fallback đúng; audit: {fallback_stats.count} bản FALLBACK, "
        f"pipeline lớn nhất {fallback_stats.max_ms} ms, {fallback_stats.over_budget} bản > {PIPELINE_BUDGET_MS} ms; {share:.2%} ≤ {ENDPOINT_LIMIT_MS} ms"
    )
    return Verdict(expected, observed, passed)


def evaluate_postgres_fault(entries: list[dict], key_counts: dict[str, int]) -> Verdict:
    """FI-08: mọi response là 503 `PERSISTENCE_UNAVAILABLE`, không có bản ghi dở dang cho các khóa đó."""
    expected = f"100 % response 503 {PERSISTENCE_UNAVAILABLE}; không có bản ghi cho các request bị từ chối"
    bad = [entry for entry in entries if entry["http_status"] != 503 or entry.get("error_code") != PERSISTENCE_UNAVAILABLE]
    ghosts = sum(1 for entry in entries if entry["http_status"] == 503 and key_counts.get(entry.get("idempotency_key"), 0) > 0)
    observed = f"{len(entries)} response, {len(bad)} không phải 503 {PERSISTENCE_UNAVAILABLE}, {ghosts} khóa bị 503 nhưng có bản ghi"
    return Verdict(expected, observed, bool(entries) and not bad and ghosts == 0)


def evaluate_redis_fault(entries: list[dict], bypass_increase: float) -> Verdict:
    """FI-07: Redis chậm không gây FALLBACK hay lỗi 5xx (lỗi cache chỉ là bỏ qua cache) và metric `bypass` tăng."""
    expected = "không có FALLBACK/5xx do cache; tadss_cache_requests_total{result=bypass} tăng"
    bad = [entry for entry in entries if entry["http_status"] != 200 or entry.get("execution_type") == TYPE_FALLBACK]
    observed = f"{len(entries)} response, {len(bad)} lỗi hoặc FALLBACK; bypass tăng {bypass_increase:.0f}"
    return Verdict(expected, observed, bool(entries) and not bad and bypass_increase > 0)


def recovery_from_events(start: datetime, end: datetime, bad_times: list[datetime], deadline_seconds: int, window_seconds: int = CLEAN_WINDOW_SECONDS) -> tuple[bool, float | None]:
    """
    Sau khi gỡ lỗi tại `start`: tìm cửa sổ sạch `window_seconds` đầu tiên (không có sự kiện xấu) trong [start, end). Đạt nếu cửa sổ đó KẾT THÚC không muộn hơn
    `start + deadline_seconds`; trả `(đạt, số giây từ start tới lúc cửa sổ sạch kết thúc)`, không có cửa sổ sạch ⇒ `(False, None)`.
    """
    window = timedelta(seconds=window_seconds)
    marks = [start] + sorted(time for time in bad_times if start <= time < end) + [end]
    for left, right in zip(marks, marks[1:]):
        if right - left >= window:
            seconds = (left + window - start).total_seconds()
            return seconds <= deadline_seconds, seconds
    return False, None


def recovery_from_samples(start_epoch: float, samples: list[tuple[float, float]], deadline_seconds: int) -> tuple[bool, float | None]:
    """
    Chuỗi `increase(...[10s])` theo thời gian (mỗi mẫu đã là tổng của một cửa sổ 10 s kết thúc tại mẫu): mẫu đầu tiên = 0 tại hoặc sau `start_epoch` là cửa sổ sạch.
    NaN (chưa có dữ liệu) không tính là sạch.
    """
    for stamp, value in samples:
        if stamp >= start_epoch and value == 0:
            seconds = stamp - start_epoch
            return seconds <= deadline_seconds, seconds
    return False, None


def recovery_verdict(label: str, deadline_seconds: int, outcome: tuple[bool, float | None]) -> Verdict:
    passed, seconds = outcome
    expected = f"{label} về 0 trong cửa sổ 10 s kết thúc ≤ {deadline_seconds} s sau khi gỡ lỗi"
    observed = "không thấy cửa sổ sạch" if seconds is None else f"cửa sổ sạch kết thúc sau {seconds:.0f} s"
    return Verdict(expected, observed, passed)


# Request đang bay đúng lúc cắt PostgreSQL có thể đã COMMIT thành công nhưng mất kết nối trước khi xác nhận về ứng dụng: ứng dụng báo 503 (không biết kết quả), bản ghi vẫn tồn tại.
# Client thử lại với cùng khóa idempotency sẽ nhận bản đã lưu, nên đây không phải bản ghi dở dang; chỉ tính trong khoảng ngắn sau khi cắt. Sau khoảng đó, 503 mà có bản ghi là vi phạm thật.
UNCERTAIN_COMMIT_GRACE_SECONDS = 2


def _ghost(entry: dict, key_counts: dict[str, int]) -> bool:
    return entry["http_status"] == 503 and entry.get("error_code") == PERSISTENCE_UNAVAILABLE and key_counts.get(entry.get("idempotency_key"), 0) > 0


def uncertain_commits(entries: list[dict], key_counts: dict[str, int], uncertain_until: datetime | None) -> int:
    """Số response 503 có bản ghi nhưng hoàn tất trước `uncertain_until` (cam kết bị cắt trước khi xác nhận)."""
    if uncertain_until is None:
        return 0
    return sum(1 for entry in entries if _ghost(entry, key_counts) and parse_ts(entry["ts"]) < uncertain_until)


def invariant_violations(entries: list[dict], key_counts: dict[str, int], uncertain_until: datetime | None = None) -> int:
    """
    Số response 200 mà khóa có > 1 bản `PRIMARY` + số response 200 không có bản ghi + số response 503 có bản ghi hoàn tất từ `uncertain_until` trở đi
    (`None` ⇒ mọi 503 có bản ghi đều là vi phạm). 503 có bản ghi trước `uncertain_until` là cam kết không chắc chắn, đếm riêng ở `uncertain_commits`.
    """
    duplicated = sum(1 for entry in entries if entry["http_status"] == 200 and key_counts.get(entry.get("idempotency_key"), 0) > 1)
    missing = sum(1 for entry in entries if entry["http_status"] == 200 and key_counts.get(entry.get("idempotency_key"), 0) == 0)
    ghosts = sum(1 for entry in entries if _ghost(entry, key_counts) and (uncertain_until is None or parse_ts(entry["ts"]) >= uncertain_until))
    return duplicated + missing + ghosts
