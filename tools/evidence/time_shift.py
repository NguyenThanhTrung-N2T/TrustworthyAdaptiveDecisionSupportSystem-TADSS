"""Dời thời gian theo bội số 7 ngày (gói 5.6, quyết định Q-04), dùng chung cho replay SmashCourt (RP) và adapter khách sạn (PT).

Mọi mốc của một bản ghi (`requestedAt`, `startTime`, `endTime`, `memberSince`) dời cùng `k` tuần, `k` là số nhỏ nhất sao cho `requestedAt` sau khi dời
không sớm hơn thời điểm gửi, nên độ lệch δ = (requestedAt dời) - (thời điểm gửi) thuộc [0; 7 ngày). Runtime tính approval window bằng giờ máy chủ,
nên δ ≥ 0 bảo đảm khoảng tới giờ chơi thực tế không ngắn hơn lead time gốc.

Dời theo NGÀY LỊCH của múi giờ gắn với mốc (giữ thứ trong tuần và giờ địa phương), không phải 7 × 24 giờ tuyệt đối, nên đúng cả khi qua ngày đổi giờ
mùa hè (VD `Europe/Lisbon` của dataset khách sạn). Giờ địa phương không tồn tại (rơi vào khoảng nhảy giờ) được chuẩn hóa theo UTC sang thời điểm hợp lệ gần nhất.
"""
from __future__ import annotations

import math
from datetime import datetime, timedelta, timezone

WEEK = timedelta(days=7)


def _require_aware(moment: datetime, name: str) -> None:
    if moment.tzinfo is None or moment.utcoffset() is None:
        raise ValueError(f"{name} phải có múi giờ (naive datetime không dời được theo ngày lịch)")


def shift(moment: datetime, weeks: int) -> datetime:
    """Dời `moment` đi `weeks` x 7 ngày lịch trong múi giờ của chính nó; giữ thứ trong tuần và giờ địa phương."""
    _require_aware(moment, "moment")
    moved = moment + timedelta(days=7 * weeks)  # datetime có múi giờ cộng timedelta là phép cộng theo giờ treo tường của tzinfo
    return moved.astimezone(timezone.utc).astimezone(moment.tzinfo)  # chuẩn hóa giờ không tồn tại / mơ hồ về một thời điểm hợp lệ


def weeks_to_shift(requested_at: datetime, now: datetime) -> int:
    """Số tuần `k ≥ 0` nhỏ nhất để `shift(requested_at, k) >= now`; `k = 0` khi `requested_at` đã ở tương lai hoặc đúng bằng `now`."""
    _require_aware(requested_at, "requested_at")
    _require_aware(now, "now")
    behind = now - requested_at
    if behind <= timedelta(0):
        return 0
    weeks = max(0, math.ceil(behind / WEEK))  # ước lượng theo 7 x 24 giờ, rồi hiệu chỉnh vì ngày đổi giờ có thể lệch ±1 giờ
    while shift(requested_at, weeks) < now:
        weeks += 1
    while weeks > 0 and shift(requested_at, weeks - 1) >= now:
        weeks -= 1
    return weeks
