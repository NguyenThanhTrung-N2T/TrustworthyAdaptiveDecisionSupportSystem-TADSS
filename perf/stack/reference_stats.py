"""Rút file thống kê tham chiếu cho bộ sinh dữ liệu perf từ CSV booking SmashCourt (Epic 6C, task 2.17; P6-42, plan 6C Phụ lục B.6.4).

    python perf/stack/reference_stats.py [data/private/smashcourt_bookings.csv] > perf/stack/distribution.reference.json

Đầu vào: CSV của `tools/evidence/sql/export_smashcourt_bookings.sql` (gói 5.6; chỉ ở data/private/, gitignore, không có tên, điện thoại, email).
Đầu ra: CHỈ phân bố tổng hợp (giờ bắt đầu, thời lượng, số sân/đơn, thời gian đặt trước, hồ sơ lịch sử khách lúc đặt), cùng định dạng với
`distribution.placeholder.json` nên datagen đọc thẳng; không có định danh, ngày cụ thể hay số tiền của đơn nào ⇒ được commit.
Phần cấu trúc (số chi nhánh, sân, giờ mở cửa, tỷ lệ lấp lịch tương lai) giữ của bản placeholder: CSV không đủ để suy ra (ghi ở `derived`).
"""
from __future__ import annotations

import csv
import json
import sys
from collections import Counter
from datetime import date, datetime, time
from pathlib import Path
from zoneinfo import ZoneInfo

VN = ZoneInfo("Asia/Ho_Chi_Minh")
PLACEHOLDER = Path(__file__).with_name("distribution.placeholder.json")
DEFAULT_CSV = Path(__file__).resolve().parents[2] / "data" / "private" / "smashcourt_bookings.csv"
_HISTORY = ("completed", "no_show", "payment_expired")


def profile_of(completed: int, no_show: int, payment_expired: int) -> str:
    """Nhóm hồ sơ lịch sử của khách lúc đặt (cùng tên với placeholder); xét theo thứ tự rủi ro giảm dần."""
    if no_show >= 2:
        return "rui_ro"
    if no_show == 1:
        return "thinh_thoang_vang"
    if payment_expired >= 2:
        return "hay_bo_thanh_toan"
    if completed >= 1:
        return "quen"
    return "moi"


def _hours(start: str, end: str) -> int:
    s, e = time.fromisoformat(start), time.fromisoformat(end)
    minutes = (e.hour * 60 + e.minute) - (s.hour * 60 + s.minute)
    return max(1, -(-minutes // 60))   # làm tròn lên giờ


def build(csv_path: Path, placeholder: Path = PLACEHOLDER) -> dict:
    base = json.loads(placeholder.read_text(encoding="utf-8"))
    with csv_path.open(encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f))
    if not rows:
        raise ValueError(f"{csv_path.name}: không có dòng nào")

    hours, durations, leads, profiles, statuses = Counter(), Counter(), Counter(), Counter(), Counter()
    ranges: dict[str, dict[str, list[int]]] = {}
    two_courts = online = 0
    for row in rows:
        items = sorted(json.loads(row["items"]), key=lambda i: (i["date"], i["start_time"]))
        first = items[0]
        hours[str(time.fromisoformat(first["start_time"]).hour)] += 1
        durations[str(_hours(first["start_time"], first["end_time"]))] += 1
        two_courts += len({i["court_id"] for i in items}) >= 2
        created_vn = datetime.fromisoformat(row["created_at"].replace("Z", "+00:00")).astimezone(VN).date()
        leads[str(max(0, (date.fromisoformat(first["date"]) - created_vn).days))] += 1
        counts = {k: int(row[f"history_{k}"] or 0) for k in _HISTORY}
        name = profile_of(**counts)
        profiles[name] += 1
        span = ranges.setdefault(name, {k: [v, v] for k, v in counts.items()})
        for k, v in counts.items():
            span[k] = [min(span[k][0], v), max(span[k][1], v)]
        statuses[row["status"]] += 1
        online += row["source"] == "ONLINE"

    total = len(rows)
    order = [p["name"] for p in base["customer_profiles"]]
    customer_profiles = [{"name": name, "weight": round(profiles[name] / total, 4), **ranges.get(name, {k: [0, 0] for k in _HISTORY})}
                         for name in order]
    by_int = lambda counter: dict(sorted(counter.items(), key=lambda kv: int(kv[0])))  # noqa: E731
    return {
        "_comment": "SINH TỰ ĐỘNG bởi perf/stack/reference_stats.py từ CSV booking SmashCourt (data/private, không vào repo). Chỉ phân bố tổng hợp; "
                    "phần cấu trúc giữ của distribution.placeholder.json (xem derived).",
        "source": f"REFERENCE ({total} booking)",
        "sample": {"bookings": total, "online_share": round(online / total, 4), "final_status": dict(sorted(statuses.items()))},
        "derived": ["customer_profiles", "hour_weights", "duration_hours_weights", "two_courts_share", "lead_time_days_weights"],
        **{key: base[key] for key in ("branches", "courts_per_branch", "open_hours", "staff_per_branch", "history_days", "future_days",
                                      "future_occupancy")},
        "customer_profiles": customer_profiles,
        "hour_weights": by_int(hours),
        "duration_hours_weights": by_int(durations),
        "two_courts_share": round(two_courts / total, 4),
        "lead_time_days_weights": by_int(leads),
    }


def main(argv: list[str]) -> int:
    path = Path(argv[1]) if len(argv) > 1 else DEFAULT_CSV
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    print(json.dumps(build(path), ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
