"""SCALE-2 (O-18): ba điều kiện cộng điều kiện có request lặp; annotation, snapshot và query_range của client."""
import json

from evaluator.clients import grafana, prometheus
from evaluator.steps.scale_step import evaluate_scale2


def _entry(key: str, status: int = 200, decision: str | None = "d", repeat: bool = False) -> dict:
    return {"ts": "2026-10-09T01:00:00.000Z", "http_status": status, "idempotency_key": key, "decision_id": decision if status == 200 else None, "repeat": repeat}


def _good() -> tuple[list[dict], dict[str, int], dict[str, float]]:
    entries = [_entry("a", decision="da"), _entry("a", decision="da", repeat=True), _entry("b", decision="db")]
    return entries, {"a": 1, "b": 1}, {"tadss-1": 120.0, "tadss-2": 80.0}


def test_scale2_checks_pass_when_all_conditions_hold() -> None:
    verdict = evaluate_scale2(*_good())

    assert verdict["passed"], verdict
    assert set(verdict["checks"]) == {"no_server_errors", "single_primary_per_key", "same_decision_id_per_key", "both_instances_in_prometheus", "repeats_exercised"}


def test_scale2_fails_on_server_error() -> None:
    entries, counts, instances = _good()

    verdict = evaluate_scale2(entries + [_entry("c", status=503)], counts, instances)

    assert not verdict["checks"]["no_server_errors"]["passed"] and not verdict["passed"]


def test_scale2_fails_when_key_has_two_primaries_or_none() -> None:
    entries, _, instances = _good()

    assert not evaluate_scale2(entries, {"a": 2, "b": 1}, instances)["checks"]["single_primary_per_key"]["passed"]
    assert not evaluate_scale2(entries, {"a": 1}, instances)["checks"]["single_primary_per_key"]["passed"]


def test_scale2_fails_when_same_key_returns_different_decision_ids() -> None:
    entries, counts, instances = _good()
    entries[1]["decision_id"] = "khac"

    assert not evaluate_scale2(entries, counts, instances)["checks"]["same_decision_id_per_key"]["passed"]


def test_scale2_fails_when_one_instance_has_no_metrics() -> None:
    entries, counts, _ = _good()

    for instances in ({"tadss-1": 100.0}, {"tadss-1": 100.0, "tadss-2": 0.0}, {}):
        assert not evaluate_scale2(entries, counts, instances)["checks"]["both_instances_in_prometheus"]["passed"]


def test_scale2_instance_detail_prints_rounded_numbers_but_decides_on_real_values() -> None:
    entries, counts, _ = _good()

    verdict = evaluate_scale2(entries, counts, {"tadss-1": 1475.047, "tadss-2": 1459.28})
    detail = verdict["checks"]["both_instances_in_prometheus"]["detail"]

    assert "'tadss-1': 1475" in detail and "'tadss-2': 1459" in detail and "1475.04" not in detail and "1459.28" not in detail
    # Giá trị 0.4 làm tròn thành 0 nhưng vẫn > 0: quyết định đạt/không dùng số thực
    assert evaluate_scale2(entries, counts, {"tadss-1": 0.4, "tadss-2": 0.4})["checks"]["both_instances_in_prometheus"]["passed"]


def test_scale2_fails_when_no_request_was_repeated() -> None:
    entries, counts, instances = _good()
    for entry in entries:
        entry["repeat"] = False

    assert not evaluate_scale2(entries, counts, instances)["checks"]["repeats_exercised"]["passed"]


def test_annotate_posts_chaos_tags() -> None:
    sent = []
    grafana.annotate("http://g:3000/", "FI-02 bật", ["chaos", "FI-02"], 1700000000000, lambda m, u, b: (sent.append((m, u, b)) or (200, "{}")))

    assert sent == [("POST", "http://g:3000/api/annotations", {"dashboardUID": "tadss-runtime", "time": 1700000000000, "tags": ["chaos", "FI-02"], "text": "FI-02 bật"})]


def test_annotate_raises_on_http_error() -> None:
    try:
        grafana.annotate("http://g:3000", "x", ["chaos"], 1, lambda m, u, b: (401, "no"))
    except grafana.GrafanaError as error:
        assert "401" in str(error)
    else:
        raise AssertionError("phải ném GrafanaError")


def test_snapshot_collects_prometheus_panels_and_survives_query_errors() -> None:
    dashboard = {
        "dashboard": {"uid": "tadss-runtime", "panels": [
            {"id": 1, "title": "RPS", "type": "timeseries", "datasource": {"type": "prometheus"}, "targets": [{"refId": "A", "expr": 'sum(rate(x{instance=~"$instance"}[1m]))'}]},
            {"id": 2, "title": "Hàng", "type": "row", "panels": [
                {"id": 3, "title": "Lỗi", "type": "stat", "datasource": {"type": "prometheus"}, "targets": [{"refId": "A", "expr": "boom"}]},
                {"id": 4, "title": "Log", "type": "logs", "datasource": {"type": "loki"}, "targets": [{"refId": "A", "expr": "{app=\"x\"}"}]},
            ]},
        ]},
        "meta": {"folderTitle": "TADSS"},
    }
    seen = []

    def run_query(expr: str) -> list[dict]:
        seen.append(expr)
        if expr == "boom":
            raise RuntimeError("lỗi truy vấn")
        return [{"metric": {}, "values": [[1, "2"]]}]

    snapshot = grafana.build_snapshot(dashboard, {"start": "a", "end": "b"}, run_query)

    assert seen == ['sum(rate(x{instance=~".*"}[1m]))', "boom"], "thay $instance, bỏ panel Loki"
    assert [panel["id"] for panel in snapshot["panels"]] == [3, 1] or [panel["id"] for panel in snapshot["panels"]] == [1, 3]
    broken = next(panel for panel in snapshot["panels"] if panel["id"] == 3)
    assert "lỗi truy vấn" in broken["queries"][0]["error"]
    assert snapshot["dashboard"]["uid"] == "tadss-runtime" and snapshot["window"] == {"start": "a", "end": "b"}
    json.dumps(snapshot)


def test_prometheus_query_range_and_series_values() -> None:
    body = json.dumps({"status": "success", "data": {"result": [{"metric": {}, "values": [[10, "1"], [15, "0"]]}, {"metric": {}, "values": [[10, "2"], [15, "0"]]}]}})
    urls = []
    from datetime import datetime, timezone

    result = prometheus.query_range("http://p/", "up", datetime(2026, 1, 1, tzinfo=timezone.utc), datetime(2026, 1, 1, 0, 1, tzinfo=timezone.utc), 5, lambda url: (urls.append(url) or (200, body)))

    assert "query_range" in urls[0] and "step=5" in urls[0]
    assert prometheus.series_values(result) == [(10.0, 3.0), (15.0, 0.0)]


def test_wait_instances_ready_polls_until_both_are_healthy() -> None:
    from evaluator.steps.scale_step import wait_instances_ready

    answers = {"http://a/health/ready": [503, 503, 200], "http://b/health/ready": [200]}
    clock = {"now": 0.0}
    notes: list[str] = []

    def probe(url: str) -> int:
        queue = answers[url]
        return queue.pop(0) if len(queue) > 1 else queue[0]

    def sleep(seconds: float) -> None:
        clock["now"] += seconds

    assert wait_instances_ready(["http://a", "http://b/"], probe, notes, lambda: clock["now"], sleep)
    assert clock["now"] == 6.0 and notes == []


def test_wait_instances_ready_times_out_with_a_note() -> None:
    from evaluator.steps.scale_step import wait_instances_ready

    clock = {"now": 0.0}
    notes: list[str] = []

    ok = wait_instances_ready(["http://a"], lambda url: 503, notes, lambda: clock["now"], lambda s: clock.__setitem__("now", clock["now"] + s), timeout=10)

    assert not ok and "http://a" in notes[0]
