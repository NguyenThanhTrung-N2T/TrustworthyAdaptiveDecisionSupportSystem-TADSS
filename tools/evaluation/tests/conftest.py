"""Cấu hình pytest: đưa thư mục tools/evaluation vào sys.path để import `evaluator`."""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
for entry in (ROOT, ROOT.parent):  # tools/evaluation (gói evaluator) và tools (gói evidence)
    if str(entry) not in sys.path:
        sys.path.insert(0, str(entry))
FIXTURES = Path(__file__).resolve().parent / "fixtures"
