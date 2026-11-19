# Báo cáo tổng kết HTML cho người đọc không chuyên (người dùng chốt 19/11): kết luận chung, trả lời theo câu hỏi, cần lưu ý, chưa kiểm
from datetime import datetime, timezone

import pytest

from testboard import catalog, report
from testboard.model import FAILED, LIMITED, PASSED, CodeResult, KpiResult, SourceResult

_NOW = datetime(2026, 11, 19, 15, 30, tzinfo=timezone.utc)


@pytest.fixture(scope="module")
def entries():
    return catalog.load()


def _source(name, commit="", **codes):
    return SourceResult(name, commit=commit, codes={code.replace("_", "-"): CodeResult(state, detail=detail) for code, (state, detail) in codes.items()})


def test_every_question_points_at_codes_that_exist(entries):
    groups = {entry.group for entry in entries.values()}
    for question in report.QUESTIONS:
        assert set(question.groups) <= groups, question.text
        assert set(question.codes) <= set(entries), question.text
    assert set(report.PLAIN_TITLES) <= set(entries)


def test_every_group_is_covered_by_some_question(entries):
    covered = {group for question in report.QUESTIONS for group in question.groups}
    loose = {code for question in report.QUESTIONS for code in question.codes}
    missing = {entry.group for code, entry in entries.items() if entry.group not in covered and code not in loose}
    assert missing == set()


def test_plain_numbers_turn_k6_p95_into_words():
    text = report.plain_numbers("gộp 3 lượt: verdict ĐẠT · read p95 15 ms, booking p95 87 ms")
    assert text == "95 % lượt xem sân, lịch xong trong 0,015 giây; 95 % lượt đặt sân xong trong 0,087 giây"
    assert report.plain_numbers("p95 120 ms") == ""


@pytest.mark.parametrize(("states", "verdict"), [((PASSED, PASSED), "Kết luận: ĐẠT<"), ((PASSED, LIMITED), "ĐẠT, CÓ ĐIỂM CẦN LƯU Ý"),
                                                 ((LIMITED, FAILED), "CHƯA ĐẠT HẾT: 1 phép kiểm không đạt")])
def test_verdict_follows_the_worst_tested_result(entries, states, verdict):
    sources = {"x": _source("x", UT_01=(states[0], ""), UT_02=(states[1], ""))}
    assert verdict in report.render(entries, sources, _NOW)


def test_question_answer_counts_tested_and_untested_and_speaks_numbers(entries):
    sources = {"k6": _source("k6", SP_01=(PASSED, "gộp 3 lượt: verdict ĐẠT · booking p95 87 ms"))}
    merged = {item.entry.code: item for item in report.board.merge(entries, sources)}
    question = next(q for q in report.QUESTIONS if "SP-01" in q.codes)

    state, text = report.answer(question, merged, sources)

    assert state == PASSED
    assert text.startswith("Có. Đã kiểm 1/4 phép kiểm, còn 3 chưa kiểm.")
    assert "95 % lượt đặt sân xong trong 0,087 giây" in text


def test_question_with_nothing_run_says_not_checked(entries):
    question = next(q for q in report.QUESTIONS if "FE" in q.groups)
    merged = {item.entry.code: item for item in report.board.merge(entries, {})}
    assert report.answer(question, merged, {}) == (report.UNTESTED, "Chưa kiểm (26 phép kiểm chưa chạy).")


def test_kpi_alone_can_answer_a_question(entries):
    sources = {"sc6": SourceResult("sc6", kpis={"SC6": KpiResult(FAILED, "FAIL", "1 vi phạm")})}
    merged = {item.entry.code: item for item in report.board.merge(entries, sources)}
    question = next(q for q in report.QUESTIONS if "SC6" in q.kpis)
    assert report.answer(question, merged, sources)[0] == FAILED


def test_report_lists_problems_with_plain_titles_and_escapes_text(entries):
    sources = {"ci-be": _source("ci-be", commit="1fa2d2c", SP_04=(FAILED, "RAM +15,5 % <script>")), "m": _source("m", LT_02=(LIMITED, "504"))}

    page = report.render(entries, sources, _NOW)

    assert "Chạy liên tục 90 phút" in page and "TADSS: tăng dần tới quá tải" in page
    assert "&lt;script&gt;" in page and "<script>" not in page
    assert "Lập lúc 22:30 19/11/2026" in page and "BE 1fa2d2c" in page


def test_write_keeps_latest_copy_and_prunes_old_dated_reports(entries, tmp_path, monkeypatch):
    monkeypatch.setattr(report, "KEEP_REPORTS", 2)
    for minute in range(3):
        latest = report.write(entries, {}, tmp_path, datetime(2026, 11, 19, 15, minute, tzinfo=timezone.utc))

    assert latest == tmp_path / "BAO_CAO_MOI_NHAT.html" and "Báo cáo chất lượng SmashCourt" in latest.read_text(encoding="utf-8")
    assert sorted(path.name for path in (tmp_path / "reports").iterdir()) == ["BAO_CAO_20261119-150100.html", "BAO_CAO_20261119-150200.html"]


def test_every_group_has_a_plain_name_and_meaning(entries):
    assert {entry.group for entry in entries.values()} <= set(report.GROUPS_PLAIN)


def test_only_the_chosen_code_speaks_numbers_in_an_answer(entries):
    sources = {"k6": _source("k6", SP_01=(PASSED, "booking p95 87 ms"), SP_06=(PASSED, "booking p95 93 ms"), SP_09=(PASSED, "booking p95 70 ms"))}
    merged = {item.entry.code: item for item in report.board.merge(entries, sources)}
    fault_question = next(q for q in report.QUESTIONS if "SP-09" in q.codes)
    assert "Số đo" not in report.answer(fault_question, merged, sources)[1]


@pytest.mark.parametrize(
    ("code", "source", "state", "detail", "expected"),
    [("SP-06", "k6-SP-06", LIMITED, "gộp 3 lượt: verdict KHÔNG ĐỦ DỮ LIỆU (ipn p99 KHÔNG ĐỦ DỮ LIỆU)", "quá ít lần đo"),
     ("LT-02", "manual", LIMITED, "2026-10-17 · Claude: 504 · bằng chứng: x.md", "Máy đo bận nên một ít yêu cầu quá giờ"),
     ("IT-07", "ci-tadss", FAILED, "OverrideEndpointTests.Override_X", "Một phép kiểm tự động bị lỗi"),
     ("SP-04", "k6-SP-04", FAILED, "verdict KHÔNG ĐẠT", "Nếu không xử lý thì bộ nhớ máy chủ có thể tăng dần")],
)
def test_problem_meaning_is_plain_language(entries, code, source, state, detail, expected):
    item = report.board.MergedCode(entries[code], state, source, "", detail)
    assert expected in report.meaning(item, {"LT-02": "Máy đo bận nên một ít yêu cầu quá giờ"})
