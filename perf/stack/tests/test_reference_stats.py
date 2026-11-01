# Task 2.17: file thống kê tham chiếu rút từ CSV booking (data/private, không vào repo) chỉ có phân bố tổng hợp, đúng định dạng datagen đọc
import csv
import json
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import datagen  # noqa: E402
import reference_stats  # noqa: E402

_COLUMNS = ["booking_id", "branch_id", "customer_id", "source", "status", "created_at", "items", "final_total", "tier_rank", "member_since",
            "history_completed", "history_no_show", "history_cancelled", "history_late_cancelled", "history_payment_expired"]


def _item(court: str, day: str, start: str, end: str) -> dict:
    return {"court_id": court, "court_type_id": "t1", "date": day, "start_time": start, "end_time": end, "price": 100000}


def _write_csv(path: Path) -> None:
    rows = [
        # khách mới đặt 2 giờ tối thứ Hai, trước 1 ngày (created_at UTC 2026-01-04 15:00 = 22:00 VN ngày 04)
        ("b1", "br1", "c1", "ONLINE", "COMPLETED", "2026-01-04T15:00:00.000000Z", [_item("k1", "2026-01-05", "18:00:00", "20:00:00")], 0, 0, 0),
        # khách quen 3 lần hoàn tất, đặt 2 sân cùng ngày lúc 06:00
        ("b2", "br1", "c2", "ONLINE", "PAID_ONLINE", "2026-01-05T00:30:00.000000Z",
         [_item("k1", "2026-01-05", "06:00:00", "07:00:00"), _item("k2", "2026-01-05", "06:00:00", "07:00:00")], 3, 0, 0),
        # khách hay bỏ thanh toán, đơn tại quầy chi nhánh khác
        ("b3", "br2", "", "WALK_IN", "CANCELLED", "2026-01-06T10:00:00.000000Z", [_item("k9", "2026-01-08", "17:00:00", "18:00:00")], 0, 0, 2),
        # khách rủi ro: 2 lần không đến
        ("b4", "br2", "c4", "ONLINE", "NO_SHOW", "2026-01-06T11:00:00.000000Z", [_item("k9", "2026-01-09", "19:00:00", "20:00:00")], 1, 2, 0),
    ]
    with path.open("w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(_COLUMNS)
        for bid, branch, customer, source, status, created, items, completed, no_show, expired in rows:
            writer.writerow([bid, branch, customer, source, status, created, json.dumps(items), "100000", "", "", completed, no_show, 0, 0, expired])


def test_stats_are_aggregates_in_datagen_format(tmp_path):
    source = tmp_path / "bookings.csv"
    _write_csv(source)

    stats = reference_stats.build(source)

    assert stats["source"].startswith("REFERENCE")
    assert stats["sample"]["bookings"] == 4
    assert stats["hour_weights"] == {"6": 1, "17": 1, "18": 1, "19": 1}
    assert stats["duration_hours_weights"] == {"1": 3, "2": 1}
    assert stats["two_courts_share"] == 0.25
    assert stats["lead_time_days_weights"] == {"0": 1, "1": 1, "2": 1, "3": 1}
    weights = {p["name"]: p["weight"] for p in stats["customer_profiles"]}
    assert weights == {"moi": 0.25, "quen": 0.25, "thinh_thoang_vang": 0.0, "hay_bo_thanh_toan": 0.25, "rui_ro": 0.25}
    quen = next(p for p in stats["customer_profiles"] if p["name"] == "quen")
    assert quen["completed"] == [3, 3]


def test_stats_contain_no_identifier_from_the_csv(tmp_path):
    source = tmp_path / "bookings.csv"
    _write_csv(source)

    text = json.dumps(reference_stats.build(source))

    for identifier in ('"b1"', '"br1"', '"c1"', '"k1"', '"k9"', '"t1"', "2026-01"):
        assert identifier not in text


def test_datagen_runs_on_reference_stats(tmp_path):
    source = tmp_path / "bookings.csv"
    _write_csv(source)
    stats_file = tmp_path / "stats.json"
    stats_file.write_text(json.dumps(reference_stats.build(source)), encoding="utf-8")

    sql = datagen.generate(3, 50, datagen.date(2026, 11, 2), stats_file)

    assert "source=REFERENCE" in sql and "INSERT INTO public.bookings" in sql
