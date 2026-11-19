"""Bộ đọc từng nguồn kết quả (task 2.3–2.5). Mỗi hàm thuần: nhận đường dẫn file đã có, trả `SourceResult`; tải từ CI nằm ở `ci.py`."""
from __future__ import annotations

import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

import yaml

from testboard import paths  # noqa: F401  (đặt sys.path cho evaluator.catalog)
from testboard.catalog import CODE
from testboard.model import FAILED, LIMITED, PASSED, STATE_NAMES, CodeResult, KpiResult, SourceResult, state_from_counts, verdict_state
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


def read_k6(path: Path, code: str, known: set[str], source: SourceResult, label: str = "") -> SourceResult:
    """Một lượt k6 cho một mã SP: summary JSON (kèm verdict.json cùng thư mục), thư mục lượt, hoặc bản gộp `pooled-*.json`
    của verdict.py (B.7.2 kết luận theo bản gộp ≥ 3 lượt). Nạp nhiều file cho cùng mã (vd hai phía A/B của SP-06) thì giữ
    trạng thái xấu nhất; `label` ghi tên từng phía vào chi tiết."""
    if code not in known:
        raise ValueError(f"{code} không có trong danh mục")
    if path.is_dir():
        path = path / "summary.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    result = _pooled_result(data) if "runs" in data and "groups" in data else _summary_result(data, path)
    if label:
        result.detail = f"{label}: {result.detail}"
    _merge_code(source, code, result)
    return source


def _pooled_result(pooled: dict) -> CodeResult:
    if pooled.get("selftest"):
        raise ValueError("lượt tự kiểm (--selftest) chỉ để kiểm threshold, không nạp lên dashboard")
    status = str(pooled.get("status", ""))
    groups = ", ".join(f"{name} p95 {group['p95']:.0f} ms" for name, group in (pooled.get("groups") or {}).items() if isinstance(group.get("p95"), (int, float)))
    # Chỉ tiêu không đạt hoặc thiếu mẫu (vd p99 của nhóm ít request): nói rõ để ô vàng không bị hiểu là cả lượt hỏng
    off = [f"{name} {metric} {_VERDICT_LABEL.get(value, value)}" for name, group in (pooled.get("groups") or {}).items()
           for metric, value in (("p95", group.get("p95Status")), ("p99", group.get("p99Status")), ("lỗi", (group.get("errors") or {}).get("status")))
           if value and value != "PASS"]
    detail = (f"gộp {len(pooled.get('runs') or [])} lượt: verdict {_VERDICT_LABEL.get(status, status)}" + (f" ({', '.join(off)})" if off else "")
              + (f" · {groups}" if groups else ""))
    return CodeResult(_VERDICT_STATE.get(status, LIMITED), detail=detail)


def _summary_result(summary: dict, path: Path) -> CodeResult:
    metrics = summary.get("metrics", {})
    failed = sorted(f"{name}: {expr}" for name, metric in metrics.items() for expr, value in (metric.get("thresholds") or {}).items() if _k6_threshold_failed(value))
    duration = metrics.get("http_req_duration", {})
    duration = duration.get("values", duration)
    errors = metrics.get("http_req_failed", {})
    error_rate = errors.get("values", errors).get("rate", errors.get("value"))
    parts = [f"p95 {duration['p(95)']:.0f} ms" if "p(95)" in duration else "", f"p99 {duration['p(99)']:.0f} ms" if "p(99)" in duration else ""]
    if isinstance(error_rate, (int, float)):
        parts.append(f"lỗi {error_rate * 100:.2f} %")
    text = ", ".join(part for part in parts if part)
    result = CodeResult(FAILED if failed else PASSED, detail=("vượt ngưỡng: " + "; ".join(failed[:_DETAIL_MAX]) + " · " if failed else "") + text)
    verdict_file = path.parent / "verdict.json"
    if verdict_file.exists():
        _apply_k6_verdict(json.loads(verdict_file.read_text(encoding="utf-8")), result, text)
    return result


_MERGED_DETAIL_MAX = 480


def _merge_code(source: SourceResult, code: str, result: CodeResult) -> None:
    """Nhiều kết quả cho một mã trong cùng một nguồn: giữ trạng thái xấu nhất, cộng số ca, nối chi tiết."""
    current = source.codes.get(code)
    if current is None:
        source.codes[code] = result
        return
    detail = " | ".join(part for part in (current.detail, result.detail) if part)
    source.codes[code] = CodeResult(max(current.state, result.state), current.passed + result.passed, current.failed + result.failed,
                                    current.skipped + result.skipped, detail[:_MERGED_DETAIL_MAX])


def _merge_kpi(source: SourceResult, kpi: str, result: KpiResult) -> None:
    current = source.kpis.get(kpi)
    if current is None or result.state > current.state:
        source.kpis[kpi] = result


# Kết luận của perf/stack/verdict.py (task 2.18) quyết định thay threshold k6: k6 không biết thiếu mẫu, lượt dừng giữa chừng hay máy đo thiếu tài nguyên
_VERDICT_STATE = {"PASS": PASSED, "FAIL": FAILED, "NOT_RECOVERED": FAILED, "INSUFFICIENT": LIMITED}
_VERDICT_LABEL = {"PASS": "ĐẠT", "FAIL": "KHÔNG ĐẠT", "NOT_RECOVERED": "KHÔNG HỒI PHỤC", "INSUFFICIENT": "KHÔNG ĐỦ DỮ LIỆU"}


def _apply_k6_verdict(verdict: dict, result: CodeResult, summary: str) -> None:
    if verdict.get("selftest"):
        raise ValueError("lượt tự kiểm (--selftest) chỉ để kiểm threshold, không nạp lên dashboard")
    status = str(verdict.get("status", ""))
    detail = f"verdict {_VERDICT_LABEL.get(status, status)}" + (f" · {summary}" if summary else "")
    if verdict.get("validity", "OK") != "OK":   # INCOMPLETE / INFRA_LIMITED (P6-47): không kết luận đạt hay hỏng
        result.state = LIMITED
        result.detail = f"{verdict['validity']}: {'; '.join(verdict.get('problems') or verdict.get('infra') or [])[:120]} · {detail}"
        return
    result.state, result.detail = _VERDICT_STATE.get(status, LIMITED), detail


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


# Plan 6C, việc B4 (D1): FI-11…14 chạy trong lượt SP-09 (perf/stack/faults.py ghi faults.json + fault-latency.json, perf.sh ghi sc6.json)
FAULT_CODES = {"stop-tadss": "FI-11", "latency": "FI-12", "flag-off": "FI-13", "reset": "FI-14"}


def _read_json(path: Path) -> dict | None:
    return json.loads(path.read_text(encoding="utf-8")) if path.is_file() else None


def read_faults(run_dir: Path, known: set[str], source: SourceResult) -> SourceResult:
    """Một lượt SP-09 có tiêm lỗi ⇒ mã FI theo kiểu lỗi. Đạt chỉ khi đủ ba điều: kiểm đơn trong lỗi đạt (cọc 100 %, không chờ duyệt,
    không quyết định trùng), SC6 trên DB của lượt đạt, lượt hợp lệ (P6-47). Thiếu SC6 hoặc lượt INFRA_LIMITED ⇒ hạn chế."""
    faults = _read_json(run_dir / "faults.json")
    if faults is None:
        raise ValueError(f"{run_dir}: không có faults.json (không phải lượt có tiêm lỗi)")
    kind = str(faults.get("kind", ""))
    code = FAULT_CODES.get(kind)
    if code is None:
        raise ValueError(f"{run_dir}: kiểu lỗi '{kind}' không ánh xạ được sang mã FI ({', '.join(FAULT_CODES)})")
    if code not in known:
        raise ValueError(f"{code} không có trong danh mục")
    check = faults.get("check") or {}
    sc6 = _read_json(run_dir / "sc6.json")
    verdict = _read_json(run_dir / "verdict.json") or {}
    latency = (_read_json(run_dir / "fault-latency.json") or {}).get("insideFault") or {}

    sc6_status = str(sc6.get("status", "")) if sc6 else "thiếu"
    parts = [f"{check.get('bookings', '?')} đơn trong lỗi", f"{check.get('notFallback', '?')} không cọc 100 %",
             f"{check.get('awaitingApproval', '?')} chờ duyệt", f"{check.get('duplicateDecisions', '?')} quyết định trùng"]
    if "effectSeconds" in check:
        parts.append(f"cờ có hiệu lực sau {check['effectSeconds']} s")
    detail = f"{run_dir.name}: " + ", ".join(parts) + f"; SC6 {sc6_status}"
    if isinstance(latency.get("p95"), (int, float)):
        detail += f"; Đặt sân p95 trong lỗi {latency['p95']:.0f} ms"
    if check.get("problems"):
        detail += " · " + "; ".join(check["problems"])[:120]

    check_state = verdict_state(str(check.get("status", "")))
    sc6_state = verdict_state(sc6_status) if sc6 else LIMITED
    if verdict.get("validity", "OK") != "OK":
        state, detail = LIMITED, f"{verdict['validity']} · {detail}"
    elif FAILED in (check_state, sc6_state):
        state = FAILED
    elif check_state == PASSED and sc6_state == PASSED:
        state = PASSED
    else:
        state = LIMITED
    _merge_code(source, code, CodeResult(state, detail=detail))
    if sc6:
        _merge_kpi(source, "SC6", KpiResult(sc6_state, sc6_status, f"{sc6.get('checkedBookings', 0)} booking sau lượt {run_dir.name}"[:160]))
    if code == "FI-13":
        _merge_kpi(source, "SC5", KpiResult(state, STATE_NAMES[state].upper(), detail[:160]))
    return source


# Bước CI không phải test (OPS-01/02, SEC-BE-7, SEC-AI-3/4, NG-2/3, OB-4…): kết luận của step trong lượt CI, đọc bằng `gh run view --json jobs`
_STEP_STATE = {"success": PASSED, "failure": FAILED, "cancelled": LIMITED, "timed_out": LIMITED}


def read_ci_steps(jobs: dict, step_codes: dict[str, tuple[str, ...]], known: set[str], source: SourceResult) -> SourceResult:
    """step_codes: tên step ⇒ mã. Step bị bỏ qua (job trước đỏ, pull request) hoặc không có trong lượt ⇒ không có kết quả, ghi vào `notes`
    để đổi tên step trong workflow không làm mã âm thầm biến mất. Không che kết quả đỏ đã có của cùng mã (giữ trạng thái xấu nhất)."""
    steps = {step.get("name"): step.get("conclusion") for job in jobs.get("jobs", []) for step in job.get("steps", [])}
    for name, codes in step_codes.items():
        if name not in steps:
            source.notes.append(f"bước CI không thấy: {name}")
            continue
        state = _STEP_STATE.get(str(steps[name]))
        if state is None:
            source.notes.append(f"bước CI không chạy: {name} ({steps[name]})")
            continue
        for code in codes:
            if code in known:
                _merge_code(source, code, CodeResult(state, detail=f"bước CI «{name}»: {steps[name]}"))
            elif code not in source.ignored:
                source.ignored.append(code)
    return source


# Kiểm thủ công (OPS-05…08, OB-BE-4, OB-AI-2/3, SEC-AI-1…) và kết quả có sai lệch đã giải thích: tests/manual-results.yaml (có trong git)
MANUAL_STATES = {"passed": PASSED, "limited": LIMITED, "failed": FAILED}
_MANUAL_FIELDS = ("code", "state", "date", "by", "evidence")
_ISO_DATE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


def manual_records(path: Path) -> object:
    return (yaml.safe_load(path.read_text(encoding="utf-8")) if path.is_file() else None) or []


def manual_errors(path: Path, known: set[str]) -> list[str]:
    """Lỗi của file ghi nhận thủ công (dùng ở `board.py check` trong CI và trước khi nạp)."""
    if not path.is_file():
        return [f"{path.name}: không có file"]
    records = manual_records(path)
    if not isinstance(records, list):
        return [f"{path.name}: phải là danh sách bản ghi (mỗi bản ghi: {', '.join(_MANUAL_FIELDS)}, detail)"]
    errors, seen = [], set()
    for index, record in enumerate(records, 1):
        if not isinstance(record, dict):
            errors.append(f"{path.name}: bản ghi {index} không phải ánh xạ")
            continue
        code = str(record.get("code", f"bản ghi {index}"))
        if code not in known:
            errors.append(f"{path.name}: {code} không có trong danh mục")
        if code in seen:
            errors.append(f"{path.name}: {code} ghi hai lần")
        seen.add(code)
        errors += [f"{path.name}: {code}: thiếu {field}" for field in _MANUAL_FIELDS[1:] if not str(record.get(field) or "").strip()]
        if record.get("state") and record["state"] not in MANUAL_STATES:
            errors.append(f"{path.name}: {code}: state phải là {', '.join(MANUAL_STATES)} (nhận {record['state']})")
        if record.get("date") and not _ISO_DATE.match(str(record["date"])):
            errors.append(f"{path.name}: {code}: date phải dạng YYYY-MM-DD (nhận {record['date']})")
    return errors


def read_manual(path: Path, known: set[str], source: SourceResult) -> SourceResult:
    errors = manual_errors(path, known)
    if errors:
        raise ValueError("file ghi nhận thủ công lỗi: " + "; ".join(errors))
    for record in manual_records(path):
        detail = f"{record['date']} · {record['by']}: {str(record.get('detail') or '').strip()} · bằng chứng: {record['evidence']}"
        source.codes[record["code"]] = CodeResult(MANUAL_STATES[record["state"]], detail=detail[:_MERGED_DETAIL_MAX])
    return source
