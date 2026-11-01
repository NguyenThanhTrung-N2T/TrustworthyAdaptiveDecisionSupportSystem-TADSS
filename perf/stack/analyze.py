"""Phân tích một lượt k6 của môi trường perf theo từng phút từ Prometheus (Epic 6C, task 2.16; plan 6C Phụ lục B.7.5, B.7.7).

    python perf/stack/analyze.py perf/results/k6/<lượt>        (perf.sh run tự gọi sau mỗi lượt)

Ghi `resources.json` vào thư mục lượt (bản số liệu cho báo cáo, không phụ thuộc Prometheus còn giữ dữ liệu) và in bảng: request/giây, VU,
p95 phía client (k6) và phía server (BE), tỉ lệ lỗi, CPU từng container (đánh dấu * khi ≥ 90 % trần trong compose perf), RAM của BE.
Khung thời gian lấy từ `metadata.json` (startedAt) và `summary.json` (testRunDurationMs). Percentile từ histogram là xấp xỉ theo bucket.
"""
from __future__ import annotations

import json
import math
import sys
import urllib.parse
import urllib.request
from collections.abc import Callable
from datetime import datetime
from pathlib import Path

import yaml

PROMETHEUS = "http://127.0.0.1:9095"
COMPOSE = Path(__file__).with_name("docker-compose.yml")
CONTAINERS = ["perf-be", "perf-postgres", "perf-tadss", "perf-tadss-postgres", "perf-redis", "perf-ai", "perf-fake-llm"]
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


def limits_from_compose(path: Path = COMPOSE) -> dict[str, float]:
    services = yaml.safe_load(path.read_text(encoding="utf-8"))["services"]
    return {s["container_name"]: float(s["cpus"]) for s in services.values() if "cpus" in s}


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


def report(run_dir: Path, query: Query = prometheus_query, limits: dict[str, float] | None = None) -> str:
    limits = limits_from_compose() if limits is None else limits
    start, end = window(run_dir)
    rows = minutes(start, end, query, limits)
    # Phút không có mẫu nào của k6 lẫn cAdvisor = máy đo / VM Docker đứng (không phải ứng dụng chậm) ⇒ lượt INFRA_LIMITED (P6-47)
    gaps = [row["minute"] for row in rows if row["rps"] is None and not row["cpu"]]
    validity = "INFRA_LIMITED" if gaps else "OK"
    (run_dir / "resources.json").write_text(json.dumps({"runId": run_dir.name, "start": start, "end": end, "cpuLimits": limits,
                                                        "gapMinutes": gaps, "validity": validity, "minutes": rows},
                                                       ensure_ascii=False, indent=2), encoding="utf-8")
    shown = [c for c in CONTAINERS if any(c in row["cpu"] for row in rows)] or sorted({n for row in rows for n in row["cpu"]})
    head = f"{'phút':>4} {'req/s':>6} {'VU':>5} {'p95 k6':>6} {'p95 BE':>6} {'lỗi %':>6} {'DB act':>6} " + " ".join(f"{c[5:]:>10}" for c in shown) + " BE MB"
    lines = [f"Tài nguyên theo phút của lượt {run_dir.name} (CPU: lõi, * = ≥ 90 % trần trong compose perf; p95: giây)", head]
    for row in rows:
        cpu = " ".join(f"{row['cpu'].get(c, 0):9.2f}{'*' if c in row['cpu_at_limit'] else ' '}" for c in shown)
        lines.append(f"{row['minute']:4d} {_fmt(row['rps'], 1)} {_fmt(row['vus'], 0, 5)} {_fmt(row['client_p95_s'])} {_fmt(row['server_be_p95_s'])} "
                     f"{_fmt(row['error_pct'])} {_fmt(row['be_db_active'], 0)} {cpu} {row['memory_mb'].get('perf-be', 0):5d}")
    if gaps:
        lines.append(f"CẢNH BÁO INFRA_LIMITED: phút {', '.join(map(str, gaps))} không có số liệu từ mọi nguồn (máy đo / VM Docker đứng); "
                     "lượt này không dùng để kết luận năng lực (P6-47), nạp lên dashboard thì thêm --limited")
    return "\n".join(lines)


def main(argv: list[str]) -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    print(report(Path(argv[1])))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
