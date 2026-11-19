"""Tải kết quả test của lượt CI gần nhất (đã chạy xong) từ GitHub bằng `gh` (task 2.3): BE, TADSS (trx + chỉ mục mã), AI (JUnit XML);
cộng kết luận các bước CI không phải test (plan 6C, B4) đọc từ `gh run view --json jobs`."""
from __future__ import annotations

import json
import subprocess
import tempfile
from dataclasses import dataclass, field
from pathlib import Path

from testboard.model import SourceResult
from testboard.sources import read_ci_steps, read_junit, read_trx

OWNER = "NguyenThanhTrung-N2T"


@dataclass(frozen=True)
class CiRepo:
    key: str
    repo: str
    workflow: str
    artifact: str
    kind: str  # trx | junit
    branch: str  # nhánh CI đẩy image và chạy test (AI dùng master)
    steps: dict[str, tuple[str, ...]] = field(default_factory=dict)  # tên step trong workflow ⇒ mã (bước kiểm không phải test)


# Tên step phải khớp `name:` trong .github/workflows của từng repo; đổi tên step thì sửa ở đây (lệch ⇒ `board.py ci` in "bước CI không thấy")
_IMAGE_STEP = "Build, check and push image"  # tools/ci/build-image.sh ⇒ check-image.sh: không file bí mật (OPS-01), non-root (OPS-02)
REPOS = {
    "be": CiRepo("be", f"{OWNER}/SmashCourt-BE", "BE CI", "be-test-results", "trx", "develop", {
        "Check Vulnerable Packages": ("SEC-BE-7",),
        _IMAGE_STEP: ("OPS-01", "OPS-02"),
    }),
    "tadss": CiRepo("tadss", f"{OWNER}/TrustworthyAdaptiveDecisionSupportSystem-TADSS", "TADSS Runtime CI", "test-results-and-coverage", "trx", "develop", {
        "Check Vulnerable Packages": ("SEC-7",),
        _IMAGE_STEP: ("OPS-01", "OPS-02"),
        "Verify Expose Configuration": ("NG-1", "NG-2", "NG-3", "NG-5", "SEC-6"),  # tools/expose/verify-expose.sh
        "Verify Observability": ("OB-2", "OB-4"),  # tools/observability/verify-observability.sh
    }),
    "ai": CiRepo("ai", f"{OWNER}/SmashCourt-AI", "AI CI", "ai-test-results", "junit", "master", {
        "Check vulnerable packages (pip-audit)": ("SEC-AI-4",),
        _IMAGE_STEP: ("OPS-01", "OPS-02", "SEC-AI-3"),  # check-image.sh của AI còn kiểm image không chứa .env
    }),
}


def _gh(*args: str) -> str:
    try:
        return subprocess.run(["gh", *args], check=True, capture_output=True, text=True, encoding="utf-8").stdout
    except subprocess.CalledProcessError as error:
        raise RuntimeError(f"gh {' '.join(args[:2])} lỗi: {(error.stderr or error.stdout or '').strip()}") from None


def latest_run(repo: CiRepo, branch: str | None) -> dict:
    """Lượt gần nhất đã chạy xong của workflow trên nhánh (mặc định nhánh CI của repo; kể cả đỏ: lượt đỏ chính là thứ cần thấy trên dashboard)."""
    branch = branch or repo.branch
    runs = json.loads(_gh("run", "list", "-R", repo.repo, "-w", repo.workflow, "-b", branch, "--status", "completed", "-L", "1",
                          "--json", "databaseId,headSha,conclusion,url,createdAt"))
    if not runs:
        raise RuntimeError(f"{repo.repo}: chưa có lượt '{repo.workflow}' nào chạy xong trên nhánh {branch}")
    return runs[0]


def download(repo: CiRepo, branch: str | None, known: set[str]) -> tuple[SourceResult, Path]:
    """Tải artifact của lượt gần nhất vào thư mục tạm và đọc; trả kết quả và thư mục (để lưu bản gốc vào perf/results)."""
    run = latest_run(repo, branch)
    target = Path(tempfile.mkdtemp(prefix=f"testboard-{repo.key}-"))
    _gh("run", "download", str(run["databaseId"]), "-R", repo.repo, "-n", repo.artifact, "-D", str(target))
    source = SourceResult(f"ci-{repo.key}", commit=run["headSha"][:7], run=f"{repo.workflow} #{run['databaseId']} ({run['conclusion']})", report=run["url"])
    if repo.kind == "trx":
        read_trx(target, known, source)
    else:
        junit = sorted(target.rglob("*.xml"))
        if not junit:
            raise RuntimeError(f"{repo.repo}: artifact {repo.artifact} không có file JUnit XML")
        read_junit(junit[0], known, source)
    if repo.steps:
        jobs = json.loads(_gh("run", "view", str(run["databaseId"]), "-R", repo.repo, "--json", "jobs"))
        (target / "ci-jobs.json").write_text(json.dumps(jobs, ensure_ascii=False, indent=2), encoding="utf-8")
        read_ci_steps(jobs, repo.steps, known, source)
    return source, target
