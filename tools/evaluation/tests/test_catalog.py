"""Ghép trx với chỉ mục catalog."""
import csv
from pathlib import Path

import pytest

from conftest import FIXTURES
from evaluator import catalog


def _index() -> dict[str, list[str]]:
    return catalog.load_index([FIXTURES / "index.json"])


def test_parse_trx_normalizes_theory_and_nested_names() -> None:
    outcomes = catalog.parse_trx(FIXTURES / "sample.trx")

    assert [outcome.test for outcome in outcomes] == [
        "TADSS.UnitTests.Sample.Alpha",
        "TADSS.UnitTests.Sample.Theory",
        "TADSS.UnitTests.Sample.Theory",
        "TADSS.UnitTests.Outer.Inner.Nested",
        "TADSS.UnitTests.Sample.Skipped",
    ]
    assert [outcome.outcome for outcome in outcomes] == ["passed", "passed", "failed", "passed", "skipped"]
    assert outcomes[2].duration_ms == 1500.0 and outcomes[0].duration_ms == 12.0


def test_catalog_join(tmp_path: Path) -> None:
    rows = catalog.join(catalog.parse_trx(FIXTURES / "sample.trx"), _index())

    summary = catalog.summarize(rows)
    assert summary["UT-02"] == {"passed": 1, "failed": 1, "skipped": 0}, "mỗi ca theory gom về đúng mã"
    assert summary["FI-03"] == {"passed": 1, "failed": 1, "skipped": 0}, "test có hai mã xuất hiện ở cả hai mã"
    assert summary["UT-14"]["passed"] == 1 and summary["UT-01"] == {"passed": 1, "failed": 0, "skipped": 1}
    assert len(rows) == 7

    target = tmp_path / "raw" / "catalog-results.csv"
    catalog.write_csv(rows, target)
    with target.open(encoding="utf-8", newline="") as handle:
        written = list(csv.DictReader(handle))
    assert list(written[0].keys()) == ["catalog_id", "test", "outcome", "duration_ms"]
    assert [row["catalog_id"] for row in written] == sorted(row["catalog_id"] for row in written)


def test_catalog_join_reports_unknown_test() -> None:
    index = _index()
    del index["TADSS.UnitTests.Sample.Alpha"]

    with pytest.raises(catalog.CatalogJoinError, match="Sample.Alpha"):
        catalog.join(catalog.parse_trx(FIXTURES / "sample.trx"), index)


@pytest.mark.parametrize(("raw", "expected"), [("00:00:01.2500000", 1250.0), ("01:02:03", 3723000.0), (None, 0.0), ("bad", 0.0)])
def test_parse_duration(raw: str | None, expected: float) -> None:
    assert catalog.parse_duration_ms(raw) == expected
