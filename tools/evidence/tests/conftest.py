"""Cấu hình pytest: đưa thư mục `tools/` vào sys.path để import gói `evidence` (và `evaluator` khi test tích hợp với evaluator)."""
import sys
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[2]
for entry in (TOOLS, TOOLS / "evaluation"):
    if str(entry) not in sys.path:
        sys.path.insert(0, str(entry))
FIXTURES = Path(__file__).resolve().parent / "fixtures"
