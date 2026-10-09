"""Dựng lại `report.html` từ một Evidence Pack có sẵn (Q-10), dùng CÙNG bộ sinh báo cáo của evaluator.

Quy trình: đọc `manifest.json` -> chạy lại EV-1…EV-5 (EV-1 bỏ qua `report.html` vì chính nó sắp được dựng lại; EV-4 không có danh sách UUID gốc nên chỉ quét cột và bí mật)
-> ghi `report.html` -> cập nhật trong manifest CHỈ băm của `report.html` và kết quả `evChecks`. Không sửa file thô và không tính lại băm của chúng: pack bị sửa
sau khi dựng sẽ vẫn có EV-1 không đạt (báo cáo ghi "không dùng làm minh chứng"), không được hợp thức hóa bằng cách dựng lại.
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from evidence import coverage_map, evidence_pack
from evidence.report import generator

REPORT_NAME = "report.html"


class PackError(RuntimeError):
    """Thư mục không phải Evidence Pack hợp lệ (thiếu hoặc hỏng `manifest.json`)."""


def read_manifest(run_dir: Path) -> dict[str, Any]:
    path = run_dir / evidence_pack.MANIFEST_NAME
    if not path.is_file():
        raise PackError(f"không có {evidence_pack.MANIFEST_NAME} trong {run_dir}")
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        raise PackError(f"{evidence_pack.MANIFEST_NAME} không phải JSON hợp lệ: {error}") from error


def _keep_original_uuid_scan(rechecked: evidence_pack.EvCheck, manifest: dict[str, Any]) -> evidence_pack.EvCheck:
    """CLI không có danh sách UUID gốc (chỉ nằm trong tiến trình evaluator) nên không quét lại được. Nếu EV-4 của lượt gốc ĐÃ quét UUID gốc thì giữ kết quả đó (đạt/không đạt và chi tiết),
    chỉ kết hợp với phần CLI kiểm lại được (cột cá nhân, chuỗi bí mật): không để kết quả yếu hơn ("chưa quét") ghi đè bằng chứng mạnh hơn."""
    stored = next((check for check in manifest.get("evChecks") or [] if check.get("code") == "EV-4"), None)
    if stored is None or (stored.get("passed") and "đã quét" not in str(stored.get("detail", ""))):
        return rechecked  # lượt gốc cũng không quét UUID (hoặc pack cũ): không có gì mạnh hơn để giữ. Kết quả KHÔNG ĐẠT của lượt gốc luôn được giữ
    detail = rechecked.detail if not rechecked.passed else str(stored["detail"])
    return evidence_pack.EvCheck("EV-4", bool(stored.get("passed")) and rechecked.passed, f"{detail}; quét UUID gốc: giữ kết quả lượt gốc")


def rebuild_report(run_dir: Path) -> tuple[list[evidence_pack.EvCheck], Path]:
    """Dựng lại báo cáo và cập nhật manifest; trả (kết quả EV-1…EV-5 cuối cùng, đường dẫn báo cáo)."""
    manifest = read_manifest(run_dir)
    main, *extra = evidence_pack.collect_sources(run_dir)
    checks = evidence_pack.run_checks(run_dir, manifest, main.d1_rows, main.entries, extra_sources=extra, main_name=main.name)
    checks[0] = evidence_pack.check_ev1(run_dir, manifest, ignore=(REPORT_NAME,))
    checks[3] = _keep_original_uuid_scan(checks[3], manifest)

    coverage = manifest.get("catalogCoverage") or {"passed": [], "failed": [], "untested": {}, "unknownCodes": []}
    codes = [*coverage.get("passed", []), *coverage.get("failed", []), *coverage.get("untested", {})]
    data = generator.build_data(run_dir, manifest, [check.to_dict() for check in checks], manifest.get("kpis", []), coverage, coverage_map.by_group(codes, coverage))
    report = run_dir / REPORT_NAME
    report.write_text(generator.render(data), encoding="utf-8")

    entry = {"path": REPORT_NAME, "sha256": evidence_pack.sha256_file(report), "rows": None}
    files = [item for item in manifest.get("files", []) if item["path"] != REPORT_NAME]
    manifest["files"] = sorted([*files, entry], key=lambda item: item["path"])
    checks[0] = evidence_pack.check_ev1(run_dir, manifest)
    manifest["evChecks"] = [check.to_dict() for check in checks]
    evidence_pack.write_manifest(run_dir, manifest)
    return checks, report
