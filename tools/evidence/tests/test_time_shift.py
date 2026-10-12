"""Dời thời gian theo bội số 7 ngày (Q-04): k nhỏ nhất để requestedAt sau khi dời ≥ thời điểm gửi; giữ thứ trong tuần và giờ địa phương."""
from datetime import datetime, timedelta
from zoneinfo import ZoneInfo

import pytest

from evidence import time_shift

VN = ZoneInfo("Asia/Ho_Chi_Minh")
LISBON = ZoneInfo("Europe/Lisbon")
UTC = ZoneInfo("UTC")
NOW = datetime(2026, 10, 9, 12, 0, tzinfo=VN)
WEEK = timedelta(days=7)


@pytest.mark.parametrize(
    "requested_offset,expected_weeks",
    [
        (timedelta(hours=-1), 1),  # trước giờ gửi 1 giờ: phải dời ít nhất 1 tuần
        (timedelta(0), 0),  # đúng giờ gửi: δ = 0 đã thỏa
        (timedelta(hours=1), 0),  # đã ở tương lai
        (-WEEK, 1),  # đúng 1 tuần trước: dời 1 tuần cho δ = 0
        (-WEEK - timedelta(seconds=1), 2),
        (timedelta(days=-400), 58),  # 400 / 7 = 57,14 nên cần 58 tuần (406 ngày)
        (timedelta(days=30), 0),
    ],
)
def test_weeks_to_shift_Theory(requested_offset: timedelta, expected_weeks: int) -> None:
    requested_at = NOW + requested_offset

    weeks = time_shift.weeks_to_shift(requested_at, NOW)

    assert weeks == expected_weeks
    delta = time_shift.shift(requested_at, weeks) - NOW
    assert delta >= timedelta(0)
    if weeks > 0:
        assert delta < WEEK, "δ phải thuộc [0; 7 ngày) khi đã phải dời"
        assert time_shift.shift(requested_at, weeks - 1) < NOW, "k phải là số nhỏ nhất"


def test_weeks_to_shift_uses_calendar_days_across_dst() -> None:
    # 26/03/2026 20:30 Lisbon (UTC+0); cộng 1 tuần theo ngày lịch là 02/04 20:30 (UTC+1) = 19:30Z, sớm hơn "đúng 7 x 24 giờ" 1 giờ
    requested_at = datetime(2026, 3, 26, 20, 30, tzinfo=LISBON)
    now = datetime(2026, 4, 2, 19, 45, tzinfo=UTC)

    weeks = time_shift.weeks_to_shift(requested_at, now)

    assert weeks == 2, "1 tuần theo ngày lịch (19:30Z) vẫn trước giờ gửi 19:45Z"
    assert timedelta(0) <= time_shift.shift(requested_at, 2) - now < WEEK


def test_time_shift_rejects_naive_moments() -> None:
    with pytest.raises(ValueError):
        time_shift.weeks_to_shift(datetime(2026, 1, 1), NOW)
    with pytest.raises(ValueError):
        time_shift.weeks_to_shift(NOW, datetime(2026, 1, 1))
    with pytest.raises(ValueError):
        time_shift.shift(datetime(2026, 1, 1), 1)


def test_shift_keeps_weekday_and_local_time() -> None:
    hanoi = datetime(2026, 3, 25, 18, 0, tzinfo=VN)
    lisbon = datetime(2026, 3, 25, 20, 30, tzinfo=LISBON)  # trước ngày đổi giờ mùa hè 29/03/2026

    for moment in (hanoi, lisbon):
        for weeks in (0, 1, 2, 30):
            moved = time_shift.shift(moment, weeks)
            assert moved.weekday() == moment.weekday()
            assert (moved.hour, moved.minute) == (moment.hour, moment.minute)
            assert moved.date() - moment.date() == timedelta(days=7 * weeks)

    assert time_shift.shift(hanoi, 3).utcoffset() == timedelta(hours=7)
    assert time_shift.shift(lisbon, 0).utcoffset() == timedelta(0)
    assert time_shift.shift(lisbon, 1).utcoffset() == timedelta(hours=1), "qua ngày đổi giờ, offset Lisbon đổi từ +00:00 sang +01:00"
    assert time_shift.shift(lisbon, 31).utcoffset() == timedelta(0), "31 tuần sau là 28/10/2026, đã hết mùa hè nên offset trở lại +00:00"


def test_shift_lands_nonexistent_local_time_on_valid_instant() -> None:
    # 01:30 ngày 29/03/2026 không tồn tại ở Lisbon (đồng hồ nhảy 01:00 lên 02:00); kết quả phải là một thời điểm hợp lệ
    before = datetime(2026, 3, 22, 1, 30, tzinfo=LISBON)

    moved = time_shift.shift(before, 1)

    assert moved.weekday() == before.weekday()
    assert moved.utcoffset() == timedelta(hours=1)
    assert moved.astimezone(UTC).replace(tzinfo=None) == datetime(2026, 3, 29, 1, 30), "01:30 (+00:00) được chuẩn hóa theo UTC thành 02:30 (+01:00)"
