"""Chính sách hủy của SmashCourt (bảng `cancel_policies`) cho mô phỏng PT-03 (gói 5.7b, spec 4.3/4.6).

Ảnh chụp có commit ở `tools/evidence/config/smashcourt_cancel_policies.json`; evaluator đọc nó từ repo mount chỉ đọc (`context.repo_dir / SNAPSHOT_RELATIVE`),
giống cách đọc `appsettings.json`. Thuật toán chọn mốc sao chép đúng `BookingService.CalculateRefundPercentAsync` của SmashCourt-BE: đã qua giờ bắt đầu → 0;
mốc có `hours_before` lớn nhất thỏa `hours_before ≤ số giờ còn lại`; không mốc nào khớp → 0. Số giờ còn lại tính đúng múi giờ (không sao chép lỗi +7 giờ nghi vấn B-2).
"""
from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from datetime import date, datetime, time, timezone
from decimal import Decimal, InvalidOperation
from pathlib import Path

SNAPSHOT_RELATIVE = Path("tools") / "evidence" / "config" / "smashcourt_cancel_policies.json"
_HUNDRED = Decimal(100)


class CancelPolicyError(ValueError):
    """Ảnh chụp chính sách hủy thiếu, sai định dạng hoặc ngoài miền giá trị."""


@dataclass(frozen=True)
class Tier:
    """Một mốc: hủy trước ít nhất `hours_before` giờ thì hoàn `refund_fraction` (0…1)."""

    hours_before: int
    refund_fraction: Decimal


@dataclass(frozen=True)
class CancelPolicy:
    """Các mốc sắp theo `hours_before` giảm dần, nguồn, ngày lấy và SHA-256 của file ảnh chụp."""

    tiers: tuple[Tier, ...]
    source: str
    retrieved_on: str
    sha256: str


def load(path: Path) -> CancelPolicy:
    """Nạp ảnh chụp; ném `CancelPolicyError` khi file sai định dạng, rỗng, mốc trùng hoặc giá trị ngoài miền (`hours_before ≥ 0`, `refund_percent` 0…100)."""
    try:
        raw = Path(path).read_bytes()
        document = json.loads(raw.decode("utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise CancelPolicyError(f"Không đọc được ảnh chụp chính sách hủy {path}: {error}") from error
    if not isinstance(document, dict):
        raise CancelPolicyError("Ảnh chụp chính sách hủy phải là một đối tượng JSON có khóa `policies`")
    tiers: list[Tier] = []
    seen: set[int] = set()
    for index, item in enumerate(document.get("policies") or []):
        if not isinstance(item, dict):
            raise CancelPolicyError(f"mốc {index}: phải là đối tượng JSON, nhận {type(item).__name__}")
        hours = item.get("hours_before")
        if not isinstance(hours, int) or isinstance(hours, bool) or hours < 0:
            raise CancelPolicyError(f"mốc {index}: hours_before phải là số nguyên ≥ 0, nhận {hours!r}")
        try:
            fraction = Decimal(str(item.get("refund_percent"))) / _HUNDRED
        except InvalidOperation as error:
            raise CancelPolicyError(f"mốc {index}: refund_percent không phải số") from error
        if not fraction.is_finite() or not Decimal(0) <= fraction <= Decimal(1):
            raise CancelPolicyError(f"mốc {index}: refund_percent phải trong 0…100")
        if hours in seen:
            raise CancelPolicyError(f"mốc {index}: hours_before {hours} bị trùng (BE yêu cầu duy nhất)")
        seen.add(hours)
        tiers.append(Tier(hours, fraction))
    if not tiers:
        raise CancelPolicyError("Ảnh chụp không có mốc nào (BE luôn giữ tối thiểu 1 mốc)")
    tiers.sort(key=lambda tier: tier.hours_before, reverse=True)
    return CancelPolicy(tuple(tiers), str(document.get("source", "")), str(document.get("retrieved_on", "")), hashlib.sha256(raw).hexdigest())


def refund_fraction(policy: CancelPolicy, hours_until_start: float) -> Decimal:
    """Tỷ lệ hoàn (0…1) theo đúng thuật toán của BE."""
    if hours_until_start < 0:
        return Decimal(0)
    for tier in policy.tiers:
        if tier.hours_before <= hours_until_start:
            return tier.refund_fraction
    return Decimal(0)


def hours_until_start_bounds(start: datetime, cancel_day: date) -> tuple[float, float]:
    """Dataset chỉ có ngày hủy: (số giờ ít nhất, nhiều nhất) từ lúc hủy tới `start` khi hủy lúc 23:59 và lúc 00:00 của ngày đó, cùng múi giờ với `start`, tính qua UTC."""
    zone = start.tzinfo

    def hours(moment: datetime) -> float:
        return (start.astimezone(timezone.utc) - moment.astimezone(timezone.utc)).total_seconds() / 3600

    return hours(datetime.combine(cancel_day, time(23, 59), tzinfo=zone)), hours(datetime.combine(cancel_day, time(0, 0), tzinfo=zone))


def scaled(policy: CancelPolicy, factor: Decimal) -> CancelPolicy:
    """Bản giãn/co mốc (×2 / ÷2) cho kịch bản độ nhạy `S-SC×2`, `S-SC÷2`: nhân mọi `hours_before` với `factor` (làm tròn tới giờ), giữ tỷ lệ hoàn."""
    tiers = tuple(Tier(int((Decimal(tier.hours_before) * factor).to_integral_value()), tier.refund_fraction) for tier in policy.tiers)
    return CancelPolicy(tiers, policy.source, policy.retrieved_on, policy.sha256)
