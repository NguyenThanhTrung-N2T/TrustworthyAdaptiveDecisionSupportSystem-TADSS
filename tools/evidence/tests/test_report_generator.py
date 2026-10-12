"""Bộ sinh báo cáo: tự chứa (không URL tải tài nguyên), đủ mục, mục thiếu dữ liệu ghi lý do, thống kê/điểm gãy đúng."""
import csv
import json
import re
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

from evidence import coverage_map, kpi
from evidence.report import charts, generator

T0 = datetime(2026, 10, 9, 1, 0, tzinfo=timezone.utc)
# Tài nguyên tải về (thuộc tính src/href/... trỏ http(s), url(...) trong CSS, @import, thẻ script/link/iframe/img/object); URL dạng văn bản trong ô bảng (nguồn dataset) thì được phép
RESOURCE_LOADING = re.compile(
    r"""(?:\b(?:src|href|action|poster|data|srcset)\s*=\s*["']?\s*(?:https?:)?//)|url\(\s*["']?\s*(?:https?:)?//|@import|<script|<link|<iframe|<img|<object""", re.IGNORECASE
)


def _iso(seconds: float) -> str:
    return (T0 + timedelta(seconds=seconds)).isoformat().replace("+00:00", "Z")


def _write_jsonl(path: Path, entries: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(json.dumps(entry) for entry in entries) + "\n", encoding="utf-8")


def _requests(count: int, duration: float = 20.0, status: int = 200, start: float = 0.0, step: float = 0.1) -> list[dict]:
    return [{"ts": _iso(start + i * step), "http_status": status, "duration_ms": duration, "phase": "main"} for i in range(count)]


def _pack(tmp_path: Path, with_incident: bool = True) -> Path:
    run = tmp_path / "EV-test"
    _write_jsonl(run / "raw" / "load" / "lt01" / "requests.jsonl", _requests(600))
    _write_jsonl(run / "raw" / "load" / "lt02" / "requests.jsonl", _requests(100, 50.0, start=0) + _requests(100, 400.0, start=10) + _requests(50, 900.0, start=20, step=0.2))
    (run / "raw" / "load" / "windows.json").write_text(
        json.dumps([{"name": "lt02", "steps": [{"rate": 10, "start": _iso(0), "end": _iso(10)}, {"rate": 50, "start": _iso(10), "end": _iso(20)}, {"rate": 90, "start": _iso(20), "end": _iso(30)}]}]), encoding="utf-8"
    )
    if with_incident:
        _write_jsonl(run / "raw" / "load" / "lt03" / "requests.jsonl", _requests(300, step=0.2))
        (run / "raw" / "chaos").mkdir(parents=True)
        with (run / "raw" / "chaos" / "chaos_log.csv").open("w", encoding="utf-8", newline="") as handle:
            writer = csv.writer(handle)
            writer.writerow(["fi_id", "run_id", "toxic", "started_at_utc", "ended_at_utc", "expected", "observed", "passed", "invariant_violations"])
            writer.writerow(["FI-02", "EV-test", "Delay", _iso(10), _iso(20), "<b>kỳ vọng</b>", "quan sát", "True", "0"])
        (run / "raw" / "metrics").mkdir(parents=True)
        (run / "raw" / "metrics" / "lt03-p99-ms.json").write_text(
            json.dumps({"result": [{"metric": {}, "values": [[T0.timestamp() + s, str(100 + s)] for s in range(0, 60, 5)] + [[T0.timestamp() + 60, "NaN"]]}]}), encoding="utf-8"
        )
    return run


def _manifest() -> dict:
    return {
        "runId": "EV-test", "mode": "quick", "createdAtUtc": "2026-10-09T01:00:00Z", "environment": "E-EVAL", "dirty": True,
        "commits": {"tadss": "abc", "smashcourtBe": None, "smashcourtFe": None}, "commitNotes": {"smashcourtBe": "không tham gia"},
        "machine": {"cpu": "x", "logicalCores": 8, "images": {"a": "b"}}, "steps": [{"name": "b", "status": "passed", "duration_seconds": 12.5}],
    }


def _data(run: Path, checks_pass: bool = True) -> dict:
    checks = [{"code": f"EV-{i}", "passed": checks_pass or i != 3, "detail": "d"} for i in range(1, 6)]
    kpis = [kpi.Kpi("SC1", kpi.PASS, "v", "lý do", ["raw/x"]).to_dict(), *[item.to_dict() for item in kpi.out_of_scope()]]
    coverage = {"passed": ["UT-01"], "failed": [], "untested": {"E2E-01": "Phase 6"}, "unknownCodes": []}
    groups = [{"group": "UT", "passed": 1, "failed": 0, "untested": 0, "total": 1}]
    return generator.build_data(run, _manifest(), checks, kpis, coverage, groups)


def test_report_self_contained(tmp_path: Path) -> None:
    html = generator.render(_data(_pack(tmp_path)))

    assert not RESOURCE_LOADING.search(html), "không có URL tải tài nguyên (URL dạng văn bản trong bảng thì được phép)"
    assert "<script" not in html and "src=" not in html and "<link" not in html
    for heading in ("Kết luận KPI", "Các bước của evaluator", "Độ phủ danh mục", "Kiểm thử tải", "Sự cố dưới tải", "SCALE-2", "Hợp đồng API", "Mutation testing", "Toàn vẹn Evidence Pack", "Giới hạn"):
        assert heading in html
    assert "<svg" in html and html.count("<svg") >= 5
    assert "&lt;b&gt;kỳ vọng" in html, "nội dung từ dữ liệu được escape"
    assert "cây làm việc có thay đổi chưa commit" in html and "không tham gia" in html


def test_report_sections_without_data_state_a_reason(tmp_path: Path) -> None:
    html = generator.render(_data(_pack(tmp_path, with_incident=False)))

    assert "Không có dữ liệu: bước c" in html or "bước c (LT-03) chưa chạy" in html
    assert "Không có dữ liệu: Stryker không chạy" in html
    assert "Không có dữ liệu: bước d chưa chạy" in html
    assert "Không có dữ liệu coverage" in html


def test_report_marks_unusable_run_when_an_ev_check_fails(tmp_path: Path) -> None:
    run = _pack(tmp_path)

    assert "không dùng làm minh chứng" in generator.render(_data(run, checks_pass=False))
    assert "không dùng làm minh chứng" not in generator.render(_data(run, checks_pass=True))


def test_latency_stats_exact() -> None:
    entries = [{"ts": _iso(i), "http_status": 200 if i < 95 else 500, "duration_ms": float(i + 1)} for i in range(100)]

    stats = generator.latency_stats(entries)

    assert stats["samples"] == 100 and stats["p50"] == 50.0 and stats["p95"] == 95.0 and stats["p99"] == 99.0 and stats["max"] == 100.0
    assert stats["error_rate"] == pytest.approx(0.05)
    assert generator.latency_stats([])["samples"] == 0


def test_lt02_breakpoint_requires_all_previous_steps_good(tmp_path: Path) -> None:
    run = _pack(tmp_path)
    entries = generator.main_phase(generator.read_jsonl(run / "raw" / "load" / "lt02" / "requests.jsonl"))
    steps = json.loads((run / "raw" / "load" / "windows.json").read_text(encoding="utf-8"))[0]["steps"]

    result = generator.lt02_breakpoint(entries, steps)

    assert [step["good"] for step in result["steps"]] == [True, False, False]
    assert result["last_good_rate"] == 10 and result["broke"]


def test_warmup_requests_are_excluded() -> None:
    entries = [{"ts": _iso(0), "http_status": 200, "duration_ms": 9999.0, "phase": "warmup"}, *_requests(5)]

    assert len(generator.main_phase(entries)) == 5


def test_chart_handles_gaps_and_empty() -> None:
    assert charts.line_chart([charts.Series("x", [])], "x", "y") == ""
    svg = charts.line_chart([charts.Series("a", [(0, 1.0), (1, None), (2, 3.0), (3, 4.0)])], "giây", "ms", [charts.Band("FI-02", 1, 2)])

    assert svg.count("<polyline") == 1 and svg.count("<circle") == 1 and "FI-02" in svg and "http" not in svg


def test_catalog_coverage_and_groups() -> None:
    codes = ["UT-01", "UT-02", "LT-04", "FI-02", "RP-01"]
    summary = {"UT-01": {"passed": 3, "failed": 0, "skipped": 0}, "UT-02": {"passed": 1, "failed": 1, "skipped": 0}, "ZZ-9": {"passed": 1, "failed": 0, "skipped": 0}}

    result = coverage_map.coverage(codes, summary, {"FI-02"})

    assert result["passed"] == ["UT-01", "FI-02"] and result["failed"] == ["UT-02"]
    assert set(result["untested"]) == {"LT-04", "RP-01"} and "Phase 6" in result["untested"]["LT-04"] and "5.6" in result["untested"]["RP-01"]
    assert result["unknownCodes"] == ["ZZ-9"]
    assert {group["group"]: group["total"] for group in coverage_map.by_group(codes, result)} == {"UT": 2, "LT": 1, "FI": 1, "RP": 1}


def test_catalog_codes_read_from_methodology() -> None:
    methodology = Path(__file__).resolve().parents[3] / "docs" / "research" / "EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md"

    codes = coverage_map.catalog_codes(methodology)

    for expected in ("UT-01", "IT-05", "CT-03", "MT-02", "FI-08", "LT-03", "SCALE-2", "OB-3", "BE-UT-01", "BE-IT-20", "FE-10", "E2E-13"):
        assert expected in codes
    assert len(codes) == len(set(codes))


def test_catalog_codes_read_two_part_prefix(tmp_path: Path) -> None:
    methodology = tmp_path / "m.md"
    methodology.write_text("| ID | X |\n| :--- | :--- |\n| BE-UT-01 | a |\n| BE-IT-20 | b |\n| FE-03 | c |\n| E2E-13 | d |\n| UT-14 | e |\n| SCALE-2 | f |\n| BE-1 | g |\n", encoding="utf-8")

    assert coverage_map.catalog_codes(methodology) == ["BE-UT-01", "BE-IT-20", "FE-03", "E2E-13", "UT-14", "SCALE-2", "BE-1"]


def test_smashcourt_codes_are_out_of_scope_of_tadss_run() -> None:
    codes = ["BE-UT-01", "BE-IT-05", "FE-01", "FE-10", "E2E-13", "CT-04", "CT-03"]

    untested = coverage_map.coverage(codes, {}, set())["untested"]

    for code in codes[:-1]:
        assert "SmashCourt-BE/FE" in untested[code] and "chưa có test trong lượt chạy này" not in untested[code]
    assert "SmashCourt" not in untested["CT-03"]


def test_catalog_coverage_failed_by_steps() -> None:
    result = coverage_map.coverage(["RP-01", "RP-02", "PT-01"], {}, {"RP-02"}, failed_by_steps={"RP-01"})

    assert result["passed"] == ["RP-02"] and result["failed"] == ["RP-01"] and set(result["untested"]) == {"PT-01"}


def test_doc_codes_are_not_reported_out_of_scope() -> None:
    result = coverage_map.coverage(["DOC-1", "DOC-4"], {"DOC-4": {"passed": 1, "failed": 0, "skipped": 0}}, set())

    assert result["passed"] == ["DOC-4"] and "gói 5.7" not in result["untested"]["DOC-1"]
    assert coverage_map.coverage(["DOC-1"], {}, {"DOC-1"})["passed"] == ["DOC-1"]
