"""Điểm mutation (MT-01…03): công thức, gộp cấu hình con theo số mutant, xử lý thiếu báo cáo; tóm tắt coverage."""
import json
from pathlib import Path

import pytest

from evaluator.steps import coverage, mutation_step as mt


def _report(**statuses: int) -> dict:
    """`_report(Killed=3, Survived=1)` → báo cáo mẫu theo schema mutation-testing-elements (mutant rải trên hai file)."""
    mutants = [{"id": f"{status}{index}", "status": status, "mutatorName": "String", "location": {"start": {"line": index + 1}}} for status, count in statuses.items() for index in range(count)]
    half = len(mutants) // 2
    return {"schemaVersion": "2", "files": {"A.cs": {"mutants": mutants[:half]}, "B.cs": {"mutants": mutants[half:]}}}


def test_score_formula_counts_timeout_as_killed_and_no_coverage_as_survived() -> None:
    counts = mt.counts_from_report(_report(Killed=6, Timeout=2, Survived=1, NoCoverage=1, Ignored=5, CompileError=3, RuntimeError=1))

    assert (counts.killed, counts.survived, counts.excluded) == (8, 2, 9)
    assert counts.score == 80.0


def test_score_is_none_without_countable_mutants() -> None:
    assert mt.counts_from_report(_report(Ignored=4)).score is None
    assert mt.counts_from_report({}).score is None


def test_stryker_score_and_merge() -> None:
    application = mt.counts_from_report(_report(Killed=60, Survived=40))  # 60 %
    infrastructure = mt.counts_from_report(_report(Killed=90, Survived=10))  # 90 %

    merged = mt.merge({"a": application, "b": infrastructure})

    assert application.score == 60.0 and infrastructure.score == 90.0
    assert merged.score == 75.0, "gộp theo số mutant (150/200), không phải trung bình hai điểm (75 cũng là trung bình nhưng sai khi kích thước khác nhau)"
    skewed = mt.merge({"a": mt.counts_from_report(_report(Killed=1, Survived=9)), "b": mt.counts_from_report(_report(Killed=90, Survived=0))})
    assert round(skewed.score, 1) == 91.0, "gộp theo số mutant: 91/100"


def test_surviving_mutants_listed_with_location() -> None:
    survivors = mt.surviving_mutants(_report(Killed=2, Survived=1, NoCoverage=1))

    assert sorted(item["status"] for item in survivors) == ["NoCoverage", "Survived"]
    assert all(item["file"] in ("A.cs", "B.cs") and item["line"] for item in survivors)


def _fake_stryker(scores: dict[str, tuple[int, int]]):
    """Lệnh giả: ghi `reports/mutation-report.json` theo (killed, survived) của từng cấu hình; cấu hình thiếu trong `scores` thì không tạo báo cáo."""

    def command(cmd: list[str], cwd: Path, env: dict, log: Path) -> int:
        config = Path(cmd[cmd.index("--config-file") + 1]).stem
        output = Path(cmd[cmd.index("--output") + 1])
        log.parent.mkdir(parents=True, exist_ok=True)
        log.write_text("log", encoding="utf-8")
        if config in scores:
            killed, survived = scores[config]
            (output / "reports").mkdir(parents=True, exist_ok=True)
            (output / "reports" / "mutation-report.json").write_text(json.dumps(_report(Killed=killed, Survived=survived)), encoding="utf-8")
        return 0

    return command


def test_run_mutation_passes_and_fails_per_threshold(tmp_path: Path) -> None:
    scores = {"mt01-sdk": (80, 20), "mt02-strategies-default": (30, 20), "mt02-strategies-mockalternative": (10, 0), "mt03-core-application": (60, 40), "mt03-core-infrastructure": (95, 5)}

    results, notes, artifacts = mt.run_mutation(tmp_path / "work", tmp_path / "out", {}, _fake_stryker(scores))

    assert results["MT-01"]["passed"] and results["MT-01"]["score"] == 80.0
    assert results["MT-02"]["score"] == pytest.approx(66.667, abs=0.001) and not results["MT-02"]["passed"], "66,7 % < 70"
    assert results["MT-03"]["passed"] and results["MT-03"]["score"] == 77.5
    assert any("MT-02" in note for note in notes) and artifacts


def test_run_mutation_missing_report_is_not_a_pass(tmp_path: Path) -> None:
    results, notes, _ = mt.run_mutation(tmp_path / "work", tmp_path / "out", {}, _fake_stryker({"mt01-sdk": (100, 0)}), {"MT-01": ("mt01-sdk",), "MT-02": ("mt02-strategies-default",)})

    assert results["MT-01"]["passed"]
    assert results["MT-02"]["score"] is None and not results["MT-02"]["passed"]
    assert any("không tạo báo cáo" in note for note in notes)


def test_coverage_summary_parses_both_decimal_styles() -> None:
    assert coverage.summarize("Line coverage: 87.5%\nBranch coverage: 71,2%") == {"line": 87.5, "branch": 71.2}
    assert coverage.summarize("không có số liệu") == {"line": None, "branch": None}


def test_coverage_without_cobertura_is_skipped_with_reason(tmp_path: Path) -> None:
    rates, notes, artifacts = coverage.generate(tmp_path, tmp_path / "tests", tmp_path / "cov", {}, lambda *a: 0)

    assert rates is None and "bước a" in notes[0] and artifacts == []


def test_coverage_merges_all_cobertura_files(tmp_path: Path) -> None:
    tests = tmp_path / "tests"
    for name in ("g1", "g2"):
        (tests / name).mkdir(parents=True)
        (tests / name / "coverage.cobertura.xml").write_text("<coverage/>", encoding="utf-8")
    seen = []

    def command(cmd: list[str], cwd: Path, env: dict, log: Path) -> int:
        seen.append(cmd)
        target = Path(next(arg for arg in cmd if arg.startswith("-targetdir:"))[len("-targetdir:"):])
        (target / "Summary.txt").write_text("Line coverage: 90.1%\nBranch coverage: 80%", encoding="utf-8")
        return 0

    rates, notes, artifacts = coverage.generate(tmp_path, tests, tmp_path / "raw" / "coverage", {}, command)

    assert rates == {"line": 90.1, "branch": 80.0}
    reports = next(arg for arg in seen[0] if arg.startswith("-reports:"))
    assert reports.count("coverage.cobertura.xml") == 2 and "-assemblyfilters:-TADSS.UnitTests;-TADSS.IntegrationTests" in seen[0]
    assert any(path.endswith("Summary.txt") for path in artifacts)
