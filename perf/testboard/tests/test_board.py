# Gộp nguồn, trạng thái ô nhóm, metric Prometheus và lượt nạp (state.json + bản gốc trong perf/results)
import json

import pytest
from testboard import board, catalog
from testboard.model import FAILED, LIMITED, PASSED, UNTESTED, CodeResult, KpiResult, SourceResult


def _source(name, **codes):
    source = SourceResult(name, commit=name[:3])
    source.codes = {code: CodeResult(state) for code, state in codes.items()}
    return source


@pytest.fixture
def entries():
    return {code: entry for code, entry in catalog.load().items() if code in {"UT-01", "UT-02", "FI-01", "FI-12", "OPS-01"}}


def test_merge_takes_worst_state_across_sources(entries):
    sources = {"ci": _source("ci-tadss", **{"UT-01": PASSED, "UT-02": PASSED}), "ev": _source("evidence", **{"UT-02": FAILED})}

    merged = {item.entry.code: item for item in board.merge(entries, sources)}

    assert merged["UT-01"].state == PASSED and merged["UT-01"].source == "ci-tadss"
    assert merged["UT-02"].state == FAILED and merged["UT-02"].source == "evidence"
    assert merged["FI-01"].state == UNTESTED and merged["FI-01"].source == ""


@pytest.mark.parametrize(
    ("states", "expected"),
    [([PASSED, PASSED], PASSED), ([PASSED, FAILED], FAILED), ([PASSED, UNTESTED], LIMITED), ([LIMITED, PASSED], LIMITED),
     ([UNTESTED, UNTESTED], UNTESTED), ([UNTESTED, FAILED], FAILED)],
)
def test_group_state_colours(states, expected):
    assert board.group_state(states) == expected


def test_exposition_has_group_tile_per_system_and_escapes_labels(entries):
    source = _source("ci", **{"FI-01": PASSED, "FI-12": FAILED})
    source.codes["FI-12"].detail = 'Class."Method"\nline'
    source.kpis["SC1"] = KpiResult(PASSED, "PASS", "p95 47 ms")

    text = board.exposition(board.merge(entries, {"ci": source}), {"ci": source})

    assert 'smashcourt_test_group_state{group="FI",group_label="Chịu lỗi",system="TADSS",passed="1"' in text
    assert 'system="BE",passed="0",failed="1"' in text
    assert 'detail="Class.\\"Method\\" line"' in text
    assert 'smashcourt_test_kpi_state{kpi="SC1"' in text and 'smashcourt_test_kpi_state{kpi="SC8"' in text
    assert text.count('group="OPS"') == 1 + 3  # một dòng mã + một ô cho mỗi tab BE/TADSS/AI


def test_record_keeps_other_sources_and_archives_raw_files(tmp_path, entries, isolated_results):
    raw = tmp_path / "junit.xml"
    raw.write_text("<x/>", encoding="utf-8")
    board.record(_source("evidence", **{"UT-01": PASSED}), [], entries, push_url=None)

    target, merged = board.record(_source("ci-ai", **{"UT-02": FAILED}), [raw], entries, push_url=None)

    state = json.loads((isolated_results / "state.json").read_text(encoding="utf-8"))
    assert set(state) == {"evidence", "ci-ai"}
    assert (target / "junit.xml").is_file() and json.loads((target / "summary.json").read_text(encoding="utf-8"))["report"]
    assert {item.entry.code: item.state for item in merged}["UT-02"] == FAILED


def test_push_puts_whole_board_to_pushgateway(monkeypatch):
    calls = []

    class Response:
        status = 200

        def __enter__(self):
            return self

        def __exit__(self, *args):
            return False

    monkeypatch.setattr(board.urllib.request, "urlopen", lambda request, timeout: calls.append(request) or Response())

    board.push("smashcourt_test_kpi_state 1\n", "http://pg:9091")

    [request] = calls
    assert (request.get_method(), request.full_url) == ("PUT", "http://pg:9091/metrics/job/testboard")
