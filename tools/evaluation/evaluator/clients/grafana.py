"""Client Grafana của stack đánh giá: kiểm OB-2 (dashboard đã nạp), annotation `chaos` và snapshot JSON dashboard (OB-3, không lưu ảnh). API Grafana của otel-lgtm dùng được không cần đăng nhập (spike S-1)."""
from __future__ import annotations

import json
import time
from dataclasses import dataclass
from typing import Callable

from evaluator.clients.http import Fetch, Send, http_get, http_request

DASHBOARD_UID = "tadss-runtime"
DASHBOARD_FOLDER = "TADSS"


@dataclass(frozen=True)
class DashboardCheck:
    """Kết quả OB-2 trên stack đánh giá."""

    passed: bool
    detail: str


def check_dashboard(
    base_url: str,
    fetch: Fetch = http_get,
    wait_seconds: float = 120.0,
    interval_seconds: float = 3.0,
    sleep: Callable[[float], None] = time.sleep,
) -> DashboardCheck:
    """
    OB-2: `GET /api/dashboards/uid/tadss-runtime` trả 200, đúng uid và nằm trong thư mục TADSS. Provisioning quét mỗi 30 giây nên thử lại tới `wait_seconds`.
    """
    url = f"{base_url.rstrip('/')}/api/dashboards/uid/{DASHBOARD_UID}"
    deadline = time.monotonic() + wait_seconds
    status, body = fetch(url)
    while status != 200 and time.monotonic() < deadline:
        sleep(interval_seconds)
        status, body = fetch(url)
    if status != 200:
        return DashboardCheck(False, f"GET {url} → {status}")
    try:
        document = json.loads(body)
    except json.JSONDecodeError:
        return DashboardCheck(False, "phản hồi không phải JSON")
    uid = document.get("dashboard", {}).get("uid")
    folder = document.get("meta", {}).get("folderTitle")
    if uid != DASHBOARD_UID or folder != DASHBOARD_FOLDER:
        return DashboardCheck(False, f"uid={uid!r} thư mục={folder!r} (cần {DASHBOARD_UID!r} / {DASHBOARD_FOLDER!r})")
    return DashboardCheck(True, f"dashboard {DASHBOARD_UID} nạp vào thư mục {DASHBOARD_FOLDER}")


class GrafanaError(RuntimeError):
    """Gọi API Grafana không thành công."""


def annotate(base_url: str, text: str, tags: list[str], at_ms: int, send: Send = lambda m, u, b: http_request(m, u, b)) -> None:
    """Annotation điểm trên dashboard TADSS (tag `chaos` + mã FI); dashboard lọc theo tag nên thấy ngay trên mọi panel thời gian."""
    status, body = send("POST", f"{base_url.rstrip('/')}/api/annotations", {"dashboardUID": DASHBOARD_UID, "time": at_ms, "tags": tags, "text": text})
    if status != 200:
        raise GrafanaError(f"POST /api/annotations → {status}: {body[:200]}")


def prometheus_targets(dashboard: dict) -> list[dict]:
    """Mọi panel (kể cả lồng trong hàng) có truy vấn PromQL: `{id, title, type, queries: [{refId, expr, legend}]}`; panel Loki/Tempo/không truy vấn bị bỏ qua."""
    found: list[dict] = []

    def visit(panels: list[dict]) -> None:
        for panel in panels:
            visit(panel.get("panels", []))
            queries = [
                {"refId": target.get("refId", ""), "expr": target["expr"], "legend": target.get("legendFormat", "")}
                for target in panel.get("targets", [])
                if target.get("expr") and (target.get("datasource") or panel.get("datasource") or {}).get("type") == "prometheus"
            ]
            if queries:
                found.append({"id": panel["id"], "title": panel.get("title", ""), "type": panel.get("type", ""), "queries": queries})

    visit(dashboard.get("panels", []))
    return found


def build_snapshot(dashboard_document: dict, window: dict, run_query: Callable[[str], list[dict]]) -> dict:
    """
    JSON snapshot (OB-3): dashboard gốc + kết quả `query_range` của từng truy vấn PromQL trong cửa sổ `window` (`start`, `end`, `step` giây, ISO 8601).
    Biến `$instance` thay bằng `.*` (cùng giá trị "All" của dashboard); truy vấn lỗi ghi vào `error` của truy vấn, không làm hỏng snapshot.
    """
    panels = []
    for panel in prometheus_targets(dashboard_document["dashboard"]):
        entries = []
        for query in panel["queries"]:
            expr = query["expr"].replace("$instance", ".*")
            try:
                entries.append({**query, "expr": expr, "result": run_query(expr)})
            except Exception as error:  # noqa: BLE001 - một truy vấn lỗi không được làm mất cả snapshot
                entries.append({**query, "expr": expr, "result": [], "error": f"{type(error).__name__}: {error}"})
        panels.append({**panel, "queries": entries})
    return {"window": window, "dashboard": dashboard_document["dashboard"], "meta": dashboard_document.get("meta", {}), "panels": panels}


def fetch_dashboard(base_url: str, fetch: Fetch = http_get) -> dict:
    status, body = fetch(f"{base_url.rstrip('/')}/api/dashboards/uid/{DASHBOARD_UID}")
    if status != 200:
        raise GrafanaError(f"GET dashboard → {status}")
    return json.loads(body)
