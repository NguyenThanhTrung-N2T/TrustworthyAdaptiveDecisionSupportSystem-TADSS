"""CT-03 (O-19): chạy Schemathesis trên `docs/contracts/openapi-v2.yaml` của bản sao `/work`, đích `eval-tadss-1`; tổng hợp theo operation từ báo cáo JUnit.
CT-03 đạt ⇔ 0 lỗi `not_a_server_error` và 0 lỗi `response_schema_conformance`; `status_code_conformance` và `content_type_conformance` được báo cáo (cảnh báo)."""
from __future__ import annotations

import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Callable

from evaluator.run_context import RunContext
from evaluator.steps.tests_step import run_command

CHECKS = ("not_a_server_error", "response_schema_conformance", "status_code_conformance", "content_type_conformance")
BLOCKING_CHECKS = ("not_a_server_error", "response_schema_conformance")
EXAMPLES = {"quick": 25, "full": 500}
SEED = 20261008
# Tiêu đề lỗi trong thân <failure> của JUnit → check sinh ra nó (tiêu đề lấy từ schemathesis 4.29.3, core/failures.py và specs/openapi/checks.py)
HEADING_TO_CHECK = {
    "Server error": "not_a_server_error",
    "Server error on unexpected Content-Type": "not_a_server_error",
    "JSON deserialization error": "response_schema_conformance",
    "Response violates schema": "response_schema_conformance",
    "Response header does not conform to the schema": "response_schema_conformance",
    "Undocumented HTTP status code": "status_code_conformance",
    "Undocumented Content-Type": "content_type_conformance",
    "Missing Content-Type header": "content_type_conformance",
}
OTHER = "other"
_HEADING = re.compile(r"^- (.+?)\s*$", re.MULTILINE)
_GENERATED = re.compile(r"(\d+) generated")

Command = Callable[[list[str], Path, dict[str, str], Path], int]


def parse_junit(xml_text: str) -> dict[str, dict]:
    """
    `{operation: {"failed": bool, "checks": {check: số lần}}}` từ báo cáo JUnit của Schemathesis (mỗi testcase một operation; `<failure>`/`<error>` mang tiêu đề lỗi dạng gạch đầu dòng).
    Tiêu đề lạ được tính vào `other`; testcase hỏng mà không đọc được tiêu đề nào cũng tính `other`.
    """
    operations: dict[str, dict] = {}
    for case in ET.fromstring(xml_text).iter("testcase"):
        name = case.get("name", "")
        checks: dict[str, int] = {}
        problems = [*case.findall("failure"), *case.findall("error")]
        for problem in problems:
            headings = _HEADING.findall(problem.text or "")
            for heading in headings or ["(không rõ)"]:
                check = HEADING_TO_CHECK.get(heading, OTHER)
                checks[check] = checks.get(check, 0) + 1
        operations[name] = {"failed": bool(problems), "checks": checks}
    return operations


def verdict(operations: dict[str, dict]) -> dict:
    """Kết luận CT-03: `passed`, tổng số lỗi theo check, danh sách cảnh báo (không chặn)."""
    totals = {check: 0 for check in (*CHECKS, OTHER)}
    for item in operations.values():
        for check, count in item["checks"].items():
            totals[check] += count
    blocking = {check: totals[check] for check in (*BLOCKING_CHECKS, OTHER) if totals[check]}
    warnings = {check: totals[check] for check in CHECKS if check not in BLOCKING_CHECKS and totals[check]}
    return {"passed": not blocking, "blocking": blocking, "warnings": warnings, "totals": totals}


def build_command(spec: Path, base_url: str, api_key: str, examples: int, report_dir: Path) -> list[str]:
    return [
        "st", "run", str(spec), "-u", base_url, "-H", f"X-Api-Key: {api_key}",
        "-c", ",".join(CHECKS), "-n", str(examples), "--seed", str(SEED),
        "--report", "junit,ndjson", "--report-dir", str(report_dir), "--no-color",
    ]


def run_contract(context: RunContext, out_dir: Path, command: Command = run_command) -> tuple[dict | None, list[str], list[str]]:
    """Trả `(kết luận, ghi chú, tệp)`; `None` khi Schemathesis không chạy được (không có báo cáo JUnit)."""
    examples = EXAMPLES[context.mode]
    log = out_dir / "schemathesis.log"
    work = context.work_dir
    exit_code = command(build_command(work / "docs" / "contracts" / "openapi-v2.yaml", context.endpoints.tadss, context.endpoints.api_key, examples, out_dir), work, dict(context.env), log)
    junit = sorted(out_dir.glob("junit-*.xml"))
    artifacts = [str(log), *map(str, junit), *map(str, sorted(out_dir.glob("ndjson-*.ndjson")))]
    if not junit:
        return None, [f"CT-03: Schemathesis không tạo báo cáo (mã {exit_code}, xem {log.name})"], artifacts
    operations = parse_junit(junit[-1].read_text(encoding="utf-8"))
    result = verdict(operations)
    generated = _GENERATED.search(log.read_text(encoding="utf-8", errors="replace"))
    report = {"mode": context.mode, "examples_per_operation": examples, "seed": SEED, "generated_cases": int(generated.group(1)) if generated else None, "operations": operations, **result}
    path = out_dir / "schemathesis-report.json"
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    artifacts.append(str(path))
    failed_ops = [name for name, item in operations.items() if item["failed"]]
    notes = [
        f"CT-03: {'đạt' if result['passed'] else 'KHÔNG ĐẠT'} — {len(operations)} operation, {report['generated_cases']} ca sinh ra ({examples} ví dụ/operation), "
        f"lỗi chặn {result['blocking'] or 0}, cảnh báo {result['warnings'] or 0}" + (f"; operation lỗi: {', '.join(failed_ops)}" if failed_ops else "")
    ]
    return report, notes, artifacts
