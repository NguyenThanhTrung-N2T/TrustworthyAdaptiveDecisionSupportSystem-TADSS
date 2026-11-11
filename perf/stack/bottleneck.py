"""Phân tích nút cổ chai của BE (Epic 6C, task 2.27; plan 6C B.5.2, B.7.7, P6-43).

    python perf/stack/bottleneck.py <thư mục lượt> [--traces] [--per-endpoint 100] [--tail 30]

Hai cách nhìn, không trộn (B.5.2):
- Trung bình (mọi lượt có telemetry): từ `_sum`/`_count` trong pha steady, thời gian trung bình mỗi request BE chia thành DB (tổng thời gian
  lệnh Npgsql), lời gọi TADSS và phần còn lại (logic BE, chờ CPU, xếp hàng). Các phần nối tiếp nhau nên cộng được. Lệnh DB của job Hangfire
  cũng nằm trong tổng DB nên phần DB là cận trên. Kèm áp lực pool kết nối và rate limiter (giả thuyết B.7.7).
- Từng request (`--traces`, chỉ lượt `up --trace`): cây span lấy từ Tempo qua Grafana "Kiểm thử"; mỗi endpoint lấy một mẫu và riêng đuôi chậm
  (trên p95 của mẫu). Thời gian DB, TADSS, AI của một request là **hợp** các khoảng span con (span chồng nhau chỉ tính một lần); phần còn lại
  là thời gian BE không chờ bên ngoài. Không lấy percentile trừ nhau.
Kết quả ghi `bottleneck.json` trong thư mục lượt.
"""
from __future__ import annotations

import argparse
import json
import sys
import urllib.parse
import urllib.request
from collections.abc import Callable
from pathlib import Path

import verdict

PROMETHEUS = "http://127.0.0.1:9095"
TEMPO = "http://127.0.0.1:3003/api/datasources/proxy/uid/tempo"
BE_SERVICE = "smashcourt-be"
BE = f'job="{BE_SERVICE}"'
TELEMETRY_HOSTS = ("smashcourt-test-alloy",)
TADSS_HOSTS = ("tadss", "toxiproxy")
AI_HOSTS = ("ai",)


def _attr_value(value: dict) -> object:
    return next(iter(value.values()), None)


def parse_trace(payload: dict) -> list[dict]:
    """Trace OTLP JSON của Tempo ⇒ danh sách span phẳng (thời gian nano dạng số nguyên)."""
    spans = []
    for batch in payload.get("batches") or payload.get("resourceSpans") or []:
        resource = {a["key"]: _attr_value(a["value"]) for a in batch.get("resource", {}).get("attributes", [])}
        for scope in batch.get("scopeSpans") or batch.get("instrumentationLibrarySpans") or []:
            for span in scope.get("spans", []):
                spans.append({
                    "service": resource.get("service.name"), "id": span.get("spanId"), "parent": span.get("parentSpanId") or None,
                    "kind": span.get("kind"), "name": span.get("name"),
                    "start": int(span["startTimeUnixNano"]), "end": int(span["endTimeUnixNano"]),
                    "attrs": {a["key"]: _attr_value(a["value"]) for a in span.get("attributes", [])},
                })
    return spans


def union_ms(intervals: list[tuple[float, float]]) -> float:
    """Tổng độ dài của hợp các khoảng (cùng đơn vị với đầu vào)."""
    total, end = 0.0, None
    for low, high in sorted(intervals):
        if end is None or low > end:
            total += high - low
            end = high
        elif high > end:
            total += high - end
            end = high
    return total


def _category(span: dict) -> str | None:
    attrs = span["attrs"]
    if attrs.get("db.system") == "postgresql":
        return "db"
    host = attrs.get("server.address")
    if host in TELEMETRY_HOSTS:
        return None
    if host in TADSS_HOSTS:
        return "tadss"
    if host in AI_HOSTS:
        return "ai"
    return "other"


def _ms(nanoseconds: int) -> float:
    return nanoseconds / 1_000_000


def breakdown(spans: list[dict]) -> dict | None:
    """Một request BE: tổng thời gian và phần DB / TADSS / AI / còn lại (ms). Gốc không phải request BE ⇒ None."""
    ids = {s["id"] for s in spans}
    roots = [s for s in spans if s["service"] == BE_SERVICE and s["kind"] == "SPAN_KIND_SERVER" and s["parent"] not in ids]
    if not roots:
        return None
    root = roots[0]
    parents = {s["id"]: s["parent"] for s in spans}

    def under_root(span: dict) -> bool:
        node = span["parent"]
        while node is not None:
            if node == root["id"]:
                return True
            node = parents.get(node)
        return False

    groups: dict[str, list[tuple[float, float]]] = {"db": [], "tadss": [], "ai": [], "other": []}
    for span in spans:
        if span["service"] != BE_SERVICE or span["kind"] != "SPAN_KIND_CLIENT" or not under_root(span):
            continue
        category = _category(span)
        if category is not None:
            groups[category].append((_ms(span["start"]), _ms(span["end"])))
    total = _ms(root["end"] - root["start"])
    outside = union_ms([i for group in groups.values() for i in group])
    return {
        "endpoint": root["name"], "total": round(total, 3),
        "db": round(union_ms(groups["db"]), 3), "tadss": round(union_ms(groups["tadss"]), 3), "ai": round(union_ms(groups["ai"]), 3),
        "app": round(total - outside, 3), "statements": len(groups["db"]),
    }


_PARTS = ("total", "db", "tadss", "ai", "app", "statements")


def _means(rows: list[dict]) -> dict:
    return {part: round(sum(r[part] for r in rows) / len(rows), 2) for part in _PARTS}


def summarize(rows: list[dict], tail: list[dict] | None = None) -> dict:
    """Theo endpoint: số mẫu, p50/p95 tổng thời gian (nội suy như k6), trung bình từng phần; đuôi chậm tính riêng."""
    result = {}
    for endpoint in sorted({r["endpoint"] for r in rows}):
        own = [r for r in rows if r["endpoint"] == endpoint]
        totals = [r["total"] for r in own]
        slow = [r for r in (tail or []) if r["endpoint"] == endpoint]
        result[endpoint] = {
            "n": len(own), "p50": round(verdict.percentile(totals, 0.5), 2), "p95": round(verdict.percentile(totals, 0.95), 2),
            "mean": _means(own), "tail": {"n": len(slow), "mean": _means(slow) if slow else None},
        }
    return result


def average_queries(seconds: int) -> dict[str, str]:
    r = f"{seconds}s"
    client = f'http_client_request_duration_seconds{{{BE},server_address=~"{"|".join(TADSS_HOSTS)}"}}'
    return {
        "requests": f"sum(increase(http_server_request_duration_seconds_count{{{BE}}}[{r}]))",
        "be_seconds": f"sum(increase(http_server_request_duration_seconds_sum{{{BE}}}[{r}]))",
        "db_seconds": f"sum(increase(db_client_commands_duration_seconds_sum{{{BE}}}[{r}]))",
        "db_commands": f"sum(increase(db_client_commands_duration_seconds_count{{{BE}}}[{r}]))",
        "tadss_calls": f"sum(increase({client.replace('seconds{', 'seconds_count{')}[{r}]))",
        "tadss_seconds": f"sum(increase({client.replace('seconds{', 'seconds_sum{')}[{r}]))",
        "pool_used_max": f'max_over_time(sum(db_client_connections_usage{{{BE},state="used"}})[{r}:15s])',
        "pool_pending_max": f"max_over_time(sum(db_client_connections_pending_requests{{{BE}}})[{r}:15s])",
        "pool_timeouts": f"sum(increase(db_client_connections_timeouts_total{{{BE}}}[{r}]))",
        "rate_limited": f'sum(increase(aspnetcore_rate_limiting_requests_total{{{BE},aspnetcore_rate_limiting_result!="acquired"}}[{r}]))',
    }


def averages(value: Callable[[str], float | None]) -> dict:
    """Phân rã trung bình mỗi request BE (ms) từ các tổng của pha steady; chuỗi không tồn tại nghĩa là chưa từng ghi ⇒ 0."""
    def get(key: str) -> float:
        return value(key) or 0.0

    requests = get("requests")
    if requests <= 0:
        raise ValueError("không có request BE trong khoảng đo (telemetry tắt hoặc sai khoảng)")

    def per(seconds: float) -> float:
        return round(seconds * 1000 / requests, 2)

    be, db, tadss = per(get("be_seconds")), per(get("db_seconds")), per(get("tadss_seconds"))
    calls = get("tadss_calls")
    return {
        "requests": round(requests),
        "perRequestMs": {"be": be, "db": db, "tadss": tadss, "rest": round(be - db - tadss, 2)},
        "dbCommandsPerRequest": round(get("db_commands") / requests, 2),
        "tadssCallMs": round(get("tadss_seconds") * 1000 / calls, 2) if calls else None,
        "pool": {"usedMax": round(get("pool_used_max")), "pendingMax": round(get("pool_pending_max")), "timeouts": round(get("pool_timeouts"))},
        "rateLimited": round(get("rate_limited")),
    }


def _http_json(url: str) -> dict:
    with urllib.request.urlopen(url, timeout=60) as response:
        return json.loads(response.read())


def prometheus_value(expr: str, at: int) -> float | None:
    data = _http_json(f"{PROMETHEUS}/api/v1/query?" + urllib.parse.urlencode({"query": expr, "time": at}))["data"]["result"]
    return float(data[0]["value"][1]) if data else None


def route_means(seconds: int, at: int) -> dict[str, dict]:
    r = f"{seconds}s"
    by = "by (http_request_method, http_route)"
    url = f"{PROMETHEUS}/api/v1/query?" + urllib.parse.urlencode({"query": (
        f"sum {by} (increase(http_server_request_duration_seconds_sum{{{BE}}}[{r}])) * 1000"
        f" / sum {by} (increase(http_server_request_duration_seconds_count{{{BE}}}[{r}]))"), "time": at})
    rows = _http_json(url)["data"]["result"]
    return {f'{x["metric"].get("http_request_method")} {x["metric"].get("http_route")}': round(float(x["value"][1]), 2)
            for x in rows if x["value"][1] not in ("NaN", "+Inf")}


def _tempo_search(query: str, start: int, end: int, limit: int) -> list[str]:
    url = f"{TEMPO}/api/search?" + urllib.parse.urlencode({"q": query, "start": start, "end": end, "limit": limit})
    return [t["traceID"] for t in _http_json(url).get("traces", [])]


def _be_endpoints(start: int, end: int) -> list[str]:
    url = f"{TEMPO}/api/v2/search/tag/name/values?" + urllib.parse.urlencode(
        {"q": f'{{ resource.service.name = "{BE_SERVICE}" && kind = server }}', "start": start, "end": end})
    values = [v.get("value") for v in _http_json(url).get("tagValues", [])]
    return sorted(v for v in values if v and v.split(" ")[0] in ("GET", "POST", "PUT", "PATCH", "DELETE"))


def trace_rows(start: int, end: int, per_endpoint: int, tail: int) -> tuple[list[dict], list[dict]]:
    sample, slow = [], []
    for endpoint in _be_endpoints(start, end):
        base = f'resource.service.name = "{BE_SERVICE}" && kind = server && name = "{endpoint}"'
        rows = [r for r in (breakdown(parse_trace(_http_json(f"{TEMPO}/api/traces/{i}"))) for i in
                            _tempo_search(f"{{ {base} }}", start, end, per_endpoint)) if r]
        sample += rows
        if len(rows) >= 20:
            threshold = verdict.percentile([r["total"] for r in rows], 0.95)
            slow += [r for r in (breakdown(parse_trace(_http_json(f"{TEMPO}/api/traces/{i}"))) for i in
                                 _tempo_search(f"{{ {base} && duration > {threshold:.0f}ms }}", start, end, tail)) if r]
    return sample, slow


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("run_dir", type=Path)
    parser.add_argument("--traces", action="store_true", help="thêm phân rã từ trace (chỉ lượt up --trace)")
    parser.add_argument("--per-endpoint", type=int, default=100)
    parser.add_argument("--tail", type=int, default=30)
    args = parser.parse_args(argv)
    v = json.loads((args.run_dir / "verdict.json").read_text(encoding="utf-8"))
    start, end = int(v["steadyStart"]), int(v["steadyEnd"])
    seconds = max(1, end - start)
    queries = average_queries(seconds)
    result = {"runId": v.get("runId"), "steadyStart": start, "steadyEnd": end,
              "averages": averages(lambda key: prometheus_value(queries[key], end)), "routeMeanMs": route_means(seconds, end)}
    if args.traces:
        sample, slow = trace_rows(start, end, args.per_endpoint, args.tail)
        result["traces"] = summarize(sample, slow)
    (args.run_dir / "bottleneck.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
