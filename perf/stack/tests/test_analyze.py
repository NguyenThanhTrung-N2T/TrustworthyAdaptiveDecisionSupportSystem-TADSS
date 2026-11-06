# Task 2.16 / B.7.7: phân tích một lượt k6 theo từng phút từ Prometheus (tải, độ trễ client/server, lỗi, CPU/RAM từng container)
import json
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import analyze  # noqa: E402


def _run_dir(tmp_path: Path) -> Path:
    run = tmp_path / "20261102-000000-load"
    run.mkdir(parents=True)
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


# ── task 2.18: giới hạn máy đo, verdict.json từ mẫu thô ─────────────────────────────────────────────────────────────────────────

def test_cpu_limits_resolve_compose_variables_to_their_default(monkeypatch):
    # 05/11 compose đổi cpus của BE thành ${PERF_BE_CPUS:-2}; trước khi sửa float() trên chuỗi biến làm analyze.py hỏng sau mỗi lượt
    monkeypatch.delenv("PERF_BE_CPUS", raising=False)
    limits = analyze.limits_from_compose()
    assert limits["perf-be"] == 2.0 and all(isinstance(v, float) for v in limits.values())


def test_cpu_limits_follow_the_environment_when_set(monkeypatch):
    monkeypatch.setenv("PERF_BE_CPUS", "3")
    assert analyze.limits_from_compose()["perf-be"] == 3.0


def test_cpu_limit_of_be_comes_from_the_run_metadata(tmp_path, monkeypatch):
    # perf.sh ghi beCpus từ container đang chạy; analyze chạy ở shell không có PERF_BE_CPUS nên phải tin metadata của lượt
    monkeypatch.delenv("PERF_BE_CPUS", raising=False)
    (tmp_path / "metadata.json").write_text(json.dumps({"beCpus": 4}), encoding="utf-8")
    assert analyze.limits_from_compose(run_dir=tmp_path)["perf-be"] == 4.0
    (tmp_path / "metadata.json").write_text(json.dumps({"startedAt": "x"}), encoding="utf-8")
    assert analyze.limits_from_compose(run_dir=tmp_path)["perf-be"] == 2.0


def test_memory_limits_are_read_from_compose_in_megabytes():
    memory = analyze.memory_limits_from_compose()
    assert memory["perf-be"] == 1024 and memory["perf-k6"] == 2048 and memory["perf-redis"] == 128


def test_k6_near_its_cpu_or_memory_ceiling_makes_the_run_infra_limited(tmp_path):
    run = _run_dir(tmp_path)

    def fake_query(expr: str, at: int) -> list[dict]:
        if "container_cpu_usage_seconds_total" in expr:
            return [{"metric": {"name": "perf-be"}, "value": [at, "1"]}, {"metric": {"name": "perf-k6"}, "value": [at, "1.3"]}]
        if "container_memory" in expr:
            return [{"metric": {"name": "perf-k6"}, "value": [at, str(500 * 2**20)]}]
        return [{"metric": {}, "value": [at, "1"]}]

    text = analyze.report(run, fake_query, {"perf-be": 2.0, "perf-k6": 1.5}, {"perf-k6": 2048})

    saved = json.loads((run / "resources.json").read_text(encoding="utf-8"))
    assert saved["validity"] == "INFRA_LIMITED" and any("perf-k6" in p for p in saved["infra"])
    assert "INFRA_LIMITED" in text


def _raw_csv(run: Path, rows: list[tuple]) -> None:
    import csv
    import gzip
    with gzip.open(run / "raw.csv.gz", "wt", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(["metric_name", "timestamp", "metric_value", "name", "extra_tags"])
        for metric, t, value, name, tags in rows:
            writer.writerow([metric, t, value, name, tags])


def _load_run(tmp_path: Path, k6_exit: int = 0, seconds: int = 120, profile: str = "load") -> Path:
    run = _run_dir(tmp_path)
    (run / "schedule.json").write_text(json.dumps({"profile": profile, "selftest": False, "steadySeconds": 120, "steps": []}), encoding="utf-8")
    (run / "k6-exit").write_text(str(k6_exit), encoding="utf-8")
    rows = []
    for second in range(seconds):
        for _ in range(10):
            rows.append(("http_req_duration", 1000 + second, 50, "GET x", "phase=steady&slo=read"))
            rows.append(("request_errors", 1000 + second, 0, "GET x", "phase=steady&slo=read"))
    _raw_csv(run, rows)
    return run


def test_evaluate_writes_verdict_json_with_status_per_group(tmp_path):
    run = _load_run(tmp_path)

    result = analyze.evaluate(run, lambda expr, at: [])

    saved = json.loads((run / "verdict.json").read_text(encoding="utf-8"))
    assert saved["validity"] == "OK" and saved["groups"]["read"]["n"] == 1200
    assert saved["groups"]["read"]["p99Status"] == "PASS" and result["status"] == "PASS"
    assert saved["episodes"]["count"] == 0 and saved["episodes"]["windowsChecked"] == 12   # 120 s steady, cửa sổ 10 s


def test_interrupted_or_short_run_is_incomplete_never_pass(tmp_path):
    interrupted = analyze.evaluate(_load_run(tmp_path / "a", k6_exit=105), lambda expr, at: [])
    short = analyze.evaluate(_load_run(tmp_path / "b", seconds=60), lambda expr, at: [])
    assert interrupted["validity"] == "INCOMPLETE" and "k6 thoát 105" in interrupted["problems"][0]
    assert short["validity"] == "INCOMPLETE"


def test_breakpoint_records_resources_at_the_break_step_and_the_one_before(tmp_path):
    run = _run_dir(tmp_path)
    steps = [{"rate": 10, "transition": 10, "hold": 180}, {"rate": 15, "transition": 10, "hold": 180}]
    (run / "schedule.json").write_text(json.dumps({"profile": "breakpoint", "selftest": False, "steadySeconds": 380, "steps": steps}),
                                       encoding="utf-8")
    (run / "k6-exit").write_text("0", encoding="utf-8")
    rows = []
    for second in range(381):
        value = 900 if second >= 240 else 50
        for _ in range(3):
            rows.append(("http_req_duration", 1000 + second, value, "GET x", "phase=steady&slo=read"))
            rows.append(("request_errors", 1000 + second, 0, "GET x", "phase=steady&slo=read"))
    _raw_csv(run, rows)
    asked: list[tuple[str, int]] = []

    def fake_query(expr: str, at: int) -> list[dict]:
        asked.append((expr, at))
        return [{"metric": {"name": "perf-be"}, "value": [at, "1.9"]}]

    result = analyze.evaluate(run, fake_query)

    assert result["breakRate"] == 15 and result["status"] == "PASS"
    assert set(result["resourcesAtBreak"]) == {"break", "before"}
    assert result["resourcesAtBreak"]["break"]["cpu"][0]["value"] == "1.9"
    assert any("db_client_connections_usage" in expr for expr, _ in asked)


def test_pool_refuses_runs_with_different_configuration(tmp_path):
    first, second = _load_run(tmp_path / "a"), _load_run(tmp_path / "b")
    for run, rate in ((first, "RATE=20"), (second, "RATE=25")):
        (run / "metadata.json").write_text(json.dumps({"runId": run.name, "profile": "load", "args": f"-e PROFILE=load -e {rate}",
                                                       "seed": {"dbChecksum": "x"}, "be": {"image": "i"}}), encoding="utf-8")
    try:
        analyze.pool([first, second])
    except ValueError as error:
        assert "khác cấu hình" in str(error)
    else:
        raise AssertionError("phải từ chối gộp")
