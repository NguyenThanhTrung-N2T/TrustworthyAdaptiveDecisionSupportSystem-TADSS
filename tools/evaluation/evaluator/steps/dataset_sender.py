"""Bộ gửi request quyết định song song có giới hạn cho bước (e) (RP-01, PT-01).

- Mỗi request: `POST {base_url}/api/v2/decisions` với `X-Api-Key`, `X-Idempotency-Key` (khóa của request), `X-Correlation-Id` mới (một UUID cho cả các lần thử lại) và `Content-Type: application/json`.
- Retry theo Q-09: 503/504 được thử lại tối đa `RetryPolicy.max_retries` (mặc định 2) lần, CÙNG khóa idempotency, chờ theo `Retry-After` (giây) chặn trần 5 giây;
  thiếu hoặc sai `Retry-After` thì chờ `default_delay_seconds`. Đây đúng là hành vi BE phải làm ở Phase 6.
- Kết quả theo ĐÚNG thứ tự đầu vào. Lỗi của từng request (timeout, kết nối đứt, 4xx/5xx) nằm trong `SendResult` (status 0 kèm `error` khi không có response HTTP), KHÔNG làm dừng lô.
  Chỉ ném `ServiceUnreachableError` khi không có request nào nhận được response HTTP mà toàn bộ lỗi là không nối được (từ chối kết nối, không phân giải được tên, mạng không tới).
- `iter_send` là chế độ dòng chảy: đầu vào có thể là generator (VD dựng + kiểm schema request kiểu lười, chế độ đầy đủ ~118 nghìn request) và chỉ giữ tối đa `window` request đang bay,
  nên bộ nhớ không tăng theo cỡ lô; trả từng cặp `(entry, SendResult)` theo thứ tự đầu vào. `send_all` gom `iter_send` thành danh sách kết quả.
- `duration_ms` là thời gian của lần thử CUỐI (không gồm thời gian chờ retry); `attempts` là tổng số lần gửi.
"""
from __future__ import annotations

import http.client
import json
import threading
import time
import uuid
from collections import deque
from concurrent.futures import Future, ThreadPoolExecutor
from dataclasses import dataclass
from typing import Any, Callable, Iterable, Iterator
from urllib.parse import urlsplit

from evidence import contract_check

DECISIONS_PATH = "/api/v2/decisions"
DEFAULT_WORKERS = 8
DEFAULT_TIMEOUT_SECONDS = 30.0
RETRY_STATUSES = (503, 504)
RETRY_AFTER_CAP_SECONDS = 5.0
UNREACHABLE_MIN_FAILURES = 3
_STALE_CONNECTION_ERRORS = (http.client.RemoteDisconnected, ConnectionResetError, ConnectionAbortedError, BrokenPipeError, http.client.CannotSendRequest)


class _ConnectFailure(OSError):
    """Không mở được kết nối tới service (từ chối, không phân giải được tên, mạng không tới, hết giờ khi nối): dấu hiệu service không tới được."""


class ServiceUnreachableError(RuntimeError):
    """Service đích không nhận kết nối (lỗi hạ tầng của lượt chạy, khác với lỗi của từng request)."""


@dataclass(frozen=True)
class RetryPolicy:
    """Quy tắc thử lại: `retry_statuses` được gửi lại tối đa `max_retries` lần, chờ `Retry-After` chặn trần `retry_after_cap_seconds`."""

    max_retries: int = 2
    retry_statuses: tuple[int, ...] = RETRY_STATUSES
    retry_after_cap_seconds: float = RETRY_AFTER_CAP_SECONDS
    default_delay_seconds: float = 0.5

    def delay(self, retry_after: str | None) -> float:
        """Thời gian chờ trước lần thử kế tiếp."""
        if retry_after is not None:
            try:
                return max(0.0, min(float(retry_after.strip()), self.retry_after_cap_seconds))
            except ValueError:
                pass
        return min(self.default_delay_seconds, self.retry_after_cap_seconds)


@dataclass(frozen=True)
class SendResult:
    """Kết quả cuối của một request. `status` 0 nghĩa là không có response HTTP (`error` nêu lý do); `body` là JSON đã parse (số thực là `Decimal`) hoặc None."""

    key: str
    status: int
    body: Any
    attempts: int
    duration_ms: float
    decision_id: str | None
    correlation_id: str
    error: str | None


class _Shared:
    """Trạng thái dùng chung giữa các luồng để phát hiện service không tới được."""

    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.answered = False
        self.connect_failures = 0

    def record_answer(self) -> None:
        with self.lock:
            self.answered = True

    def record_connect_failure(self) -> None:
        with self.lock:
            self.connect_failures += 1

    def snapshot(self) -> tuple[bool, int]:
        with self.lock:
            return self.answered, self.connect_failures


class _Connections:
    """Mỗi luồng giữ một kết nối keep-alive riêng; mọi kết nối đã mở được ghi lại để đóng khi lô kết thúc."""

    def __init__(self) -> None:
        self._local = threading.local()
        self._lock = threading.Lock()
        self._opened: set[http.client.HTTPConnection] = set()

    def current(self) -> http.client.HTTPConnection | None:
        return getattr(self._local, "connection", None)

    def open(self, factory: Callable[..., http.client.HTTPConnection], host: str, port: int | None, timeout: float) -> http.client.HTTPConnection:
        connection = factory(host, port, timeout=timeout)
        self._local.connection = connection
        with self._lock:
            self._opened.add(connection)
        return connection

    def drop(self) -> None:
        connection = self.current()
        if connection is not None:
            connection.close()
            with self._lock:
                self._opened.discard(connection)
        self._local.connection = None

    def close_all(self) -> None:
        with self._lock:
            opened, self._opened = self._opened, set()
        for connection in opened:
            connection.close()


def _exchange(connections: _Connections, scheme: str, host: str, port: int | None, headers: dict[str, str], payload: bytes, timeout: float) -> tuple[int, bytes, str | None]:
    """Một lần gửi qua kết nối keep-alive của luồng; kết nối cũ bị server đóng thì mở lại và gửi lại một lần (khóa idempotency bảo vệ khỏi trùng)."""
    for fresh in (False, True):
        connection = connections.current()
        reused = connection is not None
        if connection is None:
            factory = http.client.HTTPSConnection if scheme == "https" else http.client.HTTPConnection
            connection = connections.open(factory, host, port, timeout)
        if connection.sock is None:
            try:
                connection.connect()
            except OSError as error:
                connections.drop()
                raise _ConnectFailure(f"{type(error).__name__}: {error}") from error
        try:
            connection.request("POST", DECISIONS_PATH, body=payload, headers=headers)
            response = connection.getresponse()
            data = response.read()
        except _STALE_CONNECTION_ERRORS:
            connections.drop()
            if reused and not fresh:
                continue
            raise
        except BaseException:
            connections.drop()
            raise
        if response.will_close:
            connections.drop()
        return response.status, data, response.getheader("Retry-After")
    raise AssertionError("không tới được")  # pragma: no cover - vòng lặp luôn return hoặc raise


def _send_one(entry: dict[str, Any], target: tuple[str, str, int | None], api_key: str, retry_policy: RetryPolicy, sleep: Callable[[float], None], timeout: float,
              shared: _Shared, connections: _Connections) -> SendResult:
    key = entry["idempotency_key"]
    correlation_id = str(uuid.uuid4())
    headers = {"Content-Type": "application/json", "X-Api-Key": api_key, "X-Idempotency-Key": key, "X-Correlation-Id": correlation_id}
    payload = json.dumps(entry["body"], ensure_ascii=False).encode("utf-8")
    attempts = 0
    while True:
        attempts += 1
        started = time.perf_counter()
        status, data, retry_after, error = 0, b"", None, None
        try:
            status, data, retry_after = _exchange(connections, *target, headers, payload, timeout)
            shared.record_answer()
        except (OSError, http.client.HTTPException) as exc:
            error = str(exc) if isinstance(exc, _ConnectFailure) else f"{type(exc).__name__}: {exc}"
            if isinstance(exc, _ConnectFailure):
                shared.record_connect_failure()
        duration_ms = (time.perf_counter() - started) * 1000.0
        if status in retry_policy.retry_statuses and attempts <= retry_policy.max_retries:
            sleep(retry_policy.delay(retry_after))
            continue
        break

    body: Any = None
    if data:
        try:
            body = contract_check.parse_json(data)
        except ValueError:
            error = error or "response không phải JSON"
    decision_id = body.get("decisionId") if isinstance(body, dict) and isinstance(body.get("decisionId"), str) else None
    return SendResult(key=key, status=status, body=body, attempts=attempts, duration_ms=round(duration_ms, 3), decision_id=decision_id, correlation_id=correlation_id, error=error)


def iter_send(
    prepared: Iterable[dict[str, Any]],
    base_url: str,
    api_key: str,
    workers: int = DEFAULT_WORKERS,
    retry_policy: RetryPolicy = RetryPolicy(),
    *,
    sleep: Callable[[float], None] = time.sleep,
    timeout: float = DEFAULT_TIMEOUT_SECONDS,
    window: int | None = None,
) -> Iterator[tuple[dict[str, Any], SendResult]]:
    """Gửi lần lượt `prepared` (mỗi phần tử có `idempotency_key` và `body`), tối đa `workers` request đồng thời, và `yield (entry, SendResult)` theo đúng thứ tự đầu vào.

    Tiêu thụ `prepared` kiểu lười: chỉ đọc thêm khi số request đang bay dưới `window` (mặc định 4 × workers). Ném `ServiceUnreachableError` khi service không nhận kết nối."""
    parts = urlsplit(base_url)
    target = (parts.scheme or "http", parts.hostname or "", parts.port)
    workers = max(1, workers)
    limit = max(1, window or workers * 4)
    shared = _Shared()
    connections = _Connections()
    pending: deque[tuple[dict[str, Any], Future[SendResult]]] = deque()
    total = 0

    def settle() -> tuple[dict[str, Any], SendResult]:
        entry, future = pending.popleft()
        result = future.result()
        answered, connect_failures = shared.snapshot()
        if not answered and connect_failures >= max(UNREACHABLE_MIN_FAILURES, workers):
            raise ServiceUnreachableError(f"{base_url} không nhận kết nối ({connect_failures} lần thất bại, chưa có response nào): {result.error}")
        return entry, result

    pool = ThreadPoolExecutor(max_workers=workers, thread_name_prefix="dataset-sender")
    try:
        for entry in prepared:
            total += 1
            pending.append((entry, pool.submit(_send_one, entry, target, api_key, retry_policy, sleep, timeout, shared, connections)))
            while len(pending) >= limit:
                yield settle()
        while pending:
            yield settle()
        answered, connect_failures = shared.snapshot()
        if total and not answered and connect_failures >= total:
            raise ServiceUnreachableError(f"{base_url} không nhận kết nối ({total} request đều không nối được)")
    finally:
        pool.shutdown(wait=True, cancel_futures=True)
        connections.close_all()


def send_all(
    prepared: Iterable[dict[str, Any]],
    base_url: str,
    api_key: str,
    workers: int = DEFAULT_WORKERS,
    retry_policy: RetryPolicy = RetryPolicy(),
    *,
    sleep: Callable[[float], None] = time.sleep,
    timeout: float = DEFAULT_TIMEOUT_SECONDS,
) -> list[SendResult]:
    """Như `iter_send` nhưng gom toàn bộ kết quả theo thứ tự đầu vào."""
    return [result for _, result in iter_send(prepared, base_url, api_key, workers, retry_policy, sleep=sleep, timeout=timeout)]
