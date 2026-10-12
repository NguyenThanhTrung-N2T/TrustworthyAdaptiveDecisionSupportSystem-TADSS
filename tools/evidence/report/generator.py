"""Bộ sinh `report.html` (tự chứa, tiếng Việt) từ một Evidence Pack: đọc file thô trong pack, dựng dữ liệu từng mục rồi render bằng Jinja2. Mục không có dữ liệu ghi rõ lý do."""
from __future__ import annotations

import csv
import json
from datetime import datetime
from pathlib import Path
from typing import Any

from jinja2 import Environment, FileSystemLoader, select_autoescape

from evidence import kpi
from evidence.report.charts import Band, Series, bar_chart, line_chart

TEMPLATE_DIR = Path(__file__).resolve().parent
TIMELINE_BUCKET_SECONDS = 5
LT02_P95_LIMIT_MS = 150.0
LT02_ERROR_LIMIT = 0.01
LIMITATIONS = (
    "Máy đơn: k6, TADSS và cơ sở dữ liệu tranh CPU/RAM nên số liệu là số liệu phòng lab (cấu hình máy ghi ở đầu báo cáo).",
    "Môi trường ASPNETCORE_ENVIRONMENT=Testing (decorator tiêm lỗi chỉ đăng ký ở đây); khác Production ở HSTS, validator khóa API và giới hạn tốc độ.",
    "Rate limit được nâng lên mức tối đa (100000) để tải k6 không bị 429; hành vi giới hạn tốc độ thật được kiểm ở test SEC.",
    "Trace lấy mẫu 10 %: metric và audit không lấy mẫu nên KPI không bị ảnh hưởng, nhưng không phải request nào cũng có trace.",
    "Dữ liệu tải là dữ liệu tổng hợp theo bảng trộn khai báo trong tests/load, không phải dữ liệu nghiệp vụ thật.",
    "Cửa sổ đo theo metric Prometheus lệch tối đa một MetricExportIntervalMs ở hai đầu; số liệu k6 phía client là đối chứng chính xác theo từng request.",
    "Histogram chỉ chính xác tại các biên bucket (150, 500, 700 ms trùng ngưỡng KPI); phân vị khác là ước lượng.",
    "Evaluator mount Docker socket của máy chủ (cần cho Testcontainers): chỉ chạy trên máy của chính người dùng.",
    "Bản nhanh chỉ để diễn tập (tải rút ngắn, không chạy Stryker); số liệu chính thức lấy từ bản đầy đủ.",
    "Nhãn NO_SHOW/CANCELLED của booking SmashCourt đến từ môi trường phát triển (dữ liệu thử, không phải hành vi khách thật) nên số tiền cọc giữ được trên NO_SHOW chỉ mang tính mô tả (methodology Mục 8).",
    "Replay dời mọi mốc thời gian của một booking cùng k × 7 ngày (Q-04) để request nằm trong tương lai: giữ thứ trong tuần, giờ địa phương và lead time danh nghĩa; booking gốc có lead time ≤ 120 phút (cờ window_bias) có thể được duyệt trong khi thực tế không còn kịp, nên báo cáo nêu thêm kết quả hiệu chỉnh offline.",
    "Replay dùng hạng khách tại thời điểm xuất dữ liệu (không có lịch sử hạng) và không có demand (trường tùy chọn, bỏ trống) nên mức cọc theo hạng và khung giờ chỉ mang tính mô tả.",
    "Dataset khách sạn không có giờ đặt, giờ nhận/trả phòng và định danh khách: adapter dùng giờ danh nghĩa 10:00 (đặt), 14:00 (đến), 12:00 (đi) và không ghép được khách giữa các đơn; lịch sử khách lấy từ cột previous_* của từng dòng.",
    "Với miền khách sạn, ngưỡng EUR của HighValueThreshold và MaxItemMinutes của instance PT là tham số vận hành chọn một lần từ phân bố dữ liệu trước lượt chạy, không chỉnh theo kết quả; PT-02 chỉ mô tả, không kiểm định hay kết luận nhân quả.",
)
POLICY_LABELS = (("STATIC_100", "STATIC_100 (cọc 100 % tĩnh)"), ("TADSS_V2", "TADSS_V2 (kết quả runtime)"), ("TADSS_V2_ADJUSTED", "TADSS_V2 (hiệu chỉnh window_bias)"))
STATUS_ORDER = ("APPROVED", "NEED_APPROVAL", "REJECTED", "NO_RESULT")
NO_DATA_REASON = "bước e (dữ liệu thực tế) chưa chạy trong lượt này"


def read_jsonl(path: Path) -> list[dict]:
    if not path.exists():
        return []
    with path.open(encoding="utf-8") as handle:
        return [json.loads(line) for line in handle if line.strip()]


def read_csv_rows(path: Path) -> list[dict[str, str]]:
    if not path.exists():
        return []
    with path.open(encoding="utf-8", newline="") as handle:
        return list(csv.DictReader(handle))


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else None


def _ts(value: str) -> float:
    return datetime.fromisoformat(value.replace("Z", "+00:00")).timestamp()


def main_phase(entries: list[dict]) -> list[dict]:
    """Request ở pha chính (bỏ warm-up), kể cả kịch bản không có warm-up."""
    return [entry for entry in entries if "http_status" in entry and entry.get("phase") != "warmup"]


def latency_stats(entries: list[dict]) -> dict[str, float | int | None]:
    durations = [float(entry["duration_ms"]) for entry in entries]
    if not durations:
        return {"samples": 0, "p50": None, "p95": None, "p99": None, "max": None, "error_rate": None}
    errors = sum(1 for entry in entries if entry["http_status"] != 200)
    return {
        "samples": len(durations), "p50": kpi.percentile(durations, 0.5), "p95": kpi.percentile(durations, 0.95), "p99": kpi.percentile(durations, 0.99),
        "max": max(durations), "error_rate": errors / len(entries),
    }


def timeline(entries: list[dict], bucket: int = TIMELINE_BUCKET_SECONDS) -> tuple[list[tuple[float, float | None]], list[tuple[float, float | None]]]:
    """(p95 theo thời gian, RPS theo thời gian) theo bucket `bucket` giây tính từ request đầu tiên."""
    if not entries:
        return [], []
    start = min(_ts(entry["ts"]) for entry in entries)
    buckets: dict[int, list[float]] = {}
    for entry in entries:
        buckets.setdefault(int((_ts(entry["ts"]) - start) // bucket), []).append(float(entry["duration_ms"]))
    p95 = [(index * bucket + bucket / 2, kpi.percentile(values, 0.95)) for index, values in sorted(buckets.items())]
    rps = [(index * bucket + bucket / 2, len(values) / bucket) for index, values in sorted(buckets.items())]
    return p95, rps


def lt02_breakpoint(entries: list[dict], steps: list[dict]) -> dict[str, Any]:
    """Điểm gãy (methodology 4.6): RPS lớn nhất mà mọi bậc đến đó đều có p95 < 150 ms và lỗi < 1 %."""
    rows, last_good = [], None
    broken = False
    for step in steps:
        start, end = _ts(step["start"]), _ts(step["end"])
        window = [entry for entry in entries if start <= _ts(entry["ts"]) < end]
        stats = latency_stats(window)
        good = stats["samples"] > 0 and stats["p95"] < LT02_P95_LIMIT_MS and stats["error_rate"] < LT02_ERROR_LIMIT
        broken = broken or not good
        if good and not broken:
            last_good = step["rate"]
        rows.append({"rate": step["rate"], "samples": stats["samples"], "p95": stats["p95"], "error_rate": stats["error_rate"], "good": good})
    return {"steps": rows, "last_good_rate": last_good, "broke": broken}


def load_sections(run_dir: Path) -> list[dict[str, Any]]:
    load_dir = run_dir / "raw" / "load"
    windows = {item["name"]: item for item in (read_json(load_dir / "windows.json") or [])}
    sections = []
    for directory in sorted(path for path in load_dir.glob("*") if path.is_dir() and path.name not in ("lt03", "scale2")):
        entries = main_phase(read_jsonl(directory / "requests.jsonl"))
        stats = latency_stats(entries)
        p95_series, rps_series = timeline(entries)
        window = windows.get(directory.name, {})
        section: dict[str, Any] = {
            "name": directory.name, "stats": stats,
            "chart": line_chart([Series("p95 (ms)", p95_series)], "giây từ lúc bắt đầu", "p95 (ms)") if p95_series else "",
            "rps_chart": line_chart([Series("RPS", rps_series)], "giây từ lúc bắt đầu", "request/giây") if rps_series else "",
            "breakpoint": lt02_breakpoint(entries, window["steps"]) if window.get("steps") else None,
        }
        sections.append(section)
    return sections


def _metric_points(path: Path, origin: float) -> list[tuple[float, float | None]]:
    document = read_json(path)
    if not document:
        return []
    totals: dict[float, float | None] = {}
    for series in document.get("result", []):
        for stamp, value in series["values"]:
            number = float(value)
            totals[float(stamp) - origin] = None if number != number else (totals.get(float(stamp) - origin) or 0.0) + number
    return sorted(totals.items())


def incident_section(run_dir: Path) -> dict[str, Any]:
    rows = read_csv_rows(run_dir / "raw" / "chaos" / "chaos_log.csv")
    entries = main_phase(read_jsonl(run_dir / "raw" / "load" / "lt03" / "requests.jsonl"))
    if not rows or not entries:
        return {"rows": rows, "charts": [], "reason": "bước c (LT-03) chưa chạy hoặc không có dữ liệu"}
    origin = min(_ts(entry["ts"]) for entry in entries)
    bands = [Band(row["fi_id"], _ts(row["started_at_utc"]) - origin, _ts(row["ended_at_utc"]) - origin) for row in rows]
    metrics = run_dir / "raw" / "metrics"
    charts = []
    for title, files, unit in (
        ("p99 độ trễ endpoint (ms, từ metric)", [("p99", "lt03-p99-ms.json")], "ms"),
        ("Tỉ lệ fallback và 503 (request/giây)", [("fallback", "lt03-fallback-rate.json"), ("503", "lt03-status-503-rate.json"), ("bypass cache", "lt03-cache-bypass-rate.json")], "req/s"),
        ("Trạng thái breaker (0 đóng, 1 nửa mở, 2 mở, 3 cô lập)", [("strategy", "lt03-circuit-state.json"), ("Redis", "lt03-cache-circuit-state.json")], "trạng thái"),
    ):
        series = [Series(name, _metric_points(metrics / file, origin)) for name, file in files]
        charts.append({"title": title, "svg": line_chart(series, "giây từ lúc k6 bắt đầu", unit, bands)})
    p95_client, _ = timeline(entries)
    charts.append({"title": "p95 phía client (ms, từ k6)", "svg": line_chart([Series("p95 client", p95_client)], "giây từ lúc k6 bắt đầu", "ms", bands)})
    return {"rows": rows, "charts": charts, "reason": ""}


def _rate(value: float | None) -> str:
    return "-" if value is None else f"{value:.3f}"


def _percent(value: float | None) -> str:
    return "-" if value is None else f"{value:.1%}"


def _money(value: float | None) -> str:
    return "-" if value is None else f"{value:,.0f}".replace(",", ".")


def _policy_rows(scope: dict[str, Any]) -> list[dict[str, Any]]:
    """Bảng so sánh RP-02 của một phạm vi: mỗi dòng một chỉ số, ba cột chính sách theo `POLICY_LABELS`."""
    policies = scope["policies"]
    metrics = (
        ("Số booking có quyết định", lambda p: str(p["decided"])),
        ("Số booking có cọc (APPROVED)", lambda p: str(p["approved"])),
        ("Tỷ lệ cọc trung bình (trên booking có cọc)", lambda p: _rate(p["mean_rate"])),
        ("Số tiền cọc trung bình (VND, trên booking có cọc)", lambda p: _money(p["mean_amount"])),
        ("Tỷ lệ NEED_APPROVAL", lambda p: _percent(p["need_approval_rate"])),
        ("Booking nhãn NO_SHOW", lambda p: str(p["no_show"]["bookings"])),
        ("Tổng tiền cọc giữ được trên NO_SHOW (VND)", lambda p: _money(p["no_show"]["deposit_kept"])),
        ("Tổng tiền các booking NO_SHOW (VND)", lambda p: _money(p["no_show"]["total_price"])),
    )
    return [{"label": label, "cells": [fmt(policies[key]) for key, _ in POLICY_LABELS]} for label, fmt in metrics]


def _breakdown_rows(online: dict[str, Any], everything: dict[str, Any], key: str) -> list[dict[str, Any]]:
    """Mức cọc trung bình theo nhóm (`by_tier` hoặc `by_window`) ở hai phạm vi: ONLINE và toàn tập."""
    labels = list(dict.fromkeys([*online[key], *everything[key]]))
    empty = {"bookings": 0, "mean_rate": None, "need_approval": 0}
    rows = []
    for label in labels:
        left, right = online[key].get(label, empty), everything[key].get(label, empty)
        rows.append({"label": label, "online": [left["bookings"], _rate(left["mean_rate"]), left["need_approval"]], "all": [right["bookings"], _rate(right["mean_rate"]), right["need_approval"]]})
    return rows


def _missing_analysis_reason(summary: dict[str, Any] | None, analysis_file: str, code: str) -> str:
    """Lý do thiếu file phân tích: phần đã gửi request mà thiếu file thì nói rõ thiếu file (ghi chú thành công của bước e không phải lý do); còn lại lấy ghi chú của bước e."""
    if summary is not None and int(summary.get("sent") or 0) > 0 and summary.get("status") in ("passed", "failed"):
        return f"bước e đã gửi {summary['sent']} request nhưng thiếu raw/analysis/{analysis_file} (phân tích {code} chưa hoàn tất, xem ghi chú của bước f)"
    return (summary or {}).get("note") or NO_DATA_REASON


def rp_section(run_dir: Path) -> dict[str, Any]:
    """RP-01/RP-02 từ `raw/analysis/rp_summary.json` và `raw/replay/summary.json`; thiếu phân tích thì chỉ trả lý do."""
    summary = read_json(run_dir / "raw" / "replay" / "summary.json")
    analysis = read_json(run_dir / "raw" / "analysis" / "rp_summary.json")
    if analysis is None:
        return {"available": False, "reason": _missing_analysis_reason(summary, "rp_summary.json", "RP-02"), "summary": summary}
    scopes = analysis["scopes"]
    online, everything = scopes["ONLINE"], scopes["ALL"]
    channels = [{"channel": channel, "cells": [counts.get(status, 0) for status in STATUS_ORDER], "total": sum(counts.values())} for channel, counts in everything["status_by_channel"].items()]
    series = [
        Series(label, [(0, online["policies"][key]["mean_rate"]), (1, online["policies"][key]["need_approval_rate"])])
        for key, label in POLICY_LABELS
    ]
    return {
        "available": True, "reason": "", "summary": summary, "bookings": analysis["bookings"], "calendar": analysis["calendar"], "channels": channels,
        "tiers": _breakdown_rows(online, everything, "by_tier"), "windows": _breakdown_rows(online, everything, "by_window"),
        "compare": [{"scope": "ONLINE", "bookings": online["bookings"], "bias": online["window_bias_bookings"], "changes": online["adjusted_changes"], "rows": _policy_rows(online)},
                    {"scope": "Toàn tập", "bookings": everything["bookings"], "bias": everything["window_bias_bookings"], "changes": everything["adjusted_changes"], "rows": _policy_rows(everything)}],
        "policy_labels": [label for _, label in POLICY_LABELS], "status_order": STATUS_ORDER,
        "chart": bar_chart(["Tỷ lệ cọc trung bình", "Tỷ lệ NEED_APPROVAL"], series, "tỷ lệ (0…1)", y_max=1.0),
    }


def _reject_text(rejected: dict[str, int] | None) -> str:
    return ", ".join(f"{reason}: {count}" for reason, count in (rejected or {}).items()) or "không"


def pt_section(run_dir: Path) -> dict[str, Any]:
    """PT-01 (nguồn, đếm, kiểm mã) và PT-02 (hai bảng chéo) từ `raw/portability/*`, `raw/analysis/*`."""
    summary = read_json(run_dir / "raw" / "portability" / "summary.json")
    source = read_json(run_dir / "raw" / "portability" / "source.json")
    code_check = read_json(run_dir / "raw" / "analysis" / "sc7_code_check.json")
    analysis = read_json(run_dir / "raw" / "analysis" / "pt_summary.json")
    reason = (summary or {}).get("note") or NO_DATA_REASON
    section: dict[str, Any] = {"summary": summary, "source": source, "code_check": code_check, "reason": reason, "analysis_reason": _missing_analysis_reason(summary, "pt_summary.json", "PT-02"), "info": None, "analysis": None}
    if summary is not None and "sent" in summary:
        source = source or {}
        status_counts = summary.get("status_counts") or {}
        commits = ", ".join((code_check or {}).get("commits") or [])
        section["info"] = [
            ("Nguồn dữ liệu (URL)", source.get("url", "-")),
            ("SHA-256 ghim", source.get("sha256", "-")),
            ("Giấy phép / trích dẫn", f"{source.get('license', '-')}; {source.get('citation', '-')}"),
            ("Số dòng đọc", f"{summary.get('total_rows', source.get('total_rows', '-'))} (hợp lệ sau lọc: {summary.get('valid_count', '-')})"),
            ("Bị loại trước khi gửi, theo lý do (Q-08)", _reject_text(summary.get("rejected"))),
            ("Chế độ / seed / cỡ mẫu", f"{summary.get('mode', '-')} / {source.get('seed', '-')} / {summary.get('sample_size') or 'toàn bộ dòng hợp lệ'}"),
            ("Request chuẩn bị / gửi", f"{summary.get('prepared', '-')} / {summary.get('sent', '-')} (adapter chặn trước khi gửi: {summary.get('not_sent', 0)})"),
            ("Phản hồi 200", str(status_counts.get("200", 0))),
            ("Lỗi contract (Q-09)", str(summary.get("contract_error_requests", "-"))),
            ("Số lần thử lại (503/504)", str(summary.get("retries", "-"))),
            ("Kiểm mã SC7 (Q-11)", f"{(code_check or {}).get('status', 'chưa kiểm')}: {(code_check or {}).get('reason', '-')}" + (f" — commit đã xét: {commits}" if commits else "")),
        ]
    if analysis is not None:
        section["analysis"] = analysis
        groups = analysis["rate_groups"]
        section["chart"] = bar_chart([item["label"] for item in groups], [Series("tỷ lệ is_canceled", [(index, item["cancel_rate"]) for index, item in enumerate(groups)])], "tỷ lệ hủy (0…1)", y_max=1.0)
        section["status_order"] = STATUS_ORDER
    return section


def dataset_sections(run_dir: Path) -> dict[str, Any]:
    return {"rp": rp_section(run_dir), "pt": pt_section(run_dir)}


def build_data(run_dir: Path, manifest: dict[str, Any], checks: list[dict[str, Any]], kpis: list[dict[str, Any]], coverage: dict[str, Any], groups: list[dict[str, Any]]) -> dict[str, Any]:
    """Gom mọi mục báo cáo; dữ liệu thiếu trả về rỗng/None để template ghi lý do."""
    raw = run_dir / "raw"
    return {
        "manifest": manifest,
        "usable": all(check["passed"] for check in checks),
        "checks": checks,
        "kpis": kpis,
        "groups": groups,
        "coverage": coverage,
        "load": load_sections(run_dir),
        "incident": incident_section(run_dir),
        "scale2": read_json(raw / "scale" / "scale2-checks.json"),
        "contract": read_json(raw / "contract" / "schemathesis-report.json"),
        "mutation": read_json(raw / "mutation" / "mutation-summary.json"),
        "coverage_rates": read_json(raw / "coverage" / "coverage-summary.json"),
        "datasets": dataset_sections(run_dir),
        "limitations": LIMITATIONS,
    }


def render(data: dict[str, Any]) -> str:
    environment = Environment(loader=FileSystemLoader(str(TEMPLATE_DIR)), autoescape=select_autoescape(["html", "j2"]), trim_blocks=True, lstrip_blocks=True)
    environment.filters["ms"] = lambda value: "-" if value is None else f"{value:.1f}"
    environment.filters["pct"] = lambda value: "-" if value is None else f"{value:.2%}"
    environment.filters["rate"] = _rate
    return environment.get_template("template.html.j2").render(**data)
