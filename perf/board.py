"""Điểm vào testboard từ gốc repo: `python perf/board.py <lệnh>` (danh sách lệnh: `python perf/board.py -h`)."""
import sys
from pathlib import Path

sys.dont_write_bytecode = True  # không sinh __pycache__ trong repo
sys.path.insert(0, str(Path(__file__).resolve().parent))
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

from testboard.cli import main  # noqa: E402

if __name__ == "__main__":
    sys.exit(main())
