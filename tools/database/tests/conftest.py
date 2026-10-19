"""Cấu hình pytest: đưa thư mục tools/database vào sys.path để import `schema_snapshot`."""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))
