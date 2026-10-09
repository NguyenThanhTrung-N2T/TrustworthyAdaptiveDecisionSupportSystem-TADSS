"""EV-1…EV-5 trên pack mẫu: pack hợp lệ đạt; mỗi vi phạm làm đúng một kiểm tra không đạt."""
import csv
import json
from pathlib import Path

import pytest

from evidence import evidence_pack as ep

D1_HEADER = ["decision_id", "role", "correlation_id", "strategy_name"]


def _write_csv(path: Path, header: list[str], rows: list[list[str]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(header)
        writer.writerows(rows)


def _entry(decision: str | None, key: str, corr: str, status: int = 200, repeat: bool = False) -> dict:
    return {"http_status": status, "decision_id": decision, "idempotency_key": key, "correlation_id": corr, "repeat": repeat}


@pytest.fixture
def pack(tmp_path: Path):
    """Pack mẫu: 2 quyết định, một request lặp, một 503; manifest đủ."""
    run = tmp_path / "EV-test"
    d1_rows = [["d1", "PRIMARY", "c1", "Default"], ["d2", "PRIMARY", "c2", "Default"], ["d2s", "SHADOW", "c2", "Mock"]]
    _write_csv(run / "raw" / "d1" / "decision_audit.csv", D1_HEADER, d1_rows)
    (run / "config").mkdir()
    (run / "config" / "modes.json").write_text("{}", encoding="utf-8")
    (run / "raw" / "notes.txt").write_text("ghi chú sạch", encoding="utf-8")
    d2 = [_entry("d1", "k1", "c1"), _entry("d2", "k2", "c2"), _entry("d2", "k2", "c3", repeat=True), _entry(None, "k3", "c4", status=503)]
    base = {"runId": "EV-test", "commits": {"tadss": "abc123", "smashcourtBe": None, "smashcourtFe": None}, "commitNotes": {"smashcourtBe": "không tham gia", "smashcourtFe": "không tham gia"}, "machine": {"cpu": "x"}}
    manifest = ep.build_manifest(run, base)
    ep.write_manifest(run, manifest)
    return run, manifest, ep.read_csv(run / "raw" / "d1" / "decision_audit.csv"), d2


def _codes(checks) -> dict[str, bool]:
    return {check.code: check.passed for check in checks}


def test_valid_pack_passes_all_checks(pack) -> None:
    run, manifest, d1, d2 = pack

    results = ep.run_checks(run, manifest, d1, d2)

    assert _codes(results) == {"EV-1": True, "EV-2": True, "EV-3": True, "EV-4": True, "EV-5": True}, [r.detail for r in results]


def test_manifest_has_hashes_and_row_counts(pack) -> None:
    run, manifest, *_ = pack

    entry = next(item for item in manifest["files"] if item["path"] == "raw/d1/decision_audit.csv")

    assert entry["rows"] == 3 and len(entry["sha256"]) == 64
    assert all(item["path"] != "manifest.json" for item in manifest["files"])


def test_ev1_detects_one_changed_byte_missing_and_extra_file(pack) -> None:
    run, manifest, *_ = pack
    target = run / "raw" / "notes.txt"

    target.write_text("ghi chú sạcH", encoding="utf-8")
    assert not ep.check_ev1(run, manifest).passed
    target.write_text("ghi chú sạch", encoding="utf-8")
    assert ep.check_ev1(run, manifest).passed
    target.unlink()
    assert "thiếu file" in ep.check_ev1(run, manifest).detail
    target.write_text("ghi chú sạch", encoding="utf-8")
    (run / "raw" / "extra.txt").write_text("x", encoding="utf-8")
    assert "không có trong manifest" in ep.check_ev1(run, manifest).detail


def test_ev2_detects_correlation_or_decision_missing_in_d1(pack) -> None:
    run, manifest, d1, d2 = pack

    assert not ep.check_ev2(d1, d2 + [_entry("d9", "k9", "c9")]).passed, "decision_id không có trong D1"
    assert not ep.check_ev2(d1, d2 + [_entry("d1", "k9", "c-lạ")]).passed, "request đầu tiên mà correlation_id không có trong D1"
    assert ep.check_ev2(d1, d2 + [_entry("d1", "k1", "c-replay", repeat=True)]).passed, "request lặp không ghi dòng mới nên correlation_id không bắt buộc"
    assert not ep.check_ev2(d1, [_entry("d1", "k", "c1", status=503)]).passed, "D2 không có response 200 thì không có gì để đối chiếu"


def test_ev3_detects_missing_extra_and_mismatched_counts(pack) -> None:
    run, manifest, d1, d2 = pack

    assert ep.check_ev3(d1, d2).passed
    assert not ep.check_ev3(d1[:1], d2).passed, "thiếu bản PRIMARY của d2"
    assert not ep.check_ev3(d1 + [{"decision_id": "d1", "role": "PRIMARY", "correlation_id": "cx", "strategy_name": "x"}], d2).passed, "d1 có hai bản PRIMARY"
    assert not ep.check_ev3(d1, d2 + [_entry("d2", "k-khác", "c9")]).passed, "hai khóa khác nhau cùng một decisionId"


@pytest.mark.parametrize("column", ["email", "phone", "customer_id", "reservation_id", "Note"])
def test_ev4_detects_personal_data_column(pack, column: str) -> None:
    run, manifest, *_ = pack
    _write_csv(run / "raw" / "extra.csv", ["decision_id", column], [["d1", "x"]])

    assert not ep.check_ev4(run).passed


def test_ev4_allows_strategy_name_and_flags_secret_strings(pack) -> None:
    run, manifest, *_ = pack
    assert ep.check_ev4(run).passed, "strategy_name chứa 'name' nhưng không phải cột cấm"

    (run / "config" / "bad.txt").write_text("ConnectionStrings: Host=x;Password=hunter2;", encoding="utf-8")
    assert not ep.check_ev4(run).passed
    (run / "config" / "bad.txt").write_text("Host=x;Password=tadss_eval;", encoding="utf-8")
    assert ep.check_ev4(run).passed, "mật khẩu cố định của stack đánh giá không phải bí mật"
    (run / "config" / "bad.txt").write_text("đọc appsettings.Development.json", encoding="utf-8")
    assert not ep.check_ev4(run).passed


def test_ev4_skips_source_listing_reports(pack) -> None:
    run, manifest, *_ = pack
    report = run / "raw" / "coverage" / "index.html"
    report.parent.mkdir(parents=True)
    report.write_text("<pre>var cs = \"Host=x;Password=hunter2\";</pre>", encoding="utf-8")

    assert ep.check_ev4(run).passed, "coverage/mutation là liệt kê mã nguồn của repo"


def test_ev5_requires_commit_machine_config(pack) -> None:
    run, manifest, *_ = pack

    assert ep.check_ev5(run, manifest).passed
    assert not ep.check_ev5(run, {**manifest, "commits": {**manifest["commits"], "tadss": ""}}).passed
    assert not ep.check_ev5(run, {k: v for k, v in manifest.items() if k != "machine"}).passed
    assert not ep.check_ev5(run, {**manifest, "commitNotes": {}}).passed, "BE/FE null mà không có lý do"
    without_be = {**manifest, "commits": {"tadss": "abc", "smashcourtFe": None}}
    assert not ep.check_ev5(run, without_be).passed
    (run / "config" / "modes.json").unlink()
    assert not ep.check_ev5(run, manifest).passed, "config/ rỗng"


def test_manifest_round_trips_json(pack) -> None:
    run, manifest, *_ = pack

    assert json.loads((run / "manifest.json").read_text(encoding="utf-8")) == manifest


# --- Nhiều nguồn (D1, D2-like) và quét UUID gốc (gói 5.6) ---

RAW_UUID = "7b0f2a4e-93a1-4c55-8a1d-0f6a2c9e1b77"


def _pt_entries(count: int = 3) -> list[dict]:
    return [_entry(f"p{i}", f"portability:r:{i}", f"pc{i}") for i in range(count)]


def _pt_d1(count: int = 3) -> list[dict[str, str]]:
    return [{"decision_id": f"p{i}", "role": "PRIMARY", "correlation_id": f"pc{i}", "strategy_name": "Default"} for i in range(count)]


def test_ev2_ev3_multiple_sources(pack) -> None:
    run, manifest, d1, d2 = pack
    replay_d1 = {"decision_id": "r1", "role": "PRIMARY", "correlation_id": "rc1", "strategy_name": "Default"}
    main = ep.EvSource("chính (D2 + replay)", d1 + [replay_d1], d2 + [_entry("r1", "replay:r:1", "rc1")])
    portability = ep.EvSource("PT", _pt_d1(), _pt_entries())

    both = ep.run_checks(run, manifest, main.d1_rows, main.entries, extra_sources=[portability], main_name="chính (D2 + replay)")
    assert _codes(both)["EV-2"] and _codes(both)["EV-3"], [check.detail for check in both]
    assert "chính (D2 + replay)" in both[1].detail and "PT" in both[1].detail and "PT" in both[2].detail

    # D1 của PT thiếu một bản PRIMARY: EV-3 không đạt và chỉ rõ nguồn PT, nguồn chính vẫn đạt
    broken = ep.EvSource("PT", _pt_d1()[:2], _pt_entries())
    assert ep.check_ev3_sources([main, broken]).passed is False
    detail = ep.check_ev3_sources([main, broken]).detail
    assert "[PT]" in detail and "KHÔNG ĐẠT" in detail and "[chính (D2 + replay)]" in detail
    assert ep.check_ev2_sources([main, broken]).passed is False and "[PT]" in ep.check_ev2_sources([main, broken]).detail
    assert ep.check_ev3_sources([main, portability]).passed


def test_ev_sources_without_entries_are_not_applicable_but_all_empty_fails(pack) -> None:
    run, manifest, d1, d2 = pack
    main = ep.EvSource("chính", d1, d2)
    empty = ep.EvSource("PT", [], [])

    result = ep.check_ev3_sources([main, empty])

    assert result.passed and "PT" in result.detail and "không áp dụng" in result.detail
    assert not ep.check_ev2_sources([ep.EvSource("chính", d1, []), empty]).passed, "không nguồn nào có response 200 để đối chiếu"
    assert not ep.check_ev2_sources([ep.EvSource("chính", d1, [_entry(None, "k", "c", status=503)])]).passed, "có request nhưng không 200 nào vẫn là không đạt"


def test_ev2_replay_entries_use_the_requests_jsonl_shape() -> None:
    rows = [{"key": "replay:r:1", "status": 200, "decision_id": "d1", "correlation_id": "c1"}, {"key": "replay:r:2", "status": None, "decision_id": None, "correlation_id": None}]

    entries = ep.step_entries(rows)

    assert entries == [
        {"http_status": 200, "decision_id": "d1", "idempotency_key": "replay:r:1", "correlation_id": "c1", "repeat": False},
        {"http_status": None, "decision_id": None, "idempotency_key": "replay:r:2", "correlation_id": None, "repeat": False},
    ]


def test_collect_sources_reads_pack_files(pack) -> None:
    run, manifest, d1, d2 = pack
    load = run / "raw" / "load" / "lt01"
    load.mkdir(parents=True)
    (load / "requests.jsonl").write_text("\n".join(json.dumps(entry) for entry in d2) + "\n", encoding="utf-8")
    replay_dir = run / "raw" / "replay"
    replay_dir.mkdir(parents=True)
    (replay_dir / "requests.jsonl").write_text(json.dumps({"key": "replay:r:1", "status": 200, "decision_id": "r1", "correlation_id": "rc1"}) + "\n", encoding="utf-8")
    _write_csv(run / "raw" / "d1" / "decision_audit.csv", D1_HEADER, [*([row[k] for k in D1_HEADER] for row in d1), ["r1", "PRIMARY", "rc1", "Default"]])
    portability = run / "raw" / "portability"
    portability.mkdir(parents=True)
    (portability / "requests.jsonl").write_text("\n".join(json.dumps({"key": f"portability:r:{i}", "status": 200, "decision_id": f"p{i}", "correlation_id": f"pc{i}"}) for i in range(3)) + "\n", encoding="utf-8")
    _write_csv(run / "raw" / "d1" / "decision_audit_portability.csv", D1_HEADER, [[f"p{i}", "PRIMARY", f"pc{i}", "Default"] for i in range(3)])

    sources = ep.collect_sources(run)

    assert [source.name for source in sources] == [ep.MAIN_SOURCE_NAME, ep.PORTABILITY_SOURCE_NAME]
    assert len(sources[0].entries) == 5 and len(sources[1].entries) == 3 and len(sources[1].d1_rows) == 3
    assert ep.check_ev3_sources(sources).passed


def test_collect_sources_omits_portability_when_it_did_not_run(pack) -> None:
    run, manifest, *_ = pack

    assert [source.name for source in ep.collect_sources(run)] == [ep.MAIN_SOURCE_NAME]


def test_ev4_detects_raw_uuid(pack) -> None:
    run, manifest, *_ = pack
    _write_csv(run / "raw" / "d4" / "replay_smashcourt.csv", ["booking_hash", "decision_id"], [["abc", RAW_UUID.upper()]])

    assert ep.check_ev4(run).passed, "không có danh sách UUID gốc thì không quét được (chỉ kiểm cột và bí mật)"
    result = ep.check_ev4(run, {RAW_UUID})

    assert not result.passed and "raw/d4/replay_smashcourt.csv" in result.detail and RAW_UUID not in result.detail, "không in lại UUID gốc"
    assert ep.check_ev4(run, {"11111111-2222-4333-8444-555555555555"}).passed, "UUID khác (VD decision_id mới sinh) không bị coi là UUID gốc"


def test_ev4_uuid_scan_covers_report_and_json(pack) -> None:
    run, manifest, *_ = pack
    (run / "report.html").write_text(f"<td>{RAW_UUID}</td>", encoding="utf-8")

    result = ep.check_ev4(run, {RAW_UUID})

    assert not result.passed and "report.html" in result.detail


def test_ev4_clean_pack_passes_with_uuid_scan(pack) -> None:
    run, manifest, *_ = pack
    _write_csv(run / "raw" / "d4" / "replay_smashcourt.csv", ["booking_hash", "decision_id"], [["abc", "0f9c7e5a-0000-4000-8000-00000000d001"]])

    assert ep.check_ev4(run, {RAW_UUID}).passed


def test_find_uuids_lowercases_and_matches_the_ev4_byte_pattern() -> None:
    text = "booking 5B0A6F0E-7C0D-4A0E-9A64-0C2E7F1B9A11, branch 1f2e3d4c-0000-4000-8000-000000000001; không phải: 5b0a6f0e7c0d4a0e9a640c2e7f1b9a11"

    found = ep.find_uuids(text)

    assert found == {"5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11", "1f2e3d4c-0000-4000-8000-000000000001"}
    assert {match.decode("ascii").lower() for match in ep.UUID_PATTERN.findall(text.encode("utf-8"))} == found, "mẫu chữ và mẫu byte của EV-4 phải khớp nhau"
