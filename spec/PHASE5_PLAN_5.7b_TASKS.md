# Gói 5.7b — Kinh tế mô phỏng chính sách cọc (PT-03) + shadow trong đánh giá (PT-04): Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bước (e)/(f) của evaluator chạy shadow `MockAlternative` song song với `Default` trên dữ liệu khách sạn, đối soát shadow theo `correlation_id`, rồi tính chỉ số kinh tế **mô phỏng** theo chính sách hủy thật của SmashCourt và ghi `raw/d5/policy_economics.json` + mục mới trong `report.html`.

**Architecture:**
- **Không** đổi `src/`, SDK, contract 2.0.0 hay migration.
- **Shadow** bật bằng biến môi trường của riêng `eval-tadss-portability`.
- **Logic thuần** đặt ở `tools/evidence/`, mỗi file một trách nhiệm:
  - `cancel_policy.py` — chính sách hủy và thuật toán chọn mốc của BE;
  - `policy_metrics.py` — công thức `φ`, `AS_IS_BE`, AUC, percentile;
  - `shadow_pairing.py` — ghép `PRIMARY`/`SHADOW`;
  - `policy_economics.py` — tổng hợp và bootstrap ghép cặp.
- **Phần có I/O** (xả hàng đợi shadow, đọc Prometheus/PostgreSQL) đặt ở `tools/evaluation/evaluator/shadow_drain.py`, gọi từ bước (e).
- Bước (f) ghép D1, dựng D5 có cột shadow, ghi `policy_economics.json`, báo cáo.

**Tech Stack:**
- Python 3.11, thư viện chuẩn (`decimal`, `math.fsum`, `random.Random`, `collections.Counter`, `zoneinfo`).
- Có sẵn: pytest, Jinja2, psycopg (đã có trong image evaluator).
- Docker Compose. Không thêm package nào.

**Spec:** file đặc tả riêng đã gỡ (15/10/2026, theo yêu cầu người dùng); nội dung thiết kế rev 3.3 đã chốt nằm trong chính plan này (Goal, Architecture, Global Constraints, các Task) và trong tài liệu `docs/` (methodology PT-03/PT-04, DATASET_SCHEMA).

## Global Constraints

- **Không** sửa `src/**`, `docs/contracts/**`, migration; **không** sửa SmashCourt-BE (spec 4.6).
- **Không** chạy `git commit/push/...`: người dùng tự commit (`../.claude/rules/git-safety.md`). Mỗi task kết thúc ở "đã sửa + test xanh". `spec/` không stage.
- **Không** thêm package Python; `requirements.txt` giữ nguyên.
- Chạy pytest **từng bộ riêng**:
  - `python -m pytest -q tools/evidence`
  - `python -m pytest -q tools/evaluation`
  - `python -m pytest -q tools/docs`
- Sửa `docs/**` thì `python tools/docs/check_docs.py` phải thoát 0.
- Chỉ số là **mô phỏng theo kịch bản**. Tên trong output và báo cáo theo đúng bảng 4.3 của spec; không dùng "tiền mất", "doanh thu mất".
- Hai biến thể hoàn tiền `REFUND_OF_PAID`, `REFUND_OF_TOTAL_CAPPED` luôn được báo **ngang hàng**. `AS_IS_BE` **không** có `φ` và không vào xếp hạng.
- Nhãn hủy `is_canceled` chỉ dùng cho AUC và `Δr`. Sự kiện giữ cọc lấy từ `reservation_status`.
- So sánh chính sách chỉ trên tập `PAIRED`. Fallback ở bất kỳ phía nào không được tính là quyết định của strategy.
- Giá trị thiếu hoặc lỗi **không bao giờ** đổi thành 0: loại và đếm theo lý do.
- Comment và docstring viết tiếng Việt như mã xung quanh; định danh viết tiếng Anh.
- Shell trong repo: file `.sh` phải là LF; sửa file dùng công cụ Edit, không `sed -i`.

## Cấu trúc đích

| File | Trách nhiệm | Task |
| :--- | :--- | :---: |
| `tools/evidence/config/smashcourt_cancel_policies.json` (mới) | Ảnh chụp chính sách hủy, có nguồn và ngày lấy | 1 |
| `tools/evidence/cancel_policy.py` (mới) | Nạp ảnh chụp, chọn mốc như BE, số giờ còn lại theo hai cận, giãn mốc | 1 |
| `tools/evidence/policy_metrics.py` (mới) | `φ` hai biến thể, `AS_IS_BE`, AUC có ties, `Δr`, percentile | 2 |
| `tools/evidence/hotel_adapter.py` | Đọc `reservation_status(_date)`, thêm vào meta và D5 | 3 |
| `tools/evidence/shadow_pairing.py` (mới) | Ghép theo `correlation_id`, bốn trạng thái, kiểm toàn vẹn, gắn cột shadow vào D5 | 4 |
| `tools/evidence/dataset_analysis.py` | `build_d5_with_shadow`, sửa docstring `d1_index`, `dataset_coverage` thêm PT-03/PT-04 | 4, 7 |
| `tools/evidence/policy_economics.py` (mới) | Chuẩn bị dòng, kịch bản, điểm ước lượng, bootstrap ghép cặp, xếp hạng, bias | 5 |
| `docker-compose.yml` | Env shadow của `eval-tadss-portability` | 6 |
| `tools/evaluation/evaluator/shadow_drain.py` (mới) | Chụp counter, xả hàng đợi, nhãn đối soát | 6 |
| `tools/evaluation/evaluator/steps/dataset_step.py` | Gọi chụp counter trước khi gửi PT, xả sau khi gửi, ghi `shadow_reconciliation` | 6 |
| `tools/evaluation/evaluator/steps/evidence_step.py` | D5 có shadow, `policy_economics.json`, sao chép ảnh chụp vào `config/` | 7 |
| `tools/evidence/report/{generator.py,charts.py,template.html.j2}` | Mục PT-03/PT-04, biểu đồ phân tán | 8 |
| `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`, `DATASET_SCHEMA_SPECIFICATION.md`, `docs/guides/EVALUATION_RUN_GUIDE.md`, `spec/PHASE5_PLAN_5.7_M5_FREEZE.md` | PT-03/PT-04, cột D5, file mới, M-10 thu hẹp | 9 |

**Ghi chú về image evaluator:**

- `docker/evaluator/Dockerfile.dockerignore` chỉ cho `tools/evidence/*.py` và `report/*` vào image, nên ảnh chụp JSON **không** nằm trong image.
- Evaluator đọc nó từ repo mount chỉ đọc qua `context.repo_dir / cancel_policy.SNAPSHOT_RELATIVE`, giống cách đang đọc `appsettings.json` (`dataset_analysis.APPSETTINGS_RELATIVE`).
- Vì vậy **không** sửa dockerignore.

---

### Task 1: Ảnh chụp chính sách hủy và `cancel_policy.py`

**Files:**
- Create: `tools/evidence/config/smashcourt_cancel_policies.json`
- Create: `tools/evidence/cancel_policy.py`
- Test: `tools/evidence/tests/test_cancel_policy.py`

**Interfaces:**
- Produces:
  - `SNAPSHOT_RELATIVE: Path`;
  - `CancelPolicyError(ValueError)`;
  - `@dataclass(frozen=True) Tier(hours_before: int, refund_fraction: Decimal)`;
  - `@dataclass(frozen=True) CancelPolicy(tiers: tuple[Tier, ...], source: str, retrieved_on: str, sha256: str)`, trong đó `tiers` sắp theo `hours_before` giảm dần;
  - `load(path: Path) -> CancelPolicy`;
  - `refund_fraction(policy: CancelPolicy, hours_until_start: float) -> Decimal`;
  - `hours_until_start_bounds(start: datetime, cancel_day: date) -> tuple[float, float]` trả (ít nhất, nhiều nhất);
  - `scaled(policy: CancelPolicy, factor: Decimal) -> CancelPolicy`.

- [ ] **Step 1: Tạo ảnh chụp** (số liệu người dùng gửi 14/10/2026, bỏ `id`, spec 4.3)

`tools/evidence/config/smashcourt_cancel_policies.json`:

```json
{
  "source": "SmashCourt-BE, bảng cancel_policies (GET /api/cancel-policies); người dùng gửi 14/10/2026",
  "retrieved_on": "2026-10-14",
  "last_updated_in_source": "2026-03-27",
  "policies": [
    {"hours_before": 24, "refund_percent": "100.00", "description": "Hủy trước 24 giờ hoàn 100%"},
    {"hours_before": 2, "refund_percent": "50.00", "description": "Hủy trước 2 giờ hoàn 50%"},
    {"hours_before": 0, "refund_percent": "0.00", "description": "Hủy trong vòng 2 giờ không hoàn tiền"}
  ]
}
```

- [ ] **Step 2: Viết test thất bại**

`tools/evidence/tests/test_cancel_policy.py`:

```python
"""Chính sách hủy SmashCourt cho mô phỏng PT-03: nạp ảnh chụp, chọn mốc đúng như BookingService.CalculateRefundPercentAsync, hai cận theo độ phân giải ngày."""
import json
from datetime import date, datetime
from decimal import Decimal
from pathlib import Path
from zoneinfo import ZoneInfo

import pytest

from evidence import cancel_policy

REPO = Path(__file__).resolve().parents[3]
LISBON = ZoneInfo("Europe/Lisbon")


def write(tmp_path: Path, policies: list[dict]) -> Path:
    path = tmp_path / "policies.json"
    path.write_text(json.dumps({"source": "test", "retrieved_on": "2026-10-14", "policies": policies}), encoding="utf-8")
    return path


@pytest.fixture()
def policy() -> cancel_policy.CancelPolicy:
    return cancel_policy.load(REPO / cancel_policy.SNAPSHOT_RELATIVE)


def test_load_committed_snapshot_sorts_tiers_and_hashes_file(policy) -> None:
    assert [(tier.hours_before, tier.refund_fraction) for tier in policy.tiers] == [(24, Decimal("1")), (2, Decimal("0.5")), (0, Decimal("0"))]
    assert len(policy.sha256) == 64 and policy.retrieved_on == "2026-10-14"


@pytest.mark.parametrize(
    ("hours", "expected"),
    [
        (30.0, Decimal("1")),     # ≥ 24 giờ
        (24.0, Decimal("1")),     # biên `≤`: đúng 24 giờ vẫn hoàn 100%
        (23.99, Decimal("0.5")),
        (2.0, Decimal("0.5")),    # biên `≤`: đúng 2 giờ hoàn 50%
        (1.99, Decimal("0")),     # mốc 0 giờ
        (0.0, Decimal("0")),
        (-0.01, Decimal("0")),    # đã qua giờ bắt đầu
    ],
)
def test_refund_fraction_matches_backend_algorithm(policy, hours: float, expected: Decimal) -> None:
    assert cancel_policy.refund_fraction(policy, hours) == expected


def test_refund_fraction_without_matching_tier_is_zero(tmp_path: Path) -> None:
    only_day = cancel_policy.load(write(tmp_path, [{"hours_before": 24, "refund_percent": "100"}]))
    assert cancel_policy.refund_fraction(only_day, 5.0) == Decimal("0")


def test_hours_until_start_bounds_cover_whole_cancel_day() -> None:
    start = datetime(2026, 7, 10, 14, 0, tzinfo=LISBON)
    least, most = cancel_policy.hours_until_start_bounds(start, date(2026, 7, 9))
    assert least == pytest.approx(14 + 1 / 60)   # hủy 23:59 ngày liền trước
    assert most == pytest.approx(38.0)           # hủy 00:00 ngày liền trước
    least_same_day, most_same_day = cancel_policy.hours_until_start_bounds(start, date(2026, 7, 10))
    assert least_same_day < 0 < most_same_day == pytest.approx(14.0)


def test_scaled_multiplies_hours_before() -> None:
    policy = cancel_policy.load(REPO / cancel_policy.SNAPSHOT_RELATIVE)
    assert [tier.hours_before for tier in cancel_policy.scaled(policy, Decimal(2)).tiers] == [48, 4, 0]
    assert [tier.hours_before for tier in cancel_policy.scaled(policy, Decimal("0.5")).tiers] == [12, 1, 0]


@pytest.mark.parametrize(
    "policies",
    [
        [],
        [{"hours_before": -1, "refund_percent": "10"}],
        [{"hours_before": 2, "refund_percent": "101"}],
        [{"hours_before": 2, "refund_percent": "x"}],
        [{"hours_before": 2, "refund_percent": "10"}, {"hours_before": 2, "refund_percent": "20"}],
        [{"hours_before": True, "refund_percent": "10"}],
    ],
)
def test_load_rejects_invalid_snapshot(tmp_path: Path, policies: list[dict]) -> None:
    with pytest.raises(cancel_policy.CancelPolicyError):
        cancel_policy.load(write(tmp_path, policies))
```

- [ ] **Step 3: Chạy để xác nhận thất bại**

Run: `python -m pytest -q tools/evidence/tests/test_cancel_policy.py`
Expected: FAIL — `ImportError: cannot import name 'cancel_policy'`

- [ ] **Step 4: Viết `cancel_policy.py`**

```python
"""Chính sách hủy của SmashCourt (bảng `cancel_policies`) cho mô phỏng PT-03 (gói 5.7b, spec 4.3/4.6).

Ảnh chụp có commit ở `tools/evidence/config/smashcourt_cancel_policies.json`; evaluator đọc nó từ repo mount chỉ đọc (`context.repo_dir / SNAPSHOT_RELATIVE`),
giống cách đọc `appsettings.json`. Thuật toán chọn mốc sao chép đúng `BookingService.CalculateRefundPercentAsync` của SmashCourt-BE: đã qua giờ bắt đầu → 0;
mốc có `hours_before` lớn nhất thỏa `hours_before ≤ số giờ còn lại`; không mốc nào khớp → 0. Số giờ còn lại tính đúng múi giờ (không sao chép lỗi +7 giờ nghi vấn B-2).
"""
from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from datetime import date, datetime, time, timezone
from decimal import Decimal, InvalidOperation
from pathlib import Path

SNAPSHOT_RELATIVE = Path("tools") / "evidence" / "config" / "smashcourt_cancel_policies.json"
_HUNDRED = Decimal(100)


class CancelPolicyError(ValueError):
    """Ảnh chụp chính sách hủy thiếu, sai định dạng hoặc ngoài miền giá trị."""


@dataclass(frozen=True)
class Tier:
    """Một mốc: hủy trước ít nhất `hours_before` giờ thì hoàn `refund_fraction` (0…1)."""

    hours_before: int
    refund_fraction: Decimal


@dataclass(frozen=True)
class CancelPolicy:
    """Các mốc sắp theo `hours_before` giảm dần, nguồn, ngày lấy và SHA-256 của file ảnh chụp."""

    tiers: tuple[Tier, ...]
    source: str
    retrieved_on: str
    sha256: str


def load(path: Path) -> CancelPolicy:
    """Nạp ảnh chụp; ném `CancelPolicyError` khi file sai định dạng, rỗng, mốc trùng hoặc giá trị ngoài miền (`hours_before ≥ 0`, `refund_percent` 0…100)."""
    try:
        raw = Path(path).read_bytes()
        document = json.loads(raw.decode("utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise CancelPolicyError(f"Không đọc được ảnh chụp chính sách hủy {path}: {error}") from error
    tiers: list[Tier] = []
    seen: set[int] = set()
    for index, item in enumerate(document.get("policies") or []):
        hours = item.get("hours_before")
        if not isinstance(hours, int) or isinstance(hours, bool) or hours < 0:
            raise CancelPolicyError(f"mốc {index}: hours_before phải là số nguyên ≥ 0, nhận {hours!r}")
        try:
            fraction = Decimal(str(item.get("refund_percent"))) / _HUNDRED
        except InvalidOperation as error:
            raise CancelPolicyError(f"mốc {index}: refund_percent không phải số") from error
        if not fraction.is_finite() or not Decimal(0) <= fraction <= Decimal(1):
            raise CancelPolicyError(f"mốc {index}: refund_percent phải trong 0…100")
        if hours in seen:
            raise CancelPolicyError(f"mốc {index}: hours_before {hours} bị trùng (BE yêu cầu duy nhất)")
        seen.add(hours)
        tiers.append(Tier(hours, fraction))
    if not tiers:
        raise CancelPolicyError("Ảnh chụp không có mốc nào (BE luôn giữ tối thiểu 1 mốc)")
    tiers.sort(key=lambda tier: tier.hours_before, reverse=True)
    return CancelPolicy(tuple(tiers), str(document.get("source", "")), str(document.get("retrieved_on", "")), hashlib.sha256(raw).hexdigest())


def refund_fraction(policy: CancelPolicy, hours_until_start: float) -> Decimal:
    """Tỷ lệ hoàn (0…1) theo đúng thuật toán của BE."""
    if hours_until_start < 0:
        return Decimal(0)
    for tier in policy.tiers:
        if tier.hours_before <= hours_until_start:
            return tier.refund_fraction
    return Decimal(0)


def hours_until_start_bounds(start: datetime, cancel_day: date) -> tuple[float, float]:
    """Dataset chỉ có ngày hủy: (số giờ ít nhất, nhiều nhất) từ lúc hủy tới `start` khi hủy lúc 23:59 và lúc 00:00 của ngày đó, cùng múi giờ với `start`, tính qua UTC."""
    zone = start.tzinfo

    def hours(moment: datetime) -> float:
        return (start.astimezone(timezone.utc) - moment.astimezone(timezone.utc)).total_seconds() / 3600

    return hours(datetime.combine(cancel_day, time(23, 59), tzinfo=zone)), hours(datetime.combine(cancel_day, time(0, 0), tzinfo=zone))


def scaled(policy: CancelPolicy, factor: Decimal) -> CancelPolicy:
    """Bản giãn mốc cho kịch bản độ nhạy `S-SC×2`, `S-SC÷2`: nhân mọi `hours_before` với `factor` (làm tròn tới giờ), giữ tỷ lệ hoàn."""
    tiers = tuple(Tier(int((Decimal(tier.hours_before) * factor).to_integral_value()), tier.refund_fraction) for tier in policy.tiers)
    return CancelPolicy(tiers, policy.source, policy.retrieved_on, policy.sha256)
```

- [ ] **Step 5: Chạy lại test**

Run: `python -m pytest -q tools/evidence/tests/test_cancel_policy.py`
Expected: PASS (mọi test).

- [ ] **Step 6: Không commit** — cập nhật checkbox; người dùng commit cuối đợt.

---

### Task 2: `policy_metrics.py` — `φ`, `AS_IS_BE`, AUC, `Δr`, percentile

**Files:**
- Create: `tools/evidence/policy_metrics.py`
- Test: `tools/evidence/tests/test_policy_metrics.py`

**Interfaces:**
- Produces:
  - Hằng biến thể hoàn tiền: `REFUND_OF_PAID`, `REFUND_OF_TOTAL_CAPPED`, `VARIANTS`;
  - Hằng lý do AUC không xác định: `ONE_CLASS`, `CONSTANT_SCORE`;
  - `retained_fraction(variant: str, r: float, pct: float) -> float`;
  - `over_refund(r: float, pct: float, price: float) -> float`;
  - `@dataclass(frozen=True) Auc(value: float | None, reason: str | None)`;
  - `auc(scores: Sequence[float], labels: Sequence[int]) -> Auc`;
  - `auc_from_counts(positives: Mapping[float, int], negatives: Mapping[float, int]) -> float`;
  - `delta_r(scores: Sequence[float], labels: Sequence[int]) -> float | None`;
  - `percentile(sorted_values: Sequence[float], q: float) -> float | None`;
  - `ci95(values: Iterable[float]) -> dict[str, float | int | None]`.

- [ ] **Step 1: Viết test thất bại**

`tools/evidence/tests/test_policy_metrics.py`:

```python
"""Công thức của mô phỏng PT-03 (spec 4.3): phần cọc giữ lại theo hai biến thể (đủ biên), hoàn vượt AS_IS_BE, AUC có ties và trường hợp không xác định, percentile."""
import pytest

from evidence import policy_metrics as pm


@pytest.mark.parametrize(
    ("variant", "r", "pct", "expected"),
    [
        (pm.REFUND_OF_PAID, 0.3, 1.0, 0.0),
        (pm.REFUND_OF_PAID, 0.3, 0.5, 0.5),
        (pm.REFUND_OF_PAID, 0.3, 0.0, 1.0),
        (pm.REFUND_OF_PAID, 0.0, 0.5, 0.5),         # φ xác định; nhân với r = 0 vẫn giữ 0
        (pm.REFUND_OF_TOTAL_CAPPED, 0.0, 0.5, 0.0),  # r = 0 → 0, không chia cho 0
        (pm.REFUND_OF_TOTAL_CAPPED, 0.3, 0.0, 1.0),  # pct = 0
        (pm.REFUND_OF_TOTAL_CAPPED, 0.3, 1.0, 0.0),  # pct = 1
        (pm.REFUND_OF_TOTAL_CAPPED, 0.5, 0.5, 0.0),  # r = pct
        (pm.REFUND_OF_TOTAL_CAPPED, 0.3, 0.5, 0.0),  # cọc thấp: hoàn hết
        (pm.REFUND_OF_TOTAL_CAPPED, 1.0, 0.5, 0.5),  # r = 1: bằng REFUND_OF_PAID
    ],
)
def test_retained_fraction_variants_and_edges(variant: str, r: float, pct: float, expected: float) -> None:
    assert pm.retained_fraction(variant, r, pct) == pytest.approx(expected)


@pytest.mark.parametrize(("r", "pct"), [(-0.1, 0.5), (1.1, 0.5), (0.5, -0.1), (0.5, 1.1)])
def test_retained_fraction_rejects_out_of_domain(r: float, pct: float) -> None:
    with pytest.raises(ValueError):
        pm.retained_fraction(pm.REFUND_OF_PAID, r, pct)


def test_retained_fraction_rejects_unknown_variant() -> None:
    with pytest.raises(ValueError):
        pm.retained_fraction("AS_IS_BE", 0.5, 0.5)   # AS_IS_BE không có φ


def test_over_refund_is_positive_only_when_refund_exceeds_deposit() -> None:
    assert pm.over_refund(0.3, 1.0, 200.0) == pytest.approx(140.0)
    assert pm.over_refund(0.5, 0.5, 200.0) == 0.0
    assert pm.over_refund(1.0, 0.5, 200.0) == 0.0


def test_auc_with_ties_counts_half() -> None:
    # positives {0.5, 0.3}, negatives {0.3, 0.3}: (0.5>0.3)*2 + (0.3=0.3)*0.5*2 = 3 trên 4 cặp
    assert pm.auc([0.5, 0.3, 0.3, 0.3], [1, 1, 0, 0]) == pm.Auc(0.75, None)


def test_auc_undefined_cases_return_null_with_reason() -> None:
    assert pm.auc([0.5, 0.3], [1, 1]) == pm.Auc(None, pm.ONE_CLASS)
    assert pm.auc([0.5, 0.5, 0.5], [1, 0, 1]) == pm.Auc(None, pm.CONSTANT_SCORE)


def test_auc_oracle_is_one_only_with_both_classes() -> None:
    assert pm.auc([1, 0, 1], [1, 0, 1]) == pm.Auc(1.0, None)
    assert pm.auc([1, 1], [1, 1]).reason == pm.ONE_CLASS


def test_delta_r() -> None:
    assert pm.delta_r([0.5, 0.3, 0.3, 0.1], [1, 1, 0, 0]) == pytest.approx(0.4 - 0.2)
    assert pm.delta_r([0.5], [1]) is None


def test_percentile_and_ci95() -> None:
    values = [float(value) for value in range(101)]
    assert pm.percentile(values, 0.025) == pytest.approx(2.5) and pm.percentile(values, 0.975) == pytest.approx(97.5)
    assert pm.percentile([], 0.5) is None
    assert pm.ci95(reversed(values)) == {"low": pytest.approx(2.5), "high": pytest.approx(97.5), "samples": 101}
```

- [ ] **Step 2: Chạy để xác nhận thất bại**

Run: `python -m pytest -q tools/evidence/tests/test_policy_metrics.py`
Expected: FAIL — `ImportError`

- [ ] **Step 3: Viết `policy_metrics.py`**

```python
"""Công thức thuần của mô phỏng kinh tế PT-03 (gói 5.7b, spec 4.3): phần cọc giữ lại `φ` theo hai biến thể hoàn tiền, hoàn vượt `AS_IS_BE`,
AUC của mức cọc được phân bổ đối với nhãn hủy (Mann–Whitney, ties tính 1/2), `Δr`, percentile cho khoảng tin cậy bootstrap.

`AS_IS_BE` (mã BE hiện tại hoàn `pct` trên `FinalTotal`, không chặn trên) KHÔNG có `φ`: chỉ đo phần hoàn vượt số cọc đã thu (B-1). AUC không xác định
(một lớp nhãn, hoặc điểm hằng) trả `None` kèm lý do, không bao giờ trả 0,5 mặc định.
"""
from __future__ import annotations

import math
from collections import Counter
from dataclasses import dataclass
from typing import Iterable, Mapping, Sequence

REFUND_OF_PAID = "REFUND_OF_PAID"
REFUND_OF_TOTAL_CAPPED = "REFUND_OF_TOTAL_CAPPED"
VARIANTS = (REFUND_OF_PAID, REFUND_OF_TOTAL_CAPPED)
ONE_CLASS = "ONE_CLASS"
CONSTANT_SCORE = "CONSTANT_SCORE"


def _check_unit(name: str, value: float) -> None:
    if not 0.0 <= value <= 1.0 or math.isnan(value):
        raise ValueError(f"{name} phải trong [0, 1], nhận {value}")


def retained_fraction(variant: str, r: float, pct: float) -> float:
    """`φ`: phần cọc đã thu bị giữ lại khi hủy với tỷ lệ hoàn `pct`. `REFUND_OF_PAID`: `1 − pct`.
    `REFUND_OF_TOTAL_CAPPED`: `0` khi `r = 0`, ngược lại `max(0, r − pct) / r` (hoàn `min(r·P, pct·P)`)."""
    _check_unit("r", r)
    _check_unit("pct", pct)
    if variant == REFUND_OF_PAID:
        return 1.0 - pct
    if variant == REFUND_OF_TOTAL_CAPPED:
        return 0.0 if r == 0 else max(0.0, r - pct) / r
    raise ValueError(f"biến thể hoàn tiền không hợp lệ: {variant!r} (chỉ {', '.join(VARIANTS)})")


def over_refund(r: float, pct: float, price: float) -> float:
    """`AS_IS_BE`: phần hoàn vượt số cọc đã thu `max(0, pct·P − r·P)`; > 0 nghĩa là BE sẽ hoàn nhiều hơn số đã thu nếu giữ nguyên mã."""
    return max(0.0, (pct - r) * price)


@dataclass(frozen=True)
class Auc:
    """AUC hoặc lý do không xác định."""

    value: float | None
    reason: str | None


def auc_from_counts(positives: Mapping[float, int], negatives: Mapping[float, int]) -> float:
    """P(điểm của đơn nhãn 1 > điểm đơn nhãn 0) + ½·P(bằng nhau), tính trên bảng đếm theo mức điểm (điểm rời rạc nên O(số mức))."""
    total_pos, total_neg = sum(positives.values()), sum(negatives.values())
    wins, below = 0.0, 0
    for score in sorted(set(positives) | set(negatives)):
        pos, neg = positives.get(score, 0), negatives.get(score, 0)
        wins += pos * below + 0.5 * pos * neg
        below += neg
    return wins / (total_pos * total_neg)


def auc(scores: Sequence[float], labels: Sequence[int]) -> Auc:
    positives = Counter(score for score, label in zip(scores, labels) if label == 1)
    negatives = Counter(score for score, label in zip(scores, labels) if label != 1)
    if not positives or not negatives:
        return Auc(None, ONE_CLASS)
    if len(set(positives) | set(negatives)) == 1:
        return Auc(None, CONSTANT_SCORE)
    return Auc(auc_from_counts(positives, negatives), None)


def delta_r(scores: Sequence[float], labels: Sequence[int]) -> float | None:
    """`mean(r | c=1) − mean(r | c=0)`; None khi thiếu một lớp."""
    positive = [score for score, label in zip(scores, labels) if label == 1]
    negative = [score for score, label in zip(scores, labels) if label != 1]
    if not positive or not negative:
        return None
    return math.fsum(positive) / len(positive) - math.fsum(negative) / len(negative)


def percentile(sorted_values: Sequence[float], q: float) -> float | None:
    """Percentile nội suy tuyến tính trên dãy ĐÃ SẮP; None khi rỗng."""
    if not sorted_values:
        return None
    position = q * (len(sorted_values) - 1)
    lower, upper = math.floor(position), math.ceil(position)
    return sorted_values[lower] + (sorted_values[upper] - sorted_values[lower]) * (position - lower)


def ci95(values: Iterable[float]) -> dict[str, float | int | None]:
    ordered = sorted(values)
    return {"low": percentile(ordered, 0.025), "high": percentile(ordered, 0.975), "samples": len(ordered)}
```

- [ ] **Step 4: Chạy lại test** — `python -m pytest -q tools/evidence/tests/test_policy_metrics.py` → PASS.
- [ ] **Step 5: Không commit.**

---

### Task 3: `hotel_adapter` đọc `reservation_status(_date)`, mở rộng meta và D5

**Files:**
- Modify: `tools/evidence/hotel_adapter.py`:
  - hằng `REQUIRED_COLUMNS`, `D5_COLUMNS` (dòng ~66–77);
  - `HotelRow` (~94–118);
  - `_parse_row` (~299–321);
  - `_build_request` (meta, ~286–292);
  - `_d5_row` (~221–230).
- Test: `tools/evidence/tests/test_hotel_adapter.py` (thêm test)

**Interfaces:**
- Produces:
  - `RESERVATION_STATUSES = ("Check-Out", "Canceled", "No-Show")`;
  - `HotelRow.reservation_status: str`, `HotelRow.reservation_status_date: date`;
  - meta có thêm `reservation_status` (chuỗi) và `reservation_status_date` (ISO `YYYY-MM-DD`);
  - `D5_COLUMNS` thêm ở **cuối**, theo thứ tự: `"reservation_status", "reservation_status_date", "shadow_pair_state", "primary_fallback", "shadow_status", "shadow_deposit_rate", "shadow_deposit_amount"`;
  - `_d5_row` ghi hai cột trạng thái từ meta (`meta.get(..., "")` để prepared cũ vẫn đọc được) và năm cột shadow **rỗng**. Task 4 điền các cột shadow.

- [ ] **Step 1: Viết test thất bại** (thêm vào `test_hotel_adapter.py`)

```python
def test_load_rows_reads_reservation_status_and_date() -> None:
    rows = hotel_adapter.load_rows(FIXTURES / "hotel_sample.csv")
    assert {row.reservation_status for row in rows} <= set(hotel_adapter.RESERVATION_STATUSES)
    assert rows[0].reservation_status == "Check-Out" and rows[0].reservation_status_date.isoformat() == "2017-01-01"


@pytest.mark.parametrize(("column", "value"), [("reservation_status", "Lost"), ("reservation_status_date", "2017-13-01")])
def test_load_rows_rejects_invalid_reservation_status_cells(tmp_path: Path, column: str, value: str) -> None:
    text = (FIXTURES / "hotel_sample.csv").read_text(encoding="utf-8").splitlines()
    header = text[0].split(",")
    cells = text[1].split(",")
    cells[header.index(column)] = value
    path = tmp_path / "bad.csv"
    path.write_text("\n".join([text[0], ",".join(cells)]) + "\n", encoding="utf-8")
    with pytest.raises(hotel_adapter.HotelDataError, match=column):
        hotel_adapter.load_rows(path)


def test_meta_and_d5_carry_reservation_status_and_empty_shadow_columns() -> None:
    rows = hotel_adapter.load_rows(FIXTURES / "hotel_sample.csv")[:1]
    prepared = hotel_adapter.build_requests(rows, Anonymizer(b"k" * 32), "run", NOW)
    meta = prepared[0]["meta"]
    assert meta["reservation_status"] == "Check-Out" and meta["reservation_status_date"] == "2017-01-01"
    d5 = hotel_adapter.d5_rows_from_outcomes([{"idempotency_key": prepared[0]["idempotency_key"], "meta": meta}], {}, {})
    assert d5[0]["reservation_status"] == "Check-Out"
    assert all(d5[0][column] == "" for column in ("shadow_pair_state", "primary_fallback", "shadow_status", "shadow_deposit_rate", "shadow_deposit_amount"))
```

Hai tên `NOW` và `Anonymizer(...)` theo đúng cách các test sẵn có trong file dùng. Trước khi viết, đọc phần đầu `test_hotel_adapter.py` để dùng đúng tên fixture và hằng (không tự đặt tên mới).

- [ ] **Step 2: Chạy để xác nhận thất bại**

Run: `python -m pytest -q tools/evidence/tests/test_hotel_adapter.py`
Expected: FAIL — `AttributeError: ... RESERVATION_STATUSES` / `reservation_status`

- [ ] **Step 3: Sửa `hotel_adapter.py`**

- Hằng (sau `TEXT_COLUMNS`):

```python
RESERVATION_STATUSES = ("Check-Out", "Canceled", "No-Show")
STATUS_COLUMNS = ("reservation_status", "reservation_status_date")
REQUIRED_COLUMNS = (*INT_COLUMNS, *TEXT_COLUMNS, *STATUS_COLUMNS, "arrival_date_year", "arrival_date_month", "arrival_date_day_of_month", "adr")
```

- `D5_COLUMNS`: thêm vào cuối tuple hiện có:

```python
    "reservation_status", "reservation_status_date", "shadow_pair_state", "primary_fallback", "shadow_status", "shadow_deposit_rate", "shadow_deposit_amount",
```

- `HotelRow`: thêm hai trường **cuối** dataclass: `reservation_status: str` và `reservation_status_date: date`. Thêm vào docstring: "nhãn kết thúc, chỉ dùng phân tích, không vào request".
- `_parse_row`: trước câu `if problems or adr is None ...`:

```python
    status = cell("reservation_status")
    if status not in RESERVATION_STATUSES:
        problems.append(f"cột reservation_status phải là {', '.join(RESERVATION_STATUSES)}, nhận '{status}'")
    try:
        status_date = date.fromisoformat(cell("reservation_status_date"))
    except ValueError:
        problems.append("cột reservation_status_date phải là ngày YYYY-MM-DD")
        status_date = None
```

  và truyền `reservation_status=status, reservation_status_date=status_date` vào `HotelRow(...)`. Điều kiện `return None` hiện có đã chặn khi `problems` khác rỗng.
- `_build_request`, trong `meta = {...}`: thêm `"reservation_status": row.reservation_status, "reservation_status_date": row.reservation_status_date.isoformat(),`.
- `_d5_row`: thêm vào dict trả về:

```python
        "reservation_status": meta.get("reservation_status", ""), "reservation_status_date": meta.get("reservation_status_date", ""),
        "shadow_pair_state": "", "primary_fallback": "", "shadow_status": "", "shadow_deposit_rate": "", "shadow_deposit_amount": "",
```

- Docstring đầu module: thay câu về cột nhãn bằng: "Cột nhãn (`is_canceled`, `reservation_status`, `reservation_status_date`) chỉ vào `meta` để phân tích PT-02/PT-03, KHÔNG vào body request".

- [ ] **Step 4: Chạy lại toàn bộ bộ evidence**

Run: `python -m pytest -q tools/evidence`
Expected: PASS. Các test so `list(row) == D5_COLUMNS` (`test_dataset_analysis.py:193`, `test_hotel_adapter.py:325`, `test_outcome_rows.py:111`) vẫn xanh vì `_d5_row` sinh đủ cột mới. Đỏ thì sửa `_d5_row`, **không** sửa test.

- [ ] **Step 5: Không commit.**

---

### Task 4: `shadow_pairing.py` — ghép theo `correlation_id`, gắn cột shadow vào D5

**Files:**
- Create: `tools/evidence/shadow_pairing.py`
- Modify: `tools/evidence/dataset_analysis.py`: docstring `d1_index` (~dòng 245–247); thêm `run_correlations` và `build_d5_with_shadow` sau `build_d5` (~226–229).
- Test: `tools/evidence/tests/test_shadow_pairing.py`

**Interfaces:**
- Consumes: hằng `D5_COLUMNS` và cột shadow của Task 3. Dòng D1 là `dict[str, str]` theo `d1_export.D1_COLUMNS`, có `role`, `correlation_id`, `reservation_hash`, `request_hash`, `created_at_utc`, `fallback_used` (`"True"`/`"False"`), `status`, `deposit_rate`, `deposit_amount`.
- Produces:
  - Hằng trạng thái ghép: `PAIRED`, `PRIMARY_FALLBACK`, `SHADOW_FALLBACK`, `UNPAIRED`, `STATES`;
  - Hằng loại vi phạm: `DUPLICATE_PRIMARY`, `MULTIPLE_SHADOW`, `ORPHAN_SHADOW`, `INVARIANT_MISMATCH`;
  - `@dataclass(frozen=True) Pairing(states: dict[str, str], primaries: dict[str, dict], shadows: dict[str, dict], outside_run: int, violations: dict[str, int], samples: list[str])`;
  - `pair(d1_rows: Iterable[dict[str, str]], run_correlations: set[str]) -> Pairing`;
  - `attach(d5_rows: list[dict], correlations: list[str | None], pairing: Pairing) -> None` — sửa tại chỗ;
  - `state_counts(pairing: Pairing) -> dict[str, int]`;
  - `dataset_analysis.run_correlations(run_dir: Path, part: str) -> dict[str, str]`;
  - `dataset_analysis.build_d5_with_shadow(run_dir: Path, d1_rows: list[dict[str, str]]) -> tuple[list[dict], shadow_pairing.Pairing] | None`.

- [ ] **Step 1: Viết test thất bại**

`tools/evidence/tests/test_shadow_pairing.py`:

```python
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
```

Thêm test cho `dataset_analysis.build_d5_with_shadow` vào `test_dataset_analysis.py`. Dựng `raw/portability/prepared.jsonl` và `requests.jsonl` trong `tmp_path` theo đúng mẫu của test `build_d5` sẵn có ở dòng ~185–195 (đọc test đó trước). Cho `requests.jsonl` có `correlation_id` `"a"`, truyền D1 gồm một cặp `"a"`, rồi assert:
- `rows[0]["shadow_pair_state"] == "PAIRED"`;
- `pairing.states == {"a": "PAIRED"}`.

- [ ] **Step 2: Chạy để xác nhận thất bại**

Run: `python -m pytest -q tools/evidence/tests/test_shadow_pairing.py`
Expected: FAIL — `ImportError`

- [ ] **Step 3: Viết `shadow_pairing.py`**

```python
"""Ghép bản PRIMARY với bản SHADOW của cùng request cho PT-04 (gói 5.7b, spec 4.1–4.2).

Bản SHADOW có `decision_id` riêng nhưng mang cùng `correlation_id`, `reservation_id`, `request_hash`, `created_at` với PRIMARY đã sinh ra nó (INV-8).
Khóa ghép là `correlation_id` (bước (e) sinh UUID mới cho mỗi request); ba trường còn lại là bất biến phải khớp. Phạm vi lượt = tập `correlation_id`
mà bước (e) đã ghi; bản ghi ngoài tập chỉ được đếm. Mỗi PRIMARY thuộc đúng một trạng thái; cả hai bản fallback xếp `PRIMARY_FALLBACK`.
Chỉ `PAIRED` dùng để so hai strategy; fallback (100 %) không phải quyết định của strategy.
"""
from __future__ import annotations

from collections import Counter, defaultdict
from dataclasses import dataclass
from typing import Any, Iterable

PAIRED = "PAIRED"
PRIMARY_FALLBACK = "PRIMARY_FALLBACK"
SHADOW_FALLBACK = "SHADOW_FALLBACK"
UNPAIRED = "UNPAIRED"
STATES = (PAIRED, PRIMARY_FALLBACK, SHADOW_FALLBACK, UNPAIRED)
DUPLICATE_PRIMARY = "DUPLICATE_PRIMARY"
MULTIPLE_SHADOW = "MULTIPLE_SHADOW"
ORPHAN_SHADOW = "ORPHAN_SHADOW"
INVARIANT_MISMATCH = "INVARIANT_MISMATCH"
INVARIANT_FIELDS = ("reservation_hash", "request_hash", "created_at_utc")
MAX_SAMPLES = 20


def _flag(value: Any) -> bool:
    return value is True or str(value).strip().lower() == "true"


@dataclass(frozen=True)
class Pairing:
    """`states`: correlation_id → trạng thái (chỉ PRIMARY trong lượt); `primaries`/`shadows`: dòng D1 theo correlation_id; `outside_run`: số dòng ngoài lượt;
    `violations`: đếm theo loại; `samples`: tối đa `MAX_SAMPLES` mẫu dạng `LOẠI:correlation_id`."""

    states: dict[str, str]
    primaries: dict[str, dict[str, Any]]
    shadows: dict[str, dict[str, Any]]
    outside_run: int
    violations: dict[str, int]
    samples: list[str]


def pair(d1_rows: Iterable[dict[str, Any]], run_correlations: set[str]) -> Pairing:
    primaries: dict[str, dict[str, Any]] = {}
    shadow_lists: dict[str, list[dict[str, Any]]] = defaultdict(list)
    violations: Counter = Counter()
    samples: list[str] = []
    outside = 0

    def violate(kind: str, corr: str) -> None:
        violations[kind] += 1
        if len(samples) < MAX_SAMPLES:
            samples.append(f"{kind}:{corr}")

    for row in d1_rows:
        corr = str(row.get("correlation_id") or "")
        if corr not in run_correlations:
            outside += 1
            continue
        if row.get("role") == "PRIMARY":
            if corr in primaries:
                violate(DUPLICATE_PRIMARY, corr)
            else:
                primaries[corr] = row
        elif row.get("role") == "SHADOW":
            shadow_lists[corr].append(row)

    shadows: dict[str, dict[str, Any]] = {}
    for corr, items in shadow_lists.items():
        if corr not in primaries:
            violate(ORPHAN_SHADOW, corr)
            continue
        if len(items) > 1:
            violate(MULTIPLE_SHADOW, corr)
        shadows[corr] = items[0]

    states: dict[str, str] = {}
    for corr, primary in primaries.items():
        shadow = shadows.get(corr)
        if shadow is None:
            states[corr] = UNPAIRED
            continue
        if any(str(primary.get(field, "")) != str(shadow.get(field, "")) for field in INVARIANT_FIELDS):
            violate(INVARIANT_MISMATCH, corr)
        if _flag(primary.get("fallback_used")):
            states[corr] = PRIMARY_FALLBACK
        elif _flag(shadow.get("fallback_used")):
            states[corr] = SHADOW_FALLBACK
        else:
            states[corr] = PAIRED
    return Pairing(states, primaries, shadows, outside, dict(violations), samples)


def state_counts(pairing: Pairing) -> dict[str, int]:
    counts = Counter(pairing.states.values())
    return {state: counts.get(state, 0) for state in STATES}


def attach(d5_rows: list[dict[str, Any]], correlations: list[str | None], pairing: Pairing) -> None:
    """Điền 5 cột shadow của D5 (cùng thứ tự với `correlations`). Request không có PRIMARY trong lượt giữ ô rỗng; thiếu SHADOW thì cột của SHADOW rỗng, không phải 0."""
    for row, corr in zip(d5_rows, correlations):
        state = pairing.states.get(corr or "")
        if state is None:
            continue
        row["shadow_pair_state"] = state
        row["primary_fallback"] = str(_flag(pairing.primaries[corr].get("fallback_used")))
        shadow = pairing.shadows.get(corr)
        if shadow is not None:
            row["shadow_status"] = shadow.get("status", "")
            row["shadow_deposit_rate"] = shadow.get("deposit_rate", "")
            row["shadow_deposit_amount"] = shadow.get("deposit_amount", "")
```

- [ ] **Step 4: Sửa `dataset_analysis.py`**

- Docstring `d1_index` thành: `"""Tra cứu D1 theo decision_id, chỉ bản PRIMARY. Bản SHADOW có decision_id riêng; ghép với PRIMARY qua shadow_pairing (correlation_id)."""`.
- Thêm `from evidence import shadow_pairing` vào khối import `evidence` sẵn có.
- Thêm sau `build_d5`:

```python
def run_correlations(run_dir: Path, part: str) -> dict[str, str]:
    """khóa idempotency → correlation_id từ `raw/{part}/requests.jsonl` của lượt (chỉ dòng có correlation_id)."""
    return {row["key"]: row["correlation_id"] for row in read_jsonl(run_dir / "raw" / part / "requests.jsonl") if row.get("correlation_id")}


def build_d5_with_shadow(run_dir: Path, d1_rows: list[dict[str, Any]]) -> tuple[list[dict[str, Any]], shadow_pairing.Pairing] | None:
    """D5 kèm 5 cột shadow (PT-04): ghép PRIMARY/SHADOW của D1 PT theo correlation_id, phạm vi lượt = correlation_id trong requests.jsonl. None khi PT không chuẩn bị dòng nào."""
    paired = _paired(run_dir, "portability")
    if paired is None:
        return None
    prepared, outcomes = paired
    rows = hotel_adapter.d5_rows_from_outcomes(prepared, outcomes, d1_index(d1_rows))
    keys = run_correlations(run_dir, "portability")
    pairing = shadow_pairing.pair(d1_rows, set(keys.values()))
    shadow_pairing.attach(rows, [keys.get(entry["idempotency_key"]) for entry in prepared], pairing)
    return rows, pairing
```

- [ ] **Step 5: Chạy lại** — `python -m pytest -q tools/evidence` → PASS.
- [ ] **Step 6: Không commit.**

---

### Task 5: `policy_economics.py` — tổng hợp, kịch bản, bootstrap ghép cặp, xếp hạng, bias

**Files:**
- Create: `tools/evidence/policy_economics.py`
- Test: `tools/evidence/tests/test_policy_economics.py`

**Interfaces:**
- Consumes:
  - `cancel_policy.CancelPolicy`, `refund_fraction`, `hours_until_start_bounds`, `scaled` (Task 1);
  - `policy_metrics.*` (Task 2);
  - D5 có `reservation_status*` (Task 3) và cột shadow (Task 4);
  - `hotel_adapter.BRANCH_TIME_ZONE`.
- Produces:
  - `BOOTSTRAP_SEED = 20261014`, `BOOTSTRAP_ITERATIONS = 1000`, `MIN_CLASS_FOR_CI = 30`;
  - Hằng chính sách: `NO_DEPOSIT`, `STATIC_100`, `TADSS_DEFAULT`, `TADSS_SHADOW_MOCKALT`, `ORACLE`, `POLICIES`;
  - Hằng kịch bản: `S_SC`, `S_SC_X2`, `S_SC_HALF`, `S_FULL`, `S_NOSHOW`, `SCENARIOS`;
  - `NA_MAIN = 1.0`, `NA_LOWER = 0.0`;
  - `PROVISIONAL_DEFAULT_VARIANT = policy_metrics.REFUND_OF_PAID` (chỉ cho test/đồ thị đơn; output luôn có cả hai biến thể);
  - `@dataclass(frozen=True) EconRow`;
  - `prepare(d5_rows) -> tuple[list[EconRow], dict[str, int]]`;
  - `summarize(d5_rows: list[dict], policy: CancelPolicy | None, *, reconciliation: dict | None, pairing_counts: dict[str, int], violations: dict[str, int], seed: int = BOOTSTRAP_SEED, iterations: int = BOOTSTRAP_ITERATIONS) -> dict[str, Any]`.
- Khóa cấp 1 của output `summarize` (Task 7–8 đọc):
  - thông tin chung: `schema`, `assumptions`, `cancel_policy`, `counts`, `crosstab`, `tier_distribution`, `event_value`;
  - chỉ số: `point`, `burden`, `discrimination`, `need_approval`, `ci`, `ranking`, `as_is_be`;
  - shadow: `shadow`, `pairing_bias`.

**Định nghĩa** (đúng spec 4.3):
- **Mỗi `EconRow` có:**
  - `price`;
  - `canceled` (`c_i`), `status` (`s_i`), `start` (14:00 `Europe/Lisbon` ngày đến, giả định), `cancel_day`;
  - `rates: dict[policy, float | None]` (`None` = `NEED_APPROVAL`);
  - `paired: bool`, `position` (thứ tự trong D5), `weekend` (ngày đến là thứ Bảy/Chủ Nhật).
- **Loại khỏi mô phỏng** (thứ tự xét, mỗi dòng một lý do):
  - `no_decision`: `status` rỗng;
  - `invalid_price`: thiếu hoặc ≤ 0;
  - `invalid_reservation_status`;
  - `unsupported_status`: `status` khác `APPROVED`/`NEED_APPROVAL`;
  - `invalid_rate`: `APPROVED` mà rate không parse được hoặc ngoài [0, 1].
- **Tập so sánh** là `paired == True`. Dòng hợp lệ nhưng không `PAIRED` vẫn vào `crosstab` và `pairing_bias`.
- **`MockAlternative`:** đọc từ `shadow_status`/`shadow_deposit_rate`; chỉ dùng khi `paired`.
- **`NEED_APPROVAL`** quy về `NA_MAIN` hoặc `NA_LOWER` lúc tính.
- **`φ` theo kịch bản cho một dòng:**
  - `Check-Out` → None;
  - `No-Show` → (1, 1);
  - `Canceled`:
    - `S-FULL` → (1, 1);
    - `S-NOSHOW` → (0, 0);
    - `S-SC*` → (φ tại `pct` của số giờ **nhiều nhất**, φ tại `pct` của số giờ **ít nhất**) = (cận dưới, cận trên).
- **Chỉ số kinh tế:**
  - `E = Σ e·P`;
  - tỷ lệ giữ lại `= Σ e·φ·r·P / E`;
  - chưa bảo đảm `= Σ e·(1 − φ·r)·P`;
  - nghĩa vụ ứng trước `= Σ (1−e)·r·P / Σ (1−e)·P`.
- **Bootstrap** chỉ cho `S-SC`, `NA_MAIN`, hai biến thể × hai cận:
  - Mỗi vòng dùng `rng.choices(range(n), k=n)`; tổng tính bằng `math.fsum(map(col.__getitem__, idx))`.
  - Hiệu tính **trong từng vòng**.
  - AUC mỗi vòng bằng `Counter(map(keys.__getitem__, idx))` với `key = mức_điểm·2 + nhãn`.
  - Vòng có `E = 0` hoặc mẫu số ứng trước bằng 0 thì bỏ và đếm.
  - CI của AUC là `INSUFFICIENT_DATA` khi lớp thiểu số < `MIN_CLASS_FOR_CI` hoặc AUC toàn mẫu là `null`.
- **Xếp hạng:** so `TADSS_DEFAULT` với `TADSS_SHADOW_MOCKALT` theo tỷ lệ giữ lại ở mọi (kịch bản, biến thể, cận), với `NA_MAIN`; dấu là `>`/`<`/`=`.
  - `stable = True` khi mọi dấu khác `=` đều giống nhau.
  - `statement`: "kết luận phụ thuộc giả định giữ cọc" khi không ổn định; "không phân biệt được trên dữ liệu này" khi CI của hiệu chứa 0.
- **Ước lượng thời gian:** khoảng 22 cột × n phép cộng mỗi vòng. Với n ≈ 118 nghìn là vài phút ở bản đầy đủ; bản quick (10.000) vài giây đến vài chục giây. Ghi `seconds` vào output.

- [ ] **Step 1: Viết test thất bại**

`tools/evidence/tests/test_policy_economics.py` — dữ liệu nhỏ, có kết quả tính tay:

```python
"""Mô phỏng PT-03 trên D5 mẫu (spec 4.3): sự kiện theo reservation_status (không theo is_canceled), kịch bản và hai cận, NEED_APPROVAL hai quy ước,
loại dữ liệu thiếu có đếm, bootstrap ghép cặp (hiệu tính trong từng vòng), AS_IS_BE tách riêng, xếp hạng và bias."""
from decimal import Decimal
from pathlib import Path

import pytest

from evidence import cancel_policy, policy_economics as pe, shadow_pairing as sp

REPO = Path(__file__).resolve().parents[3]
POLICY = cancel_policy.load(REPO / cancel_policy.SNAPSHOT_RELATIVE)


def row(*, price: str = "100", canceled: str = "0", status: str = "Check-Out", arrival: str = "2017-07-10T14:00:00", cancel_day: str = "2017-07-11",
        default: tuple[str, str] = ("APPROVED", "0.5"), mock: tuple[str, str] = ("APPROVED", "0.3"), pair_state: str = sp.PAIRED) -> dict[str, str]:
    return {"total_price": price, "is_canceled": canceled, "reservation_status": status, "arrival_local": arrival, "reservation_status_date": cancel_day,
            "status": default[0], "deposit_rate": default[1] if default[0] == "APPROVED" else "", "shadow_pair_state": pair_state,
            "shadow_status": mock[0], "shadow_deposit_rate": mock[1] if mock[0] == "APPROVED" else "", "primary_fallback": "False"}


def test_economic_event_follows_reservation_status_not_is_canceled() -> None:
    rows = [row(status="No-Show", canceled="0"), row(status="Check-Out", canceled="1")]
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    assert result["event_value"] == {"E": 100.0, "E_canceled": 0.0, "E_no_show": 100.0}
    assert result["crosstab"] == {"0": {"Check-Out": 0, "Canceled": 0, "No-Show": 1}, "1": {"Check-Out": 1, "Canceled": 0, "No-Show": 0}}
    assert result["counts"]["label_mismatch"] == 2
    # No-Show: φ = 1 ở mọi kịch bản → tỷ lệ giữ lại của Default = r = 0,5
    point = {(p["scenario"], p["variant"], p["bound"], p["na"], p["policy"]): p["retention"] for p in result["point"]}
    assert point[("S-NOSHOW", "REFUND_OF_PAID", "low", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(0.5)


def test_s_sc_bounds_from_cancel_day_resolution() -> None:
    # hủy ngày liền trước ngày đến: 14–38 giờ → mốc 50 % (cận ít giờ) hoặc 100 % (cận nhiều giờ)
    rows = [row(status="Canceled", canceled="1", cancel_day="2017-07-09"), row()]
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    point = {(p["scenario"], p["variant"], p["bound"], p["na"], p["policy"]): p["retention"] for p in result["point"]}
    # REFUND_OF_PAID, Default r = 0,5: low (hoàn 100 %) → 0; high (hoàn 50 %) → φ 0,5 × r 0,5 = 0,25
    assert point[("S-SC", "REFUND_OF_PAID", "low", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(0.0)
    assert point[("S-SC", "REFUND_OF_PAID", "high", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(0.25)
    # REFUND_OF_TOTAL_CAPPED, r = 0,5, hoàn 50 % → max(0, 0,5 − 0,5) = 0
    assert point[("S-SC", "REFUND_OF_TOTAL_CAPPED", "high", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(0.0)
    assert {item["bucket"] for item in result["tier_distribution"]} == {"UNRESOLVED_0.5_1"}


def test_need_approval_two_conventions_and_operational_table() -> None:
    rows = [row(status="Canceled", canceled="1", cancel_day="2017-07-10", default=("NEED_APPROVAL", "")), row()]
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    point = {(p["scenario"], p["variant"], p["bound"], p["na"], p["policy"]): p["retention"] for p in result["point"]}
    assert point[("S-FULL", "REFUND_OF_PAID", "low", 1.0, pe.TADSS_DEFAULT)] == pytest.approx(1.0)
    assert point[("S-FULL", "REFUND_OF_PAID", "low", 0.0, pe.TADSS_DEFAULT)] == pytest.approx(0.0)
    assert {item["policy"]: item["count"] for item in result["need_approval"]} == {pe.TADSS_DEFAULT: 1, pe.TADSS_SHADOW_MOCKALT: 0}


def test_invalid_rows_are_excluded_with_reason_never_zero() -> None:
    rows = [row(default=("", "")), row(price=""), row(status="Lost"), row(default=("REJECTED", "")), row(default=("APPROVED", "x")), row()]
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    assert result["counts"]["excluded"] == {"no_decision": 1, "invalid_price": 1, "invalid_reservation_status": 1, "unsupported_status": 1, "invalid_rate": 1}
    assert result["counts"]["compared"] == 1


def test_only_paired_rows_are_compared_and_bias_is_reported() -> None:
    rows = [row(status="Canceled", canceled="1", cancel_day="2017-07-10"), row(pair_state=sp.UNPAIRED), row(pair_state=sp.SHADOW_FALLBACK)]
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    assert result["counts"]["compared"] == 1
    bias = result["pairing_bias"]
    assert bias["reported"] is True and bias["flag"] is True and bias["non_paired"]["rows"] == 2


def test_as_is_be_counts_over_refund_separately() -> None:
    rows = [row(status="Canceled", canceled="1", cancel_day="2017-07-01", default=("APPROVED", "0.3")), row()]   # hủy sớm → hoàn 100 %
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    default_low = next(item for item in result["as_is_be"] if item["policy"] == pe.TADSS_DEFAULT and item["bound"] == "low")
    assert default_low["count"] == 1 and default_low["value"] == pytest.approx(70.0)
    assert all("φ" not in key and "retention" not in key for key in default_low)


def test_bootstrap_difference_is_computed_within_each_resample() -> None:
    # Default luôn giữ lại hơn MockAlternative đúng một lượng cố định trên mỗi đơn → CI của hiệu suy biến (low == high), dù CI riêng rộng
    rows = [row(status="No-Show", canceled="1", price=str(10 + index), default=("APPROVED", "0.5"), mock=("APPROVED", "0.3")) for index in range(40)]
    rows += [row(price=str(10 + index)) for index in range(40)]
    result = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, seed=7, iterations=200)
    diff = result["ci"]["REFUND_OF_PAID"]["low"]["diff_retention"]["MOCKALT_MINUS_DEFAULT"]
    assert diff["low"] == pytest.approx(-0.2) and diff["high"] == pytest.approx(-0.2)


def test_bootstrap_is_reproducible_by_seed() -> None:
    rows = [row(status="Canceled", canceled=str(index % 2), cancel_day="2017-07-10", price=str(50 + index)) for index in range(60)]
    first = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, seed=3, iterations=50)["ci"]
    second = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, seed=3, iterations=50)["ci"]
    assert first == second


def test_missing_policy_marks_s_sc_insufficient_and_keeps_other_scenarios() -> None:
    result = pe.summarize([row(status="No-Show", canceled="1"), row()], None, reconciliation=None, pairing_counts={}, violations={}, iterations=10)
    assert result["cancel_policy"] == {"status": "INSUFFICIENT_DATA"}
    assert {p["scenario"] for p in result["point"]} == {"S-FULL", "S-NOSHOW"}


def test_ranking_flags_dependence_on_assumption() -> None:
    # Canceled sớm (hoàn 100 % ở S-SC) với MockAlt cọc cao hơn, No-Show với Default cọc cao hơn → thứ hạng đổi giữa S-FULL và S-NOSHOW
    rows = [row(status="Canceled", canceled="1", cancel_day="2017-07-01", price="300", default=("APPROVED", "0.2"), mock=("APPROVED", "0.5")),
            row(status="No-Show", canceled="1", price="100", default=("APPROVED", "0.5"), mock=("APPROVED", "0.3")), row()]
    ranking = pe.summarize(rows, POLICY, reconciliation=None, pairing_counts={}, violations={}, iterations=10)["ranking"]
    assert ranking["stable"] is False and "phụ thuộc giả định" in ranking["statement"]
```

- [ ] **Step 2: Chạy để xác nhận thất bại**

Run: `python -m pytest -q tools/evidence/tests/test_policy_economics.py`
Expected: FAIL — `ImportError`

- [ ] **Step 3: Viết `policy_economics.py`**

```python
"""Mô phỏng kinh tế các chính sách cọc trên D5 (PT-03, gói 5.7b, spec 4.3). Kết quả là CHỈ SỐ MÔ PHỎNG THEO KỊCH BẢN, không phải kết quả tài chính.

- Nhãn hủy `is_canceled` chỉ dùng cho AUC/Δr; sự kiện giữ cọc `e = reservation_status ∈ {Canceled, No-Show}`; mẫu số `E` chung mọi kịch bản.
- Giờ đến 14:00 `Europe/Lisbon` là GIẢ ĐỊNH (dataset chỉ có ngày đến); ngày hủy chỉ có ngày → `S-SC*` báo hai cận (`low` = hủy lúc 00:00, nhiều giờ, hoàn nhiều;
  `high` = hủy lúc 23:59, ít giờ, hoàn ít).
- Hai biến thể hoàn tiền ngang hàng tới khi Q-6 được chốt; `AS_IS_BE` đo hoàn vượt riêng, không vào xếp hạng.
- So sánh chỉ trên dòng `PAIRED`; dữ liệu thiếu/lỗi bị loại có đếm, không thành 0. Bootstrap ghép cặp: mọi chính sách trên cùng tập chỉ số mỗi vòng; hiệu tính trong vòng.
"""
from __future__ import annotations

import math
import random
import time as clock
from collections import Counter
from dataclasses import dataclass
from datetime import date, datetime
from decimal import Decimal, InvalidOperation
from typing import Any, Callable
from zoneinfo import ZoneInfo

from evidence import cancel_policy, hotel_adapter, policy_metrics as pm, shadow_pairing as sp

SCHEMA = "policy_economics/1"
BOOTSTRAP_SEED = 20261014
BOOTSTRAP_ITERATIONS = 1000
MIN_CLASS_FOR_CI = 30
BIAS_FLAG_SHARE = 0.01
NO_DEPOSIT, STATIC_100, TADSS_DEFAULT, TADSS_SHADOW_MOCKALT, ORACLE = "NO_DEPOSIT", "STATIC_100", "TADSS_DEFAULT", "TADSS_SHADOW_MOCKALT", "ORACLE"
POLICIES = (NO_DEPOSIT, STATIC_100, TADSS_DEFAULT, TADSS_SHADOW_MOCKALT, ORACLE)
TADSS_POLICIES = (TADSS_DEFAULT, TADSS_SHADOW_MOCKALT)
S_SC, S_SC_X2, S_SC_HALF, S_FULL, S_NOSHOW = "S-SC", "S-SC×2", "S-SC÷2", "S-FULL", "S-NOSHOW"
SCENARIOS = (S_SC, S_SC_X2, S_SC_HALF, S_FULL, S_NOSHOW)
SC_SCENARIOS = (S_SC, S_SC_X2, S_SC_HALF)
BOUNDS = ("low", "high")
NA_MAIN, NA_LOWER = 1.0, 0.0
PROVISIONAL_DEFAULT_VARIANT = pm.REFUND_OF_PAID  # quy ước mô phỏng tạm thời (spec 4.3), KHÔNG phải chính sách tài chính đã xác nhận
CHECK_OUT, CANCELED, NO_SHOW = hotel_adapter.RESERVATION_STATUSES
EVENT_STATUSES = frozenset({CANCELED, NO_SHOW})
EXCLUSION_REASONS = ("no_decision", "invalid_price", "invalid_reservation_status", "unsupported_status", "invalid_rate")
AGGREGATE_NOTE = "Chỉ số mô phỏng theo kịch bản trên dữ liệu proxy khách sạn; không phải kết quả tài chính; giả định hành vi khách không đổi theo mức cọc; không kết luận nhân quả."
SC_LIMIT_NOTE = "Thời điểm hủy trong ngày và giờ đến không quan sát được; phân loại vào mốc hoàn tiền là bất định trong vùng đã nêu."
_ZONE = ZoneInfo(hotel_adapter.BRANCH_TIME_ZONE)


@dataclass(frozen=True)
class EconRow:
    price: float
    canceled: int
    status: str
    start: datetime
    cancel_day: date
    rates: dict[str, float | None]
    paired: bool
    position: int
    weekend: bool

    @property
    def event(self) -> int:
        return 1 if self.status in EVENT_STATUSES else 0


def _rate(status: str, text: str) -> tuple[float | None, str | None]:
    """(mức cọc hoặc None = NEED_APPROVAL, lý do loại)."""
    if status == "NEED_APPROVAL":
        return None, None
    if status != "APPROVED":
        return None, "unsupported_status"
    try:
        value = Decimal(str(text))
    except InvalidOperation:
        return None, "invalid_rate"
    if not value.is_finite() or not Decimal(0) <= value <= Decimal(1):
        return None, "invalid_rate"
    return float(value), None


def prepare(d5_rows: list[dict[str, Any]]) -> tuple[list[EconRow], dict[str, int]]:
    rows: list[EconRow] = []
    excluded = {reason: 0 for reason in EXCLUSION_REASONS}
    for position, raw in enumerate(d5_rows):
        status = str(raw.get("status") or "")
        if not status:
            excluded["no_decision"] += 1
            continue
        try:
            price = Decimal(str(raw.get("total_price")))
        except InvalidOperation:
            price = Decimal(0)
        if not price.is_finite() or price <= 0:
            excluded["invalid_price"] += 1
            continue
        reservation_status = str(raw.get("reservation_status") or "")
        try:
            cancel_day = date.fromisoformat(str(raw.get("reservation_status_date")))
            start = datetime.fromisoformat(str(raw.get("arrival_local"))).replace(tzinfo=_ZONE)
        except ValueError:
            cancel_day = None  # type: ignore[assignment]
        if reservation_status not in hotel_adapter.RESERVATION_STATUSES or cancel_day is None:
            excluded["invalid_reservation_status"] += 1
            continue
        default_rate, reason = _rate(status, str(raw.get("deposit_rate") or ""))
        if reason:
            excluded[reason] += 1
            continue
        paired = raw.get("shadow_pair_state") == sp.PAIRED
        mock_rate: float | None = None
        if paired:
            mock_rate, mock_reason = _rate(str(raw.get("shadow_status") or ""), str(raw.get("shadow_deposit_rate") or ""))
            if mock_reason:
                excluded[mock_reason] += 1
                continue
        event = 1 if reservation_status in EVENT_STATUSES else 0
        rates = {NO_DEPOSIT: 0.0, STATIC_100: 1.0, TADSS_DEFAULT: default_rate, TADSS_SHADOW_MOCKALT: mock_rate, ORACLE: float(event)}
        rows.append(EconRow(float(price), 1 if str(raw.get("is_canceled")) == "1" else 0, reservation_status, start, cancel_day, rates, paired, position, start.weekday() >= 5))
    return rows, excluded
```

Phần còn lại của module (viết tiếp trong cùng file, đúng các chữ ký dưới đây):

```python
def _effective(rate: float | None, na: float) -> float:
    return na if rate is None else rate


def _phi(scenario: str, variant: str, row: EconRow, r: float, policies: dict[str, cancel_policy.CancelPolicy]) -> tuple[float, float] | None:
    """(cận low, cận high) của φ; None cho Check-Out."""
    if row.status == CHECK_OUT:
        return None
    if row.status == NO_SHOW or scenario == S_FULL:
        return 1.0, 1.0
    if scenario == S_NOSHOW:
        return 0.0, 0.0
    least, most = cancel_policy.hours_until_start_bounds(row.start, row.cancel_day)
    pct_most = float(cancel_policy.refund_fraction(policies[scenario], most))
    pct_least = float(cancel_policy.refund_fraction(policies[scenario], least))
    return pm.retained_fraction(variant, r, pct_most), pm.retained_fraction(variant, r, pct_least)


def _scenario_policies(policy: cancel_policy.CancelPolicy | None) -> dict[str, cancel_policy.CancelPolicy]:
    if policy is None:
        return {}
    return {S_SC: policy, S_SC_X2: cancel_policy.scaled(policy, Decimal(2)), S_SC_HALF: cancel_policy.scaled(policy, Decimal("0.5"))}


def _retention_column(rows: list[EconRow], scenario: str, variant: str, bound: int, policy_name: str, na: float, policies: dict) -> list[float]:
    """Đóng góp từng dòng vào tử số tỷ lệ giữ lại: e·φ·r·P (0 cho Check-Out)."""
    column = []
    for row in rows:
        r = _effective(row.rates[policy_name], na)
        phi = _phi(scenario, variant, row, r, policies)
        column.append(0.0 if phi is None else phi[bound] * r * row.price)
    return column


def _burden_column(rows: list[EconRow], policy_name: str, na: float) -> list[float]:
    return [0.0 if row.event else _effective(row.rates[policy_name], na) * row.price for row in rows]
```

Rồi `summarize` gọi lần lượt các hàm private dưới đây. Mỗi hàm trả một khóa cấp 1 của output, và mỗi hàm có test riêng ở Step 1.

| Hàm | Trả về | Làm gì |
| :--- | :--- | :--- |
| `_crosstab(rows)` | `crosstab` | Đếm `is_canceled × reservation_status` trên **mọi** dòng hợp lệ. `label_mismatch` = số dòng (`No-Show` hoặc `Canceled` với `c = 0`) + (`Check-Out` với `c = 1`). |
| `_tier_distribution(compared, policies)` | `tier_distribution` | Bucket cho dòng `Canceled`, theo `pct` ở hai cận của `S-SC`: bằng nhau thì `REFUND_<pct>`; cả hai cận âm giờ thì `PAST_START`; khác nhau thì `UNRESOLVED_<pct_ít>_<pct_nhiều>` (vd `UNRESOLVED_0.5_1`). Số định dạng bằng `f"{float(pct):g}"`, nên ra `1`, `0.5`, `0`. `No-Show` là bucket `NO_SHOW`. Mỗi bucket có `count` và `value` (`Σ P`). |
| `_point(compared, policies)` | `point` | Danh sách dict `{scenario, variant, bound, na, policy, retention, uncovered}` cho mọi tổ hợp: kịch bản có trong `policies` (cộng `S-FULL`, `S-NOSHOW`) × `VARIANTS` × `BOUNDS` × `(NA_MAIN, NA_LOWER)` × `POLICIES`. `retention = fsum(col)/E`; `uncovered = E − fsum(col)`; `E = 0` thì `None`. |
| `_burden(compared)` | `burden` | `[{policy, na, value}]`. |
| `_discrimination(compared)` | `discrimination` | Với `NA_MAIN`, mỗi policy: `auc` (giá trị và lý do), `delta_r`, `rate_distribution` (`Counter` mức cọc → số đơn), `oracle_by_definition` cho `ORACLE`. |
| `_need_approval(compared)` | `need_approval` | `[{policy, count, rate}]` cho `TADSS_POLICIES`. |
| `_bootstrap(compared, policies, seed, iterations)` | `ci` | Cấu trúc `ci[variant][bound]`: `retention[policy]`, `burden[policy]`, `auc[policy]`, `diff_retention` (`DEFAULT_MINUS_STATIC_100`, `MOCKALT_MINUS_DEFAULT`), `diff_burden` (cùng hai khóa). Kèm `skipped` và `seed`, `iterations`. Không có `S-SC` thì `{"status": "INSUFFICIENT_DATA"}`. |
| `_ranking(point, ci)` | `ranking` | `{"by_case": [{scenario, variant, bound, sign}], "stable": bool, "indistinguishable": bool, "statement": str}`. |
| `_as_is_be(compared, policies)` | `as_is_be` | Với mỗi `TADSS_POLICIES` × `BOUNDS`, `NA_MAIN`, dòng `Canceled`: `over = pm.over_refund(r, pct_tại_cận, P)`. Báo `{policy, bound, count, value, by_pct: {pct: [count, value]}}`. **Không** có `φ`. |
| `_pairing_bias(rows)` | `pairing_bias` | So nhóm `paired` với phần còn lại: `rows`, `cancel_rate`, `mean_price`, `weekend_share`, `mean_position_share` (`position/len`). `reported = có phần không ghép`; `flag = phần không ghép / tổng > BIAS_FLAG_SHARE`; `threshold_note = "99 % là ngưỡng vận hành, không phải ngưỡng thống kê"`. |

Khung `summarize`:

```python
def summarize(d5_rows: list[dict[str, Any]], policy: cancel_policy.CancelPolicy | None, *, reconciliation: dict[str, Any] | None,
              pairing_counts: dict[str, int], violations: dict[str, int], seed: int = BOOTSTRAP_SEED, iterations: int = BOOTSTRAP_ITERATIONS) -> dict[str, Any]:
    started = clock.monotonic()
    rows, excluded = prepare(d5_rows)
    compared = [row for row in rows if row.paired]
    policies = _scenario_policies(policy)
    crosstab, mismatch = _crosstab(rows)
    events = [row for row in compared if row.event]
    result = {
        "schema": SCHEMA, "note": AGGREGATE_NOTE, "s_sc_limit": SC_LIMIT_NOTE,
        "assumptions": {"check_in_local": "14:00", "time_zone": hotel_adapter.BRANCH_TIME_ZONE, "cancel_time_bounds": ["00:00", "23:59"],
                        "need_approval": {"main": NA_MAIN, "lower": NA_LOWER}, "variants": list(pm.VARIANTS),
                        "provisional_default_variant": PROVISIONAL_DEFAULT_VARIANT, "scenarios": list(SCENARIOS)},
        "cancel_policy": {"status": "INSUFFICIENT_DATA"} if policy is None else
                         {"source": policy.source, "retrieved_on": policy.retrieved_on, "sha256": policy.sha256,
                          "tiers": [{"hours_before": t.hours_before, "refund_fraction": str(t.refund_fraction)} for t in policy.tiers]},
        "counts": {"d5_rows": len(d5_rows), "excluded": excluded, "valid": len(rows), "compared": len(compared), "label_mismatch": mismatch,
                   "events": {CANCELED: sum(row.status == CANCELED for row in compared), NO_SHOW: sum(row.status == NO_SHOW for row in compared)},
                   "check_out": sum(row.status == CHECK_OUT for row in compared)},
        "crosstab": crosstab,
        "event_value": {"E": math.fsum(r.price for r in events), "E_canceled": math.fsum(r.price for r in events if r.status == CANCELED),
                        "E_no_show": math.fsum(r.price for r in events if r.status == NO_SHOW)},
        "tier_distribution": _tier_distribution(compared, policies),
        "point": _point(compared, policies), "burden": _burden(compared), "discrimination": _discrimination(compared),
        "need_approval": _need_approval(compared), "as_is_be": _as_is_be(compared, policies) if policies else [],
        "pairing_bias": _pairing_bias(rows),
        "shadow": {"reconciliation": reconciliation, "pairing": pairing_counts, "violations": violations},
    }
    result["ci"] = _bootstrap(compared, policies, seed, iterations) if policies else {"status": "INSUFFICIENT_DATA"}
    result["ranking"] = _ranking(result["point"], result["ci"])
    result["seconds"] = round(clock.monotonic() - started, 2)
    return result
```

Lõi `_bootstrap` (bắt buộc đúng cách tính hiệu trong vòng):

```python
def _bootstrap(rows: list[EconRow], policies: dict, seed: int, iterations: int) -> dict[str, Any]:
    n = len(rows)
    if n == 0:
        return {"status": "INSUFFICIENT_DATA"}
    rng = random.Random(seed)
    event_col = [row.event * row.price for row in rows]
    burden_den = [(1 - row.event) * row.price for row in rows]
    burden_num = {name: _burden_column(rows, name, NA_MAIN) for name in POLICIES}
    labels = [row.canceled for row in rows]
    auc_keys: dict[str, list[int]] = {}
    for name in TADSS_POLICIES:
        scores = [_effective(row.rates[name], NA_MAIN) for row in rows]
        if pm.auc(scores, labels).value is not None and min(sum(labels), n - sum(labels)) >= MIN_CLASS_FOR_CI:
            levels = {score: index for index, score in enumerate(sorted(set(scores)))}
            auc_keys[name] = [levels[score] * 2 + label for score, label in zip(scores, labels)]
    cases = {(variant, bound): {name: _retention_column(rows, S_SC, variant, index, name, NA_MAIN, policies) for name in POLICIES}
             for variant in pm.VARIANTS for index, bound in enumerate(BOUNDS)}
    samples: dict[str, list[float]] = {}
    skipped = 0

    def add(key: str, value: float) -> None:
        samples.setdefault(key, []).append(value)

    population = range(n)
    for _ in range(iterations):
        idx = rng.choices(population, k=n)

        def total(column: list[float]) -> float:
            return math.fsum(map(column.__getitem__, idx))

        e_total, den_total = total(event_col), total(burden_den)
        if e_total == 0 or den_total == 0:
            skipped += 1
            continue
        burden = {name: total(column) / den_total for name, column in burden_num.items()}
        for name, value in burden.items():
            add(f"burden|{name}", value)
        add("diff_burden|DEFAULT_MINUS_STATIC_100", burden[TADSS_DEFAULT] - burden[STATIC_100])
        add("diff_burden|MOCKALT_MINUS_DEFAULT", burden[TADSS_SHADOW_MOCKALT] - burden[TADSS_DEFAULT])
        for (variant, bound), columns in cases.items():
            retention = {name: total(column) / e_total for name, column in columns.items()}
            for name, value in retention.items():
                add(f"{variant}|{bound}|retention|{name}", value)
            add(f"{variant}|{bound}|diff|DEFAULT_MINUS_STATIC_100", retention[TADSS_DEFAULT] - retention[STATIC_100])
            add(f"{variant}|{bound}|diff|MOCKALT_MINUS_DEFAULT", retention[TADSS_SHADOW_MOCKALT] - retention[TADSS_DEFAULT])
        for name, keys in auc_keys.items():
            counts = Counter(map(keys.__getitem__, idx))
            positives = {key // 2: value for key, value in counts.items() if key % 2 == 1}
            negatives = {key // 2: value for key, value in counts.items() if key % 2 == 0}
            if positives and negatives and len(set(positives) | set(negatives)) > 1:
                add(f"auc|{name}", pm.auc_from_counts(positives, negatives))
    ...  # gom `samples` thành cấu trúc `ci[variant][bound]` như bảng trên bằng pm.ci95; AUC không đủ điều kiện → "INSUFFICIENT_DATA"
```

Đoạn `...` cuối là phép dựng dict thuần từ khóa chuỗi đã định nghĩa; viết rõ trong mã, không để `...`.

- [ ] **Step 4: Chạy lại** — `python -m pytest -q tools/evidence/tests/test_policy_economics.py`, sau đó `python -m pytest -q tools/evidence` → PASS.
- [ ] **Step 5: Đo nhanh thời gian bootstrap** trên 10.000 dòng giả, để ghi vào báo cáo task (không phải test):

```bash
python -c "import sys,time; sys.path.insert(0,'tools'); from evidence import policy_economics as pe, cancel_policy as cp; from pathlib import Path
rows=[{'total_price':str(50+i%400),'is_canceled':str(i%3==0),'reservation_status':['Check-Out','Canceled','No-Show'][i%3],'arrival_local':'2017-07-10T14:00:00','reservation_status_date':'2017-07-0'+str(1+i%9),'status':'APPROVED','deposit_rate':'0.5','shadow_pair_state':'PAIRED','shadow_status':'APPROVED','shadow_deposit_rate':['0.3','0.5'][i%2],'primary_fallback':'False'} for i in range(10000)]
t=time.time(); pe.summarize(rows, cp.load(Path(cp.SNAPSHOT_RELATIVE)), reconciliation=None, pairing_counts={}, violations={}); print(round(time.time()-t,1),'s')"
```

Expected: in ra số giây. Nếu vượt khoảng 60 s ở 10.000 dòng thì dừng lại báo người dùng **trước khi** chạy quick (ngoại suy cho bản đầy đủ 118 nghìn dòng).

- [ ] **Step 6: Không commit.**

---

### Task 6: Bật shadow cho instance PT, xả hàng đợi và đối soát ở bước (e)

**Files:**
- Modify: `docker-compose.yml` (environment của `eval-tadss-portability`, sau dòng `Tadss__Strategies__Default__HighValueThreshold`, ~dòng 186)
- Create: `tools/evaluation/evaluator/shadow_drain.py`
- Modify: `tools/evaluation/evaluator/steps/dataset_step.py`: `Services` (~dòng 46–57) và `_run_portability` (~191–216); thêm helper `_merge_summary`.
- Test: `tools/evaluation/tests/test_shadow_drain.py`, thêm test vào `tools/evaluation/tests/test_dataset_step.py`

**Interfaces:**
- Produces:
  - Hằng nhãn đối soát: `RECONCILED`, `UNKNOWN`, `FAILED`, `AGGREGATE_STATEMENT`;
  - Tham số xả: `POLL_SECONDS = 2.0`, `STALL_SECONDS = 30.0`, `MAX_SECONDS = 600.0`, `PREFILTER_MARGIN = timedelta(minutes=5)`;
  - Tham số Prometheus: `PT_INSTANCE = "tadss-pt"`, `RECORDED_QUERY`, `FAILED_QUERY`, `DROPPED_QUERY`;
  - `@dataclass(frozen=True) Counters(recorded: float | None, failed: float | None, dropped: float | None)`;
  - `read_counters(query: Callable[[str], list[dict]]) -> Counters`;
  - `count_roles_sql() -> str`;
  - `drain(count_roles: Callable[[], dict[str, int]], read: Callable[[], Counters], before: Counters, *, monotonic, sleep) -> dict[str, Any]`;
  - `summary.json` của PT có khóa `shadow_reconciliation` (dict trả về từ `drain`, hoặc `{"label": "UNKNOWN", "reason": ...}` khi không có DSN hay không gửi gì).

- [ ] **Step 1: Sửa compose** (spec 4.1). Thêm vào `environment` của `eval-tadss-portability`:

```yaml
      Tadss__Shadow__Enabled: "true"   # PT-04 (gói 5.7b): chạy bóng MockAlternative cạnh Default; chỉ instance PT, không nhận tải của bước (b)
      Tadss__Shadow__Strategy: MockAlternativeDecisionStrategy
      Tadss__Shadow__SampleRate: "1"
      Tadss__Shadow__QueueCapacity: "10000"   # tối đa của validator; không phải giới hạn tổng request (worker tiêu thụ liên tục)
      Tadss__Strategies__MockAlternative__HighValueThreshold: "750"   # EUR, cùng lý do với Default (p90 giá trị đơn)
```

Run: `docker compose config -q`
Expected: không in gì, thoát 0 (chỉ kiểm cú pháp, **không** `up`).

- [ ] **Step 2: Viết test thất bại cho `shadow_drain`**

`tools/evaluation/tests/test_shadow_drain.py`:

```python
"""Xả hàng đợi shadow và đối soát tổng hợp PT-04 (spec 4.2): delta counter theo lượt, phát hiện reset, nhãn RECONCILED/UNKNOWN/FAILED, hết hạn có residual."""
from evaluator import shadow_drain as sd


class FakeClock:
    def __init__(self) -> None:
        self.now = 0.0

    def monotonic(self) -> float:
        return self.now

    def sleep(self, seconds: float) -> None:
        self.now += seconds


def run(sequence: list[tuple[dict[str, int], sd.Counters]], before: sd.Counters) -> dict:
    clock, state = FakeClock(), {"i": 0}

    def step() -> tuple[dict[str, int], sd.Counters]:
        item = sequence[min(state["i"], len(sequence) - 1)]
        return item

    def roles() -> dict[str, int]:
        return step()[0]

    def read() -> sd.Counters:
        value = step()[1]
        state["i"] += 1
        return value

    return sd.drain(roles, read, before, monotonic=clock.monotonic, sleep=clock.sleep)


def test_reconciled_when_equation_and_recorded_match() -> None:
    before = sd.Counters(5, 1, 0)
    result = run([({"PRIMARY": 10, "SHADOW": 7}, sd.Counters(12, 1, 0)), ({"PRIMARY": 10, "SHADOW": 9}, sd.Counters(14, 2, 0))], before)
    assert result["label"] == sd.RECONCILED and result["delta"] == {"recorded": 9, "failed": 1, "dropped": 0} and result["residual"] == 0
    assert result["statement"] == sd.AGGREGATE_STATEMENT


def test_absent_failed_and_dropped_counters_count_as_zero_when_recorded_present() -> None:
    result = run([({"PRIMARY": 3, "SHADOW": 3}, sd.Counters(3, None, None))], sd.Counters(None, None, None))
    assert result["label"] == sd.RECONCILED


def test_counter_reset_is_unknown() -> None:
    result = run([({"PRIMARY": 3, "SHADOW": 3}, sd.Counters(2, 0, 0))], sd.Counters(10, 0, 0))
    assert result["label"] == sd.UNKNOWN and "reset" in result["reason"]


def test_stall_times_out_with_residual_unknown() -> None:
    result = run([({"PRIMARY": 10, "SHADOW": 6}, sd.Counters(6, 0, 1))], sd.Counters(0, 0, 0))
    assert result["label"] == sd.UNKNOWN and result["residual"] == 3 and result["seconds"] >= sd.STALL_SECONDS


def test_recorded_differs_from_db_after_stall_is_failed() -> None:
    result = run([({"PRIMARY": 4, "SHADOW": 4}, sd.Counters(3, 0, 0))], sd.Counters(0, 0, 0))
    assert result["label"] == sd.FAILED and result["reason"] == "counter_mismatch"


def test_prometheus_unavailable_is_unknown() -> None:
    result = run([({"PRIMARY": 4, "SHADOW": 4}, sd.Counters(None, None, None))], sd.Counters(None, None, None))
    assert result["label"] == sd.UNKNOWN


def test_read_counters_parses_sum_results_and_missing_series() -> None:
    answers = {sd.RECORDED_QUERY: [{"value": [0, "12"]}], sd.FAILED_QUERY: [], sd.DROPPED_QUERY: [{"value": [0, "1"]}]}
    assert sd.read_counters(lambda expr: answers[expr]) == sd.Counters(12.0, None, 1.0)
```

- [ ] **Step 3: Chạy để xác nhận thất bại**

Run: `python -m pytest -q tools/evaluation/tests/test_shadow_drain.py`
Expected: FAIL — `ImportError`

- [ ] **Step 4: Viết `shadow_drain.py`**

```python
"""Xả hàng đợi shadow của instance PT và đối soát tổng hợp cho PT-04 (gói 5.7b, spec 4.2).

Counter `tadss.shadow.*` tích lũy theo vòng đời process, nên dùng HIỆU giữa ảnh chụp trước khi gửi và lúc xả xong; counter cuối < đầu = process khởi động lại → UNKNOWN.
Series vắng: trước khi gửi coi là 0; sau khi xả, `failed`/`dropped` vắng là 0 khi `recorded` có mặt (pipeline metric của instance đang chạy), `recorded` vắng → UNKNOWN.
Phạm vi lượt theo tập correlation_id của lượt (truy vấn của bước gọi). Phương trình khớp chỉ là đối soát TỔNG HỢP, không phải bằng chứng từng request đã được xử lý;
bằng chứng theo request là trạng thái ghép cặp ở bước (f).
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import timedelta
from typing import Any, Callable

RECONCILED, UNKNOWN, FAILED = "RECONCILED", "UNKNOWN", "FAILED"
AGGREGATE_STATEMENT = "Đối soát tổng hợp (DB + counter), không phải bằng chứng tuyệt đối về từng tác vụ; bằng chứng theo request là trạng thái ghép cặp theo correlation_id."
POLL_SECONDS = 2.0
STALL_SECONDS = 30.0
MAX_SECONDS = 600.0
PREFILTER_MARGIN = timedelta(minutes=5)
PT_INSTANCE = "tadss-pt"
RECORDED_QUERY = f'sum(tadss_shadow_executions_total{{outcome="recorded",instance="{PT_INSTANCE}"}})'
FAILED_QUERY = f'sum(tadss_shadow_executions_total{{outcome="failed",instance="{PT_INSTANCE}"}})'
DROPPED_QUERY = f'sum(tadss_shadow_dropped_total{{instance="{PT_INSTANCE}"}})'


def count_roles_sql() -> str:
    """Đếm PRIMARY/SHADOW của lượt: lọc thô theo thời gian (đã lùi PREFILTER_MARGIN ở nơi gọi) rồi theo tập correlation_id của lượt."""
    return "SELECT role, count(*) FROM tadss.decision_executions WHERE created_at >= %s AND correlation_id = ANY(%s) GROUP BY role"


@dataclass(frozen=True)
class Counters:
    recorded: float | None
    failed: float | None
    dropped: float | None

    def to_dict(self) -> dict[str, float | None]:
        return {"recorded": self.recorded, "failed": self.failed, "dropped": self.dropped}


def _value(result: list[dict]) -> float | None:
    return float(result[0]["value"][1]) if result else None


def read_counters(query: Callable[[str], list[dict]]) -> Counters:
    return Counters(_value(query(RECORDED_QUERY)), _value(query(FAILED_QUERY)), _value(query(DROPPED_QUERY)))


def _delta(before: Counters, after: Counters) -> tuple[dict[str, int] | None, str | None]:
    if after.recorded is None:
        return None, "prometheus_unavailable"
    start = {name: value or 0.0 for name, value in before.to_dict().items()}
    end = {"recorded": after.recorded, "failed": after.failed or 0.0, "dropped": after.dropped or 0.0}
    if any(end[name] < start[name] for name in end):
        return None, "counter_reset"
    return {name: int(round(end[name] - start[name])) for name in end}, None


def drain(count_roles: Callable[[], dict[str, int]], read: Callable[[], Counters], before: Counters, *,
          monotonic: Callable[[], float], sleep: Callable[[float], None]) -> dict[str, Any]:
    started = monotonic()
    last_shadow, last_progress = -1, started
    while True:
        roles, after = count_roles(), read()
        primary, shadow = roles.get("PRIMARY", 0), roles.get("SHADOW", 0)
        delta, problem = _delta(before, after)
        done = delta is not None and delta["recorded"] == shadow and shadow + delta["failed"] + delta["dropped"] == primary
        now = monotonic()
        if shadow != last_shadow:
            last_shadow, last_progress = shadow, now
        if done or now - last_progress >= STALL_SECONDS or now - started >= MAX_SECONDS:
            break
        sleep(POLL_SECONDS)
    result: dict[str, Any] = {"primary": primary, "shadow": shadow, "delta": delta, "before": before.to_dict(), "after": after.to_dict(),
                              "seconds": round(monotonic() - started, 1), "statement": AGGREGATE_STATEMENT}
    if delta is None:
        return {**result, "label": UNKNOWN, "reason": problem, "residual": None}
    residual = primary - shadow - delta["failed"] - delta["dropped"]
    if delta["recorded"] != shadow:
        return {**result, "label": FAILED, "reason": "counter_mismatch", "residual": residual}
    if done:
        return {**result, "label": RECONCILED, "reason": None, "residual": 0}
    return {**result, "label": UNKNOWN, "reason": "timeout", "residual": residual}
```

Kiểm lại test `test_reset`: `_delta` cho `"counter_reset"`, và test chỉ cần `"reset" in reason`. Kiểm lại `test_stall`: `shadow` không đổi nên sau 30 s dừng với `residual = 10 − 6 − 0 − 1 = 3`.

- [ ] **Step 5: Tích hợp vào `dataset_step.py`**

- Import: `from evaluator import shadow_drain`; `from evaluator.clients import postgres, prometheus`.
- `Services` thêm:

```python
    pt_fetch_factory: Callable[[str], Callable[[str, tuple], list[tuple]]] = postgres.connect_fetch
    prom_query: Callable[[str, str], list[dict]] = lambda base, expr: prometheus.query(base, expr)
```

- Thêm helper:

```python
def _shadow_probe(context: RunContext, services: Services) -> tuple[Callable[[], shadow_drain.Counters], Callable[[str, tuple], list[tuple]]] | None:
    """(đọc counter, truy vấn DB PT) hoặc None khi stack không có DSN của database PT (chạy ngoài stack đánh giá, hoặc test)."""
    dsn = context.env.get("EVAL_PT_PG_DSN")
    if not dsn:
        return None
    base = context.endpoints.prometheus

    def read() -> shadow_drain.Counters:
        try:
            return shadow_drain.read_counters(lambda expr: services.prom_query(base, expr))
        except prometheus.PrometheusError:
            return shadow_drain.Counters(None, None, None)

    return read, services.pt_fetch_factory(dsn)


def _merge_summary(out_dir: Path, key: str, value: Any) -> None:
    path = out_dir / "summary.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    data[key] = value
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2, default=_json_default), encoding="utf-8")
```

- Trong `_run_portability`: ngay trước dòng `now = services.clock()` ở cuối hàm, thay `return _send_part(...)` bằng:

```python
    probe = _shadow_probe(context, services)
    before = probe[0]() if probe else None
    send_started = services.clock()
    now = services.clock()
    requests = _lazy(selection.rows, lambda chunk: hotel_adapter.build_requests(chunk, anonymizer, context.run_id, now))
    outcome = _send_part(context, services, PART_PORTABILITY, out_dir, requests, target, now, extra, phases)
    _merge_summary(out_dir, "shadow_reconciliation", _reconcile(out_dir, services, probe, before, send_started, phases))
    return outcome
```

  và thêm:

```python
def _reconcile(out_dir: Path, services: Services, probe, before, send_started: datetime, phases: Phases) -> dict[str, Any]:
    """PT-04: xả hàng đợi shadow và đối soát tổng hợp; tập correlation_id của lượt lấy từ requests.jsonl vừa ghi."""
    if probe is None:
        return {"label": shadow_drain.UNKNOWN, "reason": "EVAL_PT_PG_DSN chưa đặt: không đối soát được shadow"}
    correlations = [row["correlation_id"] for row in _read_rows(out_dir / "requests.jsonl") if row.get("correlation_id")]
    if not correlations:
        return {"label": shadow_drain.UNKNOWN, "reason": "không có request PT nào được gửi"}
    read, fetch = probe
    since = send_started - shadow_drain.PREFILTER_MARGIN

    def roles() -> dict[str, int]:
        return {str(role): int(count) for role, count in fetch(shadow_drain.count_roles_sql(), (since, correlations))}

    return phases.timed("shadow_drain", lambda: shadow_drain.drain(roles, read, before, monotonic=services.monotonic, sleep=services.sleep))
```

  `_read_rows` đọc JSONL từng dòng. Nếu module chưa có hàm tương đương thì viết 4 dòng: mở file, bỏ dòng rỗng, `json.loads`.
- Cập nhật docstring module: thêm một gạch đầu dòng về `shadow_reconciliation` trong `summary.json` của PT, và pha `shadow_drain` trong `Phases`.

- [ ] **Step 6: Test tích hợp bước (e)** (thêm vào `test_dataset_step.py`, dùng đúng `Harness`/fixture sẵn có của file):
  - Không có `EVAL_PT_PG_DSN`: `summary.json` của PT có `shadow_reconciliation.label == "UNKNOWN"` và lý do chứa `EVAL_PT_PG_DSN`. Các test cũ không đổi.
  - Có DSN: tiêm `pt_fetch_factory` trả `lambda sql, params: [("PRIMARY", n), ("SHADOW", n)]`, với `n` = số request, và `prom_query` theo hai lần gọi (trước 0, sau n). Assert:
    - `label == "RECONCILED"`;
    - tham số thứ hai của truy vấn là danh sách đúng các `correlation_id` trong `requests.jsonl`;
    - `phase_seconds` có khóa `shadow_drain`.

  Run: `python -m pytest -q tools/evaluation`
  Expected: PASS.

- [ ] **Step 7: Không commit.**

---

### Task 7: Bước (f) — D5 có shadow, `policy_economics.json`, ảnh chụp vào `config/`, độ phủ PT-03/PT-04

**Files:**
- Modify: `tools/evaluation/evaluator/steps/evidence_step.py`: `write_datasets` (~dòng 137–159), `write_config` (~162–177).
- Modify: `tools/evidence/dataset_analysis.py`: `dataset_coverage` (~262–275).
- Test: `tools/evaluation/tests/test_evidence_step_datasets.py`, `tools/evidence/tests/test_dataset_analysis.py`.

**Interfaces:**
- Consumes:
  - `build_d5_with_shadow` (Task 4);
  - `policy_economics.summarize` (Task 5);
  - `cancel_policy.load`, `SNAPSHOT_RELATIVE` (Task 1);
  - `summary.json["shadow_reconciliation"]` (Task 6).
- Produces:
  - `raw/d5/policy_economics.json`;
  - `config/smashcourt_cancel_policies.json` (bản sao);
  - `dataset_coverage` trả thêm `PT-03` và `PT-04`.

**Quy tắc độ phủ:**

| Mã | Đạt khi | Đỏ khi | Còn lại |
| :--- | :--- | :--- | :--- |
| `PT-03` | `raw/d5/policy_economics.json` tồn tại | — | — |
| `PT-04` | Nhãn đối soát `RECONCILED` **và** `shadow.violations` rỗng | Nhãn `FAILED`, **hoặc** có vi phạm toàn vẹn | Không vào đâu (`UNKNOWN` → "chưa kiểm được", lý do theo `NOT_IN_SCOPE` của `^(RP\|PT)-`) |

- [ ] **Step 1: Viết test thất bại**

  **`test_dataset_analysis.py`:**
  - Dựng thư mục với `raw/d5/policy_economics.json` có `{"shadow": {"reconciliation": {"label": "RECONCILED"}, "violations": {}}}` ⇒ `"PT-03"` và `"PT-04"` thuộc tập đạt.
  - Đổi `label` thành `"FAILED"` ⇒ `"PT-04"` thuộc tập đỏ.
  - Giữ `RECONCILED` nhưng `violations = {"ORPHAN_SHADOW": 1}` ⇒ đỏ.
  - `UNKNOWN` ⇒ không thuộc tập nào.

  **`test_evidence_step_datasets.py`** (theo mẫu test D5 sẵn có trong file):
  - Có `raw/d1/decision_audit_portability.csv` chứa một cặp PRIMARY/SHADOW cùng `correlation_id` với `requests.jsonl`.
  - `context.repo_dir` trỏ tới repo thật (chứa ảnh chụp).
  - Sau `write_datasets`:
    - `raw/d5/policy_economics.json` tồn tại;
    - `cancel_policy.sha256` dài 64;
    - `shadow.pairing.PAIRED == 1`;
    - D5 CSV có cột `shadow_pair_state` bằng `PAIRED`.
  - Nếu `repo_dir` không có ảnh chụp: file vẫn được ghi với `cancel_policy.status == "INSUFFICIENT_DATA"`, và ghi chú nêu lý do.

- [ ] **Step 2: Chạy để xác nhận thất bại** — chạy riêng từng bộ `tools/evidence` và `tools/evaluation` → FAIL ở các test mới.

- [ ] **Step 3: Sửa `write_datasets`** — thay khối D5:

```python
    pt_rows = evidence_pack.read_csv(pt_d1) if pt_d1.exists() else []
    built = dataset_analysis.build_d5_with_shadow(run_dir, pt_rows)
    if built is not None:
        d5, pairing = built
        count = dataset_analysis.write_csv(run_dir / "raw" / "d5" / "portability_hotel.csv", hotel_adapter.D5_COLUMNS, d5)
        _write_json(run_dir / "raw" / "analysis" / "pt_summary.json", dataset_analysis.pt_summary(d5))
        notes.append(f"D5: {count} dòng; phân tích PT-02 đã ghi")
        policy, policy_note = _load_cancel_policy(context)
        reconciliation = (generator.read_json(run_dir / "raw" / "portability" / "summary.json") or {}).get("shadow_reconciliation")
        economics = policy_economics.summarize(d5, policy, reconciliation=reconciliation, pairing_counts=shadow_pairing.state_counts(pairing),
                                               violations=pairing.violations)
        economics["shadow"]["outside_run"] = pairing.outside_run
        economics["shadow"]["samples"] = pairing.samples
        _write_json(run_dir / "raw" / "d5" / "policy_economics.json", economics)
        notes.append(f"PT-03/PT-04: {economics['counts']['compared']} đơn so sánh, đối soát shadow {(reconciliation or {}).get('label', 'UNKNOWN')}"
                     + (f"; {policy_note}" if policy_note else ""))
```

  `pt_index` cũ giờ chỉ còn dùng ở đây nên bỏ (hoặc tính trong `build_d5_with_shadow` như Task 4 đã làm). Thêm:

```python
def _load_cancel_policy(context: RunContext) -> tuple[cancel_policy.CancelPolicy | None, str | None]:
    path = context.repo_dir / cancel_policy.SNAPSHOT_RELATIVE
    try:
        return cancel_policy.load(path), None
    except cancel_policy.CancelPolicyError as error:
        return None, f"S-SC INSUFFICIENT_DATA: {error}"
```

  Import `cancel_policy`, `policy_economics`, `shadow_pairing` từ `evidence`.

- [ ] **Step 4: Sửa `write_config`** — sau vòng `for source, target ...`:

```python
    snapshot = repo / cancel_policy.SNAPSHOT_RELATIVE
    if snapshot.exists():
        shutil.copy2(snapshot, config / snapshot.name)
```

- [ ] **Step 5: Sửa `dataset_coverage`** — sau vòng `for` hiện có:

```python
    economics = _read_json(run_dir / "raw" / "d5" / "policy_economics.json")
    if economics is not None:
        passed.add("PT-03")
        shadow = economics.get("shadow") or {}
        label = (shadow.get("reconciliation") or {}).get("label")
        if label == "FAILED" or shadow.get("violations"):
            failed.add("PT-04")
        elif label == "RECONCILED":
            passed.add("PT-04")
```

  Docstring đổi thành "(mã đạt, mã đỏ) của RP-01, RP-02, PT-01…PT-04", thêm hai câu quy tắc PT-03/PT-04 như bảng trên.

- [ ] **Step 6: Chạy lại** — chạy riêng `python -m pytest -q tools/evidence` và `python -m pytest -q tools/evaluation` → PASS.
- [ ] **Step 7: Không commit.**

---

### Task 8: Báo cáo — mục PT-03/PT-04 và biểu đồ đánh đổi

**Files:**
- Modify: `tools/evidence/report/charts.py` (thêm `scatter_chart`)
- Modify: `tools/evidence/report/generator.py` (thêm `economics_section`, gọi trong `dataset_sections`)
- Modify: `tools/evidence/report/template.html.j2` (sau khối PT-02, ~dòng 196)
- Test: `tools/evidence/tests/test_report_datasets.py`

**Interfaces:**
- Consumes: khóa của `policy_economics.json` (Task 5).
- Produces:
  - `charts.scatter_chart(points: list[tuple[str, float, float]], x_label: str, y_label: str) -> str` — SVG, trục 0…1;
  - `generator.economics_section(run_dir: Path) -> dict[str, Any]` với các khóa `available`, `reason`, `note`, `s_sc_limit`, `counts`, `crosstab`, `tiers`, `table`, `sensitivity`, `ranking`, `as_is_be`, `need_approval`, `reconciliation`, `pairing`, `bias`, `chart`;
  - `dataset_sections` trả thêm khóa `"economics"`.

**Yêu cầu hiển thị:**
- Đọc skill `dataviz` trước khi viết `scatter_chart`.
- Dùng `PALETTE` sẵn có. Mỗi điểm có nhãn chữ cạnh điểm, không dựa vào màu.
- Trục x là "Nghĩa vụ ứng trước của khách hoàn tất (proxy)", trục y là "Tỷ lệ cọc giữ lại mô phỏng (S-SC)".
- Mỗi chính sách hai điểm (hai biến thể, cận `low`), nhãn dạng `TADSS_DEFAULT · PAID`.

- [ ] **Step 1: Viết test thất bại** (`test_report_datasets.py`)

  Ghi một `policy_economics.json` tối thiểu: dùng output của `pe.summarize` trên 3 dòng mẫu ở Task 5, ghi bằng `json.dumps`. Sau đó `render(build_data(...))`, hoặc gọi `economics_section` trực tiếp như test PT sẵn có làm, và assert HTML chứa:
  - `"PT-03"` và `"PT-04"`;
  - `"REFUND_OF_PAID"` và `"REFUND_OF_TOTAL_CAPPED"` trong cùng một bảng;
  - câu `AGGREGATE_NOTE` và câu `SC_LIMIT_NOTE`;
  - câu `shadow_drain.AGGREGATE_STATEMENT` khi `reconciliation` có;
  - `"<svg"`;
  - **không** chứa `"tiền mất"`.

  Thiếu file thì mục hiện "Không có dữ liệu: …".

  Thêm test `scatter_chart` trả chuỗi bắt đầu bằng `<svg` và chứa từng nhãn.

- [ ] **Step 2: Chạy để xác nhận thất bại** — `python -m pytest -q tools/evidence/tests/test_report_datasets.py` → FAIL.

- [ ] **Step 3: Viết `scatter_chart`** (cùng kích thước và lề với `line_chart`, trục cố định 0…1):

```python
def scatter_chart(points: list[tuple[str, float, float]], x_label: str, y_label: str) -> str:
    """Biểu đồ phân tán trên [0,1]×[0,1]: mỗi điểm một chấm và nhãn chữ cạnh chấm (không dựa vào màu để phân biệt)."""
    inner_w, inner_h = WIDTH - MARGIN_LEFT - MARGIN_RIGHT, HEIGHT - MARGIN_TOP - MARGIN_BOTTOM

    def sx(value: float) -> float:
        return MARGIN_LEFT + value * inner_w

    def sy(value: float) -> float:
        return MARGIN_TOP + (1 - value) * inner_h

    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{WIDTH}" height="{HEIGHT}" role="img" aria-label="{escape(y_label)} theo {escape(x_label)}">']
    for tick in (0, 0.25, 0.5, 0.75, 1):
        parts.append(f'<line x1="{sx(0)}" y1="{sy(tick):.1f}" x2="{sx(1)}" y2="{sy(tick):.1f}" stroke="#d0d7de" stroke-width="1"/>')
        parts.append(f'<text x="{MARGIN_LEFT - 6}" y="{sy(tick) + 4:.1f}" font-size="11" text-anchor="end">{tick:g}</text>')
        parts.append(f'<text x="{sx(tick):.1f}" y="{HEIGHT - MARGIN_BOTTOM + 14}" font-size="11" text-anchor="middle">{tick:g}</text>')
    for index, (label, x, y) in enumerate(points):
        color = PALETTE[index % len(PALETTE)]
        parts.append(f'<circle cx="{sx(x):.1f}" cy="{sy(y):.1f}" r="4" fill="{color}"/>')
        parts.append(f'<text x="{sx(x) + 6:.1f}" y="{sy(y) - 6:.1f}" font-size="11">{escape(label)}</text>')
    parts.append(f'<text x="{MARGIN_LEFT + inner_w / 2:.1f}" y="{HEIGHT - 4}" font-size="12" text-anchor="middle">{escape(x_label)}</text>')
    parts.append(f'<text x="12" y="{MARGIN_TOP + inner_h / 2:.1f}" font-size="12" text-anchor="middle" transform="rotate(-90 12 {MARGIN_TOP + inner_h / 2:.1f})">{escape(y_label)}</text>')
    parts.append("</svg>")
    return "".join(parts)
```

- [ ] **Step 4: Viết `economics_section`** trong `generator.py`

  Đọc `raw/d5/policy_economics.json`; thiếu thì trả `{"available": False, "reason": ...}` (theo cách `pt_section` dựng `analysis_reason`). Dựng:
  - `table`: hàng = 4 chính sách so sánh cộng ORACLE (ghi "mốc lý tưởng"); cột = tỷ lệ giữ lại `S-SC` theo **hai biến thể**, mỗi biến thể ghi `low…high` và CI 95%; kèm nghĩa vụ ứng trước, AUC (hoặc lý do), `Δr`.
  - `sensitivity`: hàng = kịch bản, cột = `TADSS_DEFAULT`, `TADSS_SHADOW_MOCKALT`, dấu so sánh.
  - Các khóa `tiers`, `as_is_be`, `need_approval`, `reconciliation`, `pairing`, `bias` lấy thẳng từ JSON.
  - `chart = scatter_chart(...)` như quy định ở phần Interfaces.

  Nhãn chính sách theo spec 4.3:
  - `STATIC_100` → "Mốc tham chiếu cọc 100% (bằng mức fallback)";
  - `ORACLE` → "Mốc lý tưởng (dùng kết quả đã biết)".

- [ ] **Step 5: Template** — thêm sau khối PT-02:
  - `<h3>PT-03 — So sánh kinh tế mô phỏng các chính sách cọc</h3>`, với các phần theo đúng thứ tự spec 4.5:
    1. ghi chú `note` và `s_sc_limit`;
    2. bảng chéo `is_canceled × reservation_status`;
    3. phân bố theo mốc;
    4. bảng chính (hai biến thể);
    5. độ nhạy và xếp hạng (`ranking.statement`);
    6. `NEED_APPROVAL`;
    7. `AS_IS_BE` (tiêu đề "Rủi ro triển khai nếu giữ nguyên mã hoàn tiền hiện tại (B-1) — không dùng để xếp hạng");
    8. biểu đồ.
  - `<h3>PT-04 — Shadow mode: đối soát và ghép cặp</h3>`: nhãn, câu `statement`, delta, `residual`, bốn trạng thái ghép, vi phạm, bias kèm `threshold_note`.
  - Cập nhật tiêu đề mục 9 thành "Dữ liệu thực tế (RP-01, RP-02, PT-01…PT-04)".

- [ ] **Step 6: Chạy lại** — `python -m pytest -q tools/evidence` → PASS.

  Dựng lại báo cáo từ một thư mục test để mắt kiểm: `python -m evidence.report <thư mục tmp có policy_economics.json>`, chạy trong `tools/`. Mở `report.html` và kiểm bảng hai biến thể, nhãn và biểu đồ.

- [ ] **Step 7: Không commit.**

---

### Task 9: Tài liệu — methodology, DATASET_SCHEMA, guide, M-10

**Files:**
- Modify: `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`:
  - bảng §4.10, ~dòng 268–271;
  - "Cách chạy" của PT;
  - Mục 8 (mối đe dọa hiệu lực);
  - dòng bước (e) và (f) ở Mục 6.1.
- Modify: `docs/research/DATASET_SCHEMA_SPECIFICATION.md`: bảng §2 (~36–37), D5 (~98–104).
- Modify: `docs/guides/EVALUATION_RUN_GUIDE.md`: §4 đọc kết quả.
- Modify: `spec/PHASE5_PLAN_5.7_M5_FREEZE.md`: M-10 (spec/ không vào git, nhưng Task 10 của 5.7 đọc nó).

- [ ] **Step 1: Methodology** — thêm hai dòng vào bảng PT (cột đầu là mã, để `coverage_map.catalog_codes` và `CatalogIndex.KnownIds()` nhận):

```markdown
| PT-03 | Mô phỏng kinh tế các chính sách cọc (`NO_DEPOSIT`, mốc tham chiếu cọc 100 %, Default, MockAlternative chạy bóng, mốc lý tưởng) trên các cặp `PAIRED` của PT, theo chính sách hủy thật của SmashCourt (`S-SC`, hai biến thể hoàn tiền ngang hàng, hai cận do độ phân giải ngày) và các kịch bản độ nhạy | Báo cáo mô tả: tỷ lệ cọc giữ lại mô phỏng, nghĩa vụ ứng trước (proxy), AUC của mức cọc được phân bổ, CI 95 % bootstrap ghép cặp; không kết luận nhân quả, không phải kết quả tài chính |
| PT-04 | Shadow mode trong đánh giá: `MockAlternativeDecisionStrategy` chạy bóng cạnh Default trên instance PT; đối soát tổng hợp (DB + delta counter) và ghép cặp theo `correlation_id` | `RECONCILED` và 0 vi phạm toàn vẹn (`UNKNOWN` = chưa kiểm được; `FAILED` = đỏ); tỷ lệ `PAIRED` ≥ 99 % là ngưỡng vận hành để chạy bản đầy đủ |
```

  Thêm vào "Cách chạy" của PT một gạch đầu dòng **PT-03/PT-04 (gói 5.7b)**, tóm tắt và **liên kết** tới spec 5.7b mục 4.2–4.3, không chép công thức. Mục 8 thêm ba mối đe dọa:
  - dữ liệu proxy và giả định giờ đến 14:00;
  - độ phân giải ngày quanh mốc 2 và 24 giờ;
  - gốc tính hoàn tiền chưa chốt (Q-6).

- [ ] **Step 2: DATASET_SCHEMA**
  - D5: bổ sung 7 cột mới theo thứ tự của `D5_COLUMNS`, kèm nghĩa: `shadow_pair_state` có 4 giá trị; ô shadow rỗng khi thiếu, không phải 0.
  - Bảng §2: thêm `raw/d5/policy_economics.json` (PT-03/PT-04) và `config/smashcourt_cancel_policies.json`.
  - Câu cuối đoạn giấy phép (dòng ~104): thay "nếu không thì chỉ đưa `pt_summary.json`" bằng quy tắc Q-2 mới: "D5, `pt_summary.json`, `policy_economics.json` và mục PT của báo cáo chỉ vào Evidence Pack chính thức sau khi xác minh giấy phép và điều kiện phân phối của đúng nguồn đang ghim; quyết định từng artifact".

- [ ] **Step 3: Guide §4** — thêm cách đọc mục PT-03/PT-04 (nhãn đối soát, `PAIRED`, hai biến thể, hai cận) và lệnh xem nhanh:

```bash
python -c "import json; d=json.load(open('evidence/<run_id>/raw/d5/policy_economics.json',encoding='utf-8')); print(d['shadow']['reconciliation']['label'], d['shadow']['pairing'], d['counts']['compared'])"
```

- [ ] **Step 4: M-10 của plan 5.7** — thêm câu:

  > Thu hẹp bởi gói 5.7b (Q-2, 14/10/2026): `pt_summary.json`, `policy_economics.json`, D5 và mục PT của `report.html` chỉ vào pack chính thức sau khi xác minh giấy phép của đúng nguồn đang ghim; quyết định từng artifact.

- [ ] **Step 5: Kiểm tài liệu**

  Run: `python tools/docs/check_docs.py` → thoát 0. Rồi `python -m pytest -q tools/docs` → PASS.

  Thêm mã PT-03/PT-04 vào methodology có thể làm test C# `CatalogTraceabilityTests` liệt kê mã chưa có test. Đó là hợp lệ (mã do evaluator kiểm), không cần sửa C#. Chạy `dotnet test tests/TADSS.UnitTests -c Release --filter "FullyQualifiedName~Catalog" -nologo` để xác nhận vẫn xanh; đỏ thì đọc thông báo và báo người dùng, **không** nới test.

- [ ] **Step 6: Không commit.**

---

### Task 10: Kiểm chứng toàn bộ, bàn giao lượt quick, cổng quyết định full

**Files:** không sửa mã. Kết quả ghi vào báo cáo `spec/PHASE5_REPORT_5.7b.md` (mới, không stage).

- [ ] **Step 1: Chạy toàn bộ kiểm tra cục bộ**, mỗi bộ riêng:

```bash
python -m pytest -q tools/evidence
python -m pytest -q tools/evaluation
python -m pytest -q tools/docs
python tools/docs/check_docs.py
docker compose config -q
git -C . status --short src/ docs/contracts/
```

Expected:
- ba bộ pytest PASS;
- `check_docs` thoát 0;
- `compose config` không in gì;
- lệnh `git status` cuối **không in dòng nào** (không đụng `src/` hay contract).

- [ ] **Step 2: Bàn giao cho người dùng**

  Đưa lệnh chạy lượt quick, chạy toàn bộ để kiểm tiêu chí 6.3 (SC1…SC8 không đổi):

```powershell
docker compose up -d --build
docker compose logs -f evaluator      # chờ tới dòng tóm tắt cuối
docker compose logs --tail 60 evaluator
docker compose ps -a
```

  Người dùng gửi lại hai output cuối.

- [ ] **Step 3: Phân tích lượt quick** theo spec 6:
  1. `raw/portability/summary.json` → `shadow_reconciliation`: nhãn, `delta`, `residual`, `seconds`. Nếu `UNKNOWN` vì `prometheus_unavailable` thì mở Grafana của stack, xem tên series và nhãn thật của `tadss_shadow_*`, sửa **chỉ** ba hằng `*_QUERY` trong `shadow_drain.py` (kèm test), rồi chạy lại `bash tools/evaluation/run.sh quick --steps e,f`.
  2. `raw/d5/policy_economics.json`:
     - `shadow.pairing`, tỷ lệ `PAIRED`;
     - `violations`, `outside_run`;
     - `pairing_bias`;
     - `crosstab` và `label_mismatch`;
     - `tier_distribution`;
     - `seconds`.
  3. So SC1…SC8 với `EV-20261011-EVAL-quick-r1` (`manifest.json` → KPI).
  4. Thời gian bước (e) theo `phase_seconds`; ngoại suy cho full.
- [ ] **Step 4: Cổng quyết định**

  Ghi vào `spec/PHASE5_REPORT_5.7b.md`. **Dừng và bàn với người dùng** nếu bất kỳ điều nào sau xảy ra:
  - `PAIRED` < 99 %;
  - nhãn khác `RECONCILED`;
  - có vi phạm;
  - SC nào đổi kết luận;
  - thời gian ngoại suy vượt ngân sách lượt full đã thông báo (~2,8 giờ + phần tăng).

  Ngược lại, đề xuất người dùng chạy U-4 full theo lệnh của guide. Quick chỉ kiểm chứng cơ chế, không cam kết kết quả cho Evidence Pack.
- [ ] **Step 5: Gợi ý commit gộp** (người dùng tự chạy):
  - Stage: `git add -A; git reset spec/; git status`.
  - Message:

```text
feat(evaluation): simulate deposit policy economics and shadow mode on portability data

- PT-03: scenario-based economics on Hotel Booking (SmashCourt cancel policy, two refund variants, day-resolution bounds, paired bootstrap)
- PT-04: shadow MockAlternative on the PT instance, per-run counter deltas, correlation_id pairing and integrity checks
- D5 gains reservation status and shadow columns; report section and trade-off chart
- docs: methodology PT-03/PT-04, dataset schema, run guide; licence gate for aggregated PT artifacts

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

---

## Phụ lục — Đối chiếu spec → task

| Spec | Task |
| :--- | :---: |
| 4.1 cấu hình shadow, khóa `correlation_id`, docstring `d1_index` | 4, 6 |
| 4.2 phạm vi lượt theo `C_run`, delta counter, reset, ba nhãn, bốn trạng thái, toàn vẹn, xả, bias luôn báo | 4, 5, 6, 7 |
| 4.3 hai biến số `c`/`e`, chỉ số, `S-SC` theo BE, hai cận, hai biến thể (từng khúc), `AS_IS_BE` riêng, kịch bản độ nhạy, chính sách so sánh, `NEED_APPROVAL`, loại dữ liệu, bootstrap ghép cặp, xếp hạng | 1, 2, 5 |
| 4.4 đo thời gian tách riêng (`phase_seconds.shadow_drain`, `execution_time_ms` trong D1, `seconds` của mô phỏng) | 5, 6, 10 |
| 4.5 báo cáo, `policy_economics.json`, giấy phép Q-2 | 7, 8, 9 |
| 4.6 không sửa BE; thuật toán chọn mốc giống BE; không sao chép B-2 | 1 |
| 5 tech, kiểm thử | 1–8 |
| 6 tiêu chí hoàn thành, cổng full | 10 |
| 8 Q-2 (M-10 thu hẹp) | 9 |
