"""Xả hàng đợi shadow của instance PT và đối soát tổng hợp cho PT-04 (gói 5.7b, spec 4.2).

Counter `tadss.shadow.*` tích lũy theo vòng đời process, nên dùng HIỆU giữa ảnh chụp trước khi gửi và lúc xả xong; counter cuối < đầu = process khởi động lại → UNKNOWN.
Đọc Prometheus lỗi (`Counters.error`) khác series vắng: baseline lỗi → UNKNOWN/baseline_unavailable, sau khi xả lỗi → UNKNOWN/prometheus_unavailable.
Series vắng: trước khi gửi coi là 0; sau khi xả, `failed`/`dropped` vắng là 0 khi `recorded` có mặt (pipeline metric của instance đang chạy), `recorded` vắng → UNKNOWN.
Phạm vi lượt theo tập correlation_id của lượt (truy vấn của bước gọi). Phương trình khớp chỉ là đối soát TỔNG HỢP, không phải bằng chứng từng request đã được xử lý;
bằng chứng theo request là trạng thái ghép cặp ở bước (f).

Tên metric Prometheus và nhãn `instance` trong các hằng `*_QUERY` đã xác nhận trên lượt quick thật `EV-20261015-EVAL-quick-r2`; truy vấn chỉ lấy series còn
được xuất (`LIVE_SERIES_WINDOW`) để instance vừa bị tạo lại không mang số của tiến trình cũ vào ảnh chụp (cô lập ở đây để chỉ sửa một chỗ).
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import timedelta
from typing import Any, Callable

RECONCILED, UNKNOWN, FAILED = "RECONCILED", "UNKNOWN", "FAILED"
AGGREGATE_STATEMENT = "Đối soát tổng hợp (DB + counter), không phải bằng chứng tuyệt đối về từng tác vụ; bằng chứng theo request là trạng thái ghép cặp theo correlation_id."
POLL_SECONDS = 2.0
STALL_SECONDS = 30.0
MAX_SECONDS = 600.0
PREFILTER_MARGIN = timedelta(minutes=5)
PT_INSTANCE = "tadss-pt"
# Chỉ tính series còn được xuất trong cửa sổ ngắn (3 × chu kỳ xuất metric 5 s của instance PT): truy vấn tức thời trần vẫn trả mẫu cuối của tiến trình
# đã dừng trong 5 phút (lookback của Prometheus), nên instance vừa bị tạo lại trước lượt sẽ cho ảnh chụp "trước" là số của tiến trình cũ
LIVE_SERIES_WINDOW = "15s"
RECORDED_QUERY = f'sum(last_over_time(tadss_shadow_executions_total{{outcome="recorded",instance="{PT_INSTANCE}"}}[{LIVE_SERIES_WINDOW}]))'
FAILED_QUERY = f'sum(last_over_time(tadss_shadow_executions_total{{outcome="failed",instance="{PT_INSTANCE}"}}[{LIVE_SERIES_WINDOW}]))'
DROPPED_QUERY = f'sum(last_over_time(tadss_shadow_dropped_total{{instance="{PT_INSTANCE}"}}[{LIVE_SERIES_WINDOW}]))'


def count_roles_sql() -> str:
    """Đếm PRIMARY/SHADOW của lượt: lọc thô theo thời gian (đã lùi PREFILTER_MARGIN ở nơi gọi) rồi theo tập correlation_id của lượt."""
    return "SELECT role, count(*) FROM tadss.decision_executions WHERE created_at >= %s AND correlation_id = ANY(%s) GROUP BY role"


@dataclass(frozen=True)
class Counters:
    recorded: float | None
    failed: float | None
    dropped: float | None
    error: bool = False  # True = đọc Prometheus LỖI (mạng/HTTP/JSON sai), khác "series vắng" (None với error=False)

    def to_dict(self) -> dict[str, float | bool | None]:
        values: dict[str, float | bool | None] = {"recorded": self.recorded, "failed": self.failed, "dropped": self.dropped}
        if self.error:
            values["error"] = True
        return values


def _value(result: list[dict]) -> float | None:
    return float(result[0]["value"][1]) if result else None


def read_counters(query: Callable[[str], list[dict]]) -> Counters:
    """Đọc ba counter (tổng theo instance PT); series vắng → None."""
    return Counters(_value(query(RECORDED_QUERY)), _value(query(FAILED_QUERY)), _value(query(DROPPED_QUERY)))


def _delta(before: Counters, after: Counters) -> tuple[dict[str, int] | None, str | None]:
    if after.error or after.recorded is None:
        return None, "prometheus_unavailable"
    if before.error:  # baseline đọc lỗi ≠ series vắng (=0): không có mốc tin cậy để lấy hiệu
        return None, "baseline_unavailable"
    start = {name: value or 0.0 for name, value in (("recorded", before.recorded), ("failed", before.failed), ("dropped", before.dropped))}
    end = {"recorded": after.recorded, "failed": after.failed or 0.0, "dropped": after.dropped or 0.0}
    if any(end[name] < start[name] for name in end):
        return None, "counter_reset"
    return {name: int(round(end[name] - start[name])) for name in end}, None


def drain(count_roles: Callable[[], dict[str, int]], read: Callable[[], Counters], before: Counters, *,
          monotonic: Callable[[], float], sleep: Callable[[float], None]) -> dict[str, Any]:
    """Thăm dò đến khi đối soát khớp, hoặc `shadow` đứng yên STALL_SECONDS, hoặc quá MAX_SECONDS; trả kết quả có nhãn RECONCILED/UNKNOWN/FAILED."""
    started = monotonic()
    last_marker, last_progress = None, started
    while True:
        roles, after = count_roles(), read()
        primary, shadow = roles.get("PRIMARY", 0), roles.get("SHADOW", 0)
        delta, problem = _delta(before, after)
        done = delta is not None and delta["recorded"] == shadow and shadow + delta["failed"] + delta["dropped"] == primary
        now = monotonic()
        marker = (shadow, delta["recorded"] if delta else None)  # tiến triển = số dòng SHADOW trong DB đổi HOẶC counter `recorded` đổi (export counter có thể trễ so với DB)
        if marker != last_marker:
            last_marker, last_progress = marker, now
        if done or now - last_progress >= STALL_SECONDS or now - started >= MAX_SECONDS:
            break
        sleep(POLL_SECONDS)
    result: dict[str, Any] = {"primary": primary, "shadow": shadow, "delta": delta, "before": before.to_dict(), "after": after.to_dict(),
                              "seconds": round(monotonic() - started, 1), "statement": AGGREGATE_STATEMENT}
    if delta is None:
        return {**result, "label": UNKNOWN, "reason": problem, "residual": None}
    residual = primary - shadow - delta["failed"] - delta["dropped"]
    if delta["recorded"] != shadow:
        return {**result, "label": FAILED, "reason": "counter_mismatch", "residual": residual}
    if done:
        return {**result, "label": RECONCILED, "reason": None, "residual": 0}
    return {**result, "label": UNKNOWN, "reason": "timeout", "residual": residual}
