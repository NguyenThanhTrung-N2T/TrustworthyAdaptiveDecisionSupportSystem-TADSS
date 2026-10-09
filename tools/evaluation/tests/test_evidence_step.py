"""Bước (f) (Evidence Pack) với dữ liệu tổng hợp: dựng pack đủ tệp, KPI/EV đúng, báo cáo tự chứa, không lộ định danh gốc; D1 lỗi → error."""
import csv
import json
import re
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

from evaluator import machine
from evaluator.results import ERROR, FAILED, PASSED, StepResult
from evaluator.run_context import build_context
from evaluator.steps import evidence_step as step

NOW = datetime(2026, 10, 9, 1, 0, tzinfo=timezone.utc)
REPO = Path(__file__).resolve().parents[3]


def _iso(seconds: float) -> str:
    return (NOW + timedelta(seconds=seconds)).isoformat().replace("+00:00", "Z")


def _payload() -> str:
    return json.dumps({"items": [{"startTime": "2026-10-20T10:00:00+07:00"}], "customer": {"isGuest": False, "tier": "GOLD"}, "totalPrice": 100000, "currency": "VND", "requestedAt": "2026-10-09T01:00:00+00:00"})


def _db_row(decision_id: str, corr: str) -> tuple:
    return (decision_id, "00000000-aaaa", "branch-guid", "PRIMARY", corr, NOW, "DefaultDecisionStrategy", "1.0.0", "APPROVED", "APPROVED", 0.3, 30000, "OK", None,
            "AUTOMATED", 10, False, None, False, "h" * 64, _payload(), None, None, None, None, None, None)


def _setup(tmp_path: Path, mismatch: bool = False):
    evidence = tmp_path / "ev"
    env = {"EVAL_REPO": str(REPO), "EVAL_EVIDENCE": str(evidence), "EVAL_MODE": "quick", "EVAL_STEPS": "f", "EVAL_PG_DSN": "host=x", "EVAL_API_KEY": "k"}
    context = build_context(env, NOW)
    load = context.evidence_dir / "raw" / "load" / "lt01"
    load.mkdir(parents=True)
    entries = []
    for index in range(40):
        entries.append({"ts": _iso(index * 0.5), "scenario": "lt01", "phase": "main", "http_status": 200, "duration_ms": 20.0 + index, "decision_status": "APPROVED", "execution_type": "AUTOMATED",
                        "correlation_id": f"corr-{index}", "idempotency_key": f"key-{index}", "decision_id": f"dec-{index}", "repeat": False})
    (load / "requests.jsonl").write_text("\n".join(json.dumps(e) for e in entries) + "\n", encoding="utf-8")
    (context.evidence_dir / "raw" / "load" / "windows.json").write_text(
        json.dumps([{"scenario": "lt01", "name": "lt01", "run": 1, "start": _iso(0), "end": _iso(20), "warmup_seconds": 0, "main_start": _iso(0), "steps": []}]), encoding="utf-8")
    rows = [_db_row(f"dec-{i}", f"corr-{i}") for i in range(40 if not mismatch else 39)]
    return context, (lambda dsn: (lambda sql, params: rows))


def _query_ok(base, expr, at=None, fetch=None):
    return [{"metric": {}, "value": [0, "0.99"]}]


def test_evidence_step_builds_a_valid_pack(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(machine, "collect", lambda images, run=None: {"cpu_count": 8, "images": {"x": "sha"}, "docker": {}})
    context, fetch_factory = _setup(tmp_path)
    context.outcomes.append(StepResult("b", PASSED, NOW, NOW + timedelta(seconds=30)))

    result = step.run(context, fetch_factory, _query_ok)

    run_dir = context.evidence_dir
    assert result.status == PASSED, result.notes
    assert result.name == "f"
    for expected in ("manifest.json", "report.html", "raw/d1/decision_audit.csv", "raw/d2/timeseries.csv", "raw/d2/summary.csv", "config/k6-modes.json", "config/chaos-schedule.json", "config/compose-evaluation.yml", "raw/metrics/sc1-lt01.json"):
        assert (run_dir / expected).exists(), expected
    manifest = json.loads((run_dir / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["schemaVersion"] == "2.0.0" and manifest["environment"] == "E-EVAL" and manifest["mode"] == "quick"
    assert manifest["commits"]["smashcourtBe"] is None and manifest["commitNotes"]["smashcourtBe"]
    assert {item["path"] for item in manifest["files"]} >= {"report.html", "raw/d1/decision_audit.csv"}
    assert [check["passed"] for check in manifest["evChecks"]] == [True] * 5
    verdicts = {item["code"]: item["verdict"] for item in manifest["kpis"]}
    assert verdicts["SC1"] == "PASS" and verdicts["SC5"] == "NOT_APPLICABLE" and verdicts["SC8"] == "INSUFFICIENT_DATA"
    assert "00000000-aaaa" not in (run_dir / "raw" / "d1" / "decision_audit.csv").read_text(encoding="utf-8"), "định danh gốc đã được ẩn danh"
    html = (run_dir / "report.html").read_text(encoding="utf-8")
    assert not re.search(r"https?://", html) and "SC1" in html


def test_evidence_step_fails_when_audit_does_not_match_responses(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(machine, "collect", lambda images, run=None: {"images": {}})
    context, fetch_factory = _setup(tmp_path, mismatch=True)

    result = step.run(context, fetch_factory, _query_ok)

    assert result.status == FAILED
    assert any("EV-2" in note and "KHÔNG ĐẠT" in note for note in result.notes)
    assert "không dùng làm minh chứng" in (context.evidence_dir / "report.html").read_text(encoding="utf-8")


def test_evidence_step_sc1_fails_when_server_share_below_threshold(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(machine, "collect", lambda images, run=None: {"images": {}})
    context, fetch_factory = _setup(tmp_path)

    result = step.run(context, fetch_factory, lambda *a, **k: [{"metric": {}, "value": [0, "0.90"]}])

    assert result.status == FAILED and any(note.startswith("SC1: FAIL") for note in result.notes)


def test_evidence_step_without_dsn_or_with_broken_database_is_error(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(machine, "collect", lambda images, run=None: {"images": {}})
    context, _ = _setup(tmp_path)

    def broken(dsn):
        def fetch(sql, params):
            raise ConnectionError("không kết nối được")

        return fetch

    assert step.run(context, broken, _query_ok).status == ERROR
    del context.env["EVAL_PG_DSN"]
    assert step.run(context, broken, _query_ok).status == ERROR


def test_compose_extract_has_no_comments_or_secrets(tmp_path: Path) -> None:
    context, _ = _setup(tmp_path)
    step.write_config(context, context.evidence_dir)

    text = (context.evidence_dir / "config" / "compose-evaluation.yml").read_text(encoding="utf-8")

    assert "eval-tadss-1" in text and "appsettings" not in text and "tadss-runtime:" not in text
    assert not any(line.strip().startswith("#") for line in text.splitlines())


def test_verified_codes_follow_step_results_and_chaos_rows(tmp_path: Path) -> None:
    context, _ = _setup(tmp_path)
    context.outcomes += [StepResult("b", PASSED, NOW, NOW), StepResult("c", FAILED, NOW, NOW)]

    codes = step.verified_codes(context, {"MT-01": {"passed": True}, "MT-02": {"passed": False}}, [{"fi_id": "FI-02", "passed": "True"}, {"fi_id": "FI-03", "passed": "False"}])

    assert codes == {"LT-01", "LT-02", "FI-02", "MT-01"}


def test_write_d2_outputs_csv_with_spec_columns(tmp_path: Path) -> None:
    context, _ = _setup(tmp_path)

    entries = step.write_d2(context.evidence_dir)

    assert len(entries) == 40
    header = next(csv.reader((context.evidence_dir / "raw" / "d2" / "timeseries.csv").open(encoding="utf-8")))
    assert header == ["timestamp_utc", "scenario", "http_status", "duration_ms", "decision_status", "execution_type", "correlation_id"]
