# Test của perf/testboard: không cần Docker, Pushgateway hay mạng (đẩy metric và tải CI được thay hoặc bỏ qua)
import sys
from pathlib import Path

import pytest

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from testboard import paths  # noqa: E402
from testboard.catalog import load  # noqa: E402


@pytest.fixture(scope="session")
def known() -> set[str]:
    return set(load())


@pytest.fixture(autouse=True)
def isolated_results(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Path:
    # Mọi test ghi state.json và bản gốc vào thư mục tạm, không đụng perf/results thật
    results = tmp_path / "results"
    monkeypatch.setattr(paths, "RESULTS_DIR", results)
    monkeypatch.setattr(paths, "STATE_FILE", results / "state.json")
    return results
