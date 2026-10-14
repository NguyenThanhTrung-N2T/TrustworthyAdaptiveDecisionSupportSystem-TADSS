"""Xả hàng đợi shadow và đối soát tổng hợp PT-04 (spec 4.2): delta counter theo lượt, phát hiện reset, nhãn RECONCILED/UNKNOWN/FAILED, hết hạn có residual."""
from evaluator import shadow_drain as sd


class FakeClock:
    def __init__(self) -> None:
        self.now = 0.0

    def monotonic(self) -> float:
        return self.now

    def sleep(self, seconds: float) -> None:
        self.now += seconds


def run(sequence: list[tuple[dict[str, int], sd.Counters]], before: sd.Counters) -> dict:
    clock, state = FakeClock(), {"i": 0}

    def step() -> tuple[dict[str, int], sd.Counters]:
        item = sequence[min(state["i"], len(sequence) - 1)]
        return item

    def roles() -> dict[str, int]:
        return step()[0]

    def read() -> sd.Counters:
        value = step()[1]
        state["i"] += 1
        return value

    return sd.drain(roles, read, before, monotonic=clock.monotonic, sleep=clock.sleep)


def test_reconciled_when_equation_and_recorded_match() -> None:
    before = sd.Counters(5, 1, 0)
    result = run([({"PRIMARY": 10, "SHADOW": 7}, sd.Counters(12, 1, 0)), ({"PRIMARY": 10, "SHADOW": 9}, sd.Counters(14, 2, 0))], before)
    assert result["label"] == sd.RECONCILED and result["delta"] == {"recorded": 9, "failed": 1, "dropped": 0} and result["residual"] == 0
    assert result["statement"] == sd.AGGREGATE_STATEMENT


def test_absent_failed_and_dropped_counters_count_as_zero_when_recorded_present() -> None:
    result = run([({"PRIMARY": 3, "SHADOW": 3}, sd.Counters(3, None, None))], sd.Counters(None, None, None))
    assert result["label"] == sd.RECONCILED


def test_counter_reset_is_unknown() -> None:
    result = run([({"PRIMARY": 3, "SHADOW": 3}, sd.Counters(2, 0, 0))], sd.Counters(10, 0, 0))
    assert result["label"] == sd.UNKNOWN and "reset" in result["reason"]


def test_stall_times_out_with_residual_unknown() -> None:
    result = run([({"PRIMARY": 10, "SHADOW": 6}, sd.Counters(6, 0, 1))], sd.Counters(0, 0, 0))
    assert result["label"] == sd.UNKNOWN and result["residual"] == 3 and result["seconds"] >= sd.STALL_SECONDS


def test_recorded_differs_from_db_after_stall_is_failed() -> None:
    result = run([({"PRIMARY": 4, "SHADOW": 4}, sd.Counters(3, 0, 0))], sd.Counters(0, 0, 0))
    assert result["label"] == sd.FAILED and result["reason"] == "counter_mismatch"


def test_prometheus_unavailable_is_unknown() -> None:
    result = run([({"PRIMARY": 4, "SHADOW": 4}, sd.Counters(None, None, None))], sd.Counters(None, None, None))
    assert result["label"] == sd.UNKNOWN


def test_baseline_read_error_is_unknown_even_when_after_has_numbers() -> None:
    """Baseline đọc lỗi (≠ series vắng = 0) thì hiệu không tin được: UNKNOWN/baseline_unavailable, không suy ra RECONCILED."""
    result = run([({"PRIMARY": 3, "SHADOW": 3}, sd.Counters(3, 0, 0))], sd.Counters(None, None, None, error=True))
    assert result["label"] == sd.UNKNOWN and result["reason"] == "baseline_unavailable" and result["delta"] is None
    assert result["before"] == {"recorded": None, "failed": None, "dropped": None, "error": True}


def test_after_read_error_is_prometheus_unavailable() -> None:
    result = run([({"PRIMARY": 3, "SHADOW": 3}, sd.Counters(None, None, None, error=True))], sd.Counters(0, 0, 0))
    assert result["label"] == sd.UNKNOWN and result["reason"] == "prometheus_unavailable"
    assert result["after"]["error"] is True


def test_counters_to_dict_omits_error_unless_true() -> None:
    assert sd.Counters(1, 2, 3).to_dict() == {"recorded": 1, "failed": 2, "dropped": 3}
    assert sd.Counters(None, None, None, error=True).to_dict()["error"] is True


def test_read_counters_parses_sum_results_and_missing_series() -> None:
    answers = {sd.RECORDED_QUERY: [{"value": [0, "12"]}], sd.FAILED_QUERY: [], sd.DROPPED_QUERY: [{"value": [0, "1"]}]}
    assert sd.read_counters(lambda expr: answers[expr]) == sd.Counters(12.0, None, 1.0)


def test_counter_lag_with_stable_db_count_is_progress_not_stall() -> None:
    """DB đã đủ (SHADOW=PRIMARY=4) nhưng counter export chậm: mỗi lần counter nhích là có tiến triển, tổng thời gian vượt STALL_SECONDS vẫn RECONCILED."""
    roles = {"PRIMARY": 4, "SHADOW": 4}
    sequence = [(roles, sd.Counters(value, 0, 0)) for value in [1] * 10 + [2] * 10 + [3] * 10 + [4]]
    result = run(sequence, sd.Counters(0, 0, 0))
    assert result["label"] == sd.RECONCILED and result["seconds"] > sd.STALL_SECONDS
