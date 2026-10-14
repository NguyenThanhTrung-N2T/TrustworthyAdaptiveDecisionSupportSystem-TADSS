"""Ghép PRIMARY/SHADOW theo correlation_id (spec 4.1–4.2): bốn trạng thái loại trừ nhau, phạm vi lượt theo tập correlation_id đã gửi, kiểm toàn vẹn, D5 thiếu shadow để ô rỗng."""
from evidence import shadow_pairing as sp


def d1(role: str, corr: str, *, fallback: bool = False, reservation: str = "r", request_hash: str = "h", created: str = "2026-10-14T10:00:00.000001+00:00",
       status: str = "APPROVED", rate: str = "0.3", amount: str = "30") -> dict[str, str]:
    return {"role": role, "correlation_id": corr, "reservation_hash": reservation, "request_hash": request_hash, "created_at_utc": created,
            "fallback_used": str(fallback), "status": status, "deposit_rate": rate, "deposit_amount": amount, "decision_id": f"{role}-{corr}"}


def test_states_are_mutually_exclusive() -> None:
    rows = [
        d1("PRIMARY", "a"), d1("SHADOW", "a", rate="0.5"),
        d1("PRIMARY", "b", fallback=True), d1("SHADOW", "b"),
        d1("PRIMARY", "c"), d1("SHADOW", "c", fallback=True),
        d1("PRIMARY", "d"),
        d1("PRIMARY", "e", fallback=True), d1("SHADOW", "e", fallback=True),   # cả hai fallback → PRIMARY_FALLBACK
    ]
    pairing = sp.pair(rows, {"a", "b", "c", "d", "e"})
    assert pairing.states == {"a": sp.PAIRED, "b": sp.PRIMARY_FALLBACK, "c": sp.SHADOW_FALLBACK, "d": sp.UNPAIRED, "e": sp.PRIMARY_FALLBACK}
    assert sp.state_counts(pairing) == {sp.PAIRED: 1, sp.PRIMARY_FALLBACK: 2, sp.SHADOW_FALLBACK: 1, sp.UNPAIRED: 1}
    assert pairing.violations == {}


def test_rows_outside_run_are_counted_not_paired() -> None:
    pairing = sp.pair([d1("PRIMARY", "old"), d1("SHADOW", "old"), d1("PRIMARY", "a")], {"a"})
    assert pairing.outside_run == 2 and pairing.states == {"a": sp.UNPAIRED}


def test_integrity_violations() -> None:
    rows = [
        d1("PRIMARY", "dup"), d1("PRIMARY", "dup"),
        d1("PRIMARY", "multi"), d1("SHADOW", "multi"), d1("SHADOW", "multi"),
        d1("SHADOW", "orphan"),
        d1("PRIMARY", "skew"), d1("SHADOW", "skew", request_hash="other"),
    ]
    pairing = sp.pair(rows, {"dup", "multi", "orphan", "skew"})
    assert pairing.violations == {sp.DUPLICATE_PRIMARY: 1, sp.MULTIPLE_SHADOW: 1, sp.ORPHAN_SHADOW: 1, sp.INVARIANT_MISMATCH: 1}
    assert pairing.samples and all(":" in sample for sample in pairing.samples)


def test_attach_fills_shadow_columns_and_keeps_missing_empty() -> None:
    pairing = sp.pair([d1("PRIMARY", "a"), d1("SHADOW", "a", status="NEED_APPROVAL", rate="", amount=""), d1("PRIMARY", "b", fallback=True)], {"a", "b"})
    blank = {"shadow_pair_state": "", "primary_fallback": "", "shadow_status": "", "shadow_deposit_rate": "", "shadow_deposit_amount": ""}
    rows = [dict(blank), dict(blank), dict(blank)]
    sp.attach(rows, ["a", "b", None], pairing)
    assert rows[0] == {"shadow_pair_state": sp.PAIRED, "primary_fallback": "False", "shadow_status": "NEED_APPROVAL", "shadow_deposit_rate": "", "shadow_deposit_amount": ""}
    assert rows[1]["shadow_pair_state"] == sp.UNPAIRED and rows[1]["primary_fallback"] == "True" and rows[1]["shadow_status"] == ""
    assert rows[2] == blank   # request không có quyết định: không trạng thái, không 0
