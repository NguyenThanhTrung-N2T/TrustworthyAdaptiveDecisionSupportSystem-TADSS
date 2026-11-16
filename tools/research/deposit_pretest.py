"""Phép thử trước của đề cương khóa luận (Mục 1.5): chia tiền cọc theo rủi ro có hơn mức chung và bảng luật không?

Dữ liệu: Hotel Booking Demand (bản TidyTuesday 2020-02-11, cùng nguồn và mã SHA-256 với `tools/evidence/hotel_adapter.py`), chỉ đơn
`deposit_type = No Deposit` có giá trị > 0. Tệp gốc không sửa, không commit: tải về `data/datasets/` (gitignore) và kiểm SHA-256 trước khi đọc.

Cách đo (giống nhau cho mọi chiến lược):
- Mỗi chiến lược gán một mức cọc d cho từng đơn của tập thử. Mức cọc trung bình D và phần tiền bị hở L đều tính theo giá trị đơn v:
  D = Σ d·v / Σ v, L = Σ (1 − d)·v·y / Σ v, với y = 1 nếu đơn bị tính là bỏ.
- Chiến lược dùng điểm rủi ro s (mô hình, bảng tra) đặt cọc theo quy tắc ngân sách rủi ro d = max(0, 1 − ε / s).
- "Cùng mức cọc": chọn ε bằng chia đôi sao cho D = 0,5. Việc chọn này không dùng nhãn của tập thử.
- "Cùng mức hở": chọn ε sao cho L bằng phần tiền bị hở của mức chung 50 %. Việc chọn này có dùng nhãn, nên chỉ là cách đọc đường biên
  để so sánh, không phải một chính sách triển khai được.
- Mọi tham số học (trọng số hồi quy, tỷ lệ của bảng tra, hệ số k) chỉ lấy từ tập huấn luyện. Tham số cố định chọn trước khi chạy, không chỉnh theo kết quả.

Chạy: `python -I tools/research/deposit_pretest.py` (tải dữ liệu nếu thiếu, ghi `tools/research/results/deposit_pretest.json`);
`--check` chạy lại và so với tệp kết quả đã commit. Hướng dẫn và giới hạn: `tools/research/README.md`.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import random
import sys
import tempfile
import urllib.request
from datetime import date, timedelta
from pathlib import Path
from typing import Callable, Sequence

SOURCE_URL = "https://raw.githubusercontent.com/rfordatascience/tidytuesday/d75aaa0d31596ad6487ae0db20138067d301e031/data/2020/2020-02-11/hotels.csv"
SOURCE_SHA256 = "7c2ae42a7353905ea136e5c2287f17c92c5435826598bfbb8491c6f0c7b1fc06"
EXPECTED_ROW_COUNT = 119390

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_DATA = REPO_ROOT / "data" / "datasets" / "hotels.csv"
DEFAULT_RESULTS = Path(__file__).resolve().parent / "results" / "deposit_pretest.json"

# Tham số cố định (chọn một lần ở lần chạy đầu 13/11/2026, không chỉnh theo kết quả)
SGD_SEED = 7
SGD_EPOCHS = 6
SGD_LEARNING_RATE = 0.05
SGD_L2 = 1e-5
LOOKUP_PRIOR_WEIGHT = 5          # bảng tra kéo tỷ lệ của nhóm nhỏ về tỷ lệ chung như thêm 5 đơn ảo
CREDIBILITY_K_GRID = (1, 2, 5, 10, 20)
TARGET_DEPOSIT = 0.5
STRICT_WINDOWS = (1, 3, 7)       # nhãn chặt: không đến, hoặc hủy trong K ngày trước ngày đến
MAIN_WINDOW = 7
ROUNDING_STEP = 0.2
BOOTSTRAP_SEED = 20261115
BOOTSTRAP_ROUNDS = 500
RISK_MARGIN_SEED = 1
RISK_MARGIN_ROUNDS = 200
RISK_WINDOWS = (  # (học tới, hiệu chỉnh từ, tới, thử từ, tới) theo năm*100+tháng của ngày đến
    (201608, 201609, 201612, 201701, 201708),
    (201603, 201604, 201607, 201608, 201703),
    (201512, 201601, 201606, 201607, 201612),
)
HAND_RULE = {0: 0.2, 1: 0.4, 2: 0.6, 3: 0.8}   # luật tự đặt: đặt càng xa ngày đến thì cọc càng cao
MONTHS = {m: i + 1 for i, m in enumerate(
    "January February March April May June July August September October November December".split())}
DIGITS = 4


def bins(value: float, cuts: Sequence[float]) -> int:
    """Số thứ tự của khoảng chứa `value` (các mốc `cuts` tăng dần, mốc thuộc khoảng bên trái)."""
    for i, cut in enumerate(cuts):
        if value <= cut:
            return i
    return len(cuts)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def ensure_dataset(path: Path, url: str = SOURCE_URL, expected_sha256: str = SOURCE_SHA256) -> None:
    """Tải tệp nếu chưa có; tệp có sẵn hoặc vừa tải đều phải khớp SHA-256, lệch thì dừng (không đọc dữ liệu lạ)."""
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(dir=path.parent, delete=False, suffix=".part") as tmp:
            with urllib.request.urlopen(url, timeout=120) as response:
                tmp.write(response.read())
            tmp_path = Path(tmp.name)
        if sha256_file(tmp_path) != expected_sha256:
            tmp_path.unlink()
            raise ValueError(f"tệp tải từ {url} sai SHA-256, đã xóa")
        tmp_path.replace(path)
    actual = sha256_file(path)
    if actual != expected_sha256:
        raise ValueError(f"{path}: SHA-256 {actual} khác {expected_sha256}; xóa tệp để tải lại")


def load_rows(path: Path) -> tuple[list[dict], int]:
    """(đơn No Deposit có giá trị > 0, tổng số dòng của tệp). Chỉ dùng thông tin biết được lúc đặt làm đặc điểm."""
    rows: list[dict] = []
    total = 0
    with path.open(encoding="utf-8", newline="") as handle:
        for r in csv.DictReader(handle):
            total += 1
            if r["deposit_type"] != "No Deposit":
                continue
            try:
                adr = float(r["adr"])
            except ValueError:
                continue
            nights = int(r["stays_in_weekend_nights"]) + int(r["stays_in_week_nights"])
            value = adr * max(1, nights)
            if value <= 0:
                continue
            arrival = date(int(r["arrival_date_year"]), MONTHS[r["arrival_date_month"]], int(r["arrival_date_day_of_month"]))
            lead = int(r["lead_time"])
            status = r["reservation_status"]
            days_before = None
            if status == "Canceled":
                days_before = (arrival - date.fromisoformat(r["reservation_status_date"])).days
            pc, po = int(r["previous_cancellations"]), int(r["previous_bookings_not_canceled"])
            kids = r["children"] not in ("", "NA", "0", "0.0") or r["babies"] not in ("", "0")
            features = [
                "hotel=" + r["hotel"], "seg=" + r["market_segment"], "ctype=" + r["customer_type"],
                "lead=%d" % bins(lead, (0, 7, 30, 90, 180)), "nights=%d" % bins(nights, (1, 3, 7)),
                "adr=%d" % bins(adr, (60, 90, 120, 160)), "adults=%d" % bins(int(r["adults"]), (1, 2)),
                "kids=%d" % int(kids), "repeat=" + r["is_repeated_guest"],
                "pc=%d" % bins(pc, (0, 1)), "po=%d" % bins(po, (0, 2, 9)),
            ]
            rows.append({
                "ym": arrival.year * 100 + arrival.month, "arrival": arrival, "booked": arrival - timedelta(days=lead),
                "canceled": int(r["is_canceled"]), "status": status, "days_before": days_before,
                "value": value, "pc": pc, "po": po, "leadbin": bins(lead, (7, 30, 90)), "hotel": r["hotel"], "f": features,
            })
    return rows, total


def label(row: dict, window: int | None) -> int:
    """1 nếu đơn bị tính là bỏ. `window` None: mọi đơn bị hủy; số K: không đến, hoặc hủy trong K ngày trước ngày đến."""
    if window is None:
        return row["canceled"]
    if row["status"] == "No-Show":
        return 1
    return int(row["status"] == "Canceled" and row["days_before"] <= window)


def fit_logistic(train: Sequence[dict], ys: Sequence[int]) -> Callable[[dict], float]:
    """Hồi quy logistic trên đặc điểm dạng nhãn (SGD, L2), seed cố định nên tất định."""
    base = sum(ys) / len(ys)
    index: dict[str, int] = {}
    for r in train:
        for name in r["f"]:
            index.setdefault(name, len(index))
    weights = [0.0] * len(index)
    bias = math.log(base / (1 - base))
    rng = random.Random(SGD_SEED)
    for epoch in range(SGD_EPOCHS):
        lr = SGD_LEARNING_RATE / (1 + epoch)
        order = list(range(len(train)))  # mỗi vòng xáo lại từ thứ tự gốc của tệp
        rng.shuffle(order)
        for i in order:
            idx = [index[n] for n in train[i]["f"]]
            z = bias + sum(weights[j] for j in idx)
            gradient = 1 / (1 + math.exp(-max(-30.0, min(30.0, z)))) - ys[i]
            bias -= lr * gradient
            for j in idx:
                weights[j] -= lr * (gradient + SGD_L2 * weights[j])

    def predict(r: dict) -> float:
        z = bias + sum(weights[index[n]] for n in r["f"] if n in index)
        return 1 / (1 + math.exp(-max(-30.0, min(30.0, z))))

    return predict


def fit_lookup(train: Sequence[dict], ys: Sequence[int], key: Callable[[dict], object]) -> Callable[[dict], float]:
    """Bảng tra: tỷ lệ bỏ của từng nhóm trong dữ liệu cũ, kéo về tỷ lệ chung như thêm LOOKUP_PRIOR_WEIGHT đơn ảo."""
    base = sum(ys) / len(ys)
    counts: dict[object, list[int]] = {}
    for r, y in zip(train, ys):
        cell = counts.setdefault(key(r), [0, 0])
        cell[0] += 1
        cell[1] += y
    rates = {k: (c[1] + LOOKUP_PRIOR_WEIGHT * base) / (c[0] + LOOKUP_PRIOR_WEIGHT) for k, c in counts.items()}
    return lambda r: rates.get(key(r), base)


def fit_credibility(train: Sequence[dict], ys: Sequence[int]) -> tuple[Callable[[dict], float], int]:
    """Chỉ dùng lịch sử của khách (số lần hủy, số lần hoàn tất), kéo về tỷ lệ chung; k chọn trên tập huấn luyện."""
    base = sum(ys) / len(ys)

    def score(r: dict, k: int) -> float:
        return (k * base + r["pc"]) / (k + r["pc"] + r["po"])

    best = min(CREDIBILITY_K_GRID, key=lambda k: sum((score(r, k) - y) ** 2 for r, y in zip(train, ys)))
    return (lambda r: score(r, best)), best


def auc(scores: Sequence[float], ys: Sequence[int]) -> float:
    """Xác suất một đơn bị bỏ được chấm rủi ro cao hơn một đơn không bị bỏ (hòa tính nửa)."""
    pairs = sorted(zip(scores, ys))
    positives = sum(ys)
    negatives = len(ys) - positives
    rank_sum, i = 0.0, 0
    while i < len(pairs):
        j = i
        while j < len(pairs) and pairs[j][0] == pairs[i][0]:
            j += 1
        rank_sum += (i + j + 1) / 2 * sum(y for _, y in pairs[i:j])
        i = j
    return (rank_sum - positives * (positives + 1) / 2) / (positives * negatives)


def calibration(scores: Sequence[float], ys: Sequence[int], groups: int = 5) -> list[list[float]]:
    """[dự đoán trung bình, tỷ lệ thực tế] của từng nhóm, chia theo thứ tự điểm thành `groups` nhóm bằng nhau."""
    order = sorted(range(len(ys)), key=lambda i: scores[i])
    n = len(order)
    out = []
    for g in range(groups):
        part = order[g * n // groups:(g + 1) * n // groups]
        out.append([sum(scores[i] for i in part) / len(part), sum(ys[i] for i in part) / len(part)])
    return out


def budget_deposits(scores: Sequence[float], eps: float, step: float | None = None) -> list[float]:
    """d = max(0, 1 − ε / s), cắt trong [0, 1]; có `step` thì làm tròn lên bậc gần nhất."""
    out = []
    for s in scores:
        d = max(0.0, min(1.0, 1 - eps / max(s, 1e-9)))
        if step:
            d = min(1.0, math.ceil(d / step - 1e-9) * step)
        out.append(d)
    return out


def evaluate(deposits: Sequence[float], test: Sequence[dict], ys: Sequence[int]) -> dict:
    """Mức cọc trung bình, phần tiền bị hở (đều theo giá trị) và tỷ lệ đơn cọc ≤ 20 %."""
    total = sum(r["value"] for r in test)
    deposit = sum(d * r["value"] for d, r in zip(deposits, test)) / total
    exposed = sum((1 - d) * r["value"] * y for d, r, y in zip(deposits, test, ys)) / total
    low = sum(1 for d in deposits if d <= 0.2 + 1e-9) / len(deposits)
    return {"deposit": deposit, "exposed": exposed, "low_share": low}


def solve_eps(scores: Sequence[float], test: Sequence[dict], ys: Sequence[int], metric: str, target: float,
              step: float | None = None) -> float:
    """Chia đôi trên log ε cho tới khi `metric` (deposit giảm dần, exposed tăng dần theo ε) gần `target` nhất."""
    lo, hi = math.log(1e-6), math.log(1.0)
    for _ in range(40):
        mid = (lo + hi) / 2
        value = evaluate(budget_deposits(scores, math.exp(mid), step), test, ys)[metric]
        too_high = value > target if metric == "deposit" else value < target
        if too_high:
            lo = mid
        else:
            hi = mid
    return math.exp((lo + hi) / 2)


def compare(scores: Sequence[float], test: Sequence[dict], ys: Sequence[int], step: float | None = None) -> dict:
    """Điểm rủi ro → hai cách đọc: cùng mức cọc 50 % (giảm bao nhiêu phần hở) và cùng mức hở với mức chung 50 % (cọc còn bao nhiêu)."""
    uniform_rate = evaluate([0.0] * len(test), test, ys)["exposed"]  # không cọc: tỷ lệ giá trị của các đơn bị bỏ
    at_dep = evaluate(budget_deposits(scores, solve_eps(scores, test, ys, "deposit", TARGET_DEPOSIT, step), step), test, ys)
    at_exp = evaluate(budget_deposits(scores, solve_eps(scores, test, ys, "exposed", TARGET_DEPOSIT * uniform_rate, step), step), test, ys)
    return {
        "same_deposit": {**at_dep, "reduction_vs_uniform": 1 - at_dep["exposed"] / ((1 - at_dep["deposit"]) * uniform_rate)},
        "same_exposure": at_exp,
    }


def fixed_rule(deposits: Sequence[float], test: Sequence[dict], ys: Sequence[int]) -> dict:
    """Luật cho sẵn mức cọc: so với mức chung có cùng mức cọc trung bình."""
    uniform_rate = evaluate([0.0] * len(test), test, ys)["exposed"]
    result = evaluate(deposits, test, ys)
    return {**result, "reduction_vs_uniform": 1 - result["exposed"] / ((1 - result["deposit"]) * uniform_rate)}


def bootstrap(test: Sequence[dict], ys: Sequence[int], strategies: dict[str, Sequence[float]]) -> dict:
    """Khoảng 95 % (phân vị 2,5 và 97,5) của mức giảm so với mức chung cùng cọc, với mức cọc từng đơn giữ cố định."""
    rng = random.Random(BOOTSTRAP_SEED)
    n = len(test)
    draws: dict[str, list[float]] = {name: [] for name in strategies}
    names = list(strategies)
    gaps: list[float] = []
    for _ in range(BOOTSTRAP_ROUNDS):
        sample = [rng.randrange(n) for _ in range(n)]
        total = sum(test[i]["value"] for i in sample)
        dropped = sum(test[i]["value"] * ys[i] for i in sample) / total
        reductions = {}
        for name, deps in strategies.items():
            dep = sum(deps[i] * test[i]["value"] for i in sample) / total
            exposed = sum((1 - deps[i]) * test[i]["value"] * ys[i] for i in sample) / total
            reductions[name] = 1 - exposed / ((1 - dep) * dropped)
            draws[name].append(reductions[name])
        gaps.append(reductions[names[0]] - reductions[names[1]])

    def interval(values: list[float]) -> list[float]:
        ordered = sorted(values)
        return [ordered[int(0.025 * len(ordered))], ordered[int(0.975 * len(ordered)) - 1]]

    return {**{name: interval(values) for name, values in draws.items()}, f"{names[0]}_minus_{names[1]}": interval(gaps)}


def risk_level_on_later_data(rows: Sequence[dict], window: tuple[int, int, int, int, int]) -> dict:
    """Chọn ε trên giai đoạn hiệu chỉnh để phần hở bằng của mức chung 50 % ở giai đoạn đó (mục tiêu cố định), rồi áp lên giai đoạn sau.
    Thêm một ε thận trọng: phân vị 95 % (bootstrap) của phần hở trên giai đoạn hiệu chỉnh không vượt mục tiêu. Nhãn chặt MAIN_WINDOW ngày."""
    fit_end, cal_start, cal_end, test_start, test_end = window
    fit = [r for r in rows if r["ym"] <= fit_end]
    cal = [r for r in rows if cal_start <= r["ym"] <= cal_end]
    test = [r for r in rows if test_start <= r["ym"] <= test_end]
    yf, yc, yt = ([label(r, MAIN_WINDOW) for r in part] for part in (fit, cal, test))
    model = fit_logistic(fit, yf)
    cal_scores, test_scores = [model(r) for r in cal], [model(r) for r in test]
    target = TARGET_DEPOSIT * evaluate([0.0] * len(cal), cal, yc)["exposed"]
    eps = solve_eps(cal_scores, cal, yc, "exposed", target)
    rng = random.Random(RISK_MARGIN_SEED)
    samples = [[rng.randrange(len(cal)) for _ in cal] for _ in range(RISK_MARGIN_ROUNDS)]
    totals = [sum(cal[i]["value"] for i in s) for s in samples]

    def upper(e: float) -> float:
        d = budget_deposits(cal_scores, e)
        values = sorted(sum((1 - d[i]) * cal[i]["value"] * yc[i] for i in s) / t for s, t in zip(samples, totals))
        return values[int(0.95 * len(values)) - 1]

    lo, hi = 1e-6, eps
    for _ in range(25):
        mid = (lo * hi) ** 0.5
        lo, hi = (mid, hi) if upper(mid) <= target else (lo, mid)
    out = {"window": {"fit_until": fit_end, "calibrate": [cal_start, cal_end], "test": [test_start, test_end]},
           "label_rate": {"fit": sum(yf) / len(yf), "calibrate": sum(yc) / len(yc), "test": sum(yt) / len(yt)},
           "target_exposed": target, "uniform_50_exposed_on_test": TARGET_DEPOSIT * evaluate([0.0] * len(test), test, yt)["exposed"]}
    for name, e in (("plug_in", eps), ("conservative_p95", lo)):
        result = evaluate(budget_deposits(test_scores, e), test, yt)
        out[name] = {**result, "ratio_to_target": result["exposed"] / target}
    return out


def split(rows: Sequence[dict], name: str) -> tuple[list[dict], list[dict]]:
    """Ba cách chia theo thời gian. `primary`: học tới hết 2016, thử năm 2017 (theo ngày đến). `booked_after`: như trên nhưng tập thử
    chỉ giữ đơn đặt từ 01/01/2017, để không đơn thử nào được đặt trước khi kết thúc giai đoạn học. `earlier`: học tới 08/2016, thử 09/2016–08/2017."""
    if name == "primary":
        return [r for r in rows if r["ym"] <= 201612], [r for r in rows if r["ym"] >= 201701]
    if name == "booked_after":
        return [r for r in rows if r["ym"] <= 201612], [r for r in rows if r["ym"] >= 201701 and r["booked"] >= date(2017, 1, 1)]
    if name == "earlier":
        return [r for r in rows if r["ym"] <= 201608], [r for r in rows if 201609 <= r["ym"] <= 201708]
    raise ValueError(name)


def lookup_key(r: dict) -> tuple:
    return r["leadbin"], r["hotel"]


def tier_deposits(test: Sequence[dict]) -> list[float]:
    """Bảng luật kiểu Đồ án 2, mô phỏng theo số lần đặt đã hoàn tất (dữ liệu không có hạng thành viên)."""
    return [0.5 if r["po"] == 0 else 0.4 if r["po"] <= 2 else 0.3 if r["po"] <= 9 else 0.2 for r in test]


def run(rows: list[dict], total_rows: int) -> dict:
    canceled = [r for r in rows if r["status"] == "Canceled"]
    results: dict = {"dataset": {
        "rows_in_file": total_rows, "no_deposit_rows": len(rows), "canceled": len(canceled),
        "no_show": sum(1 for r in rows if r["status"] == "No-Show"),
        "late_or_no_show_7d": sum(label(r, 7) for r in rows),
        "canceled_share_within_days": {str(k): sum(1 for r in canceled if r["days_before"] <= k) / len(canceled) for k in (0, 1, 3, 7, 14, 30)},
    }}

    # 1. Phép thử gốc: nhãn "mọi đơn bị hủy"
    train, test = split(rows, "primary")
    ytr, ys = [label(r, None) for r in train], [label(r, None) for r in test]
    model = fit_logistic(train, ytr)
    scores = [model(r) for r in test]
    cred, k = fit_credibility(train, ytr)
    cred_scores = [cred(r) for r in test]
    repeat = [i for i, r in enumerate(test) if r["po"] >= 3]
    results["all_cancellations"] = {
        "train_rows": len(train), "test_rows": len(test), "label_rate_test": sum(ys) / len(ys),
        "repeat_guest_share_test": sum(1 for r in test if "repeat=1" in r["f"]) / len(test),
        "auc_model": auc(scores, ys), "auc_history_only": auc(cred_scores, ys), "credibility_k": k,
        "calibration_model": calibration(scores, ys),
        "uniform_50_exposed": 0.5 * evaluate([0.0] * len(test), test, ys)["exposed"],
        "tier_rule": fixed_rule(tier_deposits(test), test, ys),
        "history_only": compare(cred_scores, test, ys), "model": compare(scores, test, ys),
        "three_plus_completed": {"orders": len(repeat), "share": len(repeat) / len(test), "label_rate": sum(ys[i] for i in repeat) / len(repeat)},
    }

    # 2. Nhãn chặt theo cửa sổ K ngày
    results["strict_labels"] = {}
    for window in STRICT_WINDOWS:
        ytr, ys = [label(r, window) for r in train], [label(r, window) for r in test]
        model = fit_logistic(train, ytr)
        scores = [model(r) for r in test]
        results["strict_labels"][str(window)] = {
            "label_rate_train": sum(ytr) / len(ytr), "label_rate_test": sum(ys) / len(ys), "auc_model": auc(scores, ys),
            "calibration_model": calibration(scores, ys),
            "uniform_50_exposed": 0.5 * evaluate([0.0] * len(test), test, ys)["exposed"],
            "tier_rule": fixed_rule(tier_deposits(test), test, ys), "model": compare(scores, test, ys),
        }

    # 3. So với luật dùng đặc điểm của đơn, ở nhãn gốc và nhãn chặt 7 ngày
    results["rules_vs_model"] = {}
    for tag, window in (("all_cancellations", None), (f"strict_{MAIN_WINDOW}d", MAIN_WINDOW)):
        ytr, ys = [label(r, window) for r in train], [label(r, window) for r in test]
        one = fit_lookup(train, ytr, lambda r: r["leadbin"])
        two = fit_lookup(train, ytr, lookup_key)
        model = fit_logistic(train, ytr)
        results["rules_vs_model"][tag] = {
            "hand_rule_by_lead_time": fixed_rule([HAND_RULE[r["leadbin"]] for r in test], test, ys),
            "lookup_lead_time": compare([one(r) for r in test], test, ys),
            "lookup_lead_time_x_hotel": compare([two(r) for r in test], test, ys),
            "model": compare([model(r) for r in test], test, ys),
        }

    # 4. Độ nhạy (nhãn chặt 7 ngày): cách chia tập, làm tròn mức cọc lên bậc 20 %, khoảng tin cậy bootstrap
    sensitivity: dict = {"splits": {}}
    for name in ("primary", "booked_after", "earlier"):
        tr, te = split(rows, name)
        ytr, ys = [label(r, MAIN_WINDOW) for r in tr], [label(r, MAIN_WINDOW) for r in te]
        lookup, model = fit_lookup(tr, ytr, lookup_key), fit_logistic(tr, ytr)
        sensitivity["splits"][name] = {
            "train_rows": len(tr), "test_rows": len(te), "label_rate_test": sum(ys) / len(ys),
            "lookup_lead_time_x_hotel": compare([lookup(r) for r in te], te, ys)["same_deposit"],
            "model": compare([model(r) for r in te], te, ys)["same_deposit"],
        }
        if name == "primary":
            lookup_scores, model_scores, ys_main = [lookup(r) for r in te], [model(r) for r in te], ys
    sensitivity["rounded_to_20pct"] = {
        "lookup_lead_time_x_hotel": compare(lookup_scores, test, ys_main, ROUNDING_STEP)["same_deposit"],
        "model": compare(model_scores, test, ys_main, ROUNDING_STEP)["same_deposit"],
    }
    deposits = {
        name: budget_deposits(scores, solve_eps(scores, test, ys_main, "deposit", TARGET_DEPOSIT))
        for name, scores in (("model", model_scores), ("lookup_lead_time_x_hotel", lookup_scores))
    }
    sensitivity["bootstrap_95pct_reduction_at_same_deposit"] = bootstrap(test, ys_main, deposits)
    results["sensitivity_strict_7d"] = sensitivity

    # 5. Mức rủi ro chọn trên dữ liệu cũ có còn giữ trên dữ liệu sau không (nhãn chặt 7 ngày)
    results["risk_level_on_later_data"] = [risk_level_on_later_data(rows, w) for w in RISK_WINDOWS]
    return results


def rounded(value):
    if isinstance(value, float):
        return round(value, DIGITS)
    if isinstance(value, dict):
        return {k: rounded(v) for k, v in value.items()}
    if isinstance(value, list):
        return [rounded(v) for v in value]
    return value


def build_report(data_path: Path) -> dict:
    ensure_dataset(data_path)
    rows, total = load_rows(data_path)
    if total != EXPECTED_ROW_COUNT:
        raise ValueError(f"{data_path}: {total} dòng, cần {EXPECTED_ROW_COUNT}")
    return {
        "source": {"url": SOURCE_URL, "sha256": SOURCE_SHA256, "rows": total, "license": "CC BY 4.0 (António, de Almeida, Nunes 2019, Data in Brief 22)"},
        "parameters": {
            "sgd_seed": SGD_SEED, "sgd_epochs": SGD_EPOCHS, "sgd_learning_rate": SGD_LEARNING_RATE, "sgd_l2": SGD_L2,
            "lookup_prior_weight": LOOKUP_PRIOR_WEIGHT, "credibility_k_grid": list(CREDIBILITY_K_GRID), "target_deposit": TARGET_DEPOSIT,
            "strict_windows_days": list(STRICT_WINDOWS), "rounding_step": ROUNDING_STEP, "bootstrap_seed": BOOTSTRAP_SEED,
            "bootstrap_rounds": BOOTSTRAP_ROUNDS, "risk_margin_seed": RISK_MARGIN_SEED, "risk_margin_rounds": RISK_MARGIN_ROUNDS,
            "risk_windows": [list(w) for w in RISK_WINDOWS], "hand_rule_by_lead_bin": {str(k): v for k, v in HAND_RULE.items()},
            "primary_split": "train: arrival <= 2016-12; test: arrival 2017-01..2017-08",
        },
        "results": rounded(run(rows, total)),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--data", type=Path, default=DEFAULT_DATA)
    parser.add_argument("--out", type=Path, default=DEFAULT_RESULTS)
    parser.add_argument("--check", action="store_true", help="chạy lại và so với tệp kết quả đã có, không ghi đè")
    args = parser.parse_args(argv)
    sys.stdout.reconfigure(encoding="utf-8")
    report = build_report(args.data)
    text = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.check:
        saved = json.loads(args.out.read_text(encoding="utf-8"))
        if saved["results"] != report["results"] or saved["parameters"] != report["parameters"]:
            print(f"KHÁC với {args.out}", file=sys.stderr)
            return 1
        print(f"khớp {args.out}")
        return 0
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(text, encoding="utf-8", newline="\n")
    print(f"đã ghi {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
