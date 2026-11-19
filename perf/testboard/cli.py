"""Lệnh của testboard. Chạy từ gốc repo TADSS-Runtime: `python perf/board.py <lệnh>` (hướng dẫn: docs/guides/RUNBOOK.md)."""
from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path

from testboard import board, catalog, ci, dashboards, paths, report
from testboard.model import STATE_NAMES, SourceResult
from testboard.sources import manual_errors, mark_limited, read_evidence, read_faults, read_junit, read_k6, read_manual, read_playwright, read_sc6, read_trx


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="python perf/board.py", description="Kết quả kiểm thử: báo cáo tổng kết perf/results/BAO_CAO_MOI_NHAT.html + dashboard Kiểm thử")
    parser.add_argument("--no-push", action="store_true", help="chỉ ghi perf/results/, không đẩy lên Pushgateway (Grafana chưa bật)")
    sub = parser.add_subparsers(dest="command", required=True)
    # Hai lệnh thường dùng; các lệnh nạp bên dưới chỉ cho trường hợp riêng (perf.sh tự gọi k6 / faults sau mỗi lượt đo)
    sub.add_parser("update", help="việc thường ngày: tải kết quả CI ba repo + nạp ghi nhận kiểm tay + xuất báo cáo tổng kết")
    sub.add_parser("report", help="chỉ xuất lại báo cáo tổng kết từ kết quả đã nạp")
    sub.add_parser("catalog", help="sinh lại tests/catalog.yaml từ methodology")
    sub.add_parser("dashboards", help="sinh lại perf/grafana/dashboards/*.json")
    sub.add_parser("check", help="kiểm catalog và dashboard khớp nguồn, mô tả panel đủ 6 ý (CI)")
    sub.add_parser("push", help="đẩy lại bảng hiện có (sau khi Grafana khởi động lại)")
    ci_cmd = sub.add_parser("ci", help="tải kết quả test của lượt CI gần nhất từ GitHub (cần gh đã đăng nhập)")
    ci_cmd.add_argument("repo", choices=["be", "tadss", "ai", "all"])
    ci_cmd.add_argument("--branch", help="mặc định: nhánh CI của từng repo (BE, TADSS develop; AI master)")
    trx = sub.add_parser("trx", help="nạp trx + catalog-index.*.json chạy trên máy (BE hoặc TADSS)")
    trx.add_argument("system", choices=["be", "tadss"])
    trx.add_argument("directory", type=Path)
    junit = sub.add_parser("junit", help="nạp JUnit XML của pytest AI chạy trên máy")
    junit.add_argument("file", type=Path)
    evidence = sub.add_parser("evidence", help="nạp Evidence Pack của evaluator (thư mục evidence/<run_id>)")
    evidence.add_argument("pack", type=Path)
    k6 = sub.add_parser("k6", help="nạp kết quả k6 cho một mã SP: summary JSON, thư mục lượt hoặc bản gộp pooled-*.json (nhiều file ⇒ giữ xấu nhất)")
    k6.add_argument("code")
    k6.add_argument("files", type=Path, nargs="+", metavar="file")
    k6.add_argument("--limited", metavar="LÝ_DO", help="lượt INFRA_LIMITED/ABORTED: không dùng để kết luận (P6-47)")
    faults = sub.add_parser("faults", help="nạp FI-11…14 từ thư mục lượt SP-09 có faults.json (+ sc6.json, fault-latency.json)")
    faults.add_argument("run_dirs", type=Path, nargs="+", metavar="run_dir")
    manual = sub.add_parser("manual", help="nạp ghi nhận kiểm thủ công (mặc định tests/manual-results.yaml)")
    manual.add_argument("file", type=Path, nargs="?", default=None)
    sc6 = sub.add_parser("sc6", help="nạp báo cáo bất biến booking SC6 của BE (SC6_OUTPUT)")
    sc6.add_argument("file", type=Path)
    playwright = sub.add_parser("playwright", help="nạp báo cáo JSON của Playwright (E2E, FE)")
    playwright.add_argument("file", type=Path)
    return parser


def _report(source: SourceResult, target: Path, merged: list[board.MergedCode]) -> None:
    counts: dict[str, int] = {}
    for result in source.codes.values():
        counts[STATE_NAMES[result.state]] = counts.get(STATE_NAMES[result.state], 0) + 1
    print(f"{source.source}: {len(source.codes)} mã {counts} · commit {source.commit or '-'} · lưu ở {target}")
    if source.ignored:
        print(f"  bỏ qua mã không có trong danh mục: {', '.join(source.ignored)}")
    for note in source.notes:
        print(f"  ghi chú: {note}")
    failed = [item.entry.code for item in merged if STATE_NAMES[item.state] == "failed"]
    print(f"  bảng hiện có {len(failed)} mã hỏng" + (f": {', '.join(failed[:12])}" if failed else ""))


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    if args.command == "catalog":
        print(f"tests/catalog.yaml: {len(catalog.write())} mã")
        return 0
    if args.command == "dashboards":
        for path in dashboards.write():
            print(path)
        return 0
    if args.command == "check":
        return _check()

    known = catalog.load()
    push_url = None if args.no_push else board.PUSHGATEWAY_URL
    if args.command == "report":
        print(f"Báo cáo: {report.write(known)}")
        return 0
    if args.command == "push":
        sources = board.load_state()
        board.push(board.exposition(board.merge(known, sources), sources))
        print(f"đã đẩy bảng của {len(sources)} nguồn")
        return 0

    codes = set(known)
    loads: list[tuple[SourceResult, list[Path]]] = []
    if args.command in ("ci", "update"):
        repos = ["be", "tadss", "ai"] if args.command == "update" or args.repo == "all" else [args.repo]
        for key in repos:
            source, directory = ci.download(ci.REPOS[key], getattr(args, "branch", None), codes)
            loads.append((source, [directory]))
    if args.command == "update":
        loads.append((read_manual(paths.MANUAL_FILE, codes, SourceResult("manual", run=paths.MANUAL_FILE.name)), [paths.MANUAL_FILE]))
    elif args.command == "trx":
        loads.append((read_trx(args.directory, codes, SourceResult(f"local-{args.system}", run="chạy trên máy")), [args.directory]))
    elif args.command == "junit":
        loads.append((read_junit(args.file, codes, SourceResult("local-ai", run="chạy trên máy")), [args.file]))
    elif args.command == "evidence":
        loads.append((read_evidence(args.pack, codes, SourceResult("evidence")), [args.pack / "manifest.json", args.pack / "report.html"]))
    elif args.command == "k6":
        source = SourceResult(f"k6-{args.code}", run=", ".join(path.stem if path.is_file() else path.name for path in args.files))
        for path in args.files:
            read_k6(path, args.code, codes, source, label=(path.stem if path.is_file() else path.name) if len(args.files) > 1 else "")
        raw = [path / name for path in args.files if path.is_dir() for name in ("summary.json", "verdict.json")] + [path for path in args.files if path.is_file()]
        loads.append((mark_limited(source, args.limited) if args.limited else source, raw))
    elif args.command == "faults":
        source = SourceResult("faults", run=", ".join(path.name for path in args.run_dirs))
        for run_dir in args.run_dirs:
            read_faults(run_dir, codes, source)
        raw = [run_dir / name for run_dir in args.run_dirs for name in ("faults.json", "fault-latency.json", "sc6.json", "verdict.json")]
        loads.append((source, [path for path in raw if path.is_file()]))
    elif args.command == "manual":
        manual_file = args.file or paths.MANUAL_FILE
        loads.append((read_manual(manual_file, codes, SourceResult("manual", run=manual_file.name)), [manual_file]))
    elif args.command == "sc6":
        loads.append((read_sc6(args.file, codes, SourceResult("sc6", run=args.file.stem)), [args.file]))
    elif args.command == "playwright":
        loads.append((read_playwright(args.file, codes, SourceResult("playwright", run=args.file.stem)), [args.file]))

    for source, raw in loads:
        target, merged = board.record(source, raw, known, push_url)
        _report(source, target, merged)
        if source.source.startswith("ci-"):   # bản tải CI nằm trong thư mục tạm của hệ thống, đã chép vào perf/results: xóa để không tồn đọng
            for directory in raw:
                shutil.rmtree(directory, ignore_errors=True)
    print(f"Báo cáo: {report.write(known)}")
    return 0


def _check() -> int:
    errors = []
    expected = catalog.render_yaml(catalog.build_entries(catalog.paths.METHODOLOGY.read_text(encoding="utf-8")))
    if not catalog.paths.CATALOG_FILE.is_file() or catalog.paths.CATALOG_FILE.read_text(encoding="utf-8") != expected:
        errors.append("tests/catalog.yaml lệch methodology: chạy `python perf/board.py catalog`")
    for system in ("BE", "TADSS", "AI"):
        path = catalog.paths.DASHBOARD_DIR / f"testboard-{system.lower()}.json"
        if not path.is_file() or path.read_text(encoding="utf-8") != dashboards.render(system):
            errors.append(f"{path.name} lệch dashboards.py: chạy `python perf/board.py dashboards`")
    errors += dashboards.check()
    errors += manual_errors(paths.MANUAL_FILE, set(catalog.load()))
    for error in errors:
        print(f"LỖI: {error}")
    if not errors:
        print("OK: catalog khớp methodology, 3 dashboard khớp nguồn, mọi panel có mô tả 6 ý, ghi nhận thủ công hợp lệ")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
