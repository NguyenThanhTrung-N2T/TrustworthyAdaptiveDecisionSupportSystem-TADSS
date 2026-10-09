"""`chaos_log` (D3, DATASET_SCHEMA): một dòng cho mỗi lỗi đã tiêm."""
from __future__ import annotations

import csv
from dataclasses import dataclass
from pathlib import Path

CHAOS_LOG_COLUMNS = ("fi_id", "run_id", "toxic", "started_at_utc", "ended_at_utc", "expected", "observed", "passed", "invariant_violations")


@dataclass(frozen=True)
class ChaosRow:
    fi_id: str
    run_id: str
    toxic: str
    started_at_utc: str
    ended_at_utc: str
    expected: str
    observed: str
    passed: bool
    invariant_violations: int


def write_chaos_log(rows: list[ChaosRow], path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=CHAOS_LOG_COLUMNS)
        writer.writeheader()
        for row in rows:
            writer.writerow({column: getattr(row, column) for column in CHAOS_LOG_COLUMNS})
