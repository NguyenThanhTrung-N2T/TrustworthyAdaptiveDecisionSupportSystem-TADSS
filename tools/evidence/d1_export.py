"""Xuất D1 `decision_audit` (DATASET_SCHEMA Mục 3) từ schema `tadss` của stack đánh giá: một dòng = một bản ghi quyết định, ghép override và outcome mới nhất. Định danh được ẩn danh hóa; đặc trưng request tách từ `request_payload`."""
from __future__ import annotations

import csv
import json
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Callable, Iterable

from evidence.anonymize import Anonymizer

Fetch = Callable[[str, tuple], list[tuple]]

D1_COLUMNS = (
    "decision_id", "reservation_hash", "branch_hash", "role", "correlation_id", "created_at_utc",
    "strategy_name", "strategy_version", "strategy_status", "status", "deposit_rate", "deposit_amount", "reason_code", "approval_expires_at_utc",
    "execution_type", "execution_time_ms", "fallback_used", "failure_reason", "cache_hit", "request_hash",
    "items_count", "total_price", "currency", "earliest_start_local", "lead_time_hours", "is_peak", "customer_is_guest", "customer_tier", "customer_member_days",
    "history_completed", "history_no_show", "history_cancelled", "history_late_cancelled", "history_payment_expired", "demand_occupancy_rate", "demand_waitlist_count",
    "override_action", "override_deposit_rate", "override_actor_hash", "overridden_at_utc", "outcome", "outcome_occurred_at_utc",
)

_QUERY = """
SELECT e.decision_id, e.reservation_id, e.branch_id, e.role, e.correlation_id, e.created_at,
       e.strategy_name, e.strategy_version, e.strategy_status, e.status, e.deposit_rate, e.deposit_amount, e.reason_code, e.approval_expires_at,
       e.execution_type, e.execution_time_ms, e.fallback_used, e.failure_reason, e.cache_hit, e.request_hash, e.request_payload::text,
       o.action, o.deposit_rate, o.actor_id, o.created_at,
       c.outcome, c.occurred_at
FROM tadss.decision_executions e
LEFT JOIN LATERAL (SELECT * FROM tadss.decision_overrides x WHERE x.decision_id = e.decision_id ORDER BY x.created_at DESC LIMIT 1) o ON true
LEFT JOIN LATERAL (SELECT * FROM tadss.decision_outcomes y WHERE y.decision_id = e.decision_id ORDER BY y.reported_at DESC LIMIT 1) c ON true
WHERE e.created_at >= %s
ORDER BY e.created_at, e.id
"""


def _iso(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, datetime):
        moment = value if value.tzinfo else value.replace(tzinfo=timezone.utc)
        return moment.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")
    return str(value)


def _parse_time(value: str | None) -> datetime | None:
    return None if not value else datetime.fromisoformat(value.replace("Z", "+00:00"))


def request_features(payload_text: str, created_at: datetime) -> dict[str, Any]:
    """Đặc trưng request từ `request_payload` (JSON của DecisionRequestDto): số item, giá, tổng hợp khách, lead time...; trường không có để trống."""
    payload = json.loads(payload_text)
    items = payload.get("items") or []
    customer = payload.get("customer") or {}
    history = customer.get("history") or {}
    demand = payload.get("demand") or {}
    starts = [moment for moment in (_parse_time(item.get("startTime")) for item in items) if moment is not None]
    earliest = min(starts) if starts else None
    requested = _parse_time(payload.get("requestedAt")) or created_at
    member_since = _parse_time(customer.get("memberSince"))
    return {
        "items_count": len(items),
        "total_price": payload.get("totalPrice", ""),
        "currency": payload.get("currency", ""),
        "earliest_start_local": earliest.replace(microsecond=0).isoformat() if earliest else "",
        "lead_time_hours": round((earliest - requested).total_seconds() / 3600, 2) if earliest else "",
        "is_peak": "",  # do strategy suy ra từ lịch cao điểm, không có trong request; để trống theo DATASET_SCHEMA (trường tùy chọn)
        "customer_is_guest": customer.get("isGuest", ""),
        "customer_tier": customer.get("tier", ""),
        "customer_member_days": (requested - member_since).days if member_since else "",
        "history_completed": history.get("completedCount", ""),
        "history_no_show": history.get("noShowCount", ""),
        "history_cancelled": history.get("cancelledCount", ""),
        "history_late_cancelled": history.get("lateCancelledCount", ""),
        "history_payment_expired": history.get("paymentExpiredCount", ""),
        "demand_occupancy_rate": demand.get("occupancyRate", ""),
        "demand_waitlist_count": demand.get("waitlistCount", ""),
    }


def to_d1_rows(rows: Iterable[tuple], anonymizer: Anonymizer) -> list[dict[str, Any]]:
    result = []
    for row in rows:
        (decision_id, reservation_id, branch_id, role, correlation_id, created_at, strategy_name, strategy_version, strategy_status, status, deposit_rate, deposit_amount, reason_code,
         approval_expires_at, execution_type, execution_time_ms, fallback_used, failure_reason, cache_hit, request_hash, payload,
         override_action, override_rate, override_actor, overridden_at, outcome, outcome_at) = row
        record: dict[str, Any] = {
            "decision_id": str(decision_id),
            "reservation_hash": anonymizer.hash("reservation", str(reservation_id)),
            "branch_hash": anonymizer.hash("branch", str(branch_id)),
            "role": role,
            "correlation_id": correlation_id or "",
            "created_at_utc": _iso(created_at),
            "strategy_name": strategy_name, "strategy_version": strategy_version, "strategy_status": strategy_status, "status": status,
            "deposit_rate": "" if deposit_rate is None else deposit_rate, "deposit_amount": "" if deposit_amount is None else deposit_amount,
            "reason_code": reason_code, "approval_expires_at_utc": _iso(approval_expires_at),
            "execution_type": execution_type, "execution_time_ms": execution_time_ms, "fallback_used": fallback_used,
            "failure_reason": failure_reason or "", "cache_hit": cache_hit, "request_hash": (request_hash or "").strip(),
            "override_action": override_action or "", "override_deposit_rate": "" if override_rate is None else override_rate,
            "override_actor_hash": anonymizer.hash("actor", override_actor), "overridden_at_utc": _iso(overridden_at),
            "outcome": outcome or "", "outcome_occurred_at_utc": _iso(outcome_at),
        }
        record.update(request_features(payload, created_at))
        result.append(record)
    return result


def export_d1(fetch: Fetch, anonymizer: Anonymizer, since: datetime, path: Path) -> int:
    """Ghi D1 (bản ghi tạo từ `since` trở đi — chỉ phần của lượt chạy); trả số dòng."""
    rows = to_d1_rows(fetch(_QUERY, (since - timedelta(seconds=5),)), anonymizer)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=D1_COLUMNS)
        writer.writeheader()
        writer.writerows(rows)
    return len(rows)
