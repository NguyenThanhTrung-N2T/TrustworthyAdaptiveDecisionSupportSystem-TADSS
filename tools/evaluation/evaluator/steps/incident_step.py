"""Bước (c): sự cố dưới tải LT-03 (k6 50 RPS trên instance 1 + lỗi theo lịch cố định), chaos_log, annotation Grafana, snapshot JSON dashboard (OB-3), rồi SCALE-2."""
from __future__ import annotations

import json
import time
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from pathlib import Path
from typing import Callable

from evaluator import chaos_rules, d2, faults
from evaluator.chaos_log import ChaosRow, write_chaos_log
from evaluator.chaos_schedule import (
    BASELINE, FAULT, KIND_POSTGRES_DOWN, KIND_REDIS_LATENCY, KIND_STRATEGY_DELAY, KIND_STRATEGY_THROW, REDIS_LATENCY_MS, STRATEGY_KINDS, Phase, recovery_deadline_seconds,
    schedule_for, total_seconds,
)
from evaluator.clients import grafana, k6, prometheus
from evaluator.clients.http import http_get
from evaluator.clients.postgres import AuditReader, connect_fetch
from evaluator.clients.toxiproxy import ToxiproxyClient
from evaluator.results import ERROR, FAILED, PASSED, StepResult, utc_now
from evaluator.run_context import RunContext

STEP_NAME = "c"
K6_FAILURE_CHECK_SECONDS = 3
K6_START_DELAY_SECONDS = 2  # từ lúc chạy tiến trình k6 tới lúc kịch bản bắt đầu phát tải (khởi tạo, nạp config)
METRIC_SETTLE_SECONDS = 15
SNAPSHOT_STEP_SECONDS = 15
POSTGRES_PROXY = "postgres"
REDIS_PROXY = "redis"
TOXIC_TEXT = {
    KIND_STRATEGY_DELAY: f"profile tiêm lỗi Delay DelayMs={faults.STRATEGY_DELAY_MS}",
    KIND_STRATEGY_THROW: "profile tiêm lỗi Throw",
    KIND_POSTGRES_DOWN: "toxiproxy tắt proxy postgres",
    KIND_REDIS_LATENCY: f"toxiproxy latency {REDIS_LATENCY_MS} ms (downstream) trên proxy redis",
}
# Biểu thức cho biểu đồ dòng thời gian LT-03 (bước f); mỗi cái ghi ra raw/metrics/lt03-<tên>.json
TIMELINE_QUERIES = {
    "request-rate": 'sum(rate(tadss_decision_duration_milliseconds_count{endpoint="POST api/v2/decisions"}[15s]))',
    "p99-ms": 'histogram_quantile(0.99, sum by (le) (rate(tadss_decision_duration_milliseconds_bucket{endpoint="POST api/v2/decisions"}[15s])))',
    "status-503-rate": 'sum(rate(tadss_decision_duration_milliseconds_count{endpoint="POST api/v2/decisions",status_code="503"}[15s]))',
    "fallback-rate": "sum(rate(tadss_fallbacks_total[15s]))",
    "circuit-state": "max by (strategy) (tadss_circuit_state)",
    "cache-circuit-state": "max(tadss_cache_circuit_state)",
    "cache-bypass-rate": 'sum(rate(tadss_cache_requests_total{result="bypass"}[15s]))',
}


@dataclass
class Services:
    """Mọi thứ bước (c) gọi ra ngoài; test thay bằng bản giả."""

    toxiproxy: ToxiproxyClient
    audit: AuditReader
    write_profile: Callable[[str, int], None]
    annotate: Callable[[str, list[str], int], None]
    run_k6: Callable[..., k6.K6Result]
    query_range: Callable[..., list[dict]]
    query_increase: Callable[..., float]
    instance_increase: Callable[..., dict[str, float]]
    ready_status: Callable[[str], int]
    fetch_dashboard: Callable[[], dict]
    clock: Callable[[], datetime] = utc_now
    sleep: Callable[[float], None] = time.sleep

    def sleep_until(self, moment: datetime) -> None:
        remaining = (moment - self.clock()).total_seconds()
        if remaining > 0:
            self.sleep(remaining)


@dataclass
class AppliedFault:
    """Một lỗi đã thực sự bật/tắt: thời điểm thật (không phải thời điểm theo lịch)."""

    phase: Phase
    recovery: Phase
    started_at: datetime
    ended_at: datetime | None = None


@dataclass
class Execution:
    applied: list[AppliedFault] = field(default_factory=list)
    error: str | None = None
    cleanup_problems: list[str] = field(default_factory=list)


def _missing_dsn(sql: str, params: tuple) -> list[tuple]:
    raise RuntimeError("EVAL_PG_DSN chưa được đặt (compose đặt cho evaluator)")


def default_services(context: RunContext) -> Services:
    fault_file = Path(context.env.get("EVAL_FAULT_FILE", faults.DEFAULT_FAULT_FILE))
    grafana_url, prometheus_url = context.endpoints.grafana, context.endpoints.prometheus
    dsn = context.env.get("EVAL_PG_DSN")
    return Services(
        toxiproxy=ToxiproxyClient(context.endpoints.toxiproxy),
        audit=AuditReader(connect_fetch(dsn) if dsn else _missing_dsn),
        write_profile=lambda profile, delay_ms: faults.write_profile(fault_file, profile, delay_ms),
        annotate=lambda text, tags, at_ms: grafana.annotate(grafana_url, text, tags, at_ms),
        run_k6=k6.run_k6,
        query_range=lambda expr, start, end, step: prometheus.query_range(prometheus_url, expr, start, end, step),
        query_increase=lambda metric, start, end: prometheus.increase_over(prometheus_url, metric, start, end),
        instance_increase=lambda start, end: prometheus.increase_by_label(prometheus_url, "tadss_decisions_total", "instance", start, end),
        ready_status=lambda url: http_get(url)[0],
        fetch_dashboard=lambda: grafana.fetch_dashboard(grafana_url),
    )


def apply_fault(services: Services, kind: str) -> None:
    if kind == KIND_STRATEGY_DELAY:
        services.write_profile(faults.PROFILE_DELAY, faults.STRATEGY_DELAY_MS)
    elif kind == KIND_STRATEGY_THROW:
        services.write_profile(faults.PROFILE_THROW, faults.STRATEGY_DELAY_MS)
    elif kind == KIND_POSTGRES_DOWN:
        services.toxiproxy.set_enabled(POSTGRES_PROXY, False)
    else:
        services.toxiproxy.add_latency(REDIS_PROXY, REDIS_LATENCY_MS)


def revert_fault(services: Services, kind: str) -> None:
    if kind in STRATEGY_KINDS:
        services.write_profile(faults.PROFILE_NONE, faults.STRATEGY_DELAY_MS)
    elif kind == KIND_POSTGRES_DOWN:
        services.toxiproxy.set_enabled(POSTGRES_PROXY, True)
    else:
        services.toxiproxy.remove_latency(REDIS_PROXY)


def cleanup(services: Services) -> list[str]:
    """Đưa stack về trạng thái sạch (mọi proxy bật, không toxic, profile None); chạy bất kể lịch có lỗi hay không. Trả các vấn đề còn lại."""
    problems: list[str] = []
    steps = (
        ("toxiproxy reset", services.toxiproxy.reset),
        ("ghi profile None", lambda: services.write_profile(faults.PROFILE_NONE, faults.STRATEGY_DELAY_MS)),
    )
    for label, action in steps:
        try:
            action()
        except Exception as error:  # noqa: BLE001 - phải thử mọi bước dọn dù bước trước lỗi
            problems.append(f"{label}: {type(error).__name__}: {error}")
    try:
        problems += [f"sau dọn: {problem}" for problem in services.toxiproxy.problems()]
    except Exception as error:  # noqa: BLE001
        problems.append(f"kiểm trạng thái toxiproxy: {type(error).__name__}: {error}")
    return problems


def _annotate(services: Services, notes: list[str], text: str, tags: list[str]) -> None:
    try:
        services.annotate(text, tags, int(services.clock().timestamp() * 1000))
    except Exception as error:  # noqa: BLE001 - thiếu annotation không làm hỏng lượt đo, nhưng phải được báo cáo
        notes.append(f"Annotation Grafana lỗi ({text}): {type(error).__name__}: {error}")


def execute_schedule(services: Services, mode: str, origin: datetime, notes: list[str]) -> Execution:
    """Chạy lịch theo `origin` (lúc k6 bắt đầu). Luôn dọn dẹp ở cuối, kể cả khi bật/tắt lỗi giữa chừng ném lỗi."""
    execution = Execution()
    phases = schedule_for(mode)
    try:
        for index, phase in enumerate(phases):
            if phase.role == BASELINE:
                continue
            services.sleep_until(origin + timedelta(seconds=phase.start))
            if phase.role == FAULT:
                apply_fault(services, phase.kind)
                execution.applied.append(AppliedFault(phase, phases[index + 1], services.clock()))
                _annotate(services, notes, f"{phase.fi_id} bật: {TOXIC_TEXT[phase.kind]}", ["chaos", phase.fi_id, "start"])
            else:
                revert_fault(services, phase.kind)
                execution.applied[-1].ended_at = services.clock()
                _annotate(services, notes, f"{phase.fi_id} gỡ", ["chaos", phase.fi_id, "end"])
        services.sleep_until(origin + timedelta(seconds=total_seconds(mode)))
    except Exception as error:  # noqa: BLE001 - mọi lỗi hạ tầng giữa lịch là kết quả `error`, nhưng vẫn phải dọn
        execution.error = f"{type(error).__name__}: {error}"
    finally:
        execution.cleanup_problems = cleanup(services)
    return execution


def _iso(moment: datetime) -> str:
    return moment.strftime("%Y-%m-%dT%H:%M:%S.%fZ")


def _bypass_increase(services: Services, start: datetime, end: datetime) -> float:
    try:
        return services.query_increase('tadss_cache_requests_total{result="bypass"}', start, end + timedelta(seconds=METRIC_SETTLE_SECONDS))
    except prometheus.PrometheusError:
        return 0.0


def _redis_recovery(services: Services, start: datetime, end: datetime, deadline: int) -> tuple[bool, float | None]:
    expr = 'sum(increase(tadss_cache_requests_total{result="bypass"}[10s]))'
    try:
        samples = prometheus.series_values(services.query_range(expr, start, end + timedelta(seconds=METRIC_SETTLE_SECONDS), 5))
    except prometheus.PrometheusError:
        return False, None
    return chaos_rules.recovery_from_samples(start.timestamp(), samples, deadline)


def evaluate_faults(run_id: str, executed: Execution, entries: list[dict], services: Services) -> list[ChaosRow]:
    """Mỗi lỗi một dòng chaos_log: kết luận pha lỗi + pha hồi phục + bất biến trong cả hai pha."""
    requests = [entry for entry in entries if "http_status" in entry]
    times = [chaos_rules.parse_ts(entry["ts"]) for entry in requests]
    key_counts = services.audit.primary_key_counts(min(times) - timedelta(seconds=60), max(times) + timedelta(seconds=120)) if requests else {}
    rows = []
    for applied in executed.applied:
        kind, recovery_phase = applied.phase.kind, applied.recovery
        ended = applied.ended_at or applied.started_at
        effective = applied.started_at + timedelta(seconds=chaos_rules.SETTLE_SECONDS[kind])
        window = chaos_rules.in_window(requests, effective, ended)
        recovery_end = ended + timedelta(seconds=recovery_phase.length)
        recovery_window = chaos_rules.in_window(requests, ended, recovery_end)
        deadline = recovery_deadline_seconds(kind)

        if kind in STRATEGY_KINDS:
            fault = chaos_rules.evaluate_strategy_fault(kind, window, services.audit.fallback_stats(effective, ended))
            bad = [chaos_rules.parse_ts(e["ts"]) for e in recovery_window if e.get("execution_type") == chaos_rules.TYPE_FALLBACK]
            recovery = chaos_rules.recovery_verdict("tỉ lệ FALLBACK", deadline, chaos_rules.recovery_from_events(ended, recovery_end, bad, deadline))
        elif kind == KIND_POSTGRES_DOWN:
            fault = chaos_rules.evaluate_postgres_fault(window, key_counts)
            bad = [chaos_rules.parse_ts(e["ts"]) for e in recovery_window if e["http_status"] >= 500 or e["http_status"] == 0]
            recovery = chaos_rules.recovery_verdict("tỉ lệ 503", deadline, chaos_rules.recovery_from_events(ended, recovery_end, bad, deadline))
        else:
            fault = chaos_rules.evaluate_redis_fault(window, _bypass_increase(services, effective, ended))
            recovery = chaos_rules.recovery_verdict("bypass cache", deadline, _redis_recovery(services, ended, recovery_end, deadline))

        span = chaos_rules.in_window(requests, applied.started_at, recovery_end)
        uncertain_until = applied.started_at + timedelta(seconds=chaos_rules.UNCERTAIN_COMMIT_GRACE_SECONDS) if kind == KIND_POSTGRES_DOWN else None
        violations = chaos_rules.invariant_violations(span, key_counts, uncertain_until)
        uncertain = chaos_rules.uncertain_commits(span, key_counts, uncertain_until)
        uncertain_note = f" | [cam kết không chắc chắn] {uncertain} response 503 có bản ghi (request đang bay lúc cắt; retry cùng khóa trả bản đã lưu)" if uncertain else ""
        rows.append(
            ChaosRow(
                fi_id=applied.phase.fi_id,
                run_id=run_id,
                toxic=TOXIC_TEXT[kind],
                started_at_utc=_iso(applied.started_at),
                ended_at_utc=_iso(ended),
                expected=f"[lỗi] {fault.expected} | [hồi phục] {recovery.expected}",
                observed=f"[lỗi] {fault.observed} | [hồi phục] {recovery.observed}{uncertain_note}",
                passed=fault.passed and recovery.passed and violations == 0 and applied.ended_at is not None,
                invariant_violations=violations,
            )
        )
    return rows


def _write_metric_files(context: RunContext, services: Services, start: datetime, end: datetime, notes: list[str]) -> list[str]:
    paths = []
    out = context.raw_dir("metrics")
    for name, expr in TIMELINE_QUERIES.items():
        path = out / f"lt03-{name}.json"
        try:
            result = services.query_range(expr, start, end, 5)
        except prometheus.PrometheusError as error:
            notes.append(f"Không lấy được metric {name}: {error}")
            continue
        path.write_text(json.dumps({"query": expr, "start": start.isoformat(), "end": end.isoformat(), "step_seconds": 5, "result": result}, ensure_ascii=False), encoding="utf-8")
        paths.append(str(path))
    return paths


def _write_snapshot(context: RunContext, services: Services, start: datetime, end: datetime, notes: list[str]) -> str | None:
    """OB-3: `grafana/dashboard-snapshot.json` = JSON dashboard + dữ liệu mọi panel PromQL trong cửa sổ LT-03 (không lưu ảnh)."""
    try:
        document = services.fetch_dashboard()
        window = {"start": start.isoformat(), "end": end.isoformat(), "step_seconds": SNAPSHOT_STEP_SECONDS}
        snapshot = grafana.build_snapshot(document, window, lambda expr: services.query_range(expr, start, end, SNAPSHOT_STEP_SECONDS))
    except Exception as error:  # noqa: BLE001
        notes.append(f"Không dựng được snapshot dashboard: {type(error).__name__}: {error}")
        return None
    path = context.evidence_dir / "grafana" / "dashboard-snapshot.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(snapshot, ensure_ascii=False), encoding="utf-8")
    answered = sum(1 for panel in snapshot["panels"] if any(query["result"] for query in panel["queries"]))
    notes.append(f"Snapshot dashboard: {answered}/{len(snapshot['panels'])} panel PromQL có dữ liệu")
    return str(path)


def run_incident(context: RunContext, services: Services, notes: list[str], artifacts: list[str]) -> bool | None:
    """LT-03: True = đạt, False = có lỗi không đạt, None = lỗi hạ tầng (đã ghi vào `notes`)."""
    out_dir = context.raw_dir("load", "lt03")
    request = k6.K6Request(
        script=context.repo_dir / "tests" / "load" / "scenarios" / "lt03.js",
        scenario="lt03",
        mode=context.mode,
        base_url=context.endpoints.tadss,
        api_key=context.endpoints.api_key,
        run_id=context.run_id,
        out_dir=out_dir,
        otlp_endpoint=context.env.get("EVAL_OTLP_ENDPOINT", "eval-grafana:4317"),
    )
    with ThreadPoolExecutor(max_workers=1) as pool:
        origin = services.clock() + timedelta(seconds=K6_START_DELAY_SECONDS)
        future = pool.submit(services.run_k6, request, dict(context.env))
        services.sleep(K6_START_DELAY_SECONDS + K6_FAILURE_CHECK_SECONDS)
        if future.done():
            # k6 đã thoát ngay (không có binary, kịch bản lỗi...): không chạy lịch cả chục phút vô ích
            executed = Execution(cleanup_problems=cleanup(services))
        else:
            executed = execute_schedule(services, context.mode, origin, notes)
        result = future.result()
    artifacts += [str(result.requests_path), str(result.summary_path)]
    for problem in executed.cleanup_problems:
        notes.append(f"Dọn dẹp: {problem}")
    if executed.error or executed.cleanup_problems:
        notes.append(f"Lịch sự cố bị gián đoạn: {executed.error or 'dọn dẹp không sạch'}")
        return None
    if result.returncode != 0 or result.request_count == 0:
        notes.append(f"lt03: k6 không chạy được (mã {result.returncode}, {result.request_count} request ghi nhận)")
        return None

    services.sleep(METRIC_SETTLE_SECONDS)
    entries = list(d2.read_entries(result.requests_path))
    rows = evaluate_faults(context.run_id, executed, entries, services)
    log_path = context.raw_dir("chaos") / "chaos_log.csv"
    write_chaos_log(rows, log_path)
    artifacts.append(str(log_path))
    for row in rows:
        notes.append(f"{row.fi_id}: {'đạt' if row.passed else 'KHÔNG ĐẠT'} (bất biến vi phạm {row.invariant_violations}) — {row.observed}")
    window_end = result.ended_at + timedelta(seconds=METRIC_SETTLE_SECONDS)
    artifacts += _write_metric_files(context, services, result.started_at, window_end, notes)
    snapshot = _write_snapshot(context, services, result.started_at, window_end, notes)
    if snapshot:
        artifacts.append(snapshot)
    expected_faults = len([phase for phase in schedule_for(context.mode) if phase.role == FAULT])
    return len(rows) == expected_faults and all(row.passed for row in rows) and snapshot is not None


def run(context: RunContext, services: Services | None = None) -> StepResult:
    """Bước (c): LT-03 rồi SCALE-2 (SCALE-2 chỉ chạy khi LT-03 không gặp lỗi hạ tầng, và sau khi stack đã dọn sạch)."""
    from evaluator.steps import scale_step  # noqa: PLC0415 - tránh vòng import: scale_step dùng lại Services

    started = utc_now()
    notes: list[str] = []
    artifacts: list[str] = []
    services = services or default_services(context)
    incident = run_incident(context, services, notes, artifacts)
    if incident is None:
        return StepResult(STEP_NAME, ERROR, started, utc_now(), artifacts, notes)
    scale = scale_step.run_scale2(context, services, notes, artifacts)
    if scale is None:
        return StepResult(STEP_NAME, ERROR, started, utc_now(), artifacts, notes)
    return StepResult(STEP_NAME, PASSED if incident and scale else FAILED, started, utc_now(), artifacts, notes)
