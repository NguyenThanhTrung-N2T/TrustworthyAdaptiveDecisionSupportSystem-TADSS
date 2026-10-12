"""CT-03: đọc báo cáo JUnit của Schemathesis và kết luận đạt/không đạt (O-19)."""
from datetime import datetime, timezone
from pathlib import Path

import pytest

from evaluator.run_context import build_context
from evaluator.steps import contract_step as ct

FIXTURES = Path(__file__).resolve().parent / "fixtures"
NOW = datetime(2026, 10, 9, 1, 0, tzinfo=timezone.utc)


def _read(name: str) -> dict:
    return ct.parse_junit((FIXTURES / name).read_text(encoding="utf-8"))


def test_schemathesis_verdict_clean_report_passes() -> None:
    result = ct.verdict(_read("schemathesis_clean.xml"))

    assert result["passed"] and not result["blocking"] and not result["warnings"]


def test_schemathesis_verdict_server_error_fails() -> None:
    result = ct.verdict(_read("schemathesis_5xx.xml"))

    assert not result["passed"] and result["blocking"] == {"not_a_server_error": 1}


def test_schemathesis_verdict_schema_violation_fails() -> None:
    result = ct.verdict(_read("schemathesis_schema_violation.xml"))

    assert not result["passed"] and result["blocking"] == {"response_schema_conformance": 1}


def test_schemathesis_verdict_status_and_content_type_only_pass_with_warnings() -> None:
    result = ct.verdict(_read("schemathesis_status_only.xml"))

    assert result["passed"]
    assert result["warnings"] == {"status_code_conformance": 1, "content_type_conformance": 1}


def test_real_report_404_without_body_is_a_blocking_schema_failure() -> None:
    """Báo cáo thật từ lần chạy đầu (decisionId=`null,null` → 404 rỗng): chính lỗi dẫn tới UnmatchedDecisionRoutesController."""
    operations = _read("schemathesis_404_without_body.xml")
    result = ct.verdict(operations)

    assert not result["passed"]
    assert result["blocking"] == {"response_schema_conformance": 3}
    assert result["warnings"] == {"content_type_conformance": 3}
    assert sorted(name for name, item in operations.items() if item["failed"]) == [
        "GET /api/v2/decisions/{decisionId}",
        "POST /api/v2/decisions/{decisionId}/outcome",
        "POST /api/v2/decisions/{decisionId}/override",
    ]


def test_unknown_heading_is_blocking() -> None:
    xml = '<testsuites><testsuite><testcase name="X"><failure>- Lỗi chưa biết\n\n  chi tiết</failure></testcase></testsuite></testsuites>'

    result = ct.verdict(ct.parse_junit(xml))

    assert not result["passed"] and result["blocking"] == {"other": 1}


def test_command_uses_pinned_checks_examples_and_key(tmp_path: Path) -> None:
    command = ct.build_command(Path("/work/docs/contracts/openapi-v2.yaml"), "http://eval-tadss-1:8080", "k", 25, tmp_path)

    assert command[:2] == ["st", "run"] and command[2].replace("\\", "/").endswith("/work/docs/contracts/openapi-v2.yaml")
    assert command[command.index("-c") + 1] == "not_a_server_error,response_schema_conformance,status_code_conformance,content_type_conformance"
    assert command[command.index("-n") + 1] == "25" and command[command.index("-H") + 1] == "X-Api-Key: k"
    assert "--seed" in command


@pytest.mark.parametrize("mode,examples", [("quick", 25), ("full", 500)])
def test_run_contract_reports_and_handles_missing_report(tmp_path: Path, mode: str, examples: int) -> None:
    env = {"EVAL_REPO": str(tmp_path / "repo"), "EVAL_WORK": str(tmp_path / "work"), "EVAL_EVIDENCE": str(tmp_path / "ev"), "EVAL_MODE": mode, "EVAL_API_KEY": "k"}
    context = build_context(env, NOW)
    out = tmp_path / "out"
    out.mkdir()
    seen = []

    def silent(cmd, cwd, env, log):
        seen.append(cmd)
        log.write_text("... 1188 generated, 0 found", encoding="utf-8")
        return 0

    report, notes, _ = ct.run_contract(context, out, silent)
    assert report is None and "không tạo báo cáo" in notes[0]

    def with_report(cmd, cwd, env, log):
        log.write_text("Test cases:\n  1188 generated, 3 found 6 unique failures", encoding="utf-8")
        (out / "junit-1.xml").write_text((FIXTURES / "schemathesis_clean.xml").read_text(encoding="utf-8"), encoding="utf-8")
        return 0

    report, notes, artifacts = ct.run_contract(context, out, with_report)
    assert report["passed"] and report["generated_cases"] == 1188 and report["examples_per_operation"] == examples
    assert (out / "schemathesis-report.json").exists() and "đạt" in notes[0]
