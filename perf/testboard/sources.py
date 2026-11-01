"""Bộ đọc từng nguồn kết quả (task 2.3–2.5). Mỗi hàm thuần: nhận đường dẫn file đã có, trả `SourceResult`; tải từ CI nằm ở `ci.py`."""
from __future__ import annotations

import json
import xml.etree.ElementTree as ET
from pathlib import Path

from testboard import paths  # noqa: F401  (đặt sys.path cho evaluator.catalog)
from testboard.catalog import CODE
from testboard.model import FAILED, LIMITED, PASSED, CodeResult, KpiResult, SourceResult, state_from_counts, verdict_state
from evaluator.catalog import load_index, normalize, parse_trx  # noqa: E402  (tools/evaluation, đọc trx dùng chung với evaluator)

_DETAIL_MAX = 3


def _short(test: str) -> str:
    """`Ns.Class.Method` → `Class.Method` cho cột chi tiết."""
    return ".".join(test.split(".")[-2:])


def _codes_from_cases(cases: list[tuple[list[str], str, str]], known: set[str], source: SourceResult) -> SourceResult:
    """cases: (mã, kết quả passed|failed|skipped, tên test). Gộp theo mã; mã ngoài danh mục ghi vào `ignored`."""
    counts: dict[str, dict[str, int]] = {}
    failures: dict[str, list[str]] = {}
    for codes, outcome, test in cases:
        for code in codes:
            if code not in known:
                if code not in source.ignored:
                    source.ignored.append(code)
                continue
            bucket = counts.setdefault(code, {"passed": 0, "failed": 0, "skipped": 0})
            bucket[outcome] += 1
            if outcome == "failed":
                failures.setdefault(code, []).append(_short(test))
    for code, bucket in counts.items():
        failed_tests = failures.get(code, [])
        detail = ", ".join(failed_tests[:_DETAIL_MAX]) + (f" (+{len(failed_tests) - _DETAIL_MAX})" if len(failed_tests) > _DETAIL_MAX else "")
        source.codes[code] = CodeResult(state_from_counts(bucket["passed"], bucket["failed"]), bucket["passed"], bucket["failed"], bucket["skipped"], detail)
    source.ignored.sort()
    return source


def read_trx(directory: Path, known: set[str], source: SourceResult) -> SourceResult:
    """BE, TADSS: mọi `*.trx` + `catalog-index.*.json` (chỉ mục test → mã do test C# xuất, vì trx không giữ trait). Test không có trong chỉ mục bị bỏ qua."""
    index = load_index(sorted(directory.rglob("catalog-index.*.json")))
    if not index:
        raise ValueError(f"{directory}: không có catalog-index.*.json (CI phải chạy test với biến xuất chỉ mục)")
    cases = [(index.get(outcome.test, []), outcome.outcome, outcome.test) for path in sorted(directory.rglob("*.trx")) for outcome in parse_trx(path)]
    if not cases:
        raise ValueError(f"{directory}: không có file trx")
    return _codes_from_cases(cases, known, source)


def read_junit(path: Path, known: set[str], source: SourceResult) -> SourceResult:
    """AI: JUnit XML của pytest; mã ở thuộc tính `catalog` của từng testcase (conftest ghi từ marker `catalog`)."""
    cases = []
    for case in ET.parse(path).getroot().iter("testcase"):
        codes = [code for prop in case.iter("property") if prop.get("name") == "catalog" for code in (prop.get("value") or "").split(",") if code]
        if case.find("failure") is not None or case.find("error") is not None:
            outcome = "failed"
        elif case.find("skipped") is not None:
            outcome = "skipped"
        else:
            outcome = "passed"
        cases.append((codes, outcome, normalize(f"{case.get('classname', '')}.{case.get('name', '')}")))
    return _codes_from_cases(cases, known, source)


def read_evidence(pack: Path, known: set[str], source: SourceResult) -> SourceResult:
    """Evidence Pack của evaluator TADSS: `manifest.json` (catalogCoverage + kpis)."""
    manifest = json.loads((pack / "manifest.json").read_text(encoding="utf-8"))
    coverage = manifest.get("catalogCoverage", {})
    for state, codes in ((PASSED, coverage.get("passed", [])), (FAILED, coverage.get("failed", []))):
        for code in codes:
            if code in known:
                source.codes[code] = CodeResult(state, detail="" if state == PASSED else "xem report.html của Evidence Pack")
    for kpi in manifest.get("kpis", []):
        source.kpis[kpi["code"]] = KpiResult(verdict_state(kpi.get("verdict", "")), kpi.get("verdict", ""), str(kpi.get("value", ""))[:160])
    source.run = manifest.get("runId", pack.name)
    source.commit = ((manifest.get("commits") or {}).get("tadss") or "")[:7]
    return source


def _k6_threshold_failed(value: object) -> bool:
    # Định dạng handleSummary: {"ok": bool}; định dạng --summary-export: bool = ngưỡng đã bị vượt
    return (not value.get("ok", True)) if isinstance(value, dict) else bool(value)


def read_k6(path: Path, code: str, known: set[str], source: SourceResult) -> SourceResult:
    """Summary JSON của k6 cho một mã SP: đạt khi mọi threshold đạt; chi tiết p95/p99 và tỉ lệ lỗi."""
    if code not in known:
        raise ValueError(f"{code} không có trong danh mục")
    metrics = json.loads(path.read_text(encoding="utf-8")).get("metrics", {})
    failed = sorted(f"{name}: {expr}" for name, metric in metrics.items() for expr, value in (metric.get("thresholds") or {}).items() if _k6_threshold_failed(value))
    duration = metrics.get("http_req_duration", {})
    duration = duration.get("values", duration)
    errors = metrics.get("http_req_failed", {})
    error_rate = errors.get("values", errors).get("rate", errors.get("value"))
    parts = [f"p95 {duration['p(95)']:.0f} ms" if "p(95)" in duration else "", f"p99 {duration['p(99)']:.0f} ms" if "p(99)" in duration else ""]
    if isinstance(error_rate, (int, float)):
        parts.append(f"lỗi {error_rate * 100:.2f} %")
    summary = ", ".join(part for part in parts if part)
    source.codes[code] = CodeResult(FAILED if failed else PASSED, detail=("vượt ngưỡng: " + "; ".join(failed[:_DETAIL_MAX]) + " · " if failed else "") + summary)
    return source


def read_sc6(path: Path, known: set[str], source: SourceResult) -> SourceResult:
    """Báo cáo bất biến booking của BE (SC6, BE-IT-20 trên staging): PASS / FAIL / INCONCLUSIVE."""
    report = json.loads(path.read_text(encoding="utf-8"))
    status = str(report.get("status", ""))
    rules = report.get("violationsByRule") or {}
    value = f"{report.get('checkedBookings', 0)} booking, " + (", ".join(f"{rule}: {count}" for rule, count in rules.items()) or "0 vi phạm")
    if report.get("abortReason"):
        value += f" · dừng: {report['abortReason']}"
    source.kpis["SC6"] = KpiResult(verdict_state(status), status, value[:160])
    if "BE-IT-20" in known:
        state = verdict_state(status)
        source.codes["BE-IT-20"] = CodeResult(state, detail="" if state == PASSED else value[:160])
    return source


def read_playwright(path: Path, known: set[str], source: SourceResult) -> SourceResult:
    """Báo cáo JSON của Playwright (E2E-xx, FE-xx): mã lấy từ tên test hoặc tag (vd `E2E-03 đặt sân cọc 30 %`)."""
    cases = []

    def walk(suite: dict, prefix: str) -> None:
        for spec in suite.get("specs", []):
            title = f"{prefix} {spec.get('title', '')}".strip()
            codes = sorted(set(CODE.findall(title + " " + " ".join(spec.get("tags", [])))))
            for test in spec.get("tests", []):
                statuses = [result.get("status") for result in test.get("results", [])]
                outcome = "skipped" if test.get("status") == "skipped" or not statuses else ("passed" if statuses[-1] == "passed" else "failed")
                cases.append((codes, outcome, title))
        for child in suite.get("suites", []):
            walk(child, f"{prefix} {child.get('title', '')}".strip())

    for suite in json.loads(path.read_text(encoding="utf-8")).get("suites", []):
        walk(suite, suite.get("title", ""))
    return _codes_from_cases(cases, known, source)


def mark_limited(source: SourceResult, reason: str) -> SourceResult:
    """Lượt `INFRA_LIMITED` / `ABORTED` (P6-47): mọi mã đạt của lượt chuyển thành hạn chế, không dùng để kết luận."""
    for result in source.codes.values():
        if result.state == PASSED:
            result.state, result.detail = LIMITED, f"{reason} · {result.detail}" if result.detail else reason
    return source
