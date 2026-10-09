"""Quy tắc đạt/không đạt từng pha LT-03 và chaos_log (D3)."""
import csv
from datetime import datetime, timedelta, timezone
from pathlib import Path

from evaluator import chaos_rules as rules
from evaluator.chaos_log import CHAOS_LOG_COLUMNS, ChaosRow, write_chaos_log
from evaluator.chaos_schedule import KIND_STRATEGY_DELAY, KIND_STRATEGY_THROW
from evaluator.clients.postgres import FallbackStats

T0 = datetime(2026, 10, 9, 1, 0, tzinfo=timezone.utc)
GOOD_STATS = FallbackStats(count=30, max_ms=503 - 10, over_budget=0)


def _entry(second: float, status: int = 200, execution: str = "FALLBACK", reason: str = "FALLBACK_STRATEGY_TIMEOUT", duration: float = 520.0, key: str = "k", **extra) -> dict:
    entry = {
        "ts": (T0 + timedelta(seconds=second)).isoformat().replace("+00:00", "Z"), "http_status": status, "decision_status": "APPROVED" if status == 200 else None,
        "execution_type": execution if status == 200 else None, "reason_code": reason if status == 200 else None, "deposit_rate": 1.0 if status == 200 else None,
        "duration_ms": duration, "idempotency_key": key,
    }
    entry.update(extra)
    return entry


def test_strategy_fault_passes_when_every_response_is_correct_fallback() -> None:
    entries = [_entry(i, key=f"k{i}") for i in range(20)]

    verdict = rules.evaluate_strategy_fault(KIND_STRATEGY_DELAY, entries, GOOD_STATS)

    assert verdict.passed, verdict.observed


def test_strategy_fault_fails_on_non_fallback_or_wrong_reason_or_low_deposit() -> None:
    base = [_entry(i, key=f"k{i}") for i in range(20)]
    for broken in (
        _entry(30, execution="AUTOMATED"),
        _entry(30, reason="FALLBACK_STRATEGY_EXCEPTION"),  # lý do của FI-03 trong pha FI-02
        _entry(30, deposit_rate=0.3),
        _entry(30, status=503),
    ):
        assert not rules.evaluate_strategy_fault(KIND_STRATEGY_DELAY, base + [broken], GOOD_STATS).passed


def test_strategy_fault_accepts_circuit_open_and_ignores_cache_hits() -> None:
    entries = [_entry(i, reason="FALLBACK_CIRCUIT_OPEN", key=f"k{i}") for i in range(10)] + [_entry(50, execution="CACHE_HIT", reason="X")]

    assert rules.evaluate_strategy_fault(KIND_STRATEGY_THROW, entries, GOOD_STATS).passed, "circuit_open hợp lệ ở cả hai pha; CACHE_HIT bị bỏ qua"
    timeout_in_throw = [_entry(60, reason="FALLBACK_STRATEGY_TIMEOUT")]
    assert not rules.evaluate_strategy_fault(KIND_STRATEGY_THROW, entries + timeout_in_throw, GOOD_STATS).passed, "lý do timeout không thuộc pha Throw"


def test_strategy_fault_fails_on_pipeline_over_budget_or_slow_share() -> None:
    entries = [_entry(i, key=f"k{i}") for i in range(20)]

    assert not rules.evaluate_strategy_fault(KIND_STRATEGY_DELAY, entries, FallbackStats(30, 640, 2)).passed
    assert not rules.evaluate_strategy_fault(KIND_STRATEGY_DELAY, entries, FallbackStats(0, 0, 0)).passed, "không có bản ghi FALLBACK nào trong audit"
    slow = [_entry(i, duration=900.0, key=f"s{i}") for i in range(3)] + entries
    assert not rules.evaluate_strategy_fault(KIND_STRATEGY_DELAY, slow, GOOD_STATS).passed, "13 % chậm hơn 700 ms"


def test_postgres_fault_rules() -> None:
    errors = [_entry(i, status=503, error_code="PERSISTENCE_UNAVAILABLE", key=f"k{i}") for i in range(10)]

    assert rules.evaluate_postgres_fault(errors, {}).passed
    assert not rules.evaluate_postgres_fault(errors + [_entry(20)], {}).passed, "một response 200 lẫn trong pha"
    assert not rules.evaluate_postgres_fault(errors, {"k3": 1}).passed, "khóa bị 503 nhưng đã có bản ghi dở dang"
    assert not rules.evaluate_postgres_fault([_entry(0, status=503, error_code="OTHER")], {}).passed


def test_redis_fault_rules() -> None:
    clean = [_entry(i, execution="AUTOMATED", key=f"k{i}") for i in range(10)]

    assert rules.evaluate_redis_fault(clean, 12.0).passed
    assert not rules.evaluate_redis_fault(clean, 0.0).passed, "bypass phải tăng"
    assert not rules.evaluate_redis_fault(clean + [_entry(20)], 12.0).passed, "FALLBACK do cache là lỗi"


def test_recovery_from_events() -> None:
    start, end = T0, T0 + timedelta(seconds=60)
    late_bad = [T0 + timedelta(seconds=s) for s in (1, 5, 20, 22)]

    ok, seconds = rules.recovery_from_events(start, end, late_bad, 45)
    assert (ok, seconds) == (True, 15.0), "khoảng trống đầu tiên đủ 10 s là 5 s → 20 s; cửa sổ sạch kết thúc ở 15 s"
    ok, seconds = rules.recovery_from_events(start, end, late_bad, 12)
    assert (ok, seconds) == (False, 15.0), "quá hạn chót"
    ok, seconds = rules.recovery_from_events(start, end, [T0 + timedelta(seconds=s) for s in (1, 5, 20, 28, 35)], 45)
    assert (ok, seconds) == (True, 15.0)
    sparse = [T0 + timedelta(seconds=s) for s in (1, 5, 14, 22, 30, 38)]
    assert rules.recovery_from_events(start, end, sparse, 50) == (True, 48.0), "khoảng trống đủ 10 s đầu tiên bắt đầu ở 38 s"
    assert rules.recovery_from_events(start, end, sparse, 45) == (False, 48.0)
    ok, seconds = rules.recovery_from_events(start, end, [], 45)
    assert (ok, seconds) == (True, 10.0)
    assert rules.recovery_from_events(start, end, [T0 + timedelta(seconds=s) for s in range(0, 60, 5)], 45) == (False, None), "không bao giờ có 10 s sạch"


def test_recovery_from_samples_ignores_nan_and_earlier_samples() -> None:
    nan = float("nan")
    samples = [(99.0, 0.0), (100.0, 5.0), (105.0, nan), (110.0, 3.0), (115.0, 0.0), (120.0, 0.0)]

    assert rules.recovery_from_samples(100.0, samples, 20) == (True, 15.0)
    assert rules.recovery_from_samples(100.0, samples, 10) == (False, 15.0)
    assert rules.recovery_from_samples(100.0, [(100.0, 4.0)], 20) == (False, None)


def test_invariant_violations_counts_duplicates_missing_and_ghosts() -> None:
    entries = [
        _entry(0, key="dup"), _entry(1, key="ok"), _entry(2, key="missing"),
        _entry(3, status=503, key="ghost", error_code="PERSISTENCE_UNAVAILABLE"), _entry(4, status=503, key="clean", error_code="PERSISTENCE_UNAVAILABLE"),
    ]

    assert rules.invariant_violations(entries, {"dup": 2, "ok": 1, "ghost": 1}) == 3


def test_uncertain_commit_right_after_cut_is_not_a_violation_but_a_later_one_is() -> None:
    cut = T0 + timedelta(seconds=10)
    early = _entry(10.1, status=503, key="early", error_code="PERSISTENCE_UNAVAILABLE")
    late = _entry(14, status=503, key="late", error_code="PERSISTENCE_UNAVAILABLE")
    counts = {"early": 1, "late": 1}
    until = cut + timedelta(seconds=rules.UNCERTAIN_COMMIT_GRACE_SECONDS)

    assert rules.invariant_violations([early], counts, until) == 0
    assert rules.uncertain_commits([early], counts, until) == 1
    assert rules.invariant_violations([late], counts, until) == 1, "sau khi PostgreSQL đã cắt hẳn thì 503 mà có bản ghi là vi phạm thật"
    assert rules.uncertain_commits([late], counts, until) == 0
    assert rules.invariant_violations([early], counts) == 1, "không khai báo thời hạn thì mọi 503 có bản ghi là vi phạm"


def test_chaos_log_has_d3_columns(tmp_path: Path) -> None:
    path = tmp_path / "chaos_log.csv"

    write_chaos_log([ChaosRow("FI-02", "EV-x", "Delay", "2026-10-09T01:00:00.000000Z", "2026-10-09T01:02:00.000000Z", "e", "o", True, 0)], path)

    rows = list(csv.DictReader(path.open(encoding="utf-8")))
    assert tuple(rows[0]) == CHAOS_LOG_COLUMNS
    assert rows[0]["passed"] == "True" and rows[0]["invariant_violations"] == "0"
