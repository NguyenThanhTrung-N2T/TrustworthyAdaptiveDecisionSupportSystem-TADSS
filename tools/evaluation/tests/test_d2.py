"""D2: đọc console của k6 (logfmt hoặc JSON thô), loại warm-up, cột theo DATASET_SCHEMA."""
import csv
import json
from pathlib import Path

from evaluator import d2

ENTRY_MAIN = {
    "ts": "2026-10-08T01:00:05.123Z", "scenario": "lt01", "phase": "main", "http_status": 200, "duration_ms": 12.5,
    "decision_status": "APPROVED", "execution_type": "AUTOMATED", "correlation_id": "c-1", "idempotency_key": "k-1", "decision_id": "d-1",
}
ENTRY_WARMUP = {**ENTRY_MAIN, "phase": "warmup", "correlation_id": "c-0"}
ENTRY_ERROR = {**ENTRY_MAIN, "http_status": 503, "decision_status": None, "execution_type": None, "correlation_id": "c-2"}


def _logfmt(entry: dict) -> str:
    escaped = json.dumps(json.dumps(entry, ensure_ascii=False), ensure_ascii=False)[1:-1]
    return f'time="2026-10-08T01:00:05Z" level=info msg="{escaped}"'


def test_parse_console_line_handles_logfmt_json_and_noise() -> None:
    assert d2.parse_console_line(_logfmt(ENTRY_MAIN)) == ENTRY_MAIN
    assert d2.parse_console_line(json.dumps(ENTRY_MAIN)) == ENTRY_MAIN
    assert d2.parse_console_line('time="x" level=warning msg="Request Failed"') is None
    assert d2.parse_console_line("") is None
    assert d2.parse_console_line("{không phải json") is None


def test_requests_jsonl_to_d2(tmp_path: Path) -> None:
    console = tmp_path / "console.log"
    console.write_text("\n".join([_logfmt(ENTRY_WARMUP), _logfmt(ENTRY_MAIN), 'time="x" level=info msg="khác"', _logfmt(ENTRY_ERROR)]), encoding="utf-8")

    entries = list(d2.read_entries(console))
    rows = d2.to_d2_rows(entries)

    assert [row["correlation_id"] for row in rows] == ["c-1", "c-2"], "dòng warm-up bị loại"
    assert list(rows[0].keys()) == list(d2.D2_COLUMNS)
    assert rows[0] == {
        "timestamp_utc": "2026-10-08T01:00:05.123Z", "scenario": "lt01", "http_status": 200, "duration_ms": 12.5,
        "decision_status": "APPROVED", "execution_type": "AUTOMATED", "correlation_id": "c-1",
    }
    target = tmp_path / "d2.csv"
    d2.write_d2_csv(rows, target)
    with target.open(encoding="utf-8", newline="") as handle:
        assert next(csv.reader(handle)) == list(d2.D2_COLUMNS)


def test_write_jsonl_keeps_warmup_for_traceability(tmp_path: Path) -> None:
    path = tmp_path / "requests.jsonl"

    count = d2.write_jsonl([ENTRY_WARMUP, {"body": {}, "cells": []}, ENTRY_MAIN], path)

    lines = path.read_text(encoding="utf-8").splitlines()
    assert count == 2 and len(lines) == 2 and json.loads(lines[0])["phase"] == "warmup"


def test_status_counts_and_forbidden_statuses() -> None:
    entries = [ENTRY_WARMUP, ENTRY_MAIN, ENTRY_MAIN, {**ENTRY_MAIN, "http_status": 400}, ENTRY_ERROR, {**ENTRY_MAIN, "http_status": 0}, {**ENTRY_MAIN, "http_status": 429}]

    counts = d2.status_counts(entries)

    assert counts == {200: 2, 400: 1, 503: 1, 0: 1, 429: 1}, "warm-up không tính"
    assert d2.forbidden_statuses(counts) == {400: 1, 503: 1, 429: 1}, "mã 0 chỉ được báo cáo, không làm hỏng bước"
    assert d2.status_counts(entries, include_warmup=True)[200] == 3
