# Gói 5.2b — Lõi runtime v2: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dựng toàn bộ lõi xử lý quyết định v2 của runtime (chính sách cọc, approval window, hash, cache, pipeline, điều phối, phân loại lỗi) chạy song song v1, kiểm chứng bằng unit test, chưa mở endpoint.

**Architecture:** Mã runtime v2 nằm ở thư mục con `src/TADSS.Runtime.Api/V2/` (namespace `TADSS.Runtime.Api.V2.*`), chỉ dùng SDK v2 và hạ tầng runtime còn tồn tại sau 5.2c; v1 tiếp tục chạy nguyên vẹn. Mỗi thành phần là một đơn vị nhỏ có interface rõ, được kiểm chứng bằng test double (không DB, không Redis thật). `DecisionService` v2 điều phối các thành phần đó; nó chỉ được đăng ký DI và nối vào endpoint ở 5.2c, cùng với `EfDecisionAuditStore` và migration.

**Tech Stack:** .NET 8, ASP.NET Core 8, Polly.Core 8.4.2, StackExchange.Redis 2.8.16, `Microsoft.Extensions.Caching.Memory`, `TimeProvider` (BCL), xUnit 2.5.3, FluentAssertions 6.12.1, Moq 4.20.72, `Microsoft.Extensions.TimeProvider.Testing` 8.x (thêm mới, chỉ project test).

**Spec:** [`PHASE5_DESIGN_SPEC.md`](PHASE5_DESIGN_SPEC.md) Mục 4.2.0 (gói 5.2b, việc hoãn từ 5.1), 4.2.3 (runtime), 5 (UT-11…13), 6, 6.1. Nguồn sở hữu: [`INTEGRATION_SPEC.md`](../docs/contracts/INTEGRATION_SPEC.md) §1.1, §3.3, §3.8, §3.9; [`DECISION_PERSISTENCE_INVARIANTS.md`](../docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md) B.1, C, C.2; [`SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](../docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md) 4.1–4.4; [`RESILIENCE_AND_FAULT_TAXONOMY.md`](../docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md). Đầu vào từ gói trước: mục "Ghi chú cho gói 5.2b" cuối [`PHASE5_PLAN_5.2a_SDK_STRATEGIES_V2.md`](PHASE5_PLAN_5.2a_SDK_STRATEGIES_V2.md).

> **Cách đọc plan này:** plan mô tả **yêu cầu kỹ thuật** — trách nhiệm, interface (chữ ký), quy tắc, bảng test case và tiêu chí nghiệm thu. Plan **không** chứa mã hiện thực; người hiện thực viết mã theo TDD từ bảng test case. Chữ ký trong mục **Interfaces** là ràng buộc (các task sau dựa vào đúng tên/kiểu đó); chi tiết bên trong là tự do miễn thỏa yêu cầu.

## Global Constraints

- Build chuẩn: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror` → **0 warning, 0 error** sau mỗi task.
- **v1 không đổi hành vi:** test v1 (unit) và 32 integration test giữ xanh suốt gói, không sửa. Test v2 của 5.2a chỉ được đổi kỳ vọng ở Task 3 (các ca mapping chuyển từ `DomainInvariantException` sang `ContractValidationException` theo B-06, ghi rõ trong báo cáo). Không sửa controller, service, DTO v1. Ngoại lệ duy nhất có chủ đích: `GlobalExceptionHandler` nhận các thay đổi **bổ sung** ở Task 10 (header `Retry-After`, SqlState `40001`/`40P01`), không đổi mã lỗi v1 đang trả.
- **Runtime v2 không phụ thuộc kiểu v1 của SDK.** Trong `TADSS.Runtime.Api.V2.*` chỉ được dùng `Shared.DecisionContractSDK.V2.*` cùng hai ngoại lệ dùng chung `Shared.DecisionContractSDK.Domain.DomainInvariantException` và `Shared.DecisionContractSDK.Common.ContractValidationException`. Được dùng hạ tầng runtime sẽ tồn tại sau 5.2c: `ResilienceOptions`, `FailureReasonPrefixes`, `TadssCacheOptions`, `StrategyOptions`, `IdempotencyConflictException`, `HttpHeaderNames`. Không dùng `DecisionService` v1, `IDecisionCacheService` v1, `DecisionResiliencePipeline` v1, `StrategyResolver` v1, `TadssDbContext`, MediatR.
- Không thêm endpoint, không đổi route, không tạo/áp migration, không đụng schema `tadss`.
- SDK: **không đổi bề mặt public** (`baseline_public_api.txt` giữ nguyên trong cả gói). Chỉ Task 3 được đổi hành vi bên trong `DecisionRequestMapping`.
- Thời gian trong runtime v2 lấy từ `TimeProvider` (tiêm qua DI, đăng ký `TimeProvider.System`); test dùng `FakeTimeProvider`. Không gọi `DateTime.UtcNow`/`DateTimeOffset.UtcNow`/`Stopwatch` trực tiếp trong mã v2.
- Serialize payload v2 (audit, cache Redis) chỉ bằng `TadssJson.Options`.
- Options: `{X}Options` với `public const string SectionName = "Tadss:{X}"`; kiểm tra bằng `IValidateOptions<T>` + `ValidateOnStart()` (cấu hình sai → app không khởi động).
- Đặt tên/thư mục theo skill `tadss-architecture`; comment và message tiếng Việt; định danh tiếng Anh. Version package chỉ ở `Directory.Packages.props`.
- Test: `tests/TADSS.UnitTests/V2/Runtime/**`, namespace `TADSS.UnitTests.V2.Runtime.*`; tên `{Method}_{Scenario}_{Expected}`; mỗi test có `[Trait("CatalogId", "<mã>")]`; XML summary tiếng Việt trên class test; không `Skip`, không nới assertion, không `#pragma warning disable`, không `!` null-forgiving, không `?.Should()`. Test double viết tay hoặc Moq; **không** EF InMemory. Test có đo thời gian không được phụ thuộc tốc độ máy (dùng `FakeTimeProvider` hoặc strategy chờ vô hạn có tôn trọng token).
- Không đọc/in/sửa `.env`, `appsettings.Development.json`, User Secrets.
- **Commit chỉ khi người dùng cho phép** (rule `git-safety`; hook `git-guard` hỏi ở mỗi lệnh). Mỗi task kết thúc bằng một commit Conventional Commits, subject tiếng Anh ASCII.
- Nếu hành vi .NET/thư viện khác với giả định trong plan: sửa tối thiểu giữ đúng **ý định** của yêu cầu, ghi "sai lệch" vào báo cáo task; không nới test.

## Quyết định thiết kế của gói (người dùng duyệt cùng plan)

| Mã | Quyết định | Lý do |
| :--- | :--- | :--- |
| **B-01** | Runtime v2 ở `src/TADSS.Runtime.Api/V2/`; 5.2c dời về đúng vị trí ở spec 4.2.3 khi xóa v1 | D-07: v1/v2 cùng tồn tại; tách thư mục để 5.2c xóa v1 và đổi namespace một lần |
| **B-02** | `DecisionService` v2 **không** đăng ký DI ở 5.2b | Chưa có `IDecisionAuditStore` hiện thực; đăng ký thiếu phụ thuộc làm app không khởi động ở Development (`ValidateOnBuild`) |
| **B-03** | Một thời điểm `nowUtc` cho mỗi lần đánh giá, lấy một lần từ `TimeProvider` trước khi chọn strategy, dùng cho `DecisionContext.EvaluatedAtUtc`, `evaluatedAt`, approval window và `created_at` | Nhất quán giữa response, audit và tính toán; pipeline ≤ 500 ms nên sai lệch không đáng kể |
| **B-04** | Khi fallback: `strategyName`/`strategyVersion` là của **strategy đã được chọn**; nhận biết fallback qua `executionType = FALLBACK`, `fallbackUsed = true`, `reasonCode` `FALLBACK_*` | Giữ được thông tin strategy nào lỗi cho phân tích, metric và breaker theo strategy (5.4) |
| **B-05** | Khi approval window không khả dụng: `reasons` gồm **một câu tiếng Việt** giải thích (thay cho lý do của strategy), `recommendations = []`. Kết quả gốc được giữ ở cột `strategy_status` (đúng yêu cầu INTEGRATION_SPEC §1.1); `reasonCode` gốc của strategy ghi vào log mức Information | Manager/khách hiểu vì sao bị cọc 100%; cập nhật ví dụ INTEGRATION_SPEC §3.4 ở Task 11 |
| **B-06** | Lỗi request hai tầng đúng spec 4.2.1: mọi ràng buộc **diễn đạt được bằng JSON schema request v2** bị chặn trong `DecisionRequestMapping` → `ContractValidationException` (400 `VALIDATION_ERROR`); `DomainInvariantException` (400 `DOMAIN_INVARIANT_VIOLATION`) chỉ còn cho quy tắc ngoài schema (`endTime > startTime`, thời lượng P-01) và làm lớp phòng thủ | Giải quyết M-5 của review cuối 5.2a: cùng một vi phạm hình thức không được trả hai mã khác nhau tùy nơi bắt |
| **B-07** | Canonical của `request_hash` sắp xếp `items` theo (`startTime` UTC, `endTime` UTC, `resourceId`, `resourceTypeId`, `price`) | Cùng tập sân ở thứ tự khác là cùng một yêu cầu nghiệp vụ; tránh 409 oan khi client đổi thứ tự |
| **B-08** | `optionsFingerprint` của **mọi** strategy gồm cả lịch cao điểm (`Tadss:Calendar`) | Giải quyết M-7: `DecisionContext.Calendar` được đưa cho mọi strategy, đổi lịch phải làm mất hiệu lực cache |
| **B-09** | `IDecisionAuditStore` ở 5.2b chỉ khai báo method mà `DecisionService` cần (`FindPrimaryByIdempotencyKeyAsync`, `InsertExecutionAsync`); `GetDecisionDetailAsync` thêm ở 5.2c, pending/override/outcome ở 5.3 | Interface theo use case (spec 4.2.3), không khai báo trước method chưa có người dùng |
| **B-10** | Bảng map lỗi v2 hiện thực thành `ExceptionClassifier` thuần (test đủ ở UT-08) nhưng **chỉ kích hoạt** trong `GlobalExceptionHandler` ở 5.2c | v1 còn dựa vào `InvalidOperationException → 400`; kích hoạt sớm làm đỏ integration test v1 |

Nếu người dùng không đồng ý mục nào, sửa plan trước khi chạy Task 1.

## Chuẩn bị (người dùng thực hiện, trước Task 1)

1. Commit/merge gói 5.2a theo D-08 (PR vào `develop`, CI xanh).
2. Chọn nhánh làm việc cho 5.2b (tiếp tục `feat/phase-5-contract-v2-test-infra` hoặc nhánh mới từ `develop`). Agent chỉ chạy lệnh git đổi nhánh khi người dùng cho phép.
3. Bật Docker Desktop trước Task 11 (integration test cần Testcontainers).

## Cấu trúc file

| File (gốc `src/TADSS.Runtime.Api/V2/` trừ khi ghi khác) | Trách nhiệm | Task |
| :--- | :--- | :--- |
| `Configuration/ApprovalOptions.cs`, `ValidationOptions.cs`, `CalendarOptions.cs` (+ `PeakWindowOptions`), `Configuration/OptionsValidators.cs` | Cấu hình `Tadss:Approval`, `Tadss:Validation` (P-01), `Tadss:Calendar` và kiểm tra khi khởi động | 1 |
| `Configuration/RuntimeV2ServiceCollectionExtensions.cs` | `AddDecisionRuntimeV2(...)`: nơi duy nhất đăng ký DI cho runtime v2; mỗi task bổ sung nhóm của mình | 1, 5, 6, 7, 8 |
| `Services/Decisions/ItemDurationPolicy.cs` | Chính sách thời lượng item P-01 | 1 |
| `Services/Decisions/DepositCalculator.cs`, `Services/Decisions/ApprovalWindowPolicy.cs` (+ `ApprovalWindowResult`) | Quy đổi cọc; quy tắc approval window | 2 |
| `src/Shared.DecisionContractSDK/V2/DTOs/Mappings/DecisionRequestMapping.cs` | (Sửa) phân tầng lỗi request theo B-06 | 3 |
| `Services/RequestHasher.cs` | `request_hash` v2 (INV-3, C.2) | 4 |
| `Services/Strategies/{RegisteredStrategy,IStrategyRegistry,StrategyRegistry,OptionsFingerprintBuilder,StrategyRegistrations}.cs` | Registry strategy v2, bind tham số strategy, fingerprint | 5 |
| `Infrastructure/Resilience/{IStrategyPipeline,StrategyPipeline,StrategyExecutionOutcome,FallbackDecisionPolicy,FallbackCause}.cs`, `Infrastructure/Resilience/ResilienceOptionsValidator.cs` | Pipeline Polly cho strategy v2, fallback v2, kiểm tra ngân sách thời gian | 6 |
| `Infrastructure/Caching/{IStrategyDecisionCache,StrategyDecisionCache,StrategyDecisionCacheKey,CachedStrategyDecision}.cs` | Cache `StrategyDecision` hai tầng | 7 |
| `Infrastructure/Data/{IDecisionAuditStore,DecisionExecutionRecord,ExecutionRole,InsertExecutionResult}.cs`, `Middlewares/PersistenceUnavailableException.cs`, `Services/Decisions/DecisionResponseComposer.cs` (+ `DecisionComposition`, `ComposedDecision`) | Hợp đồng lưu audit; ghép response + bản ghi audit | 8 |
| `Services/{IDecisionService,DecisionService}.cs` | Điều phối luồng `EvaluateAsync` | 9 |
| `Middlewares/{ExceptionClassifier,ErrorClassification}.cs`; `src/TADSS.Runtime.Api/Middlewares/PersistenceFailureDetector.cs` (ngoài V2, dùng chung); `src/TADSS.Runtime.Api/Middlewares/GlobalExceptionHandler.cs` (sửa bổ sung) | Map lỗi v2; phát hiện lỗi persistence có thể thử lại | 10 |
| `src/TADSS.Runtime.Api/Configuration/DependencyInjection.cs`, `Program.cs`, `appsettings.json` | Gọi `AddDecisionRuntimeV2`; validate sớm registry v2; khai báo cấu hình mặc định | 1, 5 |
| `Directory.Packages.props`, `tests/TADSS.UnitTests/TADSS.UnitTests.csproj` | Thêm `Microsoft.Extensions.TimeProvider.Testing` | 6 |
| `tests/TADSS.UnitTests/V2/Runtime/**` | Test của từng task (+ `TestDoubles/` dùng chung: fake audit store, fake cache, strategy giả) | 1–10 |
| `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`, `docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md`, `docs/contracts/INTEGRATION_SPEC.md`, `docs/planning/BACKLOG_SPRINTS.md`, `../.claude/skills/tadss-architecture/SKILL.md` | Đồng bộ tài liệu | 11 |

Thứ tự phụ thuộc: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 (Task 2, 3, 4 độc lập nhau nhưng chạy tuần tự để tránh xung đột).

---

### Task 1: Nền runtime v2 — cấu hình, lịch cao điểm, P-01, test kiến trúc

**Files:**
- Create: `V2/Configuration/ApprovalOptions.cs`, `ValidationOptions.cs`, `CalendarOptions.cs`, `OptionsValidators.cs`, `RuntimeV2ServiceCollectionExtensions.cs`; `V2/Services/Decisions/ItemDurationPolicy.cs`
- Modify: `src/TADSS.Runtime.Api/Configuration/DependencyInjection.cs` (thêm nhóm "7. Runtime v2"), `src/TADSS.Runtime.Api/appsettings.json`
- Test: `tests/TADSS.UnitTests/V2/Runtime/Configuration/RuntimeOptionsTests.cs`, `.../Services/ItemDurationPolicyTests.cs`, `.../Architecture/RuntimeV2LayeringTests.cs`

**Interfaces:**
- Consumes: SDK v2 `BranchCalendar(IEnumerable<PeakWindow>)`, `PeakWindow(DayOfWeek, TimeOnly, TimeOnly)`, `Reservation.Items`, `ReservationItem.Start/End`, `DomainInvariantException(string invariantCode, string message, string? fieldName)`.
- Produces:
  - `ApprovalOptions` — `SectionName = "Tadss:Approval"`; `int WindowMinutes = 30`; `int MinLeadMinutes = 120`.
  - `ValidationOptions` — `SectionName = "Tadss:Validation"`; `int MinItemMinutes = 30`; `int MaxItemMinutes = 720`.
  - `CalendarOptions` — `SectionName = "Tadss:Calendar"`; `List<PeakWindowOptions> PeakWindows` (mặc định rỗng); `static IReadOnlyList<PeakWindowOptions> DefaultPeakWindows`; `BranchCalendar ToCalendar()`. `PeakWindowOptions` — `DayOfWeek Day`, `string Start`, `string End` (định dạng `HH:mm`).
  - `ItemDurationPolicy(IOptions<ValidationOptions> options)` — `void EnsureWithinLimits(Reservation reservation)`.
  - `RuntimeV2ServiceCollectionExtensions.AddDecisionRuntimeV2(this IServiceCollection services, IConfiguration configuration)` — đăng ký `TimeProvider.System` (`TryAddSingleton`), ba options + validator + `ValidateOnStart`, `BranchCalendar` singleton (từ `CalendarOptions.ToCalendar()`), `ItemDurationPolicy` singleton.

**Yêu cầu:**
1. Validator (fail-fast khi khởi động):
   - Approval: `WindowMinutes > 0`, `MinLeadMinutes ≥ 0`.
   - Validation (P-01): `0 < MinItemMinutes ≤ MaxItemMinutes`.
   - Calendar: mỗi cửa sổ có `Start`/`End` parse được theo `HH:mm` (invariant culture), `Start < End`; lỗi dựng `PeakWindow` (`DomainInvariantException`) phải được chuyển thành lỗi validation có thông điệp chỉ rõ phần tử thứ mấy, không để exception domain thoát ra khi khởi động.
2. `CalendarOptions.ToCalendar()`: danh sách rỗng ⇒ dùng `DefaultPeakWindows` = thứ Hai–thứ Sáu 17:00–21:00, thứ Bảy–Chủ Nhật 08:00–21:00 (SAS 4.3). **Không** khởi tạo sẵn phần tử trong `PeakWindows` vì binder của .NET **nối thêm** phần tử vào list có sẵn thay vì thay thế.
3. `appsettings.json` thêm các section `Tadss:Approval`, `Tadss:Validation`, `Tadss:Calendar:PeakWindows` với giá trị mặc định ở trên (để người vận hành thấy và chỉnh được), kèm comment tiếng Việt như các section hiện có.
4. `ItemDurationPolicy.EnsureWithinLimits`: với mỗi item, thời lượng `End − Start` (tính trên instant, không theo giờ địa phương) phải nằm trong `[MinItemMinutes, MaxItemMinutes]` (bao gồm hai biên). Vi phạm → `DomainInvariantException` với `invariantCode = "INVALID_ITEM_DURATION"`, `fieldName = "items[{index}]"`, message tiếng Việt nêu giới hạn đang áp dụng.
5. `DependencyInjection.AddInfrastructureServices` gọi `AddDecisionRuntimeV2` ở nhóm mới "7. Runtime v2 (song song v1; endpoint ở gói 5.2c)". App v1 vẫn khởi động bình thường với cấu hình mặc định.
6. Test kiến trúc UT-09 `RuntimeV2LayeringTests`: duyệt mọi kiểu trong assembly runtime có namespace bắt đầu `TADSS.Runtime.Api.V2`; với kiểu cơ sở, interface, field, property, tham số/kiểu trả về của method và constructor (đệ quy vào generic argument và element type), không được tham chiếu kiểu thuộc các namespace SDK v1 (`Shared.DecisionContractSDK.{Domain,DTOs,Enums,Interfaces,Common}` không có `.V2`) **trừ** `DomainInvariantException` và `ContractValidationException`; và không tham chiếu các kiểu runtime v1 bị cấm ở Global Constraints. Phải có guard "tìm thấy ít nhất một kiểu V2" để test không pass rỗng.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Bind_DefaultConfiguration_UsesSpecDefaults` | Bind từ `appsettings.json` thật của project | Approval 30/120, Validation 30/720, lịch 7 cửa sổ đúng SAS 4.3 | UT-01 |
| `Bind_PeakWindowsOverridden_ReplacesDefaults` | Cấu hình in-memory chỉ có 1 cửa sổ | Lịch có đúng 1 cửa sổ (không bị nối với mặc định) | UT-05 |
| `Bind_NoCalendarSection_FallsBackToDefaults` | Không có section Calendar | 7 cửa sổ mặc định | UT-05 |
| `Validate_MinGreaterThanMax_Fails` | Min 60, Max 30 | Resolve options ném `OptionsValidationException` nêu P-01 | UT-01 |
| `Validate_MinZero_Fails` | Min 0 | Như trên | UT-01 |
| `Validate_ApprovalWindowZero_Fails` | `WindowMinutes = 0` | `OptionsValidationException` | UT-10 |
| `Validate_PeakWindowBadFormat_Fails` | `Start = "7pm"` | `OptionsValidationException` chỉ rõ phần tử | UT-05 |
| `Validate_PeakWindowStartAfterEnd_Fails` | 21:00–17:00 | `OptionsValidationException`, không phải `DomainInvariantException` | UT-05 |
| `EnsureWithinLimits_Boundaries_Theory` | 29, 30, 720, 721 phút | 29, 721 ném `INVALID_ITEM_DURATION`; 30, 720 hợp lệ | UT-01 |
| `EnsureWithinLimits_SecondItemTooLong_ReportsIndex` | Item thứ 2 vượt | `FieldName = "items[1]"` | UT-01 |
| `EnsureWithinLimits_ItemAcrossDstChange_UsesInstantDuration` | Item 2 giờ qua thời điểm đổi giờ (`Europe/Lisbon`) | Thời lượng tính theo instant | UT-01 |
| `RuntimeV2Types_DoNotReferenceSdkV1OrRuntimeV1` | Quét assembly | Không có vi phạm; có ≥ 1 kiểu V2 | UT-09 |
| `AddDecisionRuntimeV2_DefaultConfiguration_ResolvesRegisteredServices` | `ServiceCollection` + cấu hình mặc định | Resolve được `BranchCalendar`, `ItemDurationPolicy`, `TimeProvider` | UT-09 |

- [ ] **Step 1:** Viết các test trên (test kiến trúc lúc này chỉ có kiểu của Task 1 để quét).
- [ ] **Step 2:** Chạy `dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj -c Release --filter "FullyQualifiedName~V2.Runtime"` → FAIL (thiếu kiểu).
- [ ] **Step 3:** Hiện thực theo Yêu cầu 1–6.
- [ ] **Step 4:** Chạy lại filter → PASS; build `--warnaserror`; chạy toàn bộ unit test → xanh.
- [ ] **Step 5:** Đề xuất commit `feat(runtime-v2): options, peak calendar and item duration policy for runtime v2` và chờ người dùng cho phép.

**Nghiệm thu:** cấu hình sai bất kỳ ở ba section làm app không khởi động; app v1 khởi động với cấu hình mặc định; test kiến trúc khóa ranh giới v1/v2 cho mọi task sau.

---

### Task 2: `DepositCalculator` và `ApprovalWindowPolicy`

**Files:**
- Create: `V2/Services/Decisions/DepositCalculator.cs`, `V2/Services/Decisions/ApprovalWindowPolicy.cs` (+ record `ApprovalWindowResult`)
- Modify: `V2/Configuration/RuntimeV2ServiceCollectionExtensions.cs` (đăng ký singleton)
- Test: `tests/TADSS.UnitTests/V2/Runtime/Services/DepositCalculatorTests.cs`, `.../ApprovalWindowPolicyTests.cs`

**Interfaces:**
- Consumes: `ApprovalOptions` (Task 1); SDK v2 `StrategyDecision`, `DecisionStatus`, `ReasonCodes.ApprovalWindowUnavailable`, `DepositDto`.
- Produces:
  - `DepositCalculator` — `DepositDto Calculate(decimal totalPrice, decimal rate, string currency)`; `static int GetMinorUnits(string currency)`.
  - `ApprovalWindowPolicy(IOptions<ApprovalOptions> options)` — `ApprovalWindowResult Apply(StrategyDecision decision, DateTimeOffset earliestStart, DateTimeOffset nowUtc)`.
  - `ApprovalWindowResult` — `DecisionStatus Status`, `DecisionStatus StrategyStatus`, `decimal? DepositRate`, `string ReasonCode`, `IReadOnlyList<string> Reasons`, `IReadOnlyList<DecisionRecommendation> Recommendations`, `DateTimeOffset? ApprovalExpiresAt`, `bool WindowUnavailable`.

**Yêu cầu:**
1. `DepositCalculator` (spec 4.2.3, INTEGRATION_SPEC §3.3):
   - `amount = round(totalPrice × rate)` với số chữ số thập phân theo tiền tệ: `VND` 0; `EUR`, `USD` 2; tiền tệ khác 2. Làm tròn `MidpointRounding.AwayFromZero`.
   - `rate ∈ [0, 1]`, `totalPrice ≥ 0`, `currency` khớp `^[A-Z]{3}\z`; vi phạm → `ArgumentOutOfRangeException`/`ArgumentException` (lỗi lập trình — đầu vào đã được domain/strategy bảo đảm).
   - Trả `DepositDto { Rate = rate, Amount = amount, Currency = currency }`; `rate` giữ nguyên, không làm tròn.
   - Calculator là nơi duy nhất quy đổi tiền cọc; 5.3 dùng lại cho override (quy đổi `amount → rate` sẽ thêm ở 5.3, không làm ở đây).
2. `ApprovalWindowPolicy.Apply` (INTEGRATION_SPEC §1.1):
   - `APPROVED`/`REJECTED`: giữ nguyên quyết định; `ApprovalExpiresAt = null`; `StrategyStatus = Status`.
   - `NEED_APPROVAL`: `expires = min(nowUtc + WindowMinutes, earliestStart − MinLeadMinutes)`, tính trên instant và trả ở UTC (offset 0).
     - `expires > nowUtc` ⇒ giữ `NEED_APPROVAL`, `ApprovalExpiresAt = expires`, `DepositRate = null`.
     - `expires ≤ nowUtc` ⇒ `Status = APPROVED`, `StrategyStatus = NEED_APPROVAL`, `DepositRate = 1`, `ReasonCode = APPROVAL_WINDOW_UNAVAILABLE`, `Reasons` = một câu tiếng Việt (B-05), `Recommendations = []`, `ApprovalExpiresAt = null`, `WindowUnavailable = true`.
   - Áp dụng như nhau cho `NEED_APPROVAL` của strategy và của fallback.
   - Kết quả luôn thỏa INV-7: `APPROVED ⇔ DepositRate != null`; `NEED_APPROVAL ⇔ ApprovalExpiresAt != null`.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Calculate_SpecSample_Returns114000` | 228000 × 0.5 VND | 114000 | UT-12 |
| `Calculate_VndRounding_Theory` | 100001 × 0.333 → 33300.333; 1001 × 0.5 → 500.5 | 33300; 501 (AwayFromZero) | UT-12 |
| `Calculate_TwoDecimalCurrencies_Theory` | 20.01 × 0.5 USD; EUR tương tự; mã lạ `GBP` | 10.01 (không phải 10.00); `GBP` dùng 2 chữ số | UT-12 |
| `Calculate_RateBoundaries_Theory` | rate 0 và 1 | 0; đúng `totalPrice` đã làm tròn | UT-12 |
| `Calculate_InvalidInputs_Throws` | rate −0.01, 1.01; total < 0; currency `vnd` | Ném lỗi đối số | UT-12 |
| `Apply_Approved_PassesThrough` | APPROVED 0.3 | Không đổi, expires null | UT-10 |
| `Apply_Rejected_PassesThrough` | REJECTED | Không đổi, rate null, expires null | UT-10 |
| `Apply_NeedApprovalFarFuture_UsesWindowMinutes` | earliestStart = now + 3 ngày | expires = now + 30 phút | UT-10 |
| `Apply_NeedApprovalCloseStart_UsesLeadTime` | earliestStart = now + 140 phút | expires = now + 20 phút | UT-10 |
| `Apply_ExpiryEqualsNow_BecomesApprovedFullDeposit` | earliestStart = now + 120 phút | APPROVED, rate 1, `APPROVAL_WINDOW_UNAVAILABLE`, StrategyStatus NEED_APPROVAL (biên `≤`) | UT-10 |
| `Apply_ExpiryOneMinuteAfterNow_StaysNeedApproval` | earliestStart = now + 121 phút | NEED_APPROVAL, expires = now + 1 phút | UT-10 |
| `Apply_StartInPast_BecomesApprovedFullDeposit` | earliestStart < now | APPROVED 100% | UT-10 |
| `Apply_NonUtcInputs_ReturnsUtcExpiry` | earliestStart với offset +07:00 | Expiry offset 0, đúng instant | UT-10 |
| `Apply_CustomOptions_Respected` | Window 10, Lead 60 | Công thức dùng giá trị cấu hình | UT-10 |
| `Apply_AllPaths_SatisfyInv7` | Theory mọi nhánh | INV-7 đúng | UT-10 |

- [ ] **Step 1:** Viết test → **Step 2:** chạy, FAIL → **Step 3:** hiện thực → **Step 4:** PASS, build, toàn bộ unit xanh → **Step 5:** đề xuất commit `feat(runtime-v2): deposit calculator and approval window policy`.

**Nghiệm thu:** mọi ô của quy tắc §1.1 và mọi nhánh làm tròn có test; hai lớp không đọc đồng hồ (thời điểm là tham số).

---

### Task 3: Phân tầng lỗi request trong `DecisionRequestMapping` (B-06)

**Files:**
- Modify: `src/Shared.DecisionContractSDK/V2/DTOs/Mappings/DecisionRequestMapping.cs`
- Test: `tests/TADSS.UnitTests/V2/Contracts/DecisionRequestMappingTests.cs` (bổ sung), `tests/TADSS.UnitTests/V2/Contracts/RequestErrorTierTests.cs` (mới)

**Interfaces:**
- Consumes: `ContractValidationException(string errorCode, string message, string? fieldName)`; tập ca âm của `DecisionSchemaTests` (5.2a) và helper `V2Samples`.
- Produces: không đổi chữ ký — `DecisionRequestMapping.ToDomain(DecisionRequestDto)` giữ nguyên; chỉ đổi loại exception cho vi phạm hình thức.

**Yêu cầu:**
1. Trước khi dựng domain, `ToDomain` kiểm mọi ràng buộc mà `decision-request.schema.json` diễn đạt được và có thể lọt qua deserialize, ném `ContractValidationException` với `errorCode` chi tiết và `fieldName` dạng đường dẫn JSON (`items`, `items[2].price`, `customer.customerId`, `currency`, `demand.occupancyRate`, …):
   - `items`: null, rỗng, > 20 phần tử, phần tử null;
   - `price < 0`, `totalPrice < 0`;
   - `currency` không khớp `^[A-Z]{3}\z`;
   - `customer` null; `isGuest = false` mà `customerId` null; `isGuest = true` mà `customerId` hoặc `memberSince` khác null;
   - các số đếm `history` âm; `demand.occupancyRate ∉ [0, 1]`; `demand.waitlistCount < 0`;
   - `metadata` có value null.
2. Mã `errorCode` dùng lại các mã đã có (`INVALID_ITEMS`, `INVALID_CUSTOMER`, `INVALID_METADATA`, `INVALID_TIME_ZONE`) và thêm mới khi cần, đặt theo cùng kiểu (`INVALID_PRICE`, `INVALID_CURRENCY`, `INVALID_HISTORY`, `INVALID_DEMAND`). Trên wire, cả nhóm này là `code = VALIDATION_ERROR` (bảng ở Task 10, kích hoạt ở 5.2c); `errorCode` là chi tiết, xuất hiện qua `errors[field]`.
3. Bất biến domain (constructor SDK) giữ nguyên làm lớp phòng thủ. Sau thay đổi, `DomainInvariantException` từ `ToDomain` chỉ còn với quy tắc schema **không** diễn đạt được: `endTime ≤ startTime` và định danh bằng `Guid.Empty` (chuỗi `00000000-…` vẫn hợp lệ theo `format: uuid`).
4. Không đổi bề mặt public SDK: `PublicApiSurfaceTests` xanh, `git diff --stat docs/contracts/baseline_public_api.txt` rỗng.
5. Test nhất quán schema ↔ mapping (CT-01, phần request): với **mỗi** ca âm của request schema (dùng chung nguồn dữ liệu với `DecisionSchemaTests`; nếu nguồn đang là dữ liệu nội tuyến thì tách ra `MemberData` dùng chung, không chép), hoặc deserialize ném `JsonException`, hoặc `ToDomain` ném `ContractValidationException` — **không bao giờ** thành công và không bao giờ là `DomainInvariantException`.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `ToDomain_FormatViolations_Theory` | Mỗi vi phạm ở Yêu cầu 1 (một dòng/vi phạm) | `ContractValidationException` đúng `ErrorCode` + `FieldName` | UT-02 |
| `ToDomain_EndNotAfterStart_StillDomainInvariant` | `endTime = startTime` | `DomainInvariantException` | UT-01 |
| `SchemaInvalidRequest_NeverMapsToDomain_Theory` | Mọi ca âm của request schema | JsonException hoặc ContractValidationException | CT-01 |
| `ToDomain_SpecSample_StillMaps` | Mẫu §3.3 | Thành công, giá trị như cũ | UT-02 |

- [ ] Step 1–5 như Task 2; commit đề xuất `fix(sdk-v2): report schema-level request violations as contract validation errors`.

**Nghiệm thu:** cùng một vi phạm hình thức luôn ra `VALIDATION_ERROR` dù bị bắt ở schema (BE) hay ở TADSS; baseline không đổi; test 5.2a cũ xanh (ca cũ đổi kỳ vọng từ domain sang contract thì ghi rõ trong báo cáo, kèm lý do B-06).

---

### Task 4: `RequestHasher` v2

**Files:**
- Create: `V2/Services/RequestHasher.cs`
- Test: `tests/TADSS.UnitTests/V2/Runtime/Services/RequestHasherTests.cs`

**Interfaces:**
- Consumes: SDK v2 `DecisionRequestDto` và các DTO con.
- Produces: `static class RequestHasher` — `string ComputeHash(DecisionRequestDto request)` (64 ký tự hex thường).

**Yêu cầu** (DECISION_PERSISTENCE_INVARIANTS C.2, INV-3; B-07):
1. **Danh sách trường khai báo tường minh trong mã**, theo thứ tự cố định: `reservationId`, `branchId`, `branchTimeZone`, `items[]` (`resourceId`, `resourceTypeId`, `startTime`, `endTime`, `price`), `customer` (`isGuest`, `customerId`, `tier`, `memberSince`, `history{completedCount, noShowCount, cancelledCount, lateCancelledCount, paymentExpiredCount, lastCompletedAt}`), `totalPrice`, `currency`, `channel`.
2. **Không** đưa vào hash: `requestedAt`, `metadata`, `demand`.
3. Chuẩn hóa giá trị:
   - thời gian → UTC từ giá trị có offset, định dạng `yyyy-MM-ddTHH:mm:ssZ` (bỏ phần lẻ giây như C.2);
   - GUID → định dạng `D` chữ thường; null → JSON `null` (giữ khóa, không bỏ trường);
   - enum → đúng tên wire (`ONLINE`, `GOLD`); chuỗi phân loại (`branchTimeZone`, `currency`) giữ nguyên vì đã được kiểm chặt;
   - số tiền và số đếm → chuỗi bất biến theo culture, **bỏ số 0 vô nghĩa ở phần thập phân** (`228000`, `228000.0`, `228000.00` cho cùng chuỗi);
   - `items` sắp xếp theo B-07 trước khi ghi.
4. Ghi canonical bằng `Utf8JsonWriter` không thụt lề theo đúng thứ tự trên (không serialize object ẩn danh/POCO bằng reflection); SHA-256 trên UTF-8 → hex thường.
5. Hasher là hàm thuần, không phụ thuộc `CultureInfo.CurrentCulture`, `TimeZoneInfo.Local`.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `ComputeHash_SpecSample_MatchesPinnedValue` | Mẫu §3.3 | Bằng giá trị hex đã ghim trong test (tính một lần khi hiện thực, ghi vào báo cáo task; đổi giá trị này về sau = thay đổi INV-3 có chủ đích) | UT-06 |
| `ComputeHash_SameInstantDifferentOffsets_SameHash` | `18:00+07:00` vs `11:00Z` | Bằng nhau | UT-06 |
| `ComputeHash_DifferentCultures_SameHash` | Chạy dưới `vi-VN`, `fr-FR`, `ar-SA`, `InvariantCulture` (đặt và khôi phục `CurrentCulture`/`CurrentUICulture` trong test) | Bằng nhau | UT-06 |
| `ComputeHash_DecimalScale_SameHash` | `228000` vs `228000.00` | Bằng nhau | UT-06 |
| `ComputeHash_ItemOrder_SameHash` | 2 item đảo thứ tự | Bằng nhau | UT-06 |
| `ComputeHash_ExcludedFields_SameHash_Theory` | Đổi `requestedAt`, `metadata`, `demand` | Bằng nhau | UT-06 |
| `ComputeHash_BusinessFieldChanged_DifferentHash_Theory` | Đổi từng trường ở Yêu cầu 1 (một dòng/trường, gồm từng số đếm history, `memberSince`, `channel`) | Khác nhau | UT-06 |
| `ComputeHash_NullVersusZeroHistory_DifferentHash` | `history = null` vs history toàn 0 | Khác nhau | UT-06 |
| `ComputeHash_Output_Is64LowercaseHex` | Bất kỳ | Khớp `^[0-9a-f]{64}$` | UT-06 |

- [ ] Step 1–5; commit đề xuất `feat(runtime-v2): deterministic request hash v2`.

**Nghiệm thu:** INV-3 có test khóa; danh sách trường trong mã khớp C.2; giá trị ghim được dán vào báo cáo.

---

### Task 5: Registry strategy v2, tham số strategy và `optionsFingerprint`

**Files:**
- Create: `V2/Services/Strategies/RegisteredStrategy.cs`, `IStrategyRegistry.cs`, `StrategyRegistry.cs`, `OptionsFingerprintBuilder.cs`, `StrategyRegistrations.cs`
- Modify: `V2/Configuration/RuntimeV2ServiceCollectionExtensions.cs`, `src/TADSS.Runtime.Api/Program.cs` (validate sớm registry v2 cạnh dòng validate `IStrategyResolver` v1), `src/TADSS.Runtime.Api/appsettings.json` (section `Tadss:Strategies`)
- Test: `tests/TADSS.UnitTests/V2/Runtime/Services/StrategyRegistryTests.cs`, `.../OptionsFingerprintTests.cs`, `.../StrategyRegistrationsTests.cs`

**Interfaces:**
- Consumes: `StrategyOptions.ActiveStrategyName` (`Tadss:Strategy`, lớp runtime hiện có); `BranchCalendar` (Task 1); strategy v2 `TADSS.Strategies.Default.V2.DefaultDecisionStrategy(DefaultStrategyOptions)`, `TADSS.Strategies.MockAlternative.V2.MockAlternativeDecisionStrategy(MockAlternativeStrategyOptions)` và `Validate()` của hai lớp options.
- Produces:
  - `sealed record RegisteredStrategy(IReservationDecisionStrategy Strategy, string OptionsFingerprint)`.
  - `IStrategyRegistry` — `RegisteredStrategy ResolveActive()`; `RegisteredStrategy ResolveByName(string strategyName)`; `IReadOnlyList<RegisteredStrategy> All { get; }`.
  - `StrategyRegistry(IEnumerable<RegisteredStrategy> strategies, IOptions<StrategyOptions> options, ILogger<StrategyRegistry> logger)`.
  - `OptionsFingerprintBuilder` — `Add(string name, decimal value)`, `Add(string name, int value)`, `AddCalendar(BranchCalendar calendar)`, `string Build()` (16 ký tự hex thường).
  - `static class StrategyRegistrations` — `RegisteredStrategy CreateDefault(IConfiguration configuration, BranchCalendar calendar)`, `RegisteredStrategy CreateMockAlternative(IConfiguration configuration, BranchCalendar calendar)`.

**Yêu cầu:**
1. **Registry** (UT-04 phần resolver): tra theo tên không phân biệt hoa thường; trùng `StrategyName` → `InvalidOperationException` khi khởi tạo; `ActiveStrategyName` rỗng hoặc không có trong registry → `InvalidOperationException` liệt kê tên hợp lệ; `ResolveByName` tên lạ → `KeyNotFoundException`. Registry dùng chung section `Tadss:Strategy:ActiveStrategyName` với v1 (tên strategy v1/v2 trùng nhau). Router theo request thuộc 5.4.
2. **Tham số strategy** (spec 4.2.2): bind `Tadss:Strategies:Default` → `DefaultStrategyOptions`, `Tadss:Strategies:MockAlternative` → `MockAlternativeStrategyOptions`; thiếu section ⇒ giá trị mặc định của POCO; gọi `Validate()` của options khi tạo — lỗi → app không khởi động (exception có tên section). Thuộc tính `init` phải bind được; nếu binder của .NET 8 không bind được `init`, ghi sai lệch và chọn cách bind tường minh trong `StrategyRegistrations`, **không** đổi SDK/Strategies.
3. **Fingerprint** (spec 4.2.3, UT-13; B-08): mỗi hàm `Create*` khai báo tường minh danh sách tham số theo thứ tự cố định — Default: `HighValueThreshold`, `NoShowReviewThreshold`, `StandardDepositRate`, `SilverDepositRate`, `GoldDepositRate`, `PremiumDepositRate`, `CacheTtlMinutes`; MockAlternative: `HighValueThreshold`, `PeakDepositRate`, `OffPeakDepositRate`, `CacheTtlMinutes`; sau đó `AddCalendar` (cửa sổ sắp theo ngày, giờ bắt đầu, giờ kết thúc). Builder ghi `name=value` (số bất biến culture, bỏ số 0 vô nghĩa như Task 4), nối bằng `\n`, SHA-256, lấy 16 ký tự hex đầu. Không serialize POCO bằng reflection.
4. DI: `AddDecisionRuntimeV2` đăng ký hai `RegisteredStrategy` (singleton, dựng từ `IConfiguration` + `BranchCalendar`) và `IStrategyRegistry` singleton. `Program.cs` resolve `IStrategyRegistry` ngay sau khi build app (fail-fast như v1).
5. `appsettings.json` thêm `Tadss:Strategies:Default` và `Tadss:Strategies:MockAlternative` với giá trị mặc định của 5.2a (ghi comment "đổi tham số cần restart").

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `ResolveActive_Configured_ReturnsThatStrategy_Theory` | Default / MockAlternative / tên viết thường | Đúng strategy v2, version `2.0.0` | UT-04 |
| `Constructor_DuplicateName_Throws` | Hai đăng ký cùng tên | `InvalidOperationException` | UT-04 |
| `Constructor_UnknownActive_ThrowsListingAvailable` | Active = `Foo` | Message chứa tên hợp lệ | UT-04 |
| `ResolveByName_Unknown_ThrowsKeyNotFound` | | `KeyNotFoundException` | UT-04 |
| `CreateDefault_ConfiguredValues_AreBound` | Cấu hình `HighValueThreshold = 3000000` | Strategy dùng ngưỡng mới (kiểm qua quyết định với `totalPrice = 2500000`) | UT-04 |
| `CreateDefault_InvalidOptions_Throws` | `GoldDepositRate = 1.5` | Exception nêu section | UT-04 |
| `Fingerprint_SameOptionsDifferentCultures_Same` | Dựng dưới nhiều culture | Bằng nhau | UT-13 |
| `Fingerprint_EachParameterChanged_Differs_Theory` | Đổi từng tham số của hai strategy | Khác nhau | UT-13 |
| `Fingerprint_CalendarChanged_Differs` | Đổi một cửa sổ | Khác nhau (cả hai strategy) | UT-13 |
| `Fingerprint_CalendarOrder_Irrelevant` | Cùng cửa sổ, khai báo khác thứ tự | Bằng nhau | UT-13 |
| `Fingerprint_Format_Is16LowercaseHex` | | `^[0-9a-f]{16}$` | UT-13 |

- [ ] Step 1–5; commit đề xuất `feat(runtime-v2): strategy registry with bound options and options fingerprint`.

**Nghiệm thu:** app khởi động fail-fast với cấu hình strategy sai; fingerprint tất định và đổi khi tham số hoặc lịch đổi.

---

### Task 6: Pipeline Polly cho strategy v2 và fallback v2

**Files:**
- Create: `V2/Infrastructure/Resilience/IStrategyPipeline.cs`, `StrategyPipeline.cs`, `StrategyExecutionOutcome.cs`, `FallbackDecisionPolicy.cs`, `FallbackCause.cs`, `ResilienceOptionsValidator.cs`
- Modify: `V2/Configuration/RuntimeV2ServiceCollectionExtensions.cs`; `Directory.Packages.props` + `tests/TADSS.UnitTests/TADSS.UnitTests.csproj` (thêm `Microsoft.Extensions.TimeProvider.Testing`, bản 8.x mới nhất tương thích .NET 8, chốt khi restore)
- Test: `tests/TADSS.UnitTests/V2/Runtime/Resilience/StrategyPipelineTests.cs`, `.../ResilienceOptionsValidatorTests.cs`, `.../FallbackDecisionPolicyTests.cs`

**Interfaces:**
- Consumes: `ResilienceOptions` (`Tadss:Resilience`, lớp runtime hiện có), `FailureReasonPrefixes`; SDK v2 `IReservationDecisionStrategy`, `StrategyDecision`, `DecisionContext`, `ReasonCodes.Fallback*`, `RecommendationType.REQUIRE_APPROVAL`, `ExecutionType`.
- Produces:
  - `sealed record StrategyExecutionOutcome(StrategyDecision Decision, ExecutionType ExecutionType, bool FallbackUsed, string? FailureReason, int ExecutionTimeMs)`.
  - `IStrategyPipeline` — `Task<StrategyExecutionOutcome> ExecuteAsync(IReservationDecisionStrategy strategy, Reservation reservation, DecisionContext context, CancellationToken cancellationToken)`.
  - `StrategyPipeline(IOptions<ResilienceOptions> options, TimeProvider timeProvider, ILogger<StrategyPipeline> logger)`.
  - `enum FallbackCause { Timeout, Exception, CircuitOpen }`; `static class FallbackDecisionPolicy` — `StrategyDecision Create(FallbackCause cause)`.
  - `ResilienceOptionsValidator : IValidateOptions<ResilienceOptions>`.

**Yêu cầu:**
1. Thứ tự lớp giữ như v1 (SAS 4.2: "giữ nguyên cấu hình Polly v8"): Outer timeout → Fallback → Retry → Circuit breaker → Inner timeout; tham số lấy từ `ResilienceOptions`. Gán `TimeProvider` của DI cho pipeline builder.
2. Thành công ⇒ `AUTOMATED`, `FallbackUsed = false`, `FailureReason = null`.
3. Fallback (RESILIENCE_AND_FAULT_TAXONOMY F2–F4), quyết định do `FallbackDecisionPolicy` tạo — luôn `RequireApproval`, không I/O, không ném lỗi:

   | Nguyên nhân | `reasonCode` | `FailureReason` |
   | :--- | :--- | :--- |
   | Inner timeout sau hết lượt retry | `FALLBACK_STRATEGY_TIMEOUT` | `FailureReasonPrefixes.FormatTimeout(InnerTimeoutMs)` |
   | Strategy ném lỗi (kể cả trả `null`, kể cả `DomainInvariantException` từ factory `StrategyDecision`) | `FALLBACK_STRATEGY_EXCEPTION` | `FailureReasonPrefixes.FormatException(...)` — chỉ tên kiểu exception, không chép message (tránh lộ chi tiết vào audit/response) |
   | Circuit đang mở | `FALLBACK_CIRCUIT_OPEN` | `FailureReasonPrefixes.FormatCircuitBreakerOpen()` |

   `reasons` tiếng Việt (giữ tinh thần thông điệp fallback v1 nhưng **không** nhắc "100%" — cọc 100% là chính sách của SmashCourt, không phải TADSS); `recommendations` = một `REQUIRE_APPROVAL`. Outcome `FALLBACK`, `FallbackUsed = true`.
4. `OperationCanceledException` do **token của caller** → lan ra ngoài (không fallback). Outer timeout → `TimeoutRejectedException` lan ra (504 ở Task 10).
5. `ExecutionTimeMs` = thời gian toàn pipeline đo bằng `TimeProvider.GetTimestamp()`/`GetElapsedTime`, làm tròn xuống, ≥ 0.
6. Pipeline là singleton dùng chung cho mọi strategy (một circuit breaker — V-2 sửa ở 5.4; ghi chú trong XML doc). Pipeline v2 là instance riêng, không chia trạng thái breaker với v1.
7. `ResilienceOptionsValidator` (UT-07): mọi giá trị > 0 (`MaxRetryAttempts ≥ 0`), `0 < CircuitBreakerFailureRatio ≤ 1`, và **ngân sách** `InnerTimeoutMs × (MaxRetryAttempts + 1) + RetryBaseDelayMs × MaxRetryAttempts ≤ OuterTimeoutMs`. Đăng ký kèm `ValidateOnStart` trong `AddDecisionRuntimeV2` — áp dụng cho cả v1 vì dùng chung section; mặc định (420 ≤ 500) hợp lệ. Kiểm tra cấu hình của integration test/compose không vi phạm (ghi kết quả vào báo cáo).

**Test cases** (timeout dùng strategy giả chờ `Task.Delay(Timeout.Infinite, token)` với `InnerTimeoutMs` nhỏ — kết quả không phụ thuộc tốc độ máy):

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `ExecuteAsync_StrategySucceeds_Automated` | Strategy trả APPROVED 0.5 | `AUTOMATED`, quyết định nguyên vẹn | UT-07 |
| `ExecuteAsync_PassesContextToStrategy` | Strategy ghi lại tham số | Nhận đúng `Reservation`, `DecisionContext` | UT-07 |
| `ExecuteAsync_FailsOnceThenSucceeds_RetriedAutomated` | Lần 1 ném, lần 2 trả | `AUTOMATED`, strategy gọi 2 lần | UT-07 |
| `ExecuteAsync_AlwaysThrows_FallbackException` | Luôn ném | `FALLBACK`, `FALLBACK_STRATEGY_EXCEPTION`, số lần gọi = `MaxRetryAttempts + 1`, `FailureReason` bắt đầu `STRATEGY_EXCEPTION` và không chứa message gốc | UT-07 |
| `ExecuteAsync_StrategyReturnsNull_FallbackException` | Trả null | Như trên | UT-07 |
| `ExecuteAsync_AlwaysSlow_FallbackTimeout` | Chờ vô hạn có tôn trọng token | `FALLBACK_STRATEGY_TIMEOUT`, `FailureReason` bắt đầu `STRATEGY_TIMEOUT` | UT-07 |
| `ExecuteAsync_CircuitOpen_FallbackWithoutInvokingStrategy` | Lỗi đủ ngưỡng (`MinimumThroughput` nhỏ) rồi gọi tiếp | `FALLBACK_CIRCUIT_OPEN`; lần gọi sau không chạm strategy | UT-07 |
| `ExecuteAsync_CallerCancelled_Throws` | Token caller hủy | `OperationCanceledException`, không fallback | UT-07 |
| `ExecuteAsync_MeasuresElapsedWithTimeProvider` | `FakeTimeProvider` tiến 7 ms trong strategy | `ExecutionTimeMs = 7` | UT-07 |
| `Create_EachCause_RequireApprovalWithMatchingCode_Theory` | 3 nguyên nhân | NEED_APPROVAL, đúng reasonCode, 1 recommendation `REQUIRE_APPROVAL`, reasons không chứa "100%" | UT-07 |
| `Validate_DefaultOptions_Succeeds` | Mặc định | Hợp lệ | UT-07 |
| `Validate_BudgetExceedsOuter_Fails` | Inner 200, retry 2, delay 30, outer 500 | Lỗi nêu công thức ngân sách | UT-07 |
| `Validate_NonPositiveValues_Fail_Theory` | Từng tham số ≤ 0 / ratio ngoài (0, 1] | Lỗi | UT-07 |

- [ ] Step 1–5; commit đề xuất `feat(runtime-v2): resilience pipeline and fallback policy for v2 strategies`.

**Nghiệm thu:** F2–F4 cho strategy v2 có test; ngân sách thời gian được kiểm khi khởi động.

---

### Task 7: Cache v2 — chỉ cache `StrategyDecision`

**Files:**
- Create: `V2/Infrastructure/Caching/IStrategyDecisionCache.cs`, `StrategyDecisionCache.cs`, `StrategyDecisionCacheKey.cs`, `CachedStrategyDecision.cs` (internal)
- Modify: `V2/Configuration/RuntimeV2ServiceCollectionExtensions.cs`
- Test: `tests/TADSS.UnitTests/V2/Runtime/Caching/StrategyDecisionCacheTests.cs`, `.../StrategyDecisionCacheKeyTests.cs`

**Interfaces:**
- Consumes: `TadssCacheOptions` (`Provider`, `EnableFallbackToMemory`), `IMemoryCache`, `IConnectionMultiplexer` (đã đăng ký ở v1, `AbortOnConnectFail = false`), `TadssJson.Options`; SDK v2 `StrategyDecision` và các factory.
- Produces:
  - `IStrategyDecisionCache` — `Task<StrategyDecision?> GetAsync(string key, CancellationToken cancellationToken)`; `Task SetAsync(string key, StrategyDecision decision, TimeSpan ttl, CancellationToken cancellationToken)`.
  - `static class StrategyDecisionCacheKey` — `string Build(string strategyName, string strategyVersion, string optionsFingerprint, string requestHash)`.
  - `StrategyDecisionCache(IMemoryCache memoryCache, IOptions<TadssCacheOptions> options, ILogger<StrategyDecisionCache> logger, IConnectionMultiplexer? redis = null)`.

**Yêu cầu:**
1. Key đúng spec 4.2.3: `tadss:v2:{strategyName}:{strategyVersion}:{optionsFingerprint}:{requestHash}`; mọi thành phần không rỗng; không cộng thêm `InstanceName` (đã nằm trong tiền tố `tadss:`).
2. Giá trị cache là `StrategyDecision`, **không** phải response (V-3). Tầng memory lưu chính instance (bất biến sau F6 của 5.2a); tầng Redis lưu JSON của `CachedStrategyDecision` (`Status`, `DepositRate`, `ReasonCode`, `Reasons`, `Recommendations{Type, Description}`) serialize bằng `TadssJson.Options`. Đọc từ Redis dựng lại qua đúng factory theo `Status` (invariant được kiểm lại).
3. Thứ tự đọc: Redis (nếu `Provider = Redis` và có multiplexer) → memory (nếu `EnableFallbackToMemory` hoặc không dùng Redis). Ghi: Redis và memory theo cùng quy tắc như cache v1. TTL lấy từ tham số (do `StrategyCachePolicy.Ttl` quyết định ở Task 9), không dùng `TtlHours`.
4. Cô lập miền lỗi (nguyên tắc 2 của RESILIENCE_AND_FAULT_TAXONOMY): mọi lỗi Redis/memory/deserialize/payload hỏng (JSON sai, status lạ, vi phạm invariant khi dựng lại) ⇒ coi như miss (đọc) hoặc bỏ qua (ghi), log Warning kèm key, **không** ném ra ngoài; riêng `OperationCanceledException` của caller được lan ra.
5. Breaker riêng cho Redis và metric `bypass` thuộc 5.4 — không làm ở đây.
6. Tầng Redis được test bằng Moq trên `IConnectionMultiplexer`/`IDatabase` (không Redis thật; Redis thật thuộc IT-13 ở 5.2c).

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Build_AllParts_FollowsSpecFormat` | | `tadss:v2:Default…:2.0.0:{fp}:{hash}` | UT-13 |
| `Build_EmptyPart_Throws_Theory` | Từng phần rỗng | `ArgumentException` | UT-13 |
| `SetThenGet_MemoryProvider_RoundTripsEachStatus_Theory` | APPROVED/NEED_APPROVAL/REJECTED có reasons + recommendations | Dữ liệu bằng nhau | UT-13 |
| `SetThenGet_RedisProvider_RoundTripsViaJson` | Moq lưu chuỗi đã ghi rồi trả lại | Dữ liệu bằng nhau; chuỗi ghi là JSON theo `TadssJson` | UT-13 |
| `Set_UsesGivenTtl` | TTL 60 phút | Redis `StringSetAsync` nhận expiry 60 phút | UT-13 |
| `Get_RedisThrows_FallsBackToMemory` | Redis ném `RedisConnectionException` | Lấy được từ memory, không ném | UT-13 |
| `Get_RedisThrowsAndNoMemoryFallback_ReturnsNull` | `EnableFallbackToMemory = false` | `null`, không ném | UT-13 |
| `Get_CorruptPayload_Theory` | JSON hỏng; status lạ; rate 1.5 | `null`, không ném | UT-13 |
| `Set_RedisThrows_DoesNotThrow` | | Không ném; memory vẫn có | UT-13 |
| `Get_CallerCancelled_Throws` | Token hủy | `OperationCanceledException` | UT-13 |
| `Get_MissingKey_ReturnsNull` | | `null` | UT-13 |

- [ ] Step 1–5; commit đề xuất `feat(runtime-v2): strategy decision cache (memory and redis tiers)`.

**Nghiệm thu:** chỉ `StrategyDecision` được cache; lỗi cache không bao giờ thoát ra.

---

### Task 8: Hợp đồng audit và `DecisionResponseComposer`

**Files:**
- Create: `V2/Infrastructure/Data/IDecisionAuditStore.cs`, `DecisionExecutionRecord.cs`, `ExecutionRole.cs`, `InsertExecutionResult.cs`; `V2/Middlewares/PersistenceUnavailableException.cs`; `V2/Services/Decisions/DecisionResponseComposer.cs` (+ `DecisionComposition`, `ComposedDecision`)
- Modify: `V2/Configuration/RuntimeV2ServiceCollectionExtensions.cs` (composer singleton; **không** đăng ký store)
- Test: `tests/TADSS.UnitTests/V2/Runtime/Services/DecisionResponseComposerTests.cs`; `tests/TADSS.UnitTests/V2/Runtime/TestDoubles/FakeDecisionAuditStore.cs` (dùng cho Task 9)

**Interfaces:**
- Consumes: `DepositCalculator`, `ApprovalWindowPolicy`, `ApprovalWindowResult` (Task 2); `StrategyExecutionOutcome` (Task 6); `RegisteredStrategy` (Task 5); SDK v2 `DecisionRequestDto`, `DecisionResultDto`, `DepositDto`, `DecisionRecommendationDto`, `Reservation`, `TadssJson.Options`.
- Produces:
  - `enum ExecutionRole { PRIMARY, SHADOW }`.
  - `sealed record DecisionExecutionRecord` — đúng các cột của DECISION_PERSISTENCE_INVARIANTS B.1 trừ `id`: `Guid DecisionId`, `Guid ReservationId`, `Guid BranchId`, `ExecutionRole Role`, `string StrategyName`, `string StrategyVersion`, `DecisionStatus StrategyStatus`, `DecisionStatus Status`, `decimal? DepositRate`, `decimal? DepositAmount`, `string ReasonCode`, `ExecutionType ExecutionType`, `int ExecutionTimeMs`, `bool FallbackUsed`, `string? FailureReason`, `bool CacheHit`, `DateTimeOffset? ApprovalExpiresAt`, `string RequestHash`, `string RequestPayload`, `string ResponsePayload`, `string? IdempotencyKey`, `string? CorrelationId`, `DateTimeOffset CreatedAt`.
  - `sealed record InsertExecutionResult(DecisionExecutionRecord Stored, bool IsExistingWinner)`.
  - `IDecisionAuditStore` (B-09) — `Task<DecisionExecutionRecord?> FindPrimaryByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)`; `Task<InsertExecutionResult> InsertExecutionAsync(DecisionExecutionRecord record, CancellationToken cancellationToken)`.
  - `PersistenceUnavailableException(string message, Exception innerException)`.
  - `sealed record DecisionComposition(Guid DecisionId, DecisionRequestDto Request, Reservation Reservation, RegisteredStrategy Strategy, StrategyExecutionOutcome Outcome, bool CacheHit, DateTimeOffset NowUtc, string RequestHash, string IdempotencyKey, string? CorrelationId)`.
  - `sealed record ComposedDecision(DecisionResultDto Response, DecisionExecutionRecord Record)`.
  - `DecisionResponseComposer(DepositCalculator depositCalculator, ApprovalWindowPolicy approvalWindowPolicy)` — `ComposedDecision Compose(DecisionComposition composition)`.

**Yêu cầu:**
1. Hợp đồng store (XML doc phải ghi rõ để 5.2c hiện thực đúng):
   - append-only (INV-4): không có method sửa/xóa;
   - `InsertExecutionAsync` khi gặp vi phạm unique `uq_decision_idempotency_key` (SqlState `23505` **và** đúng tên constraint) trả bản thắng với `IsExistingWinner = true`, không ném;
   - lỗi kết nối/timeout/lỗi có thể thử lại của PostgreSQL ⇒ ném `PersistenceUnavailableException`; vi phạm ràng buộc khác ⇒ để exception gốc lan ra (thành 500, không phải 503).
2. `Compose`:
   - Áp `ApprovalWindowPolicy.Apply(outcome.Decision, reservation.EarliestStart, nowUtc)`.
   - Nếu kết quả `APPROVED`: `deposit = DepositCalculator.Calculate(reservation.TotalPrice, rate, reservation.Currency)`; ngược lại `deposit = null`.
   - `executionType`: `CACHE_HIT` khi `CacheHit` (khi đó `fallbackUsed = false`, `failureReason = null`); ngược lại lấy từ outcome.
   - `strategyName`/`strategyVersion` = của strategy đã chọn (B-04).
   - `evaluatedAt = createdAt = NowUtc` (B-03), offset 0.
   - Response và record cùng dữ liệu nghiệp vụ; `StrategyStatus` lấy từ `ApprovalWindowResult.StrategyStatus`; `Role = PRIMARY`.
   - `RequestPayload` = request DTO serialize bằng `TadssJson.Options` (gồm cả `demand`, `metadata`, `requestedAt`); `ResponsePayload` = response serialize bằng `TadssJson.Options`.
   - INV-7 luôn đúng ở cả response và record.
3. `FakeDecisionAuditStore` (test double, thread-safe): lưu theo idempotency key; cho phép cấu hình (a) ném `PersistenceUnavailableException` khi insert, (b) giả lập thua tranh chấp — trả một bản thắng định sẵn với `IsExistingWinner = true`; ghi lại số lần insert.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Compose_ApprovedAutomated_HasDepositAndNoExpiry` | APPROVED 0.5, 228000 VND | `deposit {0.5, 114000, VND}`, expires null, `AUTOMATED` | UT-10 |
| `Compose_NeedApproval_HasExpiryNoDeposit` | NEED_APPROVAL, start xa | expires = now + 30 phút, deposit null | UT-10 |
| `Compose_NeedApprovalNoWindow_ApprovedFullDeposit` | NEED_APPROVAL, start gần | APPROVED, rate 1, amount 228000, `APPROVAL_WINDOW_UNAVAILABLE`, record.StrategyStatus NEED_APPROVAL | UT-10 |
| `Compose_Rejected_NoDepositNoExpiry` | REJECTED | Cả hai null | UT-10 |
| `Compose_Fallback_KeepsSelectedStrategyIdentity` | Outcome FALLBACK | `strategyName` = strategy đã chọn, `executionType FALLBACK`, `fallbackUsed true`, `failureReason` có trong record | UT-11 |
| `Compose_CacheHit_MarksCacheHit` | `CacheHit = true` | `CACHE_HIT`, `cacheHit true`, `fallbackUsed false` | UT-13 |
| `Compose_Timestamps_UseNowUtc` | `NowUtc` cố định | `evaluatedAt = createdAt = NowUtc` | UT-11 |
| `Compose_Payloads_AreTadssJsonOfInputs` | | `RequestPayload` deserialize lại bằng `TadssJson` ra request tương đương (gồm `demand`); `ResponsePayload` ra response tương đương | UT-11 |
| `Compose_ResponsePayload_ValidAgainstResultSchema_Theory` | Mọi nhánh trên | Hợp lệ theo `decision-result.schema.json` (helper `ContractSchemas` của 5.2a) | CT-01 |
| `Compose_AllPaths_SatisfyInv7_Theory` | Mọi nhánh | INV-7 đúng ở response và record | UT-10 |

- [ ] Step 1–5; commit đề xuất `feat(runtime-v2): audit store contract and decision response composer`.

**Nghiệm thu:** mọi đường ra response v2 hợp lệ theo schema và INV-7; hợp đồng store đủ rõ để 5.2c hiện thực không phải đoán.

---

### Task 9: `DecisionService` v2 — điều phối

**Files:**
- Create: `V2/Services/IDecisionService.cs`, `V2/Services/DecisionService.cs`
- Test: `tests/TADSS.UnitTests/V2/Runtime/Services/DecisionServiceTests.cs`; test double bổ sung trong `tests/TADSS.UnitTests/V2/Runtime/TestDoubles/` (fake pipeline, fake cache, strategy ghi lời gọi)

**Interfaces:**
- Consumes: mọi thành phần Task 1–8: `RequestHasher.ComputeHash`, `IDecisionAuditStore`, `DecisionRequestMapping.ToDomain`, `ItemDurationPolicy`, `IStrategyRegistry`, `BranchCalendar`, `IStrategyDecisionCache`, `StrategyDecisionCacheKey`, `IStrategyPipeline`, `DecisionResponseComposer`, `TimeProvider`, `IdempotencyConflictException(string idempotencyKey, string message)`.
- Produces:
  - `IDecisionService` (namespace `TADSS.Runtime.Api.V2.Services`) — `Task<DecisionResultDto> EvaluateAsync(DecisionRequestDto request, string idempotencyKey, string? correlationId, CancellationToken cancellationToken)`.
  - `DecisionService(IDecisionAuditStore auditStore, IStrategyRegistry strategyRegistry, IStrategyPipeline pipeline, IStrategyDecisionCache cache, ItemDurationPolicy itemDurationPolicy, DecisionResponseComposer composer, BranchCalendar calendar, TimeProvider timeProvider, ILogger<DecisionService> logger)`.
  - **Không** đăng ký DI (B-02); 5.2c đăng ký scoped cùng `EfDecisionAuditStore`.

**Yêu cầu** (spec 4.2.3 "Luồng `DecisionService.EvaluateAsync`", SAS 4.4, INTEGRATION_SPEC §3.9):
1. Thứ tự bắt buộc:
   1. `idempotencyKey` rỗng/khoảng trắng hoặc > 128 ký tự → `ArgumentException` (controller 5.2c chặn trước bằng `MISSING_HEADER`/`VALIDATION_ERROR`; ở đây là lỗi lập trình).
   2. `requestHash = RequestHasher.ComputeHash(request)`.
   3. `FindPrimaryByIdempotencyKeyAsync`: có bản ghi và cùng hash → deserialize `ResponsePayload` bằng `TadssJson.Options` và trả **nguyên văn** (không chạy strategy, không ghi gì); khác hash → `IdempotencyConflictException`.
   4. `reservation = request.ToDomain()`; `itemDurationPolicy.EnsureWithinLimits(reservation)`.
   5. `nowUtc = timeProvider.GetUtcNow()` (B-03); `strategy = strategyRegistry.ResolveActive()`; `context = new DecisionContext(nowUtc, calendar)`.
   6. Nếu `strategy.Strategy.CachePolicy.IsEnabled`: đọc cache theo key `StrategyDecisionCacheKey.Build(name, version, fingerprint, requestHash)`; hit ⇒ outcome `StrategyExecutionOutcome(decision, CACHE_HIT, false, null, elapsedMs)` với thời gian đọc cache đo bằng `TimeProvider`.
   7. Miss/không cache ⇒ `pipeline.ExecuteAsync(...)`; chỉ khi outcome `AUTOMATED` và cache được bật mới `SetAsync` với TTL = `CachePolicy.Ttl`.
   8. `composer.Compose(...)` với `DecisionId = Guid.NewGuid()`.
   9. `InsertExecutionAsync` đồng bộ (fail-closed, INV-10): lỗi lan ra, **không** trả response. Kết quả `IsExistingWinner`: cùng hash → trả response của bản thắng (deserialize `ResponsePayload`); khác hash → `IdempotencyConflictException`.
   10. Trả response.
2. Lỗi trước bước 9 (validation, P-01, conflict) không ghi audit và không gọi strategy.
3. Service tự cô lập lỗi cache: mọi exception từ `IStrategyDecisionCache` trừ `OperationCanceledException` của caller ⇒ đọc coi như miss, ghi bỏ qua, log Warning — không phụ thuộc vào việc hiện thực cache đã nuốt lỗi (nguyên tắc 2, RESILIENCE_AND_FAULT_TAXONOMY).
4. Log có cấu trúc theo style log hiện có: quyết định cuối, strategy, executionType, thời gian, idempotency key, correlation id; khi approval window không khả dụng, log thêm `reasonCode` gốc của strategy (B-05); không log payload đầy đủ.
5. Không dùng MediatR; không gọi `TadssDbContext`.

**Test cases** (dùng `FakeTimeProvider`, `FakeDecisionAuditStore`, fake cache/pipeline ghi lời gọi; strategy thật v2 khi cần quyết định thật):

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `EvaluateAsync_NewRequest_PersistsAndReturnsAutomated` | Key mới, Default, GOLD | Response APPROVED 0.3, 1 bản ghi PRIMARY, record = response | UT-11 |
| `EvaluateAsync_SameKeySameHash_ReplaysStoredResponse` | Gọi 2 lần cùng key/payload | Lần 2 trả đúng response lần 1 (cùng `decisionId`), strategy/pipeline gọi 1 lần, 1 bản ghi | UT-11 |
| `EvaluateAsync_SameKeyOnlyDemandChanged_Replays` | Lần 2 đổi `demand`, `requestedAt`, `metadata` | Replay, không 409 | UT-11 |
| `EvaluateAsync_SameKeyDifferentHash_ThrowsConflict` | Đổi `totalPrice` | `IdempotencyConflictException`, không insert, không gọi strategy | UT-11 |
| `EvaluateAsync_IdempotencyCheckedBeforeStrategyResolution` | Store đã có bản ghi; registry giả ném nếu bị gọi | Replay thành công | UT-11 |
| `EvaluateAsync_ItemTooShort_ThrowsBeforeStrategy` | Item 15 phút | `DomainInvariantException INVALID_ITEM_DURATION`; không insert, không gọi pipeline | UT-11 |
| `EvaluateAsync_FormatViolation_ThrowsContractValidation` | `customer: null` | `ContractValidationException`; không insert | UT-11 |
| `EvaluateAsync_CacheEnabledHit_ReturnsCacheHitWithNewIdentity` | Cache có sẵn quyết định | `CACHE_HIT`; pipeline không gọi; hai lần hit (khác key idempotency) cho hai `decisionId` khác nhau | UT-13 |
| `EvaluateAsync_CacheHit_RecomputesApprovalWindow` | Cache NEED_APPROVAL; `FakeTimeProvider` khác nhau giữa hai lần | `approvalExpiresAt` tính theo `now` của từng lần | UT-11 |
| `EvaluateAsync_CacheMissAutomated_WritesCacheWithPolicyTtl` | Miss | `SetAsync` 1 lần, TTL = `CachePolicy.Ttl` | UT-13 |
| `EvaluateAsync_FallbackOutcome_NotCached` | Pipeline giả trả FALLBACK | Không `SetAsync`; NEED_APPROVAL có expires | UT-13 |
| `EvaluateAsync_CacheDisabledPolicy_NeverTouchesCache` | Strategy giả `CachePolicy.Disabled` | Không `GetAsync`/`SetAsync` | UT-13 |
| `EvaluateAsync_CacheThrows_TreatedAsMiss` | Cache giả ném `InvalidOperationException` khi đọc và khi ghi | Pipeline được gọi, kết quả `AUTOMATED` như miss, không ném ra ngoài | UT-13 |
| `EvaluateAsync_WindowUnavailable_LogsStrategyReasonCode` | Strategy NEED_APPROVAL, start gần | Response `APPROVAL_WINDOW_UNAVAILABLE`; log Information chứa `reasonCode` gốc | UT-11 |
| `EvaluateAsync_StoreUnavailable_ThrowsAndReturnsNothing` | Store ném `PersistenceUnavailableException` | Exception lan ra (INV-10) | UT-11 |
| `EvaluateAsync_LostRaceSameHash_ReturnsWinner` | Store trả `IsExistingWinner` cùng hash | Trả response của bản thắng | UT-11 |
| `EvaluateAsync_LostRaceDifferentHash_ThrowsConflict` | Bản thắng khác hash | `IdempotencyConflictException` | UT-11 |
| `EvaluateAsync_ContextUsesTimeProvider` | `FakeTimeProvider` đặt năm 2030 | Strategy nhận `EvaluatedAtUtc` = 2030; response `evaluatedAt` = 2030 | UT-11 |
| `EvaluateAsync_PersistedRecord_MatchesSpecColumns` | | Role PRIMARY, BranchId, RequestHash, CorrelationId, IdempotencyKey, CreatedAt đúng | UT-11 |
| `EvaluateAsync_InvalidIdempotencyKey_Throws_Theory` | Rỗng, khoảng trắng, 129 ký tự | `ArgumentException` | UT-11 |
| `EvaluateAsync_CallerCancelled_PropagatesWithoutPersisting` | Token hủy trước pipeline | `OperationCanceledException`, không insert | UT-11 |

- [ ] Step 1–5; commit đề xuất `feat(runtime-v2): decision service orchestration v2`.

**Nghiệm thu:** UT-11 khóa đủ thứ tự idempotency → domain → cache → pipeline → approval window → audit, fail-closed và tranh chấp idempotency.

---

### Task 10: Phân loại lỗi v2 và bổ sung cho `GlobalExceptionHandler`

**Files:**
- Create: `V2/Middlewares/ErrorClassification.cs`, `V2/Middlewares/ExceptionClassifier.cs`; `src/TADSS.Runtime.Api/Middlewares/PersistenceFailureDetector.cs`
- Modify: `src/TADSS.Runtime.Api/Middlewares/GlobalExceptionHandler.cs` (bổ sung), `src/TADSS.Runtime.Api/Common/RuntimeErrorCodes.cs` (xóa nếu không còn tham chiếu, thay bằng `V2.Common.ErrorCodes.PersistenceUnavailable` — cùng giá trị)
- Test: `tests/TADSS.UnitTests/V2/Runtime/Middlewares/ExceptionClassifierTests.cs`, `.../GlobalExceptionHandlerAdditionsTests.cs`, `tests/TADSS.UnitTests/V2/Contracts/WireCodeValuesTests.cs`

**Interfaces:**
- Consumes: `PersistenceUnavailableException` (Task 8), `IdempotencyConflictException`, `DomainInvariantException`, `ContractValidationException`, `Polly.Timeout.TimeoutRejectedException`, `Npgsql.NpgsqlException`, `Npgsql.PostgresException`; SDK v2 `ErrorCodes`, `ReasonCodes`.
- Produces:
  - `sealed record ErrorClassification(int StatusCode, string Code, string Title, bool ExposeMessage, int? RetryAfterSeconds)`.
  - `static class ExceptionClassifier` — `ErrorClassification? Classify(Exception exception, bool requestAborted)` (`null` = không ghi response).
  - `static class PersistenceFailureDetector` — `bool IsRetryable(Exception exception)`.

**Yêu cầu:**
1. `PersistenceFailureDetector.IsRetryable`: duyệt toàn chuỗi `InnerException`; đúng khi gặp `PersistenceUnavailableException`, `NpgsqlException { IsTransient: true }`, hoặc `PostgresException` có `SqlState` `40001` (serialization failure) / `40P01` (deadlock) — kiểm **tường minh** theo SqlState, không dựa vào danh sách `IsTransient` của Npgsql. `PostgresException 23505` và vi phạm ràng buộc khác ⇒ `false`.
2. `ExceptionClassifier` hiện thực đúng bảng map lỗi spec 4.2.3 (UT-08):

   | Exception | Status / `code` | `ExposeMessage` | `RetryAfterSeconds` |
   | :--- | :--- | :---: | :---: |
   | `ContractValidationException` | 400 `VALIDATION_ERROR` | có | — |
   | `DomainInvariantException` | 400 `DOMAIN_INVARIANT_VIOLATION` | có | — |
   | `IdempotencyConflictException` | 409 `IDEMPOTENCY_KEY_REUSE` | có | — |
   | `PersistenceFailureDetector.IsRetryable` đúng | 503 `PERSISTENCE_UNAVAILABLE` | không | 5 |
   | `OperationCanceledException` khi `requestAborted` | `null` (không response) | — | — |
   | `TimeoutRejectedException` | 504 `GATEWAY_TIMEOUT` | không | 5 |
   | Còn lại (gồm `InvalidOperationException`, `ArgumentException`, `DbUpdateException` 23505 không phải idempotency, `OperationCanceledException` không do client) | 500 `INTERNAL_ERROR` | không | — |

   Kiểm tra persistence đứng **trước** các nhánh khác (lỗi hạ tầng bọc trong `InvalidOperationException` vẫn là 503 — V-8). `MISSING_HEADER` và `DecisionStateConflictException` thêm ở 5.2c/5.3 khi có nguồn phát sinh.
3. Kích hoạt classifier trong handler là việc của 5.2c (B-10). Ở 5.2b, `GlobalExceptionHandler` chỉ nhận thay đổi **bổ sung, không đổi mã v1**:
   - dùng `PersistenceFailureDetector.IsRetryable` thay cho kiểm tra cũ (mở rộng 503 cho `40001`/`40P01` và `PersistenceUnavailableException`);
   - response 503 có header HTTP `Retry-After: 5` (giữ extension `retryAfterSeconds` hiện có);
   - mã 503 lấy từ `ErrorCodes.PersistenceUnavailable` của SDK v2.
4. Ghim giá trị wire (CT-02, bổ sung M-6 của review 5.2a): test so **giá trị** mọi hằng của `ErrorCodes` v2 với bảng INTEGRATION_SPEC §3.8 (13 mã) và mọi hằng của `ReasonCodes` v2 với SAS 4.3 + INTEGRATION_SPEC §1.1 (9 mã); đếm số hằng bằng reflection để thêm hằng mới mà quên cập nhật test thì test đỏ.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Classify_MappingTable_Theory` | Một dòng cho mỗi hàng bảng trên | Đúng status, code, ExposeMessage, RetryAfter | UT-08 |
| `Classify_TransientNpgsqlNestedThreeLevels_Is503` | Bọc trong `InvalidOperationException` → `DbUpdateException` | 503 | UT-08 |
| `Classify_SerializationFailureAndDeadlock_Is503_Theory` | `PostgresException` `40001`, `40P01` | 503, RetryAfter 5 | UT-08 |
| `Classify_UniqueViolation_IsNot503` | `PostgresException 23505` bọc `DbUpdateException` | 500 | UT-08 |
| `Classify_ServerSideErrors_NeverClientError_Theory` | `InvalidOperationException`, `ArgumentException`, `NullReferenceException` | 500, không lộ message | UT-08 |
| `Classify_RequestAborted_ReturnsNull` | OCE + aborted | `null` | UT-08 |
| `Classify_CancellationNotFromClient_Is500` | OCE, không aborted | 500 | UT-08 |
| `Handler_PersistenceUnavailable_SetsRetryAfterHeader` | `DefaultHttpContext` + `PersistenceUnavailableException` | 503, header `Retry-After: 5`, `code PERSISTENCE_UNAVAILABLE` | UT-08 |
| `Handler_DeadlockSqlState_Returns503` | `PostgresException 40P01` | 503 + header | UT-08 |
| `Handler_ExistingV1Mappings_Unchanged_Theory` | `InvalidOperationException`, `DomainInvariantException` | Như hành vi v1 hiện tại (400 + mã cũ) | UT-08 |
| `ErrorCodes_Values_MatchIntegrationSpec` | Reflection | 13 hằng, đúng giá trị | CT-02 |
| `ReasonCodes_Values_MatchSpec` | Reflection | 9 hằng, đúng giá trị | CT-02 |

- [ ] Step 1–5; commit đề xuất `feat(runtime-v2): v2 exception classifier, retryable persistence detection and Retry-After`.

**Nghiệm thu:** bảng map lỗi v2 được khóa bằng test; v1 không đổi mã; hai việc hoãn từ 5.1 (Retry-After, 40001/40P01) xong.

---

### Task 11: Đồng bộ tài liệu, kiểm chứng cuối gói

**Files:**
- Modify: `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md` (Mục 4.1 thêm UT-11, UT-12, UT-13 đúng định nghĩa spec Mục 5)
- Modify: `docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md` (Mục 4.2: `IDecisionAuditStore`, `DecisionResponseComposer`, cache chỉ lưu `StrategyDecision`, MediatR bị gỡ ở 5.2c; ghi B-04 vào mô tả Fallback ở 4.3)
- Modify: `docs/contracts/INTEGRATION_SPEC.md` (§3.3: hai tầng lỗi B-06 — "vi phạm ràng buộc hình thức → `VALIDATION_ERROR`; chỉ quy tắc ngoài schema (`endTime > startTime`, thời lượng) → `DOMAIN_INVARIANT_VIOLATION`"; §3.4: ví dụ `APPROVAL_WINDOW_UNAVAILABLE` có `reasons` một câu giải thích theo B-05; §3.8: 503 kèm header `Retry-After`)
- Modify: `docs/planning/BACKLOG_SPRINTS.md` (T5.7: phần map exception đã có classifier, kích hoạt ở 5.2c; T5.8: xong `RequestHasher` v2)
- Modify: `../.claude/skills/tadss-architecture/SKILL.md` (runtime v2 ở `src/TADSS.Runtime.Api/V2/` tạm thời; `TimeProvider` bắt buộc; quy tắc test kiến trúc v1/v2)

**Yêu cầu:**
1. Tài liệu chỉ ghi điều đã hiện thực; không chép lại spec; sửa ở file sở hữu (THESIS_DEVELOPMENT_PLAYBOOK Mục III).
2. Kiểm tra link như gói 5.2a (lệnh vòng `for` trên `docs/planning/BACKLOG_SPRINTS.md docs/contracts/INTEGRATION_SPEC.md spec/*.md`) → chỉ in `done`.
3. Kiểm chứng cuối gói:
   - `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror --no-incremental` → 0 warning, 0 error.
   - `dotnet test TADSS.slnx -c Release --no-build -nologo` → `TADSS.UnitTests` xanh toàn bộ (695 + test mới; ghi số), `TADSS.IntegrationTests` 32/32 (cần Docker).
   - `git diff --stat <base gói> -- docs/contracts/baseline_public_api.txt` → rỗng.
   - `git diff --stat <base gói> -- src/TADSS.Runtime.Api ':!src/TADSS.Runtime.Api/V2'` → chỉ gồm `Middlewares/GlobalExceptionHandler.cs`, `Middlewares/PersistenceFailureDetector.cs`, `Configuration/DependencyInjection.cs`, `Program.cs`, `appsettings.json`, `Common/RuntimeErrorCodes.cs`.
   - Khởi động app bằng cấu hình mặc định (`dotnet run --project src/TADSS.Runtime.Api` với môi trường không phải Development, hoặc test host) không lỗi validate options; ghi kết quả.
4. Báo cáo gói: bảng 6.1 phần 5.2 (bất biến → test khóa, trạng thái), danh sách CatalogId đã phủ (UT-01, 04 (resolver), 05, 06, 07, 08, 09, 10, 11, 12, 13, CT-01 phần request/response, CT-02 phần giá trị wire), sai lệch đã ghi ở từng task.

- [ ] Step 1: Cập nhật tài liệu → Step 2: kiểm tra link → Step 3: kiểm chứng → Step 4: đề xuất commit `docs: runtime core v2 progress (package 5.2b)` → Step 5: đề xuất PR vào `develop` (người dùng tạo/merge, CI xanh).

---

## Ghi chú cho gói 5.2c (không làm ở 5.2b)

- Hiện thực `EfDecisionAuditStore` theo hợp đồng Task 8 (SqlState `23505` + tên constraint `uq_decision_idempotency_key`; lỗi có thể thử lại → `PersistenceUnavailableException`); thêm `GetDecisionDetailAsync`; đăng ký `IDecisionService` v2 (scoped) và store.
- `DecisionsController` v2 (`api/v2/decisions`): thiếu `X-Idempotency-Key` → 400 `MISSING_HEADER`; key > 128 ký tự → 400 `VALIDATION_ERROR`; formatter JSON riêng dùng `TadssJson.Options` (không dùng options MVC có `JsonStringEnumConverter` — `TadssJson.Apply` ném lỗi nếu gặp); `InvalidModelStateResponseFactory`/lỗi deserialize → `VALIDATION_ERROR`.
- Kích hoạt `ExceptionClassifier` trong `GlobalExceptionHandler` khi xóa v1 (B-10); `errors[field]` lấy từ `FieldName` của hai exception validation.
- Dời `src/TADSS.Runtime.Api/V2/**` về vị trí spec 4.2.3 cùng lúc đổi namespace SDK v2; xóa `RuntimeV2LayeringTests` hoặc đổi thành test "không còn kiểu v1".
- IT-13: Redis thật được dùng (tắt fallback memory ở Testing); IT-01…05 viết lại cho v2; CT-01 kiểm response IT theo schema.
- 5.4: breaker cho Redis + metric `bypass`; pipeline theo strategy (V-2); router thay `ResolveActive`.
- 5.3: `DepositCalculator` bổ sung quy đổi `amount → rate` cho override; `IDecisionAuditStore` thêm pending/override/outcome.
