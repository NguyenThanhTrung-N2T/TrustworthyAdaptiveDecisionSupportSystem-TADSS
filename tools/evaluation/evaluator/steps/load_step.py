"""Bước (b): kịch bản tải k6 LT-01 → LT-02 → LT-05 trên instance 1 trực tiếp (không qua nginx), ghi cửa sổ thời gian thật cho truy vấn ở bước (f)."""
from __future__ import annotations

import json
import time
from datetime import datetime, timedelta
from pathlib import Path
from typing import Callable

from evaluator import d2
from evaluator.clients import k6, prometheus
from evaluator.results import ERROR, FAILED, PASSED, StepResult, utc_now
from evaluator.run_context import RunContext

STEP_NAME = "b"
SCENARIO_ORDER = ("lt01", "lt02", "lt05")
METRIC_SETTLE_SECONDS = 15  # 2 × MetricExportIntervalMs (5 s) + chu kỳ xuất của k6 (5 s)
RUNS_TOTAL_METRIC = "k6_http_reqs_total"
SERVER_COUNT_METRIC = "tadss_decision_duration_milliseconds_count"


def modes_path(repo_dir: Path) -> Path:
    return repo_dir / "tests" / "load" / "config" / "modes.json"


def duration_seconds(value: str) -> int:
    """`30s`/`2m`/`1h` → giây (cùng ký hiệu với k6)."""
    return int(value[:-1]) * {"s": 1, "m": 60, "h": 3600}[value[-1]]


def planned_runs(profile: dict) -> int:
    return int(profile.get("runs", 1))


def run_name(key: str, profile: dict, run_index: int) -> str:
    """Tên thư mục kết quả: `lt01` khi chạy một lần, `lt01-r2` khi lặp nhiều lần."""
    return key if planned_runs(profile) == 1 else f"{key}-r{run_index}"


def step_windows(profile: dict, main_start, hold_key: str = "hold") -> list[dict]:
    """Cửa sổ từng bậc của kịch bản ramping (LT-02): mỗi bậc chuyển 1 giây rồi giữ `hold`; các kịch bản còn lại không có bậc."""
    if profile.get("executor") != "ramping-arrival-rate":
        return []
    hold = duration_seconds(profile[hold_key])
    windows = []
    for index, rate in enumerate(profile["steps"]):
        begin = main_start + timedelta(seconds=index * (hold + 1))
        windows.append({"rate": rate, "start": begin.isoformat(), "end": (begin + timedelta(seconds=hold + 1)).isoformat()})
    return windows


def run(
    context: RunContext,
    run_k6: Callable[..., k6.K6Result] = k6.run_k6,
    query_increase: Callable[..., float] = prometheus.increase_over,
    sleep: Callable[[float], None] = time.sleep,
) -> StepResult:
    """
    Chạy các kịch bản của chế độ hiện tại (nhanh: LT-01, LT-02; đầy đủ: thêm LT-05). `failed` nếu có response 400/429/5xx (payload sai hoặc bị giới hạn) hoặc Prometheus
    không có số liệu `k6_*`/`tadss_*` trong cửa sổ chạy; `error` nếu không chạy được k6.
    """
    started = utc_now()
    notes: list[str] = []
    artifacts: list[str] = []
    windows: list[dict] = []
    failed = False
    profiles = json.loads(modes_path(context.repo_dir).read_text(encoding="utf-8")).get(context.mode, {})
    base_env = dict(context.env)

    def finish(status: str) -> StepResult:
        return StepResult(STEP_NAME, status, started, utc_now(), artifacts, notes)

    for key in (name for name in SCENARIO_ORDER if name in profiles):
        profile = profiles[key]
        for run_index in range(1, planned_runs(profile) + 1):
            name = run_name(key, profile, run_index)
            out_dir = context.raw_dir("load", name)
            request = k6.K6Request(
                script=context.repo_dir / "tests" / "load" / "scenarios" / f"{key}.js",
                scenario=name,
                mode=context.mode,
                base_url=context.endpoints.tadss,
                api_key=context.endpoints.api_key,
                run_id=context.run_id,
                out_dir=out_dir,
                otlp_endpoint=context.env.get("EVAL_OTLP_ENDPOINT", "eval-grafana:4317"),
            )
            result = run_k6(request, base_env)
            artifacts += [str(result.requests_path), str(result.summary_path)]
            if result.returncode != 0 or result.request_count == 0:
                notes.append(f"{name}: k6 không chạy được (mã {result.returncode}, {result.request_count} request ghi nhận)")
                return finish(ERROR)
            warmup_seconds = duration_seconds(profile["warmup"])
            main_start = result.started_at + timedelta(seconds=warmup_seconds)
            windows.append(
                {
                    "scenario": key,
                    "name": name,
                    "run": run_index,
                    "start": result.started_at.isoformat(),
                    "end": result.ended_at.isoformat(),
                    "warmup_seconds": warmup_seconds,
                    "main_start": main_start.isoformat(),
                    "steps": step_windows(profile, main_start),
                }
            )
            problem = _check_responses(name, result.requests_path, notes)
            _note_dropped_iterations(name, result.summary_path, notes)
            failed = failed or problem
            sleep(METRIC_SETTLE_SECONDS)

    windows_path = context.raw_dir("load") / "windows.json"
    windows_path.write_text(json.dumps(windows, ensure_ascii=False, indent=2), encoding="utf-8")
    artifacts.append(str(windows_path))
    if not windows:
        notes.append(f"Chế độ {context.mode} không có kịch bản tải nào trong modes.json")
        return finish(ERROR)

    failed = _check_prometheus(context, windows, query_increase, notes) or failed
    return finish(FAILED if failed else PASSED)


def _check_responses(name: str, requests_path: Path, notes: list[str]) -> bool:
    """Đếm response theo mã ở pha chính; True nếu có 400/429/5xx. Mã 0 (không có phản hồi) và số lượt k6 bỏ được báo cáo nhưng không làm hỏng bước."""
    counts = d2.status_counts(d2.read_entries(requests_path))
    total = sum(counts.values())
    forbidden = d2.forbidden_statuses(counts)
    summary = ", ".join(f"{status}×{count}" for status, count in sorted(counts.items()))
    notes.append(f"{name}: {total} request (pha chính) — {summary}" + (f"; KHÔNG ĐẠT, mã không được phép: {forbidden}" if forbidden else ""))
    if counts.get(0):
        notes.append(f"{name}: {counts[0]} request không có phản hồi (lỗi kết nối hoặc quá hạn)")
    return bool(forbidden)


def _metric_count(metric: dict) -> int:
    """k6 ≥ 1 ghi phẳng `{count, rate}`, bản cũ có lớp `values`."""
    return int(metric.get("count", metric.get("values", {}).get("count", 0)))


def dropped_iterations(summary_path: Path) -> dict[str, int] | None:
    """
    Số lượt k6 phải bỏ vì hết VU, trong summary.json: `{"warmup": n, "main": n}` khi có metric theo scenario (`dropped_iterations{scenario:…}`, do threshold trong options.js sinh ra),
    `{"total": n}` khi chỉ có metric tổng (summary cũ; không có metric nào ⇒ 0); không đọc được ⇒ None.
    """
    try:
        metrics = json.loads(summary_path.read_text(encoding="utf-8"))["metrics"]
    except (OSError, KeyError, json.JSONDecodeError):
        return None
    by_phase = {phase: metrics.get(f"dropped_iterations{{scenario:{phase}}}") for phase in ("warmup", "main")}
    if any(metric is not None for metric in by_phase.values()):
        return {phase: _metric_count(metric) if metric is not None else 0 for phase, metric in by_phase.items()}
    total = metrics.get("dropped_iterations")
    return {"total": _metric_count(total) if total is not None else 0}


def _note_dropped_iterations(name: str, summary_path: Path, notes: list[str]) -> None:
    dropped = dropped_iterations(summary_path)
    if not dropped:
        return
    if "total" in dropped:
        if dropped["total"]:
            notes.append(f"{name}: k6 bỏ {dropped['total']} lượt (dropped_iterations; thường ở pha khởi động, xem summary.json)")
    elif dropped["main"]:
        notes.append(f"{name}: k6 bỏ {dropped['main']} lượt ở pha chính — tải thực tế thấp hơn danh nghĩa (xem summary.json)")
    elif dropped["warmup"]:
        notes.append(f"{name}: k6 bỏ {dropped['warmup']} lượt ở warm-up, 0 ở pha chính (pha chính đủ tải)")


def _check_prometheus(context: RunContext, windows: list[dict], query_increase, notes: list[str]) -> bool:
    """Prometheus phải có `k6_*` và `tadss_decision_duration_*` trong cửa sổ toàn bước (chứng minh metric tới được Grafana)."""
    start = datetime.fromisoformat(windows[0]["start"])
    end = datetime.fromisoformat(windows[-1]["end"]) + timedelta(seconds=METRIC_SETTLE_SECONDS)
    problems = False
    for metric in (RUNS_TOTAL_METRIC, SERVER_COUNT_METRIC):
        try:
            increase = query_increase(context.endpoints.prometheus, metric, start, end)
        except prometheus.PrometheusError as error:
            notes.append(f"Prometheus {metric}: không truy vấn được ({error})")
            problems = True
            continue
        notes.append(f"Prometheus {metric}: tăng {increase:.0f} trong cửa sổ chạy" + ("" if increase > 0 else " — KHÔNG CÓ SỐ LIỆU"))
        problems = problems or increase <= 0
    return problems
