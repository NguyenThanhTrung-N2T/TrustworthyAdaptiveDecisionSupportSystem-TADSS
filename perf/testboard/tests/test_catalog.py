# Task 2.1: tests/catalog.yaml sinh từ methodology và khớp hai chiều; cùng tập mã với evaluator (coverage_map) và CatalogIndex C#
import pytest
from testboard import catalog, paths

import coverage_map  # tools/evidence (nạp qua testboard.paths)


def test_catalog_file_is_up_to_date_with_methodology():
    expected = catalog.render_yaml(catalog.build_entries(paths.METHODOLOGY.read_text(encoding="utf-8")))

    assert paths.CATALOG_FILE.read_text(encoding="utf-8") == expected, "chạy `python perf/board.py catalog` sau khi sửa methodology"


def test_catalog_has_every_methodology_code_and_nothing_else():
    methodology_codes = coverage_map.catalog_codes(paths.METHODOLOGY)

    assert list(catalog.load()) == methodology_codes


def test_every_entry_has_group_label_tool_system_and_methodology_link():
    for entry in catalog.load().values():
        assert entry.group_label and entry.tool and entry.title, entry.code
        assert set(entry.systems) <= set(catalog.SYSTEMS) and entry.systems, entry.code
        assert entry.link.startswith(paths.METHODOLOGY_URL + "#4"), entry.code


@pytest.mark.parametrize(
    ("code", "group"),
    [("UT-01", "UT"), ("BE-UT-01", "BE-UT"), ("OB-1", "OB"), ("OB-BE-1", "OB-BE"), ("SCALE-2", "SCALE"), ("E2E-13", "E2E"), ("SP-10", "SP")],
)
def test_group_of_strips_only_the_trailing_number(code, group):
    assert catalog.group_of(code) == group


@pytest.mark.parametrize(
    ("code", "systems"),
    [("UT-01", ("TADSS",)), ("FI-03", ("TADSS",)), ("FI-12", ("BE",)), ("CT-04", ("BE",)), ("LT-04", ("BE",)), ("BE-IT-20", ("BE",)),
     ("AI-CT-01", ("AI",)), ("SEC-AI-4", ("AI",)), ("OPS-01", ("BE", "TADSS", "AI")), ("SP-10", ("BE", "AI")), ("SP-01", ("BE",))],
)
def test_systems_of_puts_code_on_the_right_tabs(code, systems):
    assert catalog.systems_of(code, catalog.group_of(code)) == systems


def test_build_entries_rejects_unknown_group_and_duplicates():
    head = "## 4. DANH MỤC\n\n### 4.1. X\n\n| ID | Phạm vi | Tiêu chí đạt |\n| :--- | :--- | :--- |\n"

    with pytest.raises(ValueError, match="ZZ"):
        catalog.build_entries(head + "| ZZ-01 | a | b |\n")
    with pytest.raises(ValueError, match="hai lần"):
        catalog.build_entries(head + "| UT-01 | a | b |\n| UT-01 | c | d |\n")


def test_build_entries_reads_scope_criteria_and_ignores_rows_outside_section_4():
    text = ("## 3. KHÁC\n\n| ID | x |\n| UT-99 | ngoài mục 4 |\n\n## 4. DANH MỤC\n\n### 4.1. UT — Unit\n\n"
            "| ID | Phạm vi | Tiêu chí đạt | Xây dựng |\n| :--- | :--- | :--- | :--- |\n| UT-01 | Bất biến `domain` [v2](x.md) | **Đạt** hết | P5 |\n\n## 5. KPI\n| UT-02 | sau mục 4 |\n")

    [entry] = catalog.build_entries(text)

    assert (entry.code, entry.title, entry.criteria, entry.section) == ("UT-01", "Bất biến domain v2", "Đạt hết", "4.1. UT — Unit")
    assert entry.link.endswith("#41-ut--unit")
