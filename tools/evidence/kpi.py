"""Kết luận KPI SC1…SC8 (EXPERIMENTAL_DESIGN Mục 5; quy tắc cụ thể ở plan 5.5 Task 11). Thuần hàm: nhận dữ liệu đã đọc, trả `Kpi` kèm lý do và tệp nguồn."""
from __future__ import annotations

import math
import random
import statistics
from dataclasses import asdict, dataclass, field
from typing import Any

PASS = "PASS"
FAIL = "FAIL"
NOT_APPLICABLE = "NOT_APPLICABLE"
INSUFFICIENT_DATA = "INSUFFICIENT_DATA"

SC1_SERVER_SHARE = 0.95
SC1_CLIENT_P95_MS = 150.0
MT_THRESHOLD = 70.0
BOOTSTRAP_RESAMPLES = 2000
BOOTSTRAP_SEED = 20261008

SC2_CATALOG = ("FI-02", "FI-03", "FI-04")
SC3_CATALOG = ("IT-05",)
SC4_CATALOG = ("IT-04", "IT-07", "SCALE-1")
SC7_MIN_RECORDS = 10000


@dataclass(frozen=True)
class Kpi:
    """Một kết luận KPI: `code`, `verdict` (PASS/FAIL/NOT_APPLICABLE/INSUFFICIENT_DATA), giá trị đọc được, lý do và tệp thô làm căn cứ."""

    code: str
    verdict: str
    value: str
    reason: str
    sources: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


def percentile(values: list[float], fraction: float) -> float:
    """Phân vị theo thứ hạng gần nhất (nearest-rank): chính xác theo từng request, không nội suy."""
    ordered = sorted(values)
    rank = max(1, math.ceil(round(fraction * len(ordered), 9)))
    return ordered[min(rank, len(ordered)) - 1]


def bootstrap_interval(values: list[float], confidence: float = 0.95) -> tuple[float, float] | None:
    """Khoảng tin cậy bootstrap của trung bình trên các lần lặp; dưới hai lần lặp thì không tính được."""
    if len(values) < 2:
        return None
    generator = random.Random(BOOTSTRAP_SEED)
    means = sorted(statistics.fmean(generator.choices(values, k=len(values))) for _ in range(BOOTSTRAP_RESAMPLES))
    low = means[int(((1 - confidence) / 2) * BOOTSTRAP_RESAMPLES)]
    high = means[min(BOOTSTRAP_RESAMPLES - 1, int((1 - (1 - confidence) / 2) * BOOTSTRAP_RESAMPLES))]
    return low, high


def sc1(runs: list[dict[str, Any]]) -> Kpi:
    """
    `runs`: mỗi lần lặp LT-01 `{name, server_share_le_150 (None nếu không truy vấn được), client_p95_ms, samples}`.
    Đạt khi MỌI lần lặp có tỉ lệ ≤ 150 ms phía server ≥ 95 % và p95 phía client < 150 ms. Bucket đếm "≤ 150" còn KPI viết "< 150": phép kiểm "< 150" chính xác là p95 phía client của k6.
    """
    sources = ["raw/load/lt01*/requests.jsonl", "raw/metrics/lt01-*.json"]
    if not runs:
        return Kpi("SC1", INSUFFICIENT_DATA, "-", "không có lần chạy LT-01 (bước b chưa chạy hoặc lỗi)", sources)
    if any(run["server_share_le_150"] is None for run in runs):
        return Kpi("SC1", INSUFFICIENT_DATA, "-", "không truy vấn được tỉ lệ ≤ 150 ms từ Prometheus cho mọi lần lặp", sources)
    shares = [run["server_share_le_150"] for run in runs]
    p95s = [run["client_p95_ms"] for run in runs]
    interval = bootstrap_interval(p95s)
    detail = "; ".join(f"{run['name']}: server ≤150 ms {run['server_share_le_150']:.2%}, p95 client {run['client_p95_ms']:.1f} ms ({run['samples']} mẫu)" for run in runs)
    detail += f"; p95 client trung bình {statistics.fmean(p95s):.1f} ms" + (f", KTC 95 % bootstrap [{interval[0]:.1f}; {interval[1]:.1f}]" if interval else " (một lần lặp: không có khoảng tin cậy)")
    ok = all(share >= SC1_SERVER_SHARE for share in shares) and all(p95 < SC1_CLIENT_P95_MS for p95 in p95s)
    return Kpi("SC1", PASS if ok else FAIL, f"min server {min(shares):.2%}; max p95 client {max(p95s):.1f} ms", detail, sources)


def _catalog_state(summary: dict[str, dict[str, int]], codes: tuple[str, ...]) -> tuple[bool, list[str]]:
    """(tất cả mã có test và không ca đỏ, mô tả vấn đề)."""
    problems = []
    for code in codes:
        counts = summary.get(code)
        if not counts or counts["passed"] + counts["failed"] == 0:
            problems.append(f"{code}: chưa có kết quả")
        elif counts["failed"]:
            problems.append(f"{code}: {counts['failed']} ca đỏ")
    return not problems, problems


def sc2(chaos_rows: list[dict[str, str]], summary: dict[str, dict[str, int]] | None) -> Kpi:
    """FI-02/FI-03 trong LT-03 (chaos_log) và FI-02/03/04 trong test bước a đều đạt."""
    sources = ["raw/chaos/chaos_log.csv", "raw/tests/catalog-results.csv"]
    rows = {row["fi_id"]: row for row in chaos_rows}
    if summary is None or not all(code in rows for code in ("FI-02", "FI-03")):
        return Kpi("SC2", INSUFFICIENT_DATA, "-", "thiếu kết quả bước a (test FI-02/03/04) hoặc bước c (LT-03)", sources)
    catalog_ok, problems = _catalog_state(summary, SC2_CATALOG)
    chaos_ok = all(rows[code]["passed"] == "True" for code in ("FI-02", "FI-03"))
    detail = "; ".join([f"LT-03 {code}: {'đạt' if rows[code]['passed'] == 'True' else 'KHÔNG ĐẠT'}" for code in ("FI-02", "FI-03")] + (problems or ["test FI-02/03/04: đạt"]))
    return Kpi("SC2", PASS if catalog_ok and chaos_ok else FAIL, "fallback APPROVED cọc 100 %, pipeline ≤ 500 ms, ≥ 99 % ≤ 700 ms", detail, sources)


def sc3(ev3_passed: bool | None, ev3_detail: str, summary: dict[str, dict[str, int]] | None) -> Kpi:
    """Số response 200 = số bản PRIMARY (EV-3) và test bất biến audit (IT-05) đạt."""
    sources = ["raw/d1/decision_audit.csv", "raw/load/*/requests.jsonl", "raw/tests/catalog-results.csv"]
    if ev3_passed is None or summary is None:
        return Kpi("SC3", INSUFFICIENT_DATA, "-", "thiếu D1/D2 hoặc kết quả test", sources)
    catalog_ok, problems = _catalog_state(summary, SC3_CATALOG)
    detail = f"EV-3: {ev3_detail}; " + ("; ".join(problems) if problems else "IT-05 đạt")
    return Kpi("SC3", PASS if ev3_passed and catalog_ok else FAIL, "0 thiếu, 0 thừa", detail, sources)


def sc4(summary: dict[str, dict[str, int]] | None, scale2_passed: bool | None, chaos_rows: list[dict[str, str]]) -> Kpi:
    """IT-04, IT-07, SCALE-1 đạt; SCALE-2 đạt; 0 vi phạm bất biến trong LT-03."""
    sources = ["raw/tests/catalog-results.csv", "raw/scale/scale2-checks.json", "raw/chaos/chaos_log.csv"]
    if summary is None or scale2_passed is None:
        return Kpi("SC4", INSUFFICIENT_DATA, "-", "thiếu kết quả bước a (IT-04/07, SCALE-1) hoặc SCALE-2", sources)
    catalog_ok, problems = _catalog_state(summary, SC4_CATALOG)
    violations = sum(int(row["invariant_violations"]) for row in chaos_rows)
    detail = "; ".join(problems or ["IT-04, IT-07, SCALE-1 đạt"]) + f"; SCALE-2 {'đạt' if scale2_passed else 'KHÔNG ĐẠT'}; vi phạm bất biến trong LT-03: {violations}"
    # Giá trị nêu luôn lý do không đạt (ca đỏ, SCALE-2) để dòng tóm tắt không chỉ ghi "0 vi phạm" trong khi KPI FAIL
    failures = problems + ([] if scale2_passed else ["SCALE-2 KHÔNG ĐẠT"])
    value = "; ".join([*failures, f"{violations} vi phạm"])
    return Kpi("SC4", PASS if catalog_ok and scale2_passed and violations == 0 else FAIL, value, detail, sources)


def sc8(summary: dict[str, dict[str, int]] | None, mutation: dict[str, dict[str, Any]] | None) -> Kpi:
    """100 % ca test đạt (bước a) và mọi MT ≥ 70 %. Không chạy MT (bản nhanh) ⇒ INSUFFICIENT_DATA."""
    sources = ["raw/tests/catalog-results.csv", "raw/mutation/mutation-summary.json"]
    if summary is None:
        return Kpi("SC8", INSUFFICIENT_DATA, "-", "bước a chưa chạy", sources)
    failed = sum(counts["failed"] for counts in summary.values())
    total = sum(counts["passed"] + counts["failed"] for counts in summary.values())
    if mutation is None:
        return Kpi("SC8", INSUFFICIENT_DATA, f"{total - failed}/{total} ca đạt", "Stryker không chạy ở bản nhanh nên chưa đánh giá được mutation score", sources)
    low = {code: result["score"] for code, result in mutation.items() if result["score"] is None or result["score"] < MT_THRESHOLD}
    detail = f"{total - failed}/{total} ca đạt; " + ", ".join(f"{code} {result['score']:.1f} %" if result["score"] is not None else f"{code} không có điểm" for code, result in mutation.items())
    return Kpi("SC8", PASS if failed == 0 and not low and total > 0 else FAIL, detail, detail, sources)


def sc7(pt_summary: dict[str, Any] | None, code_check: dict[str, Any] | None) -> Kpi:
    """
    PT-01 (adapter khách sạn, không đổi mã runtime/SDK). `pt_summary`: `raw/portability/summary.json` của bước (e) (None nếu PT chưa chạy);
    `code_check`: `{status: PASS|FAIL|INSUFFICIENT_DATA, reason, commits}` từ kiểm mã Q-11 của evaluator (None nếu chưa kiểm).
    FAIL khi kiểm mã đỏ hoặc có lỗi contract (kể cả request adapter dựng sai schema bị chặn trước khi gửi, Q-09); PASS khi ≥ 10.000 bản ghi gửi, 0 lỗi contract và kiểm mã đạt;
    còn lại (PT bị bỏ hoặc không gửi được, dưới 10.000, adapter chưa commit) là INSUFFICIENT_DATA.
    """
    sources = ["raw/portability/summary.json", "raw/d5/portability_hotel.csv", "raw/analysis/sc7_code_check.json"]
    code_status = (code_check or {}).get("status")
    code_text = (code_check or {}).get("reason") or "chưa kiểm mã"
    commits = ", ".join((code_check or {}).get("commits") or [])
    code_detail = f"kiểm mã: {code_text}" + (f" (commit đã xét: {commits})" if commits else "")
    if code_status == FAIL:
        return Kpi("SC7", FAIL, "kiểm mã không đạt", code_detail, sources)
    if pt_summary is None or pt_summary.get("status") in ("skipped", "error") or "sent" not in pt_summary:
        why = (pt_summary or {}).get("note") or "bước e chưa chạy phần PT"
        return Kpi("SC7", INSUFFICIENT_DATA, "-", f"PT-01 chưa có dữ liệu gửi: {why}; {code_detail}", sources)
    sent = int(pt_summary["sent"])
    errors = int(pt_summary.get("contract_error_requests") or 0)
    retries = int(pt_summary.get("retries") or 0)
    value = f"{sent} bản ghi gửi, {errors} lỗi contract, {retries} lần thử lại"
    if errors:
        return Kpi("SC7", FAIL, value, f"{errors} bản ghi lỗi contract (Q-09); {code_detail}", sources)
    if sent < SC7_MIN_RECORDS:
        return Kpi("SC7", INSUFFICIENT_DATA, value, f"mới gửi {sent} bản ghi, cần ≥ {SC7_MIN_RECORDS} (10.000); {code_detail}", sources)
    if code_status != PASS:
        return Kpi("SC7", INSUFFICIENT_DATA, value, f"chưa kết luận được phần mã: {code_detail}", sources)
    return Kpi("SC7", PASS, value, f"≥ {SC7_MIN_RECORDS} bản ghi, 0 lỗi contract; {code_detail}", sources)


def out_of_scope() -> list[Kpi]:
    """SC5, SC6: ngoài phạm vi lượt chạy của TADSS đơn lẻ (SC7 do `sc7` kết luận từ gói 5.6)."""
    return [
        Kpi("SC5", NOT_APPLICABLE, "-", "FI-13 thuộc SmashCourt-BE (Phase 6)"),
        Kpi("SC6", NOT_APPLICABLE, "-", "bộ truy vấn bất biến booking thuộc SmashCourt-BE (Phase 6)"),
    ]
