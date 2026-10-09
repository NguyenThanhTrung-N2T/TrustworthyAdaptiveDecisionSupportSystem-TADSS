"""Bước (d): Schemathesis (CT-03), coverage gộp và Stryker mutation (MT-01…03). Bản nhanh: CT-03 25 ví dụ, có coverage (khi có bước a), bỏ Stryker (O-20)."""
from __future__ import annotations

import json
from pathlib import Path
from typing import Callable

from evaluator import workspace
from evaluator.results import ERROR, FAILED, PASSED, StepResult, utc_now
from evaluator.run_context import RunContext
from evaluator.steps import contract_step, coverage, mutation_step
from evaluator.steps.tests_step import run_command

STEP_NAME = "d"
Command = Callable[[list[str], Path, dict[str, str], Path], int]


def _ensure_workspace(context: RunContext, notes: list[str]) -> bool:
    """Dùng lại `/work` của bước (a) nếu đã dựng trong cùng lượt; ngược lại copy mới (loại file bí mật). False nếu không dựng được."""
    if "a" in context.steps and (context.work_dir / "tests").is_dir():
        return True
    try:
        workspace.prepare_workspace(context.repo_dir, context.work_dir)
    except (workspace.WorkspaceError, OSError) as error:
        notes.append(f"Không dựng được thư mục làm việc: {error}")
        return False
    return True


def run(context: RunContext, command: Command = run_command) -> StepResult:
    """`failed` nếu CT-03 có lỗi chặn hoặc có MT dưới ngưỡng; `error` nếu Schemathesis/Stryker không chạy được."""
    started = utc_now()
    notes: list[str] = []
    artifacts: list[str] = []

    def finish(status: str) -> StepResult:
        return StepResult(STEP_NAME, status, started, utc_now(), artifacts, notes)

    if not _ensure_workspace(context, notes):
        return finish(ERROR)
    env = {**context.env, "DOTNET_NOLOGO": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
    failed = False

    contract, contract_notes, contract_files = contract_step.run_contract(context, context.raw_dir("contract"), command)
    notes += contract_notes
    artifacts += contract_files
    if contract is None:
        return finish(ERROR)
    failed = failed or not contract["passed"]

    restore_log = context.raw_dir("tests") / "dotnet-tool-restore.log"
    artifacts.append(str(restore_log))
    if command(["dotnet", "tool", "restore"], context.work_dir, env, restore_log) != 0:
        notes.append(f"dotnet tool restore thất bại (xem {restore_log.name})")
        return finish(ERROR)

    rates, coverage_notes, coverage_files = coverage.generate(context.work_dir, context.raw_dir("tests"), context.evidence_dir / "raw" / "coverage", env, command)
    notes += coverage_notes
    artifacts += coverage_files

    if context.mode == "full" or context.env.get("EVAL_MUTATION") == "1":
        results, mutation_notes, mutation_files = mutation_step.run_mutation(context.work_dir, context.raw_dir("mutation"), env, command)
        notes += mutation_notes
        artifacts += mutation_files
        summary = context.raw_dir("mutation") / "mutation-summary.json"
        summary.write_text(json.dumps({"formula": mutation_step.SCORE_FORMULA, "threshold": mutation_step.THRESHOLD, "results": results}, ensure_ascii=False, indent=2), encoding="utf-8")
        artifacts.append(str(summary))
        if any(result["score"] is None for result in results.values()):
            return finish(ERROR)
        failed = failed or not all(result["passed"] for result in results.values())
    else:
        notes.append("Stryker không chạy ở bản nhanh (O-20); SC8 ghi 'chưa đủ dữ liệu'. Chạy riêng: EVAL_MODE=full EVAL_STEPS=d (hoặc EVAL_MUTATION=1)")
    if rates is not None:
        (context.raw_dir("coverage") / "coverage-summary.json").write_text(json.dumps(rates), encoding="utf-8")
    return finish(FAILED if failed else PASSED)
