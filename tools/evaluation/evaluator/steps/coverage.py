"""Coverage (O-20): gộp các file cobertura của bước (a) bằng ReportGenerator thành HTML + cobertura gộp + tóm tắt văn bản."""
from __future__ import annotations

import re
from pathlib import Path
from typing import Callable

from evaluator.steps.tests_step import run_command

Command = Callable[[list[str], Path, dict[str, str], Path], int]
_LINE_COVERAGE = re.compile(r"Line coverage:\s*([\d.,]+)%")
_BRANCH_COVERAGE = re.compile(r"Branch coverage:\s*([\d.,]+)%")
EXCLUDED_ASSEMBLIES = "-TADSS.UnitTests;-TADSS.IntegrationTests"


def find_cobertura(tests_dir: Path) -> list[Path]:
    """Mọi `coverage.cobertura.xml` coverlet ghi dưới thư mục kết quả test (mỗi lượt `dotnet test` một thư mục GUID)."""
    return sorted(tests_dir.rglob("coverage.cobertura.xml"))


def summarize(summary_text: str) -> dict[str, float | None]:
    """Đọc tỉ lệ dòng/nhánh từ `Summary.txt` của ReportGenerator (dấu phẩy hoặc chấm thập phân)."""
    result: dict[str, float | None] = {"line": None, "branch": None}
    for key, pattern in (("line", _LINE_COVERAGE), ("branch", _BRANCH_COVERAGE)):
        match = pattern.search(summary_text)
        if match:
            result[key] = float(match.group(1).replace(",", "."))
    return result


def generate(work_dir: Path, tests_dir: Path, out_dir: Path, env: dict[str, str], command: Command = run_command) -> tuple[dict[str, float | None] | None, list[str], list[str]]:
    """Trả `(tỉ lệ, ghi chú, tệp)`; không có cobertura (chưa chạy bước a) hoặc ReportGenerator lỗi ⇒ tỉ lệ None kèm lý do."""
    files = find_cobertura(tests_dir)
    if not files:
        return None, ["Coverage bỏ qua: chưa có coverage.cobertura.xml (chạy kèm bước a, ví dụ EVAL_STEPS=a,d)"], []
    out_dir.mkdir(parents=True, exist_ok=True)
    log = out_dir.parent / "coverage-reportgenerator.log"
    code = command(
        [
            "dotnet", "tool", "run", "reportgenerator",
            f"-reports:{';'.join(str(file) for file in files)}",
            f"-targetdir:{out_dir}",
            "-reporttypes:Html;Cobertura;TextSummary",
            f"-assemblyfilters:{EXCLUDED_ASSEMBLIES}",
        ],
        work_dir,
        env,
        log,
    )
    summary = out_dir / "Summary.txt"
    if code != 0 or not summary.exists():
        return None, [f"ReportGenerator thất bại (mã {code}, xem {log.name})"], [str(log)]
    rates = summarize(summary.read_text(encoding="utf-8", errors="replace"))
    return rates, [f"Coverage gộp từ {len(files)} file: dòng {rates['line']} %, nhánh {rates['branch']} % (HTML ở {out_dir.name}/)"], [str(summary), str(out_dir / "index.html")]
