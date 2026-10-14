# Gói 5.7 — Đóng băng M5 (rà soát tài liệu DOC-1…4, đóng băng contract v2, API BE ↔ FE, E-STAGING, lượt đầy đủ): Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Khép Phase 5 ở Gate M5 — "TADSS Complete & Test-Ready":
- Dọn các tồn đọng nhìn thấy ở các lượt đánh giá (log ERR giả cho 404, dòng tóm tắt SC4, số lượt k6 bị bỏ, số SCALE-2 lẻ).
- **DOC-1…DOC-4:** tài liệu hiện hành khớp mã và được **kiểm tự động** (bảng kiểm kê, chặn mô tả thứ đã gỡ, link nội bộ, bảng cấu hình `Tadss:*`).
- **Đóng băng contract v2** (`2.0.0-draft` → `2.0.0`), wire không đổi.
- **T5.22:** đặc tả API SmashCourt BE ↔ FE cho cọc / chờ duyệt / duyệt (markdown trong `SmashCourt-BE`, chưa có mã).
- **T5.23:** chốt môi trường tích hợp E-STAGING trong MASTER_EXECUTION_PLAN.
- Người dùng chạy **bản đầy đủ** trên đúng bản đã đóng băng; agent chốt Evidence Pack vào `docs/research/evidence-packs/` và viết báo cáo Gate M5.

**Architecture:** Phần lớn ngoài `src/`:
- **`src/`** chỉ đổi ở hai chỗ, không đổi hành vi nghiệp vụ:
  - `appsettings.json`: một khóa override Serilog;
  - đóng băng: `<Version>` của SDK và `Info.Version` của OpenAPI.
- **Công cụ mới `tools/docs/`** (Python thuần, thư viện chuẩn, theo mẫu `tools/evidence`): một lệnh kiểm DOC-1…DOC-3, có pytest riêng. Chạy ở CI **và** trong bước (a) của evaluator (D-14: mọi kiểm tra nằm trong một lệnh `up`).
- **DOC-4** là unit test .NET (reflection trên các `Options` có hằng `SectionName`, so với bảng trong tài liệu).
- **Evaluator / k6:** chỉ đổi cách ghi chú và cấu hình VU, không đổi tiêu chí đạt/không đạt.
- **Repo `SmashCourt-BE`:** chỉ thêm một file tài liệu.

**Tech Stack:** .NET 8 (xUnit, FluentAssertions); Python 3.11 thư viện chuẩn (`re`, `pathlib`, `argparse`), pytest; k6 (`options.js`, `modes.json`); GitHub Actions. Không thêm package nào.

**Spec:** [`PHASE5_DESIGN_SPEC.md`](PHASE5_DESIGN_SPEC.md):
- **Mục 4.7** (thiết kế chính, gồm R-01…R-04 ngày 11/10/2026; R-03 đã được người dùng nới: đóng băng ngay trong gói).
- Mục 3 (dòng 5.7), 6.1 (DOC-2, DOC-3), 9 (`docs/README.md`, methodology DOC-1…4).
- D-12, D-14.

**Nguồn sở hữu:**
- [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md): Mục 2 (E-STAGING), 4 (danh mục — DOC-1…4 thêm mới), 6.1, 8.
- [`INTEGRATION_SPEC.md`](../docs/contracts/INTEGRATION_SPEC.md): Mục 2.2 (endpoint BE đề xuất), 4 (trách nhiệm BE), **6 (quản lý phiên bản, quy tắc sau đóng băng)**.
- [`DATASET_SCHEMA_SPECIFICATION.md`](../docs/research/DATASET_SCHEMA_SPECIFICATION.md): Mục 2 (cây `docs/research/evidence-packs/{run_id}/`, file > 20 MB lên GitHub Release).
- [`MASTER_EXECUTION_PLAN.md`](../docs/planning/MASTER_EXECUTION_PLAN.md): Gate M5.

**Gói trước:** [`PHASE5_PLAN_5.6_DATA_EVIDENCE.md`](PHASE5_PLAN_5.6_DATA_EVIDENCE.md) mục "Ghi chú cho gói 5.7"; [`PHASE5_REPORT_5.5.md`](PHASE5_REPORT_5.5.md) Mục 8; lượt `EV-20261011-EVAL-quick-r1` (bản nhanh đạt mọi tiêu chí áp dụng được, ~28 phút).

---

## Global Constraints

**Build và test sau mỗi task:**
- Task có mã .NET: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror` → **0 warning, 0 error**; unit test xanh; task đụng compose/IT thì IT xanh (Docker bật).
- Task có mã Python: pytest của `tools/evaluation`, `tools/evidence` (và `tools/docs` từ Task 3) **chạy riêng từng bộ** đều xanh.
- File `.sh` giữ LF (`.gitattributes`); sửa file bằng công cụ Edit, không ghi bằng `Path.write_text` trên Windows.

**Số lượng test:**
- Đầu gói (commit `2d11288`, cộng các file tài liệu đồng bộ quyết định ngày 11/10/2026 — xem "Đồng bộ trước khi thực thi" dưới đây): **1871 unit / 240 IT**, pytest **231** (`tools/evaluation`) + **226** (`tools/evidence`).
- **Đồng bộ trước khi thực thi (11/10/2026, chưa commit, nằm chung commit của gói):** INTEGRATION_SPEC §2.2 (route theo `bookingId`, hàng chờ từ DB của BE, `deposit.rate`), BACKLOG T5.22/T5.23/T7.2, MASTER_EXECUTION_PLAN (W08, Phase 7), methodology dòng E-STAGING, DECISION_PERSISTENCE_INVARIANTS (migration v2 đã áp Neon).
- Không mất test; mỗi task ghi số trước/sau.

**Contract và wire:**
- **Chỉ Task 8** được sửa `docs/contracts/**`, `<Version>` của SDK và `Info.Version` OpenAPI.
- Sau Task 8: `git diff -- docs/contracts/schemas/` **rỗng**; phần thân `baseline_public_api.txt` (sau 3 dòng header) **không đổi**; `openapi-v2.yaml` chỉ đổi dòng `version`.
- Không đổi route, DTO, mã lỗi, status HTTP, schema DB, migration.

**Cấm nới kiểm tra:** không `Skip`, không nới assertion, không `#pragma`/`# noqa`/`# type: ignore`, không thêm ngoại lệ vào allowlist để test xanh. Bộ kiểm tài liệu không có cơ chế "bỏ qua dòng này"; ngoại lệ hợp lệ duy nhất là file **lịch sử** trong bảng kiểm kê (DOC-1) và quy tắc "câu thì quá khứ" của DOC-2 (M-03).

**Repo khác:** chỉ Task 6 thêm **một file tài liệu** vào `SmashCourt-BE` (không mã, không đổi `ErrorCodes.cs`/`SignalREvents.cs`). Không đụng `smashcourt-fe`, `SmashCourt-AI`.

**Database và bí mật:** không migration, không `dotnet ef database update`, không kết nối Neon. Không đọc/in giá trị bí mật (`.env`, `appsettings.Development.json`).

**Git:** không commit/push/branch. Gợi ý **một** commit message cho TADSS-Runtime và một cho SmashCourt-BE ở Task 9. Không stage `spec/PHASE5_*.md`.

---

## Quyết định thiết kế của gói

| # | Quyết định | Lý do / phương án đã loại |
| :--- | :--- | :--- |
| **M-01** | **Log ERR giả cho 404:** thêm override Serilog `"Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware": "Fatal"` vào `appsettings.json`. `GlobalExceptionHandler` đã tự log mọi lỗi đúng mức (5xx: Error kèm exception; 4xx: Warning; client hủy: Information) nên không mất thông tin. | Middleware `ExceptionHandlerMiddleware` của ASP.NET Core 8 luôn log `Error` "unhandled exception" **trước khi** gọi `IExceptionHandler`, kể cả với 404 `DECISION_NOT_FOUND`. Override theo category cụ thể thắng override `Microsoft.AspNetCore` của file Development. **Đã loại:** `AddFilter` trong mã (hardcode giá trị vận hành); bắt `DecisionNotFoundException` trong controller (lệch đường lỗi một nơi của 5.2). |
| **M-02** | **`tools/docs/check_docs.py`** — một lệnh kiểm DOC-1, DOC-2, DOC-3; in một dòng `DOC-x: đạt/KHÔNG ĐẠT (...)` mỗi mã, thoát 0/1. Chạy ở **CI** (job mới) và ở **bước (a)** của evaluator; pytest `tools/docs` thêm vào `PYTEST_SUITES`. | Spec 4.7 yêu cầu "script grep trong CI" (DOC-2) và "job CI" (DOC-3). D-14 yêu cầu mọi kiểm tra nằm trong lượt `up`. Python thay cho bash vì cần phân tích bảng markdown và tính anchor; cùng quy ước với `tools/evidence`. |
| **M-03** | **Quy tắc DOC-2:** danh sách cụm từ của thứ đã gỡ (hằng `REMOVED_TERMS` trong `check_docs.py`). Một lần xuất hiện **hợp lệ** khi: (1) nằm trong file được kiểm kê là **lịch sử**; hoặc (2) dòng chứa dấu hiệu nói về quá khứ/cấm (`gỡ`, `đã bỏ`, `thay cho`, `thay bằng`, `không còn`, `không dùng`, `cấm`, `phase4-baseline`, `lịch sử`; không phân biệt hoa thường). Ngoài ra là vi phạm. | Grep thuần không phân biệt "đã gỡ MediatR" với "dùng MediatR". Quy tắc ghi rõ trong methodology DOC-2 để hội đồng kiểm lại được. **Đã loại:** marker bỏ qua từng dòng (trái Global Constraints). |
| **M-04** | **Phạm vi DOC-3:** link markdown `[..](đích)` trong mọi `.md` của repo (trừ `spec/`, `.superpowers/`, `evidence/`, `data/`, `bin/`, `obj/`, `node_modules/`, `TestResults/`); bỏ qua link trong khối code. Đích tương đối phải tồn tại; `#anchor` phải khớp tiêu đề theo quy tắc slug của GitHub. Đích `http(s)://`/`mailto:` không kiểm (cần mạng). Đích **ra ngoài repo** (ví dụ `../SmashCourt-BE/...`) không kiểm được trên CI ⇒ đếm và in riêng, không tính lỗi. | `spec/` không vào git (CI không có) nên không kiểm. Link ra repo khác chỉ kiểm được ở workspace gốc. |
| **M-05** | **DOC-1:** bảng kiểm kê ở `docs/README.md`, mỗi dòng gồm ba cột: `đường dẫn` (trong dấu backtick), nhóm `hiện hành`/`lịch sử`, chủ đề sở hữu hoặc tài liệu thay thế. Phủ mọi file trong `docs/**` (mọi phần mở rộng) cùng `README.md`, `CLAUDE.md`, `src/Shared.DecisionContractSDK/README.md`, `tests/load/README.md`, `docs/contracts/schemas/README.md`. File **lịch sử** dạng `.md` phải mở đầu bằng banner `> **Tài liệu lịch sử**` (ngày, lý do, tài liệu thay thế). | Spec 4.7 DOC-1. Một nơi kiểm kê duy nhất; banner cho người đọc biết không dùng làm nguồn sự thật. |
| **M-06** | **DOC-4:** bảng tham chiếu cấu hình **mới** `docs/guides/CONFIGURATION_REFERENCE.md` (nơi sở hữu duy nhất); mỗi khóa một dòng bốn cột: khóa `Tadss:Section:Key` (trong dấu backtick, cột đầu), kiểu, mặc định, ý nghĩa / ràng buộc của validator. Test `ConfigurationDocumentationTests` (UT, `CatalogId = DOC-4`) so **hai chiều**: tập khóa trong bảng = tập `{SectionName}:{thuộc tính public có setter}` của mọi kiểu có hằng `SectionName` bắt đầu bằng `Tadss:` trong các assembly của `SourceTree`. Thuộc tính kiểu phức (danh sách, đối tượng lồng) là một dòng, mô tả các khóa con trong cột ý nghĩa. README giữ ví dụ ngắn và liên kết tới bảng. | README hiện chỉ có 5 mục rời cho 14 section. Hai chiều để bắt cả khóa thiếu tài liệu lẫn khóa tài liệu đã gỡ khỏi mã. |
| **M-07** | **Đóng băng v2.0.0** ngay trong gói (người dùng nới R-03 ngày 11/10/2026). Đổi `2.0.0-draft` → `2.0.0` ở: `<Version>` SDK (sinh lại 5 file `packages.lock.json` bằng `dotnet restore --force-evaluate`), `Info.Version` OpenAPI (code + `openapi-v2.yaml` + hai test), header baseline (generator trong `PublicApiSurfaceTests` + file), trạng thái INTEGRATION_SPEC, README/CLAUDE.md/skill `tadss-architecture`. | Quy tắc sau đóng băng đã có ở INTEGRATION_SPEC Mục 6. Đây là lần đổi baseline có chủ đích duy nhất (D-06). |
| **M-08** | **T5.22** viết tại **`SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md`** (thư mục mới; BE chưa có `docs/`). Tài liệu đánh dấu "đặc tả cho Phase 6, chưa hiện thực"; envelope, mã lỗi, vai trò, tên sự kiện SignalR theo quy ước BE (skill `smashcourt-be-architecture`). INTEGRATION_SPEC §2.2 thay câu "bản đề xuất…" bằng liên kết tới file này. | R-02. BE chưa có mã cho các endpoint nên chưa sinh OpenAPI. |
| **M-09** | **Số lượt k6 bị bỏ theo pha:** `options.js` thêm threshold luôn đạt `dropped_iterations{scenario:<tên>}: ['count>=0']` cho từng scenario (`warmup`, `main`) để `summary.json` có số liệu từng pha; ghi chú của bước (b) nói rõ pha. LT-02 tăng `preAllocatedVUs` 100 → **400** (cả hai chế độ; lượt r1 dùng tối đa 317 VU, bỏ ~184 lượt **ở pha chính** vì k6 cấp VU giữa chừng không kịp). LT-01 giữ nguyên (lượt bỏ chỉ ở warm-up, đúng chủ đích của warm-up). | Ghi chú cũ "thường ở pha khởi động" sai với LT-02. Threshold `count>=0` không bao giờ làm k6 thoát mã lỗi. |
| **M-10** | **Chốt Evidence Pack** của lượt đầy đủ vào `docs/research/evidence-packs/{run_id}/` (Task 10): giữ `manifest.json`, `report.html`, `config/`, `grafana/`, `steps.json`, CSV tổng hợp (D4, summary D2; D5 **chỉ khi** người dùng đã xác nhận U-2 — chưa xác nhận lúc làm Task 10 thì mặc định **không** đưa D5, chỉ `pt_summary.json` và phân tích PT-02 đã có trong `report.html`, ghi vào báo cáo; xác nhận sau thì bổ sung D5 bằng một commit riêng); file > 20 MB (D1, `requests.jsonl` lớn) **không commit**, ghi vào mục "GitHub Release" của báo cáo để người dùng đính kèm; manifest giữ nguyên băm. Không bao giờ đưa `data/private/` vào. Thu hẹp bởi gói 5.7b (Q-2, 14/10/2026): `pt_summary.json`, `policy_economics.json`, D5 và mục PT của `report.html` chỉ vào pack chính thức sau khi xác minh giấy phép của đúng nguồn đang ghim; quyết định từng artifact. | DATASET_SCHEMA Mục 2; O-21 của 5.5. |

---

## Chuẩn bị và việc của người dùng

| # | Việc | Khi nào | Ghi chú |
| :--- | :--- | :--- | :--- |
| U-1 | Duyệt plan, đặc biệt M-03, M-05, M-08 | Trước Task 1 | |
| U-2 | Xác nhận giấy phép Hotel Booking Demand (CC BY 4.0, DOI 10.1016/j.dib.2018.11.126) | Trước Task 10 (không chặn) | Quyết định D5 có vào `docs/research/evidence-packs/` hay không; chưa xác nhận thì mặc định không đưa D5 (M-10) |
| U-3 | Xuất CSV SmashCourt vào `data/private/smashcourt_bookings.csv` (guide mục 8) | Sau Task 9, trước lượt đầy đủ | R-04: lượt chính thức có RP |
| U-4 | Commit theo commit message của Task 9, rồi chạy `$env:EVAL_MODE="full"; docker compose up -d --build` (~3 giờ, tắt chế độ ngủ), gửi `docker compose logs --tail 60 evaluator` + `docker compose ps -a`; đóng cửa sổ PowerShell sau đó | Sau U-3 | Commit trước để EV-5 ghi đúng commit và SC7 xét được git |
| U-5 | SV2 review T5.22 (T5.29) và góp ý contract (T5.24) | Song song | Góp ý đòi đổi wire ⇒ INTEGRATION_SPEC Mục 6 |

---

## Cấu trúc đích

```text
TADSS-Runtime/
├── .github/workflows/ci.yml                         # + job "Docs (DOC-1…DOC-3)" (Task 3)
├── docs/
│   ├── README.md                                    # MỚI: bảng kiểm kê DOC-1 (Task 4)
│   ├── guides/CONFIGURATION_REFERENCE.md            # MỚI: bảng Tadss:* DOC-4 (Task 5)
│   ├── research/evidence-packs/{run_id}/            # MỚI: Evidence Pack chính thức (Task 10)
│   └── contracts/…                                  # 2.0.0 (Task 8)
├── src/
│   ├── Shared.DecisionContractSDK/*.csproj          # <Version>2.0.0 (Task 8)
│   └── TADSS.Runtime.Api/
│       ├── appsettings.json                         # override Serilog (Task 1)
│       └── Composition/PresentationServiceCollectionExtensions.cs   # Info.Version (Task 8)
├── tests/
│   ├── load/{lib/options.js, config/modes.json}     # threshold theo pha, VU LT-02 (Task 2)
│   └── TADSS.UnitTests/
│       ├── Api/Composition/LoggingConfigurationTests.cs        # MỚI (Task 1)
│       └── Architecture/ConfigurationDocumentationTests.cs     # MỚI (Task 5)
└── tools/
    ├── docs/                                        # MỚI (Task 3)
    │   ├── check_docs.py
    │   └── tests/{conftest.py, test_check_docs.py}
    ├── evaluation/evaluator/steps/{tests_step.py, load_step.py, scale_step.py}
    └── evidence/kpi.py                              # SC4 (Task 2)
SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md       # MỚI (Task 6)
```

---

### Task 0: Khép báo cáo gói 5.6

**Files:** Modify `spec/PHASE5_REPORT_5.6.md` (không stage).

**Yêu cầu:** bổ sung các mục còn thiếu theo Task 7 của plan 5.6, lấy số liệu thật:
- đối chiếu Phụ lục A plan 5.6 có cột trạng thái;
- lượt `EV-20261011-EVAL-quick-r1`: thời lượng từng bước (547/404/510/53/101/83 s), KPI SC1…SC8, EV-1…EV-5, PT (10.000/10.000, 0 lỗi contract, 107 request/s), RP `skipped`;
- các sự cố đã gặp và cách sửa trong đợt D-14 (mạng external, ngrok `service_healthy`, KeySha256 chứa key gốc → `hash_api_key.py`, migration Neon, CRLF, loại `tools/evidence` khỏi `/work`, EV-5 thiếu `.git`, cạn kết nối PostgreSQL → `max_connections=350` + đảo thứ tự bước (a));
- việc của người dùng (U-2, U-3) và mục chuyển sang 5.7 (trỏ tới plan này).

**Nghiệm thu:** báo cáo có đủ các mục mà Task 7 (yêu cầu 4) của plan 5.6 liệt kê — đối chiếu Phụ lục A có cột trạng thái, bảng spike S-1…S-5, tham số PT đã duyệt, kết quả RP/PT, thời lượng đo thật, việc của người dùng, mục chuyển sang 5.7 — và không còn dòng "bản đang viết".

---

### Task 1: Bỏ log ERR giả của 404 (M-01)

**Files:**
- Modify: `src/TADSS.Runtime.Api/appsettings.json` (khối `Serilog:MinimumLevel:Override`).
- Create: `tests/TADSS.UnitTests/Api/Composition/LoggingConfigurationTests.cs`.

- [ ] **Step 1: Viết test đỏ**

```csharp
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-08: middleware <c>ExceptionHandlerMiddleware</c> của ASP.NET Core luôn log Error trước khi gọi <c>GlobalExceptionHandler</c>, kể cả với lỗi 4xx (404 không tìm thấy quyết định);
/// cấu hình gốc hạ category đó xuống Fatal để chỉ còn dòng log của <c>GlobalExceptionHandler</c> với đúng mức (5xx Error, 4xx Warning).
/// </summary>
public class LoggingConfigurationTests
{
    private const string Category = "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware";

    /// <summary><c>appsettings.json</c> đặt override Serilog <c>Fatal</c> cho đúng category của middleware.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void AppSettings_SilencesExceptionHandlerMiddlewareCategory()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepositoryPaths.Resolve("src", "TADSS.Runtime.Api", "appsettings.json"), optional: false)
            .Build();

        configuration[$"Serilog:MinimumLevel:Override:{Category}"].Should().Be("Fatal");
    }
}
```

- [ ] **Step 2:** `dotnet test tests/TADSS.UnitTests -c Release --filter "FullyQualifiedName~LoggingConfigurationTests"` → FAIL (giá trị null).
- [ ] **Step 3:** thêm dòng `"Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware": "Fatal"` vào `Serilog:MinimumLevel:Override` của `appsettings.json` (cạnh `"Microsoft.AspNetCore": "Warning"`).
- [ ] **Step 4:** build `--warnaserror` + chạy lại test → PASS; toàn bộ unit test xanh (`AppSettingsValidationTests` vẫn đạt).
- [ ] **Step 5 (hành vi thật):** test ở Step 1 khóa cấu hình; hành vi trên container được xác nhận ở lượt đầy đủ (Task 10, yêu cầu 2): log `eval-tadss-*` không còn dòng `ERR` "An unhandled exception has occurred" đi kèm 404 `DECISION_NOT_FOUND`, còn 5xx thật vẫn có dòng `ERR` của `GlobalExceptionHandler`.

**Nghiệm thu:** test mới xanh; unit test 1871 → 1872; không đổi mã ngoài `appsettings.json`.

---

### Task 2: Ghi chú evaluator và k6 nói đúng sự thật (M-09, SC4, SCALE-2)

**Files:**
- Modify: `tools/evidence/kpi.py` (`sc4`), `tools/evidence/tests/test_kpi_rules.py`.
- Modify: `tools/evaluation/evaluator/steps/scale_step.py` (`evaluate_scale2`), `tools/evaluation/tests/test_scale_checks.py`.
- Modify: `tests/load/lib/options.js` (`buildOptions`), `tests/load/config/modes.json` (LT-02 `preAllocatedVUs` 400 ở `quick` và `full`).
- Modify: `tools/evaluation/evaluator/steps/load_step.py` (`dropped_iterations`, `_note_dropped_iterations`), `tools/evaluation/tests/test_load_step.py`.

**Interfaces:**
- `load_step.dropped_iterations(summary_path: Path) -> dict[str, int] | None` — trả `{"warmup": n, "main": n}` khi summary có metric theo scenario; `{"total": n}` khi chỉ có metric tổng (summary cũ); `None` khi không đọc được. (Đổi kiểu trả từ `int | None`; cập nhật mọi chỗ gọi.)

**Yêu cầu và test (viết test đỏ trước cho từng ý):**
1. **SC4:** giá trị KPI nêu lý do khi không đạt.
   ```python
   failures = problems + ([] if scale2_passed else ["SCALE-2 KHÔNG ĐẠT"])
   value = "; ".join([*failures, f"{violations} vi phạm"])
   ```
   Test: danh mục có `IT-04` đỏ ⇒ `verdict == FAIL` và `value` bắt đầu bằng `"IT-04: 1 ca đỏ"`; SCALE-2 trượt ⇒ `value` chứa `"SCALE-2 KHÔNG ĐẠT"`; đạt hết ⇒ `value == "0 vi phạm"` (giữ nguyên như cũ).
2. **SCALE-2:** chi tiết `both_instances_in_prometheus` in số tròn: `{name: round(value) for name, value in instance_increase.items()}`; quyết định đạt/không vẫn dùng số thực. Test: `{"tadss-1": 1475.047, "tadss-2": 1459.28}` ⇒ detail chứa `'tadss-1': 1475` và không chứa `1475.04`.
3. **k6 theo pha:** trong `buildOptions`, sau khi dựng `scenarios`:
   ```js
   // Threshold luôn đạt chỉ để summary.json có dropped_iterations theo từng scenario (evaluator ghi chú đúng pha); không dừng lượt
   thresholds: Object.fromEntries(Object.keys(scenarios).map((name) => [`dropped_iterations{scenario:${name}}`, ['count>=0']])),
   ```
   Kiểm bằng `docker run --rm --entrypoint k6 -v "$(pwd)/tests/load:/load" -w /load/scenarios tadss-evaluator:local inspect lt01.js` (không gửi tải): `thresholds` có hai khóa `warmup`/`main`.
4. **Ghi chú bước (b):** đọc `metrics["dropped_iterations{scenario:warmup}"]` và `["dropped_iterations{scenario:main}"]` (dạng phẳng `{count, rate}` hoặc lớp `values`, như hàm hiện có). Câu ghi chú:
   - chỉ warm-up: `lt01: k6 bỏ 294 lượt ở warm-up, 0 ở pha chính (pha chính đủ tải)`;
   - có pha chính: `lt02: k6 bỏ 184 lượt ở pha chính — tải thực tế thấp hơn danh nghĩa (xem summary.json)`;
   - summary cũ chỉ có tổng: giữ câu cũ. Test cả ba ca bằng `summary.json` tạm.

**Nghiệm thu:** pytest hai bộ xanh (thêm ≥ 6 test); `k6 inspect` thấy threshold; không đổi ngưỡng/kết luận KPI.

---

### Task 3: Định nghĩa DOC-1…DOC-4 và bộ kiểm `tools/docs` (M-02, M-03, M-04)

**Files:**
- Modify: `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md` — thêm **Mục 4.15 DOC — Tài liệu (gói 5.7)** (bảng `ID | Kiểm chứng | Ai chạy | Xây dựng`, đúng định dạng cột ID mà `CatalogIndex.KnownIds()` đọc) và một dòng tổng quan "Kiểm tra tài liệu (DOC-1…4)" ở bảng đầu Mục 4.
- Create: `tools/docs/check_docs.py`, `tools/docs/__init__.py` (rỗng nếu cần cho import), `tools/docs/tests/conftest.py`, `tools/docs/tests/test_check_docs.py`.
- Modify: `.github/workflows/ci.yml` — job mới `docs` ("Docs (DOC-1…DOC-3)"): checkout, setup Python 3.11, `pip install pytest`, `python tools/docs/check_docs.py`, `python -m pytest -q tools/docs`.
- Modify: `tools/evaluation/evaluator/steps/tests_step.py` — `PYTEST_SUITES` thêm `"tools/docs"`; thêm kiểm `("check_docs.py", ["python", "tools/docs/check_docs.py"], pytest_env, "check-docs.log")` chạy cùng nhóm pytest; `tools/evaluation/tests/test_tests_step.py` cập nhật thứ tự lệnh mong đợi.
- Modify: `docker/evaluator/Dockerfile` chỉ khi image thiếu thứ gì (không cần package mới).

**Nội dung Mục 4.15 (chép nguyên, chỉnh câu chữ khi viết):**

| ID | Kiểm chứng | Ai chạy | Xây dựng |
| :--- | :--- | :--- | :--- |
| DOC-1 | Mọi file trong `docs/**` và các README/CLAUDE.md của repo có đúng một dòng trong bảng kiểm kê `docs/README.md`, nhóm `hiện hành` hoặc `lịch sử`; file lịch sử dạng markdown mở đầu bằng banner `> **Tài liệu lịch sử**` | tự động (`tools/docs/check_docs.py`: CI, bước (a)) | P5 |
| DOC-2 | Tài liệu hiện hành không mô tả như hiện tại những thứ đã gỡ (danh sách `REMOVED_TERMS`): mỗi lần xuất hiện phải nằm trong file lịch sử hoặc trên dòng nói về quá khứ/cấm (`gỡ`, `đã bỏ`, `thay cho`, `thay bằng`, `không còn`, `không dùng`, `cấm`, `phase4-baseline`, `lịch sử`) | tự động (như DOC-1) | P5 |
| DOC-3 | Mọi link nội bộ tương đối trong markdown của repo trỏ tới file tồn tại; `#anchor` khớp tiêu đề (slug GitHub); link ra ngoài repo và link http không kiểm | tự động (như DOC-1) | P5 |
| DOC-4 | Bảng `docs/guides/CONFIGURATION_REFERENCE.md` khớp hai chiều với các `Options` có `SectionName` `Tadss:*` trong mã | tự động (unit test) | P5 |

**`check_docs.py` — hàm công khai (test gọi trực tiếp):**

```python
REMOVED_TERMS: tuple[str, ...] = ("MediatR", "BenchmarkRunner", "UseInMemoryDatabase", "EF InMemory", "AuditTimestampInterceptor", "/api/v1/",
                                  "--profile evaluation", "profile `evaluation`", "-p tadss-eval", "tools/chaos/run-fi.sh", "tools/contract/run-schemathesis.sh",
                                  "docker-compose.ngrok.yml", "docker-compose.neon.yml")
PAST_MARKERS: tuple[str, ...] = ("gỡ", "đã bỏ", "thay cho", "thay bằng", "không còn", "không dùng", "cấm", "phase4-baseline", "lịch sử")
EXCLUDED_DIRS = frozenset({".git", "spec", ".superpowers", "evidence", "data", "bin", "obj", "node_modules", "TestResults", ".vs"})

def load_inventory(root: Path) -> dict[str, str]: ...          # {đường dẫn tương đối dạng posix: "hiện hành" | "lịch sử"}; lỗi định dạng ⇒ InventoryError
def inventory_problems(root: Path, inventory: dict[str, str]) -> list[str]: ...   # DOC-1: file thiếu trong bảng, dòng trỏ file không tồn tại, file lịch sử .md thiếu banner
def removed_term_problems(root: Path, inventory: dict[str, str]) -> list[str]: ... # DOC-2: "đường_dẫn:dòng: 'MediatR'"
def link_problems(root: Path) -> tuple[list[str], int]: ...    # DOC-3: (vấn đề, số link ra ngoài repo đã bỏ qua)
def github_slug(heading: str) -> str: ...                      # chữ thường, bỏ ký tự không phải chữ/số/khoảng trắng/gạch ngang (giữ chữ có dấu), khoảng trắng → '-'
def main(argv: list[str] | None = None) -> int: ...            # --root (mặc định: thư mục cha của tools/); in 3 dòng DOC-x; thoát 0 khi cả ba đạt
```

Quy tắc chi tiết:
- DOC-2 quét các `.md` thuộc nhóm hiện hành **và** `README.md`/`CLAUDE.md`, **kể cả khối code** (một lệnh mẫu dùng thứ đã gỡ cũng là mô tả sai).
- DOC-3 bỏ qua link trong khối code; anchor trùng tiêu đề đánh số `-1`, `-2` như GitHub; link `#anchor` trong cùng file kiểm trên chính file đó; đích có `%20` được giải mã.
- Thông báo lỗi tiếng Việt, mỗi vấn đề một dòng, có đường dẫn và số dòng.

**Ghi thêm vào mô tả DOC-2 trong methodology:** đoạn văn kể lại một quyết định cũ mà không có dấu hiệu quá khứ (ví dụ "MediatR cung cấp pipeline…" trong một ADR) là vi phạm, trừ khi cả file là tài liệu lịch sử; cách xử lý là phân loại file (Task 4) hoặc viết lại câu, không thêm từ khóa cho lọt.

**`github_slug` — các ca bắt buộc có test (khớp cách GitHub sinh anchor):**

| Tiêu đề | Slug |
| :--- | :--- |
| `API Endpoint` | `api-endpoint` |
| `Đánh giá` | `đánh-giá` (giữ chữ có dấu) |
| `C# Code` | `c-code` |
| `1. Ba cách chạy TADSS` | `1-ba-cách-chạy-tadss` (bỏ dấu chấm) |
| `4.15. DOC — Tài liệu (gói 5.7)` | `415-doc--tài-liệu-gói-57` (bỏ gạch dài và ngoặc, **không** gộp hai gạch ngang) |
| ``Cấu hình `Tadss:Security:*` `` | `cấu-hình-tadsssecurity` |
| hai tiêu đề `Test` trong cùng file | `test`, `test-1` |

**Test (pytest, cây tạm `tmp_path`):** tối thiểu một test cho mỗi nhánh: file thiếu trong bảng; dòng bảng trỏ file không tồn tại; file lịch sử thiếu banner; term trong file hiện hành ⇒ lỗi; term trên dòng có `đã gỡ` ⇒ không lỗi; term trong file lịch sử ⇒ không lỗi; link file thiếu ⇒ lỗi; anchor sai ⇒ lỗi; anchor tiêu đề tiếng Việt có dấu đúng ⇒ không lỗi; link trong khối code ⇒ bỏ qua; link ra ngoài repo ⇒ đếm, không lỗi; `main` thoát 0/1 đúng.

**Thứ tự thực hiện:** methodology Mục 4.15 trước (mã DOC-x phải có trong danh mục), rồi TDD `check_docs.py`. Ở cuối task, **chưa** chạy `check_docs.py` trên repo thật cho đạt — việc dọn vi phạm thuộc Task 4; CI job và bước (a) sẽ đỏ cho tới hết Task 4 (ghi vào báo cáo, không commit giữa chừng).

**Nghiệm thu:** pytest `tools/docs` xanh (≥ 12 test); pytest `tools/evaluation` xanh với thứ tự lệnh mới; `CatalogTraceabilityTests` vẫn xanh.

---

### Task 4: Bảng kiểm kê tài liệu và dọn vi phạm (DOC-1, DOC-2, DOC-3)

**Files:**
- Create: `docs/README.md`.
- Modify: các tài liệu `check_docs.py` báo lỗi; thêm banner cho tài liệu lịch sử.

**Yêu cầu:**
1. Phân loại **từng** file (danh sách hiện có: 35 file trong `docs/**` + các README/CLAUDE.md của M-05). Tiêu chí:
   - **hiện hành:** sở hữu một chủ đề và phải khớp mã/kế hoạch hiện tại (đặc tả kiến trúc, contract, methodology, dataset schema, kế hoạch, guide, schema SQL…);
   - **lịch sử:** ảnh chụp tại một thời điểm, không cập nhật theo mã (ADR đã bị thay thế…). Mỗi file lịch sử ghi tài liệu thay thế ở cột 3.
   - **Đã chốt (11/10/2026):** hai file `.docx` trong `docs/` là bản đã nộp ⇒ **lịch sử** (nguồn hiện hành: `docs/planning/PROPOSAL_DO_AN_2.md` và các tài liệu sở hữu). ADR-001…004 (Accepted, khớp mã) và ADR-005 (định hướng khóa luận, tự ghi ngoài phạm vi Đồ án 2) ⇒ **hiện hành**.
   - File phân vân (ví dụ `docs/planning/PROPOSAL_DO_AN_2.md`, `docs/research/RESEARCH_THESIS_DEFENSE.md`, `docs/architecture/adr/ADR-005-why-catboost-ml.md`): đọc nội dung, đối chiếu mã.
   - **Điểm dừng — người dùng duyệt trước khi thêm banner hoặc sửa vi phạm.** Gửi một bảng cho **mọi** file (không chỉ file phân vân), các file chắc chắn để cuối bảng:

     | File | Nhóm đề xuất | Lý do (dẫn chứng từ nội dung/mã) | Tài liệu thay thế (nếu lịch sử) |
     | :--- | :--- | :--- | :--- |

     Lý do phải kiểm được (ví dụ "đề cương đã nộp ngày …, nội dung mô tả kiến trúc v1"), không suy đoán.
2. Viết `docs/README.md`: đoạn mở đầu (vai trò, quy tắc "tài liệu sở hữu thắng"), bảng kiểm kê, mục "Cách kiểm" (`python tools/docs/check_docs.py`).
3. Chạy `python tools/docs/check_docs.py`; sửa từng vi phạm **tại tài liệu sở hữu**:
   - DOC-2, file **hiện hành**, câu mô tả thứ đã gỡ như đang dùng ⇒ viết lại theo hiện trạng (hoặc xóa nếu thừa).
   - DOC-2, file **hiện hành**, câu đúng là ghi lại lịch sử (ví dụ mục lưu trữ Phase 4 trong methodology) ⇒ viết lại ở thì quá khứ cho đúng nghĩa. **Không** đổi cả file sang lịch sử chỉ vì một đoạn: file chỉ là lịch sử khi **toàn bộ** là ảnh chụp không cập nhật.
   - DOC-2, file **lịch sử** ⇒ đã được miễn, không sửa nội dung (chỉ thêm banner).
   - Không chèn từ khóa chỉ để lọt quy tắc; câu sau khi sửa phải đúng nghĩa.
   - DOC-3: sửa đường dẫn/anchor.
4. Banner lịch sử theo mẫu:
   ```markdown
   > **Tài liệu lịch sử** — ảnh chụp ngày dd/mm/yyyy, không cập nhật theo mã. Nguồn hiện hành: [<tài liệu thay thế>](<đường dẫn>).
   ```

**Nghiệm thu:** `python tools/docs/check_docs.py` thoát 0 (in `DOC-1: đạt`, `DOC-2: đạt`, `DOC-3: đạt (… link ra ngoài repo bỏ qua)`); danh sách file đổi nhóm/banner trong báo cáo; người dùng đã duyệt các file phân vân.

---

### Task 5: Bảng tham chiếu cấu hình và test DOC-4 (M-06)

**Files:**
- Create: `docs/guides/CONFIGURATION_REFERENCE.md`, `tests/TADSS.UnitTests/Architecture/ConfigurationDocumentationTests.cs`.
- Modify: `README.md` (các mục cấu hình rời trỏ tới bảng), `docs/README.md` (thêm dòng kiểm kê), `TADSS-Runtime/CLAUDE.md` (một dòng: thêm options mới phải thêm dòng vào bảng).

- [ ] **Step 1: test đỏ** (bảng chưa tồn tại):

```csharp
/// <summary>
/// DOC-4: bảng <c>docs/guides/CONFIGURATION_REFERENCE.md</c> khớp hai chiều với cấu hình thật: mỗi kiểu có hằng <c>SectionName</c> bắt đầu bằng <c>Tadss:</c>
/// góp các khóa <c>{SectionName}:{thuộc tính public có setter}</c>; tài liệu liệt kê mỗi khóa đúng một dòng dạng <c>| `Tadss:...` |</c>.
/// </summary>
public class ConfigurationDocumentationTests
{
    private static readonly Regex DocumentedKey = new(@"^\|\s*`(Tadss:[A-Za-z0-9:]+)`\s*\|", RegexOptions.Multiline);

    /// <summary>Không thiếu khóa nào của mã và không có khóa tài liệu đã gỡ khỏi mã; không khóa nào xuất hiện hai lần.</summary>
    [Fact]
    [Trait("CatalogId", "DOC-4")]
    public void ConfigurationReference_MatchesOptionsTypes()
    {
        var text = File.ReadAllText(RepositoryPaths.Resolve("docs", "guides", "CONFIGURATION_REFERENCE.md"));
        var documented = DocumentedKey.Matches(text).Select(match => match.Groups[1].Value).ToList();

        var expected = SourceTree.Assemblies.Values.Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Select(type => (Type: type, Section: type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string))
            .Where(item => item.Section is not null && item.Section.StartsWith("Tadss:", StringComparison.Ordinal))
            .SelectMany(item => item.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => $"{item.Section}:{property.Name}"))
            .ToHashSet(StringComparer.Ordinal);

        documented.Should().OnlyHaveUniqueItems("mỗi khóa đúng một dòng");
        documented.Should().BeEquivalentTo(expected, "bảng cấu hình phải khớp hai chiều với các Options của mã");
    }
}
```

   Kiểm trước khi viết: `SourceTree.Assemblies` có phủ đủ 5 project `src/` (gồm hai strategy) — nếu helper tìm options của `AppSettingsValidationTests` đã có hàm khám phá dùng chung thì **dùng lại** thay cho đoạn reflection trên.
- [ ] **Step 2:** chạy → FAIL (không có file).
- [ ] **Step 3:** viết `CONFIGURATION_REFERENCE.md`: mỗi section một mục `### Tadss:X` (tên lớp options, project), bảng `| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |`. Mặc định lấy từ giá trị khởi tạo của thuộc tính và `appsettings.json` (ghi rõ nguồn khi khác nhau); ràng buộc chép từ `{X}OptionsValidator`. Thêm mục đầu: thứ tự nguồn cấu hình (appsettings → appsettings.{Env} → biến môi trường `Tadss__X__Y`), ví dụ biến môi trường của compose đánh giá.
- [ ] **Step 4:** test xanh; build 0/0; unit 1872 → 1873; `check_docs.py` vẫn đạt.

**Nghiệm thu:** test DOC-4 xanh; README không còn mô tả khóa trùng lặp với bảng (chỉ ví dụ + liên kết).

---

### Task 6: Đặc tả API SmashCourt BE ↔ FE cho cọc / chờ duyệt / duyệt (T5.22, M-08)

**Files:**
- Create: `SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md`.
- Modify: `TADSS-Runtime/docs/contracts/INTEGRATION_SPEC.md` §2.2 — sơ đồ và ghi chú đã đồng bộ ngày 11/10/2026 (route theo `bookingId`, hàng chờ từ DB của BE, `deposit.rate`); Task 6 chỉ đối chiếu lại cho khớp tên route cuối cùng của file BE (văn xuôi, **trước** Task 8). `docs/planning/BACKLOG_SPRINTS.md` (T5.22: trạng thái xong).

**Trước khi viết:** dùng skill `smashcourt-be-architecture`; đọc `SmashCourt-BE/CLAUDE.md`, `Common/ErrorCodes.cs`, `Common/Constants/SignalREvents.cs`, `Controllers/BookingController.cs`, `CustomerBookingController.cs`, DTO booking hiện có, `docs/use-cases/05-dat-san-online.md` (workspace gốc), INTEGRATION_SPEC §1, §2, §4.

**Nội dung bắt buộc:**
1. **Trạng thái và phạm vi:** đặc tả cho Phase 6, **chưa hiện thực**; nguồn sở hữu của hợp đồng BE ↔ FE cho luồng cọc; INTEGRATION_SPEC vẫn sở hữu BE ↔ TADSS. Người review: SV2 (T5.29).
2. **Thay đổi dữ liệu nhìn thấy từ FE:** trạng thái booking mới `PENDING_APPROVAL`; các trường cọc trên booking/hóa đơn (`depositAmount`, `depositRate`, `remainingAmount`, `depositSource` ∈ `TADSS`/`MANAGER`/`FULL_PAYMENT_FALLBACK`, `approvalExpiresAt`), theo INTEGRATION_SPEC §4.3–4.4.
3. **Endpoint đã có đổi response:** `POST /api/bookings/online` (ba nhánh: thanh toán cọc / chờ duyệt / bị từ chối; mọi sự cố TADSS ⇒ 100 %), chi tiết booking của khách và của staff.
4. **Endpoint mới — định danh bằng `bookingId`** (người dùng chốt 11/10/2026; tên route cuối cùng theo quy ước BE, mẫu dưới đây):
   - FE chỉ dùng `bookingId`; BE tra `tadss_decision_id` đã lưu trên booking/hóa đơn (INTEGRATION_SPEC §4.4) rồi gọi TADSS. FE không bao giờ thấy `decisionId`.
   - **Hàng chờ đọc từ DB của BE** (booking `PENDING_APPROVAL`, `approvalExpiresAt > now`, kèm tên khách/sân/giờ), không gọi TADSS mỗi lần mở hàng chờ — khớp "trạng thái booking của BE là nguồn sự thật" (§4.3). `GET /api/v2/decisions/pending-approvals` của TADSS giữ cho đối soát/vận hành.
   - Hệ quả: sửa văn xuôi + sơ đồ INTEGRATION_SPEC §2.2 (BE không còn gọi `pending-approvals` để dựng hàng chờ; route BE theo `bookingId`). Làm **trước** Task 8 (không đổi wire TADSS).
   - `GET /api/bookings/pending-approval?branchId=&page=&pageSize=` — `BRANCH_MANAGER` chỉ chi nhánh của mình, `OWNER` mọi chi nhánh;
   - `POST /api/bookings/{bookingId}/approve` — body mức cọc **dạng tỷ lệ** (người dùng chốt 11/10/2026: Manager nhập phần trăm, BE gửi `deposit.rate`, TADSS quy đổi ra số tiền; tỷ lệ 0…1, tối đa 4 chữ số thập phân như contract TADSS) + lý do;
   - `POST /api/bookings/{bookingId}/reject` — lý do.
   - BE kiểm theo thứ tự: quyền chi nhánh → booking đang `PENDING_APPROVAL` và chưa hết hạn (theo DB của BE) → gọi override TADSS với `X-Idempotency-Key` ổn định cho thao tác duyệt của booking đó → cập nhật booking theo kết quả.
   Mỗi endpoint: vai trò, request, response trong envelope `{ success, code, message, data, errors }`, mã lỗi, và **ví dụ JSON đầy đủ** của một request và của response thành công lẫn một response lỗi (giá trị mẫu, không dữ liệu thật). Ba nhánh của `POST /api/bookings/online` mỗi nhánh một ví dụ response.
5. **Mã lỗi đề xuất** (thêm vào `ErrorCodes.cs` ở Phase 6, chưa thêm bây giờ): bảng ánh xạ **từng** tình huống → mã BE → HTTP → FE hiển thị gì:
   - mã TADSS `ALREADY_OVERRIDDEN`, `APPROVAL_EXPIRED`, `NOT_PENDING_APPROVAL`, `DECISION_NOT_FOUND`, `VALIDATION_ERROR` (mức cọc sai) khi duyệt;
   - TADSS không phản hồi / 5xx / 401 / 429 khi duyệt (khác với lúc đặt sân: duyệt **không** có đường lùi 100 %, Manager phải thử lại);
   - quyền: Manager duyệt đơn của chi nhánh khác.
   Mã BE đặt theo quy ước `ErrorCodes.cs` hiện có (kiểm tên trùng trước khi đề xuất).
6. **Sự kiện SignalR đề xuất** (tên theo `SignalREvents.cs`): có đơn chờ duyệt (tới nhóm Manager của chi nhánh), kết quả duyệt (tới khách và Manager), hàng chờ thay đổi. Mỗi sự kiện: tên, nhóm nhận, **payload JSON mẫu**, lúc phát; nêu rõ dữ liệu nào **không** gửi qua SignalR (FE tải lại qua REST).
7. **Tình huống biên:** hai Manager duyệt cùng lúc (409), duyệt sau hạn, TADSS không phản hồi khi duyệt, tắt `EnableTadssDecision` khi đang có đơn chờ, hết hạn duyệt ⇒ job hủy (không tự duyệt).
8. **Bảng truy vết E2E-01…E2E-10** (methodology 4.8) → endpoint/sự kiện dùng.
9. Câu hỏi mở cho SV2 (nếu có) thành danh sách riêng — không tự quyết thay.

**Nghiệm thu:** file đủ 9 mục; không đổi mã BE; người dùng duyệt nội dung (đây là hợp đồng với FE). Build BE không cần (chỉ tài liệu) nhưng ghi rõ "BE: chỉ thêm tài liệu".

---

### Task 7: Chốt môi trường tích hợp E-STAGING (T5.23)

**Files:** Modify `docs/planning/MASTER_EXECUTION_PLAN.md` (mục mới "Môi trường tích hợp E-STAGING", trỏ từ dòng W08 và Phase 7), `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md` Mục 2 (dòng E-STAGING: thêm liên kết tới mục mới), `docs/planning/BACKLOG_SPRINTS.md` (T5.23: trạng thái xong). Quyết định "nhánh Neon `staging`" đã được ghi ngày 11/10/2026 vào W08/Phase 7 của MASTER, T5.23/T7.2 của BACKLOG và dòng E-STAGING của methodology; Task 7 viết phần chi tiết.

**Nội dung bắt buộc:**
- Máy và cách chạy: ba repo mỗi repo một `docker-compose.yml`, chung mạng external `smashcourt-network`; thứ tự bật; BE gọi TADSS `http://tadss-runtime:8080` (không qua ngrok khi cùng máy); FE gọi BE.
- Dữ liệu: **đã chốt (11/10/2026) — nhánh Neon riêng `staging`** cho cả DB SmashCourt lẫn schema `tadss` (không dùng chung DB chính). Ghi vào mục: cách tạo/làm mới nhánh (xóa nhánh rồi tạo lại từ nhánh chính — dọn được cả ba bảng audit chỉ-thêm), chuỗi kết nối riêng cho BE/TADSS ở staging (đặt ở file bí mật/biến môi trường, không ghi vào tài liệu), migration `tadss` đi theo nhánh (nhánh tạo sau 10/10/2026 đã có v2), thời điểm xuất CSV RP so với E2E. Bảng so sánh dưới đây giữ trong mục làm phần lý do, mọi ô là sự thật đã kiểm: mọi ô phải là sự thật đã kiểm (cấu hình compose, `appsettings`, tài liệu Neon chính thức) — không ghi số giới hạn kết nối/gói miễn phí nếu chưa tra được nguồn. Tối thiểu so sánh:

  | Tiêu chí | A. Neon + Redis Cloud (như chế độ vận hành, D-11) | B. PostgreSQL + Redis cục bộ trong compose |
  | :--- | :--- | :--- |
  | E2E ghi booking/quyết định vào đâu | DB SmashCourt trên Neon **dùng chung** (cả BE lẫn schema `tadss`) | DB dùng một lần, xóa được |
  | Dữ liệu ban đầu (sân, giá, hạng khách) cho E2E | có sẵn | phải nạp: `schema_smashcourt.sql` + seed |
  | Giống môi trường demo/hội đồng | giống | khác |
  | Làm lại từ đầu giữa các lượt E2E | khó (phải dọn tay) | `down -v` |
  | Việc phải làm ở Phase 6 | ít | thêm service + script seed ở BE |

  Thêm cột C "Nhánh Neon `staging`" (phương án được chọn). Phạm vi gồm **cả BE**, không chỉ TADSS.
- VNPay sandbox: URL trả về công khai qua ngrok của BE; không lưu khóa VNPay trong tài liệu.
- Ai chạy gì ở Phase 7–8 (W13–W15: smoke E2E ở Gate M7, LT-04 và E2E-01…10 trên E-STAGING; Phase 6 Playwright chỉ chạy trên mock), cách thu Evidence (E2E chưa thuộc evaluator).
- Rủi ro: một máy chạy cả ba hệ + Docker (RAM), URL ngrok đổi mỗi lần.

**Nghiệm thu:** mục mới có đủ nội dung, ghi đúng phương án nhánh Neon đã chốt; `check_docs.py` đạt.

---

### Task 8: Đóng băng contract v2.0.0 (M-07)

**Files:**
- `src/Shared.DecisionContractSDK/Shared.DecisionContractSDK.csproj` (`<Version>2.0.0</Version>`), 5 file `packages.lock.json` (sinh lại).
- `src/TADSS.Runtime.Api/Composition/PresentationServiceCollectionExtensions.cs` (`Version = "2.0.0"`).
- `docs/contracts/openapi-v2.yaml` (dòng `version`), `docs/contracts/baseline_public_api.txt` (3 dòng header), `docs/contracts/INTEGRATION_SPEC.md` (dòng Trạng thái).
- Tests: `tests/TADSS.UnitTests/Api/Composition/HttpPipelineTests.cs`, `Api/OpenApi/OpenApiDriftTests.cs` (chuỗi version), `Architecture/PublicApiSurfaceTests.cs` (header sinh ra, summary và message).
- Tài liệu: `README.md`, `TADSS-Runtime/CLAUDE.md`, skill `tadss-architecture` (ngoài repo), `docs/planning/BACKLOG_SPRINTS.md` (T5.1, T5.21), `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md` nếu nhắc "bản nháp".

- [ ] **Step 1:** đổi chuỗi mong đợi trong ba test sang `2.0.0` / header mới:
  ```text
  # Shared.DecisionContractSDK Public API Surface Snapshot v2.0.0
  # Assembly: Shared.DecisionContractSDK, Version=2.0.0.0
  # STATUS: FROZEN v2.0.0 BASELINE (Gate M5, 11/10/2026; thay đổi theo INTEGRATION_SPEC Mục 6)
  ```
  Chạy → FAIL đúng ba test.
- [ ] **Step 2:** đổi `<Version>` SDK và `Info.Version`; `dotnet restore TADSS.slnx --force-evaluate` (sinh lại lock); `git diff --stat -- '*packages.lock.json'` chỉ đổi dòng `[2.0.0-draft, )` → `[2.0.0, )`.
- [ ] **Step 3:** chạy test → `OpenApiDriftTests` ghi bản thực tế ra `TestResults/openapi-v2.actual.yaml`; so diff với file commit: **chỉ** dòng `version` khác ⇒ chép sang `docs/contracts/`. Header baseline: sửa 3 dòng đầu file; thân giữ nguyên.
- [ ] **Step 4:** INTEGRATION_SPEC dòng Trạng thái: `v2.0.0 — **đã đóng băng** (11/10/2026, Gate M5); mọi thay đổi theo Mục 6`. README/CLAUDE.md/skill: bỏ "bản nháp"/"chờ đóng băng", ghi quy tắc sau đóng băng (đổi baseline ⇒ phiên bản lớn mới, trừ thay đổi nội bộ được phép ở Mục 6).
- [ ] **Step 5 (kiểm):**
  - build `--warnaserror --no-incremental` 0/0; unit + IT xanh;
  - `dotnet restore tests/TADSS.IntegrationTests --locked-mode` đạt (evaluator và Dockerfile dùng `--locked-mode`);
  - `docker build -t tadss-freeze-check .` đạt rồi xóa image;
  - `git diff -- docs/contracts/schemas/` rỗng; `git diff -- docs/contracts/baseline_public_api.txt` chỉ 3 dòng đầu; `git diff -- docs/contracts/openapi-v2.yaml` chỉ 1 dòng;
  - grep `2.0.0-draft` toàn repo (trừ `spec/`, `.superpowers/`, `evidence/`) ⇒ 0 kết quả.

**Nghiệm thu:** mọi mục kiểm đạt; contract không đổi wire.

---

### Task 9: Kiểm chứng phần mã, tài liệu đồng bộ, bàn giao lượt đầy đủ

**Files:** `README.md` (trạng thái, số test, mục tài liệu), `TADSS-Runtime/CLAUDE.md` (`tools/docs`, bảng cấu hình, contract đóng băng), `docs/guides/EVALUATION_RUN_GUIDE.md` (bước (a) có `check_docs.py` + pytest `tools/docs`; ghi chú k6 theo pha; mục 9 chạy tay `python tools/docs/check_docs.py`), methodology 6.1 bước (a), skill `build-verify` (sửa `docs/**`/`*.md` ⇒ chạy `check_docs.py`; sửa options ⇒ cập nhật bảng cấu hình), `docs/planning/BACKLOG_SPRINTS.md` + `MASTER_EXECUTION_PLAN.md` (trạng thái 5.7), `spec/PHASE5_DESIGN_SPEC.md` 4.7 (ghi M-xx đã hiện thực, **không stage**).

**Kiểm chứng (số liệu thật vào báo cáo):**
- `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror --no-incremental` 0/0; unit, IT xanh (đếm so với 1871/240).
- pytest ba bộ chạy riêng xanh; `python tools/docs/check_docs.py` thoát 0.
- `docker compose config -q` đạt; `ComposeFixtureAlignmentTests` xanh; `.sh` vẫn LF.
- `git diff --stat` đúng phạm vi của Global Constraints (src chỉ 3 file: `appsettings.json`, csproj SDK, `PresentationServiceCollectionExtensions.cs`).
- `git -C ../SmashCourt-BE status` chỉ có `docs/api/DEPOSIT_APPROVAL_API.md`.

**Bàn giao:** gợi ý hai commit message (TADSS-Runtime; SmashCourt-BE), lệnh stage loại `spec/`; hướng dẫn U-3, U-4 (lệnh chạy đầy đủ, hai lệnh gửi kết quả, đóng cửa sổ sau đó).

---

### Task 10: Sau lượt đầy đủ — phân tích, chốt Evidence Pack, báo cáo Gate M5

**Điều kiện bắt đầu:** người dùng gửi kết quả lượt `EV-…-EVAL-full-r{n}` (U-4).

**Files:**
- Create: `docs/research/evidence-packs/{run_id}/` (M-10), `spec/PHASE5_REPORT_5.7.md` (không stage).
- Modify: `spec/PHASE5_DESIGN_SPEC.md` D-12 (thời lượng bản đầy đủ đo thật — không stage), `README.md`, `docs/guides/EVALUATION_RUN_GUIDE.md` mục 6 (thời lượng đo thật thay "ước tính"), methodology 6.1, `docs/research/DATASET_SCHEMA_SPECIFICATION.md` (giấy phép theo U-2), `docs/README.md` (dòng kiểm kê pack), BACKLOG (T5.21) và MASTER_EXECUTION_PLAN (Gate M5).

**Yêu cầu:**
1. Phân tích lượt đầy đủ như các lượt nhanh: mỗi bước, mỗi KPI (SC1…SC8 — SC8 có mutation), EV-1…EV-5, RP-01/02 (có CSV), PT toàn bộ dataset, LT-05 (rò rỉ bộ nhớ/độ trễ theo thời gian).
   **Có tiêu chí không đạt:** tìm nguyên nhân gốc (skill `systematic-debugging`), sửa, không nới tiêu chí. Bước (f) chỉ tổng hợp dữ liệu **của chính lượt đang chạy**, nên:
   - kiểm nhanh bản sửa bằng `run.sh full --steps <các bước nguồn của KPI đó>,f` (nguồn: SC1 ← b; SC2 ← a, c; SC3 ← b, c, e; SC4 ← a, c; SC7 ← e; SC8 ← a, d; EV-1…5 ← mọi bước đã chạy);
   - **Evidence Pack chính thức phải là một lượt đầy đủ trọn vẹn** (spec 4.7: "Evidence Pack sinh từ lượt chạy đó") ⇒ sau khi bản sửa đạt, người dùng chạy lại cả lượt đầy đủ. Không ghép pack từ nhiều lượt.
   - Sửa đụng `src/` sau khi đã đóng băng: chỉ thay đổi nội bộ được phép ở INTEGRATION_SPEC Mục 6; đổi wire ⇒ dừng, hỏi người dùng.
2. Đếm log `ERR` của `eval-tadss-*` (kiểm M-01 trên lượt thật).
3. Chốt Evidence Pack theo M-10; chạy `python -m evidence.report docs/research/evidence-packs/{run_id}` dựng lại `report.html` từ bản đã chốt để chắc pack tự đủ; grep pack không có UUID thô/số điện thoại.
4. **Báo cáo Gate M5** (`spec/PHASE5_REPORT_5.7.md`): từng gạch đầu dòng của Gate M5 trong MASTER_EXECUTION_PLAN → trạng thái + minh chứng (đường dẫn); đối chiếu Phụ lục A; số test cuối; thời lượng đo thật; việc còn mở (T5.24/T5.29 của SV2, NG-4, U-2 nếu chưa xong); Docker sau gói (`docker system df`, bảng lệnh dọn — yêu cầu thường trực).
5. Gợi ý commit cuối (pack + tài liệu).

**Nghiệm thu:** Gate M5 có minh chứng cho mọi tiêu chí hoặc ghi rõ phần chưa đạt và lý do; tài liệu khớp số đo thật.

---

## Phụ lục A — Đối chiếu spec → task

| Yêu cầu (spec 4.7, R-01…R-04, và liên quan) | Task |
| :--- | :---: |
| DOC-1 bảng kiểm kê hiện hành/lịch sử ở `docs/README.md` | 3, 4 |
| DOC-2 script grep trong CI, file lịch sử miễn trừ | 3, 4 |
| DOC-3 link nội bộ, job CI | 3, 4 |
| DOC-4 bảng `Tadss:*` khớp Options, test | 3, 5 |
| Methodology thêm mã DOC-1…4 (spec Mục 9) | 3 |
| Chạy bản đầy đủ (E-EVAL), Evidence Pack từ lượt đó | U-4, 10 |
| Đóng băng v2 (baseline, `openapi-v2.yaml`, `schemas/v2/`), gỡ "bản nháp" ở INTEGRATION_SPEC | 8 |
| T5.22 API BE ↔ FE (R-02) | 6 |
| T5.23 E-STAGING trong MASTER_EXECUTION_PLAN | 7 |
| R-01 tồn đọng (log 404, SC4, k6, SCALE-2) | 1, 2 |
| R-04 lượt chính thức có RP | U-3, 10 |
| Bất biến 6.1 "tài liệu không mô tả thứ đã gỡ; link hợp lệ" có test khóa | 3, 4 |
| Gate M5 (MASTER_EXECUTION_PLAN) | 10 |
| Khép báo cáo 5.6 | 0 |

Không thuộc 5.7: hiện thực mã BE/FE cho luồng cọc, LT-04, E2E, FI-11…14, SC5/SC6 (Phase 6); review của SV2 (T5.24, T5.27, T5.29).

## Phụ lục B — Thay đổi cấu hình và hành vi cần duyệt

| # | Thay đổi | Nơi ghi |
| :--- | :--- | :--- |
| B-1 | Override Serilog cho `ExceptionHandlerMiddleware` (M-01) | `appsettings.json`, bảng cấu hình (Task 5) |
| B-2 | CI thêm job Docs; bước (a) thêm `check_docs.py` + pytest `tools/docs` (M-02) | `ci.yml`, guide, methodology 6.1 |
| B-3 | k6: threshold `count>=0` theo scenario; LT-02 `preAllocatedVUs` 400 (M-09) | `tests/load/`, methodology 4.6 nếu nhắc VU |
| B-4 | Contract 2.0.0 đóng băng; version gói SDK đổi (M-07) | INTEGRATION_SPEC, README, CLAUDE.md, skill |
| B-5 | Thư mục mới `SmashCourt-BE/docs/api/` (M-08) | BE `CLAUDE.md` nếu có mục tài liệu |
| B-6 | `docs/research/evidence-packs/{run_id}/` được commit (M-10) | DATASET_SCHEMA Mục 2 |

Không đổi: wire (route, DTO, schema JSON, mã lỗi, status), schema DB, migration, hành vi nghiệp vụ của runtime và strategy, tiêu chí đạt/không đạt của KPI.
