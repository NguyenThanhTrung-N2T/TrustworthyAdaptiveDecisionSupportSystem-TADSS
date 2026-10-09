"""Bước (e): dữ liệu thực tế (gói 5.6, Q-01…Q-09). Hai phần độc lập, chạy lần lượt RP rồi PT, cùng một `Anonymizer` của lượt chạy (cùng định danh, cùng mã băm ở D4 và D5).

- RP (RP-01): CSV SmashCourt `smashcourt_bookings.csv` trong thư mục dữ liệu riêng (mount chỉ đọc) -> request v2 -> gửi tới `eval-proxy` (cấu hình định tuyến chuẩn, Q-03).
  Thiếu hoặc rỗng file -> phần RP `skipped` (bước vẫn `passed`, Q-05).
- PT (PT-01): dataset khách sạn tải theo URL + SHA-256 ghim (cache ở volume `eval-datasets`, Q-06) -> request v2 -> gửi tới `eval-tadss-portability`. Chế độ quick lấy mẫu 10.000 dòng hợp lệ,
  full lấy mọi dòng hợp lệ (Q-07). Mất mạng -> PT `skipped`; sai SHA-256 -> PT `failed`.
- `now` (giờ gửi) lấy MỘT lần ở đầu mỗi phần, ngay sau khi đích sẵn sàng (`/health/ready`), và ghi vào summary; mọi bản ghi cùng phần dời theo cùng mốc (Q-04).
- Dựng và kiểm schema request chạy kiểu lười theo từng lô 500 dòng, song song với việc gửi (chế độ đầy đủ ~118 nghìn request): bộ nhớ không tăng theo cỡ lô.
- Lỗi contract (Q-09): request do adapter dựng không qua schema (đếm, KHÔNG gửi) hoặc response cuối cùng không phải 200 / sai schema / sai quy tắc §3.3. Bất kỳ lỗi nào -> phần đó `failed`.
  Đích không sẵn sàng hoặc không nhận kết nối -> phần đó `error` trong `summary.json` (cho bước dựng Evidence Pack), nhưng bước (e) vẫn chỉ `failed`: trạng thái `error` làm `run_steps`
  bỏ qua bước (f), mất báo cáo của (a)…(d) và verdict `INSUFFICIENT_DATA` của SC7. Lỗi bất ngờ trong mã (exception) vẫn nổi lên thành `error` qua `run_steps`.

Trạng thái bước: `failed` nếu có phần `failed` hoặc `error`; `skipped` khi cả hai phần không có dữ liệu; còn lại `passed`.

Đầu ra trong `evidence/<run_id>/raw/` (mỗi phần có `summary.json` kể cả khi `skipped`/`failed`/`error`, kèm `status` và `note`):

- `replay/requests.jsonl`, `portability/requests.jsonl`: mỗi dòng MỘT request, KHÔNG có body request: `key` (khóa idempotency), `status` (HTTP cuối; 0 = không có response; null = không gửi vì request lỗi
  schema), `attempts`, `duration_ms`, `decision_id`, `correlation_id`, `outcome` (`status`, `reason_code`, `deposit_rate`, `deposit_amount`: chuỗi thập phân thường, rỗng khi không có),
  `contract_errors` (danh sách chuỗi `SCHEMA|STATUS|RULE: ...`), và `error` (nếu có). Thứ tự dòng là thứ tự nhận kết quả; request không gửi được nằm cuối file. Định danh duy nhất là `key`.
- `replay/prepared.jsonl`, `portability/prepared.jsonl`: THEO THỨ TỰ ĐẦU VÀO, mỗi dòng `{"idempotency_key", "meta"}`; `meta` là `meta` của `replay.build_requests` / `hotel_adapter.build_requests`
  (chỉ trường đã băm hoặc dẫn xuất, không UUID gốc, không body); số `Decimal` (`total_price`) ghi thành CHUỖI. Task dựng D4/D5 ghép `prepared` với `requests` theo `key`, không cần gửi lại.
- `portability/source.json`: URL, SHA-256, kích thước, số dòng, bảng lọc Q-08, seed, cỡ mẫu, giấy phép, tham số miền của instance PT.
- `summary.json`: `part`, `status`, `note`, `target`, `workers`, `mode`, `now`, `prepared`, `sent`, `not_sent`, `status_counts`, `decision_status_counts`, `attempts_total`, `retries`,
  `contract_error_requests`, `contract_errors_by_kind`, `contract_error_samples`, `latency_ms` (p50/p95/max), `duration_seconds`, `requests_per_second` (số đo thật, O-24 của 5.5), `phase_seconds` (thời gian từng pha, xem `Phases`), cộng trường riêng
  của từng phần (RP: `source_rows`, `rejected`, `window_bias_count`; PT: `sample_size`, `selected`, `total_rows`, `valid_count`, `rejected`).
"""
from __future__ import annotations

import json
import time
from collections import Counter
from contextlib import ExitStack
from dataclasses import dataclass, field
from datetime import date, datetime
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Any, Callable, Iterable, Iterator, TypeVar

from evaluator.clients import download
from evaluator.clients.http import http_get
from evaluator.results import ERROR, FAILED, PASSED, SKIPPED, StepResult, utc_now
from evaluator.run_context import RunContext
from evaluator.steps import dataset_sender, scale_step
from evidence import contract_check, hotel_adapter, replay
from evidence.anonymize import Anonymizer

STEP_NAME = "e"
REPLAY_FILE = "smashcourt_bookings.csv"
PART_REPLAY = "replay"
PART_PORTABILITY = "portability"
LABELS = {PART_REPLAY: "RP", PART_PORTABILITY: "PT"}
BUILD_CHUNK_SIZE = 500
MAX_ERROR_SAMPLES = 20
READY_TIMEOUT_SECONDS = 90

Item = TypeVar("Item")


@dataclass
class Services:
    """Mọi thứ bước (e) gọi ra ngoài; test thay bằng bản giả."""

    fetch: Callable[[str, str, Path], Path] = download.fetch
    iter_send: Callable[..., Iterator[tuple[dict[str, Any], dataset_sender.SendResult]]] = dataset_sender.iter_send
    ready_status: Callable[[str], int] = lambda url: http_get(url)[0]
    clock: Callable[[], datetime] = utc_now
    sleep: Callable[[float], None] = time.sleep
    monotonic: Callable[[], float] = time.monotonic
    anonymizer_factory: Callable[[], Anonymizer] = Anonymizer
    ready_timeout: float = READY_TIMEOUT_SECONDS


@dataclass
class PartOutcome:
    """Kết quả một phần (RP hoặc PT): trạng thái, ghi chú một dòng và tệp sinh ra."""

    part: str
    status: str
    note: str
    artifacts: list[str] = field(default_factory=list)


@dataclass
class Stats:
    """Số liệu gom dần khi nhận kết quả (không giữ body)."""

    prepared: int = 0
    sent: int = 0
    not_sent: int = 0
    attempts_total: int = 0
    window_bias: int = 0
    contract_error_requests: int = 0
    status_counts: Counter = field(default_factory=Counter)
    decision_status_counts: Counter = field(default_factory=Counter)
    error_kinds: Counter = field(default_factory=Counter)
    samples: list[dict[str, Any]] = field(default_factory=list)
    latencies: list[float] = field(default_factory=list)

    def record_errors(self, key: str, errors: list[str]) -> None:
        if not errors:
            return
        self.contract_error_requests += 1
        self.error_kinds.update(error.split(":", 1)[0] for error in errors)
        if len(self.samples) < MAX_ERROR_SAMPLES:
            self.samples.append({"key": key, "errors": errors[:5]})


class Phases:
    """
    Cộng dồn thời gian (giây, đồng hồ monotonic) theo pha của một phần để biết bước (e) chậm ở đâu: `load` (đọc CSV), `fetch` (tải/kiểm băm dataset), `select` (lọc + lấy mẫu),
    `wait_ready` (chờ `/health/ready`), `build` (dựng + kiểm schema request, chạy lười xen kẽ lúc gửi), `response_check` (kiểm contract response), `send` (toàn bộ vòng gửi, ĐÃ GỒM
    `build` và `response_check`).
    """

    def __init__(self, monotonic: Callable[[], float]) -> None:
        self._monotonic = monotonic
        self.seconds: dict[str, float] = {}

    def add(self, name: str, started: float) -> None:
        self.seconds[name] = self.seconds.get(name, 0.0) + (self._monotonic() - started)

    def timed(self, name: str, function: Callable[[], Any]) -> Any:
        started = self._monotonic()
        try:
            return function()
        finally:
            self.add(name, started)

    def iterate(self, name: str, items: Iterable[Any]) -> Iterator[Any]:
        """Bọc một iterator, chỉ tính thời gian lấy phần tử kế tiếp (không tính thời gian bên dùng xử lý phần tử)."""
        iterator = iter(items)
        while True:
            started = self._monotonic()
            try:
                item = next(iterator)
            except StopIteration:
                self.add(name, started)
                return
            self.add(name, started)
            yield item

    def to_dict(self) -> dict[str, float]:
        return {name: round(value, 2) for name, value in self.seconds.items()}


def run(context: RunContext, services: Services | None = None) -> StepResult:
    """Chạy RP rồi PT (độc lập) và tổng hợp trạng thái bước."""
    services = services or Services()
    started = utc_now()
    anonymizer = services.anonymizer_factory()
    outcomes = [_run_replay(context, services, anonymizer), _run_portability(context, services, anonymizer)]
    statuses = {outcome.status for outcome in outcomes}
    if FAILED in statuses or ERROR in statuses:  # đích không tới được cũng chỉ là failed: bước (f) vẫn phải chạy để báo INSUFFICIENT_DATA
        status = FAILED
    elif statuses == {SKIPPED}:
        status = SKIPPED
    else:
        status = PASSED
    notes = [outcome.note for outcome in outcomes]
    artifacts = [path for outcome in outcomes for path in outcome.artifacts]
    return StepResult(STEP_NAME, status, started, utc_now(), artifacts, notes)


# --- RP ---

def _run_replay(context: RunContext, services: Services, anonymizer: Anonymizer) -> PartOutcome:
    out_dir = context.raw_dir(PART_REPLAY)
    target = context.endpoints.proxy
    path = context.datasets_private / REPLAY_FILE
    if not path.is_file() or path.stat().st_size == 0:
        return _finish(out_dir, PART_REPLAY, SKIPPED, f"bỏ qua vì thiếu hoặc rỗng {REPLAY_FILE} trong {context.datasets_private} (xuất bằng tools/evidence/sql/export_smashcourt_bookings.sql)", target, context)
    phases = Phases(services.monotonic)
    try:
        bookings = phases.timed("load", lambda: replay.load_bookings(path))
    except replay.ReplayDataError as error:
        return _finish(out_dir, PART_REPLAY, FAILED, f"file {REPLAY_FILE} sai định dạng: {error}", target, context)
    if not bookings:
        return _finish(out_dir, PART_REPLAY, SKIPPED, f"bỏ qua vì {REPLAY_FILE} không có booking nào", target, context)
    rejected = replay.count_rejections(bookings)
    extra: dict[str, Any] = {"source_rows": len(bookings), "rejected": rejected}
    if not phases.timed("wait_ready", lambda: _wait_ready(services, target)):
        return _finish(out_dir, PART_REPLAY, ERROR, f"{target} chưa sẵn sàng sau {services.ready_timeout:.0f} s (/health/ready)", target, context, {**extra, "phase_seconds": phases.to_dict()})

    now = services.clock()
    requests = _lazy(bookings, lambda chunk: replay.build_requests(chunk, anonymizer, context.run_id, now))
    return _send_part(context, services, PART_REPLAY, out_dir, requests, target, now, extra, phases)


# --- PT ---

def _run_portability(context: RunContext, services: Services, anonymizer: Anonymizer) -> PartOutcome:
    out_dir = context.raw_dir(PART_PORTABILITY)
    target = context.endpoints.portability
    phases = Phases(services.monotonic)
    try:
        dataset = phases.timed("fetch", lambda: services.fetch(hotel_adapter.SOURCE_URL, hotel_adapter.SOURCE_SHA256, context.datasets_cache))
    except download.DatasetUnavailableError as error:
        return _finish(out_dir, PART_PORTABILITY, SKIPPED, f"bỏ qua vì không tải được dataset khách sạn ({error})", target, context)
    except download.DatasetIntegrityError as error:
        return _finish(out_dir, PART_PORTABILITY, FAILED, f"dataset khách sạn sai SHA-256: {error}", target, context)
    try:
        rows = phases.timed("load", lambda: hotel_adapter.load_rows(dataset))
        selection = phases.timed("select", lambda: hotel_adapter.select(rows, context.mode, sample_size=hotel_adapter.QUICK_SAMPLE_SIZE))
    except hotel_adapter.HotelDataError as error:
        return _finish(out_dir, PART_PORTABILITY, FAILED, f"dataset khách sạn sai định dạng: {error}", target, context)
    source = _source_info(dataset, selection)
    (out_dir / "source.json").write_text(json.dumps(source, ensure_ascii=False, indent=2), encoding="utf-8")
    extra: dict[str, Any] = {"sample_size": selection.sample_size, "selected": len(selection.rows), "total_rows": selection.total_rows, "valid_count": selection.valid_count, "rejected": selection.rejected}
    if not selection.rows:
        return _finish(out_dir, PART_PORTABILITY, SKIPPED, "bỏ qua vì không còn dòng hợp lệ nào sau khi lọc", target, context, extra)
    if not phases.timed("wait_ready", lambda: _wait_ready(services, target)):
        return _finish(out_dir, PART_PORTABILITY, ERROR, f"{target} chưa sẵn sàng sau {services.ready_timeout:.0f} s (/health/ready)", target, context, {**extra, "phase_seconds": phases.to_dict()})

    now = services.clock()
    requests = _lazy(selection.rows, lambda chunk: hotel_adapter.build_requests(chunk, anonymizer, context.run_id, now))
    return _send_part(context, services, PART_PORTABILITY, out_dir, requests, target, now, extra, phases)


def _source_info(dataset: Path, selection: hotel_adapter.Selection) -> dict[str, Any]:
    return {
        "url": hotel_adapter.SOURCE_URL, "sha256": hotel_adapter.SOURCE_SHA256, "file": dataset.name, "bytes": dataset.stat().st_size,
        "total_rows": selection.total_rows, "expected_rows": hotel_adapter.EXPECTED_ROW_COUNT, "matches_expected_rows": selection.total_rows == hotel_adapter.EXPECTED_ROW_COUNT,
        "valid_count": selection.valid_count, "rejected": selection.rejected, "mode": selection.mode, "sample_size": selection.sample_size, "seed": selection.seed,
        "selected": len(selection.rows), "license": hotel_adapter.LICENSE_LABEL, "citation": hotel_adapter.CITATION,
        "max_item_minutes": hotel_adapter.MAX_ITEM_MINUTES, "high_value_threshold": str(hotel_adapter.HIGH_VALUE_THRESHOLD),
        "branch_time_zone": hotel_adapter.BRANCH_TIME_ZONE, "currency": hotel_adapter.CURRENCY,
    }


# --- Dùng chung ---

def _wait_ready(services: Services, target: str) -> bool:
    return scale_step.wait_instances_ready([target], services.ready_status, [], clock=services.monotonic, sleep=services.sleep, timeout=services.ready_timeout)


def _lazy(items: list[Item], build: Callable[[list[Item]], list[dict[str, Any]]]) -> Iterator[dict[str, Any]]:
    """Dựng request theo từng lô để việc dựng + kiểm schema trải đều trong lúc gửi."""
    for start in range(0, len(items), BUILD_CHUNK_SIZE):
        yield from build(items[start:start + BUILD_CHUNK_SIZE])


def _send_part(context: RunContext, services: Services, part: str, out_dir: Path, requests: Iterable[dict[str, Any]], target: str, now: datetime, extra: dict[str, Any],
               phases: Phases) -> PartOutcome:
    stats = Stats()
    unsent: list[dict[str, Any]] = []
    started = services.monotonic()
    try:
        with ExitStack() as stack:
            prepared_file = stack.enter_context((out_dir / "prepared.jsonl").open("w", encoding="utf-8", newline="\n"))
            requests_file = stack.enter_context((out_dir / "requests.jsonl").open("w", encoding="utf-8", newline="\n"))

            def sendable() -> Iterator[dict[str, Any]]:
                for entry in phases.iterate("build", requests):
                    stats.prepared += 1
                    meta = entry["meta"]
                    stats.window_bias += 1 if meta.get("window_bias") else 0
                    prepared_file.write(_dump({"idempotency_key": entry["idempotency_key"], "meta": meta}) + "\n")
                    if meta["request_errors"]:
                        stats.not_sent += 1
                        stats.record_errors(entry["idempotency_key"], list(meta["request_errors"]))
                        unsent.append(_row(entry["idempotency_key"], None, 0, None, None, None, {}, list(meta["request_errors"]), None))
                    else:
                        yield entry

            for entry, result in services.iter_send(sendable(), target, context.endpoints.api_key, workers=context.workers):
                errors = phases.timed("response_check", lambda: contract_check.response_errors(entry["body"], result.status, result.body))
                stats.sent += 1
                stats.attempts_total += result.attempts
                stats.status_counts[str(result.status)] += 1
                stats.latencies.append(result.duration_ms)
                if result.status == contract_check.STATUS_OK and isinstance(result.body, dict):
                    stats.decision_status_counts[str(result.body.get("status", ""))] += 1
                stats.record_errors(result.key, errors)
                requests_file.write(_dump(_row(result.key, result.status, result.attempts, result.duration_ms, result.decision_id, result.correlation_id, _outcome(result), errors, result.error)) + "\n")
            for row in unsent:
                requests_file.write(_dump(row) + "\n")
    except dataset_sender.ServiceUnreachableError as error:
        phases.add("send", started)
        return _finish(out_dir, part, ERROR, f"{target} không nhận kết nối: {error}", target, context,
                       {**extra, "prepared": stats.prepared, "sent": stats.sent, "phase_seconds": phases.to_dict()})
    phases.add("send", started)
    elapsed = max(services.monotonic() - started, 1e-9)

    retries = stats.attempts_total - stats.sent
    summary = {
        **extra, "now": now.isoformat(), "prepared": stats.prepared, "sent": stats.sent, "not_sent": stats.not_sent, "status_counts": dict(sorted(stats.status_counts.items())),
        "decision_status_counts": dict(sorted(stats.decision_status_counts.items())), "attempts_total": stats.attempts_total, "retries": retries,
        "contract_error_requests": stats.contract_error_requests, "contract_errors_by_kind": dict(sorted(stats.error_kinds.items())), "contract_error_samples": stats.samples,
        "latency_ms": _latency(stats.latencies), "duration_seconds": round(elapsed, 2), "requests_per_second": round(stats.sent / elapsed, 1), "phase_seconds": phases.to_dict(),
    }
    if part == PART_REPLAY:
        summary["window_bias_count"] = stats.window_bias
    failed = stats.contract_error_requests > 0
    note = (
        f"gửi {stats.sent}/{stats.prepared} request tới {target} ({retries} lần thử lại), {stats.contract_error_requests} lỗi contract"
        f"{' (' + ', '.join(f'{kind} {count}' for kind, count in sorted(stats.error_kinds.items())) + ')' if failed else ''}, {elapsed:.0f} s ({stats.sent / elapsed:.0f} request/s)"
    )
    return _finish(out_dir, part, FAILED if failed else PASSED, note, target, context, summary)


def _finish(out_dir: Path, part: str, status: str, note: str, target: str, context: RunContext, summary: dict[str, Any] | None = None) -> PartOutcome:
    """Ghi `summary.json` của phần (kể cả khi không gửi gì) và trả kết quả phần; ghi chú được gắn nhãn RP/PT."""
    text = f"{LABELS[part]}: {note}"
    data = {"part": part, "status": status, "note": text, "target": target, "workers": context.workers, "mode": context.mode, **(summary or {})}
    path = out_dir / "summary.json"
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2, default=_json_default), encoding="utf-8")
    artifacts = [str(item) for item in sorted(out_dir.iterdir()) if item.is_file()]
    return PartOutcome(part, status, text, artifacts)


def _row(key: str, status: int | None, attempts: int, duration_ms: float | None, decision_id: str | None, correlation_id: str | None, outcome: dict[str, str], errors: list[str],
         error: str | None) -> dict[str, Any]:
    row = {"key": key, "status": status, "attempts": attempts, "duration_ms": duration_ms, "decision_id": decision_id, "correlation_id": correlation_id, "outcome": outcome or _outcome(None), "contract_errors": errors}
    if error:
        row["error"] = error
    return row


def _outcome(result: dataset_sender.SendResult | None) -> dict[str, str]:
    """Các cột phản hồi cho D4/D5 (chuỗi thập phân thường; rỗng khi không có response 200)."""
    outcome = {"status": "", "reason_code": "", "deposit_rate": "", "deposit_amount": ""}
    if result is None or result.status != contract_check.STATUS_OK or not isinstance(result.body, dict):
        return outcome
    deposit = result.body.get("deposit") if isinstance(result.body.get("deposit"), dict) else {}
    outcome.update({
        "status": str(result.body.get("status", "")), "reason_code": str(result.body.get("reasonCode", "")),
        "deposit_rate": _number_text(deposit.get("rate")), "deposit_amount": _number_text(deposit.get("amount")),
    })
    return outcome


def _number_text(value: Any) -> str:
    if value is None or isinstance(value, bool):
        return ""
    try:
        number = value if isinstance(value, Decimal) else Decimal(str(value))
    except InvalidOperation:
        return ""
    return replay.plain(number) if number.is_finite() else ""


def _latency(values: list[float]) -> dict[str, float | None]:
    """p50/p95 theo thứ hạng gần nhất (nearest-rank) và max của thời gian lần thử cuối (ms); None khi chưa có mẫu."""
    if not values:
        return {"p50": None, "p95": None, "max": None}
    ordered = sorted(values)

    def rank(percent: int) -> float:
        return ordered[max(0, -(-len(ordered) * percent // 100) - 1)]

    return {"p50": rank(50), "p95": rank(95), "max": ordered[-1]}


def _json_default(value: Any) -> Any:
    if isinstance(value, Decimal):
        return replay.plain(value)
    if isinstance(value, (datetime, date)):
        return value.isoformat()
    raise TypeError(f"không ghi được kiểu {type(value).__name__} ra JSON")


def _dump(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, default=_json_default)
