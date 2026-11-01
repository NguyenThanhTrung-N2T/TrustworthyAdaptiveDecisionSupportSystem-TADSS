"""Kết quả của một lần nạp nguồn (CI, evidence, k6, SC6, Playwright): trạng thái từng mã và từng KPI, kèm commit và nơi xem báo cáo gốc."""
from __future__ import annotations

from dataclasses import asdict, dataclass, field

# Trạng thái mã và ô nhóm (giá trị metric; dashboard đổi thành màu): số lớn hơn "xấu" hơn để gộp nhiều nguồn bằng max
PASSED, UNTESTED, LIMITED, FAILED = 1, 2, 3, 4
STATE_NAMES = {PASSED: "passed", UNTESTED: "untested", LIMITED: "limited", FAILED: "failed"}
KPIS = tuple(f"SC{number}" for number in range(1, 9))


@dataclass
class CodeResult:
    state: int
    passed: int = 0
    failed: int = 0
    skipped: int = 0
    detail: str = ""


@dataclass
class KpiResult:
    state: int
    verdict: str
    value: str = ""


@dataclass
class SourceResult:
    source: str  # ci-be | ci-tadss | ci-ai | evidence | k6 | sc6 | playwright
    commit: str = ""
    run: str = ""
    report: str = ""  # URL lượt CI hoặc thư mục kết quả gốc
    loaded_at: str = ""
    codes: dict[str, CodeResult] = field(default_factory=dict)
    kpis: dict[str, KpiResult] = field(default_factory=dict)
    ignored: list[str] = field(default_factory=list)  # mã không có trong danh mục (vd mã quyết định P6-xx gắn nhầm trait)

    def to_dict(self) -> dict:
        return asdict(self)

    @staticmethod
    def from_dict(data: dict) -> "SourceResult":
        return SourceResult(
            **{key: value for key, value in data.items() if key not in {"codes", "kpis"}},
            codes={code: CodeResult(**value) for code, value in data.get("codes", {}).items()},
            kpis={kpi: KpiResult(**value) for kpi, value in data.get("kpis", {}).items()},
        )


def state_from_counts(passed: int, failed: int) -> int:
    """Mã có ít nhất một ca đỏ ⇒ hỏng; có ca đạt ⇒ đạt; chỉ có ca bỏ qua ⇒ chưa chạy."""
    if failed:
        return FAILED
    return PASSED if passed else UNTESTED


def verdict_state(verdict: str) -> int:
    normalized = verdict.strip().upper()
    if normalized in {"PASS", "PASSED", "OK"}:
        return PASSED
    if normalized in {"FAIL", "FAILED"}:
        return FAILED
    if normalized in {"INFRA_LIMITED", "INCONCLUSIVE", "PARTIAL", "ABORTED"}:
        return LIMITED
    return UNTESTED
