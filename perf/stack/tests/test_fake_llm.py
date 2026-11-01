# Task 2.30: LLM giả trả đúng khung Gemini generateContent (text và JSON), có usageMetadata, không cần mạng ngoài
import json
import sys
import threading
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer
from pathlib import Path

import pytest

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import fake_llm  # noqa: E402


@pytest.fixture()
def server(monkeypatch):
    monkeypatch.setenv("FAKE_LLM_DELAY_MS", "0")
    monkeypatch.setenv("FAKE_LLM_JITTER_MS", "0")
    httpd = ThreadingHTTPServer(("127.0.0.1", 0), fake_llm.Handler)
    thread = threading.Thread(target=httpd.serve_forever, daemon=True)
    thread.start()
    yield f"http://127.0.0.1:{httpd.server_address[1]}"
    httpd.shutdown()


def _post(url: str, body: dict) -> dict:
    request = urllib.request.Request(url, data=json.dumps(body).encode(), method="POST", headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=5) as response:
        return json.loads(response.read())


def test_text_request_returns_gemini_shaped_text(server):
    body = {"contents": [{"role": "user", "parts": [{"text": "xin chào"}]}]}

    reply = _post(f"{server}/v1beta/models/gemini-3.6-flash:generateContent", body)

    text = reply["candidates"][0]["content"]["parts"][0]["text"]
    assert "LLM giả" in text
    assert reply["candidates"][0]["finishReason"] == "STOP"
    assert reply["modelVersion"] == "gemini-3.6-flash"
    usage = reply["usageMetadata"]
    assert usage["totalTokenCount"] == usage["promptTokenCount"] + usage["candidatesTokenCount"]


def test_json_request_returns_object_with_keys_ai_routers_read(server):
    body = {"contents": "phân tích", "generationConfig": {"responseMimeType": "application/json"}}

    reply = _post(f"{server}/v1beta/models/m:generateContent", body)

    data = json.loads(reply["candidates"][0]["content"]["parts"][0]["text"])
    assert {"reply", "summary", "suggestions", "insights"} <= set(data)
    assert data["suggestions"] == [] and data["insights"] == []


def test_unknown_path_is_404_and_health_is_ok(server):
    with pytest.raises(urllib.error.HTTPError) as error:
        _post(f"{server}/v1beta/models/m:streamGenerateContent", {})
    assert error.value.code == 404
    with urllib.request.urlopen(f"{server}/health", timeout=5) as response:
        assert json.loads(response.read()) == {"status": "ok"}


def test_delay_stays_within_configured_jitter(monkeypatch):
    monkeypatch.setenv("FAKE_LLM_DELAY_MS", "500")
    monkeypatch.setenv("FAKE_LLM_JITTER_MS", "100")
    rng = fake_llm.random.Random(1)

    delays = [fake_llm.delay_seconds(rng) for _ in range(200)]

    assert min(delays) >= 0.4 and max(delays) <= 0.6
