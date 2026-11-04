"""Máy chủ đo hiệu năng (Epic 6C, plan 6C 2C, người dùng chốt 05/11): chia CPU theo core vật lý, kiểm máy trước lượt đo, ghi xung nhịp CPU.

    python perf/stack/hostcheck.py layout [physical|legacy]          ⇒ "<cpuset hệ thống> <cpuset công cụ>" (perf.sh up)
    python perf/stack/hostcheck.py topology                          ⇒ JSON luồng → core vật lý (metadata lượt đo)
    python perf/stack/hostcheck.py preflight [--allow-battery]       ⇒ JSON nguồn điện, chế độ nguồn, RAM, đĩa; thoát 1 nếu không an toàn để đo
    python perf/stack/hostcheck.py clock-start <thư mục lượt>        ⇒ chạy nền đọc xung nhịp CPU Windows mỗi 5 s tới khi có file .stop-cpuclock
    python perf/stack/hostcheck.py clock-summary <thư mục lượt>      ⇒ cpu-clock.json (trung vị, p10, tỷ lệ mẫu bị hạ xung)

- Máy đo là laptop (i5-1135G7, 4 core vật lý × 2 luồng): hai luồng của cùng một core chia nhau bộ thực thi, nên cpuset hệ thống / công cụ phải
  cắt đúng ranh giới core; trước 05/11 (bố cục "legacy" 0-4 / 5-7) k6 và bộ quan sát dùng chung core vật lý 2 với hệ thống.
- WSL2 không ghim vCPU vào luồng vật lý của Windows: ranh giới là cấu trúc Linux trong VM Docker nhìn thấy, không phải bảo đảm tuyệt đối.
- Laptop hạ xung khi nóng, khi chạy pin hoặc ở chế độ nguồn tiết kiệm: lượt đo phải cắm sạc; chế độ khác "Best performance" chỉ cảnh báo
  (đổi chế độ là việc của người dùng); xung nhịp thực tế ghi theo lượt để phát hiện lượt bị hạ xung.
"""
from __future__ import annotations

import argparse
import csv
import json
import shutil
import statistics
import subprocess
import sys
import time
from pathlib import Path

STOP_FILE = ".stop-cpuclock"
CLOCK_FILE = "cpu-clock.csv"
SUMMARY_FILE = "cpu-clock.json"
CLOCK_INTERVAL_S = 5
THROTTLE_RATIO = 0.8          # mẫu có xung thực tế < 80 % trung vị của lượt = bị hạ xung
MEMORY_REFUSE_RATIO = 0.95    # trần RAM các container perf + phần đang dùng của container khác vượt 95 % RAM VM Docker ⇒ không đo
MIN_FREE_DISK_GB = 5
LEGACY_LAYOUT = ("0-4", "5-7")

# Chế độ nguồn (power mode overlay) khi cắm sạc: ActiveOverlayAcPowerScheme, GUID giống nhau trên mọi máy Windows
POWER_MODES = {
    "961cc777-2547-4f9d-8174-7d86181b8a7a": "Best power efficiency",
    "00000000-0000-0000-0000-000000000000": "Balanced",
    "3af9b8d9-7c97-431d-ad78-34a8bfea439f": "Better performance",
    "ded574b5-45a0-4f42-8737-46345c09c238": "Best performance",
}
PERF_MEMORY_CONTAINERS = ("perf-postgres", "perf-tadss-postgres", "perf-redis", "perf-mailpit", "perf-fake-llm", "perf-tadss", "perf-ai",
                          "perf-be", "perf-k6")


def cpu_range(cpus: list[int]) -> str:
    """[0, 1, 2, 5] ⇒ "0-2,5" (dạng cpuset của Docker)."""
    groups: list[list[int]] = []
    for cpu in sorted(cpus):
        if groups and groups[-1][-1] == cpu - 1:
            groups[-1].append(cpu)
        else:
            groups.append([cpu])
    return ",".join(str(g[0]) if len(g) == 1 else f"{g[0]}-{g[-1]}" for g in groups)


def physical_layout(core_of: dict[int, int]) -> tuple[str, str]:
    """Chia luồng theo core vật lý: công cụ đo nhận core cuối (đủ hai luồng), hệ thống nhận các core còn lại.
    Dưới 4 core vật lý hoặc không có siêu phân luồng thì giữ bố cục cũ (không đủ chỗ cắt đúng ranh giới mà vẫn chừa CPU cho hệ thống)."""
    cores: dict[int, list[int]] = {}
    for cpu, core in core_of.items():
        cores.setdefault(core, []).append(cpu)
    if len(cores) < 4 or any(len(threads) < 2 for threads in cores.values()):
        return LEGACY_LAYOUT
    last = max(cores, key=lambda core: min(cores[core]))
    system = [cpu for core, threads in cores.items() if core != last for cpu in threads]
    return cpu_range(system), cpu_range(cores[last])


def read_topology() -> dict[int, int]:
    """Luồng → core vật lý như VM Docker nhìn thấy (/sys trong một container alpine dùng một lần)."""
    script = "for c in /sys/devices/system/cpu/cpu[0-9]*; do echo ${c##*cpu} $(cat $c/topology/core_id); done"
    out = subprocess.run(["docker", "run", "--rm", "alpine", "sh", "-c", script], capture_output=True, text=True, check=True).stdout
    return {int(cpu): int(core) for cpu, core in (line.split() for line in out.splitlines() if line.strip())}


def power_mode_name(guid: str | None) -> str:
    return POWER_MODES.get((guid or "").strip().lower(), f"không rõ ({guid})" if guid else "không đọc được")


def _powershell(command: str) -> str:
    return subprocess.run(["powershell", "-NoProfile", "-Command", command], capture_output=True, text=True, timeout=60).stdout.strip()


def read_power() -> dict:
    """Nguồn điện và chế độ nguồn khi cắm sạc (Windows, không cần quyền admin). BatteryStatus 2 = đang cắm sạc."""
    battery = _powershell("(Get-CimInstance Win32_Battery | Select-Object -First 1).BatteryStatus")
    overlay = _powershell("(Get-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes')"
                          ".ActiveOverlayAcPowerScheme")
    has_battery = battery != ""
    return {"hasBattery": has_battery, "onAc": (not has_battery) or battery == "2", "batteryStatus": battery or None,
            "powerModeAc": power_mode_name(overlay), "powerModeAcGuid": overlay or None}


def memory_check(total_bytes: int, perf_limits_bytes: int, other_usage_bytes: int) -> dict:
    """Trần RAM các container perf + RAM đang dùng của container khác so với RAM VM Docker."""
    need = perf_limits_bytes + other_usage_bytes
    return {"vmBytes": total_bytes, "perfLimitsBytes": perf_limits_bytes, "otherUsageBytes": other_usage_bytes,
            "ratio": round(need / total_bytes, 3), "ok": need <= MEMORY_REFUSE_RATIO * total_bytes}


K6_MEMORY_BYTES = 2 * 1024**3  # mem_limit của perf-k6 trong compose; container chỉ tồn tại trong lúc chạy nên luôn cộng sẵn


def _docker_memory() -> dict:
    total = int(subprocess.run(["docker", "info", "--format", "{{.MemTotal}}"], capture_output=True, text=True, check=True).stdout)
    limits = K6_MEMORY_BYTES
    for name in PERF_MEMORY_CONTAINERS:
        if name == "perf-k6":
            continue
        out = subprocess.run(["docker", "inspect", "--format", "{{.HostConfig.Memory}}", name], capture_output=True, text=True)
        limits += int(out.stdout.strip() or 0) if out.returncode == 0 else 0
    stats = subprocess.run(["docker", "stats", "--no-stream", "--format", "{{.Name}} {{.MemUsage}}"], capture_output=True, text=True).stdout
    other = sum(_parse_size(usage.split("/")[0].strip()) for name, _, usage in (line.partition(" ") for line in stats.splitlines())
                if name not in PERF_MEMORY_CONTAINERS)
    return memory_check(total, limits, other)


def _parse_size(text: str) -> int:
    units = {"B": 1, "KiB": 1024, "MiB": 1024**2, "GiB": 1024**3, "kB": 1000, "MB": 1000**2, "GB": 1000**3}
    for unit in sorted(units, key=len, reverse=True):
        if text.endswith(unit):
            return int(float(text[: -len(unit)]) * units[unit])
    return 0


def preflight(allow_battery: bool, docker_data: Path) -> tuple[dict, list[str], list[str]]:
    """(kết quả, lý do từ chối, cảnh báo)."""
    power = read_power()
    memory = _docker_memory()
    free_gb = round(shutil.disk_usage(docker_data).free / 1024**3, 1)
    refuse, warn = [], []
    if not power["onAc"] and not allow_battery:
        refuse.append("máy đang chạy pin: cắm sạc rồi chạy lại (hoặc --allow-battery, lượt đó không dùng để kết luận)")
    if power["powerModeAc"] != "Best performance":
        warn.append(f"chế độ nguồn khi cắm sạc là '{power['powerModeAc']}', nên đặt Best performance (Settings ⇒ System ⇒ Power)")
    if not memory["ok"]:
        refuse.append(f"RAM VM Docker không đủ: cần {memory['ratio']:.0%} (> {MEMORY_REFUSE_RATIO:.0%}); tắt container khác trước khi đo")
    if free_gb < MIN_FREE_DISK_GB:
        refuse.append(f"ổ chứa dữ liệu Docker còn {free_gb} GB (< {MIN_FREE_DISK_GB} GB)")
    return {"power": power, "memory": memory, "dockerDiskFreeGb": free_gb}, refuse, warn


CLOCK_SCRIPT = (
    "$out = '{out}'; $stop = '{stop}'; "
    "while (-not (Test-Path $stop)) {{ "
    "$s = (Get-Counter -Counter '\\Processor Information(_Total)\\Processor Frequency','\\Processor Information(_Total)\\% Processor Performance' "
    "-SampleInterval {interval} -MaxSamples 1).CounterSamples; "
    "$line = '{{0}},{{1}},{{2}}' -f [DateTimeOffset]::UtcNow.ToUnixTimeSeconds(), $s[0].CookedValue, [math]::Round($s[1].CookedValue, 1); "
    "Add-Content -Path $out -Value $line }}"
)


def clock_start(run_dir: Path) -> None:
    """Một tiến trình PowerShell đọc bộ đếm Windows mỗi CLOCK_INTERVAL_S giây (chạy trên Windows, ngoài VM Docker)."""
    (run_dir / CLOCK_FILE).write_text("epoch,base_mhz,performance_pct\n", encoding="utf-8")
    script = CLOCK_SCRIPT.format(out=str(run_dir / CLOCK_FILE), stop=str(run_dir / STOP_FILE), interval=CLOCK_INTERVAL_S)
    subprocess.Popen(["powershell", "-NoProfile", "-Command", script], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def clock_summary(rows: list[tuple[float, float, float]]) -> dict:
    """Xung thực tế = xung danh định × % hiệu năng (bộ đếm Processor Information của Windows)."""
    effective = [base * pct / 100 for _, base, pct in rows]
    if not effective:
        return {"samples": 0, "note": "không có mẫu xung nhịp"}
    effective.sort()
    median = statistics.median(effective)
    throttled = sum(1 for mhz in effective if mhz < THROTTLE_RATIO * median)
    return {"samples": len(effective), "intervalS": CLOCK_INTERVAL_S, "effectiveMhz": {
        "median": round(median), "p10": round(effective[int(0.1 * (len(effective) - 1))]), "min": round(effective[0]),
        "max": round(effective[-1])}, "baseMhz": round(rows[0][1]),
        "throttledShare": round(throttled / len(effective), 3), "throttledRule": f"mẫu < {THROTTLE_RATIO:.0%} trung vị của lượt"}


def read_clock(run_dir: Path) -> list[tuple[float, float, float]]:
    path = run_dir / CLOCK_FILE
    if not path.exists():
        return []
    rows = []
    for row in csv.DictReader(path.open(encoding="utf-8")):
        try:
            rows.append((float(row["epoch"]), float(row["base_mhz"]), float(row["performance_pct"])))
        except (KeyError, TypeError, ValueError):
            continue
    return rows


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    layout = sub.add_parser("layout"); layout.add_argument("mode", nargs="?", default="physical", choices=["physical", "legacy"])
    sub.add_parser("topology")
    pre = sub.add_parser("preflight"); pre.add_argument("--allow-battery", action="store_true")
    pre.add_argument("--docker-data", default="D:/")
    for name in ("clock-start", "clock-summary"):
        sub.add_parser(name).add_argument("run_dir")
    args = parser.parse_args(argv)

    if args.command == "layout":
        system, tools = LEGACY_LAYOUT if args.mode == "legacy" else physical_layout(read_topology())
        print(system, tools)
    elif args.command == "topology":
        print(json.dumps({str(cpu): core for cpu, core in sorted(read_topology().items())}))
    elif args.command == "preflight":
        result, refuse, warn = preflight(args.allow_battery, Path(args.docker_data))
        for message in warn:
            print(f"hostcheck: CẢNH BÁO {message}", file=sys.stderr)
        for message in refuse:
            print(f"hostcheck: DỪNG {message}", file=sys.stderr)
        print(json.dumps(result, ensure_ascii=False))
        return 1 if refuse else 0
    elif args.command == "clock-start":
        clock_start(Path(args.run_dir))
    else:
        run_dir = Path(args.run_dir)
        (run_dir / STOP_FILE).touch()
        time.sleep(CLOCK_INTERVAL_S + 2)  # để vòng PowerShell thấy file dừng
        summary = clock_summary(read_clock(run_dir))
        (run_dir / SUMMARY_FILE).write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps(summary, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
