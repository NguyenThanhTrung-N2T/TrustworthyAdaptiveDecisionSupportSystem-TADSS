"""Chấm một lượt k6 theo SLO đã chốt (Epic 6C, task 2.18; plan 6C Phụ lục B.7.2–B.7.5) từ MẪU THÔ của k6 (`raw.csv.gz`).

Không chấm bằng histogram Prometheus: biên bucket của k6 (…250, 500, 750, 1000… ms) kẹp ngưỡng 300 ms / 800 ms nên phân vị chỉ là nội suy.
Mọi con số (ngưỡng, số mẫu tối thiểu, cửa sổ, bậc tải) đọc từ `perf/k6/slo.json`, cùng file k6 dùng dựng threshold.
Trạng thái: PASS / FAIL / INSUFFICIENT (không đủ mẫu, không bao giờ coi là đạt); spike thêm NOT_RECOVERED.
Hàm thuần (nhận dữ liệu đã đọc) để test được; `analyze.py` đọc file, Prometheus và ghi `verdict.json`.
"""
from __future__ import annotations

import csv
import gzip
import json
import math
import re
from dataclasses import dataclass, field
from pathlib import Path

SLO_FILE = Path(__file__).resolve().parents[1] / "k6" / "slo.json"
PASS, FAIL, INSUFFICIENT, NOT_RECOVERED = "PASS", "FAIL", "INSUFFICIENT", "NOT_RECOVERED"
LABELS = {PASS: "ĐẠT", FAIL: "KHÔNG ĐẠT", INSUFFICIENT: "KHÔNG ĐỦ DỮ LIỆU", NOT_RECOVERED: "KHÔNG HỒI PHỤC"}
_DURATION = re.compile(r"(\d+)([hms])")
_UNIT = {"h": 3600, "m": 60, "s": 1}


@dataclass(frozen=True)
class Sample:
    t: float
    slo: str
    name: str
    phase: str
    value: float


@dataclass
class Raw:
    durations: list[Sample] = field(default_factory=list)   # http_req_duration (ms)
    errors: list[Sample] = field(default_factory=list)      # request_errors (1 = lỗi, 0 = không)
    dropped: list[float] = field(default_factory=list)      # thời điểm từng dropped_iterations

    def steady(self) -> "Raw":
        return Raw([s for s in self.durations if s.phase == "steady"], [s for s in self.errors if s.phase == "steady"], self.dropped)


def load_slo(path: Path = SLO_FILE) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def seconds(text: str) -> int:
    parts = _DURATION.findall(text)
    if not parts or "".join(n + u for n, u in parts) != text:
        raise ValueError(f"thời lượng không hợp lệ: {text}")
    return sum(int(n) * _UNIT[u] for n, u in parts)


def percentile(values: list[float], p: float) -> float | None:
    """Phân vị nội suy tuyến tính giữa hai hạng (cùng cách k6 tính p(95), p(99) trong summary)."""
    if not values:
        return None
    ordered = sorted(values)
    position = p * (len(ordered) - 1)
    low = math.floor(position)
    high = min(low + 1, len(ordered) - 1)
    return ordered[low] + (ordered[high] - ordered[low]) * (position - low)


def _tags(text: str) -> dict[str, str]:
    return dict(part.split("=", 1) for part in text.split("&") if "=" in part) if text else {}


def read_raw(path: Path) -> Raw:
    """Đọc `--out csv=…gz` của k6: chỉ giữ thời gian request, cờ lỗi theo request và lượt bị bỏ (file lớn: đọc từng dòng)."""
    raw = Raw()
    with gzip.open(path, "rt", encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            metric = row["metric_name"]
            if metric not in ("http_req_duration", "request_errors", "dropped_iterations"):
                continue
            t = float(row["timestamp"])
            if metric == "dropped_iterations":
                raw.dropped.append(t)
                continue
            tags = _tags(row.get("extra_tags", ""))
            sample = Sample(t, tags.get("slo", row.get("slo", "")), row.get("name", ""), tags.get("phase", row.get("phase", "")),
                            float(row["metric_value"]))
            (raw.durations if metric == "http_req_duration" else raw.errors).append(sample)
    return raw


def _limits(slo: dict, selftest: bool) -> dict:
    groups = {name: dict(limit) for name, limit in slo["groups"].items()}
    if selftest:
        groups["read"]["p95"] = 1   # tự kiểm (B.7.2): lượt PHẢI ra KHÔNG ĐẠT
    return groups


def judge(values: list[float], limit: dict, minimum: dict) -> dict:
    result: dict = {"n": len(values)}
    for key in ("p95", "p99"):
        if key not in limit:
            continue
        value = percentile(values, int(key[1:]) / 100)
        result[key] = None if value is None else round(value, 1)
        result[f"{key}Limit"] = limit[key]
        result[f"{key}Status"] = INSUFFICIENT if len(values) < minimum[key] else (PASS if value < limit[key] else FAIL)
    return result


def _combine(statuses: list[str]) -> str:
    return FAIL if FAIL in statuses else INSUFFICIENT if INSUFFICIENT in statuses or not statuses else PASS


def _error_result(errors: list[Sample], slo: dict) -> dict:
    n = len(errors)
    rate = sum(s.value for s in errors) / n if n else None
    return {"n": n, "rate": None if rate is None else round(rate, 6), "limit": slo["errorRateMax"],
            "status": INSUFFICIENT if not n else (PASS if rate < slo["errorRateMax"] else FAIL)}


def slo_verdict(raw: Raw, slo: dict, selftest: bool = False) -> dict:
    """SLO trên toàn pha steady (load, soak, closed): từng nhóm p95/p99 + số mẫu, lỗi chung và theo nhóm, p95 từng endpoint."""
    steady = raw.steady()
    limits = _limits(slo, selftest)
    groups = {}
    for name, limit in limits.items():
        values = [s.value for s in steady.durations if s.slo == name]
        if values:
            groups[name] = judge(values, limit, slo["minSamples"])
            groups[name]["errors"] = _error_result([s for s in steady.errors if s.slo == name], slo)
    endpoints = {}
    for endpoint in sorted({s.name for s in steady.durations}):
        samples = [s for s in steady.durations if s.name == endpoint]
        endpoints[endpoint] = {**judge([s.value for s in samples], {"p95": limits.get(samples[0].slo, {}).get("p95", math.inf)},
                                       slo["minSamples"]), "slo": samples[0].slo}
    errors = _error_result(steady.errors, slo)
    statuses = [g[k] for g in groups.values() for k in ("p95Status", "p99Status")] + [errors["status"]]
    return {"groups": groups, "errors": errors, "endpoints": endpoints, "status": _combine(statuses), "selftest": selftest}


def pool(raws: list[Raw], slo: dict) -> dict:
    """p95/p99 trên mẫu thô GHÉP của nhiều lượt cùng cấu hình (B.7.2), không lấy trung bình các phân vị."""
    return slo_verdict(Raw([s for r in raws for s in r.durations], [s for r in raws for s in r.errors], []), slo)


def window_stats(raw: Raw, start: float, end: float, rate: float, slo: dict, groups: list[str] | None = None) -> dict:
    """Một cửa sổ [start, end): p95 từng nhóm (vi phạm chỉ xét nhóm đủ mẫu), lỗi, tỷ lệ lượt bị bỏ so với lượt dự kiến."""
    groups = groups or slo["breakpoint"]["groups"]
    durations = [s for s in raw.durations if start <= s.t < end and s.phase == "steady"]
    errors = [s for s in raw.errors if start <= s.t < end and s.phase == "steady"]
    dropped = sum(1 for t in raw.dropped if start <= t < end)
    expected = rate * (end - start)
    stats: dict = {"start": start, "end": end, "rate": rate, "groups": {}, "violations": []}
    for name in sorted({s.slo for s in durations}):
        values = [s.value for s in durations if s.slo == name]
        p95 = percentile(values, 0.95)
        stats["groups"][name] = {"n": len(values), "p95": None if p95 is None else round(p95, 1)}
        limit = slo["groups"].get(name, {}).get("p95")
        if name in groups and len(values) >= slo["minSamples"]["p95"] and limit is not None and p95 >= limit:
            stats["violations"].append(f"p95 {name}")
    error = _error_result(errors, slo)
    stats["errors"] = {"n": error["n"], "rate": error["rate"]}
    if error["n"] and error["rate"] > slo["errorRateMax"]:
        stats["violations"].append("errors")
    stats["dropped"] = {"n": dropped, "share": round(dropped / expected, 6) if expected else None}
    if expected and dropped / expected > slo["breakpoint"]["droppedMax"]:
        stats["violations"].append("dropped")
    return stats


def step_verdict(raw: Raw, steady_start: float, steps: list[dict], slo: dict) -> dict:
    """Bậc tải (SP-05, SP-02): mỗi bậc chuyển `transition` s + giữ `hold` s; bỏ `discard` đầu phần giữ, xét các cửa sổ `window` trọn vẹn."""
    discard, window = seconds(slo["breakpoint"]["discard"]), seconds(slo["breakpoint"]["window"])
    result, offset, break_step = [], steady_start, None
    for index, step in enumerate(steps):
        hold_start = offset + step["transition"]
        windows = []
        start = hold_start + discard
        while start + window <= hold_start + step["hold"]:
            windows.append(window_stats(raw, start, start + window, step["rate"], slo))
            start += window
        violations = sorted({v for w in windows for v in w["violations"]})
        result.append({"index": index, "rate": step["rate"], "holdStart": hold_start, "holdEnd": hold_start + step["hold"],
                       "windows": windows, "violations": violations})
        if violations and break_step is None:
            break_step = index
        offset = hold_start + step["hold"]
    return {"steps": result, "breakStep": break_step, "breakRate": None if break_step is None else steps[break_step]["rate"]}


def spike_recovery(raw: Raw, recovery_start: float, end: float, slo: dict) -> dict:
    """Từ lúc tải về danh nghĩa: thời gian tới đầu chuỗi `consecutive` cửa sổ `window` liên tiếp đạt p95 nhóm `group` + lỗi < ngưỡng."""
    cfg = slo["spike"]
    window, need, group = seconds(cfg["window"]), cfg["consecutive"], cfg["group"]
    limit, minimum = slo["groups"][group]["p95"], slo["minSamples"]["p95"]
    windows, run_start, streak, start = [], None, 0, recovery_start
    while start + window <= end:
        values = [s.value for s in raw.durations if start <= s.t < start + window and s.slo == group and s.phase == "steady"]
        errors = _error_result([s for s in raw.errors if start <= s.t < start + window and s.phase == "steady"], slo)
        p95 = percentile(values, 0.95)
        good = len(values) >= minimum and p95 is not None and p95 < limit and errors["status"] == PASS
        windows.append({"offset": start - recovery_start, "n": len(values), "p95": None if p95 is None else round(p95, 1),
                        "errorRate": errors["rate"], "good": good})
        streak = streak + 1 if good else 0
        if streak == 1:
            run_start = start
        if streak == need:
            took = int(run_start - recovery_start)
            return {"seconds": took, "status": PASS if took <= cfg["passWithinSeconds"] else FAIL, "windows": windows}
        start += window
    return {"seconds": None, "status": NOT_RECOVERED, "windows": windows}


def episodes(raw: Raw, slo: dict) -> dict:
    """Đợt nghẽn (chốt 03/11, plan 6C 2C): cửa sổ `window` có p95 nhóm `group` vượt SLO (cửa sổ đủ mẫu p95); cửa sổ xấu bắt đầu cách
    cửa sổ xấu trước ≤ `mergeGap` thuộc cùng một đợt. Chỉ pha steady. Dùng để so lượt đối chứng / thí nghiệm (E1–E3) và SP-01."""
    cfg = slo["episodes"]
    window, gap, group = seconds(cfg["window"]), seconds(cfg["mergeGap"]), cfg["group"]
    limit, minimum = slo["groups"][group]["p95"], slo["minSamples"]["p95"]
    buckets: dict[float, list[float]] = {}
    for s in raw.durations:
        if s.phase == "steady" and s.slo == group:
            buckets.setdefault(s.t // window * window, []).append(s.value)
    insufficient = sum(1 for values in buckets.values() if len(values) < minimum)
    result: list[dict] = []
    for start in sorted(buckets):
        values = buckets[start]
        if len(values) < minimum:
            continue
        p95 = percentile(values, 0.95)
        if p95 <= limit:
            continue
        if result and start - result[-1]["lastStart"] <= gap:
            current = result[-1]
            current.update(end=start + window, lastStart=start, windows=current["windows"] + 1, maxP95=round(max(current["maxP95"], p95), 1))
        else:
            result.append({"start": start, "end": start + window, "lastStart": start, "windows": 1, "maxP95": round(p95, 1)})
    return {"count": len(result), "episodes": result, "windowsChecked": len(buckets) - insufficient, "windowsInsufficient": insufficient,
            "definition": cfg}


def _growth(pair: tuple[float, float] | None, limit: float) -> dict:
    if not pair or pair[0] is None or pair[1] is None or pair[0] <= 0:
        return {"status": INSUFFICIENT}
    growth = pair[1] / pair[0] - 1
    return {"baseline": pair[0], "tail": pair[1], "growth": round(growth, 4), "limit": limit, "status": PASS if growth <= limit else FAIL}


def soak_verdict(raw: Raw, steady_start: float, steady_end: float, slo: dict,
                 memory: tuple[float, float] | None, connections: tuple[float, float] | None) -> dict:
    """SP-04: SLO toàn pha; RAM BE và kết nối DB trung bình phút 15–25 so với 10 phút cuối (≤ +10 %); p95 Đọc mỗi cửa sổ 10 phút ≤ 1,2 × cửa sổ đầu."""
    cfg = slo["soak"]
    window = seconds(cfg["p95Window"])
    drift, start = [], steady_start
    while start + window <= steady_end:
        values = [s.value for s in raw.durations if start <= s.t < start + window and s.slo == "read" and s.phase == "steady"]
        drift.append({"offset": start - steady_start, "n": len(values), "p95": percentile(values, 0.95)})
        start += window
    usable = [w for w in drift if w["n"] >= slo["minSamples"]["p95"]]
    if len(usable) < 2 or len(usable) < len(drift):
        drift_status = INSUFFICIENT
        worst = None
    else:
        worst = max(w["p95"] for w in usable[1:]) / usable[0]["p95"]
        drift_status = PASS if worst <= cfg["p95GrowthFactorMax"] else FAIL
    result = {
        "slo": slo_verdict(raw, slo),
        "memory": _growth(memory, cfg["growthMax"]),
        "connections": _growth(connections, cfg["growthMax"]),
        "p95Drift": {"windows": drift, "worstFactor": None if worst is None else round(worst, 3), "limit": cfg["p95GrowthFactorMax"],
                     "status": drift_status},
    }
    result["status"] = _combine([result["slo"]["status"], result["memory"]["status"], result["connections"]["status"], drift_status])
    return result
