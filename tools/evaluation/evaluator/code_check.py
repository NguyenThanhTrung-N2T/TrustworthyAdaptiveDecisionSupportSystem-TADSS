"""Kiểm mã của SC7 (Q-11): chứng minh adapter khách sạn được thêm mà KHÔNG sửa mã runtime hay SDK, ngay trong lượt chạy, chỉ bằng lệnh git ĐỌC trên `/repo`.

Đạt khi (1) mọi commit có chạm `tools/evidence/hotel_adapter.py` đều không chạm `src/TADSS.Runtime.Api/**` hay `src/Shared.DecisionContractSDK/**`,
(2) cây làm việc không có thay đổi chưa commit trong hai thư mục đó. Không đạt khi vi phạm một trong hai. Adapter chưa commit (hoặc đang sửa dở) thì chưa kết luận
được: `INSUFFICIENT_DATA` kèm lý do "adapter chưa commit". Kết quả ghi rõ các commit đã xét.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable

from evaluator import workspace

ADAPTER_PATH = "tools/evidence/hotel_adapter.py"
RUNTIME_PATHS = ("src/TADSS.Runtime.Api", "src/Shared.DecisionContractSDK")
PASS = "PASS"
FAIL = "FAIL"
INSUFFICIENT_DATA = "INSUFFICIENT_DATA"
SHORT_HASH = 7
NORMALIZE_EOL = workspace.NORMALIZE_EOL  # chuẩn hóa dòng như máy chủ Windows (xem workspace)
MAX_FILES_SHOWN = 5

GitRunner = Callable[[list[str], Path], tuple[int, str]]


@dataclass(frozen=True)
class CodeCheck:
    """Kết luận kiểm mã: `status` (PASS/FAIL/INSUFFICIENT_DATA), lý do tiếng Việt và các commit (7 ký tự đầu) đã xét."""

    status: str
    reason: str
    commits: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        return {"status": self.status, "reason": self.reason, "commits": list(self.commits), "adapter": ADAPTER_PATH}


def check_adapter_isolation(repo: Path, run: GitRunner | None = None, adapter: str = ADAPTER_PATH, runtime_paths: tuple[str, ...] = RUNTIME_PATHS) -> CodeCheck:
    """Chạy kiểm mã trên `repo` (git chỉ đọc: `rev-parse`, `status`, `log`, `diff-tree`). `run` thay được bằng bản giả khi test.
    Lệnh git lỗi (mã thoát khác 0) không bao giờ được coi là "sạch": kết luận là `INSUFFICIENT_DATA` kèm lý do."""
    runner = run or workspace._run_git  # dùng chung bộ chạy git (safe.directory) với read_git_info
    code, _ = runner(["git", "rev-parse", "HEAD"], repo)
    if code != 0:
        return CodeCheck(INSUFFICIENT_DATA, "không đọc được git của repo nguồn nên chưa kiểm được mã")

    code, status = runner(["git", *NORMALIZE_EOL, "status", "--porcelain", "--", *runtime_paths], repo)
    if code != 0:
        return CodeCheck(INSUFFICIENT_DATA, f"git status trên {', '.join(runtime_paths)} lỗi (mã thoát {code}) nên chưa kiểm được cây làm việc")
    dirty = [line[3:] for line in status.splitlines() if line.strip()]
    if dirty:
        return CodeCheck(FAIL, f"có thay đổi chưa commit trong {', '.join(runtime_paths)}: {_files(dirty)}")

    code, log = runner(["git", "log", "--full-history", "--format=%H", "--", adapter], repo)
    if code != 0:
        return CodeCheck(INSUFFICIENT_DATA, f"git log {adapter} lỗi (mã thoát {code}) nên chưa biết các commit chạm adapter")
    full = [line.strip() for line in log.splitlines() if line.strip()]
    code, adapter_status = runner(["git", *NORMALIZE_EOL, "status", "--porcelain", "--", adapter], repo)
    if code != 0:
        return CodeCheck(INSUFFICIENT_DATA, f"git status {adapter} lỗi (mã thoát {code}) nên chưa biết adapter đã commit trọn vẹn chưa")
    if not full:
        return CodeCheck(INSUFFICIENT_DATA, f"adapter chưa commit: không có commit nào chạm {adapter}")
    commits = [commit[:SHORT_HASH] for commit in full]
    if adapter_status.strip():
        return CodeCheck(INSUFFICIENT_DATA, "adapter có thay đổi chưa commit nên bản đã chạy khác bản đã commit (adapter chưa commit trọn vẹn)", commits)

    violations = []
    for commit in full:
        # -m: commit merge cũng liệt kê file thay đổi so với từng parent (không có -m, diff-tree bỏ qua merge)
        code, names = runner(["git", "diff-tree", "-m", "--root", "--no-commit-id", "--name-only", "-r", commit], repo)
        if code != 0:
            return CodeCheck(INSUFFICIENT_DATA, f"git diff-tree commit {commit[:SHORT_HASH]} lỗi (mã thoát {code}) nên chưa biết commit chạm file nào", commits)
        touched = [name.strip() for name in names.splitlines() if _inside(name.strip(), runtime_paths)]
        if touched:
            violations.append(f"commit {commit[:SHORT_HASH]} chạm {_files(touched)}")
    if violations:
        return CodeCheck(FAIL, "commit chạm adapter đồng thời sửa mã runtime/SDK: " + "; ".join(violations), commits)
    return CodeCheck(PASS, f"{len(full)} commit chạm adapter, không commit nào chạm {' hay '.join(runtime_paths)}; cây làm việc sạch ở hai thư mục này", commits)


def _inside(name: str, directories: tuple[str, ...]) -> bool:
    """`name` nằm TRONG một trong các thư mục (ranh giới thư mục: `src/TADSS.Runtime.ApiX/…` không thuộc `src/TADSS.Runtime.Api`)."""
    return any(name == directory or name.startswith(directory.rstrip("/") + "/") for directory in directories)


def _files(names: list[str]) -> str:
    shown = ", ".join(names[:MAX_FILES_SHOWN])
    return shown + (f" (và {len(names) - MAX_FILES_SHOWN} file khác)" if len(names) > MAX_FILES_SHOWN else "")
