# Task 2.29 (plan 6C B.7.7): bằng chứng hiệu năng = file nhẹ của các lượt chọn + manifest SHA-256 của mọi file (cả mẫu thô không chép)
import hashlib
import json
import sys
from pathlib import Path

import pytest

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import perf_evidence as evidence


def _run(root: Path, name: str, files: dict[str, bytes]) -> Path:
    run = root / name
    run.mkdir(parents=True)
    for file, content in files.items():
        (run / file).write_bytes(content)
    return run


def test_collect_copies_light_files_and_hashes_every_file(tmp_path):
    run = _run(tmp_path / "k6", "20261111-224500-load-SP-01-p655-r1", {
        "verdict.json": b'{"status":"PASS"}', "metadata.json": b"{}", "raw.csv.gz": b"x" * 10, "summary.json": b"{}",
    })
    out = tmp_path / "evidence"
    manifest = evidence.collect("PERF-TEST", [run], out)

    copied = out / "PERF-TEST" / run.name
    assert sorted(p.name for p in copied.iterdir()) == ["metadata.json", "verdict.json"]
    files = manifest["runs"][0]["files"]
    assert files["raw.csv.gz"] == {"bytes": 10, "sha256": hashlib.sha256(b"x" * 10).hexdigest(), "copied": False}
    assert files["verdict.json"]["copied"] is True
    assert json.loads((out / "PERF-TEST" / "manifest.json").read_text(encoding="utf-8")) == manifest


def test_collect_copies_pooled_results_whole(tmp_path):
    pooled = tmp_path / "k6" / "pooled-20261111-224500-load-SP-01-p655-r1-3runs.json"
    pooled.parent.mkdir(parents=True)
    pooled.write_text('{"groups": {}}', encoding="utf-8")
    manifest = evidence.collect("PERF-TEST", [pooled], tmp_path / "evidence")
    assert (tmp_path / "evidence" / "PERF-TEST" / pooled.name).read_text(encoding="utf-8") == '{"groups": {}}'
    assert manifest["pooled"][0]["name"] == pooled.name


def test_collect_refuses_to_overwrite_an_existing_evidence_set(tmp_path):
    run = _run(tmp_path / "k6", "r", {"verdict.json": b"{}"})
    evidence.collect("PERF-TEST", [run], tmp_path / "evidence")
    with pytest.raises(FileExistsError):
        evidence.collect("PERF-TEST", [run], tmp_path / "evidence")


def test_verify_reports_changed_and_missing_copies(tmp_path):
    run = _run(tmp_path / "k6", "r", {"verdict.json": b'{"status":"PASS"}'})
    out = tmp_path / "evidence"
    evidence.collect("PERF-TEST", [run], out)
    assert evidence.verify(out / "PERF-TEST") == []
    (out / "PERF-TEST" / "r" / "verdict.json").write_text('{"status":"FAIL"}', encoding="utf-8")
    assert evidence.verify(out / "PERF-TEST") == ["r/verdict.json: SHA-256 lệch manifest"]
