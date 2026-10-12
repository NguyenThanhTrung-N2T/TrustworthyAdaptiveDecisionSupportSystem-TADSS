"""Độ phủ danh mục kiểm thử (`catalogCoverage` của manifest): mã nào có test đạt, mã nào có test đỏ, mã nào chưa có test và vì sao. Danh sách mã đọc từ bảng của methodology (cùng nguồn với CatalogIndex phía C#)."""
from __future__ import annotations

import re
from pathlib import Path

# Mã một hoặc hai đoạn tiền tố: `UT-01`, `SCALE-2`, `E2E-13`, `BE-UT-01`, `BE-IT-20`
_CODE_ROW = re.compile(r"^\|\s*([A-Z][A-Z0-9]*(?:-[A-Z]{2})?-\d{1,2}[a-z]?)\s*\|", re.MULTILINE)
# Mã nằm ngoài phạm vi lượt đánh giá TADSS đơn lẻ: tiền tố hoặc mã cụ thể → lý do
NOT_IN_SCOPE = (
    (re.compile(r"^(BE-|FE-|E2E-|CT-04$)"), "thuộc SmashCourt-BE/FE, Phase 6–8 (không thuộc lượt đánh giá TADSS)"),
    (re.compile(r"^LT-04$"), "E2E qua SmashCourt-BE, Phase 6"),
    (re.compile(r"^FI-1[1-4]$"), "kịch bản E2E với SmashCourt-BE, Phase 6"),
    (re.compile(r"^(RP|PT)-"), "dữ liệu replay/khách sạn (bước e, gói 5.6) chưa chạy hoặc thiếu dữ liệu trong lượt này"),
    (re.compile(r"^NG-4$"), "do người dùng chạy bằng token ngrok của mình"),
)


def catalog_codes(methodology_path: Path) -> list[str]:
    """Mọi mã ở cột đầu của các bảng trong methodology, theo thứ tự xuất hiện, không trùng."""
    seen: dict[str, None] = {}
    for code in _CODE_ROW.findall(methodology_path.read_text(encoding="utf-8")):
        seen.setdefault(code, None)
    return list(seen)


def group_of(code: str) -> str:
    return code.split("-", 1)[0]


def coverage(codes: list[str], summary: dict[str, dict[str, int]], verified_by_steps: set[str], failed_by_steps: set[str] | frozenset[str] = frozenset()) -> dict[str, object]:
    """
    `summary`: kết quả test theo mã (`catalog.summarize`). `verified_by_steps`: mã được chính evaluator kiểm (k6, Schemathesis, Stryker, dashboard…), không qua trx.
    `failed_by_steps`: mã được chính evaluator kiểm và KHÔNG đạt (VD RP-01 có lỗi contract); thuộc `failed`.
    Trả `{passed: [...], failed: [...], untested: {mã: lý do}}`; mã có cả ca đạt lẫn đỏ thuộc `failed`.
    """
    passed, failed, untested = [], [], {}
    for code in codes:
        counts = summary.get(code)
        if (counts and counts["failed"]) or code in failed_by_steps:
            failed.append(code)
        elif (counts and counts["passed"]) or code in verified_by_steps:
            passed.append(code)
        else:
            untested[code] = next((reason for pattern, reason in NOT_IN_SCOPE if pattern.match(code)), "chưa có test trong lượt chạy này (xem báo cáo gói 5.5)")
    unknown = sorted(set(summary) - set(codes))
    return {"passed": passed, "failed": failed, "untested": untested, "unknownCodes": unknown}


def by_group(codes: list[str], coverage_result: dict[str, object]) -> list[dict[str, object]]:
    """Bảng theo nhóm mã: số mã đạt / đỏ / chưa có test."""
    groups: dict[str, dict[str, int]] = {}
    for code in codes:
        bucket = groups.setdefault(group_of(code), {"passed": 0, "failed": 0, "untested": 0})
        if code in coverage_result["passed"]:  # type: ignore[operator]
            bucket["passed"] += 1
        elif code in coverage_result["failed"]:  # type: ignore[operator]
            bucket["failed"] += 1
        else:
            bucket["untested"] += 1
    return [{"group": group, **counts, "total": sum(counts.values())} for group, counts in groups.items()]
