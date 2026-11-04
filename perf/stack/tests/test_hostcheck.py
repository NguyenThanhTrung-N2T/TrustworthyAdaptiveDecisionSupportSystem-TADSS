# Plan 6C 2C (người dùng chốt 05/11): chia CPU theo core vật lý, kiểm máy trước lượt đo, tóm tắt xung nhịp CPU của laptop đo
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import hostcheck  # noqa: E402

# i5-1135G7 trong VM Docker (lscpu 05/11): 4 core × 2 luồng, cặp (0,1) (2,3) (4,5) (6,7)
LAPTOP = {0: 0, 1: 0, 2: 1, 3: 1, 4: 2, 5: 2, 6: 3, 7: 3}


def test_cpu_range_compacts_consecutive_cpus():
    assert hostcheck.cpu_range([0, 1, 2, 3, 4, 5]) == "0-5"
    assert hostcheck.cpu_range([6, 7]) == "6-7"
    assert hostcheck.cpu_range([5, 0, 2, 1]) == "0-2,5"
    assert hostcheck.cpu_range([3]) == "3"


def test_physical_layout_gives_tools_the_last_whole_core_and_system_the_rest():
    assert hostcheck.physical_layout(LAPTOP) == ("0-5", "6-7")


def test_physical_layout_never_splits_a_core_between_system_and_tools():
    system, tools = hostcheck.physical_layout(LAPTOP)
    cpus = lambda text: {int(c) for part in text.split(",") for c in (range(int(part.split("-")[0]), int(part.split("-")[-1]) + 1))}
    system_cores = {LAPTOP[c] for c in cpus(system)}
    tools_cores = {LAPTOP[c] for c in cpus(tools)}
    assert not system_cores & tools_cores


def test_physical_layout_handles_non_contiguous_sibling_numbering():
    # Một số máy đánh số luồng anh em cách nhau (0,4) (1,5) (2,6) (3,7)
    spread = {0: 0, 1: 1, 2: 2, 3: 3, 4: 0, 5: 1, 6: 2, 7: 3}
    assert hostcheck.physical_layout(spread) == ("0-2,4-6", "3,7")


def test_physical_layout_falls_back_to_legacy_without_enough_cores_or_hyperthreading():
    assert hostcheck.physical_layout({0: 0, 1: 0, 2: 1, 3: 1, 4: 2, 5: 2}) == hostcheck.LEGACY_LAYOUT
    assert hostcheck.physical_layout({c: c for c in range(8)}) == hostcheck.LEGACY_LAYOUT


def test_power_mode_names_cover_the_four_windows_overlays():
    assert hostcheck.power_mode_name("ded574b5-45a0-4f42-8737-46345c09c238") == "Best performance"
    assert hostcheck.power_mode_name("961CC777-2547-4F9D-8174-7D86181B8A7A") == "Best power efficiency"
    assert hostcheck.power_mode_name("00000000-0000-0000-0000-000000000000") == "Balanced"
    assert hostcheck.power_mode_name("") == "không đọc được"
    assert hostcheck.power_mode_name("abc").startswith("không rõ")


def test_memory_check_refuses_above_95_percent_of_the_docker_vm():
    gib = 1024**3
    assert hostcheck.memory_check(8 * gib, int(6.25 * gib), 1 * gib)["ok"]
    refused = hostcheck.memory_check(8 * gib, int(6.25 * gib), int(1.5 * gib))
    assert not refused["ok"] and refused["ratio"] > 0.95


def test_parse_size_reads_docker_stats_units():
    assert hostcheck._parse_size("512MiB") == 512 * 1024**2
    assert hostcheck._parse_size("1.5GiB") == int(1.5 * 1024**3)
    assert hostcheck._parse_size("0B") == 0


def test_clock_summary_reports_effective_clock_and_throttled_share():
    rows = [(t, 2419.0, 125.0) for t in range(18)] + [(18, 2419.0, 60.0), (19, 2419.0, 55.0)]
    summary = hostcheck.clock_summary(rows)
    assert summary["samples"] == 20
    assert summary["effectiveMhz"]["median"] == round(2419 * 1.25)
    assert summary["effectiveMhz"]["min"] == round(2419 * 0.55)
    assert summary["throttledShare"] == 0.1  # 2 / 20 mẫu dưới 80 % trung vị


def test_clock_summary_without_samples_says_so():
    assert hostcheck.clock_summary([]) == {"samples": 0, "note": "không có mẫu xung nhịp"}


def test_read_clock_skips_malformed_lines(tmp_path):
    (tmp_path / hostcheck.CLOCK_FILE).write_text("epoch,base_mhz,performance_pct\n1,2419,120.5\nhỏng\n2,2419,\n3,2419,99\n", encoding="utf-8")
    assert hostcheck.read_clock(tmp_path) == [(1.0, 2419.0, 120.5), (3.0, 2419.0, 99.0)]
