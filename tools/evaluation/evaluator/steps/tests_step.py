"""Bước (a): toàn bộ bộ test gộp một lượt, kết quả gom theo mã danh mục (D-12)."""
from __future__ import annotations

import subprocess
from pathlib import Path
from typing import Callable

from evaluator import catalog, workspace
from evaluator.clients import grafana
from evaluator.results import ERROR, FAILED, PASSED, StepResult, utc_now
from evaluator.run_context import RunContext

Command = Callable[[list[str], Path, dict[str, str], Path], int]
STEP_NAME = "a"
TEST_PROJECTS = ("TADSS.UnitTests", "TADSS.IntegrationTests")
TESTCONTAINERS_HOST = "host.docker.internal"
EXPOSE_SCRIPT = "tools/expose/verify-expose.sh"
OBSERVABILITY_SKIP_REASON = (
    "verify-observability.sh không chạy trong container evaluator: file compose mount ./docker/grafana/... bằng đường dẫn của container (/work), "
    "daemon Docker (máy chủ) không thấy đường dẫn đó nên Grafana sẽ không có dashboard. OB-2 vẫn được kiểm trên eval-grafana (đường dẫn mount là của máy chủ); "
    "kết quả verify-observability.sh lấy từ job CI observability-check."
)


def run_command(command: list[str], cwd: Path, env: dict[str, str], log_file: Path) -> int:
    """Chạy một lệnh, ghi cả stdout+stderr vào `log_file` (không in lên màn hình để bảng tóm tắt gọn); trả mã thoát."""
    log_file.parent.mkdir(parents=True, exist_ok=True)
    with log_file.open("w", encoding="utf-8") as handle:
        handle.write("$ " + " ".join(command) + "\n")
        handle.flush()
        return subprocess.run(command, cwd=cwd, env=env, stdout=handle, stderr=subprocess.STDOUT, check=False).returncode


def _build_commands() -> list[tuple[str, list[str]]]:
    commands: list[tuple[str, list[str]]] = []
    for project in TEST_PROJECTS:
        commands.append((f"restore-{project}", ["dotnet", "restore", f"tests/{project}", "--locked-mode", "-v", "q"]))
    for project in TEST_PROJECTS:
        commands.append((f"build-{project}", ["dotnet", "build", f"tests/{project}", "-c", "Release", "--no-restore", "--warnaserror", "-nologo", "-v", "q"]))
    return commands


def _test_command(project: str, trx: str, results: Path) -> list[str]:
    return [
        "dotnet", "test", f"tests/{project}", "-c", "Release", "--no-build", "-nologo",
        "--logger", f"trx;LogFileName={trx}", "--results-directory", str(results),
        "--collect:XPlat Code Coverage",
    ]


def run(context: RunContext, command: Command = run_command, check_dashboard=grafana.check_dashboard) -> StepResult:
    """
    Trên bản sao `/work` của mã nguồn: restore khóa và build Release `--warnaserror` hai project test, unit + IT (trx + coverlet cobertura, chỉ mục catalog), ghép `catalog-results.csv`,
    `verify-expose.sh` và OB-2 trên Grafana của stack đánh giá. `failed` nếu có test/kiểm tra đỏ; `error` nếu không dựng được môi trường hoặc không build được.
    """
    started = utc_now()
    results_dir = context.raw_dir("tests")
    notes: list[str] = []
    artifacts: list[str] = []
    env = {**context.env, "TADSS_CATALOG_INDEX_DIR": str(results_dir), "TESTCONTAINERS_HOST_OVERRIDE": TESTCONTAINERS_HOST, "DOTNET_NOLOGO": "1"}

    def finish(status: str) -> StepResult:
        return StepResult(STEP_NAME, status, started, utc_now(), artifacts, notes)

    try:
        copied = workspace.prepare_workspace(context.repo_dir, context.work_dir)
    except (workspace.WorkspaceError, OSError) as error:
        notes.append(f"Không dựng được thư mục làm việc: {error}")
        return finish(ERROR)
    notes.append(f"Đã copy {copied} file vào {context.work_dir} (không có file bí mật)")

    work = context.work_dir
    # SDK .NET 8 của image không đọc được TADSS.slnx (định dạng slnx cần SDK ≥ 9): restore và build theo hai project test, kéo theo mọi project src qua tham chiếu
    for label, cmd in _build_commands():
        log = results_dir / f"{label}.log"
        artifacts.append(str(log))
        if command(cmd, work, env, log) != 0:
            notes.append(f"{label} thất bại (xem {log.name})")
            return finish(ERROR)

    failed = False
    for project, trx in zip(TEST_PROJECTS, ("unit.trx", "integration.trx")):
        log = results_dir / f"{trx}.log"
        artifacts.append(str(log))
        code = command(_test_command(project, trx, results_dir), work, env, log)
        notes.append(f"{project}: {'đạt' if code == 0 else 'có test đỏ hoặc lỗi chạy'} (mã {code})")
        failed = failed or code != 0

    failed = _join_catalog(results_dir, notes, artifacts) or failed

    expose_log = results_dir / "verify-expose.log"
    artifacts.append(str(expose_log))
    expose_code = command(["bash", EXPOSE_SCRIPT], work, env, expose_log)
    notes.append(f"verify-expose.sh: {'TẤT CẢ ĐẠT' if expose_code == 0 else f'không đạt (mã {expose_code})'}")
    notes.append(OBSERVABILITY_SKIP_REASON)
    failed = failed or expose_code != 0

    dashboard = check_dashboard(context.endpoints.grafana)
    notes.append(f"OB-2 (eval-grafana): {'đạt' if dashboard.passed else 'không đạt'} — {dashboard.detail}")
    failed = failed or not dashboard.passed

    return finish(FAILED if failed else PASSED)


def _join_catalog(results_dir: Path, notes: list[str], artifacts: list[str]) -> bool:
    """Ghép trx + chỉ mục → catalog-results.csv. Trả True nếu có vấn đề (thiếu trx/chỉ mục, test lạ, hoặc có ca đỏ)."""
    trx_files = sorted(results_dir.glob("*.trx"))
    index_files = sorted(results_dir.glob("catalog-index.*.json"))
    if len(trx_files) < 2 or len(index_files) < 2:
        notes.append(f"Thiếu kết quả: {len(trx_files)} file trx, {len(index_files)} chỉ mục (cần 2 + 2)")
        return True
    try:
        outcomes = [item for path in trx_files for item in catalog.parse_trx(path)]
        rows = catalog.join(outcomes, catalog.load_index(index_files))
    except catalog.CatalogJoinError as error:
        notes.append(str(error))
        return True
    csv_path = results_dir / "catalog-results.csv"
    catalog.write_csv(rows, csv_path)
    artifacts.append(str(csv_path))
    summary = catalog.summarize(rows)
    red = sorted(code for code, counts in summary.items() if counts["failed"])
    notes.append(f"{len(outcomes)} kết quả test → {len(rows)} dòng theo {len(summary)} mã danh mục" + (f"; mã có ca đỏ: {', '.join(red)}" if red else ""))
    return bool(red)
