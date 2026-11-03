# Task 2.3–2.5: bộ đọc từng nguồn (trx + chỉ mục, JUnit, Evidence Pack, k6, SC6, Playwright) ra đúng trạng thái theo mã
import json
from pathlib import Path

from testboard.model import FAILED, LIMITED, PASSED, CodeResult, SourceResult
from testboard.sources import mark_limited, read_evidence, read_junit, read_k6, read_playwright, read_sc6, read_trx

_TRX_NS = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"


def _trx(path: Path, results: list[tuple[str, str]]) -> None:
    rows = "".join(f'<UnitTestResult testName="{name}" outcome="{outcome}" duration="00:00:00.0100000"/>' for name, outcome in results)
    path.write_text(f'<?xml version="1.0"?><TestRun xmlns="{_TRX_NS}"><Results>{rows}</Results></TestRun>', encoding="utf-8")


def test_trx_groups_cases_by_code_and_one_red_case_fails_the_code(tmp_path, known):
    _trx(tmp_path / "unit.trx", [("Ns.A.Ok", "Passed"), ("Ns.A.Theory(x: 1)", "Passed"), ("Ns.A.Theory(x: 2)", "Failed"), ("Ns.B.Skip", "NotExecuted")])
    (tmp_path / "catalog-index.Unit.json").write_text(json.dumps({"Ns.A.Ok": ["UT-01"], "Ns.A.Theory": ["UT-02"], "Ns.B.Skip": ["UT-03", "P6-18"]}))

    source = read_trx(tmp_path, known, SourceResult("ci-tadss"))

    assert source.codes["UT-01"].state == PASSED
    assert (source.codes["UT-02"].state, source.codes["UT-02"].passed, source.codes["UT-02"].failed) == (FAILED, 1, 1)
    assert source.codes["UT-02"].detail == "A.Theory"
    assert source.codes["UT-03"].skipped == 1 and source.codes["UT-03"].state != PASSED
    assert source.ignored == ["P6-18"]


def test_trx_without_index_is_rejected_with_reason(tmp_path, known):
    _trx(tmp_path / "unit.trx", [("Ns.A.Ok", "Passed")])

    try:
        read_trx(tmp_path, known, SourceResult("ci-be"))
    except ValueError as error:
        assert "catalog-index" in str(error)
    else:
        raise AssertionError("phải từ chối khi thiếu chỉ mục")


def test_junit_reads_catalog_property_of_each_testcase(tmp_path, known):
    path = tmp_path / "junit.xml"
    path.write_text("""<testsuites><testsuite>
      <testcase classname="tests.test_api" name="test_health"><properties><property name="catalog" value="AI-UT-05"/></properties></testcase>
      <testcase classname="tests.test_api" name="test_quota"><properties><property name="catalog" value="AI-UT-01,AI-UT-03"/></properties><failure message="x"/></testcase>
      <testcase classname="tests.test_x" name="test_no_code"/>
    </testsuite></testsuites>""", encoding="utf-8")

    source = read_junit(path, known, SourceResult("ci-ai"))

    assert source.codes["AI-UT-05"].state == PASSED
    assert source.codes["AI-UT-03"].state == FAILED and source.codes["AI-UT-01"].state == FAILED
    assert source.codes["AI-UT-03"].detail == "test_api.test_quota"


def test_evidence_reads_coverage_and_kpis(tmp_path, known):
    (tmp_path / "manifest.json").write_text(json.dumps({
        "runId": "EV-1", "commits": {"tadss": "906baa6567f2", "smashcourtBe": None},
        "catalogCoverage": {"passed": ["UT-01", "XX-01"], "failed": ["MT-01"], "untested": {"RP-01": "thiếu CSV"}},
        "kpis": [{"code": "SC1", "verdict": "PASS", "value": "p95 47 ms"}, {"code": "SC7", "verdict": "INFRA_LIMITED", "value": ""}],
    }), encoding="utf-8")

    source = read_evidence(tmp_path, known, SourceResult("evidence"))

    assert (source.run, source.commit) == ("EV-1", "906baa6")
    assert source.codes["UT-01"].state == PASSED and source.codes["MT-01"].state == FAILED
    assert "RP-01" not in source.codes and "XX-01" not in source.codes
    assert (source.kpis["SC1"].state, source.kpis["SC7"].state) == (PASSED, LIMITED)


def test_k6_handle_summary_format_passes_when_thresholds_ok(tmp_path, known):
    path = tmp_path / "summary.json"
    path.write_text(json.dumps({"metrics": {
        "http_req_duration": {"values": {"p(95)": 120.4, "p(99)": 300.2}, "thresholds": {"p(95)<1000": {"ok": True}}},
        "http_req_failed": {"values": {"rate": 0.002}},
    }}), encoding="utf-8")

    source = read_k6(path, "SP-01", known, SourceResult("k6-SP-01"))

    assert source.codes["SP-01"].state == PASSED
    assert source.codes["SP-01"].detail == "p95 120 ms, p99 300 ms, lỗi 0.20 %"


def test_k6_summary_export_format_fails_on_crossed_threshold(tmp_path, known):
    path = tmp_path / "summary.json"
    path.write_text(json.dumps({"metrics": {
        "http_req_duration": {"p(95)": 1500.0, "thresholds": {"p(95)<1000": True}},
        "http_req_failed": {"value": 0.05},
    }}), encoding="utf-8")

    source = read_k6(path, "SP-02", known, SourceResult("k6-SP-02"))

    assert source.codes["SP-02"].state == FAILED
    assert "http_req_duration: p(95)<1000" in source.codes["SP-02"].detail


def _k6_run_with_verdict(tmp_path: Path, verdict: dict) -> Path:
    tmp_path.mkdir(parents=True, exist_ok=True)
    path = tmp_path / "summary.json"
    path.write_text(json.dumps({"metrics": {"http_req_duration": {"values": {"p(95)": 120.4}, "thresholds": {"p(95)<300": {"ok": True}}}}}),
                    encoding="utf-8")
    (tmp_path / "verdict.json").write_text(json.dumps({"profile": "load", "selftest": False, "validity": "OK", "problems": [], **verdict}),
                                           encoding="utf-8")
    return path


def test_k6_verdict_json_decides_over_thresholds(tmp_path, known):
    # task 2.18: k6 chấm trên mọi mẫu; verdict.json còn biết thiếu mẫu, lượt dừng giữa chừng, máy đo thiếu tài nguyên
    passed = read_k6(_k6_run_with_verdict(tmp_path / "a", {"status": "PASS"}), "SP-01", known, SourceResult("k6"))
    insufficient = read_k6(_k6_run_with_verdict(tmp_path / "b", {"status": "INSUFFICIENT"}), "SP-01", known, SourceResult("k6"))
    failed = read_k6(_k6_run_with_verdict(tmp_path / "c", {"status": "FAIL"}), "SP-01", known, SourceResult("k6"))
    assert passed.codes["SP-01"].state == PASSED
    assert insufficient.codes["SP-01"].state == LIMITED and "KHÔNG ĐỦ DỮ LIỆU" in insufficient.codes["SP-01"].detail
    assert failed.codes["SP-01"].state == FAILED


def test_k6_incomplete_or_infra_limited_run_is_never_pass_nor_fail(tmp_path, known):
    incomplete = _k6_run_with_verdict(tmp_path / "a", {"status": "FAIL", "validity": "INCOMPLETE", "problems": ["k6 thoát 105"]})
    source = read_k6(incomplete, "SP-01", known, SourceResult("k6"))
    assert source.codes["SP-01"].state == LIMITED and "k6 thoát 105" in source.codes["SP-01"].detail


def test_k6_selftest_run_cannot_be_loaded(tmp_path, known):
    path = _k6_run_with_verdict(tmp_path, {"status": "FAIL", "selftest": True})
    try:
        read_k6(path, "SP-01", known, SourceResult("k6"))
    except ValueError as error:
        assert "tự kiểm" in str(error)
    else:
        raise AssertionError("lượt tự kiểm không được nạp")


def test_limited_run_turns_passed_codes_into_limited():
    source = SourceResult("k6-SP-01")
    source.codes["SP-01"] = CodeResult(PASSED)

    mark_limited(source, "INFRA_LIMITED: CPU máy đo 100 %")

    assert source.codes["SP-01"].state == LIMITED and "CPU" in source.codes["SP-01"].detail


def test_sc6_maps_status_to_kpi_and_staging_code(tmp_path, known):
    path = tmp_path / "sc6.json"
    path.write_text(json.dumps({"status": "FAIL", "checkedBookings": 40, "violationsByRule": {"R1": 2}}), encoding="utf-8")

    source = read_sc6(path, known, SourceResult("sc6"))

    assert source.kpis["SC6"].state == FAILED and source.kpis["SC6"].value == "40 booking, R1: 2"
    assert source.codes["BE-IT-20"].state == FAILED


def test_playwright_takes_codes_from_titles_and_last_retry(tmp_path, known):
    path = tmp_path / "report.json"
    path.write_text(json.dumps({"suites": [{"title": "booking.spec.ts", "specs": [], "suites": [{"title": "Đặt sân", "specs": [
        {"title": "E2E-01 đặt sân cọc theo TADSS", "tests": [{"results": [{"status": "failed"}, {"status": "passed"}]}]},
        {"title": "E2E-02 hết hạn duyệt", "tests": [{"results": [{"status": "failed"}]}]},
    ]}]}]}), encoding="utf-8")

    source = read_playwright(path, known, SourceResult("playwright"))

    assert source.codes["E2E-01"].state == PASSED  # đạt ở lần thử lại cuối
    assert source.codes["E2E-02"].state == FAILED
