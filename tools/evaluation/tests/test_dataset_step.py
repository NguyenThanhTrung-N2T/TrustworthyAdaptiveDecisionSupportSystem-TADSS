"""Bước (e) dữ liệu thực tế (RP-01, PT-01): RP rồi PT độc lập, trạng thái bước, tệp đầu ra, không lộ định danh gốc/body request. Không mạng, không Docker:
tải dataset và bộ gửi được thay bằng bản giả (bộ gửi thật đã có test riêng với server `http.server`)."""
import json
import re
import shutil
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Iterable, Iterator

import pytest

from evaluator.clients import download
from evaluator import __main__ as cli
from evaluator.results import ERROR, FAILED, PASSED, SKIPPED, StepResult, exit_code
from evaluator.run_context import build_context
from evaluator.steps import dataset_sender, dataset_step as step
from evidence import contract_check, hotel_adapter
from evidence.anonymize import Anonymizer
from evidence.contract_check import expected_amount

NOW = datetime(2026, 10, 9, 1, 0, tzinfo=timezone.utc)
FIXTURES = Path(__file__).resolve().parents[2] / "evidence" / "tests" / "fixtures"
SMASHCOURT_CSV = FIXTURES / "smashcourt_expected_export.csv"
HOTEL_CSV = FIXTURES / "hotel_sample.csv"
UUID_PATTERN = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")


def decision_result(request: dict[str, Any], status: str = "APPROVED", rate: str = "0.5") -> dict[str, Any]:
    """Response 200 hợp lệ theo decision-result.schema.json + §3.3 cho một request."""
    deposit = None
    if status == "APPROVED":
        deposit = {"rate": contract_check.parse_json(rate), "amount": expected_amount(request["totalPrice"], rate, request["currency"]), "currency": request["currency"]}
    return {
        "decisionId": "0f9c7e5a-0000-4000-8000-" + f"{abs(hash(request['reservationId'])) % 10**12:012d}", "reservationId": request["reservationId"], "status": status, "deposit": deposit,
        "strategyName": "DefaultDecisionStrategy", "strategyVersion": "2.0.0", "executionType": "AUTOMATED", "fallbackUsed": False, "reasonCode": "TIER_DEPOSIT", "reasons": [],
        "recommendations": [], "approvalExpiresAt": None, "evaluatedAt": "2026-11-18T02:15:00.041Z",
    }


class FakeSend:
    """Thay `dataset_sender.iter_send`: ghi lại mọi lần gọi, trả response hợp lệ (hoặc lỗi theo `overrides[chỉ số] -> (status, body)`)."""

    def __init__(self, overrides: dict[int, tuple[int, Any]] | None = None, unreachable: bool = False, on_call: int | None = None) -> None:
        self.overrides = overrides or {}
        self.on_call = on_call  # None: áp cho mọi lần gọi; 0 = lần gọi của RP, 1 = lần gọi của PT
        self.unreachable = unreachable
        self.calls: list[dict[str, Any]] = []

    def __call__(self, prepared: Iterable[dict[str, Any]], base_url: str, api_key: str, workers: int = 8, **kwargs: Any) -> Iterator[tuple[dict[str, Any], dataset_sender.SendResult]]:
        call = {"base_url": base_url, "api_key": api_key, "workers": workers, "keys": []}
        self.calls.append(call)
        if self.unreachable:
            raise dataset_sender.ServiceUnreachableError("không kết nối được")
        for index, entry in enumerate(prepared):
            call["keys"].append(entry["idempotency_key"])
            applies = self.on_call is None or self.on_call == len(self.calls) - 1
            status, body = self.overrides.get(index, (200, decision_result(entry["body"]))) if applies else (200, decision_result(entry["body"]))
            yield entry, dataset_sender.SendResult(
                key=entry["idempotency_key"], status=status, body=body, attempts=2 if index == 0 else 1, duration_ms=5.0 + index,
                decision_id=body.get("decisionId") if isinstance(body, dict) else None, correlation_id=f"corr-{index}", error=None,
            )


class Env:
    """Dựng context + thư mục dữ liệu + services giả cho một test."""

    def __init__(self, tmp_path: Path, mode: str = "quick", with_smashcourt: bool = True, fetch: Any = None, send: FakeSend | None = None, ready: int = 200) -> None:
        self.private = tmp_path / "private"
        self.cache = tmp_path / "cache"
        self.private.mkdir()
        if with_smashcourt:
            shutil.copy(SMASHCOURT_CSV, self.private / "smashcourt_bookings.csv")
        env = {
            "EVAL_EVIDENCE": str(tmp_path / "ev"), "EVAL_MODE": mode, "EVAL_STEPS": "e", "EVAL_API_KEY": "khoa", "EVAL_DATASETS_PRIVATE": str(self.private),
            "EVAL_DATASETS_CACHE": str(self.cache), "EVAL_WORKERS": "3", "EVAL_PROXY_URL": "http://proxy:8080", "EVAL_PT_URL": "http://pt:8080",
        }
        self.context = build_context(env, NOW)
        self.send = send or FakeSend()
        self.fetch_calls: list[tuple] = []
        self.anonymizers = 0
        self.clock_reads: list[datetime] = []
        self.probed: list[str] = []
        self.ready = ready
        self._fetch = fetch if fetch is not None else (lambda url, sha, cache: HOTEL_CSV)
        self.services = step.Services(
            fetch=self._record_fetch, iter_send=self.send, ready_status=self._probe, clock=self._clock, sleep=lambda seconds: None, anonymizer_factory=self._anonymizer,
        )

    def _record_fetch(self, url: str, sha256: str, cache: Path) -> Path:
        self.fetch_calls.append((url, sha256, cache))
        return self._fetch(url, sha256, cache)

    def _probe(self, url: str) -> int:
        self.probed.append(url)
        return self.ready

    def _clock(self) -> datetime:
        moment = NOW + timedelta(days=100 * (len(self.clock_reads) + 1))
        self.clock_reads.append(moment)
        return moment

    def _anonymizer(self) -> Anonymizer:
        self.anonymizers += 1
        return Anonymizer(b"khoa-thu-nghiem")

    def run(self):
        return step.run(self.context, self.services)

    def out(self, *parts: str) -> Path:
        return self.context.evidence_dir.joinpath("raw", *parts)

    def lines(self, *parts: str) -> list[dict[str, Any]]:
        return [json.loads(line) for line in self.out(*parts).read_text(encoding="utf-8").splitlines() if line.strip()]

    def summary(self, part: str) -> dict[str, Any]:
        return json.loads(self.out(part, "summary.json").read_text(encoding="utf-8"))


def test_step_name_is_e() -> None:
    assert step.STEP_NAME == "e"


def test_dataset_step_statuses_Theory_full_data_passes(tmp_path: Path) -> None:
    env = Env(tmp_path)

    result = env.run()

    assert result.status == PASSED, result.notes
    replay, portability = env.summary("replay"), env.summary("portability")
    assert replay["status"] == PASSED and portability["status"] == PASSED
    assert replay["sent"] == replay["prepared"] > 0 and portability["sent"] == portability["prepared"] > 0
    assert replay["contract_error_requests"] == 0 and portability["contract_error_requests"] == 0
    for relative in ("replay/requests.jsonl", "replay/prepared.jsonl", "replay/summary.json", "portability/requests.jsonl", "portability/prepared.jsonl", "portability/summary.json", "portability/source.json"):
        assert env.out(*relative.split("/")).exists(), relative
    assert any("RP" in note for note in result.notes) and any("PT" in note for note in result.notes)


def test_dataset_step_sends_to_the_right_targets_with_configured_workers(tmp_path: Path) -> None:
    env = Env(tmp_path)

    env.run()

    assert [(call["base_url"], call["api_key"], call["workers"]) for call in env.send.calls] == [("http://proxy:8080", "khoa", 3), ("http://pt:8080", "khoa", 3)]
    assert env.probed and set(env.probed) == {"http://proxy:8080/health/ready", "http://pt:8080/health/ready"}, "chờ /health/ready của từng đích"
    assert env.summary("replay")["target"] == "http://proxy:8080" and env.summary("portability")["target"] == "http://pt:8080"


def test_dataset_step_statuses_Theory_missing_smashcourt_csv_skips_replay(tmp_path: Path) -> None:
    env = Env(tmp_path, with_smashcourt=False)

    result = env.run()

    assert result.status == PASSED
    assert env.summary("replay")["status"] == SKIPPED and "smashcourt_bookings.csv" in env.summary("replay")["note"]
    assert not env.out("replay", "requests.jsonl").exists()
    assert env.summary("portability")["status"] == PASSED
    assert any("RP" in note and "bỏ qua" in note for note in result.notes)
    assert [call["base_url"] for call in env.send.calls] == ["http://pt:8080"]


def test_dataset_step_tolerates_empty_private_file_and_absent_directory(tmp_path: Path) -> None:
    empty_root = tmp_path / "empty"
    empty_root.mkdir()
    empty = Env(empty_root, with_smashcourt=False)
    (empty.private / "smashcourt_bookings.csv").write_bytes(b"")

    assert empty.run().status == PASSED and empty.summary("replay")["status"] == SKIPPED

    absent_root = tmp_path / "absent"
    absent_root.mkdir()
    absent = Env(absent_root, with_smashcourt=False)
    shutil.rmtree(absent.private)

    assert absent.run().status == PASSED and absent.summary("replay")["status"] == SKIPPED


def test_dataset_step_statuses_Theory_network_failure_skips_portability(tmp_path: Path) -> None:
    def unavailable(url: str, sha: str, cache: Path) -> Path:
        raise download.DatasetUnavailableError("mất mạng")

    env = Env(tmp_path, fetch=unavailable)

    result = env.run()

    assert result.status == PASSED
    assert env.summary("replay")["status"] == PASSED and env.summary("portability")["status"] == SKIPPED
    assert "mất mạng" in env.summary("portability")["note"]
    assert any("PT" in note and "bỏ qua" in note for note in result.notes)


def test_dataset_step_statuses_Theory_both_parts_without_data_is_skipped(tmp_path: Path) -> None:
    def unavailable(url: str, sha: str, cache: Path) -> Path:
        raise download.DatasetUnavailableError("mất mạng")

    result = Env(tmp_path, with_smashcourt=False, fetch=unavailable).run()

    assert result.status == SKIPPED


def test_dataset_step_statuses_Theory_wrong_sha_fails(tmp_path: Path) -> None:
    def tampered(url: str, sha: str, cache: Path) -> Path:
        raise download.DatasetIntegrityError("SHA-256 sai")

    env = Env(tmp_path, fetch=tampered)

    result = env.run()

    assert result.status == FAILED
    assert env.summary("portability")["status"] == FAILED and "SHA-256" in env.summary("portability")["note"]
    assert env.summary("replay")["status"] == PASSED, "RP độc lập với PT"
    assert any("SHA-256" in note for note in result.notes)


def test_dataset_step_statuses_Theory_response_failing_schema_fails(tmp_path: Path) -> None:
    broken = {"decisionId": "x", "status": "APPROVED"}  # thiếu nhiều trường bắt buộc, không có deposit
    env = Env(tmp_path, send=FakeSend({1: (200, broken)}, on_call=0))

    result = env.run()

    assert result.status == FAILED
    replay = env.summary("replay")
    assert replay["contract_error_requests"] == 1 and replay["status"] == FAILED
    bad = [row for row in env.lines("replay", "requests.jsonl") if row["contract_errors"]]
    assert len(bad) == 1 and any(error.startswith("SCHEMA") for error in bad[0]["contract_errors"])
    assert replay["contract_errors_by_kind"].get("SCHEMA", 0) >= 1
    assert env.summary("portability")["status"] == PASSED


def test_dataset_step_counts_non_200_as_contract_error(tmp_path: Path) -> None:
    env = Env(tmp_path, send=FakeSend({0: (400, {"code": "DOMAIN_INVARIANT_VIOLATION"})}, on_call=1))

    result = env.run()

    assert result.status == FAILED
    row = env.lines("portability", "requests.jsonl")[0]
    assert row["status"] == 400 and row["contract_errors"] == ["STATUS: HTTP 400, kỳ vọng 200"]
    assert env.summary("portability")["status_counts"]["400"] == 1


def test_dataset_step_request_schema_errors_count_and_are_not_sent(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(contract_check, "request_errors", lambda body: ["SCHEMA: adapter dựng sai"])
    env = Env(tmp_path)

    result = env.run()

    assert result.status == FAILED
    portability = env.summary("portability")
    assert portability["not_sent"] == portability["prepared"] and portability["sent"] == 0 and portability["contract_error_requests"] == portability["prepared"]
    assert all(call["keys"] == [] for call in env.send.calls), "request lỗi schema không được gửi"
    rows = env.lines("portability", "requests.jsonl")
    assert len(rows) == portability["prepared"] and all(row["status"] is None and row["attempts"] == 0 and row["contract_errors"] == ["SCHEMA: adapter dựng sai"] for row in rows)


def test_dataset_step_service_not_ready_fails_step_but_part_is_error(tmp_path: Path) -> None:
    env = Env(tmp_path, ready=503)
    env.services.ready_timeout = 0.0

    result = env.run()

    assert result.status == FAILED, "không dùng ERROR: run_steps sẽ bỏ qua bước (f)"
    assert env.send.calls == []
    assert any("chưa sẵn sàng" in note for note in result.notes)
    assert env.summary("replay")["status"] == ERROR and env.summary("portability")["status"] == ERROR, "summary.json giữ `error` cho Task dựng Evidence Pack"


def test_dataset_step_unreachable_service_fails_step_but_part_is_error(tmp_path: Path) -> None:
    env = Env(tmp_path, send=FakeSend(unreachable=True))

    result = env.run()

    assert result.status == FAILED
    assert env.summary("replay")["status"] == ERROR and "không nhận kết nối" in env.summary("replay")["note"]


def test_evidence_pack_step_still_runs_after_dataset_step_fails_on_unreachable_target(tmp_path: Path) -> None:
    """Qua `run_steps`: bước (e) không sẵn sàng -> failed, và bước (f) VẪN chạy (SC7 cần ra INSUFFICIENT_DATA thay vì mất cả báo cáo)."""
    env = Env(tmp_path, ready=503)
    env.services.ready_timeout = 0.0
    env.context.steps = ("e", "f")
    ran: list[str] = []

    def evidence(context):
        ran.append("f")
        return StepResult("f", PASSED, NOW, NOW)

    outcomes = cli.run_steps(env.context, {"e": lambda context: step.run(context, env.services), "f": evidence})

    assert [(o.name, o.status) for o in outcomes] == [("e", FAILED), ("f", PASSED)] and ran == ["f"]
    assert exit_code(outcomes) == 1


def test_dataset_step_unexpected_exception_still_becomes_error_through_run_steps(tmp_path: Path) -> None:
    env = Env(tmp_path, fetch=lambda url, sha, cache: (_ for _ in ()).throw(RuntimeError("lỗi mã")))
    env.context.steps = ("e", "f")

    outcomes = cli.run_steps(env.context, {"e": lambda context: step.run(context, env.services), "f": lambda context: StepResult("f", PASSED, NOW, NOW)})

    assert [(o.name, o.status) for o in outcomes] == [("e", ERROR), ("f", SKIPPED)]


def test_dataset_step_requests_jsonl_has_no_request_bodies_or_original_identifiers(tmp_path: Path) -> None:
    env = Env(tmp_path)
    original_ids = set(UUID_PATTERN.findall(SMASHCOURT_CSV.read_text(encoding="utf-8")))
    assert original_ids

    env.run()

    for relative in ("replay/requests.jsonl", "replay/prepared.jsonl", "replay/summary.json", "portability/requests.jsonl", "portability/prepared.jsonl", "portability/summary.json", "portability/source.json"):
        text = env.out(*relative.split("/")).read_text(encoding="utf-8")
        assert not (original_ids & set(UUID_PATTERN.findall(text))), f"{relative} lộ định danh gốc"
    for row in env.lines("replay", "requests.jsonl") + env.lines("portability", "requests.jsonl"):
        assert set(row) >= {"key", "status", "attempts", "decision_id", "correlation_id", "contract_errors", "outcome"}
        assert "body" not in row and "items" not in json.dumps(row)


def test_dataset_step_requests_rows_carry_response_columns_for_d4_d5(tmp_path: Path) -> None:
    env = Env(tmp_path)

    env.run()

    row = env.lines("portability", "requests.jsonl")[0]
    assert row["status"] == 200 and row["attempts"] == 2 and row["correlation_id"] == "corr-0" and row["decision_id"]
    assert row["outcome"]["status"] == "APPROVED" and row["outcome"]["reason_code"] == "TIER_DEPOSIT" and row["outcome"]["deposit_rate"] == "0.5"
    assert re.fullmatch(r"\d+(\.\d+)?", row["outcome"]["deposit_amount"])
    assert row["key"].startswith(f"portability:{env.context.run_id}:")


def test_dataset_step_prepared_jsonl_has_meta_and_key_only_in_input_order(tmp_path: Path) -> None:
    env = Env(tmp_path)

    env.run()

    prepared = env.lines("portability", "prepared.jsonl")
    requests = env.lines("portability", "requests.jsonl")
    assert [row["idempotency_key"] for row in prepared] == [row["key"] for row in requests]
    assert all(set(row) == {"idempotency_key", "meta"} for row in prepared)
    assert prepared[0]["meta"]["source_row"] >= 2 and isinstance(prepared[0]["meta"]["total_price"], str), "Decimal ghi thành chuỗi"
    replay_prepared = env.lines("replay", "prepared.jsonl")
    assert all(set(row["meta"]) >= {"booking_hash", "shift_days", "lead_time_minutes", "window_bias", "request_errors"} for row in replay_prepared)
    assert not any("body" in row for row in replay_prepared)


def test_dataset_step_uses_one_anonymizer_and_one_now_per_part(tmp_path: Path) -> None:
    env = Env(tmp_path)

    env.run()

    assert env.anonymizers == 1, "một Anonymizer mỗi lượt, dùng chung RP và PT"
    assert len(env.clock_reads) == 2, "`now` lấy đúng một lần mỗi phần"
    assert env.summary("replay")["now"] == env.clock_reads[0].isoformat() and env.summary("portability")["now"] == env.clock_reads[1].isoformat()
    shifted = {row["meta"]["shift_days"] for row in env.lines("replay", "prepared.jsonl")}
    assert all(days % 7 == 0 and days >= 0 for days in shifted)


def test_dataset_step_replay_summary_fields(tmp_path: Path) -> None:
    env = Env(tmp_path)

    env.run()

    replay = env.summary("replay")
    assert replay["source_rows"] >= replay["prepared"] and "rejected" in replay
    assert replay["decision_status_counts"] == {"APPROVED": replay["sent"]} and replay["status_counts"] == {"200": replay["sent"]}
    assert replay["retries"] == 1 and replay["attempts_total"] == replay["sent"] + 1
    assert replay["duration_seconds"] >= 0 and replay["requests_per_second"] >= 0 and replay["workers"] == 3
    assert set(replay["latency_ms"]) == {"p50", "p95", "max"}
    assert "window_bias_count" in replay


def test_dataset_step_portability_source_json(tmp_path: Path) -> None:
    env = Env(tmp_path)

    env.run()

    source = json.loads(env.out("portability", "source.json").read_text(encoding="utf-8"))
    assert source["url"] == hotel_adapter.SOURCE_URL and source["sha256"] == hotel_adapter.SOURCE_SHA256
    assert source["total_rows"] == 30 and source["expected_rows"] == hotel_adapter.EXPECTED_ROW_COUNT and source["seed"] == hotel_adapter.SAMPLE_SEED
    assert source["rejected"].keys() == set(hotel_adapter.REJECT_REASONS) and source["mode"] == "quick"
    assert source["valid_count"] >= source["selected"] == env.summary("portability")["prepared"]
    assert source["license"] == hotel_adapter.LICENSE_LABEL and source["citation"] == hotel_adapter.CITATION
    assert source["max_item_minutes"] == hotel_adapter.MAX_ITEM_MINUTES and source["high_value_threshold"] == "750"
    assert env.fetch_calls == [(hotel_adapter.SOURCE_URL, hotel_adapter.SOURCE_SHA256, env.cache)]


def test_dataset_step_quick_samples_and_full_sends_all_valid_rows(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(hotel_adapter, "QUICK_SAMPLE_SIZE", 5)
    quick_root = tmp_path / "q"
    quick_root.mkdir()
    quick = Env(quick_root, mode="quick")
    quick.run()
    full_root = tmp_path / "f"
    full_root.mkdir()
    full = Env(full_root, mode="full")
    full.run()

    assert quick.summary("portability")["prepared"] == 5 and quick.summary("portability")["sample_size"] == 5
    assert full.summary("portability")["prepared"] > 5 and full.summary("portability")["sample_size"] is None
    assert full.summary("portability")["prepared"] == json.loads(full.out("portability", "source.json").read_text(encoding="utf-8"))["valid_count"]


def test_dataset_step_replay_data_format_error_fails_with_line_numbers(tmp_path: Path) -> None:
    env = Env(tmp_path, with_smashcourt=False)
    (env.private / "smashcourt_bookings.csv").write_text("cot_la,khong_dung\n1,2\n", encoding="utf-8")

    result = env.run()

    assert result.status == FAILED
    assert env.summary("replay")["status"] == FAILED and "dòng 1" in env.summary("replay")["note"]


def test_dataset_step_has_no_fault_injection_side_effects(tmp_path: Path) -> None:
    """Bước (e) chỉ chạm hai đích gửi và thư mục raw/replay, raw/portability: không ghi file tiêm lỗi, không gọi gì ngoài `/health/ready` của đích."""
    env = Env(tmp_path)

    env.run()

    written = {path.relative_to(tmp_path).as_posix() for path in (tmp_path / "ev").rglob("*") if path.is_file()}  # `private/` và `cache/` là đầu vào, không tính
    evidence_root = f"ev/{env.context.run_id}/raw/"
    outside = {path for path in written if not (path.startswith(evidence_root + "replay/") or path.startswith(evidence_root + "portability/"))}
    assert outside == set(), f"file ngoài thư mục đầu ra của bước (e): {sorted(outside)}"
    assert not any("fault" in path for path in written)
    assert set(env.probed) == {"http://proxy:8080/health/ready", "http://pt:8080/health/ready"}
    assert {call["base_url"] for call in env.send.calls} == {"http://proxy:8080", "http://pt:8080"}


def test_phases_timed_and_iterate_accumulate_only_their_own_time() -> None:
    ticks = iter([0.0, 2.0, 10.0, 11.0, 20.0, 21.5, 30.5])
    phases = step.Phases(lambda: next(ticks))

    assert phases.timed("load", lambda: "x") == "x"  # 0 -> 2
    seen = []
    for item in phases.iterate("build", ["a"]):  # lấy "a": 10 -> 11; thời gian xử lý bên ngoài (11 -> 20) không tính
        seen.append(item)
    # lần lấy cuối (StopIteration): 20 -> 21.5
    phases.add("send", 30.0)  # mốc bắt đầu 30.0 truyền vào, chỉ đọc thêm mốc kết thúc 30.5

    assert seen == ["a"]
    assert phases.to_dict() == {"load": 2.0, "build": 2.5, "send": 0.5}


def test_dataset_step_summary_records_phase_seconds(tmp_path: Path) -> None:
    env = Env(tmp_path)

    env.run()

    replay, portability = env.summary("replay")["phase_seconds"], env.summary("portability")["phase_seconds"]
    assert set(replay) == {"load", "wait_ready", "build", "response_check", "send"}
    assert set(portability) == {"fetch", "load", "select", "wait_ready", "build", "response_check", "send"}
    for phases in (replay, portability):
        assert all(value >= 0 for value in phases.values())
        assert phases["build"] + phases["response_check"] <= phases["send"] + 0.05, "build và response_check nằm trong vòng gửi"


def test_dataset_step_not_ready_summary_still_records_wait_phase(tmp_path: Path) -> None:
    env = Env(tmp_path, ready=503)
    env.services.ready_timeout = 0

    env.run()

    assert "wait_ready" in env.summary("replay")["phase_seconds"] and "wait_ready" in env.summary("portability")["phase_seconds"]
