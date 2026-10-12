"""Dữ liệu D2 (k6 timeseries): đọc dòng console của k6, loại warm-up, chuẩn hóa cột theo DATASET_SCHEMA."""
from __future__ import annotations

import csv
import json
import re
from collections import Counter
from pathlib import Path
from typing import Iterable, Iterator

D2_COLUMNS = ("timestamp_utc", "scenario", "http_status", "duration_ms", "decision_status", "execution_type", "correlation_id")
WARMUP_PHASE = "warmup"
# k6 --console-output ghi dạng logfmt: time="..." level=info msg="<JSON đã escape>" — lấy phần msg
_MSG = re.compile(r'msg="((?:[^"\\]|\\.)*)"')


def parse_console_line(line: str) -> dict | None:
    """Một dòng console của k6 → dict (JSON thô hoặc logfmt chứa JSON); dòng khác (log nội bộ của k6) → None."""
    text = line.strip()
    if text.startswith("{"):
        payload = text
    else:
        match = _MSG.search(text)
        if not match:
            return None
        try:
            payload = json.loads(f'"{match.group(1)}"')
        except json.JSONDecodeError:
            return None
    if not payload.startswith("{"):
        return None
    try:
        parsed = json.loads(payload)
    except json.JSONDecodeError:
        return None
    return parsed if isinstance(parsed, dict) else None


def read_entries(path: Path) -> Iterator[dict]:
    """Mọi đối tượng JSON trong file console của k6, theo thứ tự."""
    with path.open(encoding="utf-8") as handle:
        for line in handle:
            entry = parse_console_line(line)
            if entry is not None:
                yield entry


def request_entries(entries: Iterable[dict]) -> list[dict]:
    """Chỉ các dòng request (có `http_status`)."""
    return [entry for entry in entries if "http_status" in entry]


def to_d2_rows(entries: Iterable[dict]) -> list[dict[str, object]]:
    """Dòng D2 đúng cột của DATASET_SCHEMA; request ở pha warm-up bị loại."""
    rows = []
    for entry in request_entries(entries):
        if entry.get("phase") == WARMUP_PHASE:
            continue
        rows.append(
            {
                "timestamp_utc": entry["ts"],
                "scenario": entry["scenario"],
                "http_status": entry["http_status"],
                "duration_ms": entry["duration_ms"],
                "decision_status": entry.get("decision_status"),
                "execution_type": entry.get("execution_type"),
                "correlation_id": entry.get("correlation_id"),
            }
        )
    return rows


def write_jsonl(entries: Iterable[dict], path: Path) -> int:
    """Ghi file `requests.jsonl` (mỗi request một dòng JSON sạch, kể cả warm-up để truy vết); trả số dòng."""
    path.parent.mkdir(parents=True, exist_ok=True)
    count = 0
    with path.open("w", encoding="utf-8") as handle:
        for entry in request_entries(entries):
            handle.write(json.dumps(entry, ensure_ascii=False, separators=(",", ":")) + "\n")
            count += 1
    return count


def write_d2_csv(rows: list[dict[str, object]], path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=D2_COLUMNS)
        writer.writeheader()
        writer.writerows(rows)


def status_counts(entries: Iterable[dict], include_warmup: bool = False) -> Counter:
    """Số response theo mã HTTP (0 = không có phản hồi: lỗi kết nối hoặc quá hạn); mặc định chỉ tính pha chính."""
    return Counter(
        int(entry["http_status"])
        for entry in request_entries(entries)
        if include_warmup or entry.get("phase") != WARMUP_PHASE
    )


def forbidden_statuses(counts: Counter) -> dict[int, int]:
    """Mã không được xuất hiện khi rate limit đã nâng và payload hợp lệ: 400 (bộ sinh sai), 429 (bị giới hạn), mọi 5xx."""
    return {status: count for status, count in counts.items() if status == 400 or status == 429 or status >= 500}
