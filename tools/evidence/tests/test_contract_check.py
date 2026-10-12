"""Kiểm contract (Q-09): request theo decision-request.schema.json; response theo decision-result.schema.json và quy tắc INTEGRATION_SPEC §3.3."""
import copy
import json
from decimal import Decimal
from typing import Any

import pytest

from evidence import contract_check

RESERVATION = "5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11"
DECISION = "0f9c7e5a-0000-4000-8000-00000000d001"


def valid_request(total: Any = 228000, currency: str = "VND") -> dict[str, Any]:
    return {
        "reservationId": RESERVATION,
        "branchId": "1f2e3d4c-0000-4000-8000-000000000001",
        "branchTimeZone": "Asia/Ho_Chi_Minh",
        "items": [{
            "resourceId": "c0a8012e-0000-4000-8000-0000000000aa",
            "resourceTypeId": "c0a8012e-0000-4000-8000-0000000000bb",
            "startTime": "2026-11-20T18:00:00+07:00",
            "endTime": "2026-11-20T20:00:00+07:00",
            "price": 240000,
        }],
        "customer": {"customerId": None, "isGuest": True, "tier": "STANDARD", "memberSince": None},
        "totalPrice": total,
        "currency": currency,
        "channel": "ONLINE",
        "requestedAt": "2026-11-18T09:15:00+07:00",
    }


def response_text(status: str = "APPROVED", rate: str = "0.5", amount: str = "114000", currency: str = "VND", approval: str = "null") -> str:
    deposit = "null" if status != "APPROVED" else f'{{"rate": {rate}, "amount": {amount}, "currency": "{currency}"}}'
    return (
        f'{{"decisionId": "{DECISION}", "reservationId": "{RESERVATION}", "status": "{status}", "deposit": {deposit},'
        f' "strategyName": "DefaultDecisionStrategy", "strategyVersion": "2.0.0", "executionType": "AUTOMATED", "fallbackUsed": false,'
        f' "reasonCode": "PEAK_HOUR_DEPOSIT", "reasons": [], "recommendations": [], "approvalExpiresAt": {approval},'
        f' "evaluatedAt": "2026-11-18T02:15:00.041Z"}}'
    )


def check(text: str, request: dict[str, Any] | None = None, status: int = 200) -> list[str]:
    return contract_check.response_errors(request or valid_request(), status, contract_check.parse_json(text))


def test_valid_request_has_no_errors() -> None:
    assert contract_check.request_errors(valid_request()) == []


def test_request_missing_items_is_reported() -> None:
    body = valid_request()
    del body["items"]

    errors = contract_check.request_errors(body)

    assert errors and any("items" in error for error in errors)


def test_request_with_unknown_field_or_bad_uuid_is_reported() -> None:
    extra = valid_request()
    extra["phone"] = "0900000000"
    bad_uuid = valid_request()
    bad_uuid["reservationId"] = "khong-phai-uuid"

    assert contract_check.request_errors(extra)
    assert contract_check.request_errors(bad_uuid)


def test_request_errors_do_not_mutate_input() -> None:
    body = valid_request()
    snapshot = copy.deepcopy(body)

    contract_check.request_errors(body)

    assert body == snapshot


def test_contract_check_Theory() -> None:
    # 200 hợp lệ
    assert check(response_text()) == []
    # NEED_APPROVAL hợp lệ: không deposit, có approvalExpiresAt
    assert check(response_text("NEED_APPROVAL", approval='"2026-11-18T05:15:00Z"')) == []
    # REJECTED hợp lệ: không deposit
    assert check(response_text("REJECTED")) == []
    # APPROVED thiếu deposit
    approved_without_deposit = json.loads(response_text())
    approved_without_deposit["deposit"] = None
    errors = contract_check.response_errors(valid_request(), 200, approved_without_deposit)
    assert errors and any(error.startswith("RULE") and "deposit" in error for error in errors)
    # NEED_APPROVAL nhưng lại có deposit
    need_with_deposit = json.loads(response_text("NEED_APPROVAL", approval='"2026-11-18T05:15:00Z"'))
    need_with_deposit["deposit"] = {"rate": 0.5, "amount": 114000, "currency": "VND"}
    assert any(error.startswith("RULE") for error in contract_check.response_errors(valid_request(), 200, need_with_deposit))
    # amount sai làm tròn (đúng là 114000)
    wrong_amount = check(response_text(amount="113999"))
    assert wrong_amount and any(error.startswith("RULE") and "amount" in error for error in wrong_amount)
    # 503: lỗi trạng thái, không kiểm body
    status_errors = contract_check.response_errors(valid_request(), 503, None)
    assert status_errors and status_errors[0].startswith("STATUS") and "503" in status_errors[0]
    # 200 nhưng body không qua schema
    broken = json.loads(response_text())
    del broken["strategyName"]
    assert any(error.startswith("SCHEMA") for error in contract_check.response_errors(valid_request(), 200, broken))
    # 200 nhưng body không phải object
    assert contract_check.response_errors(valid_request(), 200, None)
    # reservationId không khớp request
    mismatched = json.loads(response_text())
    mismatched["reservationId"] = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
    assert any("reservationId" in error for error in contract_check.response_errors(valid_request(), 200, mismatched))


@pytest.mark.parametrize(
    "total,currency,rate,amount,ok",
    [
        (228000, "VND", "0.5", "114000", True),
        (228000, "VND", "0.5", "114000.0", True),  # số kiểu 40.0 / 86.00 của wire vẫn so được
        (100001, "VND", "0.5", "50001", True),  # 50000,5 làm tròn xa 0 (AwayFromZero) ra 50001
        (100001, "VND", "0.5", "50000", False),  # làm tròn về số chẵn (banker's) hay cắt bớt đều sai
        (100003, "VND", "0.3", "30001", True),  # 30000,9 ra 30001
        ("86.35", "EUR", "0.5", "43.18", True),  # 43,175 ra 43,18 (float hoặc banker's rounding sẽ ra 43,17)
        ("86.35", "EUR", "0.5", "43.17", False),
        (86, "EUR", "0.4651", "40.0", True),  # 39,9986 ra 40,00
        (86, "EUR", "0.4651", "40", True),
        (0, "VND", "0", "0", True),
        (200, "EUR", "1", "200.00", True),
        (200, "EUR", "1", "199.99", False),
    ],
)
def test_deposit_amount_rounds_half_up_by_currency(total: Any, currency: str, rate: str, amount: str, ok: bool) -> None:
    request = valid_request(total=Decimal(total) if isinstance(total, str) else total, currency=currency)

    errors = check(response_text(rate=rate, amount=amount, currency=currency), request)

    assert (errors == []) is ok, errors


def test_deposit_currency_must_match_request() -> None:
    errors = check(response_text(currency="EUR", amount="114000.00"), valid_request(currency="VND"))

    assert any("currency" in error for error in errors)


def test_parse_json_keeps_decimal_precision() -> None:
    parsed = contract_check.parse_json('{"amount": 86.00, "rate": 0.4651, "n": 3}')

    assert parsed["amount"] == Decimal("86.00") and isinstance(parsed["amount"], Decimal)
    assert parsed["rate"] == Decimal("0.4651") and parsed["n"] == 3


@pytest.mark.parametrize("field,value", [("evaluatedAt", "hôm nay"), ("evaluatedAt", "2026-11-18T02:15:00"), ("evaluatedAt", "2026-13-45T25:61:00Z")])
def test_response_date_time_format_is_enforced(field: str, value: str) -> None:
    body = json.loads(response_text())
    body[field] = value

    errors = contract_check.response_errors(valid_request(), 200, body)

    assert any(error.startswith("SCHEMA") and field in error for error in errors), errors


def test_response_approval_expires_at_format_is_enforced() -> None:
    body = json.loads(response_text("NEED_APPROVAL", approval='"2026-11-18T05:15:00Z"'))
    body["approvalExpiresAt"] = "sáng mai"

    errors = contract_check.response_errors(valid_request(), 200, body)

    assert any(error.startswith("SCHEMA") and "approvalExpiresAt" in error for error in errors), errors


@pytest.mark.parametrize("field", ["startTime", "endTime"])
def test_request_date_time_format_is_enforced_beyond_the_pattern(field: str) -> None:
    body = valid_request()
    body["items"][0][field] = "2026-13-45T25:61:00+07:00"  # khớp pattern nhưng không phải thời điểm hợp lệ

    errors = contract_check.request_errors(body)

    assert any(field in error for error in errors), errors
