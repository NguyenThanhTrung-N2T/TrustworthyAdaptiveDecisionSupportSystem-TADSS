# Plan 6C 2C (03/11): lấy mẫu PostgreSQL khi chẩn đoán đợt nghẽn; pg_locks chỉ đọc ở chế độ locks và chỉ khi có dấu hiệu nghẽn
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import pgsampler  # noqa: E402


def _sample(active: int = 0, lock_manager: int = 0) -> dict:
    sessions = [{"state": "active", "wait_event": None} for _ in range(active)]
    sessions += [{"state": "active", "wait_event": "LockManager"} for _ in range(lock_manager)]
    return {"type": "activity", "sessions": sessions}


def test_activity_mode_never_reads_pg_locks_even_when_congested():
    # Lượt chính thức (SP-01, E1–E3): đọc pg_locks là khóa chính bộ quản lý khóa đang tranh chấp
    assert not pgsampler.should_read_locks("activity", _sample(active=50, lock_manager=40), None, 100.0)


def test_locks_mode_reads_only_on_congestion_and_at_most_every_two_seconds():
    assert not pgsampler.should_read_locks("locks", _sample(active=3), None, 100.0)
    assert pgsampler.should_read_locks("locks", _sample(active=10), None, 100.0)
    assert pgsampler.should_read_locks("locks", _sample(lock_manager=1), None, 100.0)
    assert not pgsampler.should_read_locks("locks", _sample(active=20), 99.0, 100.0)
    assert pgsampler.should_read_locks("locks", _sample(active=20), 98.0, 100.0)


def test_psql_runs_on_tool_cpus_inside_the_perf_network_and_keeps_one_connection():
    command = pgsampler.psql_command("5-7")
    assert command[:4] == ["docker", "run", "--rm", "-i"] and "--cpuset-cpus" in command and "5-7" in command
    assert ["--network", "smashcourt-test"] == command[command.index("--network"):command.index("--network") + 2]
    assert "perf-postgres" in command and "-At" in command


def test_queries_target_the_smashcourt_database_and_skip_idle_sessions():
    assert "datname = 'smashcourt'" in pgsampler.ACTIVITY_QUERY and "IS DISTINCT FROM 'idle'" in pgsampler.ACTIVITY_QUERY
    assert "pg_locks" not in pgsampler.ACTIVITY_QUERY
    assert "NOT fastpath" in pgsampler.LOCKS_QUERY
    assert "to_timestamp(1.000) AND to_timestamp(2.000)" in pgsampler.hangfire_query(1, 2)


def test_every_query_returns_jsonb_so_each_sample_stays_on_one_line():
    # json_agg chèn xuống dòng giữa các phần tử (lỗi thấy khi chạy thử 03/11); jsonb luôn in một dòng
    for sql in (pgsampler.ACTIVITY_QUERY, pgsampler.LOCKS_QUERY, pgsampler.hangfire_query(1, 2)):
        assert "jsonb_build_object(" in sql and "json_agg(" not in sql.replace("jsonb_agg(", "") and "'[]'::jsonb" in sql
