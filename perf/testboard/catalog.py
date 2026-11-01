"""Danh mục kiểm thử máy đọc `tests/catalog.yaml` (task 2.1, P6-50): sinh từ các bảng Mục 4 của methodology, một nguồn chi tiết duy nhất.
Mỗi mã: nhóm, tên nhóm, hệ thống (tab dashboard), phạm vi, tiêu chí, công cụ, link tới mục methodology. Không sửa tay file yaml: sửa methodology
rồi `python perf/testboard.py catalog`; test `test_catalog.py` đỏ khi hai bên lệch."""
from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path

import yaml

from testboard import paths
from check_docs import github_slug  # noqa: E402  (tools/docs, nạp qua paths)

# Cùng hình dạng mã với tools/evidence/coverage_map.py và CatalogIndex phía C#: UT-01, SCALE-2, E2E-13, BE-UT-01, OB-BE-1
CODE = re.compile(r"[A-Z][A-Z0-9]*(?:-[A-Z]{2})?-\d{1,2}[a-z]?")
_ROW = re.compile(rf"^\|\s*({CODE.pattern})\s*\|")
_SECTION = re.compile(r"^###\s+(4\.\d+\..*)$")
_TRAILING_NUMBER = re.compile(r"-\d{1,2}[a-z]?$")
_MARKDOWN_LINK = re.compile(r"\[([^\]]+)\]\([^)]*\)")
_TITLE_MAX = 180

# Tên nhóm hiện trên dashboard và công cụ chạy. Methodology thêm nhóm mới mà chưa khai ở đây ⇒ `build_entries` báo lỗi.
GROUPS: dict[str, tuple[str, str]] = {
    "UT": ("Unit TADSS", "xUnit"),
    "IT": ("Integration TADSS", "xUnit + Testcontainers"),
    "CT": ("Contract", "JsonSchema.Net, Schemathesis"),
    "MT": ("Mutation", "Stryker.NET"),
    "FI": ("Chịu lỗi", "xUnit + Toxiproxy"),
    "LT": ("Tải (LT)", "k6"),
    "BE-UT": ("Unit BE", "xUnit"),
    "BE-IT": ("Integration BE", "xUnit + Testcontainers + WireMock.Net"),
    "FE": ("FE", "Playwright + MSW"),
    "E2E": ("E2E", "Playwright, k6"),
    "RP": ("Replay dữ liệu thật", "Python (evaluator)"),
    "PT": ("Độc lập domain", "Python (evaluator)"),
    "SEC": ("Bảo mật TADSS", "xUnit, k6"),
    "NG": ("Expose ngrok", "verify-expose.sh"),
    "SCALE": ("Nhiều instance", "xUnit + Testcontainers, k6"),
    "OB": ("Quan sát TADSS", "xUnit, verify-observability.sh"),
    "DOC": ("Tài liệu", "check_docs.py, xUnit"),
    "OPS": ("Đóng gói và CD", "check-image.sh, cd.sh, CI"),
    "OB-BE": ("Quan sát BE", "xUnit, grafana_cloud.py"),
    "OB-AI": ("Quan sát AI", "pytest, Tempo"),
    "SEC-BE": ("Bảo mật BE", "CI"),
    "SEC-AI": ("Bảo mật AI", "CI, compose"),
    "AI-UT": ("Unit AI", "pytest (LLM giả)"),
    "AI-CT": ("Hợp đồng BE ↔ AI", "xUnit (BE)"),
    "SP": ("Hiệu năng hệ thống", "k6 (perf)"),
}
SYSTEMS = ("BE", "TADSS", "AI")
_BE_GROUPS = {"BE-UT", "BE-IT", "FE", "E2E", "OB-BE", "SEC-BE", "SP"}
_AI_GROUPS = {"AI-UT", "AI-CT", "OB-AI", "SEC-AI"}


@dataclass(frozen=True)
class CatalogEntry:
    code: str
    group: str
    group_label: str
    systems: tuple[str, ...]
    title: str
    criteria: str
    runner: str
    tool: str
    section: str
    link: str


def group_of(code: str) -> str:
    """`BE-UT-01` → `BE-UT`, `OB-1` → `OB`, `OB-BE-1` → `OB-BE` (khác `group_of` của coverage_map chỉ lấy đoạn đầu)."""
    return _TRAILING_NUMBER.sub("", code)


def systems_of(code: str, group: str) -> tuple[str, ...]:
    """Tab dashboard hiện mã này. OPS thuộc cả ba; SP-10 đo BE + AI; FI-11…14, CT-04, LT-04 là tích hợp phía BE."""
    if group == "OPS":
        return SYSTEMS
    if code == "SP-10":
        return ("BE", "AI")
    if group in _BE_GROUPS or code in {"CT-04", "LT-04"} or re.fullmatch(r"FI-1[1-4]", code):
        return ("BE",)
    if group in _AI_GROUPS:
        return ("AI",)
    return ("TADSS",)


def _plain(cell: str) -> str:
    text = _MARKDOWN_LINK.sub(r"\1", cell).replace("`", "").replace("**", "").strip()
    text = re.sub(r"\s+", " ", text)
    return text if len(text) <= _TITLE_MAX else text[: _TITLE_MAX - 1].rstrip() + "…"


def _cells(line: str) -> list[str]:
    return [cell.strip() for cell in line.strip().strip("|").split("|")]


def build_entries(methodology_text: str) -> list[CatalogEntry]:
    """Mọi mã ở cột đầu của các bảng trong Mục 4, theo thứ tự xuất hiện; trùng mã hoặc nhóm chưa khai ⇒ ValueError."""
    entries: list[CatalogEntry] = []
    seen: set[str] = set()
    section, headers, in_catalog = "", [], False
    for line in methodology_text.splitlines():
        if line.startswith("## "):
            in_catalog = line.startswith("## 4.")
            continue
        if not in_catalog:
            continue
        heading = _SECTION.match(line)
        if heading:
            section, headers = heading.group(1).strip(), []
            continue
        if line.startswith("| ID |"):
            headers = _cells(line)
            continue
        row = _ROW.match(line)
        if not row:
            continue
        code = row.group(1)
        if code in seen:
            raise ValueError(f"mã {code} xuất hiện hai lần trong methodology")
        seen.add(code)
        group = group_of(code)
        if group not in GROUPS:
            raise ValueError(f"nhóm {group} (mã {code}) chưa khai trong testboard/catalog.py GROUPS")
        cells = _cells(line)
        column = dict(zip(headers, cells))
        label, tool = GROUPS[group]
        entries.append(CatalogEntry(
            code=code,
            group=group,
            group_label=label,
            systems=systems_of(code, group),
            title=_plain(cells[1] if len(cells) > 1 else ""),
            criteria=_plain(column.get("Tiêu chí đạt", "")),
            runner=_plain(column.get("Ai chạy", "")),
            tool=tool,
            section=section,
            link=f"{paths.METHODOLOGY_URL}#{github_slug(section)}",
        ))
    return entries


def render_yaml(entries: list[CatalogEntry]) -> str:
    """YAML tất định (chuỗi dạng JSON, là chuỗi YAML hợp lệ) để diff gọn và test so khớp từng byte."""
    lines = [
        "# Danh mục kiểm thử máy đọc (Epic 6C, task 2.1). SINH TỰ ĐỘNG từ Mục 4 của",
        f"# {paths.METHODOLOGY_REPO_PATH}: không sửa tay, sửa methodology rồi chạy `python perf/testboard.py catalog`.",
        "version: 1",
        f"source: {json.dumps(paths.METHODOLOGY_REPO_PATH)}",
        "codes:",
    ]
    for entry in entries:
        lines.append(f"  - code: {json.dumps(entry.code)}")
        for key in ("group", "group_label", "systems", "title", "criteria", "runner", "tool", "section", "link"):
            value = getattr(entry, key)
            rendered = json.dumps(list(value), ensure_ascii=False) if isinstance(value, tuple) else json.dumps(value, ensure_ascii=False)
            lines.append(f"    {key}: {rendered}")
    return "\n".join(lines) + "\n"


def write(path: Path = paths.CATALOG_FILE) -> list[CatalogEntry]:
    entries = build_entries(paths.METHODOLOGY.read_text(encoding="utf-8"))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(render_yaml(entries), encoding="utf-8", newline="\n")
    return entries


def load(path: Path = paths.CATALOG_FILE) -> dict[str, CatalogEntry]:
    data = yaml.safe_load(path.read_text(encoding="utf-8"))
    return {item["code"]: CatalogEntry(**{**item, "systems": tuple(item["systems"])}) for item in data["codes"]}
