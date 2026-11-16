"""Phép thử cho Mục 2.4 của đề cương khóa luận: phần sàn của tổn thất theo chính sách hoàn tiền, và hai dạng ràng buộc của bài toán cọc.

1. Phần sàn L_min. Chính sách hủy của SmashCourt (`tools/evidence/config/smashcourt_cancel_policies.json`): hủy trước ít nhất 24 giờ
   hoàn 100 %, trước 2 tới 24 giờ hoàn 50 %, trong 2 giờ cuối không hoàn; không đến thì không hoàn. Với cọc d, tổn thất của một đơn theo
   loại kết cục k là (1 − r_k)·(1 − κ_k·d), trong đó κ_k là phần cọc doanh nghiệp giữ và r_k là khả năng bán lại khung giờ.
   Khi cọc 100 %, phần còn lại (1 − r_k)·(1 − κ_k) là phần sàn: tiền cọc không làm nó nhỏ đi được.
   Dữ liệu khách sạn tính theo ngày còn chính sách tính theo giờ, nên thử nhiều cách quy đổi "2 giờ" và "24 giờ" sang ngày, và nhiều giả định
   về khả năng bán lại; báo cả dải, theo cả giai đoạn và theo tháng.
2. Hai dạng ràng buộc, ở cùng phần hở trung bình: (A) ràng buộc trung bình, lời giải tối ưu là một ngưỡng (đơn rủi ro nhất cọc 100 %, còn lại 0 %);
   (B) ràng buộc từng đơn, lời giải d = max(0, 1 − ε/q). Dùng mô hình và nhãn chặt 7 ngày của `deposit_pretest.py` (q = khả năng bỏ).
Chạy: `python -I tools/research/floor_pretest.py [--check]`.
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path
from typing import Sequence

sys.path.insert(0, str(Path(__file__).resolve().parent))
import deposit_pretest as dp  # noqa: E402

DEFAULT_RESULTS = Path(__file__).resolve().parent / "results" / "floor_pretest.json"

# Quy đổi mốc giờ của chính sách sang ngày trước ngày đến: (mốc "2 giờ", mốc "24 giờ"); hủy trong ≤ mốc thứ nhất coi như trong 2 giờ cuối
MAPPINGS = {"0d_1d": (0, 1), "1d_3d": (1, 3), "1d_7d": (1, 7), "3d_14d": (3, 14)}
# Khả năng bán lại khung giờ theo loại kết cục (giả định): không đến và hủy trong 2 giờ cuối thì không bán lại được
RESALE = {
    "late_unsold_early_sold": {"mid": 0.0, "early": 1.0},
    "late_half_early_sold": {"mid": 0.5, "early": 1.0},
    "late_unsold_early_90": {"mid": 0.0, "early": 0.9},
}
KEEP = {"no_show": 1.0, "late": 1.0, "mid": 0.5, "early": 0.0}   # κ: phần cọc doanh nghiệp giữ, theo chính sách SmashCourt


def outcome(row: dict, mapping: tuple[int, int]) -> str | None:
    """Loại kết cục theo chính sách: no_show, late (trong 2 giờ cuối), mid (2–24 giờ), early (≥ 24 giờ), hoặc None nếu khách đến."""
    if row["status"] == "No-Show":
        return "no_show"
    if row["status"] != "Canceled":
        return None
    late, mid = mapping
    if row["days_before"] <= late:
        return "late"
    if row["days_before"] <= mid:
        return "mid"
    return "early"


def floor_share(rows: Sequence[dict], mapping: tuple[int, int], resale: dict) -> dict:
    """Theo giá trị đơn: phần sàn L_min, tỷ lệ từng loại kết cục, và khả năng bỏ có hiệu lực q = Σ (1 − r)·κ (phần cọc làm giảm được)."""
    total = sum(r["value"] for r in rows)
    share: dict[str, float] = defaultdict(float)
    floor = effective = 0.0
    for r in rows:
        k = outcome(r, mapping)
        if k is None:
            continue
        share[k] += r["value"] / total
        r_k = resale.get(k, 0.0)
        floor += r["value"] * (1 - r_k) * (1 - KEEP[k]) / total
        effective += r["value"] * (1 - r_k) * KEEP[k] / total
    return {"floor": floor, "effective_abandon": effective, "outcome_share": dict(sorted(share.items()))}


def threshold_policy(scores: Sequence[float], test: Sequence[dict], ys: Sequence[int], target: float) -> dict:
    """Dạng (A): cọc 100 % cho các đơn có điểm rủi ro cao nhất cho tới khi phần hở trung bình xuống tới mục tiêu, còn lại 0 %.
    Chọn ngưỡng theo điểm (không nhìn nhãn) bằng chia đôi trên phần hở đo được, như cách đọc "cùng mức hở" của `deposit_pretest.py`."""
    order = sorted(scores)
    lo, hi = 0, len(order)
    best = None
    while lo < hi:
        mid = (lo + hi) // 2
        cut = order[mid]
        deps = [1.0 if s >= cut else 0.0 for s in scores]
        result = dp.evaluate(deps, test, ys)
        if result["exposed"] <= target:
            best, lo = result, mid + 1
        else:
            hi = mid
    return best


def run(rows: list[dict]) -> dict:
    train, test = dp.split(rows, "primary")
    results: dict = {"floor": {}, "floor_monthly": {}}
    for mname, mapping in MAPPINGS.items():
        for rname, resale in RESALE.items():
            results["floor"][f"{mname}__{rname}"] = {"all": floor_share(rows, mapping, resale), "test_2017": floor_share(test, mapping, resale)}
    by_month: dict = defaultdict(list)
    for r in rows:
        by_month[str(r["ym"])].append(r)
    for mname in ("0d_1d", "1d_7d"):
        results["floor_monthly"][mname] = {m: floor_share(part, MAPPINGS[mname], RESALE["late_unsold_early_sold"])["floor"]
                                          for m, part in sorted(by_month.items())}

    ytr, ys = [dp.label(r, dp.MAIN_WINDOW) for r in train], [dp.label(r, dp.MAIN_WINDOW) for r in test]
    model = dp.fit_logistic(train, ytr)
    scores = [model(r) for r in test]
    uniform = dp.TARGET_DEPOSIT * dp.evaluate([0.0] * len(test), test, ys)["exposed"]
    comparison = {"target_exposed": uniform}
    for name, step in (("unrounded", None), ("rounded_20pct", dp.ROUNDING_STEP)):
        eps = dp.solve_eps(scores, test, ys, "exposed", uniform, step)
        comparison[f"per_order_cap_{name}"] = dp.evaluate(dp.budget_deposits(scores, eps, step), test, ys)
    comparison["average_threshold"] = threshold_policy(scores, test, ys, uniform)
    results["formulation_comparison_strict_7d"] = comparison
    return results


def build_report(data_path: Path) -> dict:
    dp.ensure_dataset(data_path)
    rows, total = dp.load_rows(data_path)
    if total != dp.EXPECTED_ROW_COUNT:
        raise ValueError(f"{data_path}: {total} dòng, cần {dp.EXPECTED_ROW_COUNT}")
    return {
        "source": {"url": dp.SOURCE_URL, "sha256": dp.SOURCE_SHA256, "rows": total},
        "parameters": {"mappings_days": {k: list(v) for k, v in MAPPINGS.items()}, "resale": RESALE, "keep_fraction": KEEP,
                       "cancel_policy": "≥24h hoàn 100 %, 2–24h hoàn 50 %, <2h hoàn 0 %, không đến không hoàn",
                       "comparison_label_window_days": dp.MAIN_WINDOW, "sgd_seed": dp.SGD_SEED},
        "results": dp.rounded(run(rows)),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--data", type=Path, default=dp.DEFAULT_DATA)
    parser.add_argument("--out", type=Path, default=DEFAULT_RESULTS)
    parser.add_argument("--check", action="store_true", help="chạy lại và so với tệp kết quả đã có, không ghi đè")
    args = parser.parse_args(argv)
    sys.stdout.reconfigure(encoding="utf-8")
    report = build_report(args.data)
    if args.check:
        saved = json.loads(args.out.read_text(encoding="utf-8"))
        if saved["results"] != report["results"] or saved["parameters"] != report["parameters"]:
            print(f"KHÁC với {args.out}", file=sys.stderr)
            return 1
        print(f"khớp {args.out}")
        return 0
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"đã ghi {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
