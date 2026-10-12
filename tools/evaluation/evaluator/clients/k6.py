"""Chạy k6 (O-15): lệnh, biến môi trường và chuyển file console của k6 thành requests.jsonl."""
from __future__ import annotations

import subprocess
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Callable

from evaluator import d2
from evaluator.results import utc_now

OTEL_PREFIX = "k6_"
OTEL_EXPORT_INTERVAL = "5s"


@dataclass(frozen=True)
class K6Request:
    """Một lần chạy k6: kịch bản, chế độ và nơi ghi kết quả."""

    script: Path
    scenario: str
    mode: str
    base_url: str
    api_key: str
    run_id: str
    out_dir: Path
    otlp_endpoint: str | None = None  # host:port gRPC của collector; None ⇒ không đẩy metric


@dataclass(frozen=True)
class K6Result:
    returncode: int
    started_at: datetime
    ended_at: datetime
    requests_path: Path
    summary_path: Path
    request_count: int


Runner = Callable[[list[str], dict[str, str], Path], int]


def build_command(request: K6Request) -> list[str]:
    """`k6 run [--out opentelemetry] --console-output <file> --quiet <script>`; metric k6 chỉ đẩy khi có collector."""
    command = ["k6", "run", "--quiet", "--console-output", str(request.out_dir / "console.log")]
    if request.otlp_endpoint:
        command += ["--out", "opentelemetry"]
    return command + [str(request.script)]


def build_env(request: K6Request, base_env: dict[str, str]) -> dict[str, str]:
    """Biến của kịch bản (BASE_URL, API_KEY, RUN_ID, SCENARIO, MODE, SUMMARY_PATH) và của output OpenTelemetry (S-3: tiền tố k6_, gRPC không TLS)."""
    env = {
        **base_env,
        "BASE_URL": request.base_url,
        "API_KEY": request.api_key,
        "RUN_ID": request.run_id,
        "SCENARIO": request.scenario,
        "MODE": request.mode,
        "SUMMARY_PATH": str(request.out_dir / "summary.json"),
    }
    if request.otlp_endpoint:
        env.update(
            {
                "K6_OTEL_GRPC_EXPORTER_ENDPOINT": request.otlp_endpoint,
                "K6_OTEL_GRPC_EXPORTER_INSECURE": "true",
                "K6_OTEL_METRIC_PREFIX": OTEL_PREFIX,
                "K6_OTEL_EXPORT_INTERVAL": OTEL_EXPORT_INTERVAL,
            }
        )
    return env


def run_process(command: list[str], env: dict[str, str], cwd: Path) -> int:
    try:
        return subprocess.run(command, env=env, cwd=cwd, check=False).returncode
    except FileNotFoundError:
        return 127  # không có k6 trong PATH


def run_k6(request: K6Request, base_env: dict[str, str], runner: Runner = run_process) -> K6Result:
    """Chạy k6 rồi đổi file console (logfmt) thành `requests.jsonl` sạch và xóa file thô; trả mã thoát, cửa sổ thời gian thật và số request đã ghi."""
    request.out_dir.mkdir(parents=True, exist_ok=True)
    started = utc_now()
    code = runner(build_command(request), build_env(request, base_env), request.script.parent)
    ended = utc_now()
    console = request.out_dir / "console.log"
    jsonl = request.out_dir / "requests.jsonl"
    count = 0
    if console.exists():
        count = d2.write_jsonl(d2.read_entries(console), jsonl)
        console.unlink()
    return K6Result(code, started, ended, jsonl, request.out_dir / "summary.json", count)
