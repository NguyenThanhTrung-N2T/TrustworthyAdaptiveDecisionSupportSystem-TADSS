"""Bước (e): gom Evidence Pack (D1 từ database của stack, D2 từ k6, config đã lọc secret), kết luận KPI, EV-1…EV-5, `report.html` và `manifest.json` (O-21, DATASET_SCHEMA)."""
from __future__ import annotations

import csv
import json
import re
import shutil
from datetime import datetime, timedelta
from pathlib import Path
from typing import Any, Callable

from evaluator import catalog, d2, machine, workspace
from evaluator.chaos_schedule import SCHEDULES
from evaluator.clients import prometheus
from evaluator.clients.postgres import connect_fetch
from evaluator.results import ERROR, FAILED, PASSED, StepResult, utc_now
from evaluator.run_context import RunContext
from evidence import coverage_map, d1_export, evidence_pack, kpi
from evidence.anonymize import Anonymizer
from evidence.report import generator

STEP_NAME = "e"
ENVIRONMENT = "E-EVAL"
BE_FE_NOTE = "không tham gia lượt chạy (không có LT-04/E2E; thuộc Phase 6)"
SC1_SCENARIO = "lt01"
DASHBOARD_ENDPOINT = 'endpoint="POST api/v2/decisions"'
# Mã danh mục được chính evaluator kiểm (không đi qua trx); điều kiện: bước nào đạt
STEP_VERIFIED = {"b": ("LT-01", "LT-02"), "c": ("LT-03", "SCALE-2", "OB-3"), "d": ("CT-03",)}
_COMMENT = re.compile(r"\s+#.*$")


def read_steps(outcomes: list[StepResult]) -> list[dict[str, Any]]:
    """Kết quả từng bước dạng gọn cho manifest (không kèm danh sách tệp)."""
    return [{"name": o.name, "status": o.status, "duration_seconds": round(o.duration_seconds, 1), "notes": o.notes} for o in outcomes]


def verified_codes(context: RunContext, mutation: dict[str, Any] | None, chaos_rows: list[dict[str, str]]) -> set[str]:
    """Mã danh mục mà evaluator tự kiểm và đạt: theo trạng thái bước, từng dòng chaos_log, điểm MT và OB-2 (bước a)."""
    codes: set[str] = set()
    status = {outcome.name: outcome.status for outcome in context.outcomes}
    for step, step_codes in STEP_VERIFIED.items():
        if status.get(step) == PASSED:
            codes.update(step_codes)
    if context.mode == "full" and status.get("b") == PASSED:
        codes.add("LT-05")
    codes.update(row["fi_id"] for row in chaos_rows if row["passed"] == "True")
    codes.update(code for code, result in (mutation or {}).items() if result.get("passed"))
    return codes


def sc1_runs(context: RunContext, run_dir: Path, query: Callable[..., list[dict]] = prometheus.query) -> list[dict[str, Any]]:
    """Mỗi lần LT-01: tỉ lệ ≤ 150 ms phía server (Prometheus, cửa sổ phần chính) và p95 phía client (k6, chính xác từng request). Lưu truy vấn vào raw/metrics."""
    windows = generator.read_json(run_dir / "raw" / "load" / "windows.json") or []
    runs = []
    for window in (item for item in windows if item["scenario"] == SC1_SCENARIO):
        entries = generator.main_phase(generator.read_jsonl(run_dir / "raw" / "load" / window["name"] / "requests.jsonl"))
        if not entries:
            continue
        start, end = datetime.fromisoformat(window["main_start"]), datetime.fromisoformat(window["end"])
        seconds = max(1, int((end - start).total_seconds()))
        base = f"tadss_decision_duration_milliseconds"
        expr = f'sum(increase({base}_bucket{{le="150",{DASHBOARD_ENDPOINT}}}[{seconds}s])) / sum(increase({base}_count{{{DASHBOARD_ENDPOINT}}}[{seconds}s]))'
        share: float | None
        try:
            result = query(context.endpoints.prometheus, expr, at=end + timedelta(seconds=10))
            share = float(result[0]["value"][1]) if result else None
        except (prometheus.PrometheusError, ValueError):
            share = None
        if share is not None and share != share:  # NaN: không có mẫu trong cửa sổ
            share = None
        stats = generator.latency_stats(entries)
        (run_dir / "raw" / "metrics").mkdir(parents=True, exist_ok=True)
        (run_dir / "raw" / "metrics" / f"sc1-{window['name']}.json").write_text(json.dumps({"query": expr, "window": [window["main_start"], window["end"]], "share_le_150": share}), encoding="utf-8")
        runs.append({"name": window["name"], "server_share_le_150": share, "client_p95_ms": stats["p95"], "samples": stats["samples"]})
    return runs


def write_d2(run_dir: Path) -> list[dict]:
    """D2 (timeseries + summary) từ mọi requests.jsonl; trả toàn bộ dòng thô (kể cả warm-up) cho EV-2/EV-3."""
    out = run_dir / "raw" / "d2"
    all_entries: list[dict] = []
    rows: list[dict[str, object]] = []
    summary: list[dict[str, object]] = []
    for path in sorted((run_dir / "raw" / "load").glob("*/requests.jsonl")):
        entries = generator.read_jsonl(path)
        all_entries += entries
        rows += d2.to_d2_rows(entries)
        stats = generator.latency_stats(generator.main_phase(entries))
        summary.append({"scenario": path.parent.name, **{key: stats[key] for key in ("samples", "p50", "p95", "p99", "max", "error_rate")}})
    d2.write_d2_csv(rows, out / "timeseries.csv")
    with (out / "summary.csv").open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=["scenario", "samples", "p50", "p95", "p99", "max", "error_rate"])
        writer.writeheader()
        writer.writerows(summary)
    return all_entries


def write_config(context: RunContext, run_dir: Path) -> None:
    """`config/`: cấu hình đã dùng (không chứa secret): hồ sơ tải, Toxiproxy, lịch sự cố, phần stack đánh giá của compose (bỏ chú thích)."""
    config = run_dir / "config"
    config.mkdir(parents=True, exist_ok=True)
    repo = context.repo_dir
    for source, target in ((repo / "tests" / "load" / "config" / "modes.json", "k6-modes.json"), (repo / "docker" / "toxiproxy" / "toxiproxy.json", "toxiproxy.json")):
        if source.exists():
            shutil.copy2(source, config / target)
    schedule = {mode: [phase.__dict__ for phase in phases] for mode, phases in SCHEDULES.items()}
    (config / "chaos-schedule.json").write_text(json.dumps(schedule, ensure_ascii=False, indent=2), encoding="utf-8")
    compose = repo / "docker-compose.yml"
    if compose.exists():
        text = compose.read_text(encoding="utf-8")
        start = text.find("  eval-postgres:")
        end = text.find("\nnetworks:", start)
        block = text[start:end] if start >= 0 and end > start else ""
        (config / "compose-evaluation.yml").write_text("\n".join(_COMMENT.sub("", line) for line in block.splitlines() if not line.strip().startswith("#")), encoding="utf-8")
    (config / "run.json").write_text(json.dumps({"mode": context.mode, "steps": list(context.steps), "run_id": context.run_id}, ensure_ascii=False, indent=2), encoding="utf-8")


def catalog_summary(run_dir: Path) -> dict[str, dict[str, int]] | None:
    path = run_dir / "raw" / "tests" / "catalog-results.csv"
    if not path.exists():
        return None
    return catalog.summarize([{"catalog_id": r["catalog_id"], "outcome": r["outcome"]} for r in generator.read_csv_rows(path)])


def decide_kpis(context: RunContext, run_dir: Path, summary: dict | None, chaos_rows: list[dict[str, str]], ev3: evidence_pack.EvCheck, runs: list[dict[str, Any]]) -> list[kpi.Kpi]:
    scale2 = generator.read_json(run_dir / "raw" / "scale" / "scale2-checks.json")
    mutation = (generator.read_json(run_dir / "raw" / "mutation" / "mutation-summary.json") or {}).get("results")
    has_load = (run_dir / "raw" / "load").exists()
    return [
        kpi.sc1(runs),
        kpi.sc2(chaos_rows, summary),
        kpi.sc3(ev3.passed if has_load else None, ev3.detail, summary),
        kpi.sc4(summary, None if scale2 is None else scale2["passed"], chaos_rows),
        *kpi.out_of_scope(),
        kpi.sc8(summary, mutation),
    ]


def run(context: RunContext, fetch_factory: Callable[[str], Any] = connect_fetch, query: Callable[..., list[dict]] = prometheus.query) -> StepResult:
    """
    Dựng Evidence Pack của lượt chạy và kết luận. `failed` nếu có KPI FAIL hoặc EV-1…EV-5 không đạt (khi đó báo cáo ghi "không dùng làm minh chứng"); `error` nếu không xuất được D1.
    """
    started = utc_now()
    run_dir = context.evidence_dir
    notes: list[str] = []
    artifacts: list[str] = []

    def finish(status: str) -> StepResult:
        return StepResult(STEP_NAME, status, started, utc_now(), artifacts, notes)

    dsn = context.env.get("EVAL_PG_DSN")
    if not dsn:
        notes.append("EVAL_PG_DSN chưa được đặt: không xuất được D1")
        return finish(ERROR)
    anonymizer = Anonymizer()  # khóa ngẫu nhiên mỗi lượt, không lưu
    try:
        d1_count = d1_export.export_d1(fetch_factory(dsn), anonymizer, context.started_at, run_dir / "raw" / "d1" / "decision_audit.csv")
    except Exception as error:  # noqa: BLE001 - database không tới được là lỗi hạ tầng
        notes.append(f"Xuất D1 thất bại: {type(error).__name__}: {error}")
        return finish(ERROR)
    d2_entries = write_d2(run_dir)
    write_config(context, run_dir)
    notes.append(f"D1: {d1_count} dòng; D2: {len(d2_entries)} request")

    git = workspace.read_git_info(context.repo_dir)
    summary = catalog_summary(run_dir)
    chaos_rows = generator.read_csv_rows(run_dir / "raw" / "chaos" / "chaos_log.csv")
    mutation = (generator.read_json(run_dir / "raw" / "mutation" / "mutation-summary.json") or {}).get("results")
    methodology = context.repo_dir / "docs" / "research" / "EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md"
    codes = coverage_map.catalog_codes(methodology) if methodology.exists() else []
    coverage = coverage_map.coverage(codes, summary or {}, verified_codes(context, mutation, chaos_rows))
    machine_info = machine.collect(machine.IMAGES)
    base = {
        "runId": context.run_id, "createdAtUtc": context.started_at.strftime("%Y-%m-%dT%H:%M:%SZ"), "environment": ENVIRONMENT, "mode": context.mode, "dirty": git.dirty,
        "commits": {"tadss": git.commit, "smashcourtBe": None, "smashcourtFe": None}, "commitNotes": {"smashcourtBe": BE_FE_NOTE, "smashcourtFe": BE_FE_NOTE},
        "images": machine_info.get("images", {}), "machine": {key: value for key, value in machine_info.items() if key != "images"},
        "testCases": sorted(set(coverage["passed"]) | set(coverage["failed"])), "steps": read_steps(context.outcomes), "catalogCoverage": coverage,
    }
    runs = sc1_runs(context, run_dir, query)
    manifest0 = evidence_pack.build_manifest(run_dir, {**base, "kpis": []})
    d1_rows = evidence_pack.read_csv(run_dir / "raw" / "d1" / "decision_audit.csv")
    checks = evidence_pack.run_checks(run_dir, manifest0, d1_rows, d2_entries)
    kpis = decide_kpis(context, run_dir, summary, chaos_rows, checks[2], runs)
    check_dicts = [check.to_dict() for check in checks]
    manifest_report = {**manifest0, "kpis": [item.to_dict() for item in kpis]}
    data = generator.build_data(run_dir, manifest_report, check_dicts, [item.to_dict() for item in kpis], coverage, coverage_map.by_group(codes, coverage))
    report = run_dir / "report.html"
    report.write_text(generator.render(data), encoding="utf-8")
    final = evidence_pack.build_manifest(run_dir, {**base, "kpis": [item.to_dict() for item in kpis], "evChecks": check_dicts})
    evidence_pack.write_manifest(run_dir, final)
    artifacts += [str(report), str(run_dir / "manifest.json")]
    final_ev1 = evidence_pack.check_ev1(run_dir, final)

    for item in kpis:
        notes.append(f"{item.code}: {item.verdict} — {item.value}")
    notes.append("EV-1…EV-5: " + ", ".join(f"{check.code} {'đạt' if check.passed else 'KHÔNG ĐẠT'}" for check in checks))
    notes.append(f"Báo cáo: {report}")
    if not final_ev1.passed:
        notes.append(f"EV-1 sau khi ghi manifest cuối KHÔNG ĐẠT: {final_ev1.detail}")
    failed = any(item.verdict == kpi.FAIL for item in kpis) or not all(check.passed for check in checks) or not final_ev1.passed
    return finish(FAILED if failed else PASSED)
