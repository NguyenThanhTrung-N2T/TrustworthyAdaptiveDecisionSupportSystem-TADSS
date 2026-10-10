"""Bước (a) với lệnh giả: kiểm thứ tự lệnh, trạng thái và việc ghép catalog (không chạy dotnet/docker thật)."""
import shutil
from pathlib import Path

import pytest

from conftest import FIXTURES
from evaluator.clients.grafana import DashboardCheck
from evaluator.run_context import build_context
from evaluator.steps import tests_step
from datetime import datetime, timezone


def _context(tmp_path: Path):
    repo = tmp_path / "repo"
    (repo / "src").mkdir(parents=True, exist_ok=True)
    (repo / "src" / "Program.cs").write_text("x", encoding="utf-8")
    (repo / ".env").write_text("SECRET", encoding="utf-8")
    env = {"EVAL_REPO": str(repo), "EVAL_WORK": str(tmp_path / "work"), "EVAL_EVIDENCE": str(tmp_path / "evidence"), "EVAL_STEPS": "a"}
    return build_context(env, datetime(2026, 10, 8, tzinfo=timezone.utc))


class FakeCommands:
    """Ghi lại lệnh và giả lập sản phẩm (trx + chỉ mục) của hai lượt dotnet test; `codes` đặt mã thoát theo tiền tố tên lệnh."""

    def __init__(self, codes: dict[str, int] | None = None, produce_results: bool = True) -> None:
        self.codes = codes or {}
        self.produce_results = produce_results
        self.calls: list[list[str]] = []

    def __call__(self, command: list[str], cwd: Path, env: dict[str, str], log: Path) -> int:
        self.calls.append(command)
        log.parent.mkdir(parents=True, exist_ok=True)
        log.write_text("log", encoding="utf-8")
        if command[:2] == ["dotnet", "test"] and self.produce_results:
            directory = Path(env["TADSS_CATALOG_INDEX_DIR"])
            name = "unit" if "UnitTests" in command[2] else "integration"
            shutil.copy(FIXTURES / "sample.trx", directory / f"{name}.trx")
            shutil.copy(FIXTURES / "index.json", directory / f"catalog-index.{name}.json")
        key = command[1] if command[0] == "dotnet" else command[0]
        return self.codes.get(key, 0)


def _dashboard(passed: bool):
    return lambda base_url: DashboardCheck(passed, "chi tiết")


def test_step_a_runs_commands_in_order_and_fails_on_red_test(tmp_path: Path) -> None:
    commands = FakeCommands()

    result = tests_step.run(_context(tmp_path), commands, _dashboard(True))

    verbs = [(call[0], call[1]) for call in commands.calls]
    assert verbs == [("dotnet", "restore")] * 2 + [("dotnet", "build")] * 2 + [("dotnet", "test")] * 2 + [
        ("bash", "tools/expose/verify-expose.sh"), ("bash", "tools/observability/verify-observability.sh"), ("bash", "tools/evidence/sql/verify-export.sh"),
        ("python", "-m"), ("python", "-m"),
    ]
    assert [call[-1] for call in commands.calls if call[0] == "python"] == ["tools/evaluation", "tools/evidence"], "hai bộ pytest của công cụ đánh giá"
    assert result.status == "failed", "trx mẫu có một ca đỏ"
    assert any("UT-02" in note and "FI-03" in note for note in result.notes)
    assert (tmp_path / "evidence" / "EV-20261008-EVAL-quick-r1" / "raw" / "tests" / "catalog-results.csv").exists()
    assert not (tmp_path / "work" / ".env").exists(), "bí mật không được vào thư mục làm việc"


def test_step_a_passes_when_everything_is_green(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    green = FIXTURES / "sample.trx"
    text = green.read_text(encoding="utf-8").replace('outcome="Failed"', 'outcome="Passed"')
    patched = tmp_path / "green.trx"
    patched.write_text(text, encoding="utf-8")
    monkeypatch.setattr(shutil, "copy", lambda src, dst: Path(dst).write_bytes((patched if str(src).endswith(".trx") else Path(src)).read_bytes()))

    result = tests_step.run(_context(tmp_path), FakeCommands(), _dashboard(True))

    assert result.status == "passed", result.notes


@pytest.mark.parametrize("failing", ["restore", "build"])
def test_step_a_is_error_when_environment_cannot_be_built(tmp_path: Path, failing: str) -> None:
    commands = FakeCommands({failing: 1})

    result = tests_step.run(_context(tmp_path), commands, _dashboard(True))

    assert result.status == "error" and any(failing in note for note in result.notes)
    assert not any(call[1] == "test" for call in commands.calls if call[0] == "dotnet"), "không chạy test khi chưa build được"


def test_step_a_fails_when_dashboard_or_expose_check_fails(tmp_path: Path) -> None:
    dashboard_failed = tests_step.run(_context(tmp_path), FakeCommands(), _dashboard(False))
    assert any("OB-2" in note and "không đạt" in note for note in dashboard_failed.notes)

    expose_failed = tests_step.run(_context(tmp_path), FakeCommands({"bash": 1}), _dashboard(True))
    assert any("verify-expose.sh: không đạt" in note for note in expose_failed.notes)


def test_step_a_reports_missing_results_as_failure(tmp_path: Path) -> None:
    result = tests_step.run(_context(tmp_path), FakeCommands(produce_results=False), _dashboard(True))

    assert result.status == "failed" and any("Thiếu kết quả" in note for note in result.notes)


def test_step_a_is_error_when_secret_leaks_into_workspace(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    from evaluator import workspace

    monkeypatch.setattr(workspace, "_is_secret", lambda name: False)

    result = tests_step.run(_context(tmp_path), FakeCommands(), _dashboard(True))

    assert result.status == "error" and "bí mật" in result.notes[0]


class EnvRecorder(FakeCommands):
    """Như FakeCommands, ghi thêm môi trường của từng lệnh."""

    def __init__(self, codes: dict[str, int] | None = None) -> None:
        super().__init__(codes)
        self.envs: list[dict[str, str]] = []

    def __call__(self, command: list[str], cwd: Path, env: dict[str, str], log: Path) -> int:
        self.envs.append(env)
        return super().__call__(command, cwd, env, log)


def test_step_a_runs_observability_in_copy_mode_and_pytest_without_run_variables(tmp_path: Path) -> None:
    commands = EnvRecorder()

    tests_step.run(_context(tmp_path), commands, _dashboard(True))

    by_command = {" ".join(call): env for call, env in zip(commands.calls, commands.envs)}
    assert by_command["bash tools/observability/verify-observability.sh"]["OBS_DASHBOARDS_BY_COPY"] == "1", "trong container: dashboard copy vào Grafana thay cho mount"
    assert "OBS_DASHBOARDS_BY_COPY" not in by_command["bash tools/expose/verify-expose.sh"]
    pytest_envs = [env for call, env in zip(commands.calls, commands.envs) if call[0] == "python"]
    assert pytest_envs and all(not name.startswith(("EVAL_", "TADSS_")) for env in pytest_envs for name in env), "biến của lượt chạy không lọt vào pytest"


def test_step_a_fails_and_names_log_when_pytest_fails(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    green = FIXTURES / "sample.trx"
    patched = tmp_path / "green.trx"
    patched.write_text(green.read_text(encoding="utf-8").replace('outcome="Failed"', 'outcome="Passed"'), encoding="utf-8")
    monkeypatch.setattr(shutil, "copy", lambda src, dst: Path(dst).write_bytes((patched if str(src).endswith(".trx") else Path(src)).read_bytes()))

    result = tests_step.run(_context(tmp_path), FakeCommands({"python": 1}), _dashboard(True))

    assert result.status == "failed"
    assert any(note.startswith("pytest tools/evidence: không đạt") and "pytest-evidence.log" in note for note in result.notes)
    assert any(note == "verify-observability.sh: đạt" for note in result.notes) and any(note == "verify-export.sh: đạt" for note in result.notes)
