"""Mục "Dữ liệu thực tế" của báo cáo (RP-01, RP-02, PT-01, PT-02) và CLI dựng lại báo cáo từ một Evidence Pack có sẵn (Q-10)."""
import csv
import json
import shutil
from pathlib import Path

import pytest

from evidence import dataset_analysis, evidence_pack, hotel_adapter, kpi
from evidence.report import __main__ as cli
from evidence.report import charts, generator
from evidence.tests.conftest import FIXTURES
from evidence.tests.test_report_generator import RESOURCE_LOADING, _manifest, _pack

APPSETTINGS = Path(__file__).resolve().parents[3] / "src" / "TADSS.Runtime.Api" / "appsettings.json"
SOURCE_URL = hotel_adapter.SOURCE_URL


def read(name: str) -> list[dict[str, str]]:
    with (FIXTURES / name).open(encoding="utf-8", newline="") as handle:
        return list(csv.DictReader(handle))


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def add_replay(run: Path) -> None:
    analysis = dataset_analysis.rp_summary(read("d4_sample.csv"), dataset_analysis.load_calendar(APPSETTINGS))
    write_json(run / "raw" / "analysis" / "rp_summary.json", analysis)
    write_json(run / "raw" / "replay" / "summary.json", {
        "part": "replay", "status": "passed", "note": "RP: gửi 10/10", "prepared": 10, "sent": 10, "not_sent": 0, "contract_error_requests": 0, "retries": 1, "source_rows": 10,
        "rejected": {"no_invoice": 1}, "status_counts": {"200": 10}, "decision_status_counts": {"APPROVED": 6, "NEED_APPROVAL": 3}, "window_bias_count": 4,
    })


def add_portability(run: Path, code_status: str = "PASS") -> None:
    write_json(run / "raw" / "analysis" / "pt_summary.json", dataset_analysis.pt_summary(read("d5_sample.csv")))
    write_json(run / "raw" / "portability" / "summary.json", {
        "part": "portability", "status": "passed", "note": "PT: gửi 12/12", "prepared": 12, "sent": 12, "not_sent": 0, "contract_error_requests": 0, "retries": 2, "mode": "quick",
        "status_counts": {"200": 12}, "decision_status_counts": {"APPROVED": 9, "NEED_APPROVAL": 3}, "total_rows": 119390, "valid_count": 100000, "selected": 12,
        "rejected": {"zero_nights": 715, "negative_adr": 1, "invalid_arrival_date": 0, "stay_too_long": 0}, "sample_size": 10000,
    })
    write_json(run / "raw" / "portability" / "source.json", {
        "url": SOURCE_URL, "sha256": hotel_adapter.SOURCE_SHA256, "total_rows": 119390, "seed": hotel_adapter.SAMPLE_SEED, "mode": "quick", "sample_size": 10000,
        "license": hotel_adapter.LICENSE_LABEL, "citation": hotel_adapter.CITATION, "rejected": {"zero_nights": 715, "negative_adr": 1, "invalid_arrival_date": 0, "stay_too_long": 0},
    })
    write_json(run / "raw" / "analysis" / "sc7_code_check.json", {"status": code_status, "reason": "1 commit chạm adapter, không commit nào chạm runtime", "commits": ["abc1234"], "adapter": "tools/evidence/hotel_adapter.py"})


def data_for(run: Path) -> dict:
    checks = [{"code": f"EV-{i}", "passed": True, "detail": "d"} for i in range(1, 6)]
    kpis = [kpi.sc7(None, None).to_dict()]
    coverage = {"passed": [], "failed": [], "untested": {}, "unknownCodes": []}
    return generator.build_data(run, _manifest(), checks, kpis, coverage, [])


def test_report_dataset_sections(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    add_replay(run)
    add_portability(run)

    html = generator.render(data_for(run))

    assert "Dữ liệu thực tế" in html
    for heading in ("RP-01", "RP-02", "PT-01", "PT-02"):
        assert heading in html
    assert html.count("<svg") >= 5 + 2, "biểu đồ cột của RP-02 và PT-02 cộng với các biểu đồ sẵn có"
    assert SOURCE_URL in html and hotel_adapter.SOURCE_SHA256 in html, "nguồn dataset là văn bản trong bảng"
    assert not RESOURCE_LOADING.search(html), "báo cáo vẫn tự chứa: không thẻ/thuộc tính/CSS tải tài nguyên"
    for text in ("STATIC_100", "TADSS_V2", "NO_SHOW", "is_canceled", "deposit_type", "abc1234", "zero_nights"):
        assert text in html
    assert "không kết luận nhân quả" in html


def test_report_dataset_sections_without_replay_state_the_reason(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    add_portability(run)
    write_json(run / "raw" / "replay" / "summary.json", {"part": "replay", "status": "skipped", "note": "RP: bỏ qua vì thiếu hoặc rỗng smashcourt_bookings.csv"})

    html = generator.render(data_for(run))

    assert "bỏ qua vì thiếu hoặc rỗng smashcourt_bookings.csv" in html, "mục RP ghi lý do lấy từ summary.json"
    assert "RP-01" in html and "RP-02" in html and "PT-01" in html
    assert "<svg" in html


def test_report_dataset_sections_without_any_dataset_state_the_reason(tmp_path: Path) -> None:
    html = generator.render(data_for(_pack(tmp_path)))

    assert "Dữ liệu thực tế" in html
    assert html.count("Không có dữ liệu: bước e") >= 2, "cả RP và PT đều nêu lý do (bước e chưa chạy)"
    assert not RESOURCE_LOADING.search(html)


def test_report_code_check_not_committed_is_shown(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    add_portability(run)
    write_json(run / "raw" / "analysis" / "sc7_code_check.json", {"status": "INSUFFICIENT_DATA", "reason": "adapter chưa commit: không có commit nào chạm tools/evidence/hotel_adapter.py", "commits": []})

    assert "adapter chưa commit" in generator.render(data_for(run))


def test_report_limitations_include_dataset_caveats(tmp_path: Path) -> None:
    html = generator.render(data_for(_pack(tmp_path)))

    for fragment in ("môi trường phát triển", "k × 7 ngày", "không có demand", "10:00", "ngưỡng EUR"):
        assert fragment in html, fragment


def test_self_contained_check_distinguishes_text_from_resources() -> None:
    assert not RESOURCE_LOADING.search(f"<td>{SOURCE_URL}</td>"), "URL là văn bản trong ô bảng"
    assert RESOURCE_LOADING.search(f'<a href="{SOURCE_URL}">x</a>')
    assert RESOURCE_LOADING.search('<img src="https://x/y.png">')
    assert RESOURCE_LOADING.search("<style>@import url(https://x/y.css);</style>")
    assert RESOURCE_LOADING.search('<script src="//cdn/x.js"></script>')


def test_bar_chart() -> None:
    svg = charts.bar_chart(["Tỷ lệ cọc TB", "Tỷ lệ cần duyệt"], [charts.Series("STATIC_100", [(0, 1.0), (1, 0.0)]), charts.Series("TADSS_V2", [(0, 0.6), (1, None)])], "tỷ lệ", y_max=1.0)

    assert svg.startswith("<svg") and svg.count("<rect") >= 3 and "STATIC_100" in svg and "Tỷ lệ cọc TB" in svg and "http" not in svg
    assert charts.bar_chart(["a"], [charts.Series("x", [(0, None)])], "y") == ""


# --- CLI dựng lại báo cáo ---

def raw_hashes(manifest: dict) -> dict[str, str]:
    return {item["path"]: item["sha256"] for item in manifest["files"] if item["path"] != "report.html"}


def build_pack(tmp_path: Path) -> Path:
    run = _pack(tmp_path)
    shutil.rmtree(run / "raw" / "load")  # dữ liệu tải mẫu của _pack không có decision_id nên không dùng cho EV-2/EV-3
    add_replay(run)
    add_portability(run)
    (run / "config").mkdir()
    (run / "config" / "modes.json").write_text("{}", encoding="utf-8")
    d1_header = ["decision_id", "role", "correlation_id", "strategy_name"]
    for name, count, prefix in (("decision_audit.csv", 2, "r"), ("decision_audit_portability.csv", 2, "p")):
        path = run / "raw" / "d1" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.writer(handle)
            writer.writerow(d1_header)
            writer.writerows([f"{prefix}{i}", "PRIMARY", f"{prefix}c{i}", "Default"] for i in range(count))
    for part, prefix in (("replay", "r"), ("portability", "p")):
        rows = [{"key": f"{part}:x:{i}", "status": 200, "decision_id": f"{prefix}{i}", "correlation_id": f"{prefix}c{i}"} for i in range(2)]
        (run / "raw" / part / "requests.jsonl").write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
    coverage = {"passed": ["UT-01", "RP-01"], "failed": [], "untested": {"E2E-01": "Phase 6"}, "unknownCodes": []}
    base = {**_manifest(), "catalogCoverage": coverage, "kpis": [kpi.sc7(None, None).to_dict()], "evChecks": [], "testCases": ["RP-01", "UT-01"]}
    base["commitNotes"] = {"smashcourtBe": "không tham gia", "smashcourtFe": "không tham gia"}
    manifest = evidence_pack.build_manifest(run, base)
    evidence_pack.write_manifest(run, manifest)
    return run


def test_report_cli_rebuilds(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    run = build_pack(tmp_path)
    (run / "report.html").write_text("báo cáo cũ", encoding="utf-8")  # chưa nằm trong manifest -> cũng là trường hợp pack chưa có báo cáo

    code = cli.main([str(run)])

    html = (run / "report.html").read_text(encoding="utf-8")
    assert code == 0, capsys.readouterr().out
    assert "Dữ liệu thực tế" in html and "RP-02" in html and "báo cáo cũ" not in html and "không dùng làm minh chứng" not in html
    manifest = json.loads((run / "manifest.json").read_text(encoding="utf-8"))
    assert evidence_pack.check_ev1(run, manifest).passed, "EV-1 đạt sau khi tính lại băm manifest"
    entry = next(item for item in manifest["files"] if item["path"] == "report.html")
    assert entry["sha256"] == evidence_pack.sha256_file(run / "report.html")
    assert [check["code"] for check in manifest["evChecks"]] == ["EV-1", "EV-2", "EV-3", "EV-4", "EV-5"] and all(check["passed"] for check in manifest["evChecks"])
    assert "EV-1" in capsys.readouterr().out


def test_report_cli_does_not_touch_raw_files_or_bless_tampering(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    run = build_pack(tmp_path)
    cli.main([str(run)])
    before = json.loads((run / "manifest.json").read_text(encoding="utf-8"))
    tampered = run / "raw" / "analysis" / "pt_summary.json"
    tampered.write_text(tampered.read_text(encoding="utf-8").replace("12", "13", 1), encoding="utf-8")

    code = cli.main([str(run)])

    after = json.loads((run / "manifest.json").read_text(encoding="utf-8"))
    assert code == 1, "EV-1 phải không đạt vì file thô bị đổi"
    assert raw_hashes(before) == raw_hashes(after), "CLI không tính lại băm của file thô (không hợp thức hóa việc sửa)"
    assert "không dùng làm minh chứng" in (run / "report.html").read_text(encoding="utf-8")
    assert not after["evChecks"][0]["passed"]
    capsys.readouterr()


def test_report_cli_requires_a_manifest(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    assert cli.main([str(tmp_path)]) == 2
    assert "manifest.json" in capsys.readouterr().err


def test_report_cli_runs_as_module(tmp_path: Path) -> None:
    import subprocess
    import sys

    tools = Path(__file__).resolve().parents[2]
    run = build_pack(tmp_path)

    completed = subprocess.run([sys.executable, "-m", "evidence.report", str(run)], cwd=tools, capture_output=True, text=True, check=False)

    assert completed.returncode == 0, completed.stdout + completed.stderr
    assert (run / "report.html").exists()


def _store_ev4(run: Path, passed: bool, detail: str) -> None:
    manifest = json.loads((run / "manifest.json").read_text(encoding="utf-8"))
    manifest["evChecks"] = [{"code": "EV-4", "passed": passed, "detail": detail}]
    (run / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")


def test_report_cli_keeps_the_original_uuid_scan_verdict(tmp_path: Path) -> None:
    run = build_pack(tmp_path)
    _store_ev4(run, True, "không có cột cá nhân hay chuỗi bí mật; đã quét 29 UUID gốc")

    assert cli.main([str(run)]) == 0

    ev4 = json.loads((run / "manifest.json").read_text(encoding="utf-8"))["evChecks"][3]
    assert ev4["passed"] and "đã quét 29 UUID gốc" in ev4["detail"] and "quét UUID gốc: giữ kết quả lượt gốc" in ev4["detail"]
    assert "chưa quét UUID gốc" not in ev4["detail"], "không được ghi đè bằng kết quả yếu hơn"
    assert "đã quét 29 UUID gốc" in (run / "report.html").read_text(encoding="utf-8")


def test_report_cli_keeps_a_failed_original_uuid_scan(tmp_path: Path) -> None:
    run = build_pack(tmp_path)
    _store_ev4(run, False, "raw/replay/summary.json: chứa 1 UUID gốc của CSV SmashCourt")

    assert cli.main([str(run)]) == 1

    ev4 = json.loads((run / "manifest.json").read_text(encoding="utf-8"))["evChecks"][3]
    assert not ev4["passed"] and "chứa 1 UUID gốc" in ev4["detail"]


def test_report_cli_still_rechecks_columns_when_original_scan_is_kept(tmp_path: Path) -> None:
    run = build_pack(tmp_path)
    _store_ev4(run, True, "không có cột cá nhân hay chuỗi bí mật; đã quét 29 UUID gốc")
    with (run / "raw" / "analysis" / "extra.csv").open("w", encoding="utf-8", newline="") as handle:
        csv.writer(handle).writerows([["decision_id", "email"], ["x", "y"]])

    assert cli.main([str(run)]) == 1

    ev4 = json.loads((run / "manifest.json").read_text(encoding="utf-8"))["evChecks"][3]
    assert not ev4["passed"] and "email" in ev4["detail"]


def test_report_cli_without_an_original_scan_reports_not_scanned(tmp_path: Path) -> None:
    run = build_pack(tmp_path)

    assert cli.main([str(run)]) == 0

    assert "chưa quét UUID gốc" in json.loads((run / "manifest.json").read_text(encoding="utf-8"))["evChecks"][3]["detail"]


def test_report_missing_analysis_after_sending_names_the_missing_file(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    write_json(run / "raw" / "replay" / "summary.json", {"part": "replay", "status": "passed", "note": "RP: gửi 20/20 request", "prepared": 20, "sent": 20, "contract_error_requests": 0})
    write_json(run / "raw" / "portability" / "summary.json", {"part": "portability", "status": "passed", "note": "PT: gửi 30/30 request", "prepared": 30, "sent": 30, "contract_error_requests": 0})

    html = generator.render(data_for(run))

    assert "raw/analysis/rp_summary.json" in html and "raw/analysis/pt_summary.json" in html
    assert "Không có dữ liệu: RP: gửi 20/20" not in html and "Không có dữ liệu: PT: gửi 30/30" not in html, "ghi chú thành công không được dùng làm lý do thiếu dữ liệu"
    assert "20 request" in html


def test_rp_section_reason_for_a_skipped_part_is_still_the_note(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    write_json(run / "raw" / "replay" / "summary.json", {"part": "replay", "status": "skipped", "note": "RP: bỏ qua vì thiếu CSV"})

    assert generator.rp_section(run)["reason"] == "RP: bỏ qua vì thiếu CSV"


def test_report_channel_table_shows_rejected_column_and_totals_add_up(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    rows = read("d4_sample.csv")
    next(row for row in rows if row["booking_hash"] == "bk05" and row["policy"] == "TADSS_V2").update({"status": "REJECTED", "deposit_rate": "", "deposit_amount": ""})
    write_json(run / "raw" / "analysis" / "rp_summary.json", dataset_analysis.rp_summary(rows, dataset_analysis.load_calendar(APPSETTINGS)))

    section = generator.rp_section(run)
    html = generator.render(data_for(run))

    assert "REJECTED" in section["status_order"] and "<th>REJECTED</th>" in html
    for row in section["channels"]:
        assert sum(row["cells"]) == row["total"], row
    assert next(row for row in section["channels"] if row["channel"] == "ONLINE")["cells"][section["status_order"].index("REJECTED")] == 1


def test_dataset_strings_are_html_escaped(tmp_path: Path) -> None:
    payload = "<script>alert(1)</script>"
    run = _pack(tmp_path)
    add_portability(run)
    write_json(run / "raw" / "replay" / "summary.json", {"part": "replay", "status": "skipped", "note": f"RP: {payload}"})
    source = json.loads((run / "raw" / "portability" / "source.json").read_text(encoding="utf-8"))
    source.update({"license": payload, "url": f"https://x.test/?a=<b>{payload}", "citation": payload})
    write_json(run / "raw" / "portability" / "source.json", source)
    write_json(run / "raw" / "analysis" / "sc7_code_check.json", {"status": "INSUFFICIENT_DATA", "reason": payload, "commits": [payload]})

    html = generator.render(data_for(run))

    assert payload not in html and "<script" not in html
    assert html.count("&lt;script&gt;alert(1)&lt;/script&gt;") >= 4, "ghi chú RP, giấy phép, trích dẫn và kiểm mã đều bị escape"
    assert not RESOURCE_LOADING.search(html)
