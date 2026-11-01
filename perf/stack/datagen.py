"""Bộ sinh dữ liệu benchmark tất định cho môi trường perf (Epic 6C, task 2.12, 2.13; P6-42).

    python perf/stack/datagen.py --seed 42 --customers 2000 [--anchor 2026-11-02] [--distribution FILE] > seed.sql

Cùng seed + cùng ngày mốc + cùng file phân bố ⇒ cùng từng byte SQL (UUID sinh bằng uuid5, thời điểm tính từ ngày mốc, không đọc đồng hồ
khi đã có --anchor). Ngày mốc là "hôm nay" theo giờ Việt Nam: lịch sử nằm trước, đơn có sẵn nằm sau, để lịch trống và quyết định TADSS giống
một ngày vận hành thật. Không có dữ liệu cá nhân: mọi khách là `perf-customer-NNNN@smashcourt.perf`, mật khẩu thử chung PERF_PASSWORD.

Sinh: hạng thành viên, khung giờ 1 tiếng, loại sân + giá hệ thống, chi nhánh + sân, dịch vụ, chính sách hủy, chủ hệ thống, quản lý và nhân
viên theo chi nhánh, khách kèm lịch sử theo hồ sơ (hoàn tất / không đến / hết hạn thanh toán: đúng thứ BE đếm gửi TADSS) và đơn có sẵn
trong `future_days` ngày tới chiếm `future_occupancy` khung giờ (để có xung đột sân như thật).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import random
import sys
import uuid
from dataclasses import dataclass, field
from datetime import date, datetime, time, timedelta, timezone
from pathlib import Path
from zoneinfo import ZoneInfo

VN = ZoneInfo("Asia/Ho_Chi_Minh")
PLACEHOLDER = Path(__file__).with_name("distribution.placeholder.json")
PERF_PASSWORD = "Perf@12345"
# BCrypt cost 12 (như AuthService) của PERF_PASSWORD: hằng không bí mật, chỉ có ở dữ liệu perf
PASSWORD_HASH = "$2b$12$ojCdr3JFXIzOM7tYET2c4.yauitJ945GuGxSEtFlcJX4kMhHQJS36"
EMAIL_DOMAIN = "smashcourt.perf"
_NAMESPACE = uuid.UUID("6f1c2a52-6c0e-4d39-9b52-5f2a7f0c6e10")
_CHUNK = 500

# Giá theo giờ (VND): giờ thấp điểm, cao điểm (16–22 giờ ngày thường) và cuối tuần
_PRICES = {"standard": (80_000, 120_000, 140_000), "vip": (120_000, 180_000, 200_000)}
_TIERS = [("Đồng", 0, 0), ("Bạc", 100, 3), ("Vàng", 300, 5)]
_SERVICES = [("Nước suối", "chai", 10_000), ("Cầu lông (ống)", "ống", 250_000), ("Thuê vợt", "lượt", 30_000)]
_CANCEL_POLICIES = [(24, 100), (12, 50), (0, 0)]


@dataclass
class Sql:
    """Gom các dòng theo bảng, giữ thứ tự chèn theo khóa ngoại."""
    tables: dict[str, tuple[list[str], list[list[object]]]] = field(default_factory=dict)

    def add(self, table: str, columns: list[str], row: list[object]) -> None:
        cols, rows = self.tables.setdefault(table, (columns, []))
        assert cols == columns, table
        rows.append(row)

    def render(self, header: list[str]) -> str:
        out = [f"-- {line}" for line in header] + ["BEGIN;"]
        for table, (columns, rows) in self.tables.items():
            for start in range(0, len(rows), _CHUNK):
                values = ",\n".join("(" + ", ".join(_literal(v) for v in row) + ")" for row in rows[start:start + _CHUNK])
                out.append(f"INSERT INTO public.{table} ({', '.join(columns)}) VALUES\n{values};")
        out.append("COMMIT;")
        return "\n".join(out) + "\n"


def _literal(value: object) -> str:
    if value is None:
        return "NULL"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        return str(value)
    if isinstance(value, datetime):
        return f"'{value.astimezone(timezone.utc).strftime('%Y-%m-%d %H:%M:%S+00')}'"
    if isinstance(value, (date, time, uuid.UUID)):
        return f"'{value}'"
    return "'" + str(value).replace("'", "''") + "'"


class Generator:
    def __init__(self, seed: int, customers: int, anchor: date, distribution: dict) -> None:
        self.seed, self.customers, self.anchor, self.dist = seed, customers, anchor, distribution
        self.rng = random.Random(seed)
        self.sql = Sql()
        # Thời điểm tạo dữ liệu nền: nửa đêm giờ VN, 200 ngày trước ngày mốc (trước mọi lịch sử)
        self.base_time = datetime.combine(anchor - timedelta(days=200), time(0, 0), VN)
        self.booking_seq = 0
        self.used_slots: set[tuple[uuid.UUID, date, int]] = set()

    def uid(self, kind: str, *parts: object) -> uuid.UUID:
        return uuid.uuid5(_NAMESPACE, ":".join(map(str, (self.seed, kind, *parts))))

    # ── dữ liệu nền ────────────────────────────────────────────────────────
    def reference(self) -> None:
        t0 = self.base_time
        for name, points, rate in _TIERS:
            self.sql.add("loyalty_tiers", ["id", "name", "min_points", "discount_rate", "created_at", "updated_at"],
                         [self.uid("tier", points), name, points, rate, t0, t0])
        self.slots: dict[tuple[str, int], uuid.UUID] = {}
        for day_type in ("WEEKDAY", "WEEKEND"):
            for hour in range(24):
                slot_id = self.uid("slot", day_type, hour)
                self.slots[(day_type, hour)] = slot_id
                end = time(23, 59, 59) if hour == 23 else time(hour + 1, 0)
                self.sql.add("time_slots", ["id", "start_time", "end_time", "day_type", "created_at"], [slot_id, time(hour, 0), end, day_type, t0])
        self.court_types = {}
        for key, label in (("standard", "Sân tiêu chuẩn"), ("vip", "Sân VIP")):
            type_id = self.uid("court_type", key)
            self.court_types[key] = type_id
            self.sql.add("court_types", ["id", "name", "description", "status", "created_at", "updated_at"],
                         [type_id, label, "Dữ liệu benchmark perf", "ACTIVE", t0, t0])
            for (day_type, hour), slot_id in self.slots.items():
                self.sql.add("system_prices", ["id", "court_type_id", "time_slot_id", "price", "effective_from", "created_at"],
                             [self.uid("price", key, day_type, hour), type_id, slot_id, self.price(key, day_type, hour), date(2020, 1, 1), t0])
        for hours, percent in _CANCEL_POLICIES:
            self.sql.add("cancel_policies", ["id", "hours_before", "refund_percent", "description", "created_at", "updated_at"],
                         [self.uid("cancel_policy", hours), hours, percent, f"Hủy trước {hours} giờ: hoàn {percent} %", t0, t0])
        services = []
        for index, (name, unit, price) in enumerate(_SERVICES):
            service_id = self.uid("service", index)
            services.append((service_id, price))
            self.sql.add("services", ["id", "name", "description", "unit", "default_price", "status", "created_at", "updated_at"],
                         [service_id, name, None, unit, price, "ACTIVE", t0, t0])

        open_hour, close_hour = self.dist["open_hours"]
        self.branches: list[uuid.UUID] = []
        self.courts: list[tuple[uuid.UUID, uuid.UUID, str]] = []  # (court, branch, loại)
        for b in range(self.dist["branches"]):
            branch_id = self.uid("branch", b)
            self.branches.append(branch_id)
            self.sql.add("branches", ["id", "name", "address", "phone", "open_time", "close_time", "status", "created_at", "updated_at"],
                         [branch_id, f"SmashCourt Perf {b + 1}", f"{b + 1} Đường Benchmark, TP.HCM", f"028000000{b + 1:02d}",
                          time(open_hour, 0), time(close_hour, 0), "ACTIVE", t0, t0])
            for key, type_id in self.court_types.items():
                self.sql.add("branch_court_types", ["id", "branch_id", "court_type_id", "is_active", "created_at"],
                             [self.uid("branch_court_type", b, key), branch_id, type_id, True, t0])
            number = 0
            for key in ("standard", "vip"):
                for _ in range(self.dist["courts_per_branch"][key]):
                    number += 1
                    court_id = self.uid("court", b, number)
                    self.courts.append((court_id, branch_id, key))
                    self.sql.add("courts", ["id", "branch_id", "court_type_id", "name", "status", "created_at", "updated_at"],
                                 [court_id, branch_id, self.court_types[key], f"Sân {number}{' VIP' if key == 'vip' else ''}", "AVAILABLE", t0, t0])
            for index, (service_id, price) in enumerate(services):
                self.sql.add("branch_services", ["id", "branch_id", "service_id", "price", "status", "created_at", "updated_at"],
                             [self.uid("branch_service", b, index), branch_id, service_id, price, "ENABLED", t0, t0])

    def price(self, court_key: str, day_type: str, hour: int) -> int:
        off_peak, peak, weekend = _PRICES[court_key]
        if day_type == "WEEKEND":
            return weekend
        return peak if 16 <= hour < 22 else off_peak

    # ── người dùng ─────────────────────────────────────────────────────────
    def user(self, kind: str, index: int, role: str, name: str, email: str) -> uuid.UUID:
        user_id = self.uid("user", kind, index)
        t0 = self.base_time
        phone = f"09{int(hashlib.sha256(str(user_id).encode()).hexdigest(), 16) % 10**8:08d}"
        self.sql.add("users", ["id", "email", "password_hash", "full_name", "phone", "role", "status", "is_email_verified", "created_at",
                               "updated_at", "full_name_normalized"],
                     [user_id, email, PASSWORD_HASH, name, phone, role, "ACTIVE", True, t0, t0, _normalize(name)])
        return user_id

    def people(self) -> None:
        self.owner = self.user("owner", 0, "OWNER", "Chủ hệ thống Perf", f"perf-owner@{EMAIL_DOMAIN}")
        self.managers, self.staff = [], []
        for b, branch_id in enumerate(self.branches):
            manager = self.user("manager", b, "BRANCH_MANAGER", f"Quản lý Perf {b + 1}", f"perf-manager-{b + 1}@{EMAIL_DOMAIN}")
            self.managers.append(manager)
            self.branch_user(manager, branch_id, "MANAGER")
            for s in range(self.dist["staff_per_branch"]):
                staff = self.user("staff", f"{b}-{s}", "STAFF", f"Nhân viên Perf {b + 1}.{s + 1}", f"perf-staff-{b + 1}-{s + 1}@{EMAIL_DOMAIN}")
                self.staff.append((staff, branch_id))
                self.branch_user(staff, branch_id, "STAFF")

    def branch_user(self, user_id: uuid.UUID, branch_id: uuid.UUID, role: str) -> None:
        self.sql.add("user_branches", ["id", "user_id", "branch_id", "role", "is_active", "assigned_at"],
                     [self.uid("user_branch", user_id), user_id, branch_id, role, True, self.base_time])

    # ── khách và lịch sử ───────────────────────────────────────────────────
    def customers_and_history(self) -> None:
        profiles = self.dist["customer_profiles"]
        weights = [p["weight"] for p in profiles]
        for i in range(1, self.customers + 1):
            customer = self.user("customer", i, "CUSTOMER", f"Khách Perf {i:04d}", f"perf-customer-{i:04d}@{EMAIL_DOMAIN}")
            profile = self.rng.choices(profiles, weights)[0]
            counts = {kind: self.rng.randint(*profile[kind]) for kind in ("completed", "no_show", "payment_expired")}
            for kind, count in counts.items():
                for _ in range(count):
                    self.past_booking(customer, kind)
            points = counts["completed"] * 30
            tier_points = max(p for _, p, _ in _TIERS if p <= points)
            self.sql.add("customer_loyalty", ["id", "user_id", "tier_id", "total_points", "created_at", "updated_at"],
                         [self.uid("loyalty", customer), customer, self.uid("tier", tier_points), points, self.base_time, self.base_time])

    def pick_slot(self, day: date, branch: uuid.UUID | None = None) -> tuple[list[tuple[uuid.UUID, uuid.UUID, str]], int, int] | None:
        """Chọn sân + giờ bắt đầu + số giờ còn trống (không chồng lên dòng booking_courts đang hoạt động)."""
        open_hour, close_hour = self.dist["open_hours"]
        hours = {int(h): w for h, w in self.dist["hour_weights"].items()}
        durations = {int(d): w for d, w in self.dist["duration_hours_weights"].items()}
        for _ in range(20):
            start = self.rng.choices(list(hours), list(hours.values()))[0]
            duration = min(self.rng.choices(list(durations), list(durations.values()))[0], close_hour - start)
            pool = [c for c in self.courts if branch is None or c[1] == branch]
            first = self.rng.choice(pool)
            courts = [first]
            if self.rng.random() < self.dist["two_courts_share"]:
                same_branch = [c for c in pool if c[1] == first[1] and c[0] != first[0]]
                courts.append(self.rng.choice(same_branch))
            if all((c[0], day, h) not in self.used_slots for c in courts for h in range(start, start + duration)):
                return courts, start, duration
        return None

    def past_booking(self, customer: uuid.UUID, kind: str) -> None:
        day = self.anchor - timedelta(days=self.rng.randint(3, self.dist["history_days"]))
        picked = self.pick_slot(day)
        if picked is None:
            return
        courts, start, duration = picked
        status, active, cancel_reason = {
            "completed": ("COMPLETED", True, None),
            "no_show": ("NO_SHOW", True, None),
            "payment_expired": ("CANCELLED", False, "PAYMENT_EXPIRED"),
        }[kind]
        created = datetime.combine(day - timedelta(days=self.rng.randint(0, 5)), time(9, 0), VN)
        self.booking(customer, None, courts, day, start, duration, status, "ONLINE", active, cancel_reason, created)

    def future_bookings(self) -> None:
        """Đơn có sẵn (khách tại quầy, đã xác nhận) chiếm một phần khung giờ của những ngày tới."""
        open_hour, close_hour = self.dist["open_hours"]
        slots_per_day = len(self.courts) * (close_hour - open_hour)
        for offset in range(1, self.dist["future_days"] + 1):
            day = self.anchor + timedelta(days=offset)
            target = int(slots_per_day * self.dist["future_occupancy"])
            taken = 0
            for _ in range(target * 3):
                if taken >= target:
                    break
                picked = self.pick_slot(day)
                if picked is None:
                    continue
                courts, start, duration = picked
                staff = [s for s, b in self.staff if b == courts[0][1]]
                created = datetime.combine(self.anchor - timedelta(days=1), time(10, 0), VN)
                self.booking(None, self.rng.choice(staff), courts, day, start, duration, "CONFIRMED", "WALK_IN", True, None, created)
                taken += duration * len(courts)

    def booking(self, customer: uuid.UUID | None, staff: uuid.UUID | None, courts: list, day: date, start: int, duration: int,
                status: str, source: str, active: bool, cancel_reason: str | None, created: datetime) -> None:
        self.booking_seq += 1
        n = self.booking_seq
        booking_id = self.uid("booking", n)
        branch = courts[0][1]
        day_type = "WEEKEND" if day.weekday() >= 5 else "WEEKDAY"
        cancelled_at = created + timedelta(minutes=15) if status == "CANCELLED" else None
        self.sql.add("bookings", ["id", "branch_id", "customer_id", "guest_name", "guest_phone", "booking_date", "status", "source", "created_by",
                                  "cancelled_at", "cancel_source", "cancel_reason", "created_at", "updated_at", "booking_code"],
                     [booking_id, branch, customer, None if customer else f"Khách lẻ Perf {n}", None if customer else "0900000000", day, status,
                      source, staff, cancelled_at, "SYSTEM" if cancelled_at else None, cancel_reason, created, cancelled_at or created,
                      f"PF-{n:06d}"])
        court_fee = 0
        for index, (court_id, _, key) in enumerate(courts):
            if active:
                self.used_slots.update((court_id, day, h) for h in range(start, start + duration))
            booking_court = self.uid("booking_court", n, index)
            self.sql.add("booking_courts", ["id", "booking_id", "court_id", "date", "start_time", "end_time", "is_active", "created_at"],
                         [booking_court, booking_id, court_id, day, time(start, 0), time(start + duration, 0), active, created])
            for h in range(start, start + duration):
                price = self.price(key, day_type, h)
                court_fee += price
                self.sql.add("booking_price_items", ["id", "booking_court_id", "time_slot_id", "unit_price", "created_at"],
                             [self.uid("price_item", n, index, h), booking_court, self.slots[(day_type, h)], price, created])
        invoice_status = {"COMPLETED": "PAID", "NO_SHOW": "UNPAID", "CANCELLED": "EXPIRED", "CONFIRMED": "UNPAID"}[status]
        invoice_id = self.uid("invoice", n)
        self.sql.add("invoices", ["id", "booking_id", "court_fee", "final_total", "payment_status", "created_at", "updated_at", "invoice_code"],
                     [invoice_id, booking_id, court_fee, court_fee, invoice_status, created, created, f"PF-INV-{n:06d}"])
        if status == "COMPLETED":
            paid = datetime.combine(day, time(start + duration, 0), VN)
            self.sql.add("payments", ["id", "invoice_id", "method", "amount", "status", "paid_at", "created_at", "updated_at"],
                         [self.uid("payment", n), invoice_id, "CASH", court_fee, "SUCCESS", paid, paid, paid])

    def build(self) -> str:
        self.reference()
        self.people()
        self.customers_and_history()
        self.future_bookings()
        rows = sum(len(rows) for _, rows in self.sql.tables.values())
        header = [
            "Dữ liệu benchmark perf: SINH TỰ ĐỘNG bởi perf/stack/datagen.py, không sửa tay.",
            f"seed={self.seed} customers={self.customers} anchor={self.anchor} source={self.dist['source']} rows={rows} bookings={self.booking_seq}",
        ]
        # Mã đơn/hóa đơn do BE sinh (BK-..., INV-...) dùng sequence riêng, không đụng mã PF-...
        return self.sql.render(header)


def _normalize(text: str) -> str:
    import unicodedata
    stripped = unicodedata.normalize("NFD", text.replace("đ", "d").replace("Đ", "D"))
    return "".join(ch for ch in stripped if unicodedata.category(ch) != "Mn").lower()


def generate(seed: int, customers: int, anchor: date, distribution_path: Path = PLACEHOLDER) -> str:
    distribution = json.loads(distribution_path.read_text(encoding="utf-8"))
    return Generator(seed, customers, anchor, distribution).build()


def checksum(sql: str) -> str:
    return hashlib.sha256(sql.encode("utf-8")).hexdigest()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--customers", type=int, default=2000)
    parser.add_argument("--anchor", type=date.fromisoformat, default=None, help="ngày mốc (mặc định: hôm nay giờ VN)")
    parser.add_argument("--distribution", type=Path, default=PLACEHOLDER)
    parser.add_argument("--checksum", action="store_true", help="chỉ in SHA-256 của SQL")
    args = parser.parse_args(argv)
    anchor = args.anchor or datetime.now(VN).date()
    sql = generate(args.seed, args.customers, anchor, args.distribution)
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    print(checksum(sql) if args.checksum else sql, end="" if not args.checksum else "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
