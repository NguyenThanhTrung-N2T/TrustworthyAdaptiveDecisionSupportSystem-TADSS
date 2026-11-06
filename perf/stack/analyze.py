"""Phân tích một lượt k6 của môi trường perf (Epic 6C, task 2.16, 2.18; plan 6C Phụ lục B.7.2–B.7.7).

    python perf/stack/analyze.py perf/results/k6/<lượt>             (perf.sh run tự gọi sau mỗi lượt)
    python perf/stack/analyze.py pool <lượt 1> <lượt 2> <lượt 3>    p95/p99 trên mẫu thô ghép của các lượt cùng cấu hình (SP-01 ×3)

1. `resources.json`: theo từng phút từ Prometheus (request/giây, VU, p95 client/server XẤP XỈ theo bucket, lỗi, CPU từng container, đánh dấu *
   khi ≥ 90 % trần, RAM BE); lượt `INFRA_LIMITED` khi có phút mất số liệu hoặc k6 chạm trần CPU/RAM của nó (P6-47, B.7.2).
2. `verdict.json`: chấm theo SLO từ mẫu thô `raw.csv.gz` (verdict.py, phân vị chính xác) theo profile: load/soak/closed (SLO toàn pha steady),
   breakpoint/stress (bậc tải + tài nguyên tại bậc gãy), spike (thời gian hồi phục), soak (RAM, kết nối DB, trôi p95); `INCOMPLETE` khi k6 dừng
   giữa chừng hoặc pha steady ngắn hơn lịch.
Khung thời gian lấy từ `metadata.json` (startedAt) và `summary.json` (testRunDurationMs); lịch tải từ `schedule.json` (mix.js ghi).
"""
from __future__ import annotations

import json
import math
import os
import re
import sys
import urllib.parse
import urllib.request
from collections.abc import Callable
from datetime import datetime
from pathlib import Path

import yaml

import verdict

PROMETHEUS = "http://127.0.0.1:9095"
COMPOSE = Path(__file__).with_name("docker-compose.yml")
CONTAINERS = ["perf-be", "perf-postgres", "perf-tadss", "perf-tadss-postgres", "perf-redis", "perf-ai", "perf-fake-llm", "perf-k6"]
_SELECTOR = 'container_label_com_docker_compose_project="smashcourt-perf"'
_AT_LIMIT = 0.9
Query = Callable[[str, int], list[dict]]

_SCALARS = {
    "rps": "sum(rate(k6_http_reqs_total[1m]))",
    "vus": "max(max_over_time(k6_vus[1m]))",
    "client_p95_s": "histogram_quantile(0.95, sum by (le) (rate(k6_http_req_duration_milliseconds_bucket[1m]))) / 1000",
    "server_be_p95_s": 'histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{job="smashcourt-be",'
                       'deployment_environment_name="perf",http_route!~"/health.*"}[1m])))',
    "error_pct": '(sum(rate(k6_http_reqs_total{status=~"5..|0"}[1m])) or vector(0)) / sum(rate(k6_http_reqs_total[1m])) * 100',
    "be_db_active": 'sum(pg_stat_activity_count{datname="smashcourt",state="active"})',
}


def prometheus_query(expr: str, at: int) -> list[dict]:
    url = f"{PROMETHEUS}/api/v1/query?" + urllib.parse.urlencode({"query": expr, "time": at})
    with urllib.request.urlopen(url, timeout=30) as response:
        return json.load(response)["data"]["result"]


_COMPOSE_VARIABLE = re.compile(r"^\$\{(?P<name>[A-Z0-9_]+):-(?P<default>[^}]*)\}$")


def _compose_value(raw: object) -> str:
    """Giá trị compose dạng `${TÊN:-mặc định}` (perf.sh truyền biến lúc up) ⇒ biến môi trường nếu có, không thì mặc định."""
    match = _COMPOSE_VARIABLE.match(str(raw).strip())
    return os.environ.get(match["name"], match["default"]) if match else str(raw)


def limits_from_compose(path: Path = COMPOSE, run_dir: Path | None = None) -> dict[str, float]:
    services = yaml.safe_load(path.read_text(encoding="utf-8"))["services"]
    limits = {s["container_name"]: float(_compose_value(s["cpus"])) for s in services.values() if "cpus" in s}
    # Trần CPU thật của BE trong lượt (perf.sh ghi từ container đang chạy): đúng cả khi up --be-cpus mà shell phân tích không có biến
    metadata = run_dir / "metadata.json" if run_dir else None
    if metadata and metadata.exists() and (be_cpus := json.loads(metadata.read_text(encoding="utf-8")).get("beCpus")):
        limits["perf-be"] = float(be_cpus)
    return limits


def memory_limits_from_compose(path: Path = COMPOSE) -> dict[str, float]:
    services = yaml.safe_load(path.read_text(encoding="utf-8"))["services"]
    units = {"g": 1024, "m": 1}
    return {s["container_name"]: float(str(s["mem_limit"])[:-1]) * units[str(s["mem_limit"])[-1].lower()]
            for s in services.values() if "mem_limit" in s}


def infra_problems(rows: list[dict], cpu_limits: dict[str, float], memory_limits: dict[str, float]) -> list[str]:
    """Phút mất số liệu (máy đo / VM đứng) và k6 chạm trần CPU/RAM của nó (B.7.2): lượt không dùng để kết luận năng lực."""
    infra = verdict.load_slo()["infra"]
    problems = [f"phút {row['minute']}: không có số liệu từ mọi nguồn" for row in rows if row["rps"] is None and not row["cpu"]]
    for row in rows:
        cpu, memory = row["cpu"].get("perf-k6"), row["memory_mb"].get("perf-k6")
        if cpu is not None and "perf-k6" in cpu_limits and cpu >= infra["k6CpuMax"] * cpu_limits["perf-k6"]:
            problems.append(f"phút {row['minute']}: perf-k6 dùng {cpu:.2f}/{cpu_limits['perf-k6']} lõi")
        if memory is not None and "perf-k6" in memory_limits and memory >= infra["k6MemoryMax"] * memory_limits["perf-k6"]:
            problems.append(f"phút {row['minute']}: perf-k6 dùng {memory}/{memory_limits['perf-k6']:.0f} MB")
    return problems


def window(run_dir: Path) -> tuple[int, int]:
    metadata = json.loads((run_dir / "metadata.json").read_text(encoding="utf-8"))
    summary = json.loads((run_dir / "summary.json").read_text(encoding="utf-8"))
    start = int(datetime.fromisoformat(metadata["startedAt"].replace("Z", "+00:00")).timestamp())
    return start, start + round(summary["state"]["testRunDurationMs"] / 1000)


def _value(result: list[dict]) -> float | None:
    if not result:
        return None
    value = float(result[0]["value"][1])
    return None if math.isnan(value) else round(value, 3)


def _by_name(result: list[dict]) -> dict[str, float]:
    return {r["metric"].get("name", "?"): float(r["value"][1]) for r in result}


def minutes(start: int, end: int, query: Query, limits: dict[str, float]) -> list[dict]:
    rows = []
    for index in range(1, math.ceil((end - start) / 60) + 1):
        at = min(start + index * 60, end)
        cpu = {name: round(v, 3) for name, v in _by_name(query(f"sum by (name) (rate(container_cpu_usage_seconds_total{{{_SELECTOR}}}[1m]))", at)).items()}
        memory = {name: round(v / 2**20) for name, v in _by_name(query(f"max by (name) (container_memory_working_set_bytes{{{_SELECTOR}}})", at)).items()}
        row = {"minute": index, "at": at, **{key: _value(query(expr, at)) for key, expr in _SCALARS.items()}}
        row["cpu"], row["memory_mb"] = cpu, memory
        row["cpu_at_limit"] = sorted(n for n, v in cpu.items() if n in limits and v >= _AT_LIMIT * limits[n])
        rows.append(row)
    return rows


def _fmt(value: float | None, digits: int = 2, width: int = 6) -> str:
    return f"{'-':>{width}}" if value is None else f"{value:{width}.{digits}f}"


def report(run_dir: Path, query: Query = prometheus_query, limits: dict[str, float] | None = None,
           memory_limits: dict[str, float] | None = None) -> str:
    limits = limits_from_compose(run_dir=run_dir) if limits is None else limits
    memory_limits = memory_limits_from_compose() if memory_limits is None else memory_limits
    start, end = window(run_dir)
    rows = minutes(start, end, query, limits)
    # Phút không có mẫu nào của k6 lẫn cAdvisor = máy đo / VM Docker đứng (không phải ứng dụng chậm) ⇒ lượt INFRA_LIMITED (P6-47)
    gaps = [row["minute"] for row in rows if row["rps"] is None and not row["cpu"]]
    infra = infra_problems(rows, limits, memory_limits)
    validity = "INFRA_LIMITED" if infra else "OK"
    (run_dir / "resources.json").write_text(json.dumps({"runId": run_dir.name, "start": start, "end": end, "cpuLimits": limits,
                                                        "memoryLimitsMb": memory_limits, "gapMinutes": gaps, "infra": infra,
                                                        "validity": validity, "minutes": rows},
                                                       ensure_ascii=False, indent=2), encoding="utf-8")
    shown = [c for c in CONTAINERS if any(c in row["cpu"] for row in rows)] or sorted({n for row in rows for n in row["cpu"]})
    head = f"{'phút':>4} {'req/s':>6} {'VU':>5} {'p95 k6':>6} {'p95 BE':>6} {'lỗi %':>6} {'DB act':>6} " + " ".join(f"{c[5:]:>10}" for c in shown) + " BE MB"
    lines = [f"Tài nguyên theo phút của lượt {run_dir.name} (CPU: lõi, * = ≥ 90 % trần trong compose perf; p95: giây, xấp xỉ theo bucket)", head]
    for row in rows:
        cpu = " ".join(f"{row['cpu'].get(c, 0):9.2f}{'*' if c in row['cpu_at_limit'] else ' '}" for c in shown)
        lines.append(f"{row['minute']:4d} {_fmt(row['rps'], 1)} {_fmt(row['vus'], 0, 5)} {_fmt(row['client_p95_s'])} {_fmt(row['server_be_p95_s'])} "
                     f"{_fmt(row['error_pct'])} {_fmt(row['be_db_active'], 0)} {cpu} {row['memory_mb'].get('perf-be', 0):5d}")
    if infra:
        lines.append(f"CẢNH BÁO INFRA_LIMITED: {'; '.join(infra[:5])}{' …' if len(infra) > 5 else ''} (máy đo / VM Docker đứng hoặc k6 thiếu "
                     "tài nguyên); lượt này không dùng để kết luận năng lực (P6-47), dashboard ghi là hạn chế")
    return "\n".join(lines)


# ── chấm theo SLO (verdict.json) ──────────────────────────────────────────────────────────────────────────────────────────────

_BE = 'job="smashcourt-be",deployment_environment_name="perf"'


def _resource_queries(seconds: float) -> dict[str, str]:
    """Truy vấn cố định ghi lại tài nguyên trong một khoảng (bậc tải) kết thúc tại thời điểm truy vấn (B.7.5)."""
    r = f"{max(int(seconds), 15)}s"
    return {
        "cpu": f"sum by (name) (rate(container_cpu_usage_seconds_total{{{_SELECTOR}}}[{r}]))",
        "memoryMaxMb": f"max by (name) (max_over_time(container_memory_working_set_bytes{{{_SELECTOR}}}[{r}])) / 1048576",
        "gcPerSecond": f"sum by (generation) (rate(process_runtime_dotnet_gc_collections_count_total{{{_BE}}}[{r}]))",
        "threadPoolQueueMax": f"max(max_over_time(process_runtime_dotnet_thread_pool_queue_length{{{_BE}}}[{r}]))",
        "threadPoolThreadsMax": f"max(max_over_time(process_runtime_dotnet_thread_pool_threads_count{{{_BE}}}[{r}]))",
        "npgsqlConnectionsMax": f"max by (state) (max_over_time(db_client_connections_usage{{{_BE}}}[{r}]))",
        # chuỗi chỉ xuất hiện khi có request chờ pool / có quyết định đường lùi: không có = 0
        "npgsqlPendingMax": f"max(max_over_time(db_client_connections_pending_requests{{{_BE}}}[{r}])) or vector(0)",
        "npgsqlPoolMax": f"max(db_client_connections_max{{{_BE}}})",
        "postgresConnectionsByStateMax": f'max by (state) (max_over_time(pg_stat_activity_count{{datname="smashcourt"}}[{r}]))',
        "postgresLocksMax": f'max(max_over_time(pg_locks_count{{datname="smashcourt"}}[{r}]))',
        "tadssFallbackShare": f'(sum(increase(tadss_decisions_total{{instance="perf",execution_type="FALLBACK"}}[{r}])) or vector(0)) '
                              f'/ sum(increase(tadss_decisions_total{{instance="perf"}}[{r}]))',
    }


def resources_between(query: Query, start: float, end: float) -> dict:
    queries = _resource_queries(end - start)
    snapshot: dict = {"from": start, "to": end, "queries": queries}
    for key, expr in queries.items():
        try:
            snapshot[key] = [{"labels": r["metric"], "value": r["value"][1]} for r in query(expr, int(end))]
        except OSError as error:   # Prometheus không trả lời: vẫn ghi verdict, đánh dấu thiếu
            snapshot[key] = f"lỗi truy vấn: {error}"
    return snapshot


def _average(query: Query, expr: str, at: float) -> float | None:
    result = query(expr, int(at))
    return float(result[0]["value"][1]) if result else None


def _soak_pairs(query: Query, steady_start: float, steady_end: float, slo: dict) -> tuple[tuple, tuple]:
    """Trung bình phút 15–25 của pha steady và trung bình 10 phút cuối: RAM BE, số kết nối PostgreSQL của BE (B.7.4)."""
    cfg = slo["soak"]
    span = (cfg["baselineToMinute"] - cfg["baselineFromMinute"]) * 60
    baseline_end, tail = steady_start + cfg["baselineToMinute"] * 60, cfg["tailMinutes"] * 60
    memory = 'avg_over_time(container_memory_working_set_bytes{name="perf-be"}[%ds])'
    connections = 'avg_over_time(sum(pg_stat_activity_count{datname="smashcourt"})[%ds:15s])'
    return ((_average(query, memory % span, baseline_end), _average(query, memory % tail, steady_end)),
            (_average(query, connections % span, baseline_end), _average(query, connections % tail, steady_end)))


def _validity(run_dir: Path, raw: verdict.Raw, schedule: dict, resources: dict) -> tuple[str, list[str], int | None, float, float]:
    exit_file = run_dir / "k6-exit"
    k6_exit = int(exit_file.read_text(encoding="utf-8").strip()) if exit_file.exists() else None
    problems = []
    if k6_exit not in (0, 99):   # 0 = mọi ngưỡng đạt, 99 = có ngưỡng hỏng; mã khác = dừng giữa chừng / lỗi script
        problems.append(f"k6 thoát {k6_exit}")
    steady = [s.t for s in raw.durations if s.phase == "steady"]
    start, end = (min(steady), max(steady)) if steady else (0.0, 0.0)
    expected = schedule.get("steadySeconds")
    if not steady:
        problems.append("không có mẫu pha steady")
    elif expected and end - start < expected - 30:
        problems.append(f"pha steady {end - start:.0f} s, lịch {expected} s")
    return ("INCOMPLETE" if problems else resources.get("validity", "OK")), problems, k6_exit, start, end


def evaluate(run_dir: Path, query: Query = prometheus_query, slo: dict | None = None) -> dict:
    slo = slo or verdict.load_slo()
    schedule = json.loads((run_dir / "schedule.json").read_text(encoding="utf-8"))
    resources_file = run_dir / "resources.json"
    resources = json.loads(resources_file.read_text(encoding="utf-8")) if resources_file.exists() else {}
    raw = verdict.read_raw(run_dir / "raw.csv.gz")
    profile, selftest = schedule["profile"], bool(schedule.get("selftest"))
    validity, problems, k6_exit, start, end = _validity(run_dir, raw, schedule, resources)
    result: dict = {"runId": run_dir.name, "profile": profile, "selftest": selftest, "k6Exit": k6_exit, "problems": problems,
                    "validity": validity, "infra": resources.get("infra", []), "steadyStart": start, "steadyEnd": end}

    if profile in ("breakpoint", "stress"):
        steps = verdict.step_verdict(raw, start, schedule["steps"], slo)
        result.update(steps)
        index = steps["breakStep"]
        if index is not None:
            at = steps["steps"][index]
            result["resourcesAtBreak"] = {"break": resources_between(query, at["holdStart"], at["holdEnd"])}
            if index > 0:
                before = steps["steps"][index - 1]
                result["resourcesAtBreak"]["before"] = resources_between(query, before["holdStart"], before["holdEnd"])
        # breakpoint chưa tới điểm gãy ở bậc tối đa ⇒ chưa trả lời được câu hỏi; stress là mô tả: lượt hợp lệ là đủ, đọc từng bậc
        result["status"] = verdict.PASS if profile == "stress" or index is not None else verdict.INSUFFICIENT
    elif profile == "spike":
        recovery_start = start + schedule["recoveryOffset"]
        result["recovery"] = verdict.spike_recovery(raw, recovery_start, recovery_start + schedule["recoverySeconds"], slo)
        result["status"] = result["recovery"]["status"]
    elif profile == "soak":
        memory, connections = _soak_pairs(query, start, end, slo)
        result.update(verdict.soak_verdict(raw, start, end, slo, memory, connections))
    else:
        result.update(verdict.slo_verdict(raw, slo, selftest))
        if profile == "smoke":   # kiểm đường đi, không đo: chỉ chấm lỗi
            result["status"] = result["errors"]["status"]
    result["episodes"] = verdict.episodes(raw, slo)   # đợt nghẽn: so lượt đối chứng / thí nghiệm (plan 6C 2C)
    (run_dir / "verdict.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    return result


def _group_lines(result: dict) -> list[str]:
    lines = []
    for name, group in (result.get("groups") or (result.get("slo") or {}).get("groups") or {}).items():
        lines.append(f"  {name:8} n={group['n']:6d}  p95 {group.get('p95')} ms (< {group.get('p95Limit')}): "
                     f"{verdict.LABELS[group['p95Status']]:16}  p99 {group.get('p99')} ms (< {group.get('p99Limit')}): "
                     f"{verdict.LABELS[group['p99Status']]}")
    errors = result.get("errors") or (result.get("slo") or {}).get("errors")
    if errors and errors["n"]:
        lines.append(f"  lỗi     n={errors['n']:6d}  {errors['rate'] * 100:.3f} % (< {errors['limit'] * 100:.1f} %): "
                     f"{verdict.LABELS[errors['status']]}")
    for name, endpoint in (result.get("endpoints") or {}).items():
        if endpoint["slo"] == "write":
            lines.append(f"    {name}: n={endpoint['n']} p95 {endpoint.get('p95')} ms: {verdict.LABELS[endpoint['p95Status']]}")
    return lines


def describe(result: dict) -> str:
    label = verdict.LABELS.get(result.get("status", ""), result.get("status", "-"))
    problems = f" ({'; '.join(result['problems'])})" if result.get("problems") else ""
    lines = [f"Kết luận lượt {result['runId']} ({result['profile']}{', TỰ KIỂM' if result.get('selftest') else ''}): {label}"
             f" · hợp lệ: {result.get('validity')}{problems}", *_group_lines(result)]
    for step in result.get("steps", []):
        windows = ", ".join(f"Đọc p95 {w['groups'].get('read', {}).get('p95')} ms (n={w['groups'].get('read', {}).get('n', 0)}) "
                            f"lỗi {(w['errors']['rate'] or 0) * 100:.2f} % bỏ {(w['dropped']['share'] or 0) * 100:.2f} %" for w in step["windows"])
        mark = f" ⇒ VI PHẠM {', '.join(step['violations'])}" if step["violations"] else ""
        lines.append(f"  bậc {step['rate']:>4} hành trình/s: {windows}{mark}")
    if "breakRate" in result:
        lines.append(f"  điểm gãy: {result['breakRate']} hành trình/giây" if result["breakRate"] is not None
                     else "  điểm gãy: chưa tới ở bậc tối đa (tăng --max-rate)")
    if "episodes" in result:
        found = result["episodes"]
        lines.append(f"  đợt nghẽn (cửa sổ 10 s p95 Đọc > 300 ms): {found['count']}"
                     + "".join(f" · {e['windows']} cửa sổ, p95 tới {e['maxP95']} ms" for e in found["episodes"][:8]))
    if "recovery" in result:
        lines.append(f"  hồi phục sau spike: {result['recovery']['seconds']} s")
    for key in ("memory", "connections", "p95Drift"):
        if key in result:
            lines.append(f"  {key}: {json.dumps({k: v for k, v in result[key].items() if k != 'windows'}, ensure_ascii=False)}")
    return "\n".join(lines)


def _signature(run_dir: Path) -> tuple:
    metadata = json.loads((run_dir / "metadata.json").read_text(encoding="utf-8"))
    return (metadata.get("profile"), metadata.get("args"), (metadata.get("seed") or {}).get("dbChecksum"),
            *((metadata.get(s) or {}).get("image") for s in ("be", "tadss", "ai")))


def pool(run_dirs: list[Path], slo: dict | None = None) -> dict:
    """p95/p99 trên mẫu thô ghép của các lượt cùng profile, tham số, dữ liệu và image (B.7.2): không lấy trung bình các p99."""
    signatures = {_signature(run) for run in run_dirs}
    if len(signatures) != 1:
        raise ValueError(f"các lượt khác cấu hình, không gộp được: {sorted(map(str, signatures))}")
    result = verdict.pool([verdict.read_raw(run / "raw.csv.gz") for run in run_dirs], slo or verdict.load_slo())
    result["runs"] = [run.name for run in run_dirs]
    return result


def main(argv: list[str]) -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    if len(argv) > 1 and argv[1] == "pool":
        runs = [Path(a) for a in argv[2:]]
        result = pool(runs)
        target = runs[0].parent / f"pooled-{runs[0].name}-{len(runs)}runs.json"
        target.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        print(describe({**result, "runId": " + ".join(result["runs"]), "profile": "gộp", "validity": "OK"}))
        print(f"Ghi {target}")
        return 0
    run_dir = Path(argv[1])
    print(report(run_dir))
    if (run_dir / "raw.csv.gz").exists() and (run_dir / "schedule.json").exists():
        print(describe(evaluate(run_dir)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
