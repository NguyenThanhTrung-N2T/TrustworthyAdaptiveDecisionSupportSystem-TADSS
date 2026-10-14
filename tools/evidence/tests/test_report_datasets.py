"""Mục "Dữ liệu thực tế" của báo cáo (RP-01, RP-02, PT-01, PT-02) và CLI dựng lại báo cáo từ một Evidence Pack có sẵn (Q-10)."""
import csv
import json
import re
import shutil
from html import escape
from pathlib import Path

import pytest

from evidence import dataset_analysis, evidence_pack, hotel_adapter, kpi, policy_economics as pe, shadow_pairing as sp
from evidence.report import __main__ as cli
from evidence.report import charts, generator
from evidence.tests.conftest import FIXTURES
from evidence.tests.test_policy_economics import POLICY, row
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


# --- PT-03 / PT-04: kinh tế mô phỏng và đối soát shadow (gói 5.7b, Task 8) ---

RECONCILIATION = {"label": "RECONCILED", "statement": "Đối soát tổng hợp (DB + counter), không phải bằng chứng tuyệt đối về từng tác vụ; bằng chứng theo request là trạng thái ghép cặp theo correlation_id.",
                  "primary": 9, "shadow": 8, "delta": {"recorded": 8, "failed": 1, "dropped": 0}, "residual": 0, "reason": None, "seconds": 3.5}


def economics_rows() -> list[dict[str, str]]:
    return [
        row(status="Canceled", canceled="1", cancel_day="2017-07-09"),
        row(status="Canceled", canceled="1", cancel_day="2017-07-01", default=("APPROVED", "0.3"), mock=("APPROVED", "0.6")),
        row(status="No-Show", canceled="1", default=("NEED_APPROVAL", ""), mock=("APPROVED", "0.5")),
        row(), row(price="250", default=("APPROVED", "0.2")), row(status="Canceled", canceled="1", cancel_day="2017-07-10", pair_state=sp.UNPAIRED),
    ]


def add_economics(run: Path, reconciliation: dict | None = RECONCILIATION) -> dict:
    result = pe.summarize(economics_rows(), POLICY, reconciliation=reconciliation, pairing_counts={state: 1 for state in sp.STATES}, violations={"INVARIANT_MISMATCH": 2}, iterations=20)
    write_json(run / "raw" / "d5" / "policy_economics.json", result)
    return result


def test_economics_section_shows_both_variants_in_one_table_and_the_binding_sentences(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    result = add_economics(run)

    html = generator.render(data_for(run))

    assert "PT-03" in html and "PT-04" in html and "PT-01…PT-04" in html
    tables = re.findall(r"<table.*?</table>", html, re.DOTALL)
    assert any("REFUND_OF_PAID" in table and "REFUND_OF_TOTAL_CAPPED" in table and "S-SC" in table for table in tables), "hai biến thể ngang hàng trong cùng một bảng"
    assert not re.search(r"biến thể chính|variant chính|main variant", html, re.IGNORECASE), "không biến thể nào được gọi là biến thể chính"
    for sentence in (pe.AGGREGATE_NOTE, pe.SC_LIMIT_NOTE, RECONCILIATION["statement"], pe.BIAS_THRESHOLD_NOTE, escape(result["ranking"]["statement"])):
        assert sentence in html, sentence
    assert "Mốc tham chiếu cọc 100% (bằng mức fallback)" in html and "Mốc lý tưởng (dùng kết quả đã biết)" in html
    assert "Rủi ro triển khai nếu giữ nguyên mã hoàn tiền hiện tại (B-1) — không dùng để xếp hạng" in html
    assert "tiền mất" not in html
    assert not RESOURCE_LOADING.search(html)


def test_economics_section_s_sc_is_a_range_between_the_bounds_with_the_data_limit(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    result = add_economics(run)

    section = generator.economics_section(run)

    assert section["available"] is True and section["s_sc_limit"] == pe.SC_LIMIT_NOTE and section["note"] == pe.AGGREGATE_NOTE
    default = next(item for item in section["table"] if item["policy"] == pe.TADSS_DEFAULT)
    assert [cell["variant"] for cell in default["variants"]] == ["REFUND_OF_PAID", "REFUND_OF_TOTAL_CAPPED"]
    paid = default["variants"][0]
    by_na = {(p["na"], p["bound"]): p["retention"] for p in result["point"] if p["scenario"] == "S-SC" and p["variant"] == "REFUND_OF_PAID" and p["policy"] == pe.TADSS_DEFAULT}
    assert by_na[(0.0, "low")] != by_na[(1.0, "low")], "dữ liệu mẫu phải làm hai quy ước NEED_APPROVAL cho kết quả khác nhau"
    # TADSS: khoảng min…max trên hai cận ngày hủy VÀ hai quy ước NEED_APPROVAL, không chỉ đầu lạc quan r = 1
    assert paid["range"] == f"{min(by_na.values()):.3f}…{max(by_na.values()):.3f}"
    assert f"NEED_APPROVAL = 1: {by_na[(1.0, 'low')]:.3f}…{by_na[(1.0, 'high')]:.3f}" in paid["detail"]
    assert f"NEED_APPROVAL = 0: {by_na[(0.0, 'low')]:.3f}…{by_na[(0.0, 'high')]:.3f}" in paid["detail"]
    static = next(item for item in section["table"] if item["policy"] == pe.STATIC_100)
    assert static["variants"][0]["detail"] == "NEED_APPROVAL: không áp dụng", "mốc tham chiếu không có NEED_APPROVAL"
    default_burden = {item["na"]: item["value"] for item in result["burden"] if item["policy"] == pe.TADSS_DEFAULT}
    assert default["burden"] == f"{min(default_burden.values()):.3f}…{max(default_burden.values()):.3f}"
    assert {item["policy"] for item in section["table"]} == {pe.NO_DEPOSIT, pe.STATIC_100, pe.TADSS_DEFAULT, pe.TADSS_SHADOW_MOCKALT, pe.ORACLE}
    assert section["ranking"]["statement"] == result["ranking"]["statement"]
    assert section["reconciliation"]["statement"] == RECONCILIATION["statement"]
    assert section["pairing"] == {state: 1 for state in sp.STATES} and section["bias"]["flag_name"] == "pairing_bias_risk"
    assert section["chart"].startswith("<svg") and "TADSS_DEFAULT · PAID" in section["chart"] and "TADSS_DEFAULT · CAPPED" in section["chart"]


def test_economics_section_without_reconciliation_or_policy_snapshot_states_the_reason(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    write_json(run / "raw" / "d5" / "policy_economics.json", pe.summarize(economics_rows(), None, reconciliation=None, pairing_counts={}, violations={}, iterations=10))

    html = generator.render(data_for(run))

    assert "PT-03" in html and "PT-04" in html and "tiền mất" not in html
    assert "Không có dữ liệu: đối soát shadow" in html
    assert "Không có dữ liệu: chính sách hủy S-SC" in html
    assert generator.economics_section(run)["chart"] == ""


def test_economics_section_without_the_file_states_the_reason(tmp_path: Path) -> None:
    run = _pack(tmp_path)

    section = generator.economics_section(run)
    html = generator.render(data_for(run))

    assert section["available"] is False and "policy_economics.json" in section["reason"]
    assert "PT-03" in html and "PT-04" in html and html.count("Không có dữ liệu: " + section["reason"]) == 2
    assert not RESOURCE_LOADING.search(html)


def test_economics_section_with_an_unreadable_file_states_the_reason(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    (run / "raw" / "d5").mkdir(parents=True)
    (run / "raw" / "d5" / "policy_economics.json").write_text("{không phải json", encoding="utf-8")

    section = generator.economics_section(run)

    assert section["available"] is False and "không đọc được" in section["reason"]


def test_economics_reconciliation_with_only_label_and_reason_shows_dashes_and_main_table_subtitle_is_min_max(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    add_economics(run, reconciliation={"label": "UNKNOWN", "reason": "prometheus_unavailable"})

    section = generator.economics_section(run)
    html = generator.render(data_for(run))

    reconciliation = section["reconciliation"]
    assert reconciliation["label"] == "UNKNOWN" and reconciliation["reason"] == "prometheus_unavailable"
    assert reconciliation["primary"] == reconciliation["shadow"] == reconciliation["statement"] == reconciliation["delta_text"] == reconciliation["residual"] == "-"
    assert "<td>-</td><td>-</td><td>-</td><td>-</td>" in html, "ô PRIMARY/SHADOW/Delta/residual hiện '-' thay vì trống"
    assert "<th>min…max</th>" in html and "<th>low…high</th>" not in html


def test_economics_reconciliation_keeps_a_real_zero_count(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    add_economics(run, reconciliation={"label": "RECONCILED", "primary": 0, "shadow": 0, "statement": "x"})

    reconciliation = generator.economics_section(run)["reconciliation"]

    assert reconciliation["primary"] == 0 and reconciliation["shadow"] == 0


def test_economics_strings_are_html_escaped(tmp_path: Path) -> None:
    payload = "<script>alert(1)</script>"
    run = _pack(tmp_path)
    add_economics(run, reconciliation=RECONCILIATION | {"statement": payload, "label": payload})

    html = generator.render(data_for(run))

    assert payload not in html and "<script" not in html and "&lt;script&gt;" in html


def test_scatter_chart_labels_every_point_and_fixes_the_axes() -> None:
    points = [("TADSS_DEFAULT · PAID", 0.4, 0.6), ("TADSS_DEFAULT · CAPPED", 0.4, 0.6), ("STATIC_100 · PAID", 1.0, 1.0), ("NO_DEPOSIT · PAID", 0.0, 0.0)]

    svg = charts.scatter_chart(points, "Nghĩa vụ ứng trước (proxy)", "Tỷ lệ giữ lại <S-SC>")

    assert svg.startswith("<svg") and svg.endswith("</svg>")
    for label, _, _ in points:
        assert label in svg
    assert svg.count('class="point-label"') == len(points) and "Nghĩa vụ ứng trước (proxy)" in svg
    assert "&lt;S-SC&gt;" in svg and "<S-SC>" not in svg and "http" not in svg.replace("http://www.w3.org/2000/svg", "")
    assert charts.scatter_chart([], "x", "y") == ""


def test_scatter_chart_separates_labels_of_coincident_points() -> None:
    svg = charts.scatter_chart([("A · PAID", 0.5, 0.5), ("B · PAID", 0.5, 0.5), ("C · PAID", 0.5, 0.5)], "x", "y")

    ys = [float(match) for match in re.findall(r'<text x="[\d.]+" y="([\d.]+)"[^>]*class="point-label"', svg)]
    assert len(ys) == 3 and len({round(value) for value in ys}) == 3, "nhãn của các điểm trùng nhau không chồng lên nhau"


def test_economics_tables_show_both_need_approval_conventions(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    result = add_economics(run)
    point = {(p["scenario"], p["variant"], p["bound"], p["na"], p["policy"]): p["retention"] for p in result["point"]}

    section = generator.economics_section(run)
    html = generator.render(data_for(run))

    assert point[("S-FULL", "REFUND_OF_PAID", "low", 0.0, pe.TADSS_DEFAULT)] != point[("S-FULL", "REFUND_OF_PAID", "low", 1.0, pe.TADSS_DEFAULT)]
    row_ = next(item for item in section["sensitivity"] if (item["scenario"], item["variant"], item["bound"]) == ("S-FULL", "REFUND_OF_PAID", "low"))
    assert row_["default"] == f"{point[('S-FULL', 'REFUND_OF_PAID', 'low', 1.0, pe.TADSS_DEFAULT)]:.3f}"
    assert row_["default0"] == f"{point[('S-FULL', 'REFUND_OF_PAID', 'low', 0.0, pe.TADSS_DEFAULT)]:.3f}"
    assert row_["mock0"] == f"{point[('S-FULL', 'REFUND_OF_PAID', 'low', 0.0, pe.TADSS_SHADOW_MOCKALT)]:.3f}" and row_["sign0"] in (">", "<", "=")
    assert "NEED_APPROVAL = 0" in html and "không có CI cho NEED_APPROVAL = 0" in html and "xét cả hai quy ước NEED_APPROVAL trên ước lượng điểm; riêng CI 95 % của hiệu chỉ có cho NEED_APPROVAL = 1" in html and "chỉ xét NEED_APPROVAL = 1" not in html and "quy ước cận dưới 0 % có trong JSON" not in html


def test_economics_ci_cells_hold_numbers(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    add_economics(run)

    section = generator.economics_section(run)

    interval = re.compile(r"^\[-?\d\.\d{3}; -?\d\.\d{3}\]$")
    for policy in (pe.NO_DEPOSIT, pe.STATIC_100, pe.TADSS_DEFAULT, pe.TADSS_SHADOW_MOCKALT, pe.ORACLE):
        row_ = next(item for item in section["table"] if item["policy"] == policy)
        assert interval.match(row_["burden_ci"]), (policy, row_["burden_ci"])
        for cell in row_["variants"]:
            assert interval.match(cell["ci_low"]) and interval.match(cell["ci_high"]), (policy, cell)


def test_economics_section_with_missing_keys_states_the_reason_instead_of_failing(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    write_json(run / "raw" / "d5" / "policy_economics.json", {"schema": pe.SCHEMA, "point": [{"scenario": "S-SC"}]})

    section = generator.economics_section(run)
    html = generator.render(data_for(run))

    assert section["available"] is False and "thiếu khóa hoặc sai cấu trúc" in section["reason"]
    assert "PT-03" in html and "PT-04" in html and "Không có dữ liệu: raw/d5/policy_economics.json đọc được" in html


def test_scatter_chart_draws_the_span_between_the_bounds_and_keeps_the_labels() -> None:
    points = [("A · PAID", 0.4, 0.2), ("A · CAPPED", 0.4, 0.5), ("B · PAID", 0.9, 0.7)]

    svg = charts.scatter_chart(points, "x", "y", spans={"A · PAID": (0.2, 0.6), "A · CAPPED": (0.5, 0.5), "B · PAID": (0.7, 0.8)})
    plain = charts.scatter_chart(points, "x", "y")

    assert svg.count('stroke-width="2"/>') - plain.count('stroke-width="2"/>') == 6, "hai điểm có đoạn: mỗi đoạn một thân và hai gạch đầu; điểm có hai cận bằng nhau không vẽ đoạn"
    assert svg.count('class="point-label"') == 3 and all(label in svg for label, _, _ in points)
    xs = [float(value) for value in re.findall(r'<line x1="([\d.]+)" y1="[\d.]+" x2="\1"', svg)]
    assert len(set(xs)) >= 2, "đoạn đứng nằm đúng cột của điểm (hai biến thể cùng nhóm lệch nhau để không chồng)"


def test_scatter_chart_keeps_edge_points_off_the_axis_labels() -> None:
    svg = charts.scatter_chart([("A · PAID", 0.0, 0.0), ("A · CAPPED", 0.0, 0.0), ("B · PAID", 1.0, 1.0)], "x", "y")

    centers = [float(value) for value in re.findall(r'<(?:circle|polygon)[^>]*?(?:cx="([\d.]+)")', svg)]
    assert centers and min(centers) >= charts.MARGIN_LEFT + 6 and max(centers) <= charts.WIDTH - charts.MARGIN_RIGHT - 6
