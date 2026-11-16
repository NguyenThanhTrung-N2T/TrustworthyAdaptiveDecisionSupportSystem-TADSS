"""Test cho runtime_facts.py trên vài dòng dựng tay (không cần bộ minh chứng)."""
from __future__ import annotations

import runtime_facts as rf


def _row(execution_type: str = "PRIMARY", reason: str = "TIER_DEPOSIT", ms: int = 3, cache: str = "false") -> dict:
    return {"execution_type": execution_type, "reason_code": reason, "execution_time_ms": str(ms), "cache_hit": cache}


def test_summarize_countsFallbackByReasonWithMedian():
    rows = [_row(), _row("FALLBACK", "FALLBACK_STRATEGY_TIMEOUT", 400), _row("FALLBACK", "FALLBACK_STRATEGY_TIMEOUT", 420),
            _row("FALLBACK", "FALLBACK_STRATEGY_TIMEOUT", 900), _row("FALLBACK", "FALLBACK_CIRCUIT_OPEN", 0)]
    result = rf.summarize(rows)
    assert result["decisions"] == 5
    assert result["fallback_total"] == 4
    assert result["fallback_by_reason"]["FALLBACK_STRATEGY_TIMEOUT"] == {"count": 3, "median_ms": 420}
    assert list(result["fallback_by_reason"]) == ["FALLBACK_STRATEGY_TIMEOUT", "FALLBACK_CIRCUIT_OPEN"]


def test_summarize_countsCacheHits():
    assert rf.summarize([_row(cache="true"), _row(cache="False"), _row(cache="t")])["cache_hits"] == 2
