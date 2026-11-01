"""Tải kết quả test của lượt CI gần nhất (đã chạy xong) từ GitHub bằng `gh` (task 2.3): BE, TADSS (trx + chỉ mục mã), AI (JUnit XML)."""
from __future__ import annotations

import json
import subprocess
import tempfile
from dataclasses import dataclass
from pathlib import Path

from testboard.model import SourceResult
from testboard.sources import read_junit, read_trx

OWNER = "NguyenThanhTrung-N2T"


@dataclass(frozen=True)
class CiRepo:
    key: str
    repo: str
    workflow: str
    artifact: str
    kind: str  # trx | junit


REPOS = {
    "be": CiRepo("be", f"{OWNER}/SmashCourt-BE", "BE CI", "be-test-results", "trx"),
    "tadss": CiRepo("tadss", f"{OWNER}/TrustworthyAdaptiveDecisionSupportSystem-TADSS", "TADSS Runtime CI", "test-results-and-coverage", "trx"),
    "ai": CiRepo("ai", f"{OWNER}/SmashCourt-AI", "AI CI", "ai-test-results", "junit"),
}


def _gh(*args: str) -> str:
    return subprocess.run(["gh", *args], check=True, capture_output=True, text=True, encoding="utf-8").stdout


def latest_run(repo: CiRepo, branch: str) -> dict:
    """Lượt gần nhất đã chạy xong của workflow trên nhánh (kể cả đỏ: lượt đỏ chính là thứ cần thấy trên dashboard)."""
    runs = json.loads(_gh("run", "list", "-R", repo.repo, "-w", repo.workflow, "-b", branch, "--status", "completed", "-L", "1",
                          "--json", "databaseId,headSha,conclusion,url,createdAt"))
    if not runs:
        raise RuntimeError(f"{repo.repo}: chưa có lượt '{repo.workflow}' nào chạy xong trên nhánh {branch}")
    return runs[0]


def download(repo: CiRepo, branch: str, known: set[str]) -> tuple[SourceResult, Path]:
    """Tải artifact của lượt gần nhất vào thư mục tạm và đọc; trả kết quả và thư mục (để lưu bản gốc vào perf/results)."""
    run = latest_run(repo, branch)
    target = Path(tempfile.mkdtemp(prefix=f"testboard-{repo.key}-"))
    _gh("run", "download", str(run["databaseId"]), "-R", repo.repo, "-n", repo.artifact, "-D", str(target))
    source = SourceResult(f"ci-{repo.key}", commit=run["headSha"][:7], run=f"{repo.workflow} #{run['databaseId']} ({run['conclusion']})", report=run["url"])
    if repo.kind == "trx":
        return read_trx(target, known, source), target
    junit = sorted(target.rglob("*.xml"))
    if not junit:
        raise RuntimeError(f"{repo.repo}: artifact {repo.artifact} không có file JUnit XML")
    return read_junit(junit[0], known, source), target
