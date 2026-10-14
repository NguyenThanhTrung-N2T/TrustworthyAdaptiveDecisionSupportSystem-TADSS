"""Ghép bản PRIMARY với bản SHADOW của cùng request cho PT-04 (gói 5.7b, spec 4.1–4.2).

Bản SHADOW có `decision_id` riêng nhưng mang cùng `correlation_id`, `reservation_id`, `request_hash`, `created_at` với PRIMARY đã sinh ra nó (INV-8).
Khóa ghép là `correlation_id` (bước (e) sinh UUID mới cho mỗi request); ba trường còn lại là bất biến phải khớp. Phạm vi lượt = tập `correlation_id`
mà bước (e) đã ghi; bản ghi ngoài tập chỉ được đếm. Mỗi PRIMARY thuộc đúng một trạng thái; cả hai bản fallback xếp `PRIMARY_FALLBACK`.
Chỉ `PAIRED` dùng để so hai strategy; fallback (100 %) không phải quyết định của strategy.
"""
from __future__ import annotations

from collections import Counter, defaultdict
from dataclasses import dataclass
from typing import Any, Iterable

PAIRED = "PAIRED"
PRIMARY_FALLBACK = "PRIMARY_FALLBACK"
SHADOW_FALLBACK = "SHADOW_FALLBACK"
UNPAIRED = "UNPAIRED"
STATES = (PAIRED, PRIMARY_FALLBACK, SHADOW_FALLBACK, UNPAIRED)
DUPLICATE_PRIMARY = "DUPLICATE_PRIMARY"
MULTIPLE_SHADOW = "MULTIPLE_SHADOW"
ORPHAN_SHADOW = "ORPHAN_SHADOW"
INVARIANT_MISMATCH = "INVARIANT_MISMATCH"
INVARIANT_FIELDS = ("reservation_hash", "request_hash", "created_at_utc")
MAX_SAMPLES = 20


def _flag(value: Any) -> bool:
    return value is True or str(value).strip().lower() == "true"


@dataclass(frozen=True)
class Pairing:
    """`states`: correlation_id → trạng thái (chỉ PRIMARY trong lượt); `primaries`/`shadows`: dòng D1 theo correlation_id; `outside_run`: số dòng ngoài lượt;
    `violations`: đếm theo loại; `samples`: tối đa `MAX_SAMPLES` mẫu dạng `LOẠI:correlation_id`."""

    states: dict[str, str]
    primaries: dict[str, dict[str, Any]]
    shadows: dict[str, dict[str, Any]]
    outside_run: int
    violations: dict[str, int]
    samples: list[str]


def pair(d1_rows: Iterable[dict[str, Any]], run_correlations: set[str]) -> Pairing:
    primaries: dict[str, dict[str, Any]] = {}
    shadow_lists: dict[str, list[dict[str, Any]]] = defaultdict(list)
    violations: Counter = Counter()
    samples: list[str] = []
    outside = 0

    def violate(kind: str, corr: str) -> None:
        violations[kind] += 1
        if len(samples) < MAX_SAMPLES:
            samples.append(f"{kind}:{corr}")

    for row in d1_rows:
        corr = str(row.get("correlation_id") or "")
        if corr not in run_correlations:
            outside += 1
            continue
        if row.get("role") == "PRIMARY":
            if corr in primaries:
                violate(DUPLICATE_PRIMARY, corr)
            else:
                primaries[corr] = row
        elif row.get("role") == "SHADOW":
            shadow_lists[corr].append(row)

    shadows: dict[str, dict[str, Any]] = {}
    for corr, items in shadow_lists.items():
        if corr not in primaries:
            violate(ORPHAN_SHADOW, corr)
            continue
        if len(items) > 1:
            violate(MULTIPLE_SHADOW, corr)
        shadows[corr] = items[0]

    states: dict[str, str] = {}
    for corr, primary in primaries.items():
        shadow = shadows.get(corr)
        if shadow is None:
            states[corr] = UNPAIRED
            continue
        if any(str(primary.get(field, "")) != str(shadow.get(field, "")) for field in INVARIANT_FIELDS):
            violate(INVARIANT_MISMATCH, corr)
        if _flag(primary.get("fallback_used")):
            states[corr] = PRIMARY_FALLBACK
        elif _flag(shadow.get("fallback_used")):
            states[corr] = SHADOW_FALLBACK
        else:
            states[corr] = PAIRED
    return Pairing(states, primaries, shadows, outside, dict(violations), samples)


def state_counts(pairing: Pairing) -> dict[str, int]:
    counts = Counter(pairing.states.values())
    return {state: counts.get(state, 0) for state in STATES}


def attach(d5_rows: list[dict[str, Any]], correlations: list[str | None], pairing: Pairing) -> None:
    """Điền 5 cột shadow của D5 (cùng thứ tự với `correlations`). Request không có PRIMARY trong lượt giữ ô rỗng; thiếu SHADOW thì cột của SHADOW rỗng, không phải 0."""
    for row, corr in zip(d5_rows, correlations):
        state = pairing.states.get(corr or "")
        if state is None:
            continue
        row["shadow_pair_state"] = state
        row["primary_fallback"] = str(_flag(pairing.primaries[corr].get("fallback_used")))
        shadow = pairing.shadows.get(corr)
        if shadow is not None:
            row["shadow_status"] = shadow.get("status", "")
            row["shadow_deposit_rate"] = shadow.get("deposit_rate", "")
            row["shadow_deposit_amount"] = shadow.get("deposit_amount", "")
