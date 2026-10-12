"""CLI của evaluator: `python -m evaluator` (entrypoint của image). Đọc EVAL_MODE/EVAL_STEPS, chạy tuần tự các bước, in bảng tóm tắt và thoát với mã 0/1/2 (O-14)."""
from __future__ import annotations

import json
import os
import sys
from typing import Callable

from evaluator import machine, results, workspace
from evaluator.results import ERROR, StepResult, exit_code, skipped_result, utc_now
from evaluator.run_context import ConfigurationError, RunContext, build_context
from evaluator.steps import analysis_step, dataset_step, evidence_step, incident_step, load_step, tests_step

StepFunction = Callable[[RunContext], StepResult]
# Các bước đã hiện thực; bước còn lại ghi `skipped` kèm lý do
STEPS: dict[str, StepFunction] = {"a": tests_step.run, "b": load_step.run, "c": incident_step.run, "d": analysis_step.run, "e": dataset_step.run, "f": evidence_step.run}


def run_steps(context: RunContext, steps: dict[str, StepFunction]) -> list[StepResult]:
    """Chạy các bước được chọn theo thứ tự; bước sau chạy cả khi bước trước `failed`, còn `error` hạ tầng thì các bước còn lại `skipped`."""
    outcomes: list[StepResult] = []
    infrastructure_error = False
    for name in context.steps:
        if infrastructure_error:
            outcomes.append(skipped_result(name, "bỏ qua vì một bước trước gặp lỗi hạ tầng"))
            continue
        function = steps.get(name)
        if function is None:
            outcomes.append(skipped_result(name, "bước chưa được hiện thực"))
            continue
        try:
            outcome = function(context)
        except Exception as error:  # noqa: BLE001 - mọi lỗi bất ngờ của một bước là lỗi hạ tầng, không làm mất các bước đã chạy
            now = utc_now()
            outcome = StepResult(name, ERROR, now, now, notes=[f"Lỗi bất ngờ: {type(error).__name__}: {error}"])
        outcomes.append(outcome)
        context.outcomes.append(outcome)
        infrastructure_error = outcome.status == ERROR
    return outcomes


def format_summary(context: RunContext, outcomes: list[StepResult]) -> str:
    """Bảng tóm tắt in ra màn hình (tiếng Việt)."""
    lines = [f"Lượt chạy {context.run_id} (chế độ {context.mode}); minh chứng: {context.evidence_dir}", "", f"{'Bước':<6}{'Trạng thái':<12}{'Thời gian':<12}Ghi chú"]
    for outcome in outcomes:
        first, *rest = outcome.notes or [""]
        lines.append(f"{outcome.name:<6}{outcome.status:<12}{outcome.duration_seconds:>8.0f} s  {first}")
        lines.extend(f"{'':<30}{note}" for note in rest)
    code = exit_code(outcomes)
    lines += ["", {0: "KẾT QUẢ: đạt mọi tiêu chí.", 1: "KẾT QUẢ: chạy hết nhưng có tiêu chí không đạt.", 2: "KẾT QUẢ: lỗi hạ tầng hoặc không chạy hết."}[code]]
    # Khi chạy bằng `docker compose up` không có run.sh in hộ: đường dẫn trên máy (thư mục evidence/ của repo được mount vào /evidence)
    lines.append(f"Báo cáo trên máy: evidence/{context.run_id}/report.html (mã thoát {code}: 0 đạt hết, 1 có tiêu chí không đạt, 2 lỗi hạ tầng)")
    return "\n".join(lines)


def write_steps_file(context: RunContext, outcomes: list[StepResult], describe_machine: Callable[[list[str]], dict[str, object]] = machine.collect) -> None:
    """`steps.json`: kết quả từng bước + thông số máy (Task 11 gom vào manifest)."""
    context.evidence_dir.mkdir(parents=True, exist_ok=True)
    git = workspace.read_git_info(context.repo_dir)
    payload = {
        "run_id": context.run_id,
        "mode": context.mode,
        "started_at": context.started_at.isoformat(),
        "commit": git.commit,
        "dirty": git.dirty,
        "machine": describe_machine(machine.IMAGES),
        "steps": [outcome.to_dict() for outcome in outcomes],
    }
    (context.evidence_dir / "steps.json").write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def main(
    env: dict[str, str] | None = None,
    steps: dict[str, StepFunction] | None = None,
    describe_machine: Callable[[list[str]], dict[str, object]] = machine.collect,
) -> int:
    environment = dict(os.environ) if env is None else env
    try:
        context = build_context(environment, results.utc_now())
    except ConfigurationError as error:
        print(f"LỖI cấu hình: {error}", file=sys.stderr)
        return results.EXIT_INFRASTRUCTURE_ERROR
    outcomes = run_steps(context, STEPS if steps is None else steps)
    write_steps_file(context, outcomes, describe_machine)
    print(format_summary(context, outcomes))
    return exit_code(outcomes)


if __name__ == "__main__":
    sys.exit(main())
