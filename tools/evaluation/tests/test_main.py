"""CLI: điều phối bước, mã thoát và bảng tóm tắt."""
import json
from datetime import datetime, timezone
from pathlib import Path

from evaluator import __main__ as cli
from evaluator.results import ERROR, FAILED, PASSED, StepResult
from evaluator.steps import dataset_step, evidence_step

NOW = datetime(2026, 10, 8, tzinfo=timezone.utc)
NO_MACHINE = lambda images: {}  # không gọi docker thật trong test


def _step(name: str, status: str):
    return lambda context: StepResult(name, status, NOW, NOW, notes=[f"bước {name}"])


def _env(tmp_path: Path, steps: str = "a,b,c") -> dict[str, str]:
    return {"EVAL_EVIDENCE": str(tmp_path / "evidence"), "EVAL_REPO": str(tmp_path / "repo"), "EVAL_STEPS": steps}


def test_error_marks_remaining_steps_skipped(tmp_path: Path, capsys) -> None:
    code = cli.main(_env(tmp_path), {"a": _step("a", ERROR), "b": _step("b", PASSED), "c": _step("c", PASSED)}, NO_MACHINE)

    output = capsys.readouterr().out
    assert code == 2 and "lỗi hạ tầng" in output
    saved = json.loads(next((tmp_path / "evidence").glob("*/steps.json")).read_text(encoding="utf-8"))
    assert [step["status"] for step in saved["steps"]] == ["error", "skipped", "skipped"]


def test_failed_step_does_not_stop_later_steps(tmp_path: Path) -> None:
    ran: list[str] = []

    def tracking(name: str, status: str):
        def run(context):
            ran.append(name)
            return StepResult(name, status, NOW, NOW)

        return run

    code = cli.main(_env(tmp_path, "a,b"), {"a": tracking("a", FAILED), "b": tracking("b", PASSED)}, NO_MACHINE)

    assert code == 1 and ran == ["a", "b"]


def test_unimplemented_step_is_skipped_not_error(tmp_path: Path) -> None:
    code = cli.main(_env(tmp_path, "a,d"), {"a": _step("a", PASSED)}, NO_MACHINE)

    assert code == 0


def test_unexpected_exception_becomes_error(tmp_path: Path) -> None:
    def boom(context):
        raise RuntimeError("hỏng")

    assert cli.main(_env(tmp_path, "a"), {"a": boom}, NO_MACHINE) == 2


def test_invalid_configuration_exits_2(tmp_path: Path, capsys) -> None:
    code = cli.main({**_env(tmp_path), "EVAL_MODE": "turbo"}, {}, NO_MACHINE)

    assert code == 2 and "EVAL_MODE" in capsys.readouterr().err


def test_summary_ends_with_host_report_path_and_exit_code(tmp_path: Path, capsys) -> None:
    code = cli.main(_env(tmp_path, "a,b"), {"a": _step("a", PASSED), "b": _step("b", FAILED)}, NO_MACHINE)

    last = capsys.readouterr().out.splitlines()[-1]
    run_id = next((tmp_path / "evidence").iterdir()).name
    assert code == 1
    assert last.startswith(f"Báo cáo trên máy: evidence/{run_id}/report.html") and "mã thoát 1" in last, "chạy bằng docker compose up vẫn thấy đường dẫn báo cáo trên máy"


def test_steps_registry_has_dataset_step_e_before_evidence_pack_f() -> None:
    assert list(cli.STEPS) == ["a", "b", "c", "d", "e", "f"]
    assert cli.STEPS["e"] is dataset_step.run and cli.STEPS["f"] is evidence_step.run
    assert (dataset_step.STEP_NAME, evidence_step.STEP_NAME) == ("e", "f")


def test_default_run_executes_steps_in_alphabetical_order_with_evidence_pack_last(tmp_path: Path) -> None:
    ran: list[str] = []

    def tracking(name: str):
        def run(context):
            ran.append(name)
            return StepResult(name, PASSED, NOW, NOW)

        return run

    code = cli.main({"EVAL_EVIDENCE": str(tmp_path / "evidence"), "EVAL_REPO": str(tmp_path / "repo")}, {name: tracking(name) for name in "abcdef"}, NO_MACHINE)

    assert code == 0 and ran == list("abcdef")


def test_summary_table_lists_dataset_step_e_and_sc7_line(tmp_path: Path, capsys) -> None:
    def step_e(context):
        return StepResult("e", PASSED, NOW, NOW, notes=["RP: gửi 215/215 request tới eval-proxy, 0 lỗi contract", "PT: gửi 10000/10000 request, 0 lỗi contract"])

    def step_f(context):
        return StepResult("f", PASSED, NOW, NOW, notes=["SC7: INSUFFICIENT_DATA — 10000 bản ghi gửi, 0 lỗi contract, 0 lần thử lại", "EV-1…EV-5: EV-1 đạt, EV-2 đạt, EV-3 đạt, EV-4 đạt, EV-5 đạt"])

    code = cli.main(_env(tmp_path, "e,f"), {"e": step_e, "f": step_f}, NO_MACHINE)

    lines = capsys.readouterr().out.splitlines()
    assert code == 0
    assert any(line.startswith("e ") and "RP: gửi 215/215" in line for line in lines), "dòng bước (e) kèm ghi chú RP"
    assert any("PT: gửi 10000/10000" in line for line in lines)
    assert any(line.startswith("f ") for line in lines) and any("SC7: INSUFFICIENT_DATA" in line for line in lines), "dòng SC7 nằm trong ghi chú bước (f)"
