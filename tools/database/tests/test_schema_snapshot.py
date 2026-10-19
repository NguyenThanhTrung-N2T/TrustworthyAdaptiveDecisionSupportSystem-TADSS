"""Test cho schema_snapshot.py trên bản dump nhỏ viết tay (không cần PostgreSQL)."""
from __future__ import annotations

from pathlib import Path

import pytest

import schema_snapshot as snap

BEFORE = """﻿--
\\restrict abc123
-- Dumped from database version 17.11
-- Dumped by pg_dump version 17.7
SET statement_timeout = 0;

--
-- Name: booking_status; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.booking_status AS ENUM (
    'PENDING',
    'CANCELLED'
);


ALTER TYPE public.booking_status OWNER TO neondb_owner;

--
-- Name: bookings; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.bookings (
    id uuid NOT NULL,
    status public.booking_status NOT NULL,
    cancel_source public.cancel_source_enum,
    cancelled_at timestamp with time zone,
    CONSTRAINT chk_bookings_cancelled_at CHECK ((cancelled_at IS NOT NULL))
);

--
-- Name: slot_locks uq_slot_locks_booking; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.slot_locks
    ADD CONSTRAINT uq_slot_locks_booking UNIQUE (booking_id);
"""


def _after() -> str:
    """BEFORE sau khi áp đủ script Phase 6 (dạng pg_dump)."""
    text = BEFORE.replace("\\restrict abc123", "\\restrict zzz999").replace("    'CANCELLED'\n", "    'CANCELLED',\n    'PENDING_APPROVAL'\n")
    columns = [f"    {c} text," for c in snap.PHASE6_COLUMNS["bookings"] if c != "cancel_reason"]
    text = text.replace("    cancelled_at timestamp with time zone,\n",
                        "    cancelled_at timestamp with time zone,\n    cancel_reason public.booking_cancel_reason,\n" + "\n".join(columns) + "\n")
    text = text.split("--\n-- Name: slot_locks uq_slot_locks_booking")[0]
    invoice_columns = ",\n".join(f"    {c} text" for c in ("id", *snap.PHASE6_COLUMNS["invoices"]))
    blocks = [f"--\n-- Name: invoices; Type: TABLE; Schema: public; Owner: o\n--\n\nCREATE TABLE public.invoices (\n{invoice_columns}\n);\n"]
    blocks += [f"--\n-- Name: {n}; Type: TYPE; Schema: public; Owner: o\n--\n\nCREATE TYPE public.{n} AS ENUM ('A');\n" for n in snap.PHASE6_TYPES]
    blocks += [f"--\n-- Name: bookings {n}; Type: FK CONSTRAINT; Schema: public; Owner: o\n--\n\nALTER TABLE ONLY public.bookings\n    ADD CONSTRAINT {n} CHECK (true);\n"
               for n in snap.PHASE6_CONSTRAINTS]
    blocks += [f"--\n-- Name: {n}; Type: INDEX; Schema: public; Owner: o\n--\n\nCREATE INDEX {n} ON public.bookings USING btree (id);\n" for n in snap.PHASE6_INDEXES]
    return text + "\n".join(blocks)


def test_normalize_drops_dump_noise():
    text = snap.normalize(BEFORE)
    assert "\\restrict" not in text and "Dumped" not in text and not text.startswith("﻿")


def test_parse_objects_keys_and_bodies_skip_owner_and_comments():
    objects = snap.parse_objects(BEFORE)
    assert set(objects) == {("TYPE", "public", "booking_status"), ("TABLE", "public", "bookings"),
                            ("CONSTRAINT", "public", "slot_locks uq_slot_locks_booking")}
    assert "OWNER TO" not in objects[("TYPE", "public", "booking_status")]


def test_table_parts_and_enum_values():
    objects = snap.parse_objects(BEFORE)
    columns, constraints = snap.table_parts(objects[("TABLE", "public", "bookings")])
    assert list(columns) == ["id", "status", "cancel_source", "cancelled_at"]
    assert columns["id"] == "uuid NOT NULL"
    assert list(constraints) == ["chk_bookings_cancelled_at"]
    assert snap.enum_values(objects[("TYPE", "public", "booking_status")]) == ["PENDING", "CANCELLED"]


def test_constraint_names_include_inline_and_altered():
    assert snap.constraint_names(snap.parse_objects(BEFORE)) == {"chk_bookings_cancelled_at", "uq_slot_locks_booking"}


@pytest.mark.parametrize(("text", "status"), [(BEFORE, "chưa áp"), (_after(), "đã áp đủ")], ids=["before", "after"])
def test_phase6_status(text, status):
    rows = snap.phase6_rows(snap.parse_objects(text))
    assert snap.phase6_status(rows) == status
    assert all(ok for label, ok in rows if label.startswith("[có sẵn]"))


def test_phase6_status_partial_when_only_enum_added():
    text = BEFORE.replace("    'CANCELLED'\n", "    'CANCELLED',\n    'PENDING_APPROVAL'\n")
    assert snap.phase6_status(snap.phase6_rows(snap.parse_objects(text))) == "áp dở"


def test_compare_reports_columns_enum_and_objects():
    only_left, only_right, changed = snap.compare(snap.parse_objects(BEFORE), snap.parse_objects(_after()))
    assert only_left == [("CONSTRAINT", "public", "slot_locks uq_slot_locks_booking")]
    assert ("INDEX", "public", "idx_bookings_pending_approval") in only_right
    details = dict(changed)
    assert "+ cột `cancel_reason` public.booking_cancel_reason" in details[("TABLE", "public", "bookings")]
    assert details[("TYPE", "public", "booking_status")] == ["giá trị: PENDING, CANCELLED → PENDING, CANCELLED, PENDING_APPROVAL"]


def test_compare_identical_after_noise_differs():
    assert snap.compare(snap.parse_objects(BEFORE), snap.parse_objects(BEFORE.replace("abc123", "other"))) == ([], [], [])


def test_code_migrations_ignores_designer_and_snapshot(tmp_path: Path):
    for name in ("20260912105336_InitialCreate.cs", "20260912105336_InitialCreate.Designer.cs", "TadssDbContextModelSnapshot.cs"):
        (tmp_path / name).write_text("", encoding="utf-8")
    assert snap.code_migrations(tmp_path) == ["20260912105336_InitialCreate"]


def test_build_report_lists_status_migrations_and_diff():
    sources = {"repo": snap.parse_objects(BEFORE), "new": snap.parse_objects(_after())}
    report = snap.build_report(sources, {"new": None}, "20261019-000000")
    assert "| **Trạng thái** | chưa áp | đã áp đủ |" in report
    assert "không đọc được `tadss.__EFMigrationsHistory`" in report
    assert "## `repo` → `new`" in report


@pytest.mark.parametrize(("spec", "expected"), [("new", ("new", None)), ("old=a/b.sql", ("old", Path("a/b.sql")))])
def test_parse_target(spec, expected):
    assert snap.parse_target(spec) == expected


@pytest.mark.parametrize("spec", ["repo", "bad name", "x;y=a.sql"])
def test_parse_target_rejects_invalid(spec):
    with pytest.raises(SystemExit):
        snap.parse_target(spec)


def test_read_env_file_parses_keys_and_skips_comments(tmp_path: Path):
    path = tmp_path / "db-urls.env"
    path.write_text("# ghi chú\n\nSCHEMA_DB_URL_TADSS_DEV=\"postgresql://a@h/db?sslmode=require\"\nSCHEMA_DB_URL_MAIN=x=y\n", encoding="utf-8")
    assert snap.read_env_file(path) == {"SCHEMA_DB_URL_TADSS_DEV": "postgresql://a@h/db?sslmode=require", "SCHEMA_DB_URL_MAIN": "x=y"}
    assert snap.read_env_file(tmp_path / "khong-co.env") == {}


def test_connection_for_prefers_environment_then_file(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.delenv("SCHEMA_DB_URL_TADSS_DEV", raising=False)
    assert snap.connection_for("tadss-dev", {"SCHEMA_DB_URL_TADSS_DEV": "from-file"}) == "from-file"
    monkeypatch.setenv("SCHEMA_DB_URL_TADSS_DEV", "from-env")
    assert snap.connection_for("tadss-dev", {"SCHEMA_DB_URL_TADSS_DEV": "from-file"}) == "from-env"
