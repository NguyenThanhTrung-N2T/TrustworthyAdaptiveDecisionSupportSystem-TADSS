"""Phân tích mô tả RP-01/RP-02/PT-02 trên D4/D5 mẫu (số liệu tính tay), phân loại cao điểm theo lịch của runtime, và dựng D4/D5 từ file của bước (e)."""
import csv
import json
from pathlib import Path

import pytest

from evidence import dataset_analysis as analysis
from evidence import hotel_adapter, replay
from evidence.tests.conftest import FIXTURES

REPO = Path(__file__).resolve().parents[3]
APPSETTINGS = REPO / "src" / "TADSS.Runtime.Api" / "appsettings.json"


def read(name: str) -> list[dict[str, str]]:
    with (FIXTURES / name).open(encoding="utf-8", newline="") as handle:
        return list(csv.DictReader(handle))


@pytest.fixture(scope="module")
def calendar() -> list[analysis.PeakWindow]:
    return analysis.load_calendar(APPSETTINGS)


def test_calendar_is_read_from_the_runtime_appsettings(calendar) -> None:
    assert len(calendar) == 7
    monday = next(window for window in calendar if window.day == 0)
    saturday = next(window for window in calendar if window.day == 5)
    assert (monday.start.hour, monday.end.hour) == (17, 21) and (saturday.start.hour, saturday.end.hour) == (8, 21)


@pytest.mark.parametrize(
    ("start_local", "peak"),
    [
        ("2026-03-02T17:00:00", True),   # thứ Hai, đầu khung (tính cả)
        ("2026-03-02T16:59:00", False),
        ("2026-03-02T20:59:00", True),
        ("2026-03-02T21:00:00", False),   # cuối khung không tính (nửa mở)
        ("2026-03-07T08:00:00", True),    # thứ Bảy 08:00-21:00
        ("2026-03-07T07:59:00", False),
        ("2026-03-08T12:00:00", True),
    ],
)
def test_is_peak_uses_half_open_windows_of_the_runtime(calendar, start_local: str, peak: bool) -> None:
    assert analysis.is_peak(start_local, calendar) is peak


def test_rp_summary_fixture(calendar) -> None:
    summary = analysis.rp_summary(read("d4_sample.csv"), calendar)

    assert summary["bookings"] == 10 and summary["channels"] == {"ONLINE": 8, "WALK_IN": 2} and summary["main_scope"] == "ONLINE"
    online, everything = summary["scopes"]["ONLINE"], summary["scopes"]["ALL"]
    assert online["bookings"] == 8 and everything["bookings"] == 10
    assert online["window_bias_bookings"] == 2 and everything["window_bias_bookings"] == 4

    static = online["policies"]["STATIC_100"]
    assert static["mean_rate"] == pytest.approx(1.0) and static["need_approval_rate"] == 0 and static["mean_amount"] == pytest.approx(121250)
    assert static["no_show"] == {"bookings": 2, "total_price": 350000, "deposit_kept": 350000}

    runtime = online["policies"]["TADSS_V2"]
    assert (runtime["bookings"], runtime["decided"], runtime["approved"], runtime["need_approval"], runtime["no_decision"]) == (8, 7, 5, 2, 1)
    assert runtime["mean_rate"] == pytest.approx(0.6) and runtime["mean_amount"] == pytest.approx(80000) and runtime["need_approval_rate"] == pytest.approx(2 / 7)
    assert runtime["no_show"] == {"bookings": 2, "total_price": 350000, "deposit_kept": 200000}

    adjusted = online["policies"]["TADSS_V2_ADJUSTED"]
    assert (adjusted["approved"], adjusted["need_approval"]) == (6, 1)
    assert adjusted["mean_rate"] == pytest.approx(4.0 / 6) and adjusted["mean_amount"] == pytest.approx(550000 / 6) and adjusted["need_approval_rate"] == pytest.approx(1 / 7)
    assert adjusted["no_show"]["deposit_kept"] == 350000
    assert online["adjusted_changes"] == 1 and everything["adjusted_changes"] == 2, "chỉ các dòng bị gắn cờ window_bias và strategy NEED_APPROVAL đổi kết quả"

    all_runtime = everything["policies"]["TADSS_V2"]
    assert (all_runtime["approved"], all_runtime["need_approval"]) == (6, 3) and all_runtime["mean_rate"] == pytest.approx(0.55) and all_runtime["no_show"]["deposit_kept"] == 200000
    assert everything["policies"]["STATIC_100"]["no_show"]["deposit_kept"] == 450000
    assert everything["policies"]["STATIC_100"]["mean_rate"] == pytest.approx(1.0), "STATIC_100 luôn tỷ lệ 1,0"


def test_rp_summary_breakdowns_by_channel_tier_and_window(calendar) -> None:
    summary = analysis.rp_summary(read("d4_sample.csv"), calendar)

    everything, online = summary["scopes"]["ALL"], summary["scopes"]["ONLINE"]
    assert everything["status_by_channel"] == {"ONLINE": {"APPROVED": 5, "NEED_APPROVAL": 2, "NO_RESULT": 1}, "WALK_IN": {"APPROVED": 1, "NEED_APPROVAL": 1}}
    tiers = online["by_tier"]
    assert list(tiers) == ["STANDARD", "SILVER", "GOLD", "DIAMOND"]
    assert tiers["GOLD"]["mean_rate"] == pytest.approx(0.4) and tiers["GOLD"]["approved"] == 2
    assert tiers["STANDARD"] | {"mean_rate": 1.0} == tiers["STANDARD"] and tiers["STANDARD"]["bookings"] == 3 and tiers["STANDARD"]["need_approval"] == 1
    assert tiers["DIAMOND"]["mean_rate"] == pytest.approx(0.2)
    windows = online["by_window"]
    assert windows["PEAK"]["bookings"] == 4 and windows["PEAK"]["mean_rate"] == pytest.approx((0.5 + 1 + 0.2) / 3)
    assert windows["OFFPEAK"]["bookings"] == 4 and windows["OFFPEAK"]["mean_rate"] == pytest.approx(0.65), "21:00 là cuối khung (không tính) nên thuộc thấp điểm"


def test_rp_summary_empty_input(calendar) -> None:
    summary = analysis.rp_summary([], calendar)

    assert summary["bookings"] == 0 and summary["scopes"]["ONLINE"]["bookings"] == 0
    assert summary["scopes"]["ONLINE"]["policies"]["TADSS_V2"]["mean_rate"] is None


def test_adjustment_without_strategy_status_keeps_runtime_result(calendar) -> None:
    rows = [{**row, "strategy_status": "", "status_window_adjusted": row["status"]} for row in read("d4_sample.csv")]

    summary = analysis.rp_summary(rows, calendar)

    assert summary["scopes"]["ONLINE"]["adjusted_changes"] == 0
    assert summary["scopes"]["ONLINE"]["policies"]["TADSS_V2_ADJUSTED"] == summary["scopes"]["ONLINE"]["policies"]["TADSS_V2"]


def test_pt_summary_crosstab() -> None:
    summary = analysis.pt_summary(read("d5_sample.csv"))

    assert summary["records"] == 12 and summary["status_counts"] == {"APPROVED": 9, "NEED_APPROVAL": 2, "NO_RESULT": 1}
    assert summary["rate_distribution"] == {"0.3": 4, "0.5": 3, "1": 2} and summary["mean_rate"] == pytest.approx(4.7 / 9)
    by_status = {item["status"]: item for item in summary["status_by_canceled"]}
    assert by_status["APPROVED"] == {"status": "APPROVED", "total": 9, "canceled": 4, "not_canceled": 5, "cancel_rate": pytest.approx(4 / 9)}
    assert by_status["NEED_APPROVAL"]["canceled"] == 1 and by_status["NEED_APPROVAL"]["cancel_rate"] == pytest.approx(0.5)
    assert by_status["NO_RESULT"]["total"] == 1 and by_status["NO_RESULT"]["cancel_rate"] == 0
    assert summary["canceled_total"] == 5 and summary["cancel_rate_overall"] == pytest.approx(5 / 12)
    table = summary["rate_by_deposit_type"]
    assert table["rows"] == ["0.3", "0.5", "1", "NEED_APPROVAL", "NO_RESULT"] and table["columns"] == ["No Deposit", "Non Refund", "Refundable"]
    assert table["counts"]["0.3"] == {"No Deposit": 3, "Non Refund": 0, "Refundable": 1}
    assert table["counts"]["1"] == {"No Deposit": 0, "Non Refund": 2, "Refundable": 0}
    assert table["counts"]["NEED_APPROVAL"]["No Deposit"] == 2
    assert sum(sum(row.values()) for row in table["counts"].values()) == 12
    groups = {item["label"]: item for item in summary["rate_groups"]}
    assert groups["0.3"]["canceled"] == 2 and groups["0.3"]["total"] == 4 and groups["0.3"]["cancel_rate"] == pytest.approx(0.5)


def test_pt_summary_is_descriptive_only() -> None:
    summary = analysis.pt_summary(read("d5_sample.csv"))

    serialized = json.dumps(summary).lower()
    for forbidden in ("p_value", "pvalue", "chi", "correlation", "causal", "significan", "odds"):
        assert forbidden not in serialized, "PT-02 chỉ mô tả, không kiểm định hay kết luận nhân quả"


def test_pt_summary_empty_input() -> None:
    summary = analysis.pt_summary([])

    assert summary["records"] == 0 and summary["mean_rate"] is None and summary["cancel_rate_overall"] is None


# --- Dựng D4/D5 từ file của bước (e) ---

def _write_jsonl(path: Path, rows: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("".join(json.dumps(row, ensure_ascii=False) + "\n" for row in rows), encoding="utf-8")


def _meta(index: int, bias: bool, lead: int) -> dict:
    return {
        "booking_hash": f"bk{index}", "branch_hash": "br", "court_count": 1, "booking_date": "2026-03-02", "start_local": "2026-03-02T18:00:00", "duration_min": 60,
        "total_price": "100000", "customer_tier": "GOLD", "customer_is_guest": False, "dev_final_status": "COMPLETED", "channel": "ONLINE", "shift_days": 7,
        "lead_time_minutes": lead, "window_bias": bias, "request_errors": [],
    }


def test_build_d4_from_step_e_files(tmp_path: Path) -> None:
    replay_dir = tmp_path / "raw" / "replay"
    _write_jsonl(replay_dir / "prepared.jsonl", [{"idempotency_key": f"k{i}", "meta": _meta(i, i == 2, 60 if i == 2 else 900)} for i in (1, 2)])
    outcome = {"status": "NEED_APPROVAL", "reason_code": "HIGH", "deposit_rate": "", "deposit_amount": ""}
    _write_jsonl(replay_dir / "requests.jsonl", [
        {"key": "k2", "status": 200, "attempts": 1, "decision_id": "d-2", "outcome": outcome, "contract_errors": []},
        {"key": "k1", "status": 200, "attempts": 2, "decision_id": "d-1", "outcome": {**outcome, "status": "APPROVED", "deposit_rate": "0.5", "deposit_amount": "50000"}, "contract_errors": []},
    ])

    rows = analysis.build_d4(tmp_path, {"d-2": {"strategy_status": "NEED_APPROVAL"}, "d-1": {"strategy_status": "APPROVED"}})

    assert rows is not None and [(row["booking_hash"], row["policy"]) for row in rows] == [("bk1", "STATIC_100"), ("bk1", "TADSS_V2"), ("bk2", "STATIC_100"), ("bk2", "TADSS_V2")]
    assert rows[1]["status"] == "APPROVED" and rows[1]["deposit_amount"] == "50000"
    assert rows[3]["status"] == "NEED_APPROVAL" and rows[3]["status_window_adjusted"] == "APPROVED", "lead gốc 60 phút ≤ 120: hiệu chỉnh offline thành APPROVED"
    assert set(rows[0]) == set(replay.D4_COLUMNS)


def test_build_d4_d5_return_none_without_prepared_file(tmp_path: Path) -> None:
    assert analysis.build_d4(tmp_path, {}) is None
    assert analysis.build_d5(tmp_path, {}) is None


def test_build_d5_from_step_e_files(tmp_path: Path) -> None:
    directory = tmp_path / "raw" / "portability"
    meta = {
        "source_row": 5, "hotel": "City Hotel", "arrival_local": "2016-06-14T14:00:00", "nights": 2, "room_type": "A", "total_price": "170", "is_repeated_guest": 0,
        "previous_cancellations": 0, "previous_bookings_not_canceled": 0, "is_canceled": 1, "deposit_type": "Non Refund", "market_segment": "Direct", "shift_days": 7,
        "lead_time_minutes": 100, "duration_min": 100, "request_errors": [],
    }
    _write_jsonl(directory / "prepared.jsonl", [{"idempotency_key": "portability:r:5", "meta": meta}])
    _write_jsonl(directory / "requests.jsonl", [{"key": "portability:r:5", "status": 200, "attempts": 2, "decision_id": "p-5",
                                                   "outcome": {"status": "APPROVED", "reason_code": "TIER", "deposit_rate": "0.3", "deposit_amount": "51"}, "contract_errors": []}])

    rows = analysis.build_d5(tmp_path, {"p-5": {"strategy_status": "APPROVED"}})

    assert rows is not None and len(rows) == 1 and list(rows[0]) == list(hotel_adapter.D5_COLUMNS)
    assert rows[0]["attempts"] == 2 and rows[0]["deposit_type"] == "Non Refund" and rows[0]["status"] == "APPROVED"


def test_build_d5_with_shadow_pairs_by_correlation(tmp_path: Path) -> None:
    directory = tmp_path / "raw" / "portability"
    meta = {
        "source_row": 5, "hotel": "City Hotel", "arrival_local": "2016-06-14T14:00:00", "nights": 2, "room_type": "A", "total_price": "170", "is_repeated_guest": 0,
        "previous_cancellations": 0, "previous_bookings_not_canceled": 0, "is_canceled": 1, "deposit_type": "Non Refund", "market_segment": "Direct", "shift_days": 7,
        "lead_time_minutes": 100, "duration_min": 100, "request_errors": [],
    }
    _write_jsonl(directory / "prepared.jsonl", [{"idempotency_key": "portability:r:5", "meta": meta}])
    _write_jsonl(directory / "requests.jsonl", [{"key": "portability:r:5", "correlation_id": "a", "status": 200, "attempts": 1, "decision_id": "p-5",
                                                   "outcome": {"status": "APPROVED", "reason_code": "TIER", "deposit_rate": "0.3", "deposit_amount": "51"}, "contract_errors": []}])
    d1_rows = [
        {"role": "PRIMARY", "decision_id": "p-5", "correlation_id": "a", "reservation_hash": "r", "request_hash": "h", "created_at_utc": "t",
         "fallback_used": "False", "strategy_status": "APPROVED", "status": "APPROVED", "deposit_rate": "0.3", "deposit_amount": "51"},
        {"role": "SHADOW", "decision_id": "s-5", "correlation_id": "a", "reservation_hash": "r", "request_hash": "h", "created_at_utc": "t",
         "fallback_used": "False", "status": "APPROVED", "deposit_rate": "0.5", "deposit_amount": "85"},
    ]

    result = analysis.build_d5_with_shadow(tmp_path, d1_rows)

    assert result is not None
    rows, pairing = result
    assert rows[0]["shadow_pair_state"] == "PAIRED" and rows[0]["shadow_deposit_rate"] == "0.5"
    assert pairing.states == {"a": "PAIRED"}


def test_build_d5_with_shadow_returns_none_without_prepared_file(tmp_path: Path) -> None:
    assert analysis.build_d5_with_shadow(tmp_path, []) is None


def test_write_csv_round_trips_booleans_and_header(tmp_path: Path) -> None:
    rows = read("d4_sample.csv")
    path = tmp_path / "raw" / "d4" / "replay_smashcourt.csv"

    analysis.write_csv(path, replay.D4_COLUMNS, [{**row, "window_bias": row["window_bias"] == "True"} for row in rows])

    assert list(csv.reader(path.open(encoding="utf-8")))[0] == list(replay.D4_COLUMNS)
    assert len(path.read_text(encoding="utf-8").splitlines()) == 21


# --- Độ phủ danh mục và thông tin manifest ---

def _write_summary(run_dir: Path, part: str, **fields) -> None:
    directory = run_dir / "raw" / part
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "summary.json").write_text(json.dumps({"part": part, **fields}), encoding="utf-8")


def test_dataset_coverage_follows_summaries_and_analysis_files(tmp_path: Path) -> None:
    assert analysis.dataset_coverage(tmp_path) == (set(), set())
    _write_summary(tmp_path, "replay", status="passed", sent=10, contract_error_requests=0)
    _write_summary(tmp_path, "portability", status="passed", sent=10, contract_error_requests=0)

    assert analysis.dataset_coverage(tmp_path) == ({"RP-01", "PT-01"}, set())

    (tmp_path / "raw" / "analysis").mkdir(parents=True)
    (tmp_path / "raw" / "analysis" / "rp_summary.json").write_text("{}", encoding="utf-8")
    (tmp_path / "raw" / "analysis" / "pt_summary.json").write_text("{}", encoding="utf-8")
    assert analysis.dataset_coverage(tmp_path)[0] == {"RP-01", "RP-02", "PT-01", "PT-02"}


def test_dataset_coverage_marks_contract_errors_and_code_failure_as_failed(tmp_path: Path) -> None:
    _write_summary(tmp_path, "replay", status="failed", sent=10, contract_error_requests=2)
    _write_summary(tmp_path, "portability", status="passed", sent=10, contract_error_requests=0)
    (tmp_path / "raw" / "analysis").mkdir(parents=True)
    (tmp_path / "raw" / "analysis" / "sc7_code_check.json").write_text(json.dumps({"status": "FAIL"}), encoding="utf-8")

    assert analysis.dataset_coverage(tmp_path) == (set(), {"RP-01", "PT-01"})


def test_dataset_coverage_ignores_skipped_and_unreachable_parts(tmp_path: Path) -> None:
    _write_summary(tmp_path, "replay", status="skipped", note="RP: bỏ qua")
    _write_summary(tmp_path, "portability", status="error", prepared=5, sent=2, contract_error_requests=0)

    assert analysis.dataset_coverage(tmp_path) == (set(), set())


def _write_economics(run_dir: Path, shadow: dict) -> None:
    directory = run_dir / "raw" / "d5"
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "policy_economics.json").write_text(json.dumps({"shadow": shadow}), encoding="utf-8")


def test_dataset_coverage_pt03_and_pt04_follow_policy_economics(tmp_path: Path) -> None:
    assert analysis.dataset_coverage(tmp_path) == (set(), set())

    _write_economics(tmp_path, {"reconciliation": {"label": "RECONCILED"}, "violations": {}})
    assert analysis.dataset_coverage(tmp_path) == ({"PT-03", "PT-04"}, set())

    _write_economics(tmp_path, {"reconciliation": {"label": "FAILED"}, "violations": {}})
    assert analysis.dataset_coverage(tmp_path) == ({"PT-03"}, {"PT-04"})

    _write_economics(tmp_path, {"reconciliation": {"label": "RECONCILED"}, "violations": {"ORPHAN_SHADOW": 1}})
    assert analysis.dataset_coverage(tmp_path) == ({"PT-03"}, {"PT-04"})


def test_dataset_coverage_pt04_is_unchecked_when_reconciliation_is_unknown(tmp_path: Path) -> None:
    _write_economics(tmp_path, {"reconciliation": {"label": "UNKNOWN"}, "violations": {}})
    assert analysis.dataset_coverage(tmp_path) == ({"PT-03"}, set())

    _write_economics(tmp_path, {"reconciliation": None, "violations": {}})
    assert analysis.dataset_coverage(tmp_path) == ({"PT-03"}, set())


def test_dataset_info_collects_counts_source_and_code_check(tmp_path: Path) -> None:
    _write_summary(tmp_path, "replay", status="passed", note="RP: ok", sent=3, prepared=3, not_sent=0, contract_error_requests=0, retries=1, rejected={"no_invoice": 1}, window_bias_count=2, noise="bỏ")
    _write_summary(tmp_path, "portability", status="skipped", note="PT: bỏ qua vì không tải được")
    (tmp_path / "raw" / "portability" / "source.json").write_text(json.dumps({"url": "https://x/y.csv", "sha256": "ab", "seed": 7, "license": "CC", "mode": "quick", "sample_size": 10}), encoding="utf-8")

    info = analysis.dataset_info(tmp_path, {"status": "INSUFFICIENT_DATA", "reason": "adapter chưa commit", "commits": []})

    assert info["replay"] == {"status": "passed", "note": "RP: ok", "prepared": 3, "sent": 3, "not_sent": 0, "contract_error_requests": 0, "retries": 1, "rejected": {"no_invoice": 1}, "window_bias_count": 2}
    assert info["portability"]["status"] == "skipped" and info["portability"]["source"]["url"] == "https://x/y.csv" and info["portability"]["seed"] == 7 and info["portability"]["mode"] == "quick"
    assert info["portability"]["codeCheck"]["status"] == "INSUFFICIENT_DATA"
    assert analysis.dataset_info(tmp_path / "none", None) == {"replay": None, "portability": None}


def test_rejected_status_is_counted_separately(calendar) -> None:
    rows = read("d4_sample.csv")
    target = next(row for row in rows if row["booking_hash"] == "bk05" and row["policy"] == "TADSS_V2")
    target.update({"status": "REJECTED", "deposit_rate": "", "deposit_amount": "", "reason_code": "POLICY", "status_window_adjusted": "REJECTED", "strategy_status": "REJECTED"})

    summary = analysis.rp_summary(rows, calendar)

    online = summary["scopes"]["ONLINE"]
    assert online["status_by_channel"]["ONLINE"] == {"APPROVED": 5, "NEED_APPROVAL": 1, "NO_RESULT": 1, "REJECTED": 1}
    runtime = online["policies"]["TADSS_V2"]
    assert runtime["rejected"] == 1 and runtime["need_approval"] == 1 and runtime["no_decision"] == 2 and runtime["decided"] == 6
    assert sum(online["status_by_channel"]["ONLINE"].values()) == online["bookings"]
