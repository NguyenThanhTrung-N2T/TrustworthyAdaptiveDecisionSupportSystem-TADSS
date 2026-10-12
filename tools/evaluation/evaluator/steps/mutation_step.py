"""Mutation testing (MT-01…03, O-20): chạy Stryker.NET theo từng cấu hình trong `stryker/`, tính điểm từ `mutation-report.json`, gộp các cấu hình con theo số mutant."""
from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from evaluator.steps.tests_step import run_command

THRESHOLD = 70.0
# MT → các cấu hình con (mỗi cấu hình mutate một project; điểm MT = gộp theo số mutant)
TARGETS: dict[str, tuple[str, ...]] = {
    "MT-01": ("mt01-sdk",),
    "MT-02": ("mt02-strategies-default", "mt02-strategies-mockalternative"),
    "MT-03": ("mt03-core-application", "mt03-core-infrastructure"),
}
SCORE_FORMULA = "điểm = (Killed + Timeout) / (Killed + Timeout + Survived + NoCoverage); không tính Ignored, CompileError, RuntimeError"
KILLED_STATUSES = ("Killed", "Timeout")
UNKILLED_STATUSES = ("Survived", "NoCoverage")
EXCLUDED_STATUSES = ("Ignored", "CompileError", "RuntimeError", "Pending")

Command = Callable[[list[str], Path, dict[str, str], Path], int]


@dataclass(frozen=True)
class Counts:
    """Số mutant theo nhóm trạng thái của Stryker."""

    killed: int = 0
    survived: int = 0
    excluded: int = 0

    def __add__(self, other: "Counts") -> "Counts":
        return Counts(self.killed + other.killed, self.survived + other.survived, self.excluded + other.excluded)

    @property
    def total(self) -> int:
        return self.killed + self.survived

    @property
    def score(self) -> float | None:
        """Phần trăm; None khi không có mutant nào được tính (không kết luận được)."""
        return None if self.total == 0 else 100.0 * self.killed / self.total


def counts_from_report(report: dict) -> Counts:
    """Đếm mutant trong `mutation-report.json` (schema mutation-testing-elements: `files → {path: {mutants: [{status}]}}`)."""
    killed = survived = excluded = 0
    for file in report.get("files", {}).values():
        for mutant in file.get("mutants", []):
            status = mutant.get("status")
            if status in KILLED_STATUSES:
                killed += 1
            elif status in UNKILLED_STATUSES:
                survived += 1
            elif status in EXCLUDED_STATUSES:
                excluded += 1
    return Counts(killed, survived, excluded)


def surviving_mutants(report: dict) -> list[dict]:
    """Danh sách mutant sống/không phủ để thêm test: `{file, line, mutator, status, replacement}`."""
    found = []
    for path, file in report.get("files", {}).items():
        for mutant in file.get("mutants", []):
            if mutant.get("status") in UNKILLED_STATUSES:
                found.append(
                    {
                        "file": path,
                        "line": mutant.get("location", {}).get("start", {}).get("line"),
                        "mutator": mutant.get("mutatorName"),
                        "status": mutant["status"],
                        "replacement": mutant.get("replacement"),
                    }
                )
    return found


def find_report(output_dir: Path) -> Path | None:
    """Stryker ghi `<output>/reports/mutation-report.json` (hoặc trong thư mục theo timestamp)."""
    direct = output_dir / "reports" / "mutation-report.json"
    if direct.exists():
        return direct
    candidates = sorted(output_dir.rglob("mutation-report.json"))
    return candidates[-1] if candidates else None


def merge(per_config: dict[str, Counts]) -> Counts:
    total = Counts()
    for counts in per_config.values():
        total = total + counts
    return total


def run_mutation(
    work_dir: Path,
    out_root: Path,
    env: dict[str, str],
    command: Command = run_command,
    targets: dict[str, tuple[str, ...]] = TARGETS,
) -> tuple[dict[str, dict], list[str], list[str]]:
    """
    Chạy tuần tự mọi cấu hình. Trả `(kết quả theo MT, ghi chú, tệp)`; kết quả mỗi MT: `{score, threshold, passed, killed, survived, excluded, configs}`.
    `score` là None nếu không cấu hình con nào cho báo cáo (Stryker không chạy được) → `passed` False.
    """
    results: dict[str, dict] = {}
    notes: list[str] = []
    artifacts: list[str] = []
    test_dir = work_dir / "tests" / "TADSS.UnitTests"
    for code, configs in targets.items():
        per_config: dict[str, Counts] = {}
        for config in configs:
            output = out_root / config
            log = out_root / f"{config}.log"
            artifacts.append(str(log))
            exit_code = command(
                ["dotnet", "tool", "run", "dotnet-stryker", "--config-file", str(work_dir / "stryker" / f"{config}.json"), "--output", str(output)],
                test_dir,
                env,
                log,
            )
            report = find_report(output)
            if report is None:
                notes.append(f"{code}/{config}: Stryker không tạo báo cáo (mã {exit_code}, xem {log.name})")
                continue
            document = json.loads(report.read_text(encoding="utf-8"))
            counts = counts_from_report(document)
            per_config[config] = counts
            artifacts.append(str(report))
            survivors = surviving_mutants(document)
            (out_root / f"{config}-survivors.json").write_text(json.dumps(survivors, ensure_ascii=False, indent=1), encoding="utf-8")
            notes.append(f"{code}/{config}: {_format(counts)}")
        total = merge(per_config)
        complete = len(per_config) == len(configs)
        score = total.score if complete else None
        results[code] = {
            "score": score,
            "threshold": THRESHOLD,
            "passed": score is not None and score >= THRESHOLD,
            "killed": total.killed,
            "survived": total.survived,
            "excluded": total.excluded,
            "configs": {name: {"killed": c.killed, "survived": c.survived, "score": c.score} for name, c in per_config.items()},
        }
        notes.append(f"{code}: {'chưa đủ báo cáo' if score is None else f'{score:.1f} %'} (ngưỡng {THRESHOLD:.0f} %)")
    return results, notes, artifacts


def _format(counts: Counts) -> str:
    score = "n/a" if counts.score is None else f"{counts.score:.1f} %"
    return f"{score} ({counts.killed} bị giết, {counts.survived} sống/không phủ, {counts.excluded} bỏ qua)"
