"""Quy tắc kết luận KPI SC1…SC8 ở ngưỡng và ngoài phạm vi."""
import pytest

from evidence import kpi


def _run(name: str = "lt01", share: float | None = 0.97, p95: float = 120.0, samples: int = 5000) -> dict:
    return {"name": name, "server_share_le_150": share, "client_p95_ms": p95, "samples": samples}


@pytest.mark.parametrize(
    "share,p95,expected",
    [(0.95, 149.9, kpi.PASS), (0.9499, 100.0, kpi.FAIL), (0.99, 150.0, kpi.FAIL), (0.96, 149.99, kpi.PASS)],
)
def test_sc1_thresholds(share: float, p95: float, expected: str) -> None:
    assert kpi.sc1([_run(share=share, p95=p95)]).verdict == expected


def test_sc1_requires_every_repetition_and_handles_missing_data() -> None:
    assert kpi.sc1([_run("lt01-r1"), _run("lt01-r2", share=0.94), _run("lt01-r3")]).verdict == kpi.FAIL
    assert kpi.sc1([_run("lt01-r1"), _run("lt01-r2", share=None)]).verdict == kpi.INSUFFICIENT_DATA
    assert kpi.sc1([]).verdict == kpi.INSUFFICIENT_DATA


def test_sc1_reports_bootstrap_interval_only_with_repetitions() -> None:
    single = kpi.sc1([_run()])
    triple = kpi.sc1([_run("r1", p95=100.0), _run("r2", p95=110.0), _run("r3", p95=120.0)])

    assert "không có khoảng tin cậy" in single.reason
    assert "KTC 95 % bootstrap" in triple.reason
    low, high = kpi.bootstrap_interval([100.0, 110.0, 120.0])
    assert 100.0 <= low <= 110.0 <= high <= 120.0
    assert kpi.bootstrap_interval([100.0, 110.0, 120.0]) == (low, high), "tất định theo seed cố định"


def test_percentile_nearest_rank() -> None:
    values = [float(i) for i in range(1, 101)]

    assert kpi.percentile(values, 0.95) == 95.0 and kpi.percentile(values, 0.5) == 50.0 and kpi.percentile([7.0], 0.95) == 7.0


def _summary(**codes: tuple[int, int]) -> dict:
    return {code.replace("_", "-"): {"passed": passed, "failed": failed, "skipped": 0} for code, (passed, failed) in codes.items()}


def _chaos(fi02: bool = True, fi03: bool = True, violations: int = 0) -> list[dict]:
    return [
        {"fi_id": "FI-02", "passed": str(fi02), "invariant_violations": str(violations)},
        {"fi_id": "FI-03", "passed": str(fi03), "invariant_violations": "0"},
        {"fi_id": "FI-08", "passed": "True", "invariant_violations": "0"},
    ]


def test_sc2() -> None:
    ok = _summary(FI_02=(3, 0), FI_03=(3, 0), FI_04=(2, 0))

    assert kpi.sc2(_chaos(), ok).verdict == kpi.PASS
    assert kpi.sc2(_chaos(fi03=False), ok).verdict == kpi.FAIL
    assert kpi.sc2(_chaos(), _summary(FI_02=(3, 1), FI_03=(3, 0), FI_04=(2, 0))).verdict == kpi.FAIL
    assert kpi.sc2(_chaos(), _summary(FI_02=(3, 0), FI_03=(3, 0))).verdict == kpi.FAIL, "FI-04 chưa có kết quả"
    assert kpi.sc2([], ok).verdict == kpi.INSUFFICIENT_DATA and kpi.sc2(_chaos(), None).verdict == kpi.INSUFFICIENT_DATA


def test_sc3_and_sc4() -> None:
    assert kpi.sc3(True, "ok", _summary(IT_05=(4, 0))).verdict == kpi.PASS
    assert kpi.sc3(False, "lệch", _summary(IT_05=(4, 0))).verdict == kpi.FAIL
    assert kpi.sc3(True, "ok", _summary(IT_05=(3, 1))).verdict == kpi.FAIL
    assert kpi.sc3(None, "", None).verdict == kpi.INSUFFICIENT_DATA
    ok = _summary(IT_04=(2, 0), IT_07=(2, 0), SCALE_1=(1, 0))
    assert kpi.sc4(ok, True, _chaos()).verdict == kpi.PASS
    assert kpi.sc4(ok, False, _chaos()).verdict == kpi.FAIL
    assert kpi.sc4(ok, True, _chaos(violations=1)).verdict == kpi.FAIL
    assert kpi.sc4(ok, None, _chaos()).verdict == kpi.INSUFFICIENT_DATA


def test_sc8_needs_mutation_results() -> None:
    passing = _summary(UT_01=(10, 0))
    mutation = {"MT-01": {"score": 80.0}, "MT-02": {"score": 70.0}, "MT-03": {"score": 75.5}}

    assert kpi.sc8(passing, mutation).verdict == kpi.PASS
    assert kpi.sc8(passing, {**mutation, "MT-02": {"score": 69.9}}).verdict == kpi.FAIL
    assert kpi.sc8(passing, {**mutation, "MT-02": {"score": None}}).verdict == kpi.FAIL
    assert kpi.sc8(_summary(UT_01=(9, 1)), mutation).verdict == kpi.FAIL
    assert kpi.sc8(passing, None).verdict == kpi.INSUFFICIENT_DATA
    assert kpi.sc8(None, mutation).verdict == kpi.INSUFFICIENT_DATA


def test_kpi_rules_out_of_scope() -> None:
    codes = {item.code: item for item in kpi.out_of_scope()}

    assert set(codes) == {"SC5", "SC6"} and all(item.verdict == kpi.NOT_APPLICABLE and item.reason for item in codes.values())
