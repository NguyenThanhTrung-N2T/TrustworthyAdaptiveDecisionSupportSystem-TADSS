"""Bước (b) với k6 và Prometheus giả: lệnh, cửa sổ, kiểm response và kiểm số liệu."""
import json
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

from evaluator.clients import k6, prometheus
from evaluator.run_context import build_context
from evaluator.steps import load_step

NOW = datetime(2026, 10, 8, 1, 0, tzinfo=timezone.utc)
MODES = {
    "quick": {
        "lt01": {"executor": "constant-arrival-rate", "rate": 50, "warmup": "30s", "duration": "2m", "runs": 1},
        "lt02": {"executor": "ramping-arrival-rate", "startRate": 10, "warmup": "30s", "steps": [10, 50], "hold": "30s"},
    },
    "full": {"lt01": {"executor": "constant-arrival-rate", "rate": 50, "warmup": "1m", "duration": "10m", "runs": 3}},
}


def _context(tmp_path: Path, mode: str = "quick"):
    repo = tmp_path / "repo"
    (repo / "tests" / "load" / "config").mkdir(parents=True)
    (repo / "tests" / "load" / "config" / "modes.json").write_text(json.dumps(MODES), encoding="utf-8")
    env = {"EVAL_REPO": str(repo), "EVAL_EVIDENCE": str(tmp_path / "evidence"), "EVAL_MODE": mode, "EVAL_STEPS": "b", "EVAL_API_KEY": "k", "PATH": "x"}
    return build_context(env, NOW)


def _entry(status: int, phase: str = "main") -> dict:
    return {"ts": "t", "scenario": "x", "phase": phase, "http_status": status, "duration_ms": 5.0}


class FakeK6:
    """Ghi lại yêu cầu và tạo requests.jsonl theo kịch bản mã trả về; `statuses` quyết định các response của mỗi lần chạy."""

    def __init__(self, statuses=None, returncode: int = 0) -> None:
        self.statuses = statuses or [200] * 10
        self.returncode = returncode
        self.requests: list[k6.K6Request] = []
        self.clock = NOW

    def __call__(self, request: k6.K6Request, base_env: dict[str, str]) -> k6.K6Result:
        self.requests.append(request)
        path = request.out_dir / "requests.jsonl"
        path.write_text("\n".join(json.dumps(_entry(status)) for status in self.statuses) + "\n", encoding="utf-8")
        start, self.clock = self.clock, self.clock + timedelta(minutes=3)
        return k6.K6Result(self.returncode, start, self.clock, path, request.out_dir / "summary.json", len(self.statuses))


def _prometheus(increase: float = 100.0):
    return lambda base_url, metric, start, end: increase


def test_quick_mode_runs_lt01_then_lt02_and_records_windows(tmp_path: Path) -> None:
    fake = FakeK6()
    context = _context(tmp_path)

    result = load_step.run(context, fake, _prometheus(), lambda seconds: None)

    assert result.status == "passed", result.notes
    assert [request.scenario for request in fake.requests] == ["lt01", "lt02"], "bản nhanh không có LT-05"
    assert all(request.base_url == "http://eval-tadss-1:8080" and request.otlp_endpoint == "eval-grafana:4317" for request in fake.requests)
    windows = json.loads((context.raw_dir("load") / "windows.json").read_text(encoding="utf-8"))
    assert [window["scenario"] for window in windows] == ["lt01", "lt02"]
    assert windows[0]["warmup_seconds"] == 30 and windows[0]["main_start"] == (NOW + timedelta(seconds=30)).isoformat()
    steps = windows[1]["steps"]
    assert [step["rate"] for step in steps] == [10, 50] and steps[1]["start"] > steps[0]["start"]
    assert windows[0]["steps"] == []


def test_full_mode_repeats_lt01_three_times_with_separate_folders(tmp_path: Path) -> None:
    fake = FakeK6()

    load_step.run(_context(tmp_path, "full"), fake, _prometheus(), lambda seconds: None)

    assert [request.scenario for request in fake.requests] == ["lt01-r1", "lt01-r2", "lt01-r3"]
    assert len({request.out_dir for request in fake.requests}) == 3


@pytest.mark.parametrize("status", [400, 429, 500, 503])
def test_forbidden_status_fails_the_step(tmp_path: Path, status: int) -> None:
    result = load_step.run(_context(tmp_path), FakeK6([200, status, 200]), _prometheus(), lambda seconds: None)

    assert result.status == "failed" and any("KHÔNG ĐẠT" in note and str(status) in note for note in result.notes)


def test_status_zero_is_reported_but_does_not_fail(tmp_path: Path) -> None:
    result = load_step.run(_context(tmp_path), FakeK6([200, 0, 200]), _prometheus(), lambda seconds: None)

    assert result.status == "passed" and any("không có phản hồi" in note for note in result.notes)


def test_k6_failure_is_error(tmp_path: Path) -> None:
    result = load_step.run(_context(tmp_path), FakeK6(returncode=127), _prometheus(), lambda seconds: None)

    assert result.status == "error" and "k6 không chạy được" in result.notes[0]


def test_missing_prometheus_data_fails(tmp_path: Path) -> None:
    result = load_step.run(_context(tmp_path), FakeK6(), _prometheus(0.0), lambda seconds: None)

    assert result.status == "failed" and any("KHÔNG CÓ SỐ LIỆU" in note for note in result.notes)


def test_prometheus_error_fails_but_does_not_crash(tmp_path: Path) -> None:
    def broken(base_url, metric, start, end):
        raise prometheus.PrometheusError("mất kết nối")

    result = load_step.run(_context(tmp_path), FakeK6(), broken, lambda seconds: None)

    assert result.status == "failed" and any("không truy vấn được" in note for note in result.notes)


def test_dropped_iterations_are_reported_for_both_summary_shapes(tmp_path: Path) -> None:
    flat, nested, absent = tmp_path / "a.json", tmp_path / "b.json", tmp_path / "c.json"
    flat.write_text(json.dumps({"metrics": {"dropped_iterations": {"count": 89, "rate": 0.5}}}), encoding="utf-8")
    nested.write_text(json.dumps({"metrics": {"dropped_iterations": {"values": {"count": 7}}}}), encoding="utf-8")
    absent.write_text(json.dumps({"metrics": {}}), encoding="utf-8")

    assert load_step.dropped_iterations(flat) == {"total": 89}
    assert load_step.dropped_iterations(nested) == {"total": 7}
    assert load_step.dropped_iterations(absent) == {"total": 0}
    assert load_step.dropped_iterations(tmp_path / "missing.json") is None


def test_dropped_iterations_are_split_by_phase_for_both_summary_shapes(tmp_path: Path) -> None:
    flat, nested = tmp_path / "a.json", tmp_path / "b.json"
    flat.write_text(json.dumps({"metrics": {"dropped_iterations": {"count": 300}, "dropped_iterations{scenario:warmup}": {"count": 294, "rate": 9.8}, "dropped_iterations{scenario:main}": {"count": 6, "rate": 0.1}}}), encoding="utf-8")
    nested.write_text(json.dumps({"metrics": {"dropped_iterations{scenario:warmup}": {"values": {"count": 5}}, "dropped_iterations{scenario:main}": {"values": {"count": 0}}}}), encoding="utf-8")

    assert load_step.dropped_iterations(flat) == {"warmup": 294, "main": 6}
    assert load_step.dropped_iterations(nested) == {"warmup": 5, "main": 0}


def _run_with_summary(tmp_path: Path, metrics: dict) -> list[str]:
    fake = FakeK6()
    original = fake.__call__

    def with_summary(request, base_env):
        result = original(request, base_env)
        result.summary_path.write_text(json.dumps({"metrics": metrics}), encoding="utf-8")
        return result

    return load_step.run(_context(tmp_path), with_summary, _prometheus(), lambda seconds: None).notes


def test_dropped_iterations_note_for_legacy_summary_with_total_only(tmp_path: Path) -> None:
    notes = _run_with_summary(tmp_path, {"dropped_iterations": {"count": 3}})

    assert any("bỏ 3 lượt (dropped_iterations; thường ở pha khởi động, xem summary.json)" in note for note in notes)


def test_dropped_iterations_note_when_only_warmup_dropped(tmp_path: Path) -> None:
    notes = _run_with_summary(tmp_path, {"dropped_iterations{scenario:warmup}": {"count": 294}, "dropped_iterations{scenario:main}": {"count": 0}})

    assert any("k6 bỏ 294 lượt ở warm-up, 0 ở pha chính (pha chính đủ tải)" in note for note in notes)
    assert not any("tải thực tế thấp hơn danh nghĩa" in note for note in notes)


def test_dropped_iterations_note_when_main_phase_dropped(tmp_path: Path) -> None:
    notes = _run_with_summary(tmp_path, {"dropped_iterations{scenario:warmup}": {"count": 10}, "dropped_iterations{scenario:main}": {"count": 184}})

    assert any("k6 bỏ 184 lượt ở pha chính — tải thực tế thấp hơn danh nghĩa (xem summary.json)" in note for note in notes)
    assert not any("pha chính đủ tải" in note for note in notes)


def test_no_dropped_iterations_note_when_nothing_dropped(tmp_path: Path) -> None:
    notes = _run_with_summary(tmp_path, {"dropped_iterations{scenario:warmup}": {"count": 0}, "dropped_iterations{scenario:main}": {"count": 0}})

    assert not any("k6 bỏ" in note for note in notes)


def test_k6_command_and_environment() -> None:
    request = k6.K6Request(Path("/repo/tests/load/scenarios/lt01.js"), "lt01", "quick", "http://t:8080", "key", "EV-1", Path("/ev/raw/load/lt01"), "eval-grafana:4317")

    command = k6.build_command(request)
    env = k6.build_env(request, {"PATH": "p"})

    assert command[:2] == ["k6", "run"] and "--out" in command and command[command.index("--out") + 1] == "opentelemetry" and command[-1].endswith("lt01.js")
    assert env["K6_OTEL_METRIC_PREFIX"] == "k6_" and env["K6_OTEL_GRPC_EXPORTER_INSECURE"] == "true" and env["K6_OTEL_GRPC_EXPORTER_ENDPOINT"] == "eval-grafana:4317"
    assert env["BASE_URL"] == "http://t:8080" and env["MODE"] == "quick" and env["SCENARIO"] == "lt01" and env["PATH"] == "p"
    assert "--out" not in k6.build_command(k6.K6Request(request.script, "lt01", "quick", "u", "k", "r", request.out_dir, None))


def test_run_k6_converts_console_log_to_clean_jsonl(tmp_path: Path) -> None:
    def runner(command: list[str], env: dict[str, str], cwd: Path) -> int:
        console = Path(command[command.index("--console-output") + 1])
        line = json.dumps(_entry(200))
        console.write_text(f'time="x" level=info msg="{line.replace(chr(34), chr(92) + chr(34))}"\ntime="x" level=warning msg="nhiễu"\n', encoding="utf-8")
        return 0

    request = k6.K6Request(tmp_path / "lt01.js", "lt01", "quick", "u", "k", "r", tmp_path / "out", None)

    result = k6.run_k6(request, {}, runner)

    assert result.request_count == 1 and not (tmp_path / "out" / "console.log").exists()
    assert json.loads(result.requests_path.read_text(encoding="utf-8").splitlines()[0])["http_status"] == 200
