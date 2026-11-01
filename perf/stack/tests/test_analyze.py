# Task 2.16 / B.7.7: phân tích một lượt k6 theo từng phút từ Prometheus (tải, độ trễ client/server, lỗi, CPU/RAM từng container)
import json
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import analyze  # noqa: E402


def _run_dir(tmp_path: Path) -> Path:
    run = tmp_path / "20261102-000000-load"
    run.mkdir()
    (run / "metadata.json").write_text(json.dumps({"runId": run.name, "startedAt": "2026-11-01T17:00:00Z"}), encoding="utf-8")
    (run / "summary.json").write_text(json.dumps({"state": {"testRunDurationMs": 185_000}}), encoding="utf-8")
    return run


def test_window_comes_from_metadata_and_summary(tmp_path):
    start, end = analyze.window(_run_dir(tmp_path))

    assert end - start == 185 and start == 1793552400


def test_minutes_table_uses_one_query_set_per_minute_and_marks_cpu_at_limit(tmp_path):
    limits = {"perf-be": 2.0}

    def fake_query(expr: str, at: int) -> list[dict]:
        if "container_cpu_usage_seconds_total" in expr:
            return [{"metric": {"name": "perf-be"}, "value": [at, "1.95"]}, {"metric": {"name": "perf-postgres"}, "value": [at, "0.3"]}]
        if "container_memory" in expr:
            return [{"metric": {"name": "perf-be"}, "value": [at, str(500 * 2**20)]}]
        return [{"metric": {}, "value": [at, "NaN" if "k6_vus" in expr else "12.5"]}]

    rows = analyze.minutes(1000, 1000 + 185, fake_query, limits)

    assert [row["minute"] for row in rows] == [1, 2, 3, 4]   # phút lẻ cuối (5 s) vẫn có một dòng
    assert rows[0]["cpu"]["perf-be"] == 1.95 and rows[0]["cpu_at_limit"] == ["perf-be"]
    assert rows[0]["vus"] is None and rows[0]["rps"] == 12.5
    assert rows[0]["memory_mb"]["perf-be"] == 500


def test_report_writes_resources_json_and_readable_table(tmp_path):
    run = _run_dir(tmp_path)

    def fake_query(expr: str, at: int) -> list[dict]:
        return [{"metric": {"name": "perf-be"}, "value": [at, "1"]}] if "container" in expr else [{"metric": {}, "value": [at, "1"]}]

    text = analyze.report(run, fake_query, {"perf-be": 2.0})

    saved = json.loads((run / "resources.json").read_text(encoding="utf-8"))
    assert saved["runId"] == run.name and len(saved["minutes"]) == 4
    header = text.splitlines()[1]
    assert header.startswith("phút") and "be" in header.split()   # cột CPU mang tên container bỏ tiền tố perf-


def test_minutes_without_any_sample_are_reported_as_infra_gaps(tmp_path):
    # VM Docker đứng (01/11: mọi nguồn mất số liệu vài phút) ⇒ lượt INFRA_LIMITED, không dùng để kết luận (P6-47)
    run = _run_dir(tmp_path)

    def fake_query(expr: str, at: int) -> list[dict]:
        missing = at == 1793552400 + 120
        if "container" in expr:
            return [] if missing else [{"metric": {"name": "perf-be"}, "value": [at, "1"]}]
        return [] if missing else [{"metric": {}, "value": [at, "1"]}]

    text = analyze.report(run, fake_query, {"perf-be": 2.0})

    saved = json.loads((run / "resources.json").read_text(encoding="utf-8"))
    assert saved["gapMinutes"] == [2] and saved["validity"] == "INFRA_LIMITED"
    assert "INFRA_LIMITED" in text
