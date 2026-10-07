"""CLI: điều phối bước, mã thoát và bảng tóm tắt."""
import json
from datetime import datetime, timezone
from pathlib import Path

from evaluator import __main__ as cli
from evaluator.results import ERROR, FAILED, PASSED, StepResult

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
