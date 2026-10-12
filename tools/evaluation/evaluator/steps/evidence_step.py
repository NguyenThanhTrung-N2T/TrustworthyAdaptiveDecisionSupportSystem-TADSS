"""Bước (f): gom Evidence Pack (D1 từ database của stack và của instance PT, D2 từ k6, D4/D5 + phân tích RP/PT từ bước (e), config đã lọc secret), kết luận KPI (kể cả SC7),
EV-1…EV-5 (EV-2/EV-3 trên nhiều nguồn, EV-4 quét UUID gốc của CSV SmashCourt), `report.html` và `manifest.json` (O-21, DATASET_SCHEMA)."""
from __future__ import annotations

import csv
import json
import re
import shutil
from datetime import datetime, timedelta
from pathlib import Path
from typing import Any, Callable

from evaluator import catalog, code_check, d2, machine, workspace
from evaluator.chaos_schedule import SCHEDULES
from evaluator.clients import prometheus
from evaluator.clients.postgres import connect_fetch
from evaluator.results import ERROR, FAILED, PASSED, StepResult, utc_now
from evaluator.run_context import RunContext
from evaluator.steps import dataset_step, tests_step
from evidence import coverage_map, d1_export, dataset_analysis, evidence_pack, hotel_adapter, kpi, replay
from evidence.anonymize import Anonymizer
from evidence.report import generator

STEP_NAME = "f"
ENVIRONMENT = "E-EVAL"
BE_FE_NOTE = "không tham gia lượt chạy (không có LT-04/E2E; thuộc Phase 6)"
SC1_SCENARIO = "lt01"
DASHBOARD_ENDPOINT = 'endpoint="POST api/v2/decisions"'
# Mã danh mục được chính evaluator kiểm (không đi qua trx); điều kiện: bước nào đạt
STEP_VERIFIED = {"b": ("LT-01", "LT-02"), "c": ("LT-03", "SCALE-2", "OB-3"), "d": ("CT-03",)}
# DOC-1…DOC-3 do check_docs.py (bước a) kiểm; ghi chú một dòng của tests_step: "check_docs.py: đạt" / "check_docs.py: không đạt (mã …, xem check-docs.log)"
DOCS_CHECK_CODES = ("DOC-1", "DOC-2", "DOC-3")
DOCS_CHECK_PASSED = "check_docs.py: đạt"
DOCS_CHECK_FAILED = "check_docs.py: không đạt"
_COMMENT = re.compile(r"\s+#.*$")
CodeCheckFunction = Callable[[Path], code_check.CodeCheck]


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
    if _docs_check_noted(context, DOCS_CHECK_PASSED):
        codes.update(DOCS_CHECK_CODES)
    return codes


def failed_codes(context: RunContext) -> set[str]:
    """Mã danh mục mà evaluator tự kiểm và KHÔNG đạt (thuộc `failed` của độ phủ): DOC-1…DOC-3 khi `check_docs.py` của bước (a) không đạt."""
    return set(DOCS_CHECK_CODES) if _docs_check_noted(context, DOCS_CHECK_FAILED) else set()


def _docs_check_noted(context: RunContext, note: str) -> bool:
    return any(line.startswith(note) for outcome in context.outcomes if outcome.name == tests_step.STEP_NAME for line in outcome.notes)


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


def export_d1_safely(fetch_factory: Callable[[str], Any], dsn: str, anonymizer: Anonymizer, since: datetime, path: Path, label: str) -> tuple[int, str | None]:
    """Xuất D1 của một database; trả (số dòng, None) hoặc (0, thông báo lỗi). Database không tới được là lỗi hạ tầng nên mọi ngoại lệ đều thành thông báo (một chỗ bắt duy nhất)."""
    try:
        return d1_export.export_d1(fetch_factory(dsn), anonymizer, since, path), None
    except Exception as error:  # noqa: BLE001 - database không tới được là lỗi hạ tầng
        return 0, f"{label} thất bại: {type(error).__name__}: {error}"


def read_original_uuids(private_dir: Path) -> set[str]:
    """Mọi UUID (chữ thường) trong CSV SmashCourt thô, để EV-4 khẳng định không UUID gốc nào lọt vào pack. Chỉ nằm trong bộ nhớ của tiến trình, không ghi ra file."""
    path = private_dir / dataset_step.REPLAY_FILE
    if not path.is_file():
        return set()
    return evidence_pack.find_uuids(path.read_text(encoding="utf-8", errors="ignore"))


def _write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def write_datasets(context: RunContext, run_dir: Path) -> list[str]:
    """D4 (RP-01) và D5 (PT-01) từ `raw/{replay,portability}/{prepared,requests}.jsonl` của bước (e), ghép `strategy_status` từ D1 theo `decision_id`, rồi phân tích RP-02/PT-02
    ra `raw/analysis/*.json`. Phần nào bước (e) không chuẩn bị dữ liệu (bị bỏ) thì không có file. Trả các ghi chú một dòng."""
    notes: list[str] = []
    main_d1, pt_d1 = run_dir / "raw" / "d1" / "decision_audit.csv", run_dir / "raw" / "d1" / "decision_audit_portability.csv"
    main_index = dataset_analysis.d1_index(evidence_pack.read_csv(main_d1) if main_d1.exists() else [])
    pt_index = dataset_analysis.d1_index(evidence_pack.read_csv(pt_d1) if pt_d1.exists() else [])

    d4 = dataset_analysis.build_d4(run_dir, main_index)
    if d4 is not None:
        count = dataset_analysis.write_csv(run_dir / "raw" / "d4" / "replay_smashcourt.csv", replay.D4_COLUMNS, d4)
        appsettings = context.repo_dir / dataset_analysis.APPSETTINGS_RELATIVE
        if appsettings.is_file():
            _write_json(run_dir / "raw" / "analysis" / "rp_summary.json", dataset_analysis.rp_summary(d4, dataset_analysis.load_calendar(appsettings)))
            notes.append(f"D4: {count} dòng; phân tích RP-02 đã ghi")
        else:
            notes.append(f"D4: {count} dòng; KHÔNG phân tích được RP-02 vì thiếu {dataset_analysis.APPSETTINGS_RELATIVE.as_posix()} (lịch cao điểm)")
    d5 = dataset_analysis.build_d5(run_dir, pt_index)
    if d5 is not None:
        count = dataset_analysis.write_csv(run_dir / "raw" / "d5" / "portability_hotel.csv", hotel_adapter.D5_COLUMNS, d5)
        _write_json(run_dir / "raw" / "analysis" / "pt_summary.json", dataset_analysis.pt_summary(d5))
        notes.append(f"D5: {count} dòng; phân tích PT-02 đã ghi")
    return notes


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


def decide_kpis(context: RunContext, run_dir: Path, summary: dict | None, chaos_rows: list[dict[str, str]], ev3: evidence_pack.EvCheck, runs: list[dict[str, Any]],
                code_check_result: dict[str, Any] | None = None) -> list[kpi.Kpi]:
    scale2 = generator.read_json(run_dir / "raw" / "scale" / "scale2-checks.json")
    mutation = (generator.read_json(run_dir / "raw" / "mutation" / "mutation-summary.json") or {}).get("results")
    has_load = (run_dir / "raw" / "load").exists()
    return [
        kpi.sc1(runs),
        kpi.sc2(chaos_rows, summary),
        kpi.sc3(ev3.passed if has_load else None, ev3.detail, summary),
        kpi.sc4(summary, None if scale2 is None else scale2["passed"], chaos_rows),
        *kpi.out_of_scope(),
        kpi.sc7(generator.read_json(run_dir / "raw" / "portability" / "summary.json"), code_check_result),
        kpi.sc8(summary, mutation),
    ]


def run(
    context: RunContext, fetch_factory: Callable[[str], Any] = connect_fetch, query: Callable[..., list[dict]] = prometheus.query,
    check_code: CodeCheckFunction = code_check.check_adapter_isolation,
) -> StepResult:
    """
    Dựng Evidence Pack của lượt chạy và kết luận. `failed` nếu có KPI FAIL hoặc EV-1…EV-5 không đạt (khi đó báo cáo ghi "không dùng làm minh chứng"); `error` nếu không xuất được D1
    (kể cả D1 của database PT khi bước (e) đã gửi request PT). Phần dữ liệu thực tế chỉ có khi bước (e) đã chạy; thiếu thì các mục tương ứng ghi lý do và SC7 là INSUFFICIENT_DATA.
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
    d1_count, failure = export_d1_safely(fetch_factory, dsn, anonymizer, context.started_at, run_dir / "raw" / "d1" / "decision_audit.csv", "Xuất D1")
    if failure:
        notes.append(failure)
        return finish(ERROR)
    notes.append(f"D1: {d1_count} dòng")
    portability_requests = run_dir / "raw" / "portability" / "requests.jsonl"
    if portability_requests.is_file() and portability_requests.stat().st_size > 0:  # bước (e) đã gửi request PT: D1 nằm ở database riêng của instance PT
        pt_dsn = context.env.get("EVAL_PT_PG_DSN")
        if not pt_dsn:
            notes.append("EVAL_PT_PG_DSN chưa được đặt: không xuất được D1 của database PT")
            return finish(ERROR)
        pt_count, failure = export_d1_safely(fetch_factory, pt_dsn, anonymizer, context.started_at, run_dir / "raw" / "d1" / "decision_audit_portability.csv", "Xuất D1 của database PT")
        if failure:
            notes.append(failure)
            return finish(ERROR)
        notes.append(f"D1 (PT): {pt_count} dòng")
    d2_entries = write_d2(run_dir)
    write_config(context, run_dir)
    notes.append(f"D2: {len(d2_entries)} request")
    notes.extend(write_datasets(context, run_dir))

    code_check_result: dict[str, Any] | None = None
    if (run_dir / "raw" / "portability" / "summary.json").is_file():
        code_check_result = check_code(context.repo_dir).to_dict()
        _write_json(run_dir / "raw" / "analysis" / "sc7_code_check.json", code_check_result)

    git = workspace.read_git_info(context.repo_dir)
    summary = catalog_summary(run_dir)
    chaos_rows = generator.read_csv_rows(run_dir / "raw" / "chaos" / "chaos_log.csv")
    mutation = (generator.read_json(run_dir / "raw" / "mutation" / "mutation-summary.json") or {}).get("results")
    methodology = context.repo_dir / "docs" / "research" / "EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md"
    codes = coverage_map.catalog_codes(methodology) if methodology.exists() else []
    dataset_passed, dataset_failed = dataset_analysis.dataset_coverage(run_dir)
    coverage = coverage_map.coverage(codes, summary or {}, verified_codes(context, mutation, chaos_rows) | dataset_passed, dataset_failed | failed_codes(context))
    machine_info = machine.collect(machine.IMAGES)
    base = {
        "runId": context.run_id, "createdAtUtc": context.started_at.strftime("%Y-%m-%dT%H:%M:%SZ"), "environment": ENVIRONMENT, "mode": context.mode, "dirty": git.dirty,
        "commits": {"tadss": git.commit, "smashcourtBe": None, "smashcourtFe": None}, "commitNotes": {"smashcourtBe": BE_FE_NOTE, "smashcourtFe": BE_FE_NOTE},
        "images": machine_info.get("images", {}), "machine": {key: value for key, value in machine_info.items() if key != "images"},
        "testCases": sorted(set(coverage["passed"]) | set(coverage["failed"])), "steps": read_steps(context.outcomes), "catalogCoverage": coverage,
        "datasets": dataset_analysis.dataset_info(run_dir, code_check_result),
    }
    runs = sc1_runs(context, run_dir, query)
    manifest0 = evidence_pack.build_manifest(run_dir, {**base, "kpis": []})
    main_source, *extra_sources = evidence_pack.collect_sources(run_dir)  # chính: D1 + D2 + replay; PT: D1 của database PT + response PT
    checks = evidence_pack.run_checks(
        run_dir, manifest0, main_source.d1_rows, main_source.entries, extra_sources=extra_sources, main_name=main_source.name, raw_uuids=read_original_uuids(context.datasets_private),
    )
    kpis = decide_kpis(context, run_dir, summary, chaos_rows, checks[2], runs, code_check_result)
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
