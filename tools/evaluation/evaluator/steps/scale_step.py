"""SCALE-2 (O-18): k6 `scale2` qua reverse proxy tới hai instance; 20 % request lặp lại đúng khóa idempotency + payload. Đạt khi 0 response 5xx, mỗi khóa đúng một bản PRIMARY với một decisionId
duy nhất, và Prometheus có số liệu `tadss_decisions_total` của CẢ HAI instance trong cửa sổ chạy."""
from __future__ import annotations

import json
import time
from datetime import timedelta
from typing import TYPE_CHECKING

from evaluator import d2
from evaluator.chaos_rules import parse_ts
from evaluator.clients import k6, prometheus

if TYPE_CHECKING:
    from evaluator.run_context import RunContext
    from evaluator.steps.incident_step import Services

EXPECTED_INSTANCES = ("tadss-1", "tadss-2")
METRIC_SETTLE_SECONDS = 15
READY_TIMEOUT_SECONDS = 90
READY_POLL_SECONDS = 3


def evaluate_scale2(entries: list[dict], key_counts: dict[str, int], instance_increase: dict[str, float]) -> dict:
    """Ba điều kiện của O-18 cộng điều kiện để phép thử có nghĩa (có request lặp). Trả dict dùng làm `scale2-checks.json`; `passed` là kết luận chung."""
    requests = d2.request_entries(entries)
    server_errors = [entry for entry in requests if entry["http_status"] >= 500]
    by_key: dict[str, set[str]] = {}
    for entry in requests:
        if entry["http_status"] == 200 and entry.get("idempotency_key"):
            by_key.setdefault(entry["idempotency_key"], set()).add(entry.get("decision_id") or "")
    inconsistent = sorted(key for key, ids in by_key.items() if len(ids) != 1 or "" in ids)
    not_single = sorted(key for key in by_key if key_counts.get(key, 0) != 1)
    repeats = sum(1 for entry in requests if entry.get("repeat"))
    missing_instances = [name for name in EXPECTED_INSTANCES if instance_increase.get(name, 0.0) <= 0]
    rounded_increase = {name: round(value) for name, value in instance_increase.items()}  # chỉ để in; quyết định ở trên dùng số thực
    checks = {
        "no_server_errors": {"passed": not server_errors, "detail": f"{len(server_errors)} response 5xx trên {len(requests)} request"},
        "single_primary_per_key": {"passed": not not_single, "detail": f"{len(by_key)} khóa có response 200; {len(not_single)} khóa không có đúng một bản PRIMARY"},
        "same_decision_id_per_key": {"passed": not inconsistent, "detail": f"{len(inconsistent)} khóa có response khác decisionId"},
        "both_instances_in_prometheus": {"passed": not missing_instances, "detail": f"tadss_decisions_total tăng theo instance: {rounded_increase}; thiếu: {missing_instances}"},
        "repeats_exercised": {"passed": repeats > 0, "detail": f"{repeats} request lặp khóa"},
    }
    return {"passed": all(check["passed"] for check in checks.values()), "checks": checks}


def wait_instances_ready(urls: list[str], probe, notes: list[str], clock=time.monotonic, sleep=time.sleep, timeout: float = READY_TIMEOUT_SECONDS) -> bool:
    """
    Điều kiện đầu của SCALE-2: cả hai instance trả `/health/ready` 200 (kiểm cả PostgreSQL). Sau lỗi PostgreSQL ở LT-03, instance không nhận tải (chỉ instance 1 bị tải trực tiếp) có thể mất
    tới ~15 s mới nối lại được database (quan sát ở lượt chạy `EV-…-quick-r5`: 31 response 503 ở đầu SCALE-2, mọi kết nối mới của instance 2 chờ hết 15 s) nên SCALE-2 chờ cho tới khi cả hai sẵn sàng
    thay vì đo hồi phục sau sự cố (phần đó đã thuộc LT-03). Hết hạn mà vẫn chưa sẵn sàng thì ghi chú và vẫn chạy (SCALE-2 sẽ phản ánh trung thực).
    """
    deadline = clock() + timeout
    pending = list(urls)
    while pending:
        pending = [url for url in pending if probe(f"{url.rstrip('/')}/health/ready") != 200]
        if not pending:
            return True
        if clock() >= deadline:
            notes.append(f"SCALE-2: instance chưa sẵn sàng sau {timeout:.0f} s: {', '.join(pending)}")
            return False
        sleep(READY_POLL_SECONDS)
    return True


def run_scale2(context: "RunContext", services: "Services", notes: list[str], artifacts: list[str]) -> bool | None:
    """True = đạt, False = không đạt, None = lỗi hạ tầng (đã ghi vào `notes`)."""
    wait_instances_ready([context.endpoints.tadss, context.endpoints.tadss2], services.ready_status, notes)
    out_dir = context.raw_dir("load", "scale2")
    request = k6.K6Request(
        script=context.repo_dir / "tests" / "load" / "scenarios" / "scale2.js",
        scenario="scale2",
        mode=context.mode,
        base_url=context.endpoints.proxy,
        api_key=context.endpoints.api_key,
        run_id=context.run_id,
        out_dir=out_dir,
        otlp_endpoint=context.env.get("EVAL_OTLP_ENDPOINT", "eval-grafana:4317"),
    )
    result = services.run_k6(request, dict(context.env))
    artifacts += [str(result.requests_path), str(result.summary_path)]
    if result.returncode != 0 or result.request_count == 0:
        notes.append(f"scale2: k6 không chạy được (mã {result.returncode}, {result.request_count} request ghi nhận)")
        return None

    services.sleep(METRIC_SETTLE_SECONDS)
    entries = list(d2.read_entries(result.requests_path))
    times = [parse_ts(entry["ts"]) for entry in d2.request_entries(entries)]
    key_counts = services.audit.primary_key_counts(min(times) - timedelta(seconds=60), max(times) + timedelta(seconds=120))
    try:
        instances = services.instance_increase(result.started_at, result.ended_at + timedelta(seconds=METRIC_SETTLE_SECONDS))
    except prometheus.PrometheusError as error:
        notes.append(f"scale2: Prometheus không truy vấn được ({error})")
        instances = {}
    verdict = evaluate_scale2(entries, key_counts, instances)
    path = context.raw_dir("scale") / "scale2-checks.json"
    path.write_text(json.dumps(verdict, ensure_ascii=False, indent=2), encoding="utf-8")
    artifacts.append(str(path))
    for name, check in verdict["checks"].items():
        notes.append(f"SCALE-2 {name}: {'đạt' if check['passed'] else 'KHÔNG ĐẠT'} — {check['detail']}")
    return verdict["passed"]
