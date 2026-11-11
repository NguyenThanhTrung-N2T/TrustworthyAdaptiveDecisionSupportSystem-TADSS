# Task 2.27 (plan 6C B.5.2, B.7.7): phân rã thời gian của request BE từ trace (cây span) và từ metric (trung bình cộng được)
import sys
from pathlib import Path

import pytest

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bottleneck


def _span(span_id, parent, start_ms, end_ms, *, service="smashcourt-be", kind="SPAN_KIND_CLIENT", name="smashcourt", **attrs):
    return {"service": service, "id": span_id, "parent": parent, "kind": kind, "name": name,
            "start": start_ms * 1_000_000, "end": end_ms * 1_000_000, "attrs": attrs}


def _otlp(spans):
    # Dạng Tempo trả về: batches ⇒ resource.attributes ⇒ scopeSpans ⇒ spans (thời gian nano dạng chuỗi)
    batches = {}
    for s in spans:
        batch = batches.setdefault(s["service"], {
            "resource": {"attributes": [{"key": "service.name", "value": {"stringValue": s["service"]}}]},
            "scopeSpans": [{"scope": {"name": "x"}, "spans": []}]})
        batch["scopeSpans"][0]["spans"].append({
            "spanId": s["id"], "parentSpanId": s["parent"] or "", "kind": s["kind"], "name": s["name"],
            "startTimeUnixNano": str(s["start"]), "endTimeUnixNano": str(s["end"]),
            "attributes": [{"key": k, "value": {"stringValue": v}} for k, v in s["attrs"].items()]})
    return {"batches": list(batches.values())}


BOOKING = [
    _span("r", None, 0, 100, kind="SPAN_KIND_SERVER", name="POST api/bookings/online", **{"http.route": "api/bookings/online"}),
    _span("d1", "r", 5, 15, **{"db.system": "postgresql"}),
    _span("d2", "r", 10, 20, **{"db.system": "postgresql"}),          # chồng d1 5 ms: chỉ tính một lần
    _span("t", "r", 40, 70, name="POST", **{"server.address": "tadss"}),
    _span("ts", "t", 41, 69, service="tadss-runtime", kind="SPAN_KIND_SERVER", name="POST api/v2/decisions"),
    _span("td", "ts", 42, 50, service="tadss-runtime", **{"db.system": "postgresql"}),  # DB của TADSS không tính vào DB của BE
    _span("x", "r", 80, 90, name="POST", **{"server.address": "smashcourt-test-alloy"}),  # xuất telemetry: không thuộc request
]


def test_union_counts_overlapping_intervals_once():
    assert bottleneck.union_ms([(0, 10), (5, 15), (20, 25)]) == 20
    assert bottleneck.union_ms([]) == 0


def test_breakdown_splits_be_time_into_db_tadss_and_app_without_double_counting():
    row = bottleneck.breakdown(bottleneck.parse_trace(_otlp(BOOKING)))
    assert row["endpoint"] == "POST api/bookings/online"
    assert row["total"] == 100
    assert row["db"] == 15 and row["statements"] == 2
    assert row["tadss"] == 30 and row["ai"] == 0
    # phần còn lại = tổng − hợp mọi span con của BE gọi ra ngoài (DB, TADSS); span xuất telemetry không tính
    assert row["app"] == 55


def test_breakdown_ignores_traces_whose_root_is_not_a_be_request():
    spans = [_span("r", None, 0, 10, service="tadss-runtime", kind="SPAN_KIND_SERVER", name="POST api/v2/decisions")]
    assert bottleneck.breakdown(bottleneck.parse_trace(_otlp(spans))) is None


def test_summarize_reports_distribution_means_and_the_slow_tail_per_endpoint():
    rows = [{"endpoint": "POST api/auth/login", "total": t, "db": 10, "tadss": 0, "ai": 0, "app": t - 10, "statements": 6}
            for t in range(100, 300)]
    result = bottleneck.summarize(rows, tail=[{"endpoint": "POST api/auth/login", "total": 900, "db": 20, "tadss": 0, "ai": 0,
                                                 "app": 880, "statements": 6}])
    login = result["POST api/auth/login"]
    # cùng cách nội suy với verdict.percentile (như k6)
    assert login["n"] == 200 and login["p50"] == 199.5 and login["p95"] == 289.05
    assert login["mean"] == {"total": 199.5, "db": 10, "tadss": 0, "ai": 0, "app": 189.5, "statements": 6}
    assert login["tail"]["n"] == 1 and login["tail"]["mean"]["app"] == 880


def test_averages_divide_component_time_by_be_requests_and_flag_pool_and_rate_limit_pressure():
    values = {
        "requests": 1000, "be_seconds": 50.0, "db_seconds": 20.0, "db_commands": 8000,
        "tadss_calls": 100, "tadss_seconds": 1.0, "pool_used_max": 7, "pool_pending_max": None,
        "pool_timeouts": None, "rate_limited": None,
    }
    avg = bottleneck.averages(lambda key: values[key])
    assert avg["perRequestMs"] == {"be": 50.0, "db": 20.0, "tadss": 1.0, "rest": 29.0}
    assert avg["dbCommandsPerRequest"] == 8.0 and avg["tadssCallMs"] == 10.0
    # chuỗi không có (Npgsql / rate limiter chưa từng ghi) nghĩa là 0, không phải thiếu số liệu
    assert avg["pool"] == {"usedMax": 7, "pendingMax": 0, "timeouts": 0} and avg["rateLimited"] == 0


def test_averages_refuse_a_window_without_be_requests():
    with pytest.raises(ValueError):
        bottleneck.averages(lambda key: 0 if key == "requests" else None)
