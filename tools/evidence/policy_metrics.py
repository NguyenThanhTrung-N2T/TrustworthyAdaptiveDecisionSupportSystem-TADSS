"""Công thức thuần của mô phỏng kinh tế PT-03 (gói 5.7b, spec 4.3): phần cọc giữ lại `φ` theo hai biến thể hoàn tiền, hoàn vượt `AS_IS_BE`,
AUC của mức cọc được phân bổ đối với nhãn hủy (Mann–Whitney, ties tính 1/2), `Δr`, percentile cho khoảng tin cậy bootstrap.

`AS_IS_BE` (mã BE hiện tại hoàn `pct` trên `FinalTotal`, không chặn trên) KHÔNG có `φ`: chỉ đo phần hoàn vượt số cọc đã thu (B-1). AUC không xác định
(một lớp nhãn, hoặc điểm hằng) trả `None` kèm lý do, không bao giờ trả 0,5 mặc định.
"""
from __future__ import annotations

import math
from collections import Counter
from dataclasses import dataclass
from typing import Iterable, Mapping, Sequence

REFUND_OF_PAID = "REFUND_OF_PAID"
REFUND_OF_TOTAL_CAPPED = "REFUND_OF_TOTAL_CAPPED"
VARIANTS = (REFUND_OF_PAID, REFUND_OF_TOTAL_CAPPED)
ONE_CLASS = "ONE_CLASS"
CONSTANT_SCORE = "CONSTANT_SCORE"


def _check_unit(name: str, value: float) -> None:
    if not 0.0 <= value <= 1.0 or math.isnan(value):
        raise ValueError(f"{name} phải trong [0, 1], nhận {value}")


def _validate_pairs(scores: Sequence[float], labels: Sequence[int]) -> None:
    """Đầu vào lỗi phải báo lỗi, không đổi thành số mặc định: lệch độ dài, điểm thiếu/NaN, nhãn ngoài {0, 1}."""
    if len(scores) != len(labels):
        raise ValueError(f"scores và labels lệch độ dài: {len(scores)} != {len(labels)}")
    for index, score in enumerate(scores):
        if score is None or math.isnan(score):
            raise ValueError(f"điểm thiếu hoặc NaN tại vị trí {index}: {score!r}")
    for index, label in enumerate(labels):
        if label not in (0, 1):
            raise ValueError(f"nhãn phải là 0 hoặc 1, nhận {label!r} tại vị trí {index}")


def retained_fraction(variant: str, r: float, pct: float) -> float:
    """`φ`: phần cọc đã thu bị giữ lại khi hủy với tỷ lệ hoàn `pct`. `REFUND_OF_PAID`: `1 − pct`.
    `REFUND_OF_TOTAL_CAPPED`: `0` khi `r = 0`, ngược lại `max(0, r − pct) / r` (hoàn `min(r·P, pct·P)`)."""
    _check_unit("r", r)
    _check_unit("pct", pct)
    if variant == REFUND_OF_PAID:
        return 1.0 - pct
    if variant == REFUND_OF_TOTAL_CAPPED:
        return 0.0 if r == 0 else max(0.0, r - pct) / r
    raise ValueError(f"biến thể hoàn tiền không hợp lệ: {variant!r} (chỉ {', '.join(VARIANTS)})")


def over_refund(r: float, pct: float, price: float) -> float:
    """`AS_IS_BE`: phần hoàn vượt số cọc đã thu `max(0, pct·P − r·P)`; > 0 nghĩa là BE sẽ hoàn nhiều hơn số đã thu nếu giữ nguyên mã."""
    return max(0.0, (pct - r) * price)


@dataclass(frozen=True)
class Auc:
    """AUC hoặc lý do không xác định."""

    value: float | None
    reason: str | None


def auc_from_counts(positives: Mapping[float, int], negatives: Mapping[float, int]) -> float:
    """P(điểm của đơn nhãn 1 > điểm đơn nhãn 0) + ½·P(bằng nhau), tính trên bảng đếm theo mức điểm (điểm rời rạc nên O(số mức))."""
    total_pos, total_neg = sum(positives.values()), sum(negatives.values())
    if total_pos <= 0 or total_neg <= 0:
        raise ValueError("AUC cần ít nhất một đơn ở mỗi lớp nhãn")
    wins, below = 0.0, 0
    for score in sorted(set(positives) | set(negatives)):
        pos, neg = positives.get(score, 0), negatives.get(score, 0)
        wins += pos * below + 0.5 * pos * neg
        below += neg
    return wins / (total_pos * total_neg)


def auc(scores: Sequence[float], labels: Sequence[int]) -> Auc:
    """AUC của `scores` đối với nhãn `labels` (1 = hủy); `Auc(None, lý do)` khi không xác định. Đầu vào lỗi → `ValueError`."""
    _validate_pairs(scores, labels)
    positives = Counter(score for score, label in zip(scores, labels) if label == 1)
    negatives = Counter(score for score, label in zip(scores, labels) if label != 1)
    if not positives or not negatives:
        return Auc(None, ONE_CLASS)
    if len(set(positives) | set(negatives)) == 1:
        return Auc(None, CONSTANT_SCORE)
    return Auc(auc_from_counts(positives, negatives), None)


def delta_r(scores: Sequence[float], labels: Sequence[int]) -> float | None:
    """`mean(r | c=1) − mean(r | c=0)`; None khi thiếu một lớp. Đầu vào lỗi → `ValueError`."""
    _validate_pairs(scores, labels)
    positive = [score for score, label in zip(scores, labels) if label == 1]
    negative = [score for score, label in zip(scores, labels) if label != 1]
    if not positive or not negative:
        return None
    return math.fsum(positive) / len(positive) - math.fsum(negative) / len(negative)


def percentile(sorted_values: Sequence[float], q: float) -> float | None:
    """Percentile nội suy tuyến tính trên dãy ĐÃ SẮP; None khi rỗng; `q` ngoài [0, 1] → `ValueError`."""
    if math.isnan(q) or not 0.0 <= q <= 1.0:
        raise ValueError(f"q phải trong [0, 1], nhận {q}")
    if not sorted_values:
        return None
    position = q * (len(sorted_values) - 1)
    lower, upper = math.floor(position), math.ceil(position)
    return sorted_values[lower] + (sorted_values[upper] - sorted_values[lower]) * (position - lower)


def ci95(values: Iterable[float]) -> dict[str, float | int | None]:
    """Khoảng tin cậy 95% theo percentile 2,5 / 97,5 của các giá trị bootstrap."""
    ordered = sorted(values)
    return {"low": percentile(ordered, 0.025), "high": percentile(ordered, 0.975), "samples": len(ordered)}
