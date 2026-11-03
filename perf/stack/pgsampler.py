"""Lấy mẫu PostgreSQL của môi trường perf trong một lượt k6 (Epic 6C, plan 6C 2C: chẩn đoán đợt nghẽn LWLock LockManager thấy ở SP-08).

    python perf/stack/pgsampler.py <thư mục lượt> --mode activity|locks [--cpuset 5-7] [--interval 1]
    (perf.sh run --pg-sample activity|locks tự chạy trước k6 và dừng sau k6 bằng file <lượt>/.stop-pgsampler)

- activity (mọi lượt có lấy mẫu, kể cả SP-01 trước/sau): mỗi `interval` giây đọc pg_stat_activity của database smashcourt (phiên không rảnh:
  trạng thái, sự kiện chờ, lúc bắt đầu transaction/câu lệnh, 200 ký tự đầu câu lệnh; số phiên rảnh). pg_stat_activity đọc bảng trạng thái
  phiên, KHÔNG khóa bộ quản lý khóa.
- locks (CHỈ lượt chẩn đoán): thêm pg_locks theo từng phiên (tổng, ngoài fast-path, đang chờ, tên quan hệ) nhưng chỉ khi có dấu hiệu nghẽn
  (≥ `TRIGGER_ACTIVE` phiên đang chạy hoặc có phiên chờ LockManager) và cách lần đọc trước ≥ `LOCKS_MIN_GAP` giây: đọc pg_locks phải khóa
  mọi phân vùng của bộ quản lý khóa, tức chính chỗ đang tranh chấp (người dùng chốt 03/11: không đọc pg_locks trong lượt chính thức).
- Cuối lượt: lịch chạy job Hangfire trong khoảng lấy mẫu (`hangfire-jobs.json`) để đối chiếu đợt nghẽn với job nền.
psql chạy trong container postgres:17.6-alpine (image có sẵn của stack) trên CPU của công cụ đo, giữ MỘT kết nối suốt lượt.
Kết quả: `pg-samples.jsonl.gz` (mỗi dòng một mẫu JSON) trong thư mục lượt.
"""
from __future__ import annotations

import argparse
import gzip
import json
import shutil
import subprocess
import sys
import time
from pathlib import Path

IMAGE = "postgres:17.6-alpine"
NETWORK = "smashcourt-test"
TRIGGER_ACTIVE = 10
LOCKS_MIN_GAP = 2.0
STOP_FILE = ".stop-pgsampler"
READY_FILE = ".pgsampler-ready"

ACTIVITY_QUERY = (
    "SELECT jsonb_build_object('type', 'activity', 't', extract(epoch FROM clock_timestamp()), "
    "'idle', (SELECT count(*) FROM pg_stat_activity WHERE datname = 'smashcourt' AND state = 'idle'), "
    "'sessions', coalesce((SELECT jsonb_agg(a) FROM (SELECT pid, state, wait_event_type, wait_event, "
    "extract(epoch FROM xact_start) AS xact_start, extract(epoch FROM query_start) AS query_start, left(query, 200) AS query "
    "FROM pg_stat_activity WHERE datname = 'smashcourt' AND pid <> pg_backend_pid() AND state IS DISTINCT FROM 'idle') a), '[]'::jsonb));"
)
LOCKS_QUERY = (
    "SELECT jsonb_build_object('type', 'locks', 't', extract(epoch FROM clock_timestamp()), "
    "'sessions', coalesce((SELECT jsonb_agg(l) FROM (SELECT pid, count(*) AS total, count(*) FILTER (WHERE NOT fastpath) AS slow, "
    "count(*) FILTER (WHERE NOT granted) AS waiting, count(*) FILTER (WHERE locktype = 'relation') AS relations, "
    "array_agg(DISTINCT relation::regclass::text) FILTER (WHERE locktype = 'relation' AND NOT fastpath) AS slow_relations "
    "FROM pg_locks WHERE database = (SELECT oid FROM pg_database WHERE datname = 'smashcourt') AND pid <> pg_backend_pid() "
    "GROUP BY pid) l), '[]'::jsonb));"
)


def hangfire_query(start: float, end: float) -> str:
    return (
        "SELECT jsonb_build_object('type', 'hangfire', 'states', coalesce((SELECT jsonb_agg(x ORDER BY x.t) FROM (SELECT s.jobid, "
        "j.invocationdata->>'Type' AS job_type, j.invocationdata->>'Method' AS method, s.name, extract(epoch FROM s.createdat) AS t "
        f"FROM hangfire.state s JOIN hangfire.job j ON j.id = s.jobid WHERE s.createdat BETWEEN to_timestamp({start:.3f}) "
        f"AND to_timestamp({end:.3f}) AND s.name IN ('Processing', 'Succeeded', 'Failed')) x), '[]'::jsonb));"
    )


def psql_command(cpuset: str | None) -> list[str]:
    """psql trong container riêng trên CPU của công cụ đo (không lấy CPU của hệ thống được đo); mật khẩu là hằng không bí mật của stack perf."""
    command = ["docker", "run", "--rm", "-i", "--network", NETWORK, "-e", "PGPASSWORD=smashcourt_perf"]
    if cpuset:
        command += ["--cpuset-cpus", cpuset]
    return command + [IMAGE, "psql", "-h", "perf-postgres", "-U", "smashcourt", "-d", "smashcourt", "-At", "-q", "-X",
                      "-v", "ON_ERROR_STOP=0"]


def congested(sample: dict) -> bool:
    sessions = sample.get("sessions") or []
    active = sum(1 for s in sessions if s.get("state") == "active")
    return active >= TRIGGER_ACTIVE or any(s.get("wait_event") == "LockManager" for s in sessions)


def should_read_locks(mode: str, sample: dict, last_locks: float | None, now: float) -> bool:
    return mode == "locks" and congested(sample) and (last_locks is None or now - last_locks >= LOCKS_MIN_GAP)


class Psql:
    """Một phiên psql giữ suốt lượt: mỗi câu SELECT trả đúng một dòng JSON."""

    def __init__(self, command: list[str]):
        self._process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                                         encoding="utf-8", bufsize=1)

    def query(self, sql: str) -> dict:
        assert self._process.stdin is not None and self._process.stdout is not None
        self._process.stdin.write(sql + "\n")
        self._process.stdin.flush()
        line = self._process.stdout.readline()
        if not line:
            raise RuntimeError("psql đã thoát: " + (self._process.stderr.read() if self._process.stderr else ""))
        return json.loads(line)

    def close(self) -> None:
        if self._process.stdin:
            self._process.stdin.close()
        self._process.wait(timeout=30)


def run(run_dir: Path, mode: str, cpuset: str | None, interval: float) -> int:
    raw_path = run_dir / "pg-samples.jsonl"
    stop = run_dir / STOP_FILE
    psql = Psql(psql_command(cpuset))
    started, last_locks, counts = time.time(), None, {"activity": 0, "locks": 0}
    with raw_path.open("w", encoding="utf-8") as out:
        out.write(json.dumps({"type": "meta", "mode": mode, "interval": interval, "triggerActive": TRIGGER_ACTIVE,
                              "locksMinGap": LOCKS_MIN_GAP, "started": started}) + "\n")
        (run_dir / READY_FILE).write_text(str(started), encoding="utf-8")
        while not stop.exists():
            tick = time.time()
            sample = psql.query(ACTIVITY_QUERY)
            out.write(json.dumps(sample, ensure_ascii=False) + "\n")
            counts["activity"] += 1
            if should_read_locks(mode, sample, last_locks, tick):
                out.write(json.dumps(psql.query(LOCKS_QUERY), ensure_ascii=False) + "\n")
                last_locks = tick
                counts["locks"] += 1
            out.flush()
            time.sleep(max(0.0, interval - (time.time() - tick)))
        jobs = psql.query(hangfire_query(started, time.time()))
    psql.close()
    (run_dir / "hangfire-jobs.json").write_text(json.dumps(jobs["states"], ensure_ascii=False, indent=1), encoding="utf-8")
    with raw_path.open("rb") as source, gzip.open(run_dir / "pg-samples.jsonl.gz", "wb") as target:
        shutil.copyfileobj(source, target)
    raw_path.unlink()
    for name in (STOP_FILE, READY_FILE):
        (run_dir / name).unlink(missing_ok=True)
    print(f"pgsampler: {counts['activity']} mẫu activity, {counts['locks']} mẫu locks, {len(jobs['states'])} trạng thái job Hangfire")
    return 0


def main(argv: list[str]) -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("run_dir", type=Path)
    parser.add_argument("--mode", choices=["activity", "locks"], required=True)
    parser.add_argument("--cpuset")
    parser.add_argument("--interval", type=float, default=1.0)
    args = parser.parse_args(argv[1:])
    return run(args.run_dir, args.mode, args.cpuset, args.interval)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
