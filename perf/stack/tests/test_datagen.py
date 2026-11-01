# Task 2.13: bộ sinh dữ liệu tất định (cùng seed ⇒ cùng checksum), không trùng sân, đúng ràng buộc schema mà không cần DB
import re
import sys
from datetime import date
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import datagen  # noqa: E402

ANCHOR = date(2026, 11, 2)


def _gen(seed: int = 7, customers: int = 120) -> datagen.Generator:
    distribution = datagen.json.loads(datagen.PLACEHOLDER.read_text(encoding="utf-8"))
    generator = datagen.Generator(seed, customers, ANCHOR, distribution)
    generator.build()
    return generator


def test_same_seed_gives_same_checksum_and_other_seed_differs():
    first = datagen.checksum(datagen.generate(7, 120, ANCHOR))
    assert first == datagen.checksum(datagen.generate(7, 120, ANCHOR))
    assert first != datagen.checksum(datagen.generate(8, 120, ANCHOR))


def test_placeholder_distribution_is_labelled_in_the_output():
    assert "source=PLACEHOLDER" in datagen.generate(7, 10, ANCHOR)


def test_active_booking_courts_never_overlap_on_the_same_court():
    generator = _gen()
    columns, rows = generator.sql.tables["booking_courts"]
    court, day, start, end, active = (columns.index(c) for c in ("court_id", "date", "start_time", "end_time", "is_active"))
    seen: set[tuple] = set()
    for row in rows:
        if not row[active]:
            continue
        for hour in range(row[start].hour, row[end].hour):
            key = (row[court], row[day], hour)
            assert key not in seen, key
            seen.add(key)


def test_history_is_in_the_past_and_existing_bookings_in_the_future():
    generator = _gen()
    columns, rows = generator.sql.tables["bookings"]
    status, day = columns.index("status"), columns.index("booking_date")
    for row in rows:
        if row[status] == "CONFIRMED":
            assert row[day] > ANCHOR
        else:
            assert row[day] < ANCHOR


def test_rows_respect_schema_checks():
    generator = _gen()
    b_cols, bookings = generator.sql.tables["bookings"]
    for row in bookings:
        record = dict(zip(b_cols, row))
        cancelled = record["status"] == "CANCELLED"
        assert (record["cancelled_at"] is not None) == cancelled  # chk_bookings_cancelled_at
        assert record["cancel_reason"] is None or cancelled  # chk_bookings_cancel_reason
        assert record["customer_id"] or record["guest_name"]  # chk_bookings_guest_info
        assert len(record["booking_code"]) <= 20 and record["booking_code"].startswith("PF-")
    i_cols, invoices = generator.sql.tables["invoices"]
    for row in invoices:
        record = dict(zip(i_cols, row))
        assert record["final_total"] == record["court_fee"] > 0  # chk_invoices_final_total_formula (không giảm giá, không dịch vụ)
    codes = [row[b_cols.index("booking_code")] for row in bookings]
    assert len(codes) == len(set(codes))


def test_customers_have_history_mix_the_strategy_reads():
    generator = _gen(customers=400)
    columns, rows = generator.sql.tables["bookings"]
    statuses = {row[columns.index("status")] for row in rows}
    assert {"COMPLETED", "NO_SHOW", "CANCELLED", "CONFIRMED"} <= statuses
    u_cols, users = generator.sql.tables["users"]
    roles = [row[u_cols.index("role")] for row in users]
    assert roles.count("CUSTOMER") == 400 and roles.count("OWNER") == 1 and roles.count("BRANCH_MANAGER") == 3


def test_sql_escapes_quotes_and_has_no_personal_data():
    assert datagen._literal("O'Neil") == "'O''Neil'"
    sql = datagen.generate(7, 30, ANCHOR)
    emails = set(re.findall(r"'([^']+@[^']+)'", sql))
    assert emails and all(email.endswith("@smashcourt.perf") for email in emails)
