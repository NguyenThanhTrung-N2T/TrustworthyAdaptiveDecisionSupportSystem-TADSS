"""Bước (f) với dữ liệu thực tế của bước (e): D4/D5, phân tích, D1 của database PT, EV-2/EV-3 nhiều nguồn, EV-4 quét UUID gốc, SC7, độ phủ RP/PT và `datasets` của manifest (gói 5.6)."""
import csv
import json
import re

from pathlib import Path

import pytest

from evaluator import code_check, machine
from evaluator.results import ERROR, FAILED, PASSED
from evaluator.steps import dataset_step
from evaluator.steps import evidence_step as step
from evidence import kpi
from test_evidence_step import NOW, REPO, _db_row, _query_ok, _setup

pytestmark = pytest.mark.usefixtures("fixed_git_info")

EVIDENCE_FIXTURES = REPO / "tools" / "evidence" / "tests" / "fixtures"
PT_DSN = "host=pt"
FIRST_BOOKING_UUID = "10000000-0000-4000-8000-000000000001"  # booking_id đầu tiên của smashcourt_expected_export.csv


def _status_row(decision_id: str, corr: str, strategy_status: str) -> tuple:
    row = list(_db_row(decision_id, corr))
    row[8] = strategy_status
    return tuple(row)


def _outcome_row(key: str, index: int, prefix: str, status: str = "APPROVED", rate: str = "0.5", amount: str = "50") -> dict:
    return {
        "key": key, "status": 200, "attempts": 1, "duration_ms": 5.0, "decision_id": f"{prefix}d-{index}", "correlation_id": f"{prefix}c-{index}",
        "outcome": {"status": status, "reason_code": "TEST", "deposit_rate": rate if status == "APPROVED" else "", "deposit_amount": amount if status == "APPROVED" else ""}, "contract_errors": [],
    }


def _write_jsonl(path: Path, rows: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("".join(dataset_step._dump(row) + "\n" for row in rows), encoding="utf-8")


def _add_replay(context, status: str = "passed", errors: int = 0) -> int:
    from evidence import replay
    from evidence.anonymize import Anonymizer

    bookings = replay.load_bookings(EVIDENCE_FIXTURES / "smashcourt_expected_export.csv")
    requests = replay.build_requests(bookings, Anonymizer(b"khoa-test"), context.run_id, NOW)
    directory = context.evidence_dir / "raw" / "replay"
    _write_jsonl(directory / "prepared.jsonl", [{"idempotency_key": entry["idempotency_key"], "meta": entry["meta"]} for entry in requests])
    _write_jsonl(directory / "requests.jsonl", [_outcome_row(entry["idempotency_key"], index, "r", "NEED_APPROVAL" if index % 4 == 0 else "APPROVED") for index, entry in enumerate(requests)])
    (directory / "summary.json").write_text(json.dumps({
        "part": "replay", "status": status, "note": f"RP: gửi {len(requests)}", "prepared": len(requests), "sent": len(requests), "not_sent": 0, "contract_error_requests": errors, "retries": 0,
        "source_rows": len(bookings), "rejected": {}, "status_counts": {"200": len(requests)}, "decision_status_counts": {}, "window_bias_count": 1,
    }), encoding="utf-8")
    return len(requests)


def _add_portability(context, count: int = 6, status: str = "passed", errors: int = 0) -> int:
    from evidence import hotel_adapter
    from evidence.anonymize import Anonymizer

    rows = hotel_adapter.select(hotel_adapter.load_rows(EVIDENCE_FIXTURES / "hotel_sample.csv"), "full").rows[:count]
    requests = hotel_adapter.build_requests(rows, Anonymizer(b"khoa-test"), context.run_id, NOW)
    directory = context.evidence_dir / "raw" / "portability"
    _write_jsonl(directory / "prepared.jsonl", [{"idempotency_key": entry["idempotency_key"], "meta": entry["meta"]} for entry in requests])
    _write_jsonl(directory / "requests.jsonl", [_outcome_row(entry["idempotency_key"], index, "p", rate="0.3", amount="30") for index, entry in enumerate(requests)])
    (directory / "summary.json").write_text(json.dumps({
        "part": "portability", "status": status, "note": f"PT: gửi {len(requests)}", "prepared": len(requests), "sent": len(requests), "not_sent": 0, "contract_error_requests": errors,
        "retries": 2, "mode": "quick", "total_rows": 21, "valid_count": len(requests), "selected": len(requests), "rejected": {"zero_nights": 3}, "sample_size": 10000,
        "status_counts": {"200": len(requests)},
    }), encoding="utf-8")
    (directory / "source.json").write_text(json.dumps({
        "url": "https://example.test/hotels.csv", "sha256": "ab" * 32, "total_rows": 21, "seed": 20260907, "license": "CC BY 4.0", "citation": "Antonio et al.",
    }), encoding="utf-8")
    return len(requests)


def _fetch_factory(main_rows: list[tuple], pt_rows: list[tuple]):
    def factory(dsn: str):
        rows = pt_rows if dsn == PT_DSN else main_rows
        return lambda sql, params: rows

    return factory


def _setup_datasets(tmp_path: Path, monkeypatch: pytest.MonkeyPatch, replay_status: str = "passed"):
    monkeypatch.setattr(machine, "collect", lambda images, run=None: {"cpu_count": 8, "images": {}, "docker": {}})
    context, base_factory = _setup(tmp_path)
    context.env["EVAL_PT_PG_DSN"] = PT_DSN
    context.steps = ("e", "f")
    private = tmp_path / "private"
    private.mkdir()
    (private / "smashcourt_bookings.csv").write_bytes((EVIDENCE_FIXTURES / "smashcourt_expected_export.csv").read_bytes())
    context.datasets_private = private
    rp_count = _add_replay(context, replay_status)
    pt_count = _add_portability(context)
    main_rows = list(base_factory("x")("", ())) + [_status_row(f"rd-{index}", f"rc-{index}", "NEED_APPROVAL" if index % 4 == 0 else "APPROVED") for index in range(rp_count)]
    pt_rows = [_status_row(f"pd-{index}", f"pc-{index}", "APPROVED") for index in range(pt_count)]
    return context, _fetch_factory(main_rows, pt_rows), rp_count, pt_count


def _check(status: str, reason: str = "giả lập kiểm mã"):
    return lambda repo: code_check.CodeCheck(status, reason, ["abc1234"] if status != "INSUFFICIENT_DATA" else [])


def _manifest(context) -> dict:
    return json.loads((context.evidence_dir / "manifest.json").read_text(encoding="utf-8"))


def test_evidence_step_writes_dataset_files_and_valid_pack(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    context, fetch_factory, rp_count, pt_count = _setup_datasets(tmp_path, monkeypatch)
    monkeypatch.setattr(kpi, "SC7_MIN_RECORDS", pt_count)
    run_dir = context.evidence_dir

    result = step.run(context, fetch_factory, _query_ok, _check("PASS"))

    assert result.status == PASSED, result.notes
    for expected in ("raw/d4/replay_smashcourt.csv", "raw/d5/portability_hotel.csv", "raw/d1/decision_audit_portability.csv", "raw/analysis/rp_summary.json", "raw/analysis/pt_summary.json",
                     "raw/analysis/sc7_code_check.json"):
        assert (run_dir / expected).exists(), expected
    d4 = list(csv.DictReader((run_dir / "raw" / "d4" / "replay_smashcourt.csv").open(encoding="utf-8")))
    d5 = list(csv.DictReader((run_dir / "raw" / "d5" / "portability_hotel.csv").open(encoding="utf-8")))
    assert len(d4) == 2 * rp_count and len(d5) == pt_count
    assert {"decision_id", "channel", "lead_time_minutes", "shift_days", "window_bias", "strategy_status", "status_window_adjusted", "contract_errors"} <= set(d4[0])
    assert {"decision_id", "deposit_type", "market_segment", "shift_days", "attempts", "contract_errors"} <= set(d5[0])
    assert {row["strategy_status"] for row in d4 if row["policy"] == "TADSS_V2"} == {"APPROVED", "NEED_APPROVAL"}, "strategy_status ghép từ D1 theo decision_id"
    manifest = _manifest(context)
    assert [check["passed"] for check in manifest["evChecks"]] == [True] * 5, manifest["evChecks"]
    assert "[PT]" in manifest["evChecks"][1]["detail"] and "[chính (D2 + replay)]" in manifest["evChecks"][1]["detail"]
    assert "đã quét" in manifest["evChecks"][3]["detail"], "EV-4 quét UUID gốc của CSV SmashCourt"
    verdicts = {item["code"]: item["verdict"] for item in manifest["kpis"]}
    assert verdicts["SC7"] == "PASS" and verdicts["SC5"] == "NOT_APPLICABLE" and verdicts["SC6"] == "NOT_APPLICABLE"
    datasets = manifest["datasets"]
    assert datasets["replay"]["sent"] == rp_count and datasets["portability"]["source"]["url"] == "https://example.test/hotels.csv"
    assert datasets["portability"]["codeCheck"]["status"] == "PASS" and datasets["portability"]["source"]["seed"] == 20260907 and datasets["portability"]["mode"] == "quick"
    expected_codes = {"RP-01", "RP-02", "PT-01", "PT-02"}
    assert expected_codes <= set(manifest["testCases"]) and expected_codes <= set(manifest["catalogCoverage"]["passed"])
    html = (run_dir / "report.html").read_text(encoding="utf-8")
    assert "Dữ liệu thực tế" in html and "https://example.test/hotels.csv" in html and "abc1234" in html
    assert not re.search(r"(?:src|href)\s*=\s*[\"']?https?://|<script|<link", html)
    assert any(note.startswith("SC7: PASS") for note in result.notes)


def test_evidence_step_sc7_is_insufficient_when_adapter_not_committed(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    context, fetch_factory, *_ = _setup_datasets(tmp_path, monkeypatch)
    monkeypatch.setattr(kpi, "SC7_MIN_RECORDS", 1)

    result = step.run(context, fetch_factory, _query_ok, _check("INSUFFICIENT_DATA", "adapter chưa commit: không có commit nào chạm tools/evidence/hotel_adapter.py"))

    sc7 = next(item for item in _manifest(context)["kpis"] if item["code"] == "SC7")
    assert sc7["verdict"] == "INSUFFICIENT_DATA" and "adapter chưa commit" in sc7["reason"]
    assert result.status == PASSED, result.notes
    assert "adapter chưa commit" in (context.evidence_dir / "report.html").read_text(encoding="utf-8")


def test_evidence_step_sc7_fails_on_code_check_failure(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    context, fetch_factory, *_ = _setup_datasets(tmp_path, monkeypatch)
    monkeypatch.setattr(kpi, "SC7_MIN_RECORDS", 1)

    result = step.run(context, fetch_factory, _query_ok, _check("FAIL"))

    assert result.status == FAILED and any(note.startswith("SC7: FAIL") for note in result.notes)


def test_evidence_step_replay_contract_errors_move_rp01_to_failed(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    context, fetch_factory, *_ = _setup_datasets(tmp_path, monkeypatch, replay_status="failed")
    summary = context.evidence_dir / "raw" / "replay" / "summary.json"
    summary.write_text(json.dumps({"part": "replay", "status": "failed", "note": "RP: 2 lỗi", "prepared": 3, "sent": 3, "contract_error_requests": 2}), encoding="utf-8")

    step.run(context, fetch_factory, _query_ok, _check("PASS"))

    coverage = _manifest(context)["catalogCoverage"]
    assert "RP-01" in coverage["failed"] and "RP-01" not in coverage["passed"]


def test_evidence_step_ev3_names_the_portability_source_when_one_record_is_missing(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    context, _, rp_count, pt_count = _setup_datasets(tmp_path, monkeypatch)
    _, base_factory = _setup(tmp_path / "other")
    main_rows = list(base_factory("x")("", ())) + [_status_row(f"rd-{index}", f"rc-{index}", "APPROVED") for index in range(rp_count)]
    pt_rows = [_status_row(f"pd-{index}", f"pc-{index}", "APPROVED") for index in range(pt_count - 1)]

    result = step.run(context, _fetch_factory(main_rows, pt_rows), _query_ok, _check("PASS"))

    assert result.status == FAILED
    note = next(note for note in result.notes if note.startswith("EV-1…EV-5"))
    assert "EV-2 KHÔNG ĐẠT" in note and "EV-3 KHÔNG ĐẠT" in note
    assert "[PT] KHÔNG ĐẠT" in _manifest(context)["evChecks"][2]["detail"]


def test_evidence_step_ev4_fails_when_an_original_uuid_leaks(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    context, fetch_factory, *_ = _setup_datasets(tmp_path, monkeypatch)
    summary = context.evidence_dir / "raw" / "replay" / "summary.json"
    document = json.loads(summary.read_text(encoding="utf-8"))
    document["note"] = f"RP: lỡ ghi {FIRST_BOOKING_UUID}"
    summary.write_text(json.dumps(document), encoding="utf-8")

    result = step.run(context, fetch_factory, _query_ok, _check("PASS"))

    assert result.status == FAILED and any("EV-4 KHÔNG ĐẠT" in note for note in result.notes)
    ev4 = _manifest(context)["evChecks"][3]
    assert not ev4["passed"] and "raw/replay/summary.json" in ev4["detail"] and FIRST_BOOKING_UUID not in ev4["detail"], "báo cáo EV-4 nêu file lọt nhưng không in lại UUID gốc"


def test_evidence_step_without_dataset_step_degrades_gracefully(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(machine, "collect", lambda images, run=None: {"cpu_count": 8, "images": {}})
    context, fetch_factory = _setup(tmp_path)

    result = step.run(context, fetch_factory, _query_ok)

    run_dir = context.evidence_dir
    assert result.status == PASSED, result.notes
    assert not (run_dir / "raw" / "d4").exists() and not (run_dir / "raw" / "d5").exists() and not (run_dir / "raw" / "d1" / "decision_audit_portability.csv").exists()
    sc7 = next(item for item in _manifest(context)["kpis"] if item["code"] == "SC7")
    assert sc7["verdict"] == "INSUFFICIENT_DATA"
    html = (run_dir / "report.html").read_text(encoding="utf-8")
    assert "Dữ liệu thực tế" in html and html.count("Không có dữ liệu: bước e") >= 2


def test_evidence_step_requires_pt_dsn_when_portability_ran(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    context, fetch_factory, *_ = _setup_datasets(tmp_path, monkeypatch)
    del context.env["EVAL_PT_PG_DSN"]

    result = step.run(context, fetch_factory, _query_ok, _check("PASS"))

    assert result.status == ERROR and any("EVAL_PT_PG_DSN" in note for note in result.notes)


def test_read_original_uuids_only_from_the_private_csv(tmp_path: Path) -> None:
    private = tmp_path / "private"
    private.mkdir()
    (private / "smashcourt_bookings.csv").write_bytes((EVIDENCE_FIXTURES / "smashcourt_expected_export.csv").read_bytes())

    found = step.read_original_uuids(private)

    assert FIRST_BOOKING_UUID in found and "c1000000-0000-4000-8000-000000000001" in found and all(value == value.lower() for value in found)
    assert step.read_original_uuids(tmp_path / "missing") == set()
