# Task 2.18 (plan 6C B.7.2–B.7.5): chấm SLO từ mẫu thô k6, không từ histogram Prometheus (biên bucket 250/500 ms kẹp ngưỡng 300 ms)
import csv
import gzip
import sys
from pathlib import Path

import pytest

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import verdict  # noqa: E402

SLO = verdict.load_slo()
T0 = 1_793_552_400


def _raw(durations=(), errors=(), dropped=()) -> verdict.Raw:
    return verdict.Raw(
        durations=[verdict.Sample(t, slo, name, phase, value) for t, slo, name, phase, value in durations],
        errors=[verdict.Sample(t, slo, name, phase, value) for t, slo, name, phase, value in errors],
        dropped=list(dropped))


def _steady(slo: str, values, t: float = T0, name: str = "GET x"):
    return [(t, slo, name, "steady", v) for v in values]


def test_duration_strings_and_slo_file_numbers_match_the_plan():
    assert [verdict.seconds(x) for x in ("10s", "3m", "1m30s", "1h", "90m")] == [10, 180, 90, 3600, 5400]
    assert SLO["groups"]["read"] == {"p95": 300, "p99": 800} and SLO["groups"]["write"] == {"p95": 500, "p99": 1000}
    assert SLO["errorRateMax"] == 0.005 and SLO["minSamples"] == {"p95": 100, "p99": 1000}


def test_percentile_interpolates_between_ranks_like_k6():
    assert verdict.percentile([1, 2, 3, 4], 0.5) == 2.5
    assert verdict.percentile(list(range(1, 101)), 0.95) == pytest.approx(95.05)
    assert verdict.percentile([], 0.95) is None


def test_every_business_rejection_has_a_reason_and_write_endpoints_are_listed():
    for endpoint, pairs in SLO["businessRejections"].items():
        assert pairs and all(p["reason"].strip() and 400 <= p["status"] < 500 and p["code"] for p in pairs), endpoint
    assert len(SLO["writeEndpoints"]) == 4


def test_group_with_too_few_samples_is_insufficient_never_pass():
    result = verdict.judge([10.0] * 99, SLO["groups"]["read"], SLO["minSamples"])
    assert result["n"] == 99 and result["p95Status"] == "INSUFFICIENT" and result["p99Status"] == "INSUFFICIENT"


def test_p99_needs_a_thousand_samples_while_p95_needs_a_hundred():
    result = verdict.judge([10.0] * 500, SLO["groups"]["read"], SLO["minSamples"])
    assert result["p95Status"] == "PASS" and result["p99Status"] == "INSUFFICIENT"


def test_slo_verdict_fails_a_group_over_threshold_and_only_counts_steady_phase():
    durations = _steady("read", [100.0] * 1000) + _steady("booking", [1500.0] * 1000) \
        + [(T0, "read", "GET x", "warmup", 9000.0)] * 500 + [(T0, "read", "GET x", "settle", 9000.0)] * 500
    errors = _steady("read", [0] * 1000) + _steady("booking", [0] * 1000)
    result = verdict.slo_verdict(_raw(durations, errors), SLO)
    assert result["groups"]["read"]["n"] == 1000 and result["groups"]["read"]["p99Status"] == "PASS"
    assert result["groups"]["booking"]["p95Status"] == "FAIL"
    assert result["status"] == "FAIL"


def test_error_rate_includes_timeouts_and_unexpected_4xx_and_has_its_own_status():
    errors = _steady("read", [1] * 6 + [0] * 994)
    result = verdict.slo_verdict(_raw(_steady("read", [100.0] * 1000), errors), SLO)
    assert result["errors"]["n"] == 1000 and result["errors"]["rate"] == pytest.approx(0.006)
    assert result["errors"]["status"] == "FAIL"


def test_overall_is_insufficient_when_nothing_fails_but_some_p99_lacks_samples():
    durations = _steady("read", [100.0] * 1000) + _steady("login", [200.0] * 900)
    errors = _steady("read", [0] * 1000) + _steady("login", [0] * 900)
    result = verdict.slo_verdict(_raw(durations, errors), SLO)
    assert result["groups"]["login"]["p99Status"] == "INSUFFICIENT" and result["status"] == "INSUFFICIENT"


def test_write_endpoints_get_their_own_p95_when_they_have_enough_samples():
    durations = _steady("write", [100.0] * 150, name="POST /api/bookings/{id}/check-in") \
        + _steady("write", [900.0] * 20, name="POST /api/bookings/{id}/approve")
    result = verdict.slo_verdict(_raw(durations, _steady("write", [0] * 170)), SLO)
    endpoints = result["endpoints"]
    assert endpoints["POST /api/bookings/{id}/check-in"]["p95Status"] == "PASS"
    assert endpoints["POST /api/bookings/{id}/approve"]["p95Status"] == "INSUFFICIENT"


def test_selftest_lowers_read_p95_to_one_millisecond_so_the_run_must_fail():
    durations = _steady("read", [100.0] * 1000)
    result = verdict.slo_verdict(_raw(durations, _steady("read", [0] * 1000)), SLO, selftest=True)
    assert result["groups"]["read"]["p95Status"] == "FAIL" and result["status"] == "FAIL"


def test_breakpoint_skips_the_first_30_s_of_each_hold_and_finds_the_lowest_violating_step():
    # bậc 1 (10/s) nhanh; bậc 2 (15/s): 20 s đầu phần giữ chậm (bị bỏ) rồi nhanh; bậc 3 (20/s): cửa sổ thứ hai chậm
    steps = [{"rate": 10, "transition": 10, "hold": 180}, {"rate": 15, "transition": 10, "hold": 180},
             {"rate": 20, "transition": 10, "hold": 180}]
    durations, errors = [], []
    for index, step in enumerate(steps):
        hold_start = T0 + index * 190 + 10
        for second in range(180):
            slow = (index == 1 and second < 20) or (index == 2 and 90 <= second < 150)
            for _ in range(3):
                durations.append((hold_start + second, "read", "GET x", "steady", 900.0 if slow else 50.0))
                errors.append((hold_start + second, "read", "GET x", "steady", 0))
    result = verdict.step_verdict(_raw(durations, errors), T0, steps, SLO)
    assert [len(s["windows"]) for s in result["steps"]] == [2, 2, 2]
    assert result["steps"][1]["violations"] == [] and result["breakStep"] == 2
    assert result["steps"][2]["windows"][1]["violations"] == ["p95 read"]


def test_window_violation_on_errors_and_dropped_iterations_but_not_on_small_groups():
    # booking chỉ 50 mẫu (< 100) ⇒ không xét p95 booking dù chậm; lỗi 4/170 ⇒ vi phạm; bỏ 2 / (10 lượt/s × 60 s) = 0,33 % ⇒ không
    durations = [(T0 + s, "read", "GET x", "steady", 50.0) for s in range(60) for _ in range(2)] \
        + [(T0 + s, "booking", "POST b", "steady", 5000.0) for s in range(50)]
    errors = [(T0 + s, "read", "GET x", "steady", 1 if s < 2 else 0) for s in range(60) for _ in range(2)] \
        + [(T0 + s, "booking", "POST b", "steady", 0) for s in range(50)]
    stats = verdict.window_stats(_raw(durations, errors, dropped=[T0 + 5, T0 + 6]), T0, T0 + 60, 10, SLO)
    assert stats["groups"]["booking"]["n"] == 50
    assert stats["violations"] == ["errors"]


def test_spike_recovery_is_the_start_of_three_consecutive_good_10_s_windows():
    durations, errors = [], []
    for second in range(300):
        value = 2000.0 if second < 40 else 50.0
        for _ in range(12):
            durations.append((T0 + second, "read", "GET x", "steady", value))
            errors.append((T0 + second, "read", "GET x", "steady", 0))
    result = verdict.spike_recovery(_raw(durations, errors), T0, T0 + 300, SLO)
    assert result["seconds"] == 40 and result["status"] == "PASS"
    # đo từ 30 s trước: 3 cửa sổ đầu không có mẫu (không tính là tốt) ⇒ hồi phục 70 s ⇒ quá 60 s
    late = verdict.spike_recovery(_raw(durations, errors), T0 - 30, T0 + 300, SLO)
    assert late["seconds"] == 70 and late["status"] == "FAIL"


def test_spike_never_recovering_within_the_tail_is_reported_as_such():
    durations = [(T0 + s, "read", "GET x", "steady", 2000.0) for s in range(300) for _ in range(12)]
    result = verdict.spike_recovery(_raw(durations, [(t, s, n, p, 0) for t, s, n, p, _ in durations]), T0, T0 + 300, SLO)
    assert result["seconds"] is None and result["status"] == "NOT_RECOVERED"


def test_soak_compares_averages_not_single_points_and_checks_p95_drift():
    durations, errors = [], []
    for minute in range(90):
        value = 100.0 if minute < 80 else 130.0   # cửa sổ 10 phút cuối = 1,3 × cửa sổ đầu
        for second in range(0, 60, 2):
            for _ in range(4):
                durations.append((T0 + minute * 60 + second, "read", "GET x", "steady", value))
                errors.append((T0 + minute * 60 + second, "read", "GET x", "steady", 0))
    raw = _raw(durations, errors)
    ok = verdict.soak_verdict(raw, T0, T0 + 5400, SLO, memory=(500.0, 540.0), connections=(10.0, 10.5))
    assert ok["memory"]["status"] == "PASS" and ok["connections"]["status"] == "PASS"
    assert ok["p95Drift"]["status"] == "FAIL" and ok["status"] == "FAIL"
    leak = verdict.soak_verdict(raw, T0, T0 + 5400, SLO, memory=(500.0, 560.0), connections=(10.0, 10.0))
    assert leak["memory"]["growth"] == pytest.approx(0.12) and leak["memory"]["status"] == "FAIL"


def test_pooled_p99_uses_concatenated_samples_not_the_mean_of_p99s():
    # 3 lượt × 400 mẫu: mỗi lượt riêng thiếu mẫu p99; ghép 1200 mẫu thì đủ
    runs = []
    for slow in (0, 0, 30):
        values = [100.0] * (400 - slow) + [5000.0] * slow
        runs.append(_raw(_steady("login", values), _steady("login", [0] * 400)))
    pooled = verdict.pool(runs, SLO)
    login = pooled["groups"]["login"]
    assert login["n"] == 1200 and login["p99Status"] == "FAIL"
    assert login["p99"] == pytest.approx(verdict.percentile([100.0] * 1170 + [5000.0] * 30, 0.99))


def test_read_raw_parses_k6_csv_gz_and_keeps_only_needed_metrics(tmp_path):
    path = tmp_path / "raw.csv.gz"
    header = ["metric_name", "timestamp", "metric_value", "check", "error", "error_code", "expected_response", "group", "method",
              "name", "proto", "scenario", "service", "status", "subproto", "tls_version", "url", "extra_tags", "metadata"]
    rows = [
        ["http_req_duration", str(T0), "12.5", "", "", "", "true", "", "GET", "GET x", "", "default", "", "200", "", "", "",
         "journey=J1&phase=steady&slo=read", ""],
        ["http_req_waiting", str(T0), "11", "", "", "", "true", "", "GET", "GET x", "", "default", "", "200", "", "", "",
         "journey=J1&phase=steady&slo=read", ""],
        ["request_errors", str(T0 + 1), "1", "", "", "", "", "", "", "GET x", "", "default", "", "", "", "", "",
         "phase=steady&slo=read", ""],
        ["dropped_iterations", str(T0 + 2), "1", "", "", "", "", "", "", "", "", "default", "", "", "", "", "", "", ""],
    ]
    with gzip.open(path, "wt", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(header)
        writer.writerows(rows)
    raw = verdict.read_raw(path)
    assert raw.durations == [verdict.Sample(T0, "read", "GET x", "steady", 12.5)]
    assert raw.errors == [verdict.Sample(T0 + 1, "read", "GET x", "steady", 1.0)]
    assert raw.dropped == [T0 + 2]
