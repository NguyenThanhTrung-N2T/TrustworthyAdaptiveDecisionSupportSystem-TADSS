"""Phép thử trước thứ tư của đề cương khóa luận (Mục 1.5): cần bao nhiêu đơn học thì mô hình mới hơn bảng tra?

Cách làm theo phép phân tích đường học của Perlich, Provost và Simonoff (2003): lấy mẫu con của tập học ở nhiều cỡ, huấn luyện bảng tra
và mô hình trên cùng mẫu, đo trên cùng tập thử cố định. Mỗi cỡ (trừ cỡ đầy đủ) lấy mẫu ba lần với seed cố định rồi lấy trung bình.
Phép đo: mức giảm phần hở so với mức chung, ở cùng mức cọc trung bình 50 % (như `deposit_pretest.py`).

Hai bối cảnh: khách sạn (nhãn chặt 7 ngày; bảng tra theo thời gian đặt trước × loại khách sạn; mô hình 11 đặc điểm) và lịch khám
(bảng tra theo thời gian đặt trước × nhóm tuổi; mô hình có sổ tin cậy quên dần). Chạy: `python -I tools/research/curve_pretest.py [--check]`.
"""
from __future__ import annotations

import argparse
import json
import random
import sys
from pathlib import Path
from typing import Callable, Sequence

sys.path.insert(0, str(Path(__file__).resolve().parent))
import deposit_pretest as dp  # noqa: E402
import history_pretest as hp  # noqa: E402

DEFAULT_RESULTS = Path(__file__).resolve().parent / "results" / "curve_pretest.json"
SIZES = (250, 500, 1000, 2000, 5000, 10000, 20000, 40000)
SEEDS = (1, 2, 3)


def subsample(n: int, total: int, seed: int) -> list[int]:
    return random.Random(seed).sample(range(total), n)


def curve(train: Sequence[dict], test: Sequence[dict], ytr: Sequence[int], ys: Sequence[int],
          lookup_key: Callable[[dict], object], feature_key: str | None) -> list[dict]:
    """Mỗi cỡ tập học: mức giảm trung bình của bảng tra và của mô hình, và số lần lấy mẫu dùng được."""
    with_features = (lambda rs: [{**r, "f": r[feature_key]} for r in rs]) if feature_key else (lambda rs: list(rs))
    test_f = with_features(test)
    points = []
    for n in [s for s in SIZES if s < len(train)] + [len(train)]:
        draws = [subsample(n, len(train), seed) for seed in SEEDS] if n < len(train) else [list(range(n))]
        lookup_scores, model_scores = [], []
        for idx in draws:
            part, labels = [train[i] for i in idx], [ytr[i] for i in idx]
            if 0 < sum(labels) < len(labels):
                lookup = dp.fit_lookup(part, labels, lookup_key)
                model = dp.fit_logistic(with_features(part), labels)
                lookup_scores.append(dp.compare([lookup(r) for r in test], test, ys)["same_deposit"]["reduction_vs_uniform"])
                model_scores.append(dp.compare([model(r) for r in test_f], test, ys)["same_deposit"]["reduction_vs_uniform"])
        points.append({"train_rows": n, "draws": len(lookup_scores),
                       "lookup": sum(lookup_scores) / len(lookup_scores), "model": sum(model_scores) / len(model_scores),
                       "model_minus_lookup": sum(model_scores) / len(model_scores) - sum(lookup_scores) / len(lookup_scores)})
    return points


def build_report(hotel_path: Path, medical_path: Path) -> dict:
    dp.ensure_dataset(hotel_path)
    dp.ensure_dataset(medical_path, hp.SOURCE_URL, hp.SOURCE_SHA256)
    rows, _ = dp.load_rows(hotel_path)
    train, test = dp.split(rows, "primary")
    hotel = curve(train, test, [dp.label(r, dp.MAIN_WINDOW) for r in train], [dp.label(r, dp.MAIN_WINDOW) for r in test], dp.lookup_key, None)
    raw, _ = hp.load_raw(medical_path)
    mrows = hp.build_rows(raw)
    mtrain = [r for r in mrows if r["appt"] < hp.SPLIT_DAY]
    mtest = [r for r in mrows if r["appt"] >= hp.SPLIT_DAY]
    medical = curve(mtrain, mtest, [r["y"] for r in mtrain], [r["y"] for r in mtest], lambda r: r["stratum"], "f_ledger")
    return {
        "source": {"hotel_sha256": dp.SOURCE_SHA256, "medical_sha256": hp.SOURCE_SHA256},
        "parameters": {"sizes": list(SIZES), "seeds": list(SEEDS), "hotel_label_window_days": dp.MAIN_WINDOW,
                       "hotel_lookup": "lead time bin × hotel", "medical_lookup": "lead time bin × age group",
                       "medical_model_features": "booking + decayed ledger", "target_deposit": dp.TARGET_DEPOSIT},
        "results": dp.rounded({"hotel_strict_7d": hotel, "medical_noshow": medical}),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--hotel", type=Path, default=dp.DEFAULT_DATA)
    parser.add_argument("--medical", type=Path, default=hp.DEFAULT_DATA)
    parser.add_argument("--out", type=Path, default=DEFAULT_RESULTS)
    parser.add_argument("--check", action="store_true", help="chạy lại và so với tệp kết quả đã có, không ghi đè")
    args = parser.parse_args(argv)
    sys.stdout.reconfigure(encoding="utf-8")
    report = build_report(args.hotel, args.medical)
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
