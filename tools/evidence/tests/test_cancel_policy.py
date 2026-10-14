"""Chính sách hủy SmashCourt cho mô phỏng PT-03: nạp ảnh chụp, chọn mốc đúng như BookingService.CalculateRefundPercentAsync, hai cận theo độ phân giải ngày."""
import json
from datetime import date, datetime
from decimal import Decimal
from pathlib import Path
from zoneinfo import ZoneInfo

import pytest

from evidence import cancel_policy

REPO = Path(__file__).resolve().parents[3]
LISBON = ZoneInfo("Europe/Lisbon")


def write(tmp_path: Path, policies: list[dict]) -> Path:
    path = tmp_path / "policies.json"
    path.write_text(json.dumps({"source": "test", "retrieved_on": "2026-10-14", "policies": policies}), encoding="utf-8")
    return path


@pytest.fixture()
def policy() -> cancel_policy.CancelPolicy:
    return cancel_policy.load(REPO / cancel_policy.SNAPSHOT_RELATIVE)


def test_load_committed_snapshot_sorts_tiers_and_hashes_file(policy) -> None:
    assert [(tier.hours_before, tier.refund_fraction) for tier in policy.tiers] == [(24, Decimal("1")), (2, Decimal("0.5")), (0, Decimal("0"))]
    assert len(policy.sha256) == 64 and policy.retrieved_on == "2026-10-14"


@pytest.mark.parametrize(
    ("hours", "expected"),
    [
        (30.0, Decimal("1")),     # ≥ 24 giờ
        (24.0, Decimal("1")),     # biên `≤`: đúng 24 giờ vẫn hoàn 100%
        (23.99, Decimal("0.5")),
        (2.0, Decimal("0.5")),    # biên `≤`: đúng 2 giờ hoàn 50%
        (1.99, Decimal("0")),     # mốc 0 giờ
        (0.0, Decimal("0")),
        (-0.01, Decimal("0")),    # đã qua giờ bắt đầu
    ],
)
def test_refund_fraction_matches_backend_algorithm(policy, hours: float, expected: Decimal) -> None:
    assert cancel_policy.refund_fraction(policy, hours) == expected


def test_refund_fraction_without_matching_tier_is_zero(tmp_path: Path) -> None:
    only_day = cancel_policy.load(write(tmp_path, [{"hours_before": 24, "refund_percent": "100"}]))
    assert cancel_policy.refund_fraction(only_day, 5.0) == Decimal("0")


def test_hours_until_start_bounds_cover_whole_cancel_day() -> None:
    start = datetime(2026, 7, 10, 14, 0, tzinfo=LISBON)
    least, most = cancel_policy.hours_until_start_bounds(start, date(2026, 7, 9))
    assert least == pytest.approx(14 + 1 / 60)   # hủy 23:59 ngày liền trước
    assert most == pytest.approx(38.0)           # hủy 00:00 ngày liền trước
    least_same_day, most_same_day = cancel_policy.hours_until_start_bounds(start, date(2026, 7, 10))
    assert least_same_day < 0 < most_same_day == pytest.approx(14.0)


def test_scaled_multiplies_hours_before() -> None:
    policy = cancel_policy.load(REPO / cancel_policy.SNAPSHOT_RELATIVE)
    assert [tier.hours_before for tier in cancel_policy.scaled(policy, Decimal(2)).tiers] == [48, 4, 0]
    assert [tier.hours_before for tier in cancel_policy.scaled(policy, Decimal("0.5")).tiers] == [12, 1, 0]


@pytest.mark.parametrize(
    "policies",
    [
        [],
        [{"hours_before": -1, "refund_percent": "10"}],
        [{"hours_before": 2, "refund_percent": "101"}],
        [{"hours_before": 2, "refund_percent": "x"}],
        [{"hours_before": 2, "refund_percent": "10"}, {"hours_before": 2, "refund_percent": "20"}],
        [{"hours_before": True, "refund_percent": "10"}],
        ["khong-phai-dict"],
    ],
)
def test_load_rejects_invalid_snapshot(tmp_path: Path, policies: list[dict]) -> None:
    with pytest.raises(cancel_policy.CancelPolicyError):
        cancel_policy.load(write(tmp_path, policies))


@pytest.mark.parametrize("document", [[], ["policies"], "chuoi", 5, None, {"policies": ["khong-phai-dict"]}, {"policies": [{"hours_before": 2, "refund_percent": "10"}, 7]}])
def test_load_rejects_wrong_json_shape(tmp_path: Path, document: object) -> None:
    """Gốc JSON không phải đối tượng, hoặc phần tử `policies` không phải đối tượng → CancelPolicyError (không lọt AttributeError)."""
    path = tmp_path / "shape.json"
    path.write_text(json.dumps(document), encoding="utf-8")
    with pytest.raises(cancel_policy.CancelPolicyError):
        cancel_policy.load(path)
