"""Điều phối lịch sự cố với client giả: thứ tự bật/tắt, dọn dẹp luôn chạy, đánh giá chaos_log từ dữ liệu mẫu."""
import json
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

from evaluator import faults
from evaluator.chaos_schedule import FAULT, schedule_for
from evaluator.clients import k6
from evaluator.clients.postgres import AuditReader, FallbackStats
from evaluator.clients.toxiproxy import ToxiproxyClient
from evaluator.run_context import build_context
from evaluator.steps import incident_step as step

NOW = datetime(2026, 10, 9, 1, 0, tzinfo=timezone.utc)


class FakeClock:
    def __init__(self) -> None:
        self.now = NOW

    def __call__(self) -> datetime:
        return self.now

    def sleep(self, seconds: float) -> None:
        self.now += timedelta(seconds=seconds)


class FakeToxiproxy:
    def __init__(self, fail_on: str | None = None, leftover: list[str] | None = None) -> None:
        self.calls: list[tuple] = []
        self.fail_on = fail_on
        self.leftover = leftover or []

    def _record(self, *call) -> None:
        self.calls.append(call)
        if call[0] == self.fail_on:
            raise RuntimeError("toxiproxy hỏng")

    def set_enabled(self, proxy: str, enabled: bool) -> None:
        self._record("set_enabled", proxy, enabled)

    def add_latency(self, proxy: str, latency_ms: int) -> None:
        self._record("add_latency", proxy, latency_ms)

    def remove_latency(self, proxy: str) -> None:
        self._record("remove_latency", proxy)

    def reset(self) -> None:
        self._record("reset")

    def problems(self) -> list[str]:
        return self.leftover


def _services(clock: FakeClock, toxiproxy: FakeToxiproxy, profiles: list[tuple], annotations: list | None = None, annotate_error: bool = False, **overrides) -> step.Services:
    def annotate(text: str, tags: list[str], at_ms: int) -> None:
        if annotate_error:
            raise RuntimeError("grafana chết")
        (annotations if annotations is not None else []).append((text, tags))

    base = dict(
        toxiproxy=toxiproxy,
        audit=AuditReader(lambda sql, params: []),
        write_profile=lambda profile, delay_ms: profiles.append((profile, delay_ms)),
        annotate=annotate,
        run_k6=lambda request, env: None,
        query_range=lambda *a: [],
        query_increase=lambda *a: 0.0,
        instance_increase=lambda *a: {},
        ready_status=lambda url: 200,
        fetch_dashboard=lambda: {},
        clock=clock,
        sleep=clock.sleep,
    )
    base.update(overrides)
    return step.Services(**base)


def test_schedule_applies_and_reverts_each_fault_in_order_then_cleans_up() -> None:
    clock, toxi, profiles, annotations, notes = FakeClock(), FakeToxiproxy(), [], [], []
    services = _services(clock, toxi, profiles, annotations)

    execution = step.execute_schedule(services, "quick", NOW, notes)

    assert execution.error is None and execution.cleanup_problems == []
    assert [a.phase.fi_id for a in execution.applied] == ["FI-02", "FI-03", "FI-08", "FI-07"]
    assert all(a.ended_at is not None and a.ended_at > a.started_at for a in execution.applied)
    assert [(p, d) for p, d in profiles[:4]] == [("Delay", 2000), ("None", 2000), ("Throw", 2000), ("None", 2000)]
    assert profiles[-1][0] == "None", "cuối cùng luôn ghi profile None"
    assert [c for c in toxi.calls if c[0] != "reset"] == [("set_enabled", "postgres", False), ("set_enabled", "postgres", True), ("add_latency", "redis", 200), ("remove_latency", "redis")]
    assert toxi.calls[-1] == ("reset",)
    assert [tags[1] for _, tags in annotations] == ["FI-02", "FI-02", "FI-03", "FI-03", "FI-08", "FI-08", "FI-07", "FI-07"]
    assert all("chaos" in tags for _, tags in annotations)
    assert execution.applied[0].started_at == NOW + timedelta(seconds=30), "bật đúng thời điểm của lịch"
    assert clock.now == NOW + timedelta(seconds=405), "đi hết lịch bản nhanh"


def test_cleanup_always_runs() -> None:
    clock, toxi, profiles, notes = FakeClock(), FakeToxiproxy(fail_on="set_enabled"), [], []

    execution = step.execute_schedule(_services(clock, toxi, profiles), "quick", NOW, notes)

    assert execution.error and "toxiproxy hỏng" in execution.error
    assert ("reset",) in toxi.calls and profiles[-1][0] == "None", "dù lỗi giữa lịch vẫn reset toxiproxy và đặt profile None"
    assert [a.phase.fi_id for a in execution.applied] == ["FI-02", "FI-03"], "dừng ngay tại lỗi FI-08"


def test_cleanup_reports_leftover_state_and_failures() -> None:
    services = _services(FakeClock(), FakeToxiproxy(fail_on="reset", leftover=["proxy postgres đang tắt"]), [])

    problems = step.cleanup(services)

    assert any("toxiproxy reset" in p for p in problems) and any("proxy postgres đang tắt" in p for p in problems)


def test_missing_annotation_is_noted_but_not_fatal() -> None:
    notes: list[str] = []
    services = _services(FakeClock(), FakeToxiproxy(), [], annotate_error=True)

    execution = step.execute_schedule(services, "quick", NOW, notes)

    assert execution.error is None
    assert len([n for n in notes if "Annotation Grafana lỗi" in n]) == 8


def _entry(applied: step.AppliedFault, offset: float, **extra) -> dict:
    moment = applied.started_at + timedelta(seconds=offset)
    entry = {"ts": moment.isoformat().replace("+00:00", "Z"), "http_status": 200, "decision_status": "APPROVED", "execution_type": "AUTOMATED", "reason_code": "OK",
             "deposit_rate": 0.3, "duration_ms": 20.0, "idempotency_key": f"k{offset}-{applied.phase.fi_id}"}
    entry.update(extra)
    return entry


def test_evaluate_faults_builds_one_row_per_fault_and_flags_failures() -> None:
    clock, toxi = FakeClock(), FakeToxiproxy()
    execution = step.execute_schedule(_services(clock, toxi, []), "quick", NOW, [])
    entries = []
    for applied in execution.applied:
        fault_end = (applied.ended_at - applied.started_at).total_seconds()
        for offset in range(0, int(fault_end) + 59):  # hết pha hồi phục 60 s, không tràn sang lỗi kế tiếp
            during = (5 if applied.phase.fi_id == "FI-08" else 10) <= offset < fault_end
            if applied.phase.fi_id in ("FI-02", "FI-03"):
                reason = "FALLBACK_STRATEGY_TIMEOUT" if applied.phase.fi_id == "FI-02" else "FALLBACK_STRATEGY_EXCEPTION"
                entries.append(_entry(applied, offset, execution_type="FALLBACK", decision_status="APPROVED", deposit_rate=1.0, reason_code=reason, duration_ms=510.0) if during else _entry(applied, offset))
            elif applied.phase.fi_id == "FI-08":
                entries.append(_entry(applied, offset, http_status=503, decision_status=None, execution_type=None, reason_code=None, deposit_rate=None, error_code="PERSISTENCE_UNAVAILABLE") if during else _entry(applied, offset))
            else:
                entries.append(_entry(applied, offset))
    redis_clean = [[100.0, "0"]]
    counts = {e["idempotency_key"]: 1 for e in entries if e["http_status"] == 200}
    services = _services(
        clock, toxi, [],
        audit=AuditReader(lambda sql, params: [(k, c) for k, c in counts.items()] if "GROUP BY" in sql else [(40, 505, 0)]),
        query_increase=lambda *a: 25.0,
        query_range=lambda expr, start, end, stepsize: [{"metric": {}, "values": [[start.timestamp() + 5, "3"], [start.timestamp() + 20, "0"]]}],
    )

    rows = step.evaluate_faults("EV-test", execution, entries, services)

    assert [row.fi_id for row in rows] == ["FI-02", "FI-03", "FI-08", "FI-07"]
    assert [row.passed for row in rows] == [True, True, True, True], [row.observed for row in rows]
    assert all(row.invariant_violations == 0 for row in rows)
    assert redis_clean  # dữ liệu mẫu dùng ở kiểm Redis phía trên

    broken = [{**e, "http_status": 500, "execution_type": None} if e["idempotency_key"].endswith("FI-02") and e["http_status"] == 200 and e["execution_type"] == "FALLBACK" else e for e in entries]
    assert not step.evaluate_faults("EV-test", execution, broken, services)[0].passed


def test_run_incident_stops_early_when_k6_exits_immediately(tmp_path: Path) -> None:
    repo = tmp_path / "repo"
    (repo / "tests" / "load" / "config").mkdir(parents=True)
    env = {"EVAL_REPO": str(repo), "EVAL_EVIDENCE": str(tmp_path / "ev"), "EVAL_MODE": "quick", "EVAL_STEPS": "c", "EVAL_API_KEY": "k"}
    context = build_context(env, NOW)
    clock, toxi, profiles = FakeClock(), FakeToxiproxy(), []

    def dead_k6(request: k6.K6Request, base_env: dict) -> k6.K6Result:
        return k6.K6Result(127, NOW, NOW, request.out_dir / "requests.jsonl", request.out_dir / "summary.json", 0)

    services = _services(clock, toxi, profiles, run_k6=dead_k6)
    notes: list[str] = []

    outcome = step.run_incident(context, services, notes, [])

    assert outcome is None
    assert clock.now < NOW + timedelta(seconds=30), "không chạy lịch khi k6 đã chết"
    assert any("k6 không chạy được" in n for n in notes) and profiles[-1][0] == "None"
    assert json.dumps(notes)  # ghi chú serialize được


def test_toxiproxy_client_sends_expected_calls() -> None:
    sent: list[tuple] = []

    def send(method: str, url: str, body):
        sent.append((method, url, body))
        if url.endswith("/proxies") and method == "GET":
            return 200, json.dumps({"postgres": {"enabled": False, "toxics": []}, "redis": {"enabled": True, "toxics": [{"name": "latency"}]}})
        return 200, "{}"

    client = ToxiproxyClient("http://tp:8474/", send)
    client.set_enabled("postgres", False)
    client.add_latency("redis", 200)
    client.remove_latency("redis")

    assert sent[0] == ("POST", "http://tp:8474/proxies/postgres", {"enabled": False})
    assert sent[1][2]["attributes"] == {"latency": 200, "jitter": 0} and sent[1][2]["stream"] == "downstream"
    assert sent[2][:2] == ("DELETE", "http://tp:8474/proxies/redis/toxics/latency")
    assert client.problems() == ["proxy postgres đang tắt", "proxy redis còn toxic latency"]


def test_toxiproxy_client_raises_on_error() -> None:
    client = ToxiproxyClient("http://tp:8474", lambda m, u, b: (500, "boom"))

    with pytest.raises(Exception, match="500"):
        client.reset()


def test_write_profile_is_complete_and_atomic(tmp_path: Path) -> None:
    path = tmp_path / "faults" / "fault.json"

    faults.write_profile(path, "Delay", 2000)
    faults.write_profile(path, "None")

    document = json.loads(path.read_text(encoding="utf-8"))
    assert document == {"Tadss": {"Testing": {"FaultInjection": {"Profile": "None", "DelayMs": 2000}}}}
    assert list(path.parent.iterdir()) == [path], "không để file tạm"
    assert schedule_for("quick")[1].role == FAULT and FallbackStats(1, 2, 0).count == 1


def _order_context(tmp_path: Path):
    repo = tmp_path / "repo"
    (repo / "tests" / "load" / "config").mkdir(parents=True)
    env = {"EVAL_REPO": str(repo), "EVAL_EVIDENCE": str(tmp_path / "ev"), "EVAL_MODE": "quick", "EVAL_STEPS": "c", "EVAL_API_KEY": "k"}
    return build_context(env, NOW)


def test_run_measures_scale2_before_incident_faults(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    # SCALE-2 đo tính nhất quán giữa hai replica, không đo hồi phục: chạy trước LT-03 để instance 2 (dùng chung Toxiproxy) chưa dính sự cố
    # nhắm vào instance 1 (EV-20261015-EVAL-full-r3: một 504 ở SCALE-2 ngay sau FI-08/FI-07)
    from evaluator.steps import scale_step

    order: list[str] = []
    monkeypatch.setattr(scale_step, "run_scale2", lambda *args: order.append("scale2") or True)
    monkeypatch.setattr(step, "run_incident", lambda *args: order.append("lt03") or True)

    result = step.run(_order_context(tmp_path), services=object())

    assert order == ["scale2", "lt03"]
    assert result.status == "passed"


@pytest.mark.parametrize(("scale", "incident", "expected", "ran"), [
    (None, True, "error", ["scale2"]),
    (True, None, "error", ["scale2", "lt03"]),
    (False, True, "failed", ["scale2", "lt03"]),
    (True, False, "failed", ["scale2", "lt03"]),
])
def test_run_status_combines_scale2_and_incident(tmp_path: Path, monkeypatch: pytest.MonkeyPatch, scale, incident, expected: str, ran: list[str]) -> None:
    from evaluator.steps import scale_step

    order: list[str] = []
    monkeypatch.setattr(scale_step, "run_scale2", lambda *args: order.append("scale2") or scale)
    monkeypatch.setattr(step, "run_incident", lambda *args: order.append("lt03") or incident)

    result = step.run(_order_context(tmp_path), services=object())

    assert (result.status, order) == (expected, ran), "lỗi hạ tầng ở SCALE-2 thì dừng bước như trước, không chạy lịch sự cố trên stack chưa rõ trạng thái"
