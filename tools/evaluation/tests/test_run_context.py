"""run_id, EVAL_STEPS và ngữ cảnh lượt chạy."""
from datetime import datetime, timezone
from pathlib import Path

import pytest

from evaluator import run_context

DAY = datetime(2026, 10, 8, 9, 0, tzinfo=timezone.utc)


def test_run_id_next_sequence(tmp_path: Path) -> None:
    (tmp_path / "EV-20261008-EVAL-quick-r1").mkdir()
    (tmp_path / "EV-20261008-EVAL-full-r2").mkdir()
    (tmp_path / "EV-20261007-EVAL-quick-r9").mkdir()
    (tmp_path / "not-a-run").mkdir()

    assert run_context.next_run_id(tmp_path, "quick", DAY) == "EV-20261008-EVAL-quick-r3"
    assert run_context.next_run_id(tmp_path, "full", datetime(2026, 10, 9, tzinfo=timezone.utc)) == "EV-20261009-EVAL-full-r1"


def test_run_id_without_evidence_directory(tmp_path: Path) -> None:
    assert run_context.next_run_id(tmp_path / "missing", "quick", DAY) == "EV-20261008-EVAL-quick-r1"


def test_run_id_rejects_unknown_mode(tmp_path: Path) -> None:
    with pytest.raises(run_context.ConfigurationError):
        run_context.next_run_id(tmp_path, "turbo", DAY)


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        (None, ("a", "b", "c", "d", "e", "f")), ("", ("a", "b", "c", "d", "e", "f")), ("   ", ("a", "b", "c", "d", "e", "f")),
        ("b,a", ("a", "b")), (" E , c ", ("c", "e")), ("f,e", ("e", "f")), ("e,f", ("e", "f")),
    ],
)
def test_parse_steps_a_to_f(value: str | None, expected: tuple[str, ...]) -> None:
    assert run_context.parse_steps(value) == expected


def test_all_steps_are_a_to_f_with_evidence_pack_last() -> None:
    assert run_context.ALL_STEPS == ("a", "b", "c", "d", "e", "f")


@pytest.mark.parametrize("value", ["a,x", "g", "e,g"])
def test_parse_steps_rejects_unknown(value: str) -> None:
    with pytest.raises(run_context.ConfigurationError, match="g|x"):
        run_context.parse_steps(value)


def test_build_context_reads_environment(tmp_path: Path) -> None:
    env = {"EVAL_MODE": "FULL", "EVAL_STEPS": "a", "EVAL_EVIDENCE": str(tmp_path), "EVAL_GRAFANA_URL": "http://g:3000", "EVAL_API_KEY": "k"}

    context = run_context.build_context(env, DAY)

    assert (context.mode, context.steps, context.run_id) == ("full", ("a",), "EV-20261008-EVAL-full-r1")
    assert context.endpoints.grafana == "http://g:3000" and context.endpoints.tadss == "http://eval-tadss-1:8080" and context.endpoints.api_key == "k"
    assert context.raw_dir("tests") == tmp_path / "EV-20261008-EVAL-full-r1" / "raw" / "tests"


def test_build_context_dataset_defaults(tmp_path: Path) -> None:
    context = run_context.build_context({"EVAL_EVIDENCE": str(tmp_path)}, DAY)

    assert context.endpoints.portability == "http://eval-tadss-portability:8080"
    assert context.datasets_private == Path("/datasets/private") and context.datasets_cache == Path("/datasets/cache")
    assert context.workers == 8


def test_build_context_dataset_environment(tmp_path: Path) -> None:
    env = {
        "EVAL_EVIDENCE": str(tmp_path), "EVAL_PT_URL": "http://pt:9000", "EVAL_DATASETS_PRIVATE": str(tmp_path / "private"),
        "EVAL_DATASETS_CACHE": str(tmp_path / "cache"), "EVAL_WORKERS": "16",
    }

    context = run_context.build_context(env, DAY)

    assert context.endpoints.portability == "http://pt:9000"
    assert (context.datasets_private, context.datasets_cache, context.workers) == (tmp_path / "private", tmp_path / "cache", 16)


@pytest.mark.parametrize("value", ["0", "-2", "abc", "1.5"])
def test_build_context_rejects_invalid_workers(tmp_path: Path, value: str) -> None:
    with pytest.raises(run_context.ConfigurationError, match="EVAL_WORKERS"):
        run_context.build_context({"EVAL_EVIDENCE": str(tmp_path), "EVAL_WORKERS": value}, DAY)


def test_build_context_blank_workers_uses_default(tmp_path: Path) -> None:
    assert run_context.build_context({"EVAL_EVIDENCE": str(tmp_path), "EVAL_WORKERS": "  "}, DAY).workers == 8
