"""Client Grafana của stack đánh giá: kiểm OB-2 (dashboard đã nạp). API Grafana của otel-lgtm dùng được không cần đăng nhập (spike S-1)."""
from __future__ import annotations

import json
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from typing import Callable

Fetch = Callable[[str], tuple[int, str]]
DASHBOARD_UID = "tadss-runtime"
DASHBOARD_FOLDER = "TADSS"


def http_get(url: str, timeout: float = 20.0) -> tuple[int, str]:
    """GET đơn giản: (mã HTTP, thân); lỗi mạng ⇒ (0, thông báo)."""
    try:
        with urllib.request.urlopen(url, timeout=timeout) as response:  # noqa: S310 - địa chỉ nội bộ do compose đặt
            return response.status, response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode("utf-8", "replace")
    except (urllib.error.URLError, OSError) as error:
        return 0, str(error)


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
