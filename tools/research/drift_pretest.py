"""Phép thử trước thứ ba của đề cương khóa luận (Mục 1.5): mức rủi ro chọn trước có giữ được khi dữ liệu xấu đi thật không,
và tự điều chỉnh theo kết cục đến trễ giúp tới đâu.

Dữ liệu và nhãn: Hotel Booking Demand, đơn không cọc, nhãn chặt 7 ngày (dùng chung `deposit_pretest.py`). Tỷ lệ bỏ sát ngày của bộ này
đổi theo mùa, nên chọn giai đoạn hiệu chỉnh ở mùa ít bỏ và giai đoạn thử ở mùa nhiều bỏ cho ta một lần dữ liệu trôi bất lợi THẬT,
không phải giả lập.

Cách chạy thử: duyệt đơn theo ngày đặt. Mỗi đơn được quyết định mức cọc bằng ε đang có lúc đặt. Kết cục của đơn chỉ được biết vào
ngày đến (độ trễ bằng thời gian đặt trước). Ba cách:
- mức chung 50 %;
- giữ cố định ε đã chọn trên giai đoạn hiệu chỉnh;
- tự điều chỉnh: mỗi khi một kết cục về, log ε ← log ε + η · (mục tiêu − phần hở của đơn đó) · (giá trị đơn / giá trị trung bình).
  Đây là quy tắc cập nhật của Gibbs và Candès (2021) áp cho phần tiền bị hở thay cho tỷ lệ lỗi.
Mục tiêu là phần hở của mức chung 50 % đo trên giai đoạn hiệu chỉnh. Chạy: `python -I tools/research/drift_pretest.py [--check]`.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from collections import defaultdict
from pathlib import Path
from typing import Sequence

sys.path.insert(0, str(Path(__file__).resolve().parent))
import deposit_pretest as dp  # noqa: E402  (cùng thư mục; không phải gói cài đặt)

DEFAULT_RESULTS = Path(__file__).resolve().parent / "results" / "drift_pretest.json"

# Tham số cố định, chọn trước khi chạy. Giai đoạn: (học từ, học tới), (hiệu chỉnh từ, tới), (thử từ, tới), theo năm*100+tháng của ngày đến
WINDOWS = {
    "adverse_strong": ((201507, 201508), (201509, 201510), (201511, 201606)),
    "adverse_moderate": ((201507, 201606), (201607, 201609), (201610, 201703)),
    "favourable": ((201507, 201608), (201609, 201612), (201701, 201708)),
}
ETAS = (0.02, 0.05, 0.1)          # tốc độ điều chỉnh; báo cả ba để thấy kết quả có phụ thuộc cách chọn không
LOG_EPS_FLOOR = math.log(1e-5)    # chặn dưới để ε không về 0 (cọc 100 % mọi đơn vẫn đạt được)


def simulate(scores: Sequence[float], test: Sequence[dict], ys: Sequence[int], eps0: float, target: float, eta: float) -> list[float]:
    """Mức cọc từng đơn khi quyết định theo thứ tự ngày đặt và chỉ học từ kết cục đã về (vào ngày đến). eta = 0: giữ cố định ε."""
    mean_value = sum(r["value"] for r in test) / len(test)
    booked_on: dict = defaultdict(list)
    for i in sorted(range(len(test)), key=lambda i: (test[i]["booked"], i)):
        booked_on[test[i]["booked"]].append(i)
    resolve_on: dict = defaultdict(list)
    log_eps = math.log(eps0)
    deposits = [0.0] * len(test)
    for day in sorted(set(booked_on) | {r["arrival"] for r in test}):
        for i in resolve_on.pop(day, []):          # kết cục về trước, rồi mới quyết định cho đơn đặt trong ngày
            loss = (1 - deposits[i]) * ys[i]
            log_eps = min(max(log_eps + eta * (target - loss) * test[i]["value"] / mean_value, LOG_EPS_FLOOR), 0.0)
        eps = math.exp(log_eps)
        for i in booked_on.get(day, []):
            deposits[i] = dp.budget_deposits([scores[i]], eps)[0]
            resolve_on[test[i]["arrival"]].append(i)
    return deposits


def summarize(deposits: Sequence[float], test: Sequence[dict], ys: Sequence[int], target: float) -> dict:
    overall = dp.evaluate(deposits, test, ys)
    monthly = {}
    for month in sorted({r["ym"] for r in test}):
        idx = [i for i, r in enumerate(test) if r["ym"] == month]
        part = dp.evaluate([deposits[i] for i in idx], [test[i] for i in idx], [ys[i] for i in idx])
        monthly[str(month)] = part["exposed"] / target
    return {"deposit": overall["deposit"], "exposed": overall["exposed"], "ratio_to_target": overall["exposed"] / target,
            "monthly_ratio_to_target": monthly}


def monthly_abandon_share(rows: list[dict]) -> dict:
    """Tỷ lệ giá trị của các đơn bị bỏ sát ngày (nhãn chặt) theo tháng đến; cho thấy dữ liệu trôi theo mùa."""
    totals: dict = defaultdict(lambda: [0.0, 0.0])
    for r in rows:
        cell = totals[str(r["ym"])]
        cell[0] += r["value"]
        cell[1] += r["value"] * dp.label(r, dp.MAIN_WINDOW)
    return {month: dropped / value for month, (value, dropped) in sorted(totals.items())}


def run(rows: list[dict]) -> dict:
    results = {"monthly_abandon_value_share": monthly_abandon_share(rows)}
    for name, (fit_range, cal_range, test_range) in WINDOWS.items():
        pick = lambda lo_hi: [r for r in rows if lo_hi[0] <= r["ym"] <= lo_hi[1]]  # noqa: E731
        fit, cal, test = pick(fit_range), pick(cal_range), pick(test_range)
        yf, yc, yt = ([dp.label(r, dp.MAIN_WINDOW) for r in part] for part in (fit, cal, test))
        model = dp.fit_logistic(fit, yf)
        cal_scores, test_scores = [model(r) for r in cal], [model(r) for r in test]
        target = dp.TARGET_DEPOSIT * dp.evaluate([0.0] * len(cal), cal, yc)["exposed"]
        eps0 = dp.solve_eps(cal_scores, cal, yc, "exposed", target)
        delays = sorted((r["arrival"] - r["booked"]).days for r in test)
        entry = {
            "rows": {"fit": len(fit), "calibrate": len(cal), "test": len(test)},
            "abandon_value_share": {"calibrate": dp.evaluate([0.0] * len(cal), cal, yc)["exposed"],
                                    "test": dp.evaluate([0.0] * len(test), test, yt)["exposed"]},
            "median_outcome_delay_days": delays[len(delays) // 2],
            "target_exposed": target,
            "uniform_50": summarize([dp.TARGET_DEPOSIT] * len(test), test, yt, target),
            "fixed_eps": summarize(simulate(test_scores, test, yt, eps0, target, 0.0), test, yt, target),
            # Mốc "ngân sách cố định tốt nhất nhìn lại": cùng mô hình, một ε duy nhất chọn khi đã biết mọi kết cục của giai đoạn thử,
            # vừa đủ để phần hở của cả giai đoạn bằng mục tiêu. Không triển khai được; dùng để đo cái giá của việc phải tự điều chỉnh.
            "hindsight_fixed_eps": summarize(dp.budget_deposits(test_scores, dp.solve_eps(test_scores, test, yt, "exposed", target)), test, yt, target),
        }
        for eta in ETAS:
            entry[f"adaptive_eta_{eta}"] = summarize(simulate(test_scores, test, yt, eps0, target, eta), test, yt, target)
        results[name] = entry
    return results


def build_report(data_path: Path) -> dict:
    dp.ensure_dataset(data_path)
    rows, total = dp.load_rows(data_path)
    if total != dp.EXPECTED_ROW_COUNT:
        raise ValueError(f"{data_path}: {total} dòng, cần {dp.EXPECTED_ROW_COUNT}")
    return {
        "source": {"url": dp.SOURCE_URL, "sha256": dp.SOURCE_SHA256, "rows": total},
        "parameters": {"windows": {k: [list(x) for x in v] for k, v in WINDOWS.items()}, "etas": list(ETAS),
                       "label_window_days": dp.MAIN_WINDOW, "target": "50 % × tỷ lệ giá trị bị bỏ ở giai đoạn hiệu chỉnh",
                       "sgd_seed": dp.SGD_SEED, "sgd_epochs": dp.SGD_EPOCHS},
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
