"""Ghép kết quả trx với chỉ mục `{test → [CatalogId]}` do test xuất ra (O-22) thành `catalog-results.csv`."""
from __future__ import annotations

import csv
import json
import re
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

_NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
_OUTCOMES = {"Passed": "passed", "Failed": "failed", "NotExecuted": "skipped", "Inconclusive": "skipped"}
_DURATION = re.compile(r"^(?P<h>\d+):(?P<m>\d+):(?P<s>\d+(?:\.\d+)?)$")
CSV_COLUMNS = ("catalog_id", "test", "outcome", "duration_ms")


class CatalogJoinError(ValueError):
    """Có test trong trx mà chỉ mục không biết (chỉ mục thiếu hoặc lệch phiên bản test)."""


@dataclass(frozen=True)
class TestOutcome:
    """Một kết quả trong trx: tên gốc của phương thức (đã bỏ tham số theory), kết quả và thời lượng."""

    __test__ = False  # không phải lớp test của pytest

    test: str
    outcome: str
    duration_ms: float


def normalize(name: str) -> str:
    """`Ns.Class.Method(arg: 1)` → `Ns.Class.Method`; lớp lồng `Outer+Inner` → `Outer.Inner` để khớp giữa trx và chỉ mục."""
    return name.split("(", 1)[0].strip().replace("+", ".")


def parse_duration_ms(value: str | None) -> float:
    match = _DURATION.match(value or "")
    if not match:
        return 0.0
    return round((int(match["h"]) * 3600 + int(match["m"]) * 60 + float(match["s"])) * 1000, 3)


def parse_trx(path: Path) -> list[TestOutcome]:
    """Mọi `UnitTestResult` của một file trx (mỗi ca theory một dòng)."""
    results = []
    for node in ET.parse(path).getroot().iterfind(".//t:UnitTestResult", _NS):
        results.append(
            TestOutcome(
                test=normalize(node.attrib["testName"]),
                outcome=_OUTCOMES.get(node.attrib.get("outcome", ""), "failed"),
                duration_ms=parse_duration_ms(node.attrib.get("duration")),
            )
        )
    return results


def load_index(paths: list[Path]) -> dict[str, list[str]]:
    """Gộp các `catalog-index.*.json` (khóa đã chuẩn hóa)."""
    index: dict[str, list[str]] = {}
    for path in paths:
        for test, ids in json.loads(path.read_text(encoding="utf-8")).items():
            index[normalize(test)] = list(ids)
    return index


def join(outcomes: list[TestOutcome], index: dict[str, list[str]]) -> list[dict[str, object]]:
    """Mỗi (kết quả × mã) một dòng. Test không có trong chỉ mục ⇒ `CatalogJoinError` liệt kê tên (không âm thầm bỏ qua)."""
    missing = sorted({outcome.test for outcome in outcomes if outcome.test not in index})
    if missing:
        raise CatalogJoinError(f"{len(missing)} test không có trong chỉ mục catalog, ví dụ: {missing[:5]}")
    return [
        {"catalog_id": catalog_id, "test": outcome.test, "outcome": outcome.outcome, "duration_ms": outcome.duration_ms}
        for outcome in outcomes
        for catalog_id in index[outcome.test]
    ]


def write_csv(rows: list[dict[str, object]], path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=CSV_COLUMNS)
        writer.writeheader()
        writer.writerows(sorted(rows, key=lambda row: (row["catalog_id"], row["test"])))


def summarize(rows: list[dict[str, object]]) -> dict[str, dict[str, int]]:
    """Số ca theo mã và kết quả: `{catalog_id: {passed: n, failed: n, skipped: n}}` (dùng cho bảng tóm tắt và báo cáo)."""
    summary: dict[str, dict[str, int]] = {}
    for row in rows:
        bucket = summary.setdefault(str(row["catalog_id"]), {"passed": 0, "failed": 0, "skipped": 0})
        bucket[str(row["outcome"])] += 1
    return summary
