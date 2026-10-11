"""Cấu hình pytest: đưa thư mục tools/evaluation vào sys.path để import `evaluator`."""
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
for entry in (ROOT, ROOT.parent):  # tools/evaluation (gói evaluator) và tools (gói evidence)
    if str(entry) not in sys.path:
        sys.path.insert(0, str(entry))
FIXTURES = Path(__file__).resolve().parent / "fixtures"


@pytest.fixture
def fixed_git_info(monkeypatch: pytest.MonkeyPatch) -> None:
    """Commit cố định cho test của bước (f): không phụ thuộc thư mục chạy có phải bản checkout git (bản copy /work của evaluator không có .git, EV-5 sẽ trượt)."""
    from evaluator import workspace

    monkeypatch.setattr(workspace, "read_git_info", lambda repo, run=None: workspace.GitInfo("0123456789abcdef0123456789abcdef01234567", False))
