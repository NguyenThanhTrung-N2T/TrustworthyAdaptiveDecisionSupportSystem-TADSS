"""Truy vấn chỉ đọc vào bảng audit `tadss.decision_executions` của stack đánh giá (kiểm bất biến của LT-03 và SCALE-2). Kết nối đi thẳng tới `eval-postgres`, không qua Toxiproxy."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Callable

Fetch = Callable[[str, tuple], list[tuple]]

_TABLE = "tadss.decision_executions"


def connect_fetch(dsn: str) -> Fetch:
    """Hàm truy vấn dùng kết nối mới cho mỗi lần gọi (ít lần, không cần pool). `psycopg` nạp lười để pytest không cần cài."""

    def fetch(sql: str, params: tuple) -> list[tuple]:
        import psycopg  # noqa: PLC0415

        with psycopg.connect(dsn, connect_timeout=10) as connection, connection.cursor() as cursor:
            cursor.execute(sql, params)
            return cursor.fetchall()

    return fetch


@dataclass(frozen=True)
class FallbackStats:
    """Thống kê bản `PRIMARY` loại FALLBACK trong một cửa sổ: số bản, thời gian pipeline lớn nhất (ms), số bản vượt ngân sách."""

    count: int
    max_ms: int
    over_budget: int


class AuditReader:
    def __init__(self, fetch: Fetch) -> None:
        self._fetch = fetch

    def primary_key_counts(self, start: datetime, end: datetime) -> dict[str, int]:
        """Khóa idempotency → số bản `PRIMARY` tạo trong cửa sổ (đúng bất biến thì mọi giá trị là 1)."""
        rows = self._fetch(
            f"SELECT idempotency_key, count(*) FROM {_TABLE} WHERE role = 'PRIMARY' AND created_at BETWEEN %s AND %s AND idempotency_key IS NOT NULL GROUP BY idempotency_key",
            (start, end),
        )
        return {str(key): int(count) for key, count in rows}

    def fallback_stats(self, start: datetime, end: datetime, budget_ms: int = 500) -> FallbackStats:
        rows = self._fetch(
            f"SELECT count(*), coalesce(max(execution_time_ms), 0), count(*) FILTER (WHERE execution_time_ms > %s) FROM {_TABLE} "
            "WHERE role = 'PRIMARY' AND execution_type = 'FALLBACK' AND created_at BETWEEN %s AND %s",
            (budget_ms, start, end),
        )
        count, max_ms, over = rows[0]
        return FallbackStats(int(count), int(max_ms), int(over))
