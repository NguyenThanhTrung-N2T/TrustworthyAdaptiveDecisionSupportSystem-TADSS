"""Phép thử trước thứ hai của đề cương khóa luận (Mục 1.5): lịch sử của khách giúp dự đoán và chia cọc tới đâu, và ai chịu thiệt.

Dữ liệu: Medical Appointment No Shows (bản gốc trên Kaggle cần đăng nhập; dùng bản sao trên GitHub ghim theo commit, đã kiểm khớp số
dòng, số người và số lượt không đến đã công bố). Giấy phép CC BY-NC-SA 4.0. Tệp không sửa, không commit: tải về `data/datasets/`.

Khác với bộ khách sạn: lịch hẹn không có giá tiền, nên mọi lịch hẹn được coi có cùng giá trị. Con số "phần hở" ở đây là một chỉ số
(tỷ lệ lịch hẹn bị bỏ mà chưa được bảo đảm bằng cọc, theo giả định giá trị đồng nhất), không phải tiền.

Lịch sử của một lịch hẹn chỉ gồm những lần hẹn trước của cùng người mà ngày hẹn đã qua TRƯỚC ngày đặt lịch đang xét, tức kết cục đã
biết lúc đặt. Cách đo và cách chọn mức cọc giống `deposit_pretest.py` (dùng chung hàm). Chạy:
`python -I tools/research/history_pretest.py [--check]`.
"""
from __future__ import annotations

import argparse
import csv
import json
import random
import sys
from collections import defaultdict
from datetime import date
from pathlib import Path
from typing import Callable, Sequence

sys.path.insert(0, str(Path(__file__).resolve().parent))
import deposit_pretest as dp  # noqa: E402  (cùng thư mục; không phải gói cài đặt)

ORIGINAL_URL = "https://www.kaggle.com/datasets/joniarroba/noshowappointments"
SOURCE_URL = "https://raw.githubusercontent.com/samhadd-ad/Medical-Appointment-noShow/d2a909d14903b8f2bad73582ec5827c4a5f996d6/KaggleV2-May-2016.csv"
SOURCE_SHA256 = "9132d3e7d0246617df9041d3764f20ad6f08e7b0d9f0997fa254fc5e52eda27d"
EXPECTED_ROW_COUNT = 110527
LICENSE = "CC BY-NC-SA 4.0 (Hoppen, Aquarela Analytics 2016, Kaggle); bản sao GitHub không đổi giấy phép"

DEFAULT_DATA = dp.REPO_ROOT / "data" / "datasets" / "medical_noshow.csv"
DEFAULT_RESULTS = Path(__file__).resolve().parent / "results" / "history_pretest.json"

# Tham số cố định, chọn trước khi chạy
SPLIT_DAY = date(2016, 5, 25)    # học: lịch hẹn trước ngày này; thử: từ ngày này
HALF_LIFE_DAYS = 14.0            # sổ có quên dần: một lần hẹn cách đây 14 ngày tính bằng nửa lần hẹn hôm nay
CREDIBILITY_K = 2.0              # kéo tỷ lệ riêng về tỷ lệ chung như thêm 2 lịch hẹn ảo
LOW_DEPOSIT = 0.2
FAIRNESS_BOOTSTRAP_SEED = 20261116
FAIRNESS_BOOTSTRAP_ROUNDS = 500


def load_raw(path: Path) -> tuple[list[dict], int]:
    """(lịch hẹn hợp lệ, tổng số dòng). Loại dòng ngày hẹn trước ngày đặt và dòng tuổi âm."""
    out, total = [], 0
    with path.open(encoding="utf-8", newline="") as handle:
        for r in csv.DictReader(handle):
            total += 1
            booked, appointment = date.fromisoformat(r["ScheduledDay"][:10]), date.fromisoformat(r["AppointmentDay"][:10])
            if appointment < booked or int(r["Age"]) < 0:
                continue
            out.append({"pid": r["PatientId"], "booked": booked, "appt": appointment, "lead": (appointment - booked).days,
                        "y": int(r["No-show"] == "Yes"), "age": int(r["Age"]), "gender": r["Gender"], "src": r})
    return out, total


def history_at_booking(row: dict, same_patient: Sequence[dict]) -> dict:
    """Lịch sử đã biết lúc đặt: chỉ những lần hẹn có ngày hẹn trước ngày đặt của `row`."""
    prior = [p for p in same_patient if p["appt"] < row["booked"]]
    weights = [(0.5 ** ((row["booked"] - p["appt"]).days / HALF_LIFE_DAYS), p["y"]) for p in prior]
    return {"n": len(prior), "no_show": sum(p["y"] for p in prior),
            "w_show": sum(w for w, y in weights if not y), "w_no_show": sum(w for w, y in weights if y)}


def build_rows(raw: list[dict]) -> list[dict]:
    base = sum(r["y"] for r in raw) / len(raw)
    by_patient: dict[str, list[dict]] = defaultdict(list)
    for r in raw:
        by_patient[r["pid"]].append(r)
    rows = []
    for r in raw:
        h = history_at_booking(r, by_patient[r["pid"]])
        decayed = (CREDIBILITY_K * base + h["w_no_show"]) / (CREDIBILITY_K + h["w_show"] + h["w_no_show"])
        counted = (CREDIBILITY_K * base + h["no_show"]) / (CREDIBILITY_K + h["n"])
        s = r["src"]
        leadbin, agebin = dp.bins(r["lead"], (0, 2, 7, 14, 30)), dp.bins(r["age"], (5, 12, 18, 30, 45, 60, 75))
        order = ["lead=%d" % leadbin, "wd=%d" % r["appt"].weekday(), "age=%d" % agebin, "g=" + s["Gender"],
                 "sch=" + s["Scholarship"], "hip=" + s["Hipertension"], "dia=" + s["Diabetes"], "alc=" + s["Alcoholism"],
                 "hc=%d" % (s["Handcap"] != "0"), "nb=" + s["Neighbourhood"]]
        counts = ["n=%d" % dp.bins(h["n"], (0, 1, 3)), "ns=%d" % dp.bins(h["no_show"], (0, 1))]
        ledger = ["cd=%d" % dp.bins(decayed, (0.08, 0.15, 0.25, 0.4, 0.6)), "wn=%d" % dp.bins(h["w_show"] + h["w_no_show"], (0, 0.5, 2))]
        rows.append({"value": 1.0, "y": r["y"], "appt": r["appt"], "has_history": h["n"] > 0, "age": r["age"], "gender": r["gender"],
                     "stratum": (dp.bins(r["lead"], (0, 2, 7, 14, 30)), dp.bins(r["age"], (18, 45, 75))),
                     "history_counted": counted, "history_decayed": decayed,
                     "f_order": order, "f_counts": order + counts, "f_ledger": order + ledger})
    return rows


def with_features(rows: Sequence[dict], key: str) -> list[dict]:
    return [{**r, "f": r[key]} for r in rows]


def low_share(deposits: Sequence[float], test: Sequence[dict], ys: Sequence[int], pick: Callable[[dict], bool]) -> tuple[float, int]:
    """Trong số người đến (y = 0) thỏa `pick`: tỷ lệ được cọc ≤ 20 %, và số người."""
    idx = [i for i, r in enumerate(test) if ys[i] == 0 and pick(r)]
    return sum(1 for i in idx if deposits[i] <= LOW_DEPOSIT + 1e-9) / len(idx), len(idx)


def stratified_gap(deposits: Sequence[float], test: Sequence[dict], ys: Sequence[int]) -> float:
    """Chênh lệch tỷ lệ cọc thấp (đã có lịch sử trừ chưa có), tính trong từng nhóm thời gian đặt trước × tuổi rồi lấy trung bình
    theo số người chưa có lịch sử của nhóm. Loại phần chênh do hai nhóm khác nhau ở thời gian đặt trước và tuổi."""
    cells: dict[tuple, list[list[int]]] = defaultdict(lambda: [[0, 0], [0, 0]])
    for i, r in enumerate(test):
        if ys[i] == 0:
            cell = cells[r["stratum"]][int(r["has_history"])]
            cell[0] += 1
            cell[1] += deposits[i] <= LOW_DEPOSIT + 1e-9
    weighted, weight = 0.0, 0
    for new, old in cells.values():
        if new[0] and old[0]:
            weighted += new[0] * (old[1] / old[0] - new[1] / new[0])
            weight += new[0]
    return weighted / weight


def gap_interval(deposits: Sequence[float], test: Sequence[dict], ys: Sequence[int]) -> list[float]:
    """Khoảng 95 % (bootstrap) của chênh lệch tỷ lệ cọc thấp giữa người đã có và chưa có lịch sử, trong số người đến."""
    rng = random.Random(FAIRNESS_BOOTSTRAP_SEED)
    shows = [i for i in range(len(test)) if ys[i] == 0]
    gaps = []
    for _ in range(FAIRNESS_BOOTSTRAP_ROUNDS):
        sample = [shows[rng.randrange(len(shows))] for _ in shows]
        tally = [[0, 0], [0, 0]]
        for i in sample:
            cell = tally[int(test[i]["has_history"])]
            cell[0] += 1
            cell[1] += deposits[i] <= LOW_DEPOSIT + 1e-9
        gaps.append(tally[1][1] / tally[1][0] - tally[0][1] / tally[0][0])
    gaps.sort()
    return [gaps[int(0.025 * len(gaps))], gaps[int(0.975 * len(gaps)) - 1]]


def run(rows: list[dict]) -> dict:
    train = [r for r in rows if r["appt"] < SPLIT_DAY]
    test = [r for r in rows if r["appt"] >= SPLIT_DAY]
    ytr, ys = [r["y"] for r in train], [r["y"] for r in test]
    history_idx = [i for i, r in enumerate(test) if r["has_history"]]
    results: dict = {"dataset": {
        "valid_rows": len(rows), "patients_with_repeat_visits": None, "train_rows": len(train), "test_rows": len(test),
        "no_show_rate_train": sum(ytr) / len(ytr), "no_show_rate_test": sum(ys) / len(ys),
        "test_share_with_history": len(history_idx) / len(test),
        "no_show_rate_test_with_history": sum(ys[i] for i in history_idx) / len(history_idx),
        "no_show_rate_test_without_history": sum(ys[i] for i in range(len(test)) if i not in set(history_idx)) / (len(test) - len(history_idx)),
    }}

    scores: dict[str, list[float]] = {}
    lookup = dp.fit_lookup(train, ytr, lambda r: r["stratum"])
    scores["lookup_lead_time_x_age"] = [lookup(r) for r in test]
    for name, key in (("model_booking_only", "f_order"), ("model_plus_counts", "f_counts"), ("model_plus_decayed_ledger", "f_ledger")):
        model = dp.fit_logistic(with_features(train, key), ytr)
        scores[name] = [model(r) for r in with_features(test, key)]
    scores["history_only_counts"] = [r["history_counted"] for r in test]
    scores["history_only_decayed"] = [r["history_decayed"] for r in test]

    sub_test, sub_y = [test[i] for i in history_idx], [ys[i] for i in history_idx]
    results["uncovered_index"] = {
        name: {"auc": dp.auc(s, ys), "auc_with_history": dp.auc([s[i] for i in history_idx], sub_y),
               "same_deposit": dp.compare(s, test, ys)["same_deposit"],
               "same_deposit_with_history": dp.compare([s[i] for i in history_idx], sub_test, sub_y)["same_deposit"]}
        for name, s in scores.items()
    }

    deposits = {name: dp.budget_deposits(s, dp.solve_eps(s, test, ys, "deposit", dp.TARGET_DEPOSIT)) for name, s in scores.items()}
    pick = lambda *names: {n: deposits[n] for n in names}  # noqa: E731
    results["bootstrap_95pct_reduction_at_same_deposit"] = {
        "counts_vs_booking_only": dp.bootstrap(test, ys, pick("model_plus_counts", "model_booking_only")),
        "decayed_vs_booking_only": dp.bootstrap(test, ys, pick("model_plus_decayed_ledger", "model_booking_only")),
        "decayed_vs_counts": dp.bootstrap(test, ys, pick("model_plus_decayed_ledger", "model_plus_counts")),
        "booking_only_vs_lookup": dp.bootstrap(test, ys, pick("model_booking_only", "lookup_lead_time_x_age")),
    }

    groups = {"female": lambda r: r["gender"] == "F", "male": lambda r: r["gender"] == "M",
              "under_30": lambda r: r["age"] < 30, "30_plus": lambda r: r["age"] >= 30,
              "without_history": lambda r: not r["has_history"], "with_history": lambda r: r["has_history"]}
    fairness = {}
    for name in ("model_booking_only", "model_plus_decayed_ledger"):
        d = deposits[name]
        shares = {g: dict(zip(("share", "people"), low_share(d, test, ys, f))) for g, f in groups.items()}
        fairness[name] = {
            "low_deposit_share_among_shows": shares,
            "history_gap": shares["with_history"]["share"] - shares["without_history"]["share"],
            "history_gap_95pct": gap_interval(d, test, ys),
            "history_gap_within_lead_time_x_age": stratified_gap(d, test, ys),
        }
    results["fairness_at_same_deposit"] = fairness
    return results


def build_report(data_path: Path) -> dict:
    dp.ensure_dataset(data_path, SOURCE_URL, SOURCE_SHA256)
    raw, total = load_raw(data_path)
    if total != EXPECTED_ROW_COUNT:
        raise ValueError(f"{data_path}: {total} dòng, cần {EXPECTED_ROW_COUNT}")
    rows = build_rows(raw)
    results = run(rows)
    patients = defaultdict(int)
    for r in raw:
        patients[r["pid"]] += 1
    results["dataset"]["patients"] = len(patients)
    results["dataset"]["patients_with_repeat_visits"] = sum(1 for n in patients.values() if n > 1)
    results["dataset"]["rows_in_file"] = total
    return {
        "source": {"original": ORIGINAL_URL, "url": SOURCE_URL, "sha256": SOURCE_SHA256, "rows": total, "license": LICENSE},
        "parameters": {
            "split_day": SPLIT_DAY.isoformat(), "half_life_days": HALF_LIFE_DAYS, "credibility_k": CREDIBILITY_K,
            "low_deposit": LOW_DEPOSIT, "target_deposit": dp.TARGET_DEPOSIT, "value_per_appointment": 1.0,
            "sgd_seed": dp.SGD_SEED, "sgd_epochs": dp.SGD_EPOCHS, "lookup_prior_weight": dp.LOOKUP_PRIOR_WEIGHT,
            "bootstrap_seed": dp.BOOTSTRAP_SEED, "bootstrap_rounds": dp.BOOTSTRAP_ROUNDS,
            "fairness_bootstrap_seed": FAIRNESS_BOOTSTRAP_SEED, "fairness_bootstrap_rounds": FAIRNESS_BOOTSTRAP_ROUNDS,
        },
        "results": dp.rounded(results),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--data", type=Path, default=DEFAULT_DATA)
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
