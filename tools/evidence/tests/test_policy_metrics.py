"""Công thức của mô phỏng PT-03 (spec 4.3): phần cọc giữ lại theo hai biến thể (đủ biên), hoàn vượt AS_IS_BE, AUC có ties và trường hợp không xác định, percentile."""
import pytest

from evidence import policy_metrics as pm


@pytest.mark.parametrize(
    ("variant", "r", "pct", "expected"),
    [
        (pm.REFUND_OF_PAID, 0.3, 1.0, 0.0),
        (pm.REFUND_OF_PAID, 0.3, 0.5, 0.5),
        (pm.REFUND_OF_PAID, 0.3, 0.0, 1.0),
        (pm.REFUND_OF_PAID, 0.0, 0.5, 0.5),         # φ xác định; nhân với r = 0 vẫn giữ 0
        (pm.REFUND_OF_TOTAL_CAPPED, 0.0, 0.5, 0.0),  # r = 0 → 0, không chia cho 0
        (pm.REFUND_OF_TOTAL_CAPPED, 0.3, 0.0, 1.0),  # pct = 0
        (pm.REFUND_OF_TOTAL_CAPPED, 0.3, 1.0, 0.0),  # pct = 1
        (pm.REFUND_OF_TOTAL_CAPPED, 0.5, 0.5, 0.0),  # r = pct
        (pm.REFUND_OF_TOTAL_CAPPED, 0.3, 0.5, 0.0),  # cọc thấp: hoàn hết
        (pm.REFUND_OF_TOTAL_CAPPED, 1.0, 0.5, 0.5),  # r = 1: bằng REFUND_OF_PAID
    ],
)
def test_retained_fraction_variants_and_edges(variant: str, r: float, pct: float, expected: float) -> None:
    assert pm.retained_fraction(variant, r, pct) == pytest.approx(expected)


@pytest.mark.parametrize(("r", "pct"), [(-0.1, 0.5), (1.1, 0.5), (0.5, -0.1), (0.5, 1.1)])
def test_retained_fraction_rejects_out_of_domain(r: float, pct: float) -> None:
    with pytest.raises(ValueError):
        pm.retained_fraction(pm.REFUND_OF_PAID, r, pct)


def test_retained_fraction_rejects_unknown_variant() -> None:
    with pytest.raises(ValueError):
        pm.retained_fraction("AS_IS_BE", 0.5, 0.5)   # AS_IS_BE không có φ


def test_over_refund_is_positive_only_when_refund_exceeds_deposit() -> None:
    assert pm.over_refund(0.3, 1.0, 200.0) == pytest.approx(140.0)
    assert pm.over_refund(0.5, 0.5, 200.0) == 0.0
    assert pm.over_refund(1.0, 0.5, 200.0) == 0.0


def test_auc_with_ties_counts_half() -> None:
    # positives {0.5, 0.3}, negatives {0.3, 0.3}: (0.5>0.3)*2 + (0.3=0.3)*0.5*2 = 3 trên 4 cặp
    assert pm.auc([0.5, 0.3, 0.3, 0.3], [1, 1, 0, 0]) == pm.Auc(0.75, None)


def test_auc_undefined_cases_return_null_with_reason() -> None:
    assert pm.auc([0.5, 0.3], [1, 1]) == pm.Auc(None, pm.ONE_CLASS)
    assert pm.auc([0.5, 0.5, 0.5], [1, 0, 1]) == pm.Auc(None, pm.CONSTANT_SCORE)


def test_auc_oracle_is_one_only_with_both_classes() -> None:
    assert pm.auc([1, 0, 1], [1, 0, 1]) == pm.Auc(1.0, None)
    assert pm.auc([1, 1], [1, 1]).reason == pm.ONE_CLASS


def test_delta_r() -> None:
    assert pm.delta_r([0.5, 0.3, 0.3, 0.1], [1, 1, 0, 0]) == pytest.approx(0.4 - 0.2)
    assert pm.delta_r([0.5], [1]) is None


def test_percentile_and_ci95() -> None:
    values = [float(value) for value in range(101)]
    assert pm.percentile(values, 0.025) == pytest.approx(2.5) and pm.percentile(values, 0.975) == pytest.approx(97.5)
    assert pm.percentile([], 0.5) is None
    assert pm.ci95(reversed(values)) == {"low": pytest.approx(2.5), "high": pytest.approx(97.5), "samples": 101}


# --- Vòng sửa 1: đầu vào lỗi phải báo lỗi, không trả số "có vẻ hợp lý" ---

@pytest.mark.parametrize("func", [pm.auc, pm.delta_r])
@pytest.mark.parametrize(
    ("scores", "labels"),
    [
        ([float("nan"), 0.2, 0.5, float("nan")], [1, 0, 1, 0]),  # NaN
        ([None, 0.2, 0.5, 0.1], [1, 0, 1, 0]),                  # None
        ([0.1, 0.2, 0.3], [1, 0, 2]),                            # nhãn ngoài {0, 1}
        ([0.1, 0.2, 0.3], [1, 0, -1]),
        ([0.1, 0.2, 0.3], [1, 0]),                               # lệch độ dài (scores dài hơn)
        ([0.1, 0.2], [1, 0, 1]),                                 # lệch độ dài (labels dài hơn)
    ],
)
def test_auc_and_delta_r_reject_bad_input(func, scores, labels) -> None:
    with pytest.raises(ValueError):
        func(scores, labels)


def test_auc_from_counts_rejects_empty_class() -> None:
    with pytest.raises(ValueError):
        pm.auc_from_counts({}, {0.3: 2})
    with pytest.raises(ValueError):
        pm.auc_from_counts({0.3: 2}, {})
    with pytest.raises(ValueError):
        pm.auc_from_counts({0.3: 0}, {0.1: 0})


@pytest.mark.parametrize("q", [-0.01, 1.01, float("nan")])
def test_percentile_rejects_q_out_of_range(q: float) -> None:
    with pytest.raises(ValueError):
        pm.percentile([1.0, 2.0], q)


def test_ci95_empty_returns_nulls() -> None:
    assert pm.ci95([]) == {"low": None, "high": None, "samples": 0}
