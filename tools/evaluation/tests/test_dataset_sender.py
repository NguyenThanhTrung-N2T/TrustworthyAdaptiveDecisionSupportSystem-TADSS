"""Bộ gửi song song có giới hạn (RP-01/PT-01, Q-09): header, retry 503/504 cùng khóa, giữ thứ tự, cô lập lỗi từng request, chỉ ném khi service không tới được.
Server giả là `http.server` thật trên cổng ngẫu nhiên của 127.0.0.1 (không có mạng ngoài)."""
import json
import threading
import time
import uuid
from decimal import Decimal
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import pytest

from evaluator.steps import dataset_sender as sender

API_KEY = "khoa-thu-nghiem"


class FakeServer:
    """Server giả: `script[key]` là danh sách phản hồi (status, header, body) theo thứ tự lần gọi; hết kịch bản (hoặc không có) thì trả 200 với decisionId theo khóa."""

    def __init__(self, script: dict[str, list[tuple]] | None = None, delays: dict[str, float] | None = None, base_delay: float = 0.0) -> None:
        self.script = script or {}
        self.delays = delays or {}
        self.base_delay = base_delay
        self.calls: list[dict] = []
        self.lock = threading.Lock()
        self.active = 0
        self.max_active = 0
        outer = self

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *args) -> None:
                return

            def do_POST(self) -> None:
                length = int(self.headers.get("Content-Length", "0"))
                body = self.rfile.read(length)
                key = self.headers.get("X-Idempotency-Key", "")
                with outer.lock:
                    outer.active += 1
                    outer.max_active = max(outer.max_active, outer.active)
                    index = sum(1 for call in outer.calls if call["key"] == key)
                    outer.calls.append({"key": key, "headers": dict(self.headers), "body": body, "path": self.path})
                try:
                    time.sleep(outer.delays.get(key, outer.base_delay))
                    responses = outer.script.get(key, [])
                    status, headers, payload = responses[index] if index < len(responses) else (200, {}, {"decisionId": f"dec-{key}"})
                    data = payload if isinstance(payload, bytes) else json.dumps(payload).encode("utf-8")
                    self.send_response(status)
                    self.send_header("Content-Type", "application/json")
                    self.send_header("Content-Length", str(len(data)))
                    for name, value in headers.items():
                        self.send_header(name, value)
                    self.end_headers()
                    self.wfile.write(data)
                except (BrokenPipeError, ConnectionResetError):
                    pass
                finally:
                    with outer.lock:
                        outer.active -= 1

        self.httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.httpd.daemon_threads = True
        self.thread = threading.Thread(target=lambda: self.httpd.serve_forever(poll_interval=0.02), daemon=True)

    @property
    def url(self) -> str:
        return f"http://127.0.0.1:{self.httpd.server_address[1]}"

    def __enter__(self) -> "FakeServer":
        self.thread.start()
        return self

    def __exit__(self, *exc) -> None:
        self.httpd.shutdown()
        self.httpd.server_close()


def entries(count: int) -> list[dict]:
    return [{"idempotency_key": f"k{i}", "body": {"reservationId": f"r{i}"}, "meta": {}} for i in range(count)]


def test_sender_sends_required_headers_and_json_body() -> None:
    with FakeServer() as server:
        results = sender.send_all(entries(5), server.url, API_KEY, workers=2)

    assert [r.status for r in results] == [200] * 5
    correlation_ids = set()
    for call in server.calls:
        headers = {name.lower(): value for name, value in call["headers"].items()}
        assert headers["x-api-key"] == API_KEY and headers["content-type"].startswith("application/json")
        assert headers["x-idempotency-key"] == call["key"]
        correlation_ids.add(str(uuid.UUID(headers["x-correlation-id"])))
        assert call["path"] == "/api/v2/decisions"
        assert json.loads(call["body"])["reservationId"] == "r" + call["key"][1:]
    assert len(correlation_ids) == 5, "mỗi request một correlation id mới"
    assert {r.correlation_id for r in results} == correlation_ids


def test_sender_result_fields() -> None:
    with FakeServer() as server:
        (result,) = sender.send_all(entries(1), server.url, API_KEY, workers=1)

    assert (result.key, result.status, result.attempts, result.decision_id, result.error) == ("k0", 200, 1, "dec-k0", None)
    assert result.body["decisionId"] == "dec-k0" and result.duration_ms >= 0


def test_sender_parses_decimals_exactly() -> None:
    script = {"k0": [(200, {}, b'{"decisionId": "d", "deposit": {"amount": 40.0, "rate": 0.50}}')]}
    with FakeServer(script) as server:
        (result,) = sender.send_all(entries(1), server.url, API_KEY, workers=1)

    assert result.body["deposit"]["amount"] == Decimal("40.0") and str(result.body["deposit"]["amount"]) == "40.0"


def test_sender_retries_same_key() -> None:
    script = {"k0": [(503, {"Retry-After": "1"}, {"code": "PERSISTENCE_UNAVAILABLE"}), (200, {}, {"decisionId": "dec-ok"})]}
    sleeps: list[float] = []
    with FakeServer(script) as server:
        (result,) = sender.send_all(entries(1), server.url, API_KEY, workers=1, sleep=sleeps.append)

    assert (result.status, result.attempts, result.decision_id) == (200, 2, "dec-ok")
    assert [call["key"] for call in server.calls] == ["k0", "k0"]
    assert sleeps == [1.0]
    correlation = {call["headers"]["X-Correlation-Id"] for call in server.calls}
    assert len(correlation) == 1 and result.correlation_id in correlation


def test_sender_retry_after_is_capped_and_gives_up_after_two_retries() -> None:
    script = {"k0": [(504, {"Retry-After": "60"}, {})] * 5}
    sleeps: list[float] = []
    with FakeServer(script) as server:
        (result,) = sender.send_all(entries(1), server.url, API_KEY, workers=1, sleep=sleeps.append)

    assert (result.status, result.attempts) == (504, 3)
    assert sleeps == [5.0, 5.0], "Retry-After bị chặn trần 5 giây"
    assert len(server.calls) == 3


def test_sender_does_not_retry_other_errors() -> None:
    with FakeServer({"k0": [(400, {}, {"code": "VALIDATION_ERROR"}), (200, {}, {})]}) as server:
        (result,) = sender.send_all(entries(1), server.url, API_KEY, workers=1, sleep=lambda seconds: pytest.fail("không được chờ"))

    assert (result.status, result.attempts, result.body["code"]) == (400, 1, "VALIDATION_ERROR")
    assert len(server.calls) == 1


def test_sender_missing_retry_after_uses_default_delay() -> None:
    sleeps: list[float] = []
    policy = sender.RetryPolicy(default_delay_seconds=0.25)
    with FakeServer({"k0": [(503, {}, {})]}) as server:
        (result,) = sender.send_all(entries(1), server.url, API_KEY, workers=1, retry_policy=policy, sleep=sleeps.append)

    assert result.status == 200 and result.attempts == 2 and sleeps == [0.25]


def test_sender_preserves_order_and_isolates_errors() -> None:
    script = {f"k{i}": [(400, {}, {"code": "VALIDATION_ERROR"})] for i in (3, 11, 40)}
    delays = {"k7": 1.0, "k20": 0.2}  # k7 vượt timeout 0.6 s
    with FakeServer(script, delays) as server:
        results = sender.send_all(entries(50), server.url, API_KEY, workers=8, timeout=0.6, sleep=lambda seconds: None)

    assert [r.key for r in results] == [f"k{i}" for i in range(50)], "thứ tự đầu vào được giữ"
    by_key = {r.key: r for r in results}
    assert [by_key[f"k{i}"].status for i in (3, 11, 40)] == [400, 400, 400]
    assert by_key["k7"].status == 0 and by_key["k7"].error and by_key["k7"].body is None, "timeout là kết quả của riêng request đó"
    assert sum(1 for r in results if r.status == 200) == 46
    assert by_key["k20"].status == 200


def test_sender_never_exceeds_worker_limit() -> None:
    with FakeServer(base_delay=0.03) as server:
        results = sender.send_all(entries(40), server.url, API_KEY, workers=3)

    assert all(r.status == 200 for r in results)
    assert 2 <= server.max_active <= 3


def test_sender_raises_when_service_unreachable() -> None:
    with FakeServer() as server:
        dead_url = server.url
    with pytest.raises(sender.ServiceUnreachableError):
        sender.send_all(entries(20), dead_url, API_KEY, workers=4, timeout=0.5)


def test_sender_streaming_consumes_generator_lazily() -> None:
    produced = {"count": 0}

    def generate():
        for entry in entries(400):
            produced["count"] += 1
            yield entry

    with FakeServer() as server:
        stream = sender.iter_send(generate(), server.url, API_KEY, workers=4)
        first_entry, first_result = next(stream)
        seen_when_first_arrived = produced["count"]
        rest = list(stream)

    assert first_entry["idempotency_key"] == first_result.key == "k0"
    assert seen_when_first_arrived < 100, "không dựng hết 400 request trước khi có kết quả đầu"
    assert produced["count"] == 400 and len(rest) == 399
    assert [entry["idempotency_key"] for entry, _ in rest] == [f"k{i}" for i in range(1, 400)]


def test_sender_empty_input_returns_empty() -> None:
    assert sender.send_all([], "http://127.0.0.1:1", API_KEY, workers=2) == []
