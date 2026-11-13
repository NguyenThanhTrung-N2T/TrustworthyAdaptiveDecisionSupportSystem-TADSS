"""Bằng chứng của báo cáo hiệu năng (Epic 6C, task 2.29; plan 6C B.7.7).

    python perf/stack/perf_evidence.py collect <mã bộ> <thư mục lượt hoặc file pooled-*.json>...
    python perf/stack/perf_evidence.py verify perf/evidence/<mã bộ>

`collect` chép các file nhẹ của mỗi lượt (kết luận, cấu hình, tài nguyên, xung nhịp, lỗi tiêm, SC6, nút cổ chai) và nguyên file gộp vào
`perf/evidence/<mã bộ>/`, rồi ghi `manifest.json` gồm SHA-256 + kích thước của **mọi** file trong thư mục lượt. File nặng (mẫu thô
`raw.csv.gz`, summary k6, mẫu PostgreSQL, log Hangfire) không vào repo nhưng có hash để đối chiếu với `perf/results/k6/` gốc.
`verify` kiểm các file đã chép còn khớp manifest. Không ghi đè một bộ đã có.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "evidence"
COPIED = {"verdict.json", "metadata.json", "schedule.json", "cpu-clock.json", "resources.json", "faults.json", "sc6.json",
          "bottleneck.json", "k6-exit", "fault-latency.json"}  # fault-latency.json: độ trễ trong khoảng lỗi (faults.py latency, P6-57)


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def collect(set_id: str, sources: list[Path], out_root: Path = ROOT) -> dict:
    target = out_root / set_id
    if target.exists():
        raise FileExistsError(f"{target} đã có, không ghi đè bộ bằng chứng")
    target.mkdir(parents=True)
    manifest: dict = {"id": set_id, "createdUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"), "runs": [], "pooled": []}
    for source in sources:
        if source.is_file():
            shutil.copy2(source, target / source.name)
            manifest["pooled"].append({"name": source.name, "bytes": source.stat().st_size, "sha256": _sha256(source)})
            continue
        files = {}
        for path in sorted(p for p in source.iterdir() if p.is_file() and not p.name.startswith(".")):
            copied = path.name in COPIED
            if copied:
                (target / source.name).mkdir(exist_ok=True)
                shutil.copy2(path, target / source.name / path.name)
            files[path.name] = {"bytes": path.stat().st_size, "sha256": _sha256(path), "copied": copied}
        manifest["runs"].append({"runId": source.name, "files": files})
    (target / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    return manifest


def verify(set_dir: Path) -> list[str]:
    manifest = json.loads((set_dir / "manifest.json").read_text(encoding="utf-8"))
    problems = []
    entries = [(f"{run['runId']}/{name}", info) for run in manifest["runs"] for name, info in run["files"].items() if info["copied"]]
    entries += [(item["name"], item) for item in manifest["pooled"]]
    for relative, info in entries:
        path = set_dir / relative
        if not path.is_file():
            problems.append(f"{relative}: thiếu file")
        elif _sha256(path) != info["sha256"]:
            problems.append(f"{relative}: SHA-256 lệch manifest")
    return problems


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    c = sub.add_parser("collect")
    c.add_argument("set_id")
    c.add_argument("sources", nargs="+", type=Path)
    v = sub.add_parser("verify")
    v.add_argument("set_dir", type=Path)
    args = parser.parse_args(argv)
    if args.command == "collect":
        manifest = collect(args.set_id, args.sources)
        print(f"perf_evidence: {len(manifest['runs'])} lượt, {len(manifest['pooled'])} file gộp ⇒ {ROOT / args.set_id}")
        return 0
    problems = verify(args.set_dir)
    print("\n".join(problems) if problems else "perf_evidence: mọi file khớp manifest")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
