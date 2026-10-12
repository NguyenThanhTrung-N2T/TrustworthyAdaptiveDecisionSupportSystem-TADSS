"""Client REST của Toxiproxy (cổng điều khiển 8474): bật/tắt proxy, thêm/gỡ toxic độ trễ, kiểm trạng thái sạch (FI-07, FI-08, cuối LT-03)."""
from __future__ import annotations

import json
from typing import Callable

from evaluator.clients.http import Send, http_request

LATENCY_TOXIC = "latency"


class ToxiproxyError(RuntimeError):
    """Toxiproxy trả lỗi hoặc không tới được."""


class ToxiproxyClient:
    def __init__(self, base_url: str, send: Send | Callable[[str, str, object | None], tuple[int, str]] = lambda m, u, b: http_request(m, u, b)) -> None:
        self._base = base_url.rstrip("/")
        self._send = send

    def _call(self, method: str, path: str, body: object | None = None, ok: tuple[int, ...] = (200, 201, 204)) -> str:
        status, text = self._send(method, f"{self._base}{path}", body)
        if status not in ok:
            raise ToxiproxyError(f"{method} {path} → {status}: {text[:200]}")
        return text

    def set_enabled(self, proxy: str, enabled: bool) -> None:
        """Bật/tắt hẳn proxy: tắt thì kết nối hiện có bị đóng và kết nối mới bị từ chối (FI-08)."""
        self._call("POST", f"/proxies/{proxy}", {"enabled": enabled})

    def add_latency(self, proxy: str, latency_ms: int) -> None:
        """Toxic độ trễ ở chiều phản hồi (từ upstream về TADSS), toxicity 1.0 (FI-07)."""
        body = {"name": LATENCY_TOXIC, "type": "latency", "stream": "downstream", "toxicity": 1.0, "attributes": {"latency": latency_ms, "jitter": 0}}
        self._call("POST", f"/proxies/{proxy}/toxics", body)

    def remove_latency(self, proxy: str) -> None:
        self._call("DELETE", f"/proxies/{proxy}/toxics/{LATENCY_TOXIC}", ok=(200, 204, 404))

    def reset(self) -> None:
        """Bật lại mọi proxy và gỡ mọi toxic."""
        self._call("POST", "/reset")

    def problems(self) -> list[str]:
        """Những gì còn lệch khỏi trạng thái sạch (proxy tắt, còn toxic); rỗng = sạch."""
        proxies = json.loads(self._call("GET", "/proxies"))
        found = []
        for name, proxy in sorted(proxies.items()):
            if not proxy.get("enabled", False):
                found.append(f"proxy {name} đang tắt")
            for toxic in proxy.get("toxics") or []:
                found.append(f"proxy {name} còn toxic {toxic.get('name')}")
        return found
