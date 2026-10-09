"""SC7 (PT-01): PASS khi ≥ 10.000 bản ghi gửi, 0 lỗi contract và kiểm mã đạt; FAIL khi có lỗi contract hoặc kiểm mã đỏ; còn lại thiếu dữ liệu."""
import pytest

from evidence import kpi

GOOD_CODE = {"status": "PASS", "reason": "2 commit chạm adapter, không commit nào chạm src/TADSS.Runtime.Api hay SDK", "commits": ["abc1234"]}


def _summary(sent: int = 10000, errors: int = 0, status: str = "passed", not_sent: int = 0) -> dict:
    return {"part": "portability", "status": status, "sent": sent, "not_sent": not_sent, "prepared": sent + not_sent, "contract_error_requests": errors + not_sent, "retries": 3}


@pytest.mark.parametrize(
    ("summary", "code_check", "verdict"),
    [
        (_summary(9999), GOOD_CODE, kpi.INSUFFICIENT_DATA),
        (_summary(10000), GOOD_CODE, kpi.PASS),
        (_summary(10000, errors=1, status="failed"), GOOD_CODE, kpi.FAIL),
        (_summary(10000, not_sent=1, status="failed"), GOOD_CODE, kpi.FAIL),  # request do adapter dựng sai schema cũng là lỗi contract (Q-09)
        (_summary(10000), {"status": "FAIL", "reason": "commit abc chạm src/TADSS.Runtime.Api", "commits": ["abc"]}, kpi.FAIL),
        (_summary(10000), {"status": "INSUFFICIENT_DATA", "reason": "adapter chưa commit", "commits": []}, kpi.INSUFFICIENT_DATA),
        (None, GOOD_CODE, kpi.INSUFFICIENT_DATA),
        ({"part": "portability", "status": "skipped", "note": "PT: bỏ qua vì không tải được dataset"}, GOOD_CODE, kpi.INSUFFICIENT_DATA),
        ({"part": "portability", "status": "failed", "note": "PT: dataset khách sạn sai SHA-256"}, GOOD_CODE, kpi.INSUFFICIENT_DATA),
        ({"part": "portability", "status": "error", "note": "chưa sẵn sàng", "prepared": 10, "sent": 3}, GOOD_CODE, kpi.INSUFFICIENT_DATA),
        (_summary(10000), None, kpi.INSUFFICIENT_DATA),
        (_summary(20000, errors=2, status="failed"), {"status": "INSUFFICIENT_DATA", "reason": "adapter chưa commit", "commits": []}, kpi.FAIL),
    ],
    ids=["9999", "10000", "1-contract-error", "unsent-adapter-error", "code-check-red", "adapter-uncommitted", "no-pt", "pt-skipped", "pt-sha-mismatch", "pt-unreachable", "no-code-check", "errors-win"],
)
def test_sc7_rules(summary: dict | None, code_check: dict | None, verdict: str) -> None:
    result = kpi.sc7(summary, code_check)

    assert result.code == "SC7" and result.verdict == verdict
    assert result.reason and result.sources, "mỗi kết luận kèm lý do và đường dẫn file thô"


def test_sc7_pass_reports_counts_and_code_check_reason() -> None:
    result = kpi.sc7(_summary(12345), GOOD_CODE)

    assert "12345" in result.value and "0 lỗi contract" in result.value
    assert "abc1234" in result.reason or GOOD_CODE["reason"] in result.reason
    assert any(source.startswith("raw/portability/") for source in result.sources)


def test_sc7_insufficient_explains_what_is_missing() -> None:
    assert "10000" in kpi.sc7(_summary(9999), GOOD_CODE).reason or "10.000" in kpi.sc7(_summary(9999), GOOD_CODE).reason
    assert "chưa commit" in kpi.sc7(_summary(10000), {"status": "INSUFFICIENT_DATA", "reason": "adapter chưa commit", "commits": []}).reason
    assert "sai SHA-256" in kpi.sc7({"part": "portability", "status": "failed", "note": "PT: dataset khách sạn sai SHA-256"}, GOOD_CODE).reason
