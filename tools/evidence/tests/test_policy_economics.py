"""Mô phỏng PT-03 trên D5 mẫu (spec 4.3): sự kiện theo reservation_status (không theo is_canceled), kịch bản và hai cận, NEED_APPROVAL hai quy ước,
loại dữ liệu thiếu có đếm, bootstrap ghép cặp (hiệu tính trong từng vòng), AS_IS_BE tách riêng, xếp hạng và bias."""
from decimal import Decimal
from pathlib import Path

import pytest

from evidence import cancel_policy, policy_economics as pe, shadow_pairing as sp

REPO = Path(__file__).resolve().parents[3]
POLICY = cancel_policy.load(REPO / cancel_policy.SNAPSHOT_RELATIVE)


def row(*, price: str = "100", canceled: str = "0", status: str = "Check-Out", arrival: str = "2017-07-10T14:00:00", cancel_day: str = "2017-07-11",
        default: tuple[str, str] = ("APPROVED", "0.5"), mock: tuple[str, str] = ("APPROVED", "0.3"), pair_state: str = sp.PAIRED) -> dict[str, str]:
    return {"total_price": price, "is_canceled": canceled, "reservation_status": status, "arrival_local": arrival, "reservation_status_date": cancel_day,
            "status": default[0], "deposit_rate": default[1] if default[0] == "APPROVED" else "", "shadow_pair_state": pair_state,
            "shadow_status": mock[0], "shadow_deposit_rate": mock[1] if mock[0] == "APPROVED" else "", "primary_fallback": "False"}


def summarize(rows: list[dict[str, str]], policy: cancel_policy.CancelPolicy | None = POLICY, **kwargs: int) -> dict:
    return pe.summarize(rows, policy, reconciliation=None, pairing_counts={}, violations={}, **({"iterations": 10} | kwargs))


def points(result: dict) -> dict[tuple, float | None]:
    return {(p["scenario"], p["variant"], p["bound"], p["na"], p["policy"]): p["retention"] for p in result["point"]}


def test_economic_event_follows_reservation_status_not_is_canceled() -> None:
    rows = [row(status="No-Show", canceled="0"), row(status="Check-Out", canceled="1")]
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    assert result["event_value"] == {"E": 100.0, "E_canceled": 0.0, "E_no_show": 100.0}
    assert result["crosstab"] == {"0": {"Check-Out": 0, "Canceled": 0, "No-Show": 1}, "1": {"Check-Out": 1, "Canceled": 0, "No-Show": 0}}
    assert result["counts"]["label_mismatch"] == 2
    # No-Show: φ = 1 ở mọi kịch bản → tỷ lệ giữ lại của Default = r = 0,5
    point = {(p["scenario"], p["variant"], p["bound"], p["na"], p["policy"]): p["retention"] for p in result["point"]}
    assert point[("S-NOSHOW", "REFUND_OF_PAID", "low", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(0.5)


def test_s_sc_bounds_from_cancel_day_resolution() -> None:
    # hủy ngày liền trước ngày đến: 14–38 giờ → mốc 50 % (cận ít giờ) hoặc 100 % (cận nhiều giờ)
    rows = [row(status="Canceled", canceled="1", cancel_day="2017-07-09"), row()]
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    point = {(p["scenario"], p["variant"], p["bound"], p["na"], p["policy"]): p["retention"] for p in result["point"]}
    # REFUND_OF_PAID, Default r = 0,5: low (hoàn 100 %) → 0; high (hoàn 50 %) → φ 0,5 × r 0,5 = 0,25
    assert point[("S-SC", "REFUND_OF_PAID", "low", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(0.0)
    assert point[("S-SC", "REFUND_OF_PAID", "high", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(0.25)
    # REFUND_OF_TOTAL_CAPPED, r = 0,5, hoàn 50 % → max(0, 0,5 − 0,5) = 0
    assert point[("S-SC", "REFUND_OF_TOTAL_CAPPED", "high", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(0.0)
    assert {item["bucket"] for item in result["tier_distribution"]} == {"UNRESOLVED_0.5_1"}


def test_need_approval_two_conventions_and_operational_table() -> None:
    rows = [row(status="Canceled", canceled="1", cancel_day="2017-07-10", default=("NEED_APPROVAL", "")), row()]
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    point = {(p["scenario"], p["variant"], p["bound"], p["na"], p["policy"]): p["retention"] for p in result["point"]}
    assert point[("S-FULL", "REFUND_OF_PAID", "low", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(1.0)
    assert point[("S-FULL", "REFUND_OF_PAID", "low", 0.0, pe.TADSS_DEFAULT)] == pytest.approx(0.0)
    assert {item["policy"]: item["count"] for item in result["need_approval"]} == {pe.TADSS_DEFAULT: 1, pe.TADSS_SHADOW_MOCKALT: 0}


def test_invalid_rows_are_excluded_with_reason_never_zero() -> None:
    rows = [row(default=("", "")), row(price=""), row(status="Lost"), row(default=("REJECTED", "")), row(default=("APPROVED", "x")), row()]
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    assert result["counts"]["excluded"] == {"no_decision": 1, "invalid_price": 1, "invalid_reservation_status": 1, "unsupported_status": 1, "invalid_rate": 1}
    assert result["counts"]["compared"] == 1


def test_only_paired_rows_are_compared_and_bias_is_reported() -> None:
    rows = [row(status="Canceled", canceled="1", cancel_day="2017-07-10"), row(pair_state=sp.UNPAIRED), row(pair_state=sp.SHADOW_FALLBACK)]
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    assert result["counts"]["compared"] == 1
    bias = result["pairing_bias"]
    assert bias["reported"] is True and bias["flag"] is True and bias["non_paired"]["rows"] == 2


def test_as_is_be_counts_over_refund_separately() -> None:
    rows = [row(status="Canceled", canceled="1", cancel_day="2017-07-01", default=("APPROVED", "0.3")), row()]   # hủy sớm → hoàn 100 %
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    default_low = next(item for item in result["as_is_be"] if item["policy"] == pe.TADSS_DEFAULT and item["bound"] == "low")
    assert default_low["count"] == 1 and default_low["value"] == pytest.approx(70.0)
    assert all("φ" not in key and "retention" not in key for key in default_low)


def test_bootstrap_difference_is_computed_within_each_resample() -> None:
    # Mức cọc THAY ĐỔI theo đơn → CI riêng của từng chính sách có độ rộng > 0. MockAlt = Default − 0,1 trên mọi đơn No-Show (φ = 1) nên trong MỖI vòng
    # hiệu tỷ lệ giữ lại đúng bằng −0,1 → CI của hiệu suy biến tại −0,1. Trừ hai CI riêng cho nhau sẽ ra khoảng rộng, không phải [−0,1; −0,1].
    rows = [row(status="No-Show", canceled="1", price=str(10 + 7 * index), default=("APPROVED", f"{0.3 + (index % 5) * 0.1:.1f}"),
                mock=("APPROVED", f"{0.2 + (index % 5) * 0.1:.1f}")) for index in range(40)]
    rows += [row(price=str(10 + index), default=("APPROVED", f"{0.2 + (index % 3) * 0.3:.1f}")) for index in range(40)]
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, seed=7, iterations=200)
    case = result["ci"]["REFUND_OF_PAID"]["low"]
    for name in (pe.TADSS_DEFAULT, pe.TADSS_SHADOW_MOCKALT):
        assert case["retention"][name]["high"] - case["retention"][name]["low"] > 0.01
    diff = case["diff_retention"]["MOCKALT_MINUS_DEFAULT"]
    assert diff["low"] == pytest.approx(-0.1) and diff["high"] == pytest.approx(-0.1)


def test_bootstrap_is_reproducible_by_seed() -> None:
    rows = [row(status="Canceled", canceled=str(index % 2), cancel_day="2017-07-10", price=str(50 + index)) for index in range(60)]
    first = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, seed=3, iterations=50)["ci"]
    second = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, seed=3, iterations=50)["ci"]
    assert first == second


def test_missing_policy_marks_s_sc_insufficient_and_keeps_other_scenarios() -> None:
    result = pe.summarize([row(status="No-Show", canceled="1"), row()], None, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    assert result["cancel_policy"] == {"status": "INSUFFICIENT_DATA"}
    assert {p["scenario"] for p in result["point"]} == {"S-FULL", "S-NOSHOW"}


def test_ranking_flags_dependence_on_assumption() -> None:
    # Canceled sớm (hoàn 100 % ở S-SC) với MockAlt cọc cao hơn, No-Show với Default cọc cao hơn → thứ hạng đổi giữa S-FULL và S-NOSHOW
    rows = [row(status="Canceled", canceled="1", cancel_day="2017-07-01", price="300", default=("APPROVED", "0.2"), mock=("APPROVED", "0.5")),
            row(status="No-Show", canceled="1", price="100", default=("APPROVED", "0.5"), mock=("APPROVED", "0.3")), row()]
    ranking = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)["ranking"]
    assert ranking["stable"] is False and "phụ thuộc giả định" in ranking["statement"]


# ---- các test bổ sung cho từng hàm private (bảng của brief Task 5) ----

def test_bad_arrival_with_valid_cancel_day_is_excluded_not_crash() -> None:
    rows = [row(arrival="không-phải-ngày"), row(cancel_day=""), row(canceled="x"), row()]
    result = summarize(rows)
    assert result["counts"]["excluded"]["invalid_reservation_status"] == 3
    assert result["counts"]["valid"] == 1


def test_is_canceled_accepts_boolean_text() -> None:
    result = summarize([row(status="Canceled", canceled="True", cancel_day="2017-07-01"), row(canceled="False")])
    assert result["crosstab"]["1"]["Canceled"] == 1 and result["crosstab"]["0"]["Check-Out"] == 1
    assert result["counts"]["label_mismatch"] == 0


def test_tier_distribution_buckets_and_values() -> None:
    rows = [row(status="Canceled", canceled="1", cancel_day="2017-07-01", price="100"),   # ≥ 38 giờ → luôn 100 %
            row(status="Canceled", canceled="1", cancel_day="2017-07-10", price="200"),   # ngày đến: −10…14 giờ → đã qua giờ hoặc 50 %
            row(status="Canceled", canceled="1", cancel_day="2017-07-11", price="300"),   # sau ngày đến → đã qua giờ ở cả hai cận
            row(status="No-Show", canceled="1", price="400"), row()]
    tiers = {item["bucket"]: (item["count"], item["value"]) for item in summarize(rows)["tier_distribution"]}
    assert tiers == {"REFUND_1": (1, 100.0), "UNRESOLVED_PAST_0.5": (1, 200.0), "PAST_START": (1, 300.0), "NO_SHOW": (1, 400.0)}


def test_scaled_scenarios_move_tiers() -> None:
    # hủy ngày liền trước: 14–38 giờ. ×2 (48/4/0 giờ) → 50 % ở cả hai cận; ÷2 (12/1/0 giờ) → 100 % ở cả hai cận
    point = points(summarize([row(status="Canceled", canceled="1", cancel_day="2017-07-09"), row()]))
    assert point[("S-SC×2", "REFUND_OF_PAID", "low", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(0.25)
    assert point[("S-SC×2", "REFUND_OF_PAID", "high", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(0.25)
    assert point[("S-SC÷2", "REFUND_OF_PAID", "high", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(0.0)
    assert point[("S-SC", "REFUND_OF_PAID", "low", 1.0, pe.ORACLE)] == pytest.approx(0.0)
    assert point[("S-FULL", "REFUND_OF_TOTAL_CAPPED", "high", 1.0, pe.STATIC_100)] == pytest.approx(1.0)


def test_point_uncovered_and_burden_hand_computed() -> None:
    rows = [row(price="100"), row(price="300", default=("NEED_APPROVAL", "")), row(status="No-Show", canceled="1", price="200")]
    result = summarize(rows)
    uncovered = {(p["scenario"], p["na"], p["policy"]): p["uncovered"] for p in result["point"] if p["variant"] == "REFUND_OF_PAID" and p["bound"] == "low"}
    assert uncovered[("S-FULL", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(100.0)   # 200 − 0,5·200
    assert uncovered[("S-FULL", 1.0, pe.NO_DEPOSIT)] == pytest.approx(200.0)
    burden = {(item["policy"], item["na"]): item["value"] for item in result["burden"]}
    assert burden[(pe.TADSS_DEFAULT, 1.0)] == pytest.approx(350 / 400)
    assert burden[(pe.TADSS_DEFAULT, 0.0)] == pytest.approx(50 / 400)
    assert burden[(pe.STATIC_100, 1.0)] == pytest.approx(1.0) and burden[(pe.ORACLE, 1.0)] == pytest.approx(0.0)
    applicable = {item["policy"]: item["na_applicable"] for item in result["burden"]}
    assert applicable[pe.TADSS_DEFAULT] is True and applicable[pe.STATIC_100] is False


def test_zero_event_value_gives_none_not_zero() -> None:
    result = summarize([row(), row(price="50")])
    assert all(p["retention"] is None and p["uncovered"] is None for p in result["point"])
    assert result["ranking"]["by_case"] == [] or all(case["sign"] is None for case in result["ranking"]["by_case"])


def test_discrimination_reasons_and_oracle_flag() -> None:
    rows = [row(status="Canceled", canceled="1", cancel_day="2017-07-01", default=("APPROVED", "0.8")), row(default=("NEED_APPROVAL", ""))]
    disc = {item["policy"]: item for item in summarize(rows)["discrimination"]}
    assert disc[pe.NO_DEPOSIT]["auc"] == {"value": None, "reason": "CONSTANT_SCORE"}
    assert disc[pe.ORACLE]["oracle_by_definition"] is True and disc[pe.ORACLE]["auc"]["value"] == pytest.approx(1.0)
    # NEED_APPROVAL quy về 1 (NA_MAIN): đơn hủy 0,8 < đơn không hủy 1 → AUC 0, Δr −0,2
    assert disc[pe.TADSS_DEFAULT]["auc"]["value"] == pytest.approx(0.0)
    assert disc[pe.TADSS_DEFAULT]["delta_r"] == pytest.approx(-0.2)
    assert disc[pe.TADSS_DEFAULT]["rate_distribution"] == {"0.8": 1, "NEED_APPROVAL": 1}


def test_bootstrap_auc_insufficient_for_small_minority_and_counts_skips() -> None:
    rows = [row(status="No-Show", canceled="1", default=("APPROVED", "0.8"))] + [row(price=str(20 + index)) for index in range(5)]
    ci = summarize(rows, seed=1, iterations=100)["ci"]
    assert ci["REFUND_OF_PAID"]["low"]["auc"][pe.TADSS_DEFAULT] == "INSUFFICIENT_DATA"
    assert ci["iterations"] == 100 and ci["seed"] == 1
    # vòng không có đơn sự kiện nào (E = 0) bị bỏ và đếm; số mẫu còn lại khớp
    assert ci["skipped"] > 0
    assert ci["REFUND_OF_PAID"]["low"]["retention"][pe.TADSS_DEFAULT]["samples"] == 100 - ci["skipped"]


def test_missing_policy_ci_is_insufficient() -> None:
    result = summarize([row(status="No-Show", canceled="1"), row()], None)
    assert result["ci"] == {"status": "INSUFFICIENT_DATA"} and result["as_is_be"] == []


def test_pairing_bias_group_statistics() -> None:
    rows = [row(price="100", arrival="2017-07-15T14:00:00"), row(price="300", canceled="1", status="Canceled", cancel_day="2017-07-01", pair_state=sp.UNPAIRED)]
    bias = summarize(rows)["pairing_bias"]
    assert bias["paired"] == {"rows": 1, "cancel_rate": 0.0, "mean_price": 100.0, "weekend_share": 1.0, "mean_position_share": 0.0}
    assert bias["non_paired"]["mean_price"] == 300.0 and bias["non_paired"]["mean_position_share"] == 0.5
    assert "99 %" in bias["threshold_note"]
    none_missing = summarize([row()])["pairing_bias"]
    assert none_missing["reported"] is False and none_missing["flag"] is False


def test_output_has_required_top_level_keys_and_no_forbidden_wording() -> None:
    result = summarize([row(status="No-Show", canceled="1"), row()])
    assert {"schema", "assumptions", "cancel_policy", "counts", "crosstab", "tier_distribution", "event_value", "point", "burden", "discrimination",
            "need_approval", "ci", "ranking", "as_is_be", "shadow", "pairing_bias", "seconds"} <= set(result)
    assert "tiền mất" not in repr(result)


def ranking_inputs(retention: dict[str, tuple[float, float]], diff: tuple[float, float],
                   lower: dict[str, tuple[float, float]] | None = None) -> tuple[list[dict], dict]:
    """`point` tối thiểu (hai chính sách TADSS) theo kịch bản → (Default, MockAlt) ở NEED_APPROVAL = 1 và ở = 0 (`lower`, mặc định giống r = 1),
    và `ci` với cùng một CI của hiệu MOCKALT − DEFAULT (CI chỉ có cho r = 1)."""
    point = [{"scenario": scenario, "variant": variant, "bound": bound, "na": na, "policy": name, "retention": value}
             for na, table in ((pe.NA_MAIN, retention), (pe.NA_LOWER, lower or retention)) for scenario, (default, mock) in table.items()
             for variant in pe.pm.VARIANTS for bound in pe.BOUNDS for name, value in ((pe.TADSS_DEFAULT, default), (pe.TADSS_SHADOW_MOCKALT, mock))]
    ci = {variant: {bound: {"diff_retention": {"MOCKALT_MINUS_DEFAULT": {"low": diff[0], "high": diff[1], "samples": 100}}} for bound in pe.BOUNDS}
          for variant in pe.pm.VARIANTS}
    return point, ci


def names_a_winner(statement: str) -> bool:
    return pe.TADSS_DEFAULT in statement or pe.TADSS_SHADOW_MOCKALT in statement


def test_ranking_stable_but_ci_contains_zero_names_no_winner() -> None:
    ranking = pe._ranking(*ranking_inputs({"S-SC": (0.5, 0.3), "S-FULL": (0.6, 0.4)}, (-0.3, 0.1)))
    assert ranking["stable"] is True and ranking["indistinguishable"] is True
    assert "không phân biệt được trên dữ liệu này" in ranking["statement"] and not names_a_winner(ranking["statement"])


def test_ranking_stable_and_ci_excludes_zero_names_the_order() -> None:
    ranking = pe._ranking(*ranking_inputs({"S-SC": (0.5, 0.3), "S-FULL": (0.6, 0.4)}, (-0.3, -0.1)))
    assert ranking["stable"] is True and ranking["indistinguishable"] is False
    assert "TADSS_DEFAULT > TADSS_SHADOW_MOCKALT" in ranking["statement"] and "không phân biệt" not in ranking["statement"]


def test_ranking_unstable_names_no_winner() -> None:
    for diff in ((-0.3, -0.1), (-0.3, 0.1)):
        ranking = pe._ranking(*ranking_inputs({"S-SC": (0.5, 0.3), "S-FULL": (0.3, 0.5)}, diff))
        assert ranking["stable"] is False and "phụ thuộc giả định" in ranking["statement"] and ranking["unstable_dimensions"] == ["scenario"]
        assert ">" not in ranking["statement"] and "<" not in ranking["statement"] and not names_a_winner(ranking["statement"])
        assert ("không phân biệt được trên dữ liệu này" in ranking["statement"]) is (diff[1] > 0)


def test_ranking_sign_flip_only_at_need_approval_zero_is_unstable() -> None:
    # r = 1: Default > MockAlt ở mọi kịch bản và CI loại 0; r = 0: MockAlt > Default → thứ hạng phụ thuộc quy ước NEED_APPROVAL
    ranking = pe._ranking(*ranking_inputs({"S-SC": (0.5, 0.3), "S-FULL": (0.6, 0.4)}, (-0.3, -0.1), lower={"S-SC": (0.2, 0.3), "S-FULL": (0.3, 0.4)}))
    assert ranking["stable"] is False and ranking["unstable_dimensions"] == ["na"]
    assert "phụ thuộc giả định" in ranking["statement"] and "quy ước NEED_APPROVAL" in ranking["statement"] and not names_a_winner(ranking["statement"])
    assert len(ranking["by_case"]) == len(ranking["by_case_na_lower"]) == 8
    assert all(entry["na"] == pe.NA_MAIN for entry in ranking["by_case"])
    assert all(entry["na"] == pe.NA_LOWER and entry["ci_contains_zero"] is None for entry in ranking["by_case_na_lower"])


def test_ranking_same_sign_including_need_approval_zero_and_ci_excludes_zero_is_named() -> None:
    ranking = pe._ranking(*ranking_inputs({"S-SC": (0.5, 0.3), "S-FULL": (0.6, 0.4)}, (-0.3, -0.1), lower={"S-SC": (0.4, 0.3), "S-FULL": (0.5, 0.4)}))
    assert ranking["stable"] is True and ranking["unstable_dimensions"] == [] and ranking["indistinguishable"] is False
    assert "TADSS_DEFAULT > TADSS_SHADOW_MOCKALT" in ranking["statement"] and "ước lượng điểm" in ranking["statement"]


def test_ranking_without_paired_rows_says_nothing_to_compare_not_zero_events() -> None:
    result = summarize([row(pair_state=sp.UNPAIRED), row(status="Canceled", canceled="1", pair_state=sp.UNPAIRED)])
    assert result["counts"]["compared"] == 0
    assert "không có dòng PAIRED để so sánh" in result["ranking"]["statement"] and "E = 0" not in result["ranking"]["statement"]


def test_ranking_with_paired_rows_but_no_events_keeps_the_zero_events_message() -> None:
    result = summarize([row(), row()])  # cả hai PAIRED và Check-Out: E = 0 thật
    assert result["counts"]["compared"] == 2 and result["counts"]["events"] == {"Canceled": 0, "No-Show": 0}
    assert "E = 0" in result["ranking"]["statement"]


@pytest.mark.parametrize("zero", [0, 0.0, Decimal("0"), "0"])
def test_numeric_zero_deposit_rate_is_a_valid_rate_not_invalid_rate(zero: object) -> None:
    """Mức cọc số 0 / Decimal("0") (kiểu không phải chuỗi) phải được nhận là cọc 0 hợp lệ, không bị `or ""` biến thành rỗng → invalid_rate."""
    raw = row() | {"deposit_rate": zero, "shadow_deposit_rate": zero}
    rows, excluded = pe.prepare([raw])
    assert excluded["invalid_rate"] == 0 and len(rows) == 1
    assert rows[0].rates[pe.TADSS_DEFAULT] == 0.0 and rows[0].rates[pe.TADSS_SHADOW_MOCKALT] == 0.0


def test_ranking_same_sign_including_need_approval_zero_but_ci_contains_zero_names_no_winner() -> None:
    ranking = pe._ranking(*ranking_inputs({"S-SC": (0.5, 0.3), "S-FULL": (0.6, 0.4)}, (-0.3, 0.05), lower={"S-SC": (0.4, 0.3), "S-FULL": (0.5, 0.4)}))
    assert ranking["stable"] is True and ranking["indistinguishable"] is True
    assert "không phân biệt được trên dữ liệu này" in ranking["statement"] and not names_a_winner(ranking["statement"])


def test_single_label_class_reports_auc_one_class() -> None:
    result = summarize([row(default=("APPROVED", "0.5")), row(price="50", default=("APPROVED", "0.8"))])
    assert result["counts"]["compared"] == 2
    disc = {item["policy"]: item for item in result["discrimination"]}
    assert disc[pe.TADSS_DEFAULT]["auc"] == {"value": None, "reason": "ONE_CLASS"}
