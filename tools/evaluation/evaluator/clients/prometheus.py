"""Client Prometheus qua proxy datasource của Grafana (spike S-1/S-2): truy vấn tức thời để kiểm có số liệu trong một cửa sổ chạy."""
from __future__ import annotations

import json
import urllib.parse
from datetime import datetime
from typing import Callable

from evaluator.clients.http import http_get

Fetch = Callable[[str], tuple[int, str]]


class PrometheusError(RuntimeError):
    """Truy vấn không thực hiện được (mạng, HTTP hoặc JSON sai)."""


def query(base_url: str, promql: str, at: datetime | None = None, fetch: Fetch = http_get) -> list[dict]:
    """`GET {base}/api/v1/query`; trả danh sách `result` (có thể rỗng)."""
    params = {"query": promql}
    if at is not None:
        params["time"] = f"{at.timestamp():.3f}"
    status, body = fetch(f"{base_url.rstrip('/')}/api/v1/query?{urllib.parse.urlencode(params)}")
    if status != 200:
        raise PrometheusError(f"Prometheus trả {status}: {body[:200]}")
    try:
        document = json.loads(body)
    except json.JSONDecodeError as error:
        raise PrometheusError(f"Phản hồi không phải JSON: {error}") from error
    if document.get("status") != "success":
        raise PrometheusError(f"Truy vấn thất bại: {document}")
    return document["data"]["result"]


def increase_over(base_url: str, metric: str, start: datetime, end: datetime, fetch: Fetch = http_get) -> float:
    """Tổng mức tăng của counter `metric` (mọi series) trong [start, end], đánh giá tại `end`; không có series ⇒ 0."""
    seconds = max(1, int((end - start).total_seconds()) + 1)
    result = query(base_url, f"sum(increase({metric}[{seconds}s]))", at=end, fetch=fetch)
    return float(result[0]["value"][1]) if result else 0.0


def query_range(base_url: str, promql: str, start: datetime, end: datetime, step_seconds: int, fetch: Fetch = http_get) -> list[dict]:
    """`GET {base}/api/v1/query_range`; mỗi phần tử `{metric, values: [[epoch_s, "giá trị"], ...]}`."""
    params = {"query": promql, "start": f"{start.timestamp():.3f}", "end": f"{end.timestamp():.3f}", "step": str(step_seconds)}
    status, body = fetch(f"{base_url.rstrip('/')}/api/v1/query_range?{urllib.parse.urlencode(params)}")
    if status != 200:
        raise PrometheusError(f"Prometheus trả {status}: {body[:200]}")
    try:
        document = json.loads(body)
    except json.JSONDecodeError as error:
        raise PrometheusError(f"Phản hồi không phải JSON: {error}") from error
    if document.get("status") != "success":
        raise PrometheusError(f"Truy vấn thất bại: {document}")
    return document["data"]["result"]


def series_values(result: list[dict]) -> list[tuple[float, float]]:
    """Gộp mọi series thành một chuỗi `(epoch_s, tổng)` theo thời điểm (hợp lệ khi truy vấn đã `sum`, còn nhiều series thì cộng lại)."""
    totals: dict[float, float] = {}
    for series in result:
        for stamp, value in series["values"]:
            totals[float(stamp)] = totals.get(float(stamp), 0.0) + float(value)
    return sorted(totals.items())


def increase_by_label(base_url: str, metric: str, label: str, start: datetime, end: datetime, fetch: Fetch = http_get) -> dict[str, float]:
    """Mức tăng của counter `metric` trong [start, end] gộp theo nhãn `label` (ví dụ instance); nhãn không có số liệu thì vắng mặt."""
    seconds = max(1, int((end - start).total_seconds()) + 1)
    result = query(base_url, f"sum by ({label}) (increase({metric}[{seconds}s]))", at=end, fetch=fetch)
    return {item["metric"].get(label, ""): float(item["value"][1]) for item in result}
