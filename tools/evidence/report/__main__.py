"""CLI dựng lại báo cáo từ một Evidence Pack: `python -m evidence.report <thư mục pack>` (trong image evaluator: `docker compose ... run --rm --entrypoint python evaluator -m evidence.report /evidence/<run_id>`).
Mã thoát: 0 nếu EV-1…EV-5 đều đạt, 1 nếu có kiểm tra không đạt (báo cáo vẫn được ghi, kèm cảnh báo không dùng làm minh chứng), 2 nếu thư mục không phải Evidence Pack."""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from evidence.report import rebuild


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="python -m evidence.report", description="Dựng lại report.html từ một Evidence Pack có sẵn (chạy lại EV-1…EV-5, không đổi file thô).")
    parser.add_argument("pack", type=Path, help="thư mục Evidence Pack (evidence/<run_id>)")
    arguments = parser.parse_args(argv)
    try:
        checks, report = rebuild.rebuild_report(arguments.pack)
    except rebuild.PackError as error:
        print(f"LỖI: {error}", file=sys.stderr)
        return 2
    for check in checks:
        print(f"{check.code}: {'đạt' if check.passed else 'KHÔNG ĐẠT'} — {check.detail}")
    print(f"Báo cáo: {report}")
    return 0 if all(check.passed for check in checks) else 1


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):  # console Windows mặc định không phải UTF-8
        stream.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main())
