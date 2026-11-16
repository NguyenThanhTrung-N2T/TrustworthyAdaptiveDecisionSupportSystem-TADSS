"""Các con số về Đồ án 2 mà đề cương khóa luận dẫn ở Mục 1.4: lùi về mặc định, bộ nhớ đệm, số quyết định.

Nguồn: bộ minh chứng của lượt đánh giá đầy đủ `EV-20261017-EVAL-full-r1` (thư mục `evidence/`, không commit vì nặng; được sinh
bằng `bash tools/evaluation/run.sh full`, docs/guides/RUNBOOK.md). Chương trình đọc hai tệp bản ghi quyết định trong `raw/d1/`,
kiểm SHA-256 và số dòng theo `manifest.json` của bộ minh chứng, rồi tính:
- số quyết định của từng tệp (stack chính và stack chạy dữ liệu khách sạn);
- số lần lùi về mặc định theo nguyên nhân, kèm trung vị thời gian xử lý của pipeline;
- số lần dùng lại được kết quả trong bộ nhớ đệm.
Chạy: `python -I tools/research/runtime_facts.py [--pack evidence/EV-20261017-EVAL-full-r1] [--check]`.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_PACK = REPO_ROOT / "evidence" / "EV-20261017-EVAL-full-r1"
DEFAULT_RESULTS = Path(__file__).resolve().parent / "results" / "runtime_facts.json"
AUDIT_FILES = ("raw/d1/decision_audit.csv", "raw/d1/decision_audit_portability.csv")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def median(values: list[int]) -> int:
    ordered = sorted(values)
    return ordered[len(ordered) // 2]


def summarize(rows: list[dict]) -> dict:
    """Số quyết định, số lần lùi về mặc định theo nguyên nhân (mã lý do) và trung vị thời gian, số lần trúng bộ nhớ đệm."""
    fallback: dict[str, list[int]] = defaultdict(list)
    for r in rows:
        if r["execution_type"] == "FALLBACK":
            fallback[r["reason_code"]].append(int(r["execution_time_ms"]))
    return {
        "decisions": len(rows),
        "fallback_total": sum(len(v) for v in fallback.values()),
        "fallback_by_reason": {k: {"count": len(v), "median_ms": median(v)} for k, v in sorted(fallback.items(), key=lambda kv: -len(kv[1]))},
        "cache_hits": sum(1 for r in rows if r["cache_hit"].strip().lower() in ("true", "t", "1")),
    }


def build_report(pack: Path) -> dict:
    manifest = json.loads((pack / "manifest.json").read_text(encoding="utf-8"))
    expected = {f["path"]: f for f in manifest["files"]}
    out: dict = {"run_id": manifest["runId"], "created_at_utc": manifest["createdAtUtc"], "files": {}, "results": {}}
    csv.field_size_limit(10**9)
    for relative in AUDIT_FILES:
        path = pack / relative
        actual = sha256_file(path)
        if actual != expected[relative]["sha256"]:
            raise ValueError(f"{relative}: SHA-256 {actual} khác manifest")
        with path.open(encoding="utf-8", newline="") as handle:
            rows = list(csv.DictReader(handle))
        if len(rows) != expected[relative]["rows"]:
            raise ValueError(f"{relative}: {len(rows)} dòng, manifest ghi {expected[relative]['rows']}")
        out["files"][relative] = {"sha256": actual, "rows": len(rows)}
        out["results"][re.sub(r"\W+", "_", Path(relative).stem)] = summarize(rows)
    stacks = out["results"].values()
    out["results"]["all"] = {"decisions": sum(s["decisions"] for s in stacks), "cache_hits": sum(s["cache_hits"] for s in stacks)}
    return out


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--pack", type=Path, default=DEFAULT_PACK)
    parser.add_argument("--out", type=Path, default=DEFAULT_RESULTS)
    parser.add_argument("--check", action="store_true", help="tính lại và so với tệp kết quả đã có, không ghi đè")
    args = parser.parse_args(argv)
    sys.stdout.reconfigure(encoding="utf-8")
    report = build_report(args.pack)
    if args.check:
        saved = json.loads(args.out.read_text(encoding="utf-8"))
        if saved != report:
            print(f"KHÁC với {args.out}", file=sys.stderr)
            return 1
        print(f"khớp {args.out}")
        return 0
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"đã ghi {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
