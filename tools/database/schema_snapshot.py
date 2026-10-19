"""Chụp và so sánh schema PostgreSQL (Phase 6): dump nhiều branch Neon một lượt, so với bản trong repo, kiểm script Phase 6.

    python tools/database/schema_snapshot.py --target old --target new
    python tools/database/schema_snapshot.py --target new=D:/tmp/new.sql      # dùng file dump có sẵn, không kết nối

Mỗi `--target NAME` lấy chuỗi kết nối từ biến `SCHEMA_DB_URL_<NAME>` (viết hoa, ký tự khác chữ/số thành `_`): biến môi trường,
rồi file `data/private/db-urls.env` (gitignore, dạng `KEY=VALUE`); không có thì hỏi (nhập ẩn).
Chuỗi kết nối không ghi ra file, không in ra màn hình. Chỉ đọc DB (pg_dump --schema-only + một SELECT bảng migration TADSS).
Kết quả ở `data/private/schema-snapshots/<thời điểm>/` (gitignore): `<name>.sql`, `<a>_vs_<b>.diff`, `report.md`.
Dump với `--no-privileges` như `docs/database/schema_smashcourt.sql` (quyền riêng của Neon như `neon_superuser` không nạp được ở
PostgreSQL khác và làm hỏng `tools/evidence/sql/verify-export.sh`); file `<name>.sql` của target dùng để cập nhật bản trong repo.
So sánh theo từng đối tượng (bảng, cột, enum, ràng buộc, index...) dựa trên dòng tiêu đề `-- Name: …; Type: …; Schema: …` của pg_dump.
"""
from __future__ import annotations

import argparse
import difflib
import getpass
import glob
import os
import re
import shutil
import subprocess
import sys
from datetime import datetime
from itertools import combinations
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_BASELINE = REPO_ROOT / "docs" / "database" / "schema_smashcourt.sql"
DEFAULT_OUT = REPO_ROOT / "data" / "private" / "schema-snapshots"
DEFAULT_ENV_FILE = REPO_ROOT / "data" / "private" / "db-urls.env"
MIGRATIONS_DIR = REPO_ROOT / "src" / "TADSS.Infrastructure" / "Persistence" / "Migrations"
BASELINE_NAME = "repo"

# Những gì script Phase 6 (spec PHASE6_DESIGN_SPEC Mục 6.2) thêm/bỏ
PHASE6_ENUM_VALUES = {"booking_status": ("PENDING_APPROVAL",)}
PHASE6_TYPES = ("deposit_source", "approval_result", "booking_cancel_reason")
PHASE6_COLUMNS = {
    "bookings": ("tadss_decision_id", "approval_expires_at", "approval_reason_code", "approval_reasons", "approval_result",
                 "approval_resolved_by", "approval_resolved_at", "approval_resolution_note", "cancel_reason",
                 "tadss_outcome_reported_at"),
    "invoices": ("deposit_rate", "deposit_amount", "deposit_source", "hold_deadline"),
}
PHASE6_CONSTRAINTS = ("fk_bookings_approval_resolved_by", "chk_bookings_pending_approval", "chk_bookings_approval_resolution",
                      "chk_bookings_cancel_reason", "chk_invoices_deposit_rate_range", "chk_invoices_deposit_amount_positive",
                      "chk_invoices_deposit_fields")
PHASE6_INDEXES = ("uq_bookings_tadss_decision", "idx_bookings_pending_approval", "idx_bookings_outcome_pending")
PHASE6_DROPPED_CONSTRAINTS = ("uq_slot_locks_booking",)
# Điều kiện script giả định có sẵn (Mục 6.1, câu backfill)
PRECONDITION_COLUMNS = {"bookings": ("cancel_source", "cancelled_at")}
PRECONDITION_CONSTRAINTS = ("chk_bookings_cancelled_at",)

_HEADER = re.compile(r"^-- Name: (?P<name>.+?); Type: (?P<type>[^;]+); Schema: (?P<schema>[^;]+)")
_NOISE = re.compile(r"^(\\restrict|\\unrestrict|-- Dumped (from|by) )")
_SKIP_LINE = re.compile(r"^(--|SET |SELECT pg_catalog\.set_config|ALTER .* OWNER TO )")
_MIGRATION_FILE = re.compile(r"^(\d{14}_\w+)\.cs$")

ObjectKey = tuple[str, str, str]  # (type, schema, name)


def normalize(text: str) -> str:
    """Bỏ BOM, CRLF và các dòng thay đổi theo lần dump (`\\restrict`, phiên bản pg_dump) để so sánh chỉ còn khác biệt thật."""
    lines = text.lstrip("\ufeff").replace("\r\n", "\n").split("\n")
    return "\n".join(line.rstrip() for line in lines if not _NOISE.match(line))


def parse_objects(text: str) -> dict[ObjectKey, str]:
    """Tách bản dump thành các đối tượng theo dòng tiêu đề của pg_dump; thân chỉ giữ câu SQL (bỏ comment, SET, OWNER)."""
    objects: dict[ObjectKey, list[str]] = {}
    current: list[str] | None = None
    for line in normalize(text).split("\n"):
        header = _HEADER.match(line)
        if header:
            key = (header["type"].strip(), header["schema"].strip(), header["name"].strip())
            current = objects.setdefault(key, [])
            continue
        if current is not None and line.strip() and not _SKIP_LINE.match(line):
            current.append(line)
    return {key: "\n".join(body) for key, body in objects.items()}


def table_parts(body: str) -> tuple[dict[str, str], dict[str, str]]:
    """Từ `CREATE TABLE … ( … );` lấy cột (tên → định nghĩa) và ràng buộc khai báo trong bảng (tên → định nghĩa)."""
    columns: dict[str, str] = {}
    constraints: dict[str, str] = {}
    inside = False
    for raw in body.split("\n"):
        line = raw.strip()
        if line.startswith("CREATE TABLE") or line.startswith("CREATE UNLOGGED TABLE"):
            inside = True
            continue
        if not inside:
            continue
        if line.startswith(")"):
            break
        line = line.rstrip(",")
        if line.startswith("CONSTRAINT "):
            _, name, definition = line.split(" ", 2)
            constraints[name] = definition
        elif line:
            name, _, definition = line.partition(" ")
            columns[name.strip('"')] = definition
    return columns, constraints


def enum_values(body: str) -> list[str]:
    """Giá trị của `CREATE TYPE … AS ENUM (…)` theo đúng thứ tự khai báo."""
    return re.findall(r"'((?:[^']|'')*)'", body) if "AS ENUM" in body else []


def constraint_names(objects: dict[ObjectKey, str]) -> set[str]:
    """Mọi tên ràng buộc: khai báo trong bảng và thêm bằng `ALTER TABLE … ADD CONSTRAINT` (tiêu đề `bảng tên_ràng_buộc`)."""
    names = {key[2].split()[-1] for key in objects if key[0].endswith("CONSTRAINT")}
    for key, body in objects.items():
        if key[0] == "TABLE":
            names.update(table_parts(body)[1])
    return names


def phase6_rows(objects: dict[ObjectKey, str], schema: str = "public") -> list[tuple[str, bool]]:
    """Từng mục của script Phase 6 và điều kiện tiên quyết: (nhãn, có/không). Mục 'bỏ' trả True khi đã không còn."""
    rows: list[tuple[str, bool]] = []
    constraints = constraint_names(objects)
    columns = {table: table_parts(objects.get(("TABLE", schema, table), ""))[0] for table in {*PHASE6_COLUMNS, *PRECONDITION_COLUMNS}}
    for table, needed in PRECONDITION_COLUMNS.items():
        rows += [(f"[có sẵn] cột {table}.{column}", column in columns[table]) for column in needed]
    rows += [(f"[có sẵn] ràng buộc {name}", name in constraints) for name in PRECONDITION_CONSTRAINTS]
    for type_name, values in PHASE6_ENUM_VALUES.items():
        present = enum_values(objects.get(("TYPE", schema, type_name), ""))
        rows += [(f"enum {type_name} có '{value}'", value in present) for value in values]
    rows += [(f"kiểu {name}", ("TYPE", schema, name) in objects) for name in PHASE6_TYPES]
    for table, needed in PHASE6_COLUMNS.items():
        rows += [(f"cột {table}.{column}", column in columns[table]) for column in needed]
    rows += [(f"ràng buộc {name}", name in constraints) for name in PHASE6_CONSTRAINTS]
    rows += [(f"index {name}", ("INDEX", schema, name) in objects) for name in PHASE6_INDEXES]
    rows += [(f"đã bỏ {name}", name not in constraints) for name in PHASE6_DROPPED_CONSTRAINTS]
    return rows


def phase6_status(rows: list[tuple[str, bool]]) -> str:
    """Tóm tắt trạng thái script Phase 6 từ các mục (không tính mục [có sẵn])."""
    applied = [ok for label, ok in rows if not label.startswith("[có sẵn]")]
    if all(applied):
        return "đã áp đủ"
    # Trước khi áp, chỉ mục "đã bỏ uq_slot_locks_booking" có thể đúng sẵn (nếu ràng buộc chưa từng có)
    added = [ok for label, ok in rows if not label.startswith(("[có sẵn]", "đã bỏ"))]
    return "chưa áp" if not any(added) else "áp dở"


def compare(left: dict[ObjectKey, str], right: dict[ObjectKey, str]) -> tuple[list[ObjectKey], list[ObjectKey], list[tuple[ObjectKey, list[str]]]]:
    """(chỉ có bên trái, chỉ có bên phải, khác định nghĩa kèm chi tiết)."""
    only_left = sorted(set(left) - set(right))
    only_right = sorted(set(right) - set(left))
    changed = [(key, describe_change(key, left[key], right[key])) for key in sorted(set(left) & set(right)) if left[key] != right[key]]
    return only_left, only_right, changed


def describe_change(key: ObjectKey, before: str, after: str) -> list[str]:
    """Chi tiết khác biệt: bảng theo cột/ràng buộc, enum theo giá trị, đối tượng khác theo diff dòng (tối đa 12 dòng)."""
    if key[0] == "TABLE":
        lines: list[str] = []
        for label, old, new in zip(("cột", "ràng buộc"), table_parts(before), table_parts(after)):
            lines += [f"+ {label} `{name}` {new[name]}" for name in new if name not in old]
            lines += [f"- {label} `{name}` {old[name]}" for name in old if name not in new]
            lines += [f"~ {label} `{name}`: {old[name]} → {new[name]}" for name in old if name in new and old[name] != new[name]]
        return lines or ["định nghĩa bảng khác (thứ tự hoặc phần ngoài cột)"]
    if "AS ENUM" in before and "AS ENUM" in after:
        return [f"giá trị: {', '.join(enum_values(before))} → {', '.join(enum_values(after))}"]
    diff = [line for line in difflib.unified_diff(before.split("\n"), after.split("\n"), lineterm="", n=0) if not line.startswith(("---", "+++", "@@"))]
    return diff[:12] + (["…"] if len(diff) > 12 else [])


def code_migrations(directory: Path = MIGRATIONS_DIR) -> list[str]:
    """ID migration EF của TADSS trong mã (tên file không kèm `.Designer`/snapshot)."""
    return sorted(match[1] for path in directory.glob("*.cs") if (match := _MIGRATION_FILE.match(path.name)))


def find_tool(name: str) -> str | None:
    """Tìm pg_dump/psql trên PATH, không có thì lấy bản mới nhất trong `C:/Program Files/PostgreSQL/*/bin`."""
    found = shutil.which(name)
    if found:
        return found
    candidates = glob.glob(f"C:/Program Files/PostgreSQL/*/bin/{name}.exe")
    return max(candidates, key=lambda p: int(re.search(r"PostgreSQL/(\d+)", p.replace("\\", "/"))[1]), default=None)


def read_env_file(path: Path) -> dict[str, str]:
    """Đọc file `KEY=VALUE` (bỏ dòng trống, dòng `#`, dấu nháy bao ngoài); không có file thì rỗng."""
    if not path.is_file():
        return {}
    values: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        key, sep, value = line.strip().partition("=")
        if sep and key and not key.startswith("#"):
            values[key.strip()] = value.strip().strip("'\"")
    return values


def connection_for(name: str, env_file: dict[str, str] | None = None) -> str:
    """Chuỗi kết nối của target: biến `SCHEMA_DB_URL_<NAME>` (môi trường, rồi file --env-file) hoặc nhập ẩn."""
    env_name = f"SCHEMA_DB_URL_{re.sub(r'[^A-Za-z0-9]', '_', name).upper()}"
    value = (os.environ.get(env_name) or (env_file or {}).get(env_name)
             or getpass.getpass(f"Chuỗi kết nối cho '{name}' (host không có -pooler; hoặc đặt {env_name}): "))
    if not value.strip():
        raise SystemExit(f"Thiếu chuỗi kết nối cho '{name}'.")
    return value.strip()


def run_tool(command: list[str], secret: str) -> subprocess.CompletedProcess[str]:
    """Chạy pg_dump/psql; lỗi thì in stderr đã che chuỗi kết nối."""
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace",
                            env={**os.environ, "PGCONNECT_TIMEOUT": "20"}, check=False)
    if result.returncode != 0:
        result.stderr = result.stderr.replace(secret, "<chuỗi kết nối>")
    return result


def dump_target(name: str, out_dir: Path, pg_dump: str, psql: str | None,
                env_file: dict[str, str] | None = None) -> tuple[Path, list[str] | None]:
    """Dump schema của một target vào `<out>/<name>.sql`; kèm danh sách migration TADSS đã áp (None nếu không có schema tadss)."""
    url = connection_for(name, env_file)
    path = out_dir / f"{name}.sql"
    result = run_tool([pg_dump, "--schema-only", "--no-privileges", "--dbname", url, "--file", str(path)], url)
    if result.returncode != 0:
        raise SystemExit(f"pg_dump '{name}' lỗi:\n{result.stderr}")
    applied = None
    if psql:
        query = 'SELECT "MigrationId" FROM tadss."__EFMigrationsHistory" ORDER BY 1'
        result = run_tool([psql, "--dbname", url, "-At", "-c", query], url)
        applied = result.stdout.split() if result.returncode == 0 else None
    return path, applied


def build_report(sources: dict[str, dict[ObjectKey, str]], migrations: dict[str, list[str] | None], stamp: str) -> str:
    """Báo cáo markdown: trạng thái Phase 6, migration TADSS, khác biệt từng cặp."""
    names = list(sources)
    lines = [f"# So sánh schema — {stamp}", "", "Nguồn: " + ", ".join(f"`{n}`" for n in names), ""]
    checks = {name: phase6_rows(objects) for name, objects in sources.items()}
    lines += ["## Script Phase 6 (spec Mục 6.2)", "", "| Mục | " + " | ".join(names) + " |", "| :--- |" + " :---: |" * len(names)]
    lines.append("| **Trạng thái** | " + " | ".join(phase6_status(checks[n]) for n in names) + " |")
    for index, (label, _) in enumerate(checks[names[0]]):
        lines.append(f"| {label} | " + " | ".join("có" if checks[n][index][1] else "**không**" for n in names) + " |")
    if migrations:
        expected = code_migrations()
        lines += ["", "## Migration TADSS (schema `tadss`)", "", f"Trong mã: {len(expected)} migration.", ""]
        for name, applied in migrations.items():
            if applied is None:
                lines.append(f"- `{name}`: không đọc được `tadss.__EFMigrationsHistory` (không có schema tadss hoặc không có psql).")
                continue
            missing = [m for m in expected if m not in applied]
            extra = [m for m in applied if m not in expected]
            lines.append(f"- `{name}`: đã áp {len(applied)}; chưa áp: {', '.join(missing) or 'không'}; lạ (không có trong mã): {', '.join(extra) or 'không'}.")
    for left, right in combinations(names, 2):
        only_left, only_right, changed = compare(sources[left], sources[right])
        lines += ["", f"## `{left}` → `{right}`", ""]
        if not (only_left or only_right or changed):
            lines.append("Giống nhau.")
            continue
        lines += [f"- Chỉ có ở `{right}`: {key[0]} `{key[1]}.{key[2]}`" for key in only_right]
        lines += [f"- Chỉ có ở `{left}`: {key[0]} `{key[1]}.{key[2]}`" for key in only_left]
        for key, detail in changed:
            lines.append(f"- Khác: {key[0]} `{key[1]}.{key[2]}`")
            lines += [f"  - {item}" for item in detail]
    return "\n".join(lines) + "\n"


def parse_target(spec: str) -> tuple[str, Path | None]:
    """`name` (dump qua kết nối) hoặc `name=đường/dẫn.sql` (dùng file có sẵn)."""
    name, _, path = spec.partition("=")
    if not re.fullmatch(r"[A-Za-z0-9_-]+", name) or name == BASELINE_NAME:
        raise SystemExit(f"Tên target không hợp lệ: '{name}' (chữ, số, _ hoặc -; không dùng '{BASELINE_NAME}').")
    return name, Path(path) if path else None


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description="Dump và so sánh schema các branch Neon với bản trong repo (chỉ đọc).")
    parser.add_argument("--target", action="append", required=True, help="NAME hoặc NAME=file.sql; lặp lại cho nhiều branch")
    parser.add_argument("--baseline", type=Path, default=DEFAULT_BASELINE, help="bản dump trong repo để so (mặc định schema_smashcourt.sql)")
    parser.add_argument("--no-baseline", action="store_true", help="không so với bản trong repo")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT, help="thư mục gốc chứa kết quả")
    parser.add_argument("--env-file", type=Path, default=DEFAULT_ENV_FILE,
                        help="file SCHEMA_DB_URL_<NAME>=… (mặc định data/private/db-urls.env, gitignore)")
    args = parser.parse_args(argv)
    env_file = read_env_file(args.env_file)

    targets = [parse_target(spec) for spec in args.target]
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    out_dir = args.out / stamp
    out_dir.mkdir(parents=True, exist_ok=True)
    pg_dump, psql = find_tool("pg_dump"), find_tool("psql")
    if any(path is None for _, path in targets) and not pg_dump:
        raise SystemExit("Không tìm thấy pg_dump (PATH hoặc C:/Program Files/PostgreSQL/*/bin).")

    texts: dict[str, str] = {}
    migrations: dict[str, list[str] | None] = {}
    if not args.no_baseline:
        texts[BASELINE_NAME] = args.baseline.read_text(encoding="utf-8")
    for name, path in targets:
        if path is None:
            print(f"Đang dump '{name}'…")
            path, migrations[name] = dump_target(name, out_dir, pg_dump, psql, env_file)
        texts[name] = path.read_text(encoding="utf-8")

    for left, right in combinations(texts, 2):
        diff = difflib.unified_diff(normalize(texts[left]).split("\n"), normalize(texts[right]).split("\n"), left, right, lineterm="")
        (out_dir / f"{left}_vs_{right}.diff").write_text("\n".join(diff) + "\n", encoding="utf-8")
    report = build_report({name: parse_objects(text) for name, text in texts.items()}, migrations, stamp)
    (out_dir / "report.md").write_text(report, encoding="utf-8")
    print(f"Xong. Báo cáo: {out_dir / 'report.md'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
