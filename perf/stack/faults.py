"""Tiêm lỗi giữa BE và TADSS dưới tải cho SP-09 (Epic 6C, task 2.26; plan 6C 2C, người dùng chốt 07/11: FI-11, FI-12, FI-14; FI-13 thêm ở việc B3).

    python perf/stack/faults.py inject <thư mục lượt> --kind stop-tadss|latency|reset|flag-off --start-after GIÂY --duration GIÂY [--latency-ms 1500]
    python perf/stack/faults.py check <thư mục lượt>
    python perf/stack/faults.py latency <thư mục lượt>
    (perf.sh run --fault KIND tự chạy `inject` ở nền ngay trước k6, `check` và `latency` sau k6, trước analyze.py)

- stop-tadss (FI-11): `docker stop perf-tadss` trong khoảng lỗi rồi bật lại, chờ healthy.
- latency (FI-12): Toxiproxy thêm `latency` ms vào chiều TADSS ⇒ BE (vượt TimeoutMs 800 ms của BE).
- reset (FI-14): Toxiproxy `reset_peer` (đóng kết nối bằng TCP RST) trên chiều BE ⇒ TADSS.
- flag-off (FI-13, plan 6C B3 = D4): tắt cờ `TadssConfig:EnableTadssDecision` bằng file cờ của BE (P6-56, như `deploy/tadss-flag.sh` của BE)
  rồi bật lại; không cần Toxiproxy. Thời gian có hiệu lực = dòng log Warning BE ghi khi thấy giá trị mới trừ lúc bắt đầu ghi file
  (gồm cả thời gian `docker exec`, nên nghiêng về chậm). `check` đòi: hiệu lực < 2 s (đề cương), mọi đơn tạo từ +2 s tới lúc bật lại
  cọc 100 % và không có quyết định TADSS, không chờ duyệt, và trong 60 s sau khi bật lại có đơn đi qua TADSS.
Khoảng lỗi ghi ở `faults.json` (epoch giây). `check` đọc DB của lượt (chỉ đọc) và ghi kết quả vào cùng file:
đơn online tạo trong khoảng lỗi (bỏ `MARGIN_SECONDS` ở hai đầu: lời gọi TADSS sát biên có thể rơi ra ngoài khoảng) phải cọc 100 %
(`FULL_PAYMENT_FALLBACK`), không đơn nào chờ duyệt; TADSS không có hai quyết định PRIMARY cho cùng một đơn.
`latency` (P6-57, bảng trước / sau của mạch ngắt) tính độ trễ của request đặt sân online **trong đúng khoảng lỗi** `[onAt, offAt)` và
ngoài khoảng (bỏ 60 s quanh hai biên) từ mẫu thô của k6, ghi `fault-latency.json` (file riêng: `faults.json` của lượt cũ đã có hash trong
gói bằng chứng). Số request ở đây khác số đơn của `check`: request bị từ chối (tranh khung giờ) vẫn tính độ trễ nhưng không tạo đơn.
"""
from __future__ import annotations

import argparse
import json
import math
import subprocess
import sys
import time
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

TOXIPROXY = "http://127.0.0.1:18474"
PROXY = "tadss"
TOXIC = "sp09-fault"
MARGIN_SECONDS = 2
KINDS = ("stop-tadss", "latency", "reset", "flag-off")
FAULT_FILE = "faults.json"
LATENCY_FILE = "fault-latency.json"
BOOKING_ENDPOINT = "POST /api/bookings/online"
OUTSIDE_MARGIN_SECONDS = 60
BE_CONTAINER = "perf-be"
FLAG_FILE = "/app/runtime-config/feature-flags.json"
FLAG_EFFECT_LIMIT_SECONDS = 2.0  # đề cương: tắt cờ có hiệu lực < 2 giây (FI-13, SC5)
FLAG_REENABLED_WINDOW_SECONDS = 60
FLAG_CONFIRM_TIMEOUT_SECONDS = 10
_FLAG_CHANGE = "Cờ EnableTadssDecision đổi khi đang chạy:"


def toxic_payload(kind: str, latency_ms: int) -> dict:
    """Toxic của Toxiproxy cho từng kiểu lỗi (stop-tadss không dùng proxy)."""
    if kind == "latency":
        return {"name": TOXIC, "type": "latency", "stream": "downstream", "toxicity": 1.0,
                "attributes": {"latency": latency_ms, "jitter": 0}}
    if kind == "reset":
        return {"name": TOXIC, "type": "reset_peer", "stream": "upstream", "toxicity": 1.0, "attributes": {"timeout": 0}}
    raise ValueError(f"không có toxic cho kiểu lỗi {kind}")


def check_window(on_at: float, off_at: float, margin: float = MARGIN_SECONDS) -> tuple[float, float]:
    """Khoảng xét đơn tạo trong lúc lỗi: bỏ margin ở hai đầu; khoảng rỗng thì báo lỗi."""
    start, end = on_at + margin, off_at - margin
    if end <= start:
        raise ValueError(f"khoảng lỗi {off_at - on_at:.0f} s quá ngắn so với biên {margin} s")
    return start, end


def fallback_sql(start: float, end: float) -> str:
    return ("SELECT count(*), count(*) FILTER (WHERE i.deposit_source IS DISTINCT FROM 'FULL_PAYMENT_FALLBACK'), "
            "count(*) FILTER (WHERE b.status = 'PENDING_APPROVAL') "
            "FROM public.bookings b JOIN public.invoices i ON i.booking_id = b.id "
            f"WHERE b.source = 'ONLINE' AND b.created_at >= to_timestamp({start}) AT TIME ZONE 'UTC' "
            f"AND b.created_at < to_timestamp({end}) AT TIME ZONE 'UTC'")


DUPLICATE_SQL = ("SELECT count(*) FROM (SELECT reservation_id FROM tadss.decision_executions WHERE role = 'PRIMARY' "
                 "GROUP BY reservation_id HAVING count(*) > 1) d")


def judge(bookings: int, not_fallback: int, awaiting: int, duplicates: int) -> dict:
    """PASS khi có đơn trong khoảng lỗi và mọi đơn cọc 100 %, không chờ duyệt, không quyết định trùng; không có đơn ⇒ INSUFFICIENT."""
    problems = []
    if not_fallback:
        problems.append(f"{not_fallback} đơn không cọc 100 % (FULL_PAYMENT_FALLBACK)")
    if awaiting:
        problems.append(f"{awaiting} đơn chờ duyệt dù TADSS lỗi")
    if duplicates:
        problems.append(f"{duplicates} đơn có hơn một quyết định PRIMARY ở TADSS")
    status = "FAIL" if problems else ("INSUFFICIENT" if bookings == 0 else "PASS")
    return {"bookings": bookings, "notFallback": not_fallback, "awaitingApproval": awaiting, "duplicateDecisions": duplicates,
            "problems": problems, "status": status}


def flag_write_command(enabled: bool) -> list[str]:
    """Ghi file tạm rồi mv (thay nguyên file một lần, BE không đọc phải file ghi dở), như tadss-flag.sh của bản deploy."""
    body = f'{{ "TadssConfig": {{ "EnableTadssDecision": {"true" if enabled else "false"} }} }}'
    return ["docker", "exec", "-u", "0", BE_CONTAINER, "sh", "-c",
            f"echo '{body}' > {FLAG_FILE}.tmp && chmod 644 {FLAG_FILE}.tmp && mv {FLAG_FILE}.tmp {FLAG_FILE}"]


def flag_change_at(log: str, enabled: bool) -> float | None:
    """Epoch giây của dòng log đầu tiên BE ghi khi cờ đổi sang `enabled` (`docker logs --timestamps`); không có ⇒ None."""
    target = "⇒ BẬT" if enabled else "⇒ TẮT"
    for line in log.splitlines():
        stamp, _, text = line.partition(" ")
        if _FLAG_CHANGE in text and target in text:
            return _parse_docker_time(stamp)
    return None


def _parse_docker_time(stamp: str) -> float:
    whole, _, fraction = stamp.rstrip("Z").partition(".")
    moment = datetime.strptime(whole, "%Y-%m-%dT%H:%M:%S").replace(tzinfo=timezone.utc)
    return moment.timestamp() + (float(f"0.{fraction}") if fraction else 0.0)


def flag_window(on_at: float, off_at: float) -> tuple[float, float]:
    """Đơn xét khi cờ tắt: từ giới hạn hiệu lực 2 s sau lúc ghi file tới trước lúc bật lại (bỏ biên ở cuối)."""
    return check_window(on_at + FLAG_EFFECT_LIMIT_SECONDS - MARGIN_SECONDS, off_at)


def flag_sql(start: float, end: float) -> str:
    return ("SELECT count(*), count(*) FILTER (WHERE i.deposit_source IS DISTINCT FROM 'FULL_PAYMENT_FALLBACK'), "
            "count(*) FILTER (WHERE b.status = 'PENDING_APPROVAL'), count(*) FILTER (WHERE b.tadss_decision_id IS NOT NULL) "
            "FROM public.bookings b JOIN public.invoices i ON i.booking_id = b.id "
            f"WHERE b.source = 'ONLINE' AND b.created_at >= to_timestamp({start}) AT TIME ZONE 'UTC' "
            f"AND b.created_at < to_timestamp({end}) AT TIME ZONE 'UTC'")


def judge_flag(effect_seconds: float | None, bookings: int, not_fallback: int, awaiting: int, via_tadss: int, duplicates: int,
               reenabled: int) -> dict:
    """FI-13: PASS khi BE thấy cờ mới < 2 s, mọi đơn lúc cờ tắt cọc 100 % không qua TADSS, không chờ duyệt, bật lại thì có đơn qua TADSS."""
    problems = []
    if effect_seconds is None:
        problems.append(f"BE không ghi log xác nhận tắt cờ trong {FLAG_CONFIRM_TIMEOUT_SECONDS} s")
    elif effect_seconds >= FLAG_EFFECT_LIMIT_SECONDS:
        problems.append(f"cờ có hiệu lực sau {effect_seconds} s (giới hạn {FLAG_EFFECT_LIMIT_SECONDS:g} s)")
    if via_tadss:
        problems.append(f"{via_tadss} đơn vẫn gọi TADSS sau giới hạn hiệu lực")
    if not_fallback:
        problems.append(f"{not_fallback} đơn không cọc 100 % (FULL_PAYMENT_FALLBACK)")
    if awaiting:
        problems.append(f"{awaiting} đơn chờ duyệt dù cờ tắt")
    if duplicates:
        problems.append(f"{duplicates} đơn có hơn một quyết định PRIMARY ở TADSS")
    if bookings and not reenabled:
        problems.append(f"bật lại cờ nhưng {FLAG_REENABLED_WINDOW_SECONDS:g} s sau đó không đơn nào qua TADSS")
    status = "FAIL" if problems else ("INSUFFICIENT" if bookings == 0 else "PASS")
    return {"effectSeconds": effect_seconds, "bookings": bookings, "notFallback": not_fallback, "awaitingApproval": awaiting,
            "viaTadss": via_tadss, "duplicateDecisions": duplicates, "reenabledViaTadss": reenabled, "problems": problems, "status": status}


def _set_flag(enabled: bool, since: float) -> float | None:
    """Ghi file cờ rồi đợi BE ghi log xác nhận; trả thời điểm BE thấy giá trị mới (None nếu quá hạn)."""
    subprocess.run(flag_write_command(enabled), check=True, capture_output=True)
    deadline = time.time() + FLAG_CONFIRM_TIMEOUT_SECONDS
    while time.time() < deadline:
        log = subprocess.run(["docker", "logs", "--timestamps", "--since", str(int(since) - 1), BE_CONTAINER],
                             capture_output=True, text=True, encoding="utf-8", errors="replace")
        changed = flag_change_at(log.stdout + log.stderr, enabled)
        if changed is not None and changed >= since - 1:
            return changed
        time.sleep(0.2)
    return None


def _psql(container: str, user: str, db: str, sql: str) -> list[int]:
    out = subprocess.run(["docker", "exec", container, "psql", "-U", user, "-d", db, "-tA", "-F", ",", "-c", sql],
                         check=True, capture_output=True, text=True).stdout.strip()
    return [int(v) for v in out.split(",")]


def _toxiproxy(method: str, path: str, body: dict | None = None) -> None:
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(f"{TOXIPROXY}{path}", data=data, method=method, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=10):
        pass


def _wait_healthy(container: str, limit: int = 120) -> float:
    for _ in range(limit):
        status = subprocess.run(["docker", "inspect", "--format", "{{.State.Health.Status}}", container],
                                capture_output=True, text=True).stdout.strip()
        if status == "healthy":
            return time.time()
        time.sleep(1)
    raise RuntimeError(f"{container} không healthy sau {limit} giây")


def inject(run_dir: Path, kind: str, start_after: float, duration: float, latency_ms: int) -> None:
    record = {"kind": kind, "latencyMs": latency_ms if kind == "latency" else None, "plannedStartAfter": start_after,
              "plannedDuration": duration, "launchedAt": time.time()}
    (run_dir / FAULT_FILE).write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
    time.sleep(start_after)
    record["onAt"] = time.time()
    if kind == "flag-off":
        try:
            record["flagOffConfirmedAt"] = _set_flag(False, record["onAt"])
            time.sleep(max(0.0, record["onAt"] + duration - time.time()))
        finally:   # lượt dừng giữa chừng vẫn phải bật lại cờ: perf-be giữ file cờ tới khi container bị tạo lại
            record["offAt"] = time.time()
            record["flagOnConfirmedAt"] = _set_flag(True, record["offAt"])
            (run_dir / FAULT_FILE).write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
        return
    if kind == "stop-tadss":
        subprocess.run(["docker", "stop", "perf-tadss"], check=True, capture_output=True)
    else:
        _toxiproxy("POST", f"/proxies/{PROXY}/toxics", toxic_payload(kind, latency_ms))
    time.sleep(duration)
    record["offAt"] = time.time()
    if kind == "stop-tadss":
        subprocess.run(["docker", "start", "perf-tadss"], check=True, capture_output=True)
        record["tadssHealthyAt"] = _wait_healthy("perf-tadss")
    else:
        _toxiproxy("DELETE", f"/proxies/{PROXY}/toxics/{TOXIC}")
    (run_dir / FAULT_FILE).write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")


def check(run_dir: Path) -> dict:
    record = json.loads((run_dir / FAULT_FILE).read_text(encoding="utf-8"))
    if "offAt" not in record:
        result = {"status": "INCOMPLETE", "problems": ["lỗi chưa được gỡ (inject dừng giữa chừng)"]}
    elif record["kind"] == "flag-off":
        start, end = flag_window(record["onAt"], record["offAt"])
        bookings, not_fallback, awaiting, via_tadss = _psql("perf-postgres", "smashcourt", "smashcourt", flag_sql(start, end))
        after = record["offAt"] + FLAG_EFFECT_LIMIT_SECONDS
        reenabled = _psql("perf-postgres", "smashcourt", "smashcourt", flag_sql(after, after + FLAG_REENABLED_WINDOW_SECONDS))[3]
        (duplicates,) = _psql("perf-tadss-postgres", "tadss", "tadss", DUPLICATE_SQL)
        confirmed = record.get("flagOffConfirmedAt")
        effect = None if confirmed is None else round(max(0.0, confirmed - record["onAt"]), 2)
        result = {"checkWindow": [start, end], **judge_flag(effect, bookings, not_fallback, awaiting, via_tadss, duplicates, reenabled)}
    else:
        start, end = check_window(record["onAt"], record["offAt"])
        bookings, not_fallback, awaiting = _psql("perf-postgres", "smashcourt", "smashcourt", fallback_sql(start, end))
        (duplicates,) = _psql("perf-tadss-postgres", "tadss", "tadss", DUPLICATE_SQL)
        result = {"checkWindow": [start, end], **judge(bookings, not_fallback, awaiting, duplicates)}
    record["check"] = result
    (run_dir / FAULT_FILE).write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
    return result


def _percentile(values: list[float], p: float) -> float | None:
    # Cùng cách nội suy với verdict.percentile (k6); viết lại để hàm thuần không phải nạp mẫu thô
    if not values:
        return None
    ordered = sorted(values)
    position = p * (len(ordered) - 1)
    low, high = math.floor(position), math.ceil(position)
    return ordered[low] + (ordered[high] - ordered[low]) * (position - low)


def _summary(values: list[float]) -> dict:
    def rounded(value: float | None) -> float | None:
        return None if value is None else round(value, 1)
    return {"requests": len(values), "p50": rounded(_percentile(values, 0.5)), "p95": rounded(_percentile(values, 0.95)),
            "max": rounded(max(values) if values else None)}


def booking_latency(samples: list[tuple[float, str, float]], on_at: float, off_at: float) -> dict:
    """Độ trễ (ms) của `POST /api/bookings/online` trong khoảng lỗi và ngoài khoảng; `samples` là (thời điểm, endpoint, độ trễ)."""
    inside = [v for t, name, v in samples if name == BOOKING_ENDPOINT and on_at <= t < off_at]
    outside = [v for t, name, v in samples if name == BOOKING_ENDPOINT
               and not on_at - OUTSIDE_MARGIN_SECONDS <= t < off_at + OUTSIDE_MARGIN_SECONDS]
    return {"endpoint": BOOKING_ENDPOINT, "window": [on_at, off_at], "outsideMarginSeconds": OUTSIDE_MARGIN_SECONDS,
            "insideFault": _summary(inside), "outsideFault": _summary(outside)}


def latency(run_dir: Path) -> dict:
    import verdict  # mẫu thô của k6 (cùng thư mục): chỉ nạp khi cần
    record = json.loads((run_dir / FAULT_FILE).read_text(encoding="utf-8"))
    if "offAt" not in record:
        raise ValueError("lỗi chưa được gỡ (inject dừng giữa chừng): không có khoảng lỗi để tính")
    raw = verdict.read_raw(run_dir / "raw.csv.gz").steady()
    result = booking_latency([(s.t, s.name, s.value) for s in raw.durations], record["onAt"], record["offAt"])
    (run_dir / LATENCY_FILE).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    return result


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    p_inject = sub.add_parser("inject")
    p_inject.add_argument("run_dir", type=Path)
    p_inject.add_argument("--kind", choices=KINDS, required=True)
    p_inject.add_argument("--start-after", type=float, required=True)
    p_inject.add_argument("--duration", type=float, required=True)
    p_inject.add_argument("--latency-ms", type=int, default=1500)
    p_check = sub.add_parser("check")
    p_check.add_argument("run_dir", type=Path)
    p_latency = sub.add_parser("latency")
    p_latency.add_argument("run_dir", type=Path)
    args = parser.parse_args(argv)
    if args.command == "inject":
        inject(args.run_dir, args.kind, args.start_after, args.duration, args.latency_ms)
        return 0
    if args.command == "latency":
        result = latency(args.run_dir)
        print(f"faults latency: trong lỗi {json.dumps(result['insideFault'])}, ngoài lỗi {json.dumps(result['outsideFault'])}")
        return 0
    result = check(args.run_dir)
    print(f"faults: {result['status']} {json.dumps({k: v for k, v in result.items() if k != 'status'}, ensure_ascii=False)}")
    return 0 if result["status"] in ("PASS", "INSUFFICIENT") else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
