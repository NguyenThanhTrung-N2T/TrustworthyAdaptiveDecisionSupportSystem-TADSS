"""Mục PT-03 (kinh tế mô phỏng các chính sách cọc) và PT-04 (đối soát và ghép cặp shadow) của báo cáo, dựng từ `raw/d5/policy_economics.json`.
Số liệu là chỉ số mô phỏng theo kịch bản, không phải kết quả tài chính. Hai biến thể hoàn tiền luôn đi cạnh nhau (không biến thể nào là "chính"); `S-SC` là khoảng giữa hai cận;
`AS_IS_BE` đứng riêng và không dùng để xếp hạng; câu kết luận xếp hạng lấy nguyên văn từ `ranking.statement`."""
from __future__ import annotations

import json
import math
from pathlib import Path
from typing import Any

from evidence import policy_economics as pe, policy_metrics as pm, shadow_pairing as sp
from evidence.report.charts import scatter_chart

MISSING_REASON = "thiếu raw/d5/policy_economics.json (bước f chưa chạy hoặc chưa dựng được D5)"
MALFORMED_REASON = "raw/d5/policy_economics.json đọc được nhưng thiếu khóa hoặc sai cấu trúc"
UNREADABLE_REASON = "raw/d5/policy_economics.json không đọc được (không phải JSON hợp lệ)"
SC_UNAVAILABLE_REASON = "chính sách hủy S-SC (ảnh chụp chính sách thiếu hoặc lỗi nên các kịch bản S-SC* không tính được; S-FULL và S-NOSHOW vẫn có)"
RECONCILIATION_UNAVAILABLE_REASON = "đối soát shadow không có trong lượt này (shadow tắt hoặc bước đối soát chưa chạy)"
POLICY_LABELS = {
    pe.NO_DEPOSIT: "Không thu cọc",
    pe.STATIC_100: "Mốc tham chiếu cọc 100% (bằng mức fallback)",
    pe.ORACLE: "Mốc lý tưởng (dùng kết quả đã biết)",
}
VARIANT_SHORT = {pm.REFUND_OF_PAID: "PAID", pm.REFUND_OF_TOTAL_CAPPED: "CAPPED"}
X_LABEL = "Nghĩa vụ ứng trước của khách hoàn tất (proxy)"
Y_LABEL = "Tỷ lệ cọc giữ lại mô phỏng (S-SC)"
NO_VALUE = "-"


def _num(value: float | None, digits: int = 3) -> str:
    return NO_VALUE if value is None else f"{value:.{digits}f}"


def _percent(value: float | None) -> str:
    return NO_VALUE if value is None else f"{value:.1%}"


def _pick(node: Any, *path: str) -> Any:
    """Đi theo `path` trong dict lồng nhau; thiếu khóa hoặc nút không phải dict → None (CI có thể là `{"status": ...}` khi thiếu dữ liệu)."""
    for key in path:
        node = node.get(key) if isinstance(node, dict) else None
    return node


def _interval(interval: Any) -> str:
    """CI 95% dạng `[a; b]`; giá trị chữ (NOT_APPLICABLE, INSUFFICIENT_DATA) in nguyên văn."""
    if isinstance(interval, str):
        return interval
    if not isinstance(interval, dict) or interval.get("low") is None or interval.get("high") is None:
        return NO_VALUE
    return f"[{interval['low']:.3f}; {interval['high']:.3f}]"


def _auc_text(entry: dict[str, Any] | None, interval: Any) -> str:
    auc = (entry or {}).get("auc") or {}
    if auc.get("value") is None:
        return f"không xác định ({auc['reason']})" if auc.get("reason") else NO_VALUE
    text = _num(auc["value"])
    if isinstance(interval, dict):
        return f"{text} {_interval(interval)}"
    if (entry or {}).get("oracle_by_definition"):
        return f"{text} (theo định nghĩa của mốc lý tưởng, không có CI)"
    return f"{text} ({interval})" if isinstance(interval, str) else text


def _span(values: list[float | None], digits: int = 3) -> str:
    """`min…max` của các giá trị; thiếu bất kỳ giá trị nào thì không dựng khoảng (không coi thiếu là 0)."""
    if not values or any(value is None for value in values):
        return NO_VALUE
    return f"{min(values):.{digits}f}…{max(values):.{digits}f}"


def _conventions(policy: str) -> tuple[float, ...]:
    """Chính sách TADSS báo theo cả hai quy ước NEED_APPROVAL (r = 1 và r = 0); mốc tham chiếu không có NEED_APPROVAL nên chỉ một quy ước."""
    return pe.NA_CONVENTIONS if policy in pe.TADSS_POLICIES else (pe.NA_MAIN,)


def _by_convention(policy: str, per_na: dict[float, str]) -> str:
    """Dòng chi tiết dưới ô: giá trị theo từng quy ước NEED_APPROVAL; mốc tham chiếu ghi không áp dụng."""
    if policy not in pe.TADSS_POLICIES:
        return "NEED_APPROVAL: không áp dụng"
    return "; ".join(f"NEED_APPROVAL = {na:g}: {per_na[na]}" for na in pe.NA_CONVENTIONS)


def _table(document: dict[str, Any], point: dict[tuple, float | None]) -> list[dict[str, Any]]:
    """Hàng = 4 chính sách so sánh cộng `ORACLE`; mỗi biến thể hoàn tiền một ô `S-SC`. Chính sách TADSS: khoảng `min…max` trên hai cận ngày hủy VÀ hai quy ước NEED_APPROVAL
    (thấy cả hai đầu), kèm giá trị riêng từng quy ước. CI 95% chỉ có cho NEED_APPROVAL = 1 (bootstrap chạy ở quy ước này)."""
    ci = document.get("ci")
    burden = {(item["policy"], item["na"]): item["value"] for item in document.get("burden", [])}
    discrimination = {item["policy"]: item for item in document.get("discrimination", [])}
    rows: list[dict[str, Any]] = []
    for policy in pe.POLICIES:
        variants = []
        for variant in pm.VARIANTS:
            per_na = {na: [point.get((pe.S_SC, variant, bound, na, policy)) for bound in pe.BOUNDS] for na in _conventions(policy)}
            variants.append({"variant": variant, "range": _span([value for values in per_na.values() for value in values]),
                             "detail": _by_convention(policy, {na: _span(values) for na, values in per_na.items()}),
                             "ci_low": _interval(_pick(ci, variant, "low", "retention", policy)), "ci_high": _interval(_pick(ci, variant, "high", "retention", policy))})
        first = pm.VARIANTS[0]  # nghĩa vụ ứng trước và AUC không phụ thuộc biến thể hay cận ngày hủy (CI được lưu lặp ở mọi ô) → đọc ở ô (biến thể đầu, cận low)
        rows.append({"policy": policy, "label": POLICY_LABELS.get(policy, ""), "variants": variants,
                     "burden": _span([burden.get((policy, na)) for na in _conventions(policy)]),
                     "burden_detail": _by_convention(policy, {na: _num(burden.get((policy, na))) for na in pe.NA_CONVENTIONS}),
                     "burden_ci": _interval(_pick(ci, first, "low", "burden", policy)),
                     "auc": _auc_text(discrimination.get(policy), _pick(ci, first, "low", "auc", policy)), "delta_r": _num((discrimination.get(policy) or {}).get("delta_r"))})
    return rows


def _sign(default: float | None, mock: float | None) -> str:
    if default is None or mock is None:
        return NO_VALUE
    if math.isclose(default, mock, rel_tol=1e-12, abs_tol=1e-12):
        return "="
    return ">" if default > mock else "<"


def _sensitivity(ranking: dict[str, Any], point: dict[tuple, float | None]) -> list[dict[str, str]]:
    """Mỗi (kịch bản, biến thể, cận) một dòng: tỷ lệ giữ lại của hai chính sách TADSS ở CẢ HAI quy ước NEED_APPROVAL và dấu `DEFAULT ? MOCKALT` ở từng quy ước
    (xếp hạng chính thức và CI 95% của hiệu chỉ có cho NEED_APPROVAL = 1)."""
    rows = []
    for entry in ranking.get("by_case", []):
        zero = entry.get("ci_contains_zero")
        key = (entry["scenario"], entry["variant"], entry["bound"], pe.NA_LOWER)
        default0, mock0 = point.get((*key, pe.TADSS_DEFAULT)), point.get((*key, pe.TADSS_SHADOW_MOCKALT))
        rows.append({"scenario": entry["scenario"], "variant": entry["variant"], "bound": entry["bound"],
                     "default": _num(entry[pe.TADSS_DEFAULT]), "mock": _num(entry[pe.TADSS_SHADOW_MOCKALT]), "sign": entry["sign"] or NO_VALUE,
                     "default0": _num(default0), "mock0": _num(mock0), "sign0": _sign(default0, mock0),
                     "diff_ci": _interval(entry.get("diff_ci")), "zero": NO_VALUE if zero is None else "chứa 0" if zero else "không chứa 0"})
    return rows


def _bias(bias: dict[str, Any]) -> dict[str, Any]:
    groups = []
    for name, key in (("PAIRED (tập so sánh)", "paired"), ("Ngoài PAIRED", "non_paired")):
        group = bias.get(key) or {}
        groups.append({"name": name, "rows": group.get("rows", 0), "cancel_rate": _percent(group.get("cancel_rate")), "mean_price": _num(group.get("mean_price"), 2),
                       "weekend_share": _percent(group.get("weekend_share")), "mean_position_share": _percent(group.get("mean_position_share"))})
    return {**bias, "groups": groups, "share_text": _percent(bias.get("non_paired_share")), "threshold_text": _percent(bias.get("threshold"))}


def _reconciliation(reconciliation: dict[str, Any] | None) -> dict[str, Any] | None:
    if not reconciliation:
        return None
    delta = reconciliation.get("delta")
    delta_text = NO_VALUE if not delta else ", ".join(f"{key} {value}" for key, value in delta.items())
    shown = {key: NO_VALUE if reconciliation.get(key) is None else reconciliation[key] for key in ("primary", "shadow", "residual", "statement")}  # chỉ có label+reason thì không để ô trống (số 0 thật vẫn giữ)
    return {**reconciliation, **shown, "delta_text": delta_text}


def _chart(point: dict[tuple, float | None], burden: list[dict[str, Any]]) -> str:
    """Mỗi chính sách hai điểm (hai biến thể, NEED_APPROVAL = 1); x = nghĩa vụ ứng trước, y = tỷ lệ cọc giữ lại `S-SC` ở cận low, đoạn đứng nối tới cận high."""
    x_by_policy = {item["policy"]: item["value"] for item in burden if item["na"] == pe.NA_MAIN}
    points: list[tuple[str, float, float]] = []
    spans: dict[str, tuple[float, float]] = {}
    for policy in pe.POLICIES:
        for variant in pm.VARIANTS:
            x = x_by_policy.get(policy)
            low, high = (point.get((pe.S_SC, variant, bound, pe.NA_MAIN, policy)) for bound in pe.BOUNDS)
            if x is not None and low is not None and high is not None:
                label = f"{policy} · {VARIANT_SHORT[variant]}"
                points.append((label, x, low))
                spans[label] = (low, high)
    return scatter_chart(points, X_LABEL, Y_LABEL, spans)


def economics_section(run_dir: Path) -> dict[str, Any]:
    """PT-03/PT-04 từ `raw/d5/policy_economics.json`; thiếu hoặc hỏng file thì chỉ trả lý do."""
    path = run_dir / "raw" / "d5" / "policy_economics.json"
    if not path.exists():
        return {"available": False, "reason": MISSING_REASON}
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except ValueError:
        return {"available": False, "reason": UNREADABLE_REASON}
    if not isinstance(document, dict):
        return {"available": False, "reason": UNREADABLE_REASON}
    try:
        return _build(document)
    except (KeyError, TypeError, AttributeError, ValueError, IndexError) as error:  # file đọc được nhưng thiếu khóa/sai kiểu: ghi lý do thay vì làm hỏng cả báo cáo
        return {"available": False, "reason": f"{MALFORMED_REASON} ({type(error).__name__}: {error})"}


def _build(document: dict[str, Any]) -> dict[str, Any]:
    point = {(item["scenario"], item["variant"], item["bound"], item["na"], item["policy"]): item["retention"] for item in document.get("point", [])}
    ranking = document.get("ranking") or {}
    shadow = document.get("shadow") or {}
    ci = document.get("ci") or {}
    cancel_policy = document.get("cancel_policy") or {}
    assumptions = document.get("assumptions") or {}
    counts = document.get("counts") or {}
    crosstab = document.get("crosstab") or {}
    columns = list(next(iter(crosstab.values()), {}))
    return {
        "available": True, "reason": "", "s_sc_reason": SC_UNAVAILABLE_REASON, "reconciliation_reason": RECONCILIATION_UNAVAILABLE_REASON, "note": document.get("note", ""), "s_sc_limit": document.get("s_sc_limit", ""),
        "assumptions": f"giờ đến {assumptions.get('check_in_local', '-')} ({assumptions.get('time_zone', '-')}) là giả định vì dataset chỉ có ngày đến; thời điểm hủy trong ngày lấy hai cận {' và '.join(assumptions.get('cancel_time_bounds', []))}",
        "counts": counts, "excluded": ", ".join(f"{reason}: {count}" for reason, count in (counts.get("excluded") or {}).items()) or "không",
        "crosstab": {"columns": columns, "rows": [{"label": f"is_canceled = {label}", "cells": [crosstab[label].get(column, 0) for column in columns]} for label in crosstab]},
        "s_sc_available": "source" in cancel_policy,
        "policy_source": f"{cancel_policy.get('source', '-')} (lấy ngày {cancel_policy.get('retrieved_on', '-')})",
        "tiers": [{"bucket": item["bucket"], "count": item["count"], "value": _num(item["value"], 2), "share": _percent(item["share_of_E"])} for item in document.get("tier_distribution", [])],
        "table": _table(document, point), "sensitivity": _sensitivity(ranking, point),
        "ranking": {"statement": ranking.get("statement", ""), "stable": ranking.get("stable"), "indistinguishable": ranking.get("indistinguishable")},
        "bootstrap": f"seed {ci['seed']}, {ci['iterations']} vòng, bỏ {ci['skipped']} vòng không hợp lệ; AUC bỏ thêm: {ci.get('auc_skipped') or 'không'}" if "iterations" in ci else "không có CI (thiếu S-SC hoặc không còn mẫu hợp lệ)",
        "need_approval": [{"policy": item["policy"], "count": item["count"], "rate": _percent(item["rate"])} for item in document.get("need_approval", [])],
        "as_is_be": [{"policy": item["policy"], "bound": item["bound"], "count": item["count"], "value": _num(item["value"], 2),
                      "by_pct": ", ".join(f"hoàn {float(pct):.0%}: {count} đơn, {_num(value, 2)}" for pct, (count, value) in item["by_pct"].items()) or NO_VALUE}
                     for item in document.get("as_is_be", [])],
        "reconciliation": _reconciliation(shadow.get("reconciliation")), "pairing": shadow.get("pairing"), "pairing_states": sp.STATES, "violations": shadow.get("violations"),
        "bias": _bias(document.get("pairing_bias") or {}), "chart": _chart(point, document.get("burden", [])),
    }
