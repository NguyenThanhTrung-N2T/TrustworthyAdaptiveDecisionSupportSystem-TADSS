"""Test cho deposit_pretest.py trên dữ liệu nhỏ dựng tay (không cần tải bộ dữ liệu khách sạn)."""
from __future__ import annotations

import hashlib
import json
from datetime import date
from pathlib import Path

import pytest

import deposit_pretest as dp


def _row(value: float = 100.0, status: str = "Check-Out", days_before: int | None = None, ym: int = 201701, leadbin: int = 0,
         po: int = 0, features: list[str] | None = None) -> dict:
    return {"ym": ym, "booked": date(2017, 1, 1), "canceled": int(status == "Canceled"), "status": status, "days_before": days_before,
            "value": value, "pc": 0, "po": po, "leadbin": leadbin, "hotel": "City Hotel", "f": features or ["a=1"]}


def test_label_allCancellations_countsEveryCancellation():
    assert dp.label(_row(status="Canceled", days_before=40), None) == 1
    assert dp.label(_row(status="No-Show"), None) == 0


@pytest.mark.parametrize(("status", "days", "expected"), [
    ("No-Show", None, 1), ("Canceled", 7, 1), ("Canceled", 0, 1), ("Canceled", 8, 0), ("Check-Out", None, 0),
])
def test_label_strictWindow_onlyNoShowOrLateCancellation(status, days, expected):
    assert dp.label(_row(status=status, days_before=days), 7) == expected


def test_budgetDeposits_followsFormulaAndClamps():
    assert dp.budget_deposits([0.02, 0.025, 0.05, 0.30], 0.02) == pytest.approx([0.0, 0.2, 0.6, 1 - 0.02 / 0.30])
    assert dp.budget_deposits([0.0], 0.02) == [0.0]


def test_budgetDeposits_withStep_roundsUp():
    assert dp.budget_deposits([0.025, 0.05, 0.30], 0.02, 0.2) == pytest.approx([0.2, 0.6, 1.0])


def test_evaluate_weightsByValue():
    test = [_row(value=100), _row(value=300)]
    result = dp.evaluate([1.0, 0.0], test, [0, 1])
    assert result["deposit"] == pytest.approx(0.25)
    assert result["exposed"] == pytest.approx(0.75)
    assert result["low_share"] == pytest.approx(0.5)


def test_solveEps_hitsTargetDepositWithoutLabels():
    test = [_row() for _ in range(4)]
    scores = [0.01, 0.05, 0.2, 0.6]
    eps = dp.solve_eps(scores, test, [0, 0, 0, 0], "deposit", 0.5)
    assert dp.evaluate(dp.budget_deposits(scores, eps), test, [0, 0, 0, 0])["deposit"] == pytest.approx(0.5, abs=1e-6)


def test_compare_perfectScoresBeatUniform():
    test = [_row() for _ in range(10)]
    ys = [1, 1] + [0] * 8
    scores = [0.9, 0.9] + [0.01] * 8
    assert dp.compare(scores, test, ys)["same_deposit"]["reduction_vs_uniform"] > 0.9


def test_fixedRule_uniformDeposit_hasZeroReduction():
    test = [_row() for _ in range(4)]
    assert dp.fixed_rule([0.5] * 4, test, [1, 0, 0, 1])["reduction_vs_uniform"] == pytest.approx(0.0)


def test_fitLookup_shrinksSmallGroupsTowardBase():
    train = [_row(leadbin=0) for _ in range(10)] + [_row(leadbin=1)]
    ys = [0] * 10 + [1]
    table = dp.fit_lookup(train, ys, lambda r: r["leadbin"])
    base = 1 / 11
    assert table(_row(leadbin=1)) == pytest.approx((1 + dp.LOOKUP_PRIOR_WEIGHT * base) / (1 + dp.LOOKUP_PRIOR_WEIGHT))
    assert table(_row(leadbin=3)) == pytest.approx(base)


def test_fitLogistic_isDeterministicAndRanksRiskyGroupHigher():
    train = [_row(features=["g=risky"]) for _ in range(50)] + [_row(features=["g=safe"]) for _ in range(50)]
    ys = [1] * 30 + [0] * 20 + [1] * 5 + [0] * 45
    first, second = dp.fit_logistic(train, ys), dp.fit_logistic(train, ys)
    risky, safe = _row(features=["g=risky"]), _row(features=["g=safe"])
    assert first(risky) == second(risky)
    assert first(risky) > first(safe)


def test_auc_knownCases():
    assert dp.auc([0.1, 0.2, 0.8, 0.9], [0, 0, 1, 1]) == pytest.approx(1.0)
    assert dp.auc([0.5, 0.5, 0.5, 0.5], [0, 1, 0, 1]) == pytest.approx(0.5)


def test_ensureDataset_wrongChecksum_refusesFile(tmp_path: Path):
    path = tmp_path / "hotels.csv"
    path.write_bytes(b"x")
    with pytest.raises(ValueError, match="SHA-256"):
        dp.ensure_dataset(path, expected_sha256="0" * 64)


def test_ensureDataset_matchingChecksum_accepts(tmp_path: Path):
    path = tmp_path / "hotels.csv"
    path.write_bytes(b"x")
    dp.ensure_dataset(path, expected_sha256=hashlib.sha256(b"x").hexdigest())


def test_savedResults_matchCurrentParameters():
    saved = json.loads(dp.DEFAULT_RESULTS.read_text(encoding="utf-8"))
    assert saved["source"]["sha256"] == dp.SOURCE_SHA256
    assert saved["parameters"]["sgd_seed"] == dp.SGD_SEED
    assert saved["parameters"]["bootstrap_rounds"] == dp.BOOTSTRAP_ROUNDS
    assert saved["parameters"]["strict_windows_days"] == list(dp.STRICT_WINDOWS)
