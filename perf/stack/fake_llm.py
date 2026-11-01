"""LLM giả của môi trường perf (Epic 6C, task 2.30): trả lời như Gemini `generateContent` để SmashCourt-AI chạy đủ luồng mà không gọi Google.

AI trỏ vào đây bằng GEMINI_BASE_URL=http://fake-llm:8080. Độ trễ cố định FAKE_LLM_DELAY_MS (mặc định 800) cộng lệch ngẫu nhiên
±FAKE_LLM_JITTER_MS (mặc định 200) để giống lời gọi thật; bài đo SP-10 báo phần xử lý của BE + AI, không gồm độ trễ này.
Yêu cầu JSON (responseMimeType application/json) ⇒ một object có các khóa mà router AI đọc bằng `data.get(...)`, danh sách để rỗng.
Chỉ thư viện chuẩn: chạy thẳng trên image python, không build.
"""
from __future__ import annotations

import json
import os
import random
import re
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

_PATH = re.compile(r"^/v1(?:beta|alpha)?/models/([^/:]+):generateContent")
_TEXT = "Đây là câu trả lời của LLM giả trong môi trường đo hiệu năng SmashCourt; nội dung không mang nghĩa nghiệp vụ."
_JSON_REPLY = {
    "reply": _TEXT,
    "summary": _TEXT,
    "suggestions": [],
    "insights": [],
    "branch_performances": [],
    "staffing_recommendations": [],
}


def wants_json(body: dict) -> bool:
    config = body.get("generationConfig") or body.get("generation_config") or {}
    return (config.get("responseMimeType") or config.get("response_mime_type")) == "application/json"


def prompt_tokens(body: dict) -> int:
    # Ước lượng như Gemini: khoảng 4 ký tự một token
    return max(1, len(json.dumps(body.get("contents", ""), ensure_ascii=False)) // 4)


def build_response(model: str, body: dict) -> dict:
    text = json.dumps(_JSON_REPLY, ensure_ascii=False) if wants_json(body) else _TEXT
    prompt = prompt_tokens(body)
    output = max(1, len(text) // 4)
    return {
        "candidates": [{"content": {"role": "model", "parts": [{"text": text}]}, "finishReason": "STOP", "index": 0}],
        "usageMetadata": {"promptTokenCount": prompt, "candidatesTokenCount": output, "totalTokenCount": prompt + output},
        "modelVersion": model,
    }


def delay_seconds(rng: random.Random) -> float:
    base = int(os.environ.get("FAKE_LLM_DELAY_MS", "800"))
    jitter = int(os.environ.get("FAKE_LLM_JITTER_MS", "200"))
    return max(0, base + rng.randint(-jitter, jitter)) / 1000


class Handler(BaseHTTPRequestHandler):
    rng = random.Random(int(os.environ.get("FAKE_LLM_SEED", "42")))

    def _send(self, status: int, payload: dict) -> None:
        raw = json.dumps(payload, ensure_ascii=False).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def do_GET(self) -> None:  # noqa: N802 (tên hàm do http.server quy định)
        if self.path == "/health":
            self._send(200, {"status": "ok"})
        else:
            self._send(404, {"error": {"code": 404, "message": "không có"}})

    def do_POST(self) -> None:  # noqa: N802
        match = _PATH.match(self.path)
        if not match:
            self._send(404, {"error": {"code": 404, "message": f"LLM giả chỉ có generateContent, không có {self.path}"}})
            return
        length = int(self.headers.get("Content-Length") or 0)
        try:
            body = json.loads(self.rfile.read(length) or b"{}")
        except json.JSONDecodeError:
            self._send(400, {"error": {"code": 400, "message": "body không phải JSON"}})
            return
        time.sleep(delay_seconds(self.rng))
        self._send(200, build_response(match.group(1), body))

    def log_message(self, format: str, *args: object) -> None:  # noqa: A002
        return  # không ghi log từng request: lượt đo tạo hàng chục nghìn dòng


def main() -> None:
    port = int(os.environ.get("FAKE_LLM_PORT", "8080"))
    server = ThreadingHTTPServer(("0.0.0.0", port), Handler)
    server.daemon_threads = True
    print(f"LLM giả nghe cổng {port}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
