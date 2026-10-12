"""Test cho check_docs.py (DOC-1…DOC-3) trên cây thư mục tạm."""
from __future__ import annotations

from pathlib import Path

import pytest

import check_docs

BANNER = "> **Tài liệu lịch sử** — ảnh chụp ngày 01/01/2026, không cập nhật theo mã."


def _write(root: Path, relative: str, text: str = "nội dung\n") -> None:
    path = root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")


def _inventory_file(rows: list[tuple[str, str]]) -> str:
    lines = ["# Tài liệu", "", "| File | Nhóm | Ghi chú |", "| :--- | :--- | :--- |"]
    lines += [f"| `{path}` | {group} | - |" for path, group in rows]
    return "\n".join(lines) + "\n"


def _tree(root: Path, rows: list[tuple[str, str]], files: dict[str, str] | None = None) -> None:
    """Cây tối thiểu: README.md ở gốc có bảng kiểm kê `rows` (và tự có dòng cho chính nó) cùng các file `files`."""
    _write(root, "README.md", _inventory_file([("README.md", "hiện hành"), *rows]))
    for relative, text in (files or {}).items():
        _write(root, relative, text)


# --- github_slug ---

@pytest.mark.parametrize(
    ("heading", "slug"),
    [
        ("API Endpoint", "api-endpoint"),
        ("Đánh giá", "đánh-giá"),
        ("C# Code", "c-code"),
        ("1. Ba cách chạy TADSS", "1-ba-cách-chạy-tadss"),
        ("4.15. DOC — Tài liệu (gói 5.7)", "415-doc--tài-liệu-gói-57"),
        ("Cấu hình `Tadss:Security:*`", "cấu-hình-tadsssecurity"),
    ],
)
def test_github_slug_matches_github_rules(heading: str, slug: str) -> None:
    assert check_docs.github_slug(heading) == slug


# --- load_inventory ---

def test_load_inventory_reads_groups_by_relative_path(tmp_path: Path) -> None:
    _tree(tmp_path, [("docs/a.md", "hiện hành"), ("docs/b.docx", "lịch sử")])

    assert check_docs.load_inventory(tmp_path) == {"README.md": "hiện hành", "docs/a.md": "hiện hành", "docs/b.docx": "lịch sử"}


def test_load_inventory_reads_table_from_root_readme_not_docs_readme(tmp_path: Path) -> None:
    _tree(tmp_path, [("docs/a.md", "hiện hành")])
    _write(tmp_path, "docs/README.md", _inventory_file([("docs/stale.md", "lịch sử")]))

    assert check_docs.load_inventory(tmp_path) == {"README.md": "hiện hành", "docs/a.md": "hiện hành"}


def test_load_inventory_finds_table_among_other_readme_content(tmp_path: Path) -> None:
    _write(
        tmp_path,
        "README.md",
        "# Repo\n\n| Cần tìm | Tài liệu |\n| :--- | :--- |\n| x | y |\n\n```md\n| File | Nhóm |\n| :--- | :--- |\n| `docs/fake.md` | lịch sử |\n```\n\n"
        "## Tài liệu\n\n| File | Nhóm | Ghi chú |\n| :--- | :--- | :--- |\n| `README.md` | hiện hành | gốc |\n| `docs/a.md` | lịch sử | cũ |\n\nHết bảng.\n",
    )

    assert check_docs.load_inventory(tmp_path) == {"README.md": "hiện hành", "docs/a.md": "lịch sử"}


def test_load_inventory_rejects_unknown_group(tmp_path: Path) -> None:
    _tree(tmp_path, [("docs/a.md", "đang soạn")])

    with pytest.raises(check_docs.InventoryError, match="đang soạn"):
        check_docs.load_inventory(tmp_path)


def test_load_inventory_rejects_duplicate_row(tmp_path: Path) -> None:
    _tree(tmp_path, [("docs/a.md", "hiện hành"), ("docs/a.md", "lịch sử")])

    with pytest.raises(check_docs.InventoryError, match="docs/a.md"):
        check_docs.load_inventory(tmp_path)


def test_load_inventory_rejects_missing_inventory_file(tmp_path: Path) -> None:
    with pytest.raises(check_docs.InventoryError, match="README.md"):
        check_docs.load_inventory(tmp_path)


def test_load_inventory_rejects_readme_without_inventory_table(tmp_path: Path) -> None:
    _write(tmp_path, "README.md", "# Repo\n\nKhông có bảng.\n")
    _write(tmp_path, "docs/README.md", _inventory_file([("docs/a.md", "hiện hành")]))

    with pytest.raises(check_docs.InventoryError, match="README.md"):
        check_docs.load_inventory(tmp_path)


# --- DOC-1 ---

def test_inventory_flags_file_missing_from_table(tmp_path: Path) -> None:
    _tree(tmp_path, [], {"docs/guides/x.md": "x\n", "CLAUDE.md": "gốc\n"})

    problems = check_docs.inventory_problems(tmp_path, check_docs.load_inventory(tmp_path))

    assert any("docs/guides/x.md" in problem for problem in problems)
    assert any(problem.startswith("CLAUDE.md") for problem in problems), "CLAUDE.md ở gốc cũng phải có dòng kiểm kê"
    assert all("trong bảng kiểm kê README.md" in problem for problem in problems), "thông báo phải nêu README.md là nơi đặt bảng"


def test_inventory_flags_row_pointing_to_missing_file(tmp_path: Path) -> None:
    _tree(tmp_path, [("docs/ghost.md", "hiện hành")])

    problems = check_docs.inventory_problems(tmp_path, check_docs.load_inventory(tmp_path))

    assert any("docs/ghost.md" in problem and "không tồn tại" in problem for problem in problems)


def test_inventory_flags_historical_markdown_without_banner(tmp_path: Path) -> None:
    _tree(tmp_path, [("docs/old.md", "lịch sử")], {"docs/old.md": "# Cũ\n\nKhông banner\n"})

    problems = check_docs.inventory_problems(tmp_path, check_docs.load_inventory(tmp_path))

    assert any(problem.startswith("docs/old.md") and "banner" in problem for problem in problems)


def test_inventory_accepts_complete_tree_with_banner_and_non_markdown_history(tmp_path: Path) -> None:
    _tree(
        tmp_path,
        [("docs/old.md", "lịch sử"), ("docs/old.docx", "lịch sử"), ("docs/new.md", "hiện hành")],
        {"docs/old.md": f"\n{BANNER}\n\n# Cũ\n", "docs/old.docx": "nhị phân", "docs/new.md": "# Mới\n"},
    )

    assert check_docs.inventory_problems(tmp_path, check_docs.load_inventory(tmp_path)) == []


def test_inventory_ignores_excluded_directories(tmp_path: Path) -> None:
    _tree(tmp_path, [], {".git/README.md": "x", "node_modules/pkg/README.md": "x", "spec/README.md": "x", "docs/data/raw.csv": "a,b"})

    assert check_docs.inventory_problems(tmp_path, check_docs.load_inventory(tmp_path)) == []


# --- DOC-2 ---

def test_removed_term_in_current_document_is_a_problem(tmp_path: Path) -> None:
    _tree(tmp_path, [("docs/a.md", "hiện hành")], {"docs/a.md": "# A\n\nDùng MediatR để gửi lệnh.\n"})

    problems = check_docs.removed_term_problems(tmp_path, check_docs.load_inventory(tmp_path))

    assert len(problems) == 1 and problems[0].startswith("docs/a.md:3:") and "'MediatR'" in problems[0]


def test_removed_term_on_past_tense_line_is_allowed(tmp_path: Path) -> None:
    _tree(tmp_path, [("docs/a.md", "hiện hành")], {"docs/a.md": "# A\n\nMediatR đã gỡ khỏi dự án.\nCấm dùng /api/v1/ nữa.\n"})

    assert check_docs.removed_term_problems(tmp_path, check_docs.load_inventory(tmp_path)) == []


def test_removed_term_in_historical_document_is_allowed(tmp_path: Path) -> None:
    _tree(tmp_path, [("docs/old.md", "lịch sử")], {"docs/old.md": f"{BANNER}\n\nDùng MediatR.\n"})

    assert check_docs.removed_term_problems(tmp_path, check_docs.load_inventory(tmp_path)) == []


def test_removed_term_scan_includes_code_blocks_and_root_documents(tmp_path: Path) -> None:
    _tree(tmp_path, [("CLAUDE.md", "hiện hành")], {"CLAUDE.md": "# Repo\n\n```bash\ndocker compose --profile evaluation up\n```\n"})

    problems = check_docs.removed_term_problems(tmp_path, check_docs.load_inventory(tmp_path))

    assert len(problems) == 1 and problems[0].startswith("CLAUDE.md:4:") and "--profile evaluation" in problems[0]


def test_removed_term_scan_skips_non_markdown_and_unlisted_files(tmp_path: Path) -> None:
    _tree(tmp_path, [("docs/data.sql", "hiện hành")], {"docs/data.sql": "-- MediatR\n", "docs/stray.md": "MediatR\n"})

    assert check_docs.removed_term_problems(tmp_path, check_docs.load_inventory(tmp_path)) == []


# --- DOC-3 ---

def test_link_to_missing_file_is_a_problem(tmp_path: Path) -> None:
    _write(tmp_path, "docs/a.md", "# A\n\nXem [B](b.md).\n")

    problems, external = check_docs.link_problems(tmp_path)

    assert len(problems) == 1 and problems[0].startswith("docs/a.md:3:") and "b.md" in problems[0] and external == 0


def test_link_to_existing_file_and_percent_encoded_name_is_ok(tmp_path: Path) -> None:
    _write(tmp_path, "docs/a.md", "[B](b.md) [C](c%20d.md) [Gốc](../README.md)\n")
    _write(tmp_path, "docs/b.md", "# B\n")
    _write(tmp_path, "docs/c d.md", "# C\n")
    _write(tmp_path, "README.md", "# R\n")

    assert check_docs.link_problems(tmp_path) == ([], 0)


def test_wrong_anchor_is_a_problem(tmp_path: Path) -> None:
    _write(tmp_path, "docs/a.md", "[B](b.md#khong-co)\n")
    _write(tmp_path, "docs/b.md", "# Tiêu đề thật\n")

    problems, _ = check_docs.link_problems(tmp_path)

    assert len(problems) == 1 and "khong-co" in problems[0]


def test_vietnamese_anchor_and_same_file_anchor_and_duplicate_heading_are_ok(tmp_path: Path) -> None:
    _write(tmp_path, "docs/a.md", "# Đánh giá\n\n## Test\n\n## Test\n\n[tự](#đánh-giá) [hai](#test-1) [b](b.md#415-doc--tài-liệu-gói-57)\n")
    _write(tmp_path, "docs/b.md", "## 4.15. DOC — Tài liệu (gói 5.7)\n")

    assert check_docs.link_problems(tmp_path) == ([], 0)


def test_same_file_wrong_anchor_is_a_problem(tmp_path: Path) -> None:
    _write(tmp_path, "docs/a.md", "# A\n\n[x](#b)\n")

    problems, _ = check_docs.link_problems(tmp_path)

    assert len(problems) == 1 and problems[0].startswith("docs/a.md:3:")


def test_links_inside_code_blocks_and_inline_code_are_skipped(tmp_path: Path) -> None:
    _write(tmp_path, "docs/a.md", "```md\n[x](missing.md)\n```\n\nVí dụ `[y](missing2.md)` trong dòng.\n")

    assert check_docs.link_problems(tmp_path) == ([], 0)


def test_headings_inside_code_blocks_do_not_create_anchors(tmp_path: Path) -> None:
    _write(tmp_path, "docs/a.md", "```bash\n# comment\n```\n\n[x](#comment)\n")

    problems, _ = check_docs.link_problems(tmp_path)

    assert len(problems) == 1


def test_external_links_are_counted_not_checked(tmp_path: Path) -> None:
    _write(tmp_path, "docs/a.md", "[web](https://example.com/x) [mail](mailto:a@b.c) [ngoài](../../outside.md)\n")

    problems, external = check_docs.link_problems(tmp_path)

    assert problems == [] and external == 3


# --- main ---

def _clean_tree(root: Path) -> None:
    _tree(root, [("docs/a.md", "hiện hành")], {"docs/a.md": "# A\n\nNội dung ổn.\n"})


def test_main_exits_zero_when_all_pass(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    _clean_tree(tmp_path)

    code = check_docs.main(["--root", str(tmp_path)])

    out = capsys.readouterr().out
    assert code == 0 and "DOC-1: đạt" in out and "DOC-2: đạt" in out and "DOC-3: đạt" in out


def test_main_exits_one_and_lists_problems(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    _clean_tree(tmp_path)
    _write(tmp_path, "docs/a.md", "# A\n\nDùng MediatR. [x](missing.md)\n")

    code = check_docs.main(["--root", str(tmp_path)])

    out = capsys.readouterr().out
    assert code == 1 and "DOC-1: đạt" in out and "DOC-2: không đạt" in out and "DOC-3: không đạt" in out
    assert "docs/a.md:3:" in out


def test_main_reports_broken_inventory_as_doc1_failure(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    code = check_docs.main(["--root", str(tmp_path)])

    out = capsys.readouterr().out
    assert code == 1 and "DOC-1: không đạt" in out
