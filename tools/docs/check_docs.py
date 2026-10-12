"""Kiểm tra tài liệu của repo (gói 5.7): DOC-1 kiểm kê, DOC-2 thuật ngữ đã gỡ, DOC-3 link nội bộ và anchor.

Chỉ dùng thư viện chuẩn để chạy được ở CI, trong bản sao `/work` của evaluator (bước (a)) và trên máy nhà phát triển:
    python tools/docs/check_docs.py [--root <thư mục repo>]
Thoát 0 khi cả ba mã đạt; thoát 1 và liệt kê từng vấn đề (một dòng, có đường dẫn và số dòng) khi có vi phạm.
DOC-4 (bảng cấu hình khớp mã) là unit test .NET, không nằm ở đây.

Bảng kiểm kê nằm ở mục "Tài liệu" của `README.md` (gốc repo): bảng markdown có tiêu đề `| File | Nhóm | ... |`, mỗi dòng một file
(đường dẫn tính từ gốc repo, viết thường hoặc trong dấu backtick), nhóm là `hiện hành` hoặc `lịch sử`.
Không có cơ chế bỏ qua từng dòng: muốn lọt DOC-2 thì phân loại lại file hoặc viết lại câu cho đúng nghĩa.
"""
from __future__ import annotations

import argparse
import os
import posixpath
import re
import sys
import unicodedata
from pathlib import Path
from urllib.parse import unquote

# Các thứ đã gỡ khỏi repo: tài liệu hiện hành không được mô tả chúng như đang dùng (DOC-2)
REMOVED_TERMS: tuple[str, ...] = ("MediatR", "BenchmarkRunner", "UseInMemoryDatabase", "EF InMemory", "AuditTimestampInterceptor", "/api/v1/",
                                  "--profile evaluation", "profile `evaluation`", "-p tadss-eval", "tools/chaos/run-fi.sh", "tools/contract/run-schemathesis.sh",
                                  "docker-compose.ngrok.yml", "docker-compose.neon.yml")
# Dòng có một trong các dấu hiệu này được coi là đang nói về quá khứ/điều cấm (so khớp không phân biệt hoa thường)
PAST_MARKERS: tuple[str, ...] = ("gỡ", "đã bỏ", "thay cho", "thay bằng", "không còn", "không dùng", "cấm", "phase4-baseline", "lịch sử")
# Thư mục không quét (kết quả chạy, mã build, dữ liệu, đặc tả nháp...); `.pytest_cache`/`__pycache__`/`.venv` là thư mục sinh ra, không phải tài liệu
EXCLUDED_DIRS = frozenset({".git", "spec", ".superpowers", "evidence", "data", "bin", "obj", "node_modules", "TestResults", ".vs",
                           ".pytest_cache", "__pycache__", ".venv"})

INVENTORY_PATH = "README.md"
GROUP_CURRENT = "hiện hành"
GROUP_HISTORY = "lịch sử"
BANNER_PREFIX = "> **Tài liệu lịch sử**"
ROOT_DOCUMENT_NAMES = ("README.md", "CLAUDE.md")

_FENCE = re.compile(r"^\s*(`{3,}|~{3,})")
_HEADING = re.compile(r"^\s{0,3}#{1,6}\s+(.*?)(?:\s+#+)?\s*$")
_LINK_TEXT = re.compile(r"\[([^\]]*)\]\([^)]*\)")
_INLINE_CODE = re.compile(r"`[^`]*`")
_INLINE_LINK = re.compile(r"!?\[[^\]]*\]\(\s*(<[^>]*>|[^)\s]+)(?:\s+(?:\"[^\"]*\"|'[^']*'))?\s*\)")
_REFERENCE_LINK = re.compile(r"^\s{0,3}\[[^\]]+\]:\s*(<[^>]*>|\S+)")
_SCHEME = re.compile(r"^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|//)")
_SEPARATOR_CELL = re.compile(r":?-+:?")
_MARKDOWN_LINK = re.compile(r"\[[^\]]*\]\(([^)]+)\)")


class InventoryError(ValueError):
    """Bảng kiểm kê thiếu hoặc sai định dạng (không phân loại được tài liệu)."""


# --- Duyệt cây thư mục và đọc file ---

def _repo_files(root: Path) -> list[str]:
    """Mọi file (đường dẫn tương đối dạng posix, đã sắp xếp) ngoài các thư mục bị loại."""
    found: list[str] = []
    for current, dirs, files in os.walk(root):
        dirs[:] = sorted(name for name in dirs if name not in EXCLUDED_DIRS)
        base = Path(current).relative_to(root)
        found.extend((base / name).as_posix() for name in sorted(files))
    return found


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def _numbered_lines(text: str) -> list[tuple[int, str, bool]]:
    """(số dòng từ 1, nội dung, nằm trong khối code?) — dòng mở/đóng rào ``` hoặc ~~~ cũng tính là khối code."""
    result: list[tuple[int, str, bool]] = []
    fence: tuple[str, int] | None = None
    for number, line in enumerate(text.splitlines(), 1):
        match = _FENCE.match(line)
        if fence is None:
            if match:
                fence = (match.group(1)[0], len(match.group(1)))
            result.append((number, line, fence is not None))
            continue
        result.append((number, line, True))
        if match and match.group(1)[0] == fence[0] and len(match.group(1)) >= fence[1] and not line.strip().strip(fence[0]):
            fence = None
    return result


# --- Bảng kiểm kê ---

def _table_cells(line: str) -> list[str] | None:
    stripped = line.strip()
    if not stripped.startswith("|"):
        return None
    return [cell.strip() for cell in stripped.strip("|").split("|")]


def _plain(cell: str) -> str:
    return unicodedata.normalize("NFC", cell.replace("`", "").replace("*", "")).strip()


def _inventory_path(cell: str) -> str:
    """Đường dẫn từ gốc repo của ô `File`; dạng link `[tên](đích)` được hiểu tương đối với thư mục chứa bảng (gốc repo)."""
    link = _MARKDOWN_LINK.search(cell)
    if link:
        return posixpath.normpath(posixpath.join(posixpath.dirname(INVENTORY_PATH), unquote(link.group(1).split("#")[0])))
    return _plain(cell)


def load_inventory(root: Path) -> dict[str, str]:
    """Đọc bảng kiểm kê thành `{đường dẫn tương đối dạng posix: "hiện hành" | "lịch sử"}`; thiếu bảng, nhóm lạ hoặc file trùng dòng ⇒ `InventoryError`."""
    path = root / INVENTORY_PATH
    if not path.is_file():
        raise InventoryError(f"{INVENTORY_PATH}: thiếu file bảng kiểm kê")
    inventory: dict[str, str] = {}
    in_table = False
    found_table = False
    for number, line, in_code in _numbered_lines(_read(path)):
        cells = None if in_code else _table_cells(line)
        if cells is None:
            in_table = False
            continue
        if not in_table:
            if len(cells) >= 2 and _plain(cells[0]).lower() == "file" and _plain(cells[1]).lower() == "nhóm":
                in_table = found_table = True
            continue
        if _SEPARATOR_CELL.fullmatch(cells[0]):
            continue
        if len(cells) < 2 or not _inventory_path(cells[0]):
            raise InventoryError(f"{INVENTORY_PATH}:{number}: dòng kiểm kê phải có ô File và ô Nhóm")
        file_path, group = _inventory_path(cells[0]), _plain(cells[1]).lower()
        if group not in (GROUP_CURRENT, GROUP_HISTORY):
            raise InventoryError(f"{INVENTORY_PATH}:{number}: nhóm '{_plain(cells[1])}' không hợp lệ (chỉ '{GROUP_CURRENT}' hoặc '{GROUP_HISTORY}')")
        if file_path in inventory:
            raise InventoryError(f"{INVENTORY_PATH}:{number}: file '{file_path}' có nhiều hơn một dòng kiểm kê")
        inventory[file_path] = group
    if not found_table:
        raise InventoryError(f"{INVENTORY_PATH}: không thấy bảng kiểm kê (tiêu đề cột phải bắt đầu bằng 'File | Nhóm')")
    return inventory


def _needs_inventory_row(relative: str) -> bool:
    return relative.startswith("docs/") or posixpath.basename(relative) in ROOT_DOCUMENT_NAMES


def _banner_problem(path: Path, relative: str) -> str | None:
    for number, line in enumerate(_read(path).splitlines(), 1):
        if line.strip():
            if line.strip().startswith(BANNER_PREFIX):
                return None
            return f"{relative}:{number}: file lịch sử phải mở đầu bằng banner '{BANNER_PREFIX}'"
    return f"{relative}:1: file lịch sử rỗng, thiếu banner '{BANNER_PREFIX}'"


def inventory_problems(root: Path, inventory: dict[str, str]) -> list[str]:
    """DOC-1: file thiếu dòng kiểm kê, dòng trỏ file không tồn tại, file lịch sử dạng markdown thiếu banner."""
    problems = [
        f"{relative}: không có dòng nào trong bảng kiểm kê {INVENTORY_PATH}"
        for relative in _repo_files(root)
        if _needs_inventory_row(relative) and relative not in inventory
    ]
    for relative, group in inventory.items():
        path = root / relative
        if not path.is_file():
            problems.append(f"{INVENTORY_PATH}: dòng kiểm kê trỏ tới file không tồn tại '{relative}'")
        elif group == GROUP_HISTORY and relative.endswith(".md") and (problem := _banner_problem(path, relative)):
            problems.append(problem)
    return problems


# --- DOC-2: thuật ngữ đã gỡ ---

def removed_term_problems(root: Path, inventory: dict[str, str]) -> list[str]:
    """
    DOC-2: mỗi lần `REMOVED_TERMS` xuất hiện ở `.md` hiện hành (và README/CLAUDE.md không phải lịch sử), kể cả trong khối code, mà dòng đó không có dấu hiệu
    quá khứ/cấm trong `PAST_MARKERS`. Trả `"đường_dẫn:dòng: 'thuật ngữ' ..."`.
    """
    problems: list[str] = []
    for relative in _repo_files(root):
        group = inventory.get(relative)
        scanned = group == GROUP_CURRENT or (group != GROUP_HISTORY and posixpath.basename(relative) in ROOT_DOCUMENT_NAMES)
        if not (relative.endswith(".md") and scanned):
            continue
        for number, line, _ in _numbered_lines(_read(root / relative)):
            if any(marker in line.lower() for marker in PAST_MARKERS):
                continue
            problems.extend(f"{relative}:{number}: '{term}' đã gỡ nhưng tài liệu hiện hành vẫn nhắc như đang dùng" for term in REMOVED_TERMS if term in line)
    return problems


# --- DOC-3: link nội bộ và anchor ---

def github_slug(heading: str) -> str:
    """Anchor GitHub của tiêu đề: chữ thường, bỏ ký tự không phải chữ/số/khoảng trắng/gạch ngang (giữ chữ có dấu), mỗi khoảng trắng → '-' (không gộp)."""
    text = _LINK_TEXT.sub(r"\1", unicodedata.normalize("NFC", heading)).strip().lower()
    return re.sub(r"\s", "-", re.sub(r"[^\w\s-]", "", text))


def _heading_slugs(text: str) -> set[str]:
    """Anchor của mọi tiêu đề ngoài khối code; tiêu đề trùng được đánh số `-1`, `-2` như GitHub."""
    seen: dict[str, int] = {}
    slugs: set[str] = set()
    for _, line, in_code in _numbered_lines(text):
        match = None if in_code else _HEADING.match(line)
        if not match:
            continue
        base = github_slug(match.group(1))
        count = seen.get(base, 0)
        seen[base] = count + 1
        slugs.add(base if count == 0 else f"{base}-{count}")
    return slugs


def _line_targets(line: str) -> list[str]:
    """Đích của các link trên một dòng (đã bỏ code inline)."""
    plain = _INLINE_CODE.sub("", line)
    targets = [match.group(1) for match in _INLINE_LINK.finditer(plain)]
    reference = _REFERENCE_LINK.match(plain)
    return targets + ([reference.group(1)] if reference else [])


def _resolve(source: str, path_part: str) -> str:
    """Đường dẫn tương đối gốc repo của đích (rỗng ⇒ chính file nguồn); bắt đầu bằng '..' ⇒ nằm ngoài repo."""
    if not path_part:
        return source
    if path_part.startswith("/"):
        return posixpath.normpath(path_part.lstrip("/"))
    return posixpath.normpath(posixpath.join(posixpath.dirname(source), path_part))


def _check_target(root: Path, source: str, number: int, raw: str, slug_cache: dict[str, set[str]]) -> tuple[str | None, bool]:
    """(vấn đề hoặc None, đã bỏ qua vì ra ngoài repo/http?) cho một link."""
    target = raw.strip("<>")
    if _SCHEME.match(target):
        return None, True
    path_part, _, fragment = target.partition("#")
    destination = _resolve(source, unquote(path_part.partition("?")[0]))
    if destination == ".." or destination.startswith("../"):
        return None, True
    full = root / destination
    if not full.exists():
        return f"{source}:{number}: link tới file không tồn tại '{target}'", False
    if fragment and full.is_file() and destination.endswith(".md"):
        if destination not in slug_cache:
            slug_cache[destination] = _heading_slugs(_read(full))
        if unicodedata.normalize("NFC", unquote(fragment)).lower() not in slug_cache[destination]:
            return f"{source}:{number}: anchor '#{fragment}' không khớp tiêu đề nào trong {destination}", False
    return None, False


def link_problems(root: Path) -> tuple[list[str], int]:
    """DOC-3: (vấn đề, số link ra ngoài repo/http đã bỏ qua) trên mọi `.md`; link trong khối code và code inline không kiểm."""
    problems: list[str] = []
    skipped = 0
    slug_cache: dict[str, set[str]] = {}
    for relative in _repo_files(root):
        if not relative.endswith(".md"):
            continue
        for number, line, in_code in _numbered_lines(_read(root / relative)):
            if in_code:
                continue
            for raw in _line_targets(line):
                problem, external = _check_target(root, relative, number, raw, slug_cache)
                skipped += external
                if problem:
                    problems.append(problem)
    return problems, skipped


# --- Dòng lệnh ---

def _report(code: str, problems: list[str] | None, detail: str = "") -> bool:
    """In một dòng `DOC-x` kèm từng vấn đề; trả True nếu đạt."""
    if problems is None:
        print(f"{code}: không đạt (không kiểm được vì bảng kiểm kê lỗi, xem DOC-1)")
        return False
    if not problems:
        print(f"{code}: đạt{detail}")
        return True
    print(f"{code}: không đạt ({len(problems)} vấn đề)")
    for problem in problems:
        print(f"  - {problem}")
    return False


def main(argv: list[str] | None = None) -> int:
    """Chạy ba kiểm tra trên `--root` (mặc định: thư mục cha của `tools/`); thoát 0 khi cả ba đạt."""
    parser = argparse.ArgumentParser(description="Kiểm tra tài liệu DOC-1…DOC-3")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2], help="thư mục gốc repo")
    root = parser.parse_args(argv).root.resolve()

    doc2: list[str] | None
    try:
        inventory = load_inventory(root)
        doc1, doc2 = inventory_problems(root, inventory), removed_term_problems(root, inventory)
    except InventoryError as error:
        doc1, doc2 = [str(error)], None
    doc3, external = link_problems(root)

    results = [_report("DOC-1", doc1), _report("DOC-2", doc2), _report("DOC-3", doc3, f" ({external} link ra ngoài repo bỏ qua)")]
    return 0 if all(results) else 1


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")  # console Windows mặc định không in được tiếng Việt
    sys.exit(main())
