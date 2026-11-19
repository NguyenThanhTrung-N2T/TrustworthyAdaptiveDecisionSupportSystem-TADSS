# Nguồn bổ sung của bảng "Kiểm thử" (plan 6C, việc B4 = D1): bản gộp k6 nhiều lượt, FI-11…14 từ thư mục lượt SP-09,
# bước CI không phải test, ghi nhận kiểm thủ công. Mỗi nguồn chỉ ra "đạt" khi đủ bằng chứng; thiếu một phần ⇒ hạn chế, không đạt giả.
import json
from pathlib import Path

import pytest

from testboard.model import FAILED, LIMITED, PASSED, SourceResult
from testboard.sources import manual_errors, read_ci_steps, read_faults, read_k6, read_manual

_POOLED_GROUPS = {"read": {"n": 100, "p95": 14.6}, "booking": {"n": 50, "p95": 87.1}}


def _pooled(path: Path, status: str = "PASS", runs: int = 3, **extra) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps({"groups": _POOLED_GROUPS, "errors": {}, "endpoints": {}, "status": status, "selftest": False,
                                "runs": [f"run-{index}" for index in range(runs)], **extra}), encoding="utf-8")
    return path


def _run_dir(path: Path, status: str = "PASS", validity: str = "OK") -> Path:
    path.mkdir(parents=True, exist_ok=True)
    (path / "summary.json").write_text(json.dumps({"metrics": {"http_req_duration": {"values": {"p(95)": 30.0}}}}), encoding="utf-8")
    (path / "verdict.json").write_text(json.dumps({"profile": "load", "selftest": False, "status": status, "validity": validity, "problems": []}),
                                       encoding="utf-8")
    return path


# --- k6: bản gộp và nhiều lượt cho một mã ---

def test_pooled_file_uses_the_pooled_verdict_not_a_single_run(tmp_path, known):
    source = read_k6(_pooled(tmp_path / "pooled-x-3runs.json"), "SP-01", known, SourceResult("k6-SP-01"))

    assert source.codes["SP-01"].state == PASSED
    assert source.codes["SP-01"].detail.startswith("gộp 3 lượt: verdict ĐẠT")
    assert "booking p95 87 ms" in source.codes["SP-01"].detail


@pytest.mark.parametrize(("status", "state"), [("FAIL", FAILED), ("INSUFFICIENT", LIMITED), ("NOT_RECOVERED", FAILED)])
def test_pooled_status_maps_like_single_run_verdict(tmp_path, known, status, state):
    source = read_k6(_pooled(tmp_path / "pooled.json", status=status), "SP-01", known, SourceResult("k6"))
    assert source.codes["SP-01"].state == state


def test_pooled_detail_names_the_metrics_that_are_not_passed(tmp_path, known):
    groups = {"read": {"p95": 14.6, "p95Status": "PASS", "p99Status": "PASS", "errors": {"status": "PASS"}},
              "ipn": {"p95": 45.7, "p95Status": "PASS", "p99Status": "INSUFFICIENT", "errors": {"status": "PASS"}}}
    path = tmp_path / "pooled.json"
    path.write_text(json.dumps({"groups": groups, "status": "INSUFFICIENT", "selftest": False, "runs": [1, 2, 3]}), encoding="utf-8")

    detail = read_k6(path, "SP-06", known, SourceResult("k6")).codes["SP-06"].detail

    assert detail.startswith("gộp 3 lượt: verdict KHÔNG ĐỦ DỮ LIỆU (ipn p99 KHÔNG ĐỦ DỮ LIỆU) · read p95 15 ms")


def test_pooled_selftest_cannot_be_loaded(tmp_path, known):
    with pytest.raises(ValueError, match="tự kiểm"):
        read_k6(_pooled(tmp_path / "pooled.json", selftest=True), "SP-01", known, SourceResult("k6"))


def test_run_directory_reads_its_summary_and_verdict(tmp_path, known):
    source = read_k6(_run_dir(tmp_path / "20261111-003042-load-SP09-stop-tadss"), "SP-09", known, SourceResult("k6"))
    assert source.codes["SP-09"].state == PASSED and "ĐẠT" in source.codes["SP-09"].detail


def test_several_files_for_one_code_keep_the_worst_and_name_each_run(tmp_path, known):
    source = SourceResult("k6-SP-06")
    read_k6(_pooled(tmp_path / "pooled-A1-3runs.json"), "SP-06", known, source, label="A1")
    read_k6(_pooled(tmp_path / "pooled-B2-3runs.json", status="INSUFFICIENT"), "SP-06", known, source, label="B2")

    result = source.codes["SP-06"]
    assert result.state == LIMITED
    assert result.detail.startswith("A1: gộp 3 lượt") and " | B2: gộp 3 lượt: verdict KHÔNG ĐỦ DỮ LIỆU" in result.detail


# --- FI-11…14 từ thư mục lượt SP-09 ---

def _fault_run(path: Path, kind: str = "latency", check_status: str = "PASS", sc6: str | None = "PASS", validity: str = "OK", **check) -> Path:
    _run_dir(path, validity=validity)
    body = {"bookings": 159, "notFallback": 0, "awaitingApproval": 0, "duplicateDecisions": 0, "problems": [], "status": check_status, **check}
    (path / "faults.json").write_text(json.dumps({"kind": kind, "check": body}), encoding="utf-8")
    (path / "fault-latency.json").write_text(json.dumps({"insideFault": {"requests": 192, "p50": 49.9, "p95": 74.1}}), encoding="utf-8")
    if sc6 is not None:
        (path / "sc6.json").write_text(json.dumps({"status": sc6, "checkedBookings": 902, "violationsByRule": {}}), encoding="utf-8")
    return path


@pytest.mark.parametrize(("kind", "code"), [("stop-tadss", "FI-11"), ("latency", "FI-12"), ("reset", "FI-14"), ("flag-off", "FI-13")])
def test_fault_kind_maps_to_its_fi_code(tmp_path, known, kind, code):
    source = read_faults(_fault_run(tmp_path / "run", kind=kind), known, SourceResult("faults"))
    assert source.codes[code].state == PASSED


def test_fault_detail_has_the_numbers_that_prove_the_criterion(tmp_path, known):
    source = read_faults(_fault_run(tmp_path / "20261113-155023-load-SP09-p657-latency-on"), known, SourceResult("faults"))

    detail = source.codes["FI-12"].detail
    assert detail.startswith("20261113-155023-load-SP09-p657-latency-on: 159 đơn trong lỗi")
    for part in ("0 không cọc 100 %", "0 chờ duyệt", "0 quyết định trùng", "SC6 PASS", "Đặt sân p95 trong lỗi 74 ms"):
        assert part in detail
    assert source.kpis["SC6"].state == PASSED


@pytest.mark.parametrize(
    ("args", "state"),
    [({"check_status": "FAIL", "notFallback": 2}, FAILED), ({"sc6": "FAIL"}, FAILED), ({"sc6": None}, LIMITED),
     ({"validity": "INFRA_LIMITED"}, LIMITED), ({"sc6": "INCONCLUSIVE"}, LIMITED)],
)
def test_fault_run_is_passed_only_with_clean_check_clean_sc6_and_valid_run(tmp_path, known, args, state):
    source = read_faults(_fault_run(tmp_path / "run", **args), known, SourceResult("faults"))
    assert source.codes["FI-12"].state == state


def test_two_runs_of_one_fault_keep_the_worst(tmp_path, known):
    source = SourceResult("faults")
    read_faults(_fault_run(tmp_path / "a", kind="stop-tadss"), known, source)
    read_faults(_fault_run(tmp_path / "b", kind="stop-tadss", sc6="FAIL"), known, source)

    assert source.codes["FI-11"].state == FAILED
    assert source.kpis["SC6"].state == FAILED


def test_flag_off_run_reports_effect_time_and_sc5(tmp_path, known):
    source = read_faults(_fault_run(tmp_path / "run", kind="flag-off", effectSeconds=1.2), known, SourceResult("faults"))

    assert "cờ có hiệu lực sau 1.2 s" in source.codes["FI-13"].detail
    assert source.kpis["SC5"].state == PASSED


def test_unknown_fault_kind_is_rejected(tmp_path, known):
    with pytest.raises(ValueError, match="kiểu lỗi"):
        read_faults(_fault_run(tmp_path / "run", kind="disk-full"), known, SourceResult("faults"))


# --- bước CI không phải test ---

_STEPS = {"Check Vulnerable Packages": ("SEC-BE-7",), "Build, check and push image": ("OPS-01", "OPS-02")}


def _jobs(*steps: tuple[str, str | None]) -> dict:
    return {"jobs": [{"name": "job", "steps": [{"name": name, "conclusion": conclusion} for name, conclusion in steps]}]}


def test_ci_steps_map_conclusion_to_state(known):
    source = read_ci_steps(_jobs(("Check Vulnerable Packages", "success"), ("Build, check and push image", "failure")), _STEPS, known, SourceResult("ci-be"))

    assert source.codes["SEC-BE-7"].state == PASSED and "Check Vulnerable Packages" in source.codes["SEC-BE-7"].detail
    assert source.codes["OPS-01"].state == FAILED and source.codes["OPS-02"].state == FAILED


def test_skipped_or_missing_step_gives_no_result_and_is_noted(known):
    source = read_ci_steps(_jobs(("Check Vulnerable Packages", "skipped")), _STEPS, known, SourceResult("ci-be"))

    assert source.codes == {}
    assert source.notes == ["bước CI không chạy: Check Vulnerable Packages (skipped)", "bước CI không thấy: Build, check and push image"]


def test_ci_step_never_hides_a_red_test_of_the_same_code(known):
    source = SourceResult("ci-be")
    source.codes["OPS-01"] = read_ci_steps(_jobs(("Build, check and push image", "failure")), _STEPS, known, SourceResult("x")).codes["OPS-01"]
    read_ci_steps(_jobs(("Build, check and push image", "success")), _STEPS, known, source)

    assert source.codes["OPS-01"].state == FAILED


# --- ghi nhận kiểm thủ công ---

def _manual(path: Path, body: str) -> Path:
    path.write_text(body, encoding="utf-8")
    return path


_VALID = """
- code: SEC-AI-1
  state: passed
  date: 2026-11-19
  by: Claude
  evidence: docker inspect smashcourt-ai
  detail: cổng host chỉ 127.0.0.1
- code: LT-02
  state: limited
  date: 2026-10-17
  by: Claude
  evidence: spec/archive/phase5/PHASE5_REPORT_5.7.md
  detail: 504 0,04 %
"""


def test_manual_records_become_codes_with_date_and_evidence(tmp_path, known):
    source = read_manual(_manual(tmp_path / "m.yaml", _VALID), known, SourceResult("manual"))

    assert source.codes["SEC-AI-1"].state == PASSED and source.codes["LT-02"].state == LIMITED
    assert source.codes["SEC-AI-1"].detail == "2026-11-19 · Claude: cổng host chỉ 127.0.0.1 · bằng chứng: docker inspect smashcourt-ai"


@pytest.mark.parametrize(
    ("body", "message"),
    [("- {code: XX-99, state: passed, date: 2026-11-19, by: a, evidence: b}", "XX-99 không có trong danh mục"),
     ("- {code: OPS-05, state: ok, date: 2026-11-19, by: a, evidence: b}", "OPS-05: state phải là"),
     ("- {code: OPS-05, state: passed, date: 19/11, by: a, evidence: b}", "OPS-05: date phải dạng YYYY-MM-DD"),
     ("- {code: OPS-05, state: passed, date: 2026-11-19, by: a}", "OPS-05: thiếu evidence"),
     ("- {code: OPS-05, state: passed, date: 2026-11-19, by: a, evidence: b}\n- {code: OPS-05, state: passed, date: 2026-11-19, by: a, evidence: b}",
      "OPS-05 ghi hai lần"),
     ("code: OPS-05", "danh sách")],
)
def test_manual_file_errors_name_the_code_and_the_problem(tmp_path, known, body, message):
    errors = manual_errors(_manual(tmp_path / "m.yaml", body), known)

    assert any(message in error for error in errors), errors
    with pytest.raises(ValueError, match="ghi nhận thủ công"):
        read_manual(tmp_path / "m.yaml", known, SourceResult("manual"))


def test_repository_manual_file_is_valid(known):
    from testboard import paths
    assert manual_errors(paths.MANUAL_FILE, known) == []
