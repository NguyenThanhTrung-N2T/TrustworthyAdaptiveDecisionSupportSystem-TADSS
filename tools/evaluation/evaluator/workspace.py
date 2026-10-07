"""Bản sao làm việc `/repo` → `/work` loại trừ file bí mật và sản phẩm build (O-14), cùng commit và cờ dirty cho manifest."""
from __future__ import annotations

import fnmatch
import os
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

# Tên thư mục bị loại ở mọi cấp
EXCLUDED_DIRS = frozenset({"bin", "obj", "TestResults", "logs", "evidence", ".vs", ".git", ".idea", ".venv", "node_modules", ".superpowers", ".claude"})
# Mẫu tên file bị loại ở mọi cấp (secret)
EXCLUDED_FILE_PATTERNS = ("appsettings.Development.json", ".env", ".env.*")
# Sau khi copy, không file nào khớp các mẫu này được phép tồn tại trong thư mục làm việc
FORBIDDEN_FILE_PATTERNS = EXCLUDED_FILE_PATTERNS


class WorkspaceError(RuntimeError):
    """Thư mục làm việc không an toàn (có file bí mật) hoặc không dựng được."""


@dataclass(frozen=True)
class GitInfo:
    """Commit và trạng thái cây làm việc của repo nguồn."""

    commit: str | None
    dirty: bool | None


def _is_secret(name: str) -> bool:
    return any(fnmatch.fnmatch(name, pattern) for pattern in EXCLUDED_FILE_PATTERNS)


def copy_workspace(source: Path, destination: Path) -> int:
    """Copy `source` → `destination` (xóa bản cũ), loại thư mục build/rác và file bí mật; trả số file đã copy."""
    if destination.exists():
        shutil.rmtree(destination)
    copied = 0
    for root, dirs, files in os.walk(source):
        dirs[:] = [name for name in dirs if name not in EXCLUDED_DIRS]
        target_root = destination / Path(root).relative_to(source)
        target_root.mkdir(parents=True, exist_ok=True)
        for name in files:
            if _is_secret(name):
                continue
            shutil.copy2(Path(root) / name, target_root / name)
            copied += 1
    return copied


def find_forbidden(workspace: Path) -> list[Path]:
    """Mọi file bí mật còn lọt vào `workspace` (kiểm tra độc lập với bước copy)."""
    return sorted(
        Path(root) / name
        for root, _, files in os.walk(workspace)
        for name in files
        if any(fnmatch.fnmatch(name, pattern) for pattern in FORBIDDEN_FILE_PATTERNS)
    )


def prepare_workspace(source: Path, destination: Path) -> int:
    """Copy rồi kiểm tra; có file bí mật lọt qua thì dừng bằng `WorkspaceError` (bước gọi coi là `error`)."""
    copied = copy_workspace(source, destination)
    leaked = find_forbidden(destination)
    if leaked:
        raise WorkspaceError("Thư mục làm việc chứa file bí mật: " + ", ".join(str(path) for path in leaked))
    return copied


def read_git_info(repo: Path, run: Callable[[list[str], Path], tuple[int, str]] | None = None) -> GitInfo:
    """Commit và cờ dirty (`git status --porcelain` rỗng ⇒ sạch) của `repo`; không đọc được (không phải repo git) ⇒ cả hai là None."""
    runner = run or _run_git
    code, head = runner(["git", "rev-parse", "HEAD"], repo)
    if code != 0:
        return GitInfo(None, None)
    code, status = runner(["git", "status", "--porcelain"], repo)
    return GitInfo(head.strip(), bool(status.strip()) if code == 0 else None)


def _run_git(command: list[str], cwd: Path) -> tuple[int, str]:
    try:
        completed = subprocess.run(
            ["git", "-c", f"safe.directory={cwd}", *command[1:]], cwd=cwd, capture_output=True, text=True, check=False
        )
    except OSError:  # không có git hoặc thư mục không tồn tại
        return 128, ""
    return completed.returncode, completed.stdout
