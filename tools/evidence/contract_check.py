"""Kiểm contract v2 cho replay (RP) và adapter khách sạn (PT) theo định nghĩa "lỗi contract" Q-09 của gói 5.6.

- `request_errors(body)`: request do adapter dựng phải qua `decision-request.schema.json` (tiền tố lỗi `SCHEMA`).
- `response_errors(request, status, body)`: response cuối cùng phải là 200 (`STATUS`), body qua `decision-result.schema.json` (`SCHEMA`) và đúng quy tắc
  INTEGRATION_SPEC §3.3 (`RULE`): `deposit` có khi và chỉ khi `APPROVED`, `reservationId` khớp request, `deposit.currency` khớp request và
  `deposit.amount = round(totalPrice × rate)`.

Làm tròn tiền cọc, đối chiếu mã nguồn runtime (`TADSS.Application/Decisions/DepositCalculator.cs`):
`Math.Round(totalPrice × rate, GetMinorUnits(currency), MidpointRounding.AwayFromZero)`; số chữ số thập phân theo tiền tệ: `VND` 0, `EUR`/`USD` và mọi
tiền tệ khác 2; `rate` giữ nguyên (tối đa 4 chữ số thập phân), không làm tròn. Với số không âm, AwayFromZero tương đương `ROUND_HALF_UP` của `Decimal`,
nên module này dùng `Decimal` + `ROUND_HALF_UP`, không dùng `round()` của Python (làm tròn về số chẵn, và sai khi qua float).

Số trong response phải được đọc bằng `parse_json` (số thực thành `Decimal`, giữ nguyên chữ số như `40.0`, `86.00`). Nếu caller đã có `float`, số được đổi
qua `Decimal(str(x))` (dạng ngắn nhất khứ hồi của float) để so sánh không sai do biểu diễn nhị phân.

`format: date-time` (VD `evaluatedAt`, `approvalExpiresAt` của response) chỉ được jsonschema kiểm khi cài `rfc3339-validator` (ghim trong requirements.txt);
thiếu gói này thì `_validator` ném `RuntimeError` thay vì lặng lẽ bỏ qua định dạng thời điểm (sẽ nới ngầm Q-09 mục 3).

Đường dẫn schema mặc định `docs/contracts/schemas/v2` của repo; đổi bằng biến môi trường `TADSS_SCHEMA_DIR` (VD khi chạy trong image evaluator).
"""
from __future__ import annotations

import json
import os
from decimal import ROUND_HALF_UP, Decimal, localcontext
from functools import lru_cache
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator

SCHEMA_DIR_ENV = "TADSS_SCHEMA_DIR"
DEFAULT_SCHEMA_DIR = Path(__file__).resolve().parents[2] / "docs" / "contracts" / "schemas" / "v2"
STATUS_OK = 200
APPROVED = "APPROVED"
MONEY_PRECISION = 50  # đủ rộng để totalPrice × rate luôn chính xác (đầu vào tối đa vài chục chữ số)


def parse_json(text: str | bytes) -> Any:
    """Đọc JSON, số thực thành `Decimal` (giữ nguyên `40.0`, `86.00`); số nguyên vẫn là `int`."""
    return json.loads(text, parse_float=Decimal)


def minor_units(currency: str) -> int:
    """Số chữ số thập phân của tiền tệ, đúng `DepositCalculator.GetMinorUnits` của runtime: `VND` 0, còn lại 2."""
    return 0 if currency == "VND" else 2


def expected_amount(total_price: Any, rate: Any, currency: str) -> Decimal:
    """`round(totalPrice × rate)` theo số chữ số thập phân của tiền tệ, làm tròn nửa lên (tương đương AwayFromZero với số không âm)."""
    with localcontext() as context:
        context.prec = MONEY_PRECISION
        product = _decimal(total_price) * _decimal(rate)
        return product.quantize(Decimal(1).scaleb(-minor_units(currency)), rounding=ROUND_HALF_UP)


def request_errors(body: Any) -> list[str]:
    """Lỗi của request theo `decision-request.schema.json`; rỗng nghĩa là hợp lệ. Không sửa `body`."""
    return [f"SCHEMA: {error}" for error in _schema_errors("decision-request", body)]


def response_errors(request: dict[str, Any], status: int, body: Any) -> list[str]:
    """Lỗi contract của một response cuối cùng (Q-09); rỗng nghĩa là hợp lệ.

    `status` khác 200 thì chỉ báo lỗi trạng thái (không kiểm body). `body` đã parse (nên dùng `parse_json`)."""
    if status != STATUS_OK:
        return [f"STATUS: HTTP {status}, kỳ vọng {STATUS_OK}"]
    if not isinstance(body, dict):
        return ["SCHEMA: body 200 không phải object JSON"]
    errors = [f"SCHEMA: {error}" for error in _schema_errors("decision-result", body)]
    errors.extend(_rule_errors(request, body))
    return errors


def _rule_errors(request: dict[str, Any], body: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    status = body.get("status")
    deposit = body.get("deposit")
    if status == APPROVED and deposit is None:
        errors.append("RULE: status APPROVED phải có deposit")
    if status != APPROVED and deposit is not None:
        errors.append(f"RULE: status {status} không được có deposit")
    if body.get("reservationId") != request.get("reservationId"):
        errors.append("RULE: reservationId của response không khớp request")
    if isinstance(deposit, dict):
        errors.extend(_deposit_errors(request, deposit))
    return errors


def _deposit_errors(request: dict[str, Any], deposit: dict[str, Any]) -> list[str]:
    currency = request.get("currency")
    if deposit.get("currency") != currency:
        return [f"RULE: deposit.currency {deposit.get('currency')} khác currency của request {currency}"]
    try:
        expected = expected_amount(request.get("totalPrice"), deposit.get("rate"), str(currency))
        actual = _decimal(deposit.get("amount"))
    except (ArithmeticError, TypeError, ValueError):
        return ["RULE: không tính được amount vì totalPrice, rate hoặc amount không phải số"]
    if actual != expected:
        return [f"RULE: deposit.amount {actual} khác round(totalPrice × rate) = {expected}"]
    return []


def _decimal(value: Any) -> Decimal:
    if isinstance(value, bool) or value is None:
        raise TypeError("không phải số")
    if isinstance(value, Decimal):
        return value
    return Decimal(str(value))


def _schema_errors(name: str, instance: Any) -> list[str]:
    validator = _validator(name, os.environ.get(SCHEMA_DIR_ENV, ""))
    ordered = sorted(validator.iter_errors(instance), key=lambda error: ([str(part) for part in error.absolute_path], error.message))
    return [f"{_location(error.absolute_path)}: {error.message}" for error in ordered]


def _location(path: Any) -> str:
    return "/".join(str(part) for part in path) or "(gốc)"


@lru_cache(maxsize=None)
def _validator(name: str, schema_dir_override: str) -> Draft202012Validator:
    schema_dir = Path(schema_dir_override) if schema_dir_override else DEFAULT_SCHEMA_DIR
    schema = json.loads((schema_dir / f"{name}.schema.json").read_text(encoding="utf-8"))
    format_checker = Draft202012Validator.FORMAT_CHECKER
    if "date-time" not in format_checker.checkers:
        raise RuntimeError("Thiếu gói rfc3339-validator nên jsonschema không kiểm format date-time; cài tools/evidence/requirements.txt")
    return Draft202012Validator(schema, format_checker=format_checker)
