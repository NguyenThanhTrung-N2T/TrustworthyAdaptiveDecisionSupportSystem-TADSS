"""Gộp mọi nguồn đã nạp thành trạng thái từng mã, từng nhóm, từng KPI; ghi `perf/results/` (bản gốc cho báo cáo) và đẩy metric lên Pushgateway.
Mỗi lần nạp chỉ thay kết quả của đúng nguồn đó (state.json giữ nguồn khác), rồi đẩy lại toàn bộ bảng: dashboard luôn thấy một bức tranh nhất quán."""
from __future__ import annotations

import json
import shutil
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

from testboard import paths
from testboard.catalog import CatalogEntry
from testboard.model import KPIS, LIMITED, PASSED, STATE_NAMES, UNTESTED, FAILED, SourceResult

PUSHGATEWAY_URL = "http://127.0.0.1:9096"
JOB = "testboard"
_KPI_LABELS = {
    "SC1": "Độ trễ bình thường", "SC2": "Chịu lỗi strategy", "SC3": "Lưu vết đầy đủ", "SC4": "Không mất/trùng duyệt",
    "SC5": "Tắt cờ an toàn", "SC6": "Toàn vẹn booking", "SC7": "Độc lập domain", "SC8": "Chất lượng bộ test",
}


@dataclass
class MergedCode:
    entry: CatalogEntry
    state: int
    source: str = ""
    commit: str = ""
    detail: str = ""
    report: str = ""


def load_state(path: Path | None = None) -> dict[str, SourceResult]:
    path = path or paths.STATE_FILE
    if not path.is_file():
        return {}
    return {name: SourceResult.from_dict(data) for name, data in json.loads(path.read_text(encoding="utf-8")).items()}


def save_state(sources: dict[str, SourceResult], path: Path | None = None) -> None:
    path = path or paths.STATE_FILE
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps({name: result.to_dict() for name, result in sorted(sources.items())}, ensure_ascii=False, indent=2), encoding="utf-8")


def archive(source: SourceResult, raw_files: list[Path], results_dir: Path | None = None) -> Path:
    """Bản gốc của lượt nạp cho báo cáo: perf/results/<thời điểm>-<nguồn>/ (file gốc + summary.json)."""
    results_dir = results_dir or paths.RESULTS_DIR
    stamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    target = results_dir / f"{stamp}-{source.source}"
    target.mkdir(parents=True, exist_ok=True)
    for raw in raw_files:
        if raw.is_dir():
            shutil.copytree(raw, target / raw.name, dirs_exist_ok=True)
        elif raw.is_file():
            shutil.copy2(raw, target / raw.name)
    (target / "summary.json").write_text(json.dumps(source.to_dict(), ensure_ascii=False, indent=2), encoding="utf-8")
    return target


def merge(catalog: dict[str, CatalogEntry], sources: dict[str, SourceResult]) -> list[MergedCode]:
    """Mỗi mã lấy nguồn "xấu" nhất có kết quả (hỏng > hạn chế > đạt): một nguồn đỏ là đủ để mã đỏ."""
    merged = []
    for code, entry in catalog.items():
        best = MergedCode(entry, UNTESTED)
        for source in sources.values():
            result = source.codes.get(code)
            if result is None or result.state == UNTESTED:
                continue
            if best.state == UNTESTED or result.state > best.state:
                best = MergedCode(entry, result.state, source.source, source.commit, result.detail, source.report)
        merged.append(best)
    return merged


def group_state(states: list[int]) -> int:
    """Ô nhóm: đỏ nếu có mã hỏng; vàng nếu có mã hạn chế hoặc mới chạy một phần; xanh khi đạt hết; xám khi chưa chạy mã nào."""
    if FAILED in states:
        return FAILED
    if LIMITED in states or (PASSED in states and UNTESTED in states):
        return LIMITED
    return PASSED if states and all(state == PASSED for state in states) else UNTESTED


def _label(value: object) -> str:
    return str(value).replace("\\", "\\\\").replace("\n", " ").replace('"', '\\"')


def _line(name: str, labels: dict[str, object], value: float) -> str:
    return f'{name}{{{",".join(f"{key}={chr(34)}{_label(val)}{chr(34)}" for key, val in labels.items())}}} {value}'


def exposition(merged: list[MergedCode], sources: dict[str, SourceResult]) -> str:
    """Định dạng văn bản Prometheus. Nhãn `systems` dạng `,BE,TADSS,` để lọc theo tab bằng regex `.*,BE,.*`."""
    lines = ["# TYPE smashcourt_test_code_state gauge"]
    # Ô nhóm tính riêng cho từng hệ thống: FI-11…14 (tích hợp phía BE) không làm ô FI của tab TADSS thiếu mã
    groups: dict[tuple[str, str], list[MergedCode]] = {}
    for item in merged:
        entry = item.entry
        systems = "," + ",".join(entry.systems) + ","
        for system in entry.systems:
            groups.setdefault((system, entry.group), []).append(item)
        lines.append(_line("smashcourt_test_code_state", {
            "code": entry.code, "group": entry.group, "group_label": entry.group_label, "systems": systems, "title": entry.title,
            "state": STATE_NAMES[item.state], "source": item.source, "commit": item.commit, "detail": item.detail,
            "report": item.report, "link": entry.link, "tool": entry.tool,
        }, item.state))

    lines.append("# TYPE smashcourt_test_group_state gauge")
    for (system, group), items in groups.items():
        states = [item.state for item in items]
        passed, total = states.count(PASSED), len(states)
        entry = items[0].entry
        lines.append(_line("smashcourt_test_group_state", {
            "group": group, "group_label": entry.group_label, "system": system,
            "passed": passed, "failed": states.count(FAILED), "limited": states.count(LIMITED), "untested": states.count(UNTESTED), "total": total,
            "tile": f"{entry.group_label} · {passed}/{total}",
        }, group_state(states)))

    lines.append("# TYPE smashcourt_test_kpi_state gauge")
    for kpi in KPIS:
        found = [(name, source.kpis[kpi]) for name, source in sorted(sources.items()) if kpi in source.kpis]
        name, result = max(found, key=lambda pair: pair[1].state) if found else ("", None)
        lines.append(_line("smashcourt_test_kpi_state", {
            "kpi": kpi, "label": _KPI_LABELS[kpi], "verdict": result.verdict if result else "CHƯA CHẠY",
            "value": result.value if result else "", "source": name, "tile": f"{kpi} · {_KPI_LABELS[kpi]}",
        }, result.state if result else UNTESTED))

    lines.append("# TYPE smashcourt_test_source_loaded_seconds gauge")
    for name, source in sorted(sources.items()):
        loaded = datetime.fromisoformat(source.loaded_at).timestamp() if source.loaded_at else 0
        lines.append(_line("smashcourt_test_source_loaded_seconds", {"source": name, "commit": source.commit or "-", "run": source.run or "-", "report": source.report or "-"}, loaded))
    return "\n".join(lines) + "\n"


def push(body: str, url: str = PUSHGATEWAY_URL) -> None:
    """PUT thay toàn bộ nhóm `job=testboard` trên Pushgateway (nguồn cũ không còn sót metric lạc)."""
    request = urllib.request.Request(f"{url}/metrics/job/{JOB}", data=body.encode("utf-8"), method="PUT", headers={"Content-Type": "text/plain; version=0.0.4"})
    with urllib.request.urlopen(request, timeout=10) as response:
        if response.status >= 300:
            raise RuntimeError(f"Pushgateway trả {response.status}")


def record(source: SourceResult, raw_files: list[Path], catalog: dict[str, CatalogEntry], push_url: str | None = PUSHGATEWAY_URL) -> tuple[Path, list[MergedCode]]:
    """Một lượt nạp: đóng dấu thời gian, lưu bản gốc, cập nhật state.json, đẩy bảng (bỏ qua đẩy khi push_url=None)."""
    source.loaded_at = datetime.now(timezone.utc).isoformat(timespec="seconds")
    target = archive(source, raw_files)
    if not source.report:
        source.report = target.relative_to(paths.REPO_ROOT).as_posix() if target.is_relative_to(paths.REPO_ROOT) else str(target)
        (target / "summary.json").write_text(json.dumps(source.to_dict(), ensure_ascii=False, indent=2), encoding="utf-8")
    sources = load_state()
    sources[source.source] = source
    save_state(sources)
    merged = merge(catalog, sources)
    if push_url:
        push(exposition(merged, sources), push_url)
    return target, merged
