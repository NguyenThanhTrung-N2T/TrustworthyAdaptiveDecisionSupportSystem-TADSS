"""HTTP tối giản dùng chung cho các client của evaluator (địa chỉ nội bộ do compose đặt)."""
from __future__ import annotations

import json
import urllib.error
import urllib.request
from typing import Callable

Fetch = Callable[[str], tuple[int, str]]
Send = Callable[[str, str, object | None], tuple[int, str]]


def http_request(method: str, url: str, body: object | None = None, timeout: float = 20.0) -> tuple[int, str]:
    """Gửi request (thân JSON nếu có): (mã HTTP, thân); lỗi mạng ⇒ (0, thông báo)."""
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(url, data=data, method=method, headers={"Content-Type": "application/json"} if data is not None else {})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:  # noqa: S310 - địa chỉ nội bộ do compose đặt
            return response.status, response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode("utf-8", "replace")
    except (urllib.error.URLError, OSError) as error:
        return 0, str(error)


def http_get(url: str, timeout: float = 20.0) -> tuple[int, str]:
    """GET đơn giản: (mã HTTP, thân); lỗi mạng ⇒ (0, thông báo)."""
    return http_request("GET", url, None, timeout)
