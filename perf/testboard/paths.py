"""Đường dẫn dùng chung và nơi nạp module có sẵn của repo (đọc trx của evaluator, slug anchor của check_docs) để không chép lại."""
from __future__ import annotations

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
METHODOLOGY = REPO_ROOT / "docs" / "research" / "EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md"
METHODOLOGY_REPO_PATH = "docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md"
METHODOLOGY_URL = "https://github.com/NguyenThanhTrung-N2T/TrustworthyAdaptiveDecisionSupportSystem-TADSS/blob/develop/" + METHODOLOGY_REPO_PATH
CATALOG_FILE = REPO_ROOT / "tests" / "catalog.yaml"
PERF_DIR = REPO_ROOT / "perf"
RESULTS_DIR = PERF_DIR / "results"
STATE_FILE = RESULTS_DIR / "state.json"
DASHBOARD_DIR = PERF_DIR / "grafana" / "dashboards"

for _tool_dir in (REPO_ROOT / "tools" / "evaluation", REPO_ROOT / "tools" / "evidence", REPO_ROOT / "tools" / "docs"):
    if str(_tool_dir) not in sys.path:
        sys.path.insert(0, str(_tool_dir))
