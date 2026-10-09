"""Dựng `manifest.json` và chạy EV-1…EV-5 (DATASET_SCHEMA Mục 2, 5). Dùng được độc lập với evaluator: chỉ cần thư mục `evidence/<run_id>/`."""
from __future__ import annotations

import csv
import hashlib
import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Collection, Iterable, Sequence

SCHEMA_VERSION = "2.0.0"
MANIFEST_NAME = "manifest.json"
# Ghi sau cùng bởi evaluator (nội dung đã nằm trong manifest.steps) nên không thể có băm trong manifest
UNHASHED_NAMES = frozenset({MANIFEST_NAME, "steps.json"})
# Cột dữ liệu cá nhân hoặc định danh chưa ẩn danh (EV-4); so khớp đúng tên cột, không so khớp chuỗi con (`strategy_name` hợp lệ)
FORBIDDEN_COLUMNS = frozenset(
    {"name", "customer_name", "full_name", "phone", "phone_number", "email", "note", "notes", "address", "ip", "ip_address", "customer_id", "reservation_id", "branch_id", "actor_id", "user_id"}
)
# Chuỗi mẫu bí mật đã biết: khóa API thật không phải hằng đánh giá, chuỗi kết nối có mật khẩu khác mật khẩu cố định của stack đánh giá, token ngrok, tệp cấu hình Development
SECRET_PATTERNS = (
    re.compile(rb"appsettings\.Development", re.IGNORECASE),
    re.compile(rb"NGROK_AUTHTOKEN\s*[:=]\s*[A-Za-z0-9_]{12,}"),
    re.compile(rb"Password=(?!tadss_eval\b)[^;\s\"']{3,}", re.IGNORECASE),
    re.compile(rb"postgres(?:ql)?://[^:\s/]+:(?!tadss_eval\b)[^@\s]+@"),
    re.compile(rb"Bearer\s+[A-Za-z0-9._\-]{20,}"),
)
# Báo cáo coverage/mutation là bản liệt kê mã nguồn của chính repo (không phải dữ liệu lượt chạy) nên không quét mẫu bí mật trong đó
SCAN_SKIP_PREFIXES = ("raw/coverage/", "raw/mutation/")
TEXT_SUFFIXES = {".csv", ".json", ".jsonl", ".txt", ".log", ".yml", ".yaml", ".xml", ".md", ".html"}
SCAN_MAX_BYTES = 50 * 1024 * 1024
UUID_REGEX = r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"  # UUID có gạch ngang; nguồn duy nhất của mẫu UUID cho EV-4
UUID_PATTERN = re.compile(UUID_REGEX.encode("ascii"))  # quét file dạng byte; EV-4 so với danh sách UUID gốc do evaluator truyền vào
_UUID_TEXT_PATTERN = re.compile(UUID_REGEX)
MAIN_SOURCE_NAME = "chính (D2 + replay)"
PORTABILITY_SOURCE_NAME = "PT"


def find_uuids(text: str) -> set[str]:
    """Mọi UUID (chữ thường) trong một chuỗi; cùng mẫu với phép quét EV-4 nên danh sách UUID gốc và phép quét không lệch nhau."""
    return {match.lower() for match in _UUID_TEXT_PATTERN.findall(text)}


@dataclass(frozen=True)
class EvCheck:
    """Kết quả một kiểm tra EV."""

    code: str
    passed: bool
    detail: str

    def to_dict(self) -> dict[str, Any]:
        return {"code": self.code, "passed": self.passed, "detail": self.detail}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _row_count(path: Path) -> int | None:
    """Số dòng dữ liệu của CSV (trừ dòng tiêu đề) và JSONL; file khác → None."""
    if path.suffix == ".csv":
        with path.open(encoding="utf-8", newline="") as handle:
            return max(0, sum(1 for _ in handle) - 1)
    if path.suffix == ".jsonl":
        with path.open(encoding="utf-8") as handle:
            return sum(1 for line in handle if line.strip())
    return None


def list_files(run_dir: Path) -> list[Path]:
    """Mọi file trong pack trừ `manifest.json` (tự nó không thể chứa băm của chính nó) và `steps.json` (ghi sau cùng), sắp xếp để manifest ổn định."""
    return sorted(path for path in run_dir.rglob("*") if path.is_file() and path.name not in UNHASHED_NAMES)


def build_manifest(run_dir: Path, base: dict[str, Any]) -> dict[str, Any]:
    """`base` gồm các trường do evaluator biết (runId, createdAtUtc, environment, commits, images, machine, testCases, mode, dirty, steps, kpis…); hàm thêm `files` kèm SHA-256 và số dòng."""
    manifest = {"schemaVersion": SCHEMA_VERSION, **base}
    manifest["files"] = [{"path": path.relative_to(run_dir).as_posix(), "sha256": sha256_file(path), "rows": _row_count(path)} for path in list_files(run_dir)]
    return manifest


def write_manifest(run_dir: Path, manifest: dict[str, Any]) -> Path:
    path = run_dir / MANIFEST_NAME
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    return path


def check_ev1(run_dir: Path, manifest: dict[str, Any], ignore: Collection[str] = ()) -> EvCheck:
    """EV-1: SHA-256 của mọi file khớp manifest, không thiếu, không thừa. `ignore` là các đường dẫn tương đối bỏ qua (VD `report.html` khi chính nó sắp được dựng lại)."""
    listed = {entry["path"]: entry["sha256"] for entry in manifest.get("files", []) if entry["path"] not in ignore}
    actual = {path.relative_to(run_dir).as_posix(): path for path in list_files(run_dir) if path.relative_to(run_dir).as_posix() not in ignore}
    problems = [f"thiếu file {name}" for name in sorted(set(listed) - set(actual))] + [f"file không có trong manifest: {name}" for name in sorted(set(actual) - set(listed))]
    problems += [f"băm lệch: {name}" for name, path in actual.items() if name in listed and sha256_file(path) != listed[name]]
    return EvCheck("EV-1", not problems, "; ".join(problems[:5]) if problems else f"{len(listed)} file khớp SHA-256")


@dataclass(frozen=True)
class EvSource:
    """Một cặp nguồn cho EV-2/EV-3: D1 của một database và các dòng kiểu D2 (response của các request đã gửi tới đúng database đó)."""

    name: str
    d1_rows: list[dict[str, str]]
    entries: list[dict]


def step_entries(rows: Iterable[dict]) -> list[dict]:
    """Dòng `requests.jsonl` của bước (e) (`key`, `status`, `decision_id`, `correlation_id`) thành dạng D2 mà EV-2/EV-3 đọc."""
    return [
        {"http_status": row.get("status"), "decision_id": row.get("decision_id"), "idempotency_key": row.get("key"), "correlation_id": row.get("correlation_id"), "repeat": False}
        for row in rows
    ]


def _read_jsonl(path: Path) -> list[dict]:
    if not path.exists():
        return []
    with path.open(encoding="utf-8") as handle:
        return [json.loads(line) for line in handle if line.strip()]


def collect_sources(run_dir: Path) -> list[EvSource]:
    """Các nguồn đối chiếu của một pack, đọc từ file: nguồn chính = D1 + D2 (mọi `raw/load/*/requests.jsonl`) + replay (RP gửi qua proxy vào cùng database, Q-03);
    nguồn PT = D1 của database PT + response PT. PT chỉ có mặt khi bước (e) đã gửi request PT."""
    d1_path = run_dir / "raw" / "d1" / "decision_audit.csv"
    entries: list[dict] = []
    for path in sorted((run_dir / "raw" / "load").glob("*/requests.jsonl")):
        entries += _read_jsonl(path)
    entries += step_entries(_read_jsonl(run_dir / "raw" / "replay" / "requests.jsonl"))
    sources = [EvSource(MAIN_SOURCE_NAME, read_csv(d1_path) if d1_path.exists() else [], entries)]
    portability = _read_jsonl(run_dir / "raw" / "portability" / "requests.jsonl")
    if portability:
        pt_d1 = run_dir / "raw" / "d1" / "decision_audit_portability.csv"
        sources.append(EvSource(PORTABILITY_SOURCE_NAME, read_csv(pt_d1) if pt_d1.exists() else [], step_entries(portability)))
    return sources


def _ev2_one(source: EvSource) -> tuple[bool, str]:
    decision_ids = {row["decision_id"] for row in source.d1_rows}
    correlations = {row["correlation_id"] for row in source.d1_rows}
    missing_decision = missing_correlation = total = 0
    for entry in source.entries:
        if entry.get("http_status") != 200:
            continue
        total += 1
        if entry.get("decision_id") not in decision_ids:
            missing_decision += 1
        if not entry.get("repeat") and entry.get("correlation_id") not in correlations:
            missing_correlation += 1
    passed = total > 0 and missing_decision == 0 and missing_correlation == 0
    return passed, (f"{total} response 200; thiếu decision_id trong D1: {missing_decision}; thiếu correlation_id: {missing_correlation}" if total else "không có response 200 nào")


def _ev3_one(source: EvSource) -> tuple[bool, str]:
    ok = [entry for entry in source.entries if entry.get("http_status") == 200]
    primaries: dict[str, int] = {}
    for row in source.d1_rows:
        if row["role"] == "PRIMARY":
            primaries[row["decision_id"]] = primaries.get(row["decision_id"], 0) + 1
    decision_ids = {entry["decision_id"] for entry in ok}
    keys = {entry["idempotency_key"] for entry in ok}
    wrong = [decision for decision in decision_ids if primaries.get(decision, 0) != 1]
    passed = bool(ok) and not wrong and len(keys) == len(decision_ids)
    return passed, f"{len(keys)} khóa có 200, {len(decision_ids)} decisionId, {len(wrong)} decisionId không có đúng một bản PRIMARY"


def _across_sources(code: str, sources: Sequence[EvSource], check_one: Callable[[EvSource], tuple[bool, str]]) -> EvCheck:
    """Chạy `check_one` trên từng nguồn có request; nguồn không có request nào là "không áp dụng". Không nguồn nào áp dụng được thì không đạt."""
    applicable = [source for source in sources if source.entries]
    results = [(source, *check_one(source)) for source in applicable]
    parts = [f"[{source.name}] {'đạt' if passed else 'KHÔNG ĐẠT'}: {detail}" for source, passed, detail in results]
    parts += [f"[{source.name}] không áp dụng (không có request)" for source in sources if not source.entries]
    if not applicable:
        return EvCheck(code, False, "không có nguồn nào có response 200 để đối chiếu" + (f" ({'; '.join(parts)})" if parts else ""))
    return EvCheck(code, all(passed for _, passed, _ in results), " | ".join(parts))


def check_ev2_sources(sources: Sequence[EvSource]) -> EvCheck:
    """EV-2: ở MỖI nguồn, mọi response 200 trỏ tới `decision_id` có trong D1 của nguồn đó; request đầu tiên của mỗi khóa (không lặp) còn có `correlation_id` trong D1."""
    return _across_sources("EV-2", sources, _ev2_one)


def check_ev3_sources(sources: Sequence[EvSource]) -> EvCheck:
    """EV-3 (SC3): ở MỖI nguồn, mỗi `decisionId` có response 200 có đúng một bản `PRIMARY`; số khóa idempotency có 200 bằng số decisionId khác nhau (không thiếu, không thừa)."""
    return _across_sources("EV-3", sources, _ev3_one)


def check_ev2(d1_rows: list[dict[str, str]], d2_entries: Iterable[dict]) -> EvCheck:
    """EV-2 trên một nguồn duy nhất (D1 + D2); xem `check_ev2_sources`."""
    return check_ev2_sources([EvSource("D2", d1_rows, list(d2_entries))])


def check_ev3(d1_rows: list[dict[str, str]], d2_entries: Iterable[dict]) -> EvCheck:
    """EV-3 trên một nguồn duy nhất (D1 + D2); xem `check_ev3_sources`."""
    return check_ev3_sources([EvSource("D2", d1_rows, list(d2_entries))])


def check_ev4(run_dir: Path, raw_uuids: Collection[str] | None = None) -> EvCheck:
    """EV-4: không CSV nào có cột cá nhân/định danh chưa ẩn danh; không file văn bản nào chứa chuỗi mẫu bí mật đã biết; và (khi có `raw_uuids`) không file văn bản nào
    chứa UUID gốc của CSV SmashCourt. `raw_uuids` do evaluator đọc từ CSV trong tiến trình và truyền vào, không ghi ra file; báo cáo không in lại UUID."""
    problems: list[str] = []
    originals = {value.lower() for value in raw_uuids} if raw_uuids else set()
    for path in list_files(run_dir):
        relative = path.relative_to(run_dir).as_posix()
        if path.suffix == ".csv":
            with path.open(encoding="utf-8", newline="") as handle:
                header = next(csv.reader(handle), [])
            bad = sorted(FORBIDDEN_COLUMNS & {column.strip().lower() for column in header})
            if bad:
                problems.append(f"{relative}: cột {', '.join(bad)}")
        if path.suffix in TEXT_SUFFIXES and path.stat().st_size <= SCAN_MAX_BYTES:
            content = path.read_bytes()
            if not relative.startswith(SCAN_SKIP_PREFIXES):
                for pattern in SECRET_PATTERNS:
                    if pattern.search(content):
                        problems.append(f"{relative}: khớp mẫu bí mật {pattern.pattern[:40].decode('ascii', 'replace')}")
                        break
            if originals:
                leaked = {match.decode("ascii").lower() for match in UUID_PATTERN.findall(content)} & originals
                if leaked:
                    problems.append(f"{relative}: chứa {len(leaked)} UUID gốc của CSV SmashCourt")
    scanned = f"; đã quét {len(originals)} UUID gốc" if originals else "; chưa quét UUID gốc (không có danh sách)"
    return EvCheck("EV-4", not problems, "; ".join(problems[:5]) if problems else "không có cột cá nhân hay chuỗi bí mật" + scanned)


def check_ev5(run_dir: Path, manifest: dict[str, Any]) -> EvCheck:
    """EV-5: manifest có commit (BE/FE được phép `null` kèm lý do khi lượt chạy không có E2E), cấu hình đã dùng và thông số máy."""
    problems = []
    commits = manifest.get("commits") or {}
    if not commits.get("tadss"):
        problems.append("thiếu commit tadss")
    for repo in ("smashcourtBe", "smashcourtFe"):
        if repo not in commits:
            problems.append(f"thiếu khóa commits.{repo}")
        elif commits[repo] is None and not (manifest.get("commitNotes") or {}).get(repo):
            problems.append(f"commits.{repo} = null nhưng thiếu lý do trong commitNotes")
    if not manifest.get("machine"):
        problems.append("thiếu thông số máy")
    config_dir = run_dir / "config"
    if not config_dir.is_dir() or not any(config_dir.iterdir()):
        problems.append("thiếu thư mục config/")
    return EvCheck("EV-5", not problems, "; ".join(problems) if problems else "đủ commit, cấu hình và thông số máy")


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8", newline="") as handle:
        return list(csv.DictReader(handle))


def run_checks(
    run_dir: Path, manifest: dict[str, Any], d1_rows: list[dict[str, str]], d2_entries: list[dict], *,
    extra_sources: Sequence[EvSource] = (), main_name: str = "D2", raw_uuids: Collection[str] | None = None,
) -> list[EvCheck]:
    """EV-1…EV-5. EV-2/EV-3 chạy trên nguồn chính (`d1_rows` + `d2_entries`, tên `main_name`) và mọi `extra_sources` (VD PT với D1 của database PT)."""
    sources = [EvSource(main_name, d1_rows, d2_entries), *extra_sources]
    return [check_ev1(run_dir, manifest), check_ev2_sources(sources), check_ev3_sources(sources), check_ev4(run_dir, raw_uuids), check_ev5(run_dir, manifest)]
