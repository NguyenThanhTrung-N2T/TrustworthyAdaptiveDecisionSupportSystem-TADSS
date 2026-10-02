# Gói 5.2c — Chuyển hẳn sang v2: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Biến runtime thành hệ thống chỉ có v2: gỡ toàn bộ v1 (runtime, SDK, strategies, benchmark, test), đưa mã v2 về đúng vị trí/namespace gốc, hiện thực lưu audit trên PostgreSQL (migration v2 + `EfDecisionAuditStore`), mở `POST /api/v2/decisions` và `GET /api/v2/decisions/{decisionId}`, viết lại integration/fault-injection test cho v2 và chốt `openapi-v2.yaml` cùng baseline public API nháp v2.

**Architecture:** Lõi v2 đã có từ 5.2b (`DecisionService`, pipeline, cache, composer, classifier, hợp đồng store). Gói này (1) làm cứng các điểm hở còn lại của lõi, (2) gỡ v1 theo hai lớp (runtime + test trước, SDK + strategies sau), (3) dời mã v2 từ thư mục/namespace tạm về cây đích ở spec 4.2.1/4.2.3, (4) thêm tầng persistence EF Core sau `IDecisionAuditStore`, (5) thêm controller mỏng + `DecisionQueryService`, (6) kích hoạt `ExceptionClassifier` trong `GlobalExceptionHandler`. Không thêm project; không đổi wire/mã lỗi/schema ngoài spec.

**Tech Stack:** .NET 8, ASP.NET Core 8, EF Core 8 + Npgsql 8 (PostgreSQL 16), StackExchange.Redis 2.8.16, Polly.Core 8.4.2, Swashbuckle 6.6.2 (+ `Microsoft.OpenApi`), Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy), xUnit 2.5.3, FluentAssertions 6.12.1, Moq 4.20.72, JsonSchema.Net 9.4.0 (chỉ project test).

**Spec:** [`PHASE5_DESIGN_SPEC.md`](PHASE5_DESIGN_SPEC.md) Mục 4.2.0 (gói 5.2c, điểm dừng D-05/D-06, việc hoãn từ 5.1), 4.2.1 (cây SDK), 4.2.3 (runtime, map lỗi, migration, gỡ v1), 4.2.4 (5.2d — chỉ để biết ranh giới), 5, 6, 6.1. Nguồn sở hữu: [`INTEGRATION_SPEC.md`](../docs/contracts/INTEGRATION_SPEC.md) §3.1–3.4, §3.8, §3.9; [`DECISION_PERSISTENCE_INVARIANTS.md`](../docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md) B.1–B.4, C; [`RESILIENCE_AND_FAULT_TAXONOMY.md`](../docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md); [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) Mục 4.1 (IT-01…05, IT-13, IT-15, FI-01…10, CT-01, CT-02). Gói trước: [`PHASE5_PLAN_5.2b_RUNTIME_CORE_V2.md`](PHASE5_PLAN_5.2b_RUNTIME_CORE_V2.md) (ghi chú cuối file là đầu vào của gói này).

> **Cách đọc plan này:** plan mô tả **yêu cầu kỹ thuật** — trách nhiệm, interface (chữ ký), quy tắc, bảng test case và tiêu chí nghiệm thu. Plan **không** chứa mã hiện thực; người hiện thực viết mã theo TDD từ bảng test case. Chữ ký trong mục **Interfaces** là ràng buộc (task sau dựa vào đúng tên/kiểu đó); chi tiết bên trong do người hiện thực quyết định, miễn đạt tiêu chí nghiệm thu. Tên đường dẫn viết theo cây **đích** sau Task 4 trừ khi ghi "(trước Task 4)".

## Global Constraints

- Build chuẩn: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror` → **0 warning, 0 error** sau mỗi task. Unit test (`TADSS.UnitTests`) xanh sau mỗi task. Integration test (cần Docker) xanh sau mỗi task từ Task 5 trở đi và phải **chạy được** (không chỉ biên dịch) ở Task 9, 10, 12.
- Số unit test **giảm** ở Task 2–3 (xóa test v1) là đúng; test v2 **không được mất**: mỗi task xóa/dời mã phải ghi vào báo cáo *số test v2 trước và sau* (đếm bằng `dotnet test --list-tests` trên không gian tên v2/đường dẫn tương ứng) và hai số phải bằng nhau.
- Chỉ được xóa test v1 cùng task xóa mã v1 mà test đó kiểm tra (Phụ lục A). Không xóa test để chữa test đỏ; không `Skip`; không nới assertion; không `#pragma warning disable`; không `!` null-forgiving; không `?.Should()`.
- **Không đổi wire/ngữ nghĩa ngoài spec.** Muốn thêm/đổi route, header, mã lỗi, trường JSON ngoài INTEGRATION_SPEC ⇒ dừng và hỏi người dùng. Quyết định C-06/C-07 dưới đây là phần đã được chốt trong plan.
- `docs/contracts/baseline_public_api.txt`: **không sửa** trong Task 1–2. Chỉ Task 3 được thay bằng baseline nháp v2 (D-06), trong **commit riêng**, kèm danh sách kiểu bị gỡ/thêm trong báo cáo; người dùng review trước khi merge.
- **Migration (D-05):** dừng **trước** `dotnet ef migrations add` và xin phép người dùng (cổng G-1). Migration chỉ được áp lên database Testcontainers (và compose do người dùng chạy). **Tuyệt đối không** áp lên Neon, không `dotnet ef database update`, không đọc/in chuỗi kết nối thật.
- Không đọc/in/sửa `.env`, `appsettings.Development.json`, User Secrets.
- Thời gian trong runtime lấy từ `TimeProvider`; không `DateTime.UtcNow`/`DateTimeOffset.UtcNow`/`Stopwatch` trực tiếp trong mã runtime. Serialize payload (audit, cache Redis, response) chỉ bằng `TadssJson.Options`.
- SDK và Strategies **0 NuGet** (UT-09). Package mới/gỡ chỉ khai báo ở `Directory.Packages.props`. Options theo mẫu `{X}Options` + `SectionName` + `IValidateOptions<T>` + `ValidateOnStart()`.
- Test: `{Method}_{Scenario}_{Expected}`; mỗi test có `[Trait("CatalogId", …)]`; integration test thêm `[Trait("Category", "Integration")]`; XML summary tiếng Việt trên class test; **không** EF InMemory; test DB dùng Testcontainers PostgreSQL thật.
- Comment/message tiếng Việt như mã hiện có; định danh tiếng Anh. Subject commit Conventional Commits, tiếng Anh ASCII.
- **Commit chỉ khi người dùng cho phép** (rule `git-safety`; hook `git-guard` hỏi từng lệnh). Mỗi task kết thúc bằng một (hoặc vài) commit có chủ đích; không `git stash`, không đụng file ngoài phạm vi task.
- Nếu hành vi .NET/thư viện khác giả định trong plan: sửa tối thiểu giữ đúng **ý định** của yêu cầu, ghi "sai lệch" vào báo cáo task; không nới test.

## Quyết định thiết kế của gói (người dùng duyệt cùng plan)

| Mã | Quyết định | Lý do |
| :--- | :--- | :--- |
| **C-01** | Thứ tự: (1) làm cứng lõi v2 → (2) gỡ v1 runtime+test, kích hoạt classifier → (3) gỡ v1 SDK/strategies, đổi namespace, baseline nháp → (4) dời mã runtime về cây đích → (5) model EF + migration → (6) `EfDecisionAuditStore` → (7) `DecisionQueryService` → (8) controller + nối HTTP → (9) integration test v2 → (10) fault-injection v2 → (11) OpenAPI → (12) tài liệu + kiểm chứng cuối | Mỗi bước build + unit test xanh; việc nguy hiểm (xóa, đổi namespace) tách khỏi việc thêm tính năng để diff review được |
| **C-02** | Gỡ v1 hai lớp: Task 2 (runtime, benchmark, test runtime/IT v1) rồi Task 3 (SDK v1, strategies v1, test SDK/domain/contract v1) | `PublicApiSurfaceTests` so với baseline v1 nên chỉ được đổi khi bề mặt SDK đổi, tức Task 3 |
| **C-03** | `GlobalExceptionHandler` chuyển sang `ExceptionClassifier` ngay ở Task 2 (cùng task xóa test handler v1) | B-10 của 5.2b; test v1 của handler (`InvalidOperationException → 400`…) bị xóa cùng lúc nên không còn mâu thuẫn |
| **C-04** | Model EF và migration v2 gồm **cả 3 bảng** B.1–B.3 + hàm `tadss.reject_mutation()` + trigger (row + truncate) cho cả 3 bảng; store chỉ hiện thực phần 5.2c | Schema là một khối do DECISION_PERSISTENCE_INVARIANTS sở hữu; IT-05 kiểm tra trigger trên cả ba; tránh migration thứ hai ở 5.3 |
| **C-05** | `IDecisionAuditStore.GetDecisionDetailAsync` trả execution + override + outcome (join) ngay từ 5.2c; 5.2c chưa có đường ghi override/outcome nên hai phần luôn `null` trong luồng thật (test dựng bằng SQL trực tiếp) | Hợp đồng store đúng spec 4.2.3 và không đổi ở 5.3 |
| **C-06** | Response `POST` v2 **không** có header `X-Decision-ID` (spec v2 không liệt kê); chỉ `X-Correlation-ID` được echo | Wire chỉ theo INTEGRATION_SPEC §3.2; v1 từng có header này nhưng chưa consumer nào dùng |
| **C-07** | `X-Correlation-ID` dài hơn 128 ký tự: middleware **thay bằng GUID mới** (log Information, response echo GUID mới); giá trị hợp lệ vẫn giữ nguyên | Cột `correlation_id varchar(128)`; một header dài không được biến thành lỗi 500 vĩnh viễn (22001). Bổ sung một câu vào INTEGRATION_SPEC §3.2 |
| **C-08** | `nowUtc` của mỗi lần đánh giá được **cắt về micro giây** một lần (B-03 giữ nguyên, chỉ thêm độ chính xác) | `timestamptz` lưu micro giây; `created_at` trong DB phải bằng `evaluatedAt` trong payload |
| **C-09** | Tỷ lệ cọc có tối đa **4 chữ số thập phân** (cột `numeric(5,4)`): kiểm tra khi khởi động ở options của từng strategy; thêm chốt chặn ở composer (vi phạm = `InvalidOperationException` → 500, không ghi audit) | Cột sẽ làm tròn âm thầm làm payload ≠ cột; lỗi cấu hình phải nổ lúc khởi động, không lúc có khách |
| **C-10** | F1–F7 (fault injection v1) chuyển sang v2 bằng cách thay `IStrategyRegistry`/`IStrategyDecisionCache` bằng test double **chỉ trong host test** (không đăng ký gì ở mã Production). F6 (SaveChanges ném lỗi → 500 v1) không chuyển: được thay bằng biến thể FI-08 ghi audit | Spec 4.2.0 (việc hoãn từ 5.1); tránh trùng `CatalogId` FI-08 |
| **C-11** | Integration test dùng lại `ContractSchemas`/`RepositoryPaths` của UnitTests bằng `<Compile Include … Link>` trong csproj IT (kèm `JsonSchema.Net`) thay vì sao chép hay tạo project mới | Một nguồn helper, không thêm project |
| **C-12** | Test unit v2 dời từ `tests/TADSS.UnitTests/V2/**` về cây `Domain|Contracts|Services|Strategies|Infrastructure|Architecture|…` (namespace `TADSS.UnitTests.*`); tên kiểu còn chứa "V2" (`V2Samples`, `V2TestData`…) giữ nguyên, dọn ở 5.2d | 5.2c chỉ xóa "V2" khỏi **đường dẫn và namespace** |
| **C-13** | Thêm `docs/contracts/schemas/v2/decision-detail.schema.json` (viết tay đúng §3.4) để CT-01 phủ cả `GET` | Spec 4.2.1: schema viết tay là nguồn chuẩn; §3.4 đã định nghĩa hình dạng |
| **C-14** | Endpoint 5.2c **chưa có xác thực** (`X-Api-Key` thuộc 5.3): chỉ chạy local/IT, **không** expose ra ngoài (ngrok) trước 5.3 | Phạm vi gói; ghi vào báo cáo và INTEGRATION_SPEC |

Nếu người dùng không đồng ý mục nào, sửa plan trước khi chạy Task 1.

## Chuẩn bị (người dùng thực hiện)

1. Quyết định nhánh làm việc (tiếp tục `feat/phase-5-contract-v2-test-infra` hoặc nhánh mới). Agent chỉ đổi nhánh khi người dùng cho phép.
2. **Bật Docker Desktop trước Task 5** (Testcontainers: PostgreSQL, Redis, Toxiproxy). Task 1–4 chỉ cần unit test.
3. Tại cổng **G-1** (Task 5): cho phép chạy `dotnet ef migrations add`, và cài `dotnet-ef` nếu chưa có.
4. Cuối gói: review (a) baseline nháp v2, (b) mã migration, (c) `openapi-v2.yaml`; chạy `docker compose up` thử; áp migration lên Neon là bước riêng của người dùng (migration xóa bảng v1 — dữ liệu kiểm thử).
5. Commit/merge plan 5.2b (hiện `spec/PHASE5_PLAN_5.2b_RUNTIME_CORE_V2.md` đã nằm trong lịch sử nhánh) và plan này theo ý người dùng.

## Cấu trúc file (cây đích)

| File (gốc `src/TADSS.Runtime.Api/` trừ khi ghi khác) | Trách nhiệm | Task |
| :--- | :--- | :--- |
| `Middlewares/CorrelationIdMiddleware.cs`; `Services/DecisionService.cs`; `Services/Decisions/DecisionResponseComposer.cs`; `Middlewares/{ExceptionClassifier,PersistenceFailureDetector}.cs`; `src/TADSS.Strategies.*/…Options.cs`; `Configuration/…Extensions.cs` (đăng ký cache) | Làm cứng lõi (mang từ review cuối 5.2b) | 1 |
| `Middlewares/GlobalExceptionHandler.cs` | Dùng `ExceptionClassifier`; `Retry-After`; `errors[field]` | 2 |
| `src/Shared.DecisionContractSDK/**`, `src/TADSS.Strategies.*/**` | Chỉ còn kiểu v2, ở namespace gốc | 3 |
| `Configuration/`, `Controllers/`, `Services/`, `Services/Decisions/`, `Services/Strategies/`, `Infrastructure/{Caching,Data,Resilience}/`, `Middlewares/` | Mã runtime v2 ở vị trí đích (spec 4.2.3) | 4 |
| `Infrastructure/Data/{TadssDbContext.cs,Entities/*,Configurations/*,Migrations/*}` | Model EF 3 bảng, migration v2 | 5 |
| `Infrastructure/Data/EfDecisionAuditStore.cs`; `IDecisionAuditStore.cs` (mở rộng); `DecisionDetailRecord.cs`, `DecisionOverrideRecord.cs`, `DecisionOutcomeRecord.cs` | Hiện thực store, dịch lỗi PostgreSQL | 6 |
| `Services/{IDecisionQueryService,DecisionQueryService}.cs`; `Middlewares/DecisionNotFoundException.cs` | Chi tiết quyết định → `DecisionDetailDto` | 7 |
| `Controllers/DecisionsController.cs`; `Configuration/{DependencyInjection,…}.cs`; `Program.cs` | Endpoint v2, JSON v2, DI, Swagger | 8 |
| `tests/TADSS.IntegrationTests/**` | IT-01…05, IT-13, IT-15 và FI-01…04, 06, 08, 10 cho v2 | 9, 10 |
| `docs/contracts/openapi-v2.yaml`, `docs/contracts/schemas/v2/decision-detail.schema.json` | Artifact contract | 8–11 |
| Tài liệu, `CLAUDE.md`, `README.md`, `../.claude/skills/tadss-architecture/SKILL.md` | Đồng bộ | 12 |

Thứ tự phụ thuộc: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12.

---

### Task 1: Làm cứng lõi v2 trước khi cắt

**Files:** (đường dẫn trước Task 4, dưới `src/TADSS.Runtime.Api/V2/`)
- Modify: `Middlewares/CorrelationIdMiddleware.cs` (thư mục gốc Middlewares, dùng chung); `V2/Services/DecisionService.cs`; `V2/Services/Decisions/DecisionResponseComposer.cs`; `V2/Middlewares/ExceptionClassifier.cs`; `Middlewares/PersistenceFailureDetector.cs`; `V2/Configuration/RuntimeV2ServiceCollectionExtensions.cs`; `src/TADSS.Strategies.Default/V2/DefaultStrategyOptions.cs`, `src/TADSS.Strategies.MockAlternative/V2/MockAlternativeStrategyOptions.cs`.
- Test: các file test hiện có tương ứng dưới `tests/TADSS.UnitTests/V2/Runtime/**` và `tests/TADSS.UnitTests/V2/Strategies/**`; thêm `…/Middlewares/CorrelationIdMiddlewareTests.cs`.

**Interfaces:**
- Consumes: `ExceptionClassifier.Classify(Exception, bool requestAborted)`; `ErrorCodes.MissingHeader`, `ErrorCodes.ValidationError`; `DecisionService.EvaluateAsync`; `StrategyDecision.Approve`.
- Produces: hành vi (không đổi chữ ký public): (a) `Classify(ContractValidationException)` dùng `ErrorCode` của exception khi thuộc tập đóng {`VALIDATION_ERROR`, `MISSING_HEADER`}, ngược lại `VALIDATION_ERROR`; (b) `PersistenceFailureDetector.IsRetryable` duyệt cả `AggregateException.InnerExceptions`; (c) `CorrelationIdMiddleware` theo C-07; (d) `nowUtc` theo C-08; (e) tỷ lệ cọc theo C-09; (f) `IConnectionMultiplexer` được đăng ký trong chính `AddDecisionRuntimeV2` (lazy, `AbortOnConnectFail = false`, cùng quy tắc timeout hiện có), không còn phụ thuộc đăng ký của v1.

**Yêu cầu:**
1. **Classifier/Detector (UT-08):** thêm hai hành vi (a), (b). `ExposeMessage` của `MISSING_HEADER` = có. Không thêm mã mới ngoài 13 mã §3.8.
2. **Correlation id (C-07):** giá trị hợp lệ = không rỗng/khoảng trắng và ≤ 128 ký tự; ngược lại tạo GUID mới. Giá trị > 128 ký tự không bao giờ lọt vào `HttpContext.TraceIdentifier`, log scope, response header hay audit. Log Information một lần (không in nguyên giá trị dài; chỉ độ dài).
3. **`nowUtc` (C-08):** lấy một lần từ `TimeProvider`, cắt phần dưới micro giây (bội số 10 tick), rồi dùng cho `DecisionContext.EvaluatedAtUtc`, `evaluatedAt`, approval window, `createdAt` — đúng B-03. Test với `FakeTimeProvider` đặt giá trị có tick lẻ.
4. **Tỷ lệ cọc (C-09):** (i) `DefaultStrategyOptions`/`MockAlternativeStrategyOptions.Validate()` từ chối tỷ lệ có > 4 chữ số thập phân (thông báo nêu tên trường); (ii) composer: kết quả `APPROVED` mà `rate` có scale > 4 → `InvalidOperationException` (không dùng `!`), service không ghi audit. Giá trị mặc định trong `appsettings.json` phải vẫn hợp lệ.
5. **Redis (mang từ review cuối 5.2b, M5):** chuyển đăng ký `IConnectionMultiplexer` (nguyên văn quy tắc hiện ở `DependencyInjection.cs` v1: parse chuỗi, `AbortOnConnectFail=false`, `ConnectTimeout`, `SyncTimeout`, `AsyncTimeout`) vào nhóm đăng ký cache của `AddDecisionRuntimeV2` để Task 2 xóa DI v1 mà cache v2 không âm thầm thành memory-only. Chỉ tạo multiplexer khi `Provider = Redis` được resolve (không kết nối khi khởi động, không kết nối khi `Provider = Memory`); khi `Provider = Redis` mà không lấy được multiplexer → log Warning một lần lúc resolve cache (không ném).
6. Các thay đổi chỉ chạm V2 và hai file dùng chung; **không** đụng controller/service v1.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Classify_ContractValidationWithMissingHeaderCode_Is400MissingHeader` | `ErrorCode = MISSING_HEADER` | 400 `MISSING_HEADER`, ExposeMessage | UT-08 |
| `Classify_ContractValidationWithUnknownCode_FallsBackToValidationError` | `ErrorCode` lạ (kể cả `INTERNAL_ERROR`) | 400 `VALIDATION_ERROR` | UT-08 |
| `IsRetryable_AggregateWithTransientNpgsql_ReturnsTrue` | `AggregateException` chứa `NpgsqlException{IsTransient}` ở nhánh con thứ hai; và chứa 23505 | true; false | UT-08 |
| `Invoke_CorrelationIdOver128Chars_ReplacesWithGuid` | Header 129 và 1000 ký tự | `TraceIdentifier`, response header, log scope là GUID mới; header 128 ký tự giữ nguyên | UT-08 |
| `Invoke_CorrelationIdValidOrMissing_UnchangedBehavior` | Header hợp lệ / thiếu / khoảng trắng | Giữ nguyên / GUID mới / GUID mới | UT-08 |
| `EvaluateAsync_NowWithSubMicrosecondTicks_IsTruncatedConsistently` | Clock có tick lẻ | `evaluatedAt = createdAt = EvaluatedAtUtc` đều bằng giá trị cắt, không dư tick | UT-11 |
| `Validate_DepositRateWithMoreThanFourDecimals_Fails_Theory` | Mỗi tỷ lệ của hai strategy = 0.33333 | `Validate()` lỗi, nêu tên trường | UT-05 |
| `Compose_ApprovedWithRateScaleOverFour_Throws` | Strategy giả trả rate 0.12345 | `InvalidOperationException`, không có bản ghi nào được tạo | UT-10 |
| `AddDecisionRuntimeV2_RedisProvider_RegistersMultiplexerWithoutConnecting` | `Provider = Redis`, host chưa tồn tại | Resolve được cache, không ném, không chặn khởi động; `Provider = Memory` không resolve multiplexer | UT-13 |

**Nghiệm thu:** 6 điểm hở của review cuối 5.2b được khóa bằng test; v1 không đổi hành vi; `baseline_public_api.txt` không đổi; commit đề xuất `fix(runtime-v2): harden core before cutover`.

---

### Task 2: Gỡ v1 phía runtime và test; kích hoạt classifier

**Files:**
- Delete: `src/TADSS.Runtime.Api/Controllers/DecisionsController.cs`; `Services/{DecisionService,IDecisionService,RequestHasher,StrategyResolver,IStrategyResolver}.cs`; `Infrastructure/Caching/{CacheKeyBuilder,IDecisionCacheService,MemoryDecisionCacheService,RedisDecisionCacheService,ResilientDecisionCacheService}.cs`; `Infrastructure/Resilience/{DecisionExecutionOutcome,DecisionResiliencePipeline,FallbackDecisionStrategy,IDecisionResiliencePipeline}.cs`; `Infrastructure/Data/Auditing/` (MediatR); `Common/{CustomerTierConstants,RuntimeErrorCodes}.cs`; `tests/TADSS.BenchmarkRunner/` (cả project); các test v1 theo Phụ lục A (nhóm "Task 2").
- Modify: `Configuration/DependencyInjection.cs` (bỏ đăng ký v1: resolver, strategy v1, pipeline/cache v1, `IConnectionMultiplexer` v1 (đã chuyển ở Task 1), MediatR, `IDecisionService` v1; **giữ** `AddDbContext` kèm interceptor tới Task 5, health check, `DatabaseOptions`, `AddDecisionRuntimeV2`, exception handler, Swagger); `Program.cs` (bỏ `IStrategyResolver`); `Middlewares/GlobalExceptionHandler.cs`; `TADSS.Runtime.Api.csproj` (bỏ `MediatR`); `TADSS.slnx`; `tests/TADSS.UnitTests/TADSS.UnitTests.csproj` (bỏ ProjectReference `BenchmarkRunner`, package EF InMemory); `Directory.Packages.props` (bỏ `MediatR`, `Microsoft.EntityFrameworkCore.InMemory`); CLAUDE.md lệnh benchmark (chỉ dòng lệnh, phần còn lại ở Task 12).
- Giữ nguyên ở task này: `Infrastructure/Data/{TadssDbContext,Entities,Configurations,Interceptors,Migrations}` (đổi ở Task 5), `Infrastructure/Resilience/{ResilienceOptions,FailureReasonPrefixes}.cs`, `Infrastructure/Caching/TadssCacheOptions.cs`, `Configuration/{StrategyOptions,DatabaseOptions,RequiredHeaderOperationFilter}.cs`, `Common/HttpHeaderNames.cs`, `Middlewares/{CorrelationIdMiddleware,IdempotencyConflictException,PersistenceFailureDetector}.cs`, toàn bộ `V2/`.

**Interfaces:**
- Consumes: `ExceptionClassifier.Classify`, `ErrorClassification(StatusCode, Code, Title, ExposeMessage, RetryAfterSeconds)`, `PersistenceFailureDetector`.
- Produces: `GlobalExceptionHandler : IExceptionHandler` dùng classifier (ràng buộc cho Task 8, 9).

**Yêu cầu:**
1. Xóa đúng danh sách trên; **không** xóa gì thuộc SDK/Strategies (Task 3). Sau task, ứng dụng khởi động được (không controller nào) và `AddDecisionRuntimeV2` là nơi đăng ký duy nhất cho lõi.
2. `GlobalExceptionHandler`: `Classify` → `null` ⇒ không ghi response, log Information (giữ hành vi hiện tại khi client hủy), trả `true`. Ngược lại tạo `ProblemDetails` (`application/problem+json`) với `status`, `title`, `type` (giữ định dạng `https://tadss.smashcourt.vn/errors/{code-kebab}`), `instance`, `detail` = message của exception **chỉ khi** `ExposeMessage` (ngược lại câu chung theo mã: 500, 503, 504), extension `code`, `traceId`; nếu `RetryAfterSeconds` có giá trị ⇒ extension `retryAfterSeconds` **và** header `Retry-After`; nếu exception là `ContractValidationException`/`DomainInvariantException` có `FieldName` ⇒ extension `errors` = `{ FieldName: [message] }`. Không bao giờ trả message của exception hạ tầng. Log: 4xx mức Warning, 5xx mức Error kèm exception. Bỏ nhánh `DbUpdateConcurrencyException`/`KeyNotFoundException`/`InvalidOperationException → 400` của v1 (bảng map v2 là đóng).
3. `FailureReasonPrefixes`, `ResilienceOptions`, `TadssCacheOptions`, `StrategyOptions`, `HttpHeaderNames` còn được lõi v2 dùng — giữ.
4. `RuntimeErrorCodes` bị xóa: mọi tham chiếu (kể cả IT `PersistenceFaultInjectionTests` — sẽ viết lại ở Task 10; xem Phụ lục A) chuyển sang `ErrorCodes` của SDK v2.
5. Test v1 của handler (`MiddlewareAndInterceptorTests` phần handler) được thay bằng test của handler mới (bảng dưới); phần test interceptor giữ tới Task 5.

**Test cases:** (`tests/TADSS.UnitTests/Infrastructure/…` hoặc thư mục V2 hiện tại, theo vị trí test handler 5.2b)

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Handle_MappingTable_Theory` | Mỗi hàng bảng map spec 4.2.3 qua `DefaultHttpContext` | Đúng status, `code`, content-type problem+json, `traceId` | UT-08 |
| `Handle_ClientAbort_WritesNothing` | OCE + `RequestAborted` đã hủy | Không ghi body, trả `true`, log Information | UT-08 |
| `Handle_RetryableErrors_SetRetryAfterHeaderAndExtension` | 503 (kể cả `40001`, `40P01`), 504 | Header `Retry-After: 5` và `retryAfterSeconds = 5` | UT-08 |
| `Handle_ServerSideErrors_HideMessage_Theory` | `InvalidOperationException`, `ArgumentException`, `NullReferenceException`, `DbUpdateException` 23505 | 500 `INTERNAL_ERROR`, `detail` không chứa message gốc | UT-08 |
| `Handle_FieldNameOnValidationErrors_PopulatesErrorsExtension` | `ContractValidationException("…","msg","items[0].price")` | `errors["items[0].price"] = ["msg"]` | UT-08 |
| `Handle_ContractValidationMissingHeader_Is400MissingHeader` | `ErrorCode = MISSING_HEADER` | 400 `MISSING_HEADER` | UT-08 |

**Nghiệm thu:** build `--warnaserror` 0/0; unit test v2 giữ nguyên số lượng (báo cáo số trước/sau); IT project biên dịch được (các file IT v1 theo Phụ lục A đã bị xóa); không còn tham chiếu `MediatR`, EF InMemory, `BenchmarkRunner`, `RuntimeErrorCodes` ở bất cứ csproj/mã nào; ứng dụng khởi động với cấu hình mặc định (Production, 20 giây, không lỗi validate options); commit đề xuất `refactor: remove v1 runtime, benchmark runner and activate v2 exception mapping`.

---

### Task 3: Gỡ v1 phía SDK và Strategies; đổi namespace về gốc; baseline nháp v2

**Files:**
- Delete (SDK): mọi file v1: `Common/{ApiResponse,PagedResult,ErrorCodes}.cs`; `DTOs/{Decisions,Governance,Mappings}` v1; `Domain/{CustomerInfo,PaymentPolicy,Reservation,ReservationMetadata,ReservationSchedule,ResourceInfo}.cs`; `Enums/*` v1; `Interfaces/IReservationDecisionStrategy.cs` v1. Giữ `Common/ContractValidationException.cs`, `Domain/DomainInvariantException.cs`.
- Move + đổi namespace (SDK): toàn bộ `src/Shared.DecisionContractSDK/V2/**` lên thư mục cha đúng cây spec 4.2.1; namespace `Shared.DecisionContractSDK.V2.X` → `Shared.DecisionContractSDK.X` (`Common`, `Domain`, `Decisions`, `DTOs.*`, `Enums`, `Interfaces`).
- Strategies: xóa `DefaultDecisionStrategy.cs`/`MockAlternativeDecisionStrategy.cs` v1 và v1 options (nếu có); dời `V2/*` lên gốc project; namespace `TADSS.Strategies.{Name}.V2` → `TADSS.Strategies.{Name}`.
- Modify: mọi `using`/tham chiếu `…V2…` trong `src/TADSS.Runtime.Api/**` và `tests/**`; `src/Shared.DecisionContractSDK/Shared.DecisionContractSDK.csproj` (`Version` → `2.0.0-draft`); `README.md` của SDK; `tests/TADSS.UnitTests/Architecture/PublicApiSurfaceTests.cs` (tiêu đề/ghi chú, **không** đổi cơ chế so sánh; header sinh ra `v2.0.0-draft`, `STATUS: DRAFT v2.0.0 BASELINE — chờ đóng băng M5`); `SdkV2LayeringTests`/`RuntimeV2LayeringTests` (cập nhật cho không còn v1, hoặc để Task 4 xử lý — ghi rõ).
- Replace (commit riêng): `docs/contracts/baseline_public_api.txt` bằng bề mặt thực tế (`TestResults/baseline_public_api.actual.txt`).
- Test v1 bị xóa: nhóm "Task 3" ở Phụ lục A.

**Interfaces:**
- Consumes: không (thuần cấu trúc).
- Produces: namespace gốc `Shared.DecisionContractSDK.*` và `TADSS.Strategies.{Name}` (mọi task sau dùng); alias `using ErrorCodes = …V2…` biến mất.

**Yêu cầu:**
1. Chỉ **di chuyển/đổi namespace**; tên kiểu và hành vi không đổi (diff dạng rename). Không viết lại logic. Với kiểu trùng tên giữa v1 và v2, v1 phải xóa **trước** khi dời v2.
2. Bề mặt public mới = đúng cây spec 4.2.1. Nếu bề mặt thực tế chứa kiểu ngoài cây (hoặc thiếu kiểu trong cây) ⇒ ghi vào báo cáo và hỏi, không tự thêm/bớt.
3. Sau khi `PublicApiSurfaceTests` đỏ do lệch baseline (mong đợi), tạo baseline nháp từ file `actual` trong **commit riêng**; báo cáo liệt kê: kiểu bị gỡ (v1), kiểu thêm (v2), kiểu giữ. Đây là điểm dừng D-06 của gói: người dùng review trước merge; **không** tự chỉnh nội dung baseline bằng tay.
4. Xóa dòng ghi chú "tạm thời/V2" trong XML doc không còn đúng; XML doc public vẫn bắt buộc (`TreatWarningsAsErrors`).
5. `SdkDependencyPurityTests`/`StrategyDependencyPurityTests` giữ nguyên, phải xanh (0 NuGet).

**Test cases:** không thêm test mới; yêu cầu: toàn bộ test v2 hiện có xanh sau khi đổi namespace (đếm trước/sau bằng nhau), `PublicApiSurfaceTests` xanh sau commit baseline.

**Nghiệm thu:** `grep` không còn `\.V2\b` trong namespace/using của `src/**` (trừ `src/TADSS.Runtime.Api/V2/` thư mục — Task 4); `git diff --stat` cho thấy phần lớn là rename; hai commit: `refactor(sdk): replace v1 contract with v2 at root namespace` và `docs(contract): v2.0.0-draft public api baseline`.

---

### Task 4: Dời mã runtime v2 về cây đích; dọn đường dẫn test

**Files:**
- Move (runtime): `src/TADSS.Runtime.Api/V2/Configuration/*` → `Configuration/`; `V2/Infrastructure/{Caching,Data,Resilience}/*` → `Infrastructure/{Caching,Data,Resilience}/`; `V2/Middlewares/*` → `Middlewares/`; `V2/Services/*` → `Services/` (giữ `Services/Decisions/`, `Services/Strategies/`); namespace `TADSS.Runtime.Api.V2.X` → `TADSS.Runtime.Api.X`.
- Move (test): `tests/TADSS.UnitTests/V2/Architecture|Common|Contracts|Decisions|Domain|Strategies|TestData` → cùng tên không có tiền tố `V2/`; `V2/Runtime/{Architecture,Caching,Configuration,Middlewares,Resilience,Services,TestDoubles}` → `Architecture`, `Infrastructure/Caching`, `Configuration`, `Middlewares`, `Infrastructure/Resilience`, `Services`, `TestDoubles`; namespace `TADSS.UnitTests.V2.*` → `TADSS.UnitTests.*` (C-12).
- Rename: `Configuration/RuntimeV2ServiceCollectionExtensions.cs` → `Configuration/DecisionRuntimeServiceCollectionExtensions.cs`, `AddDecisionRuntimeV2` → `AddDecisionRuntime`.
- Replace: `RuntimeV2LayeringTests` → `Architecture/NoLegacyV1Tests.cs` (UT-09): không còn kiểu/namespace chứa đoạn `.V2.` hay thư mục `V2` trong `src/**`; `SdkV2LayeringTests` cập nhật tương ứng (giữ các ràng buộc còn đúng: SDK không phụ thuộc Runtime, Strategies chỉ phụ thuộc SDK).
- Delete: `Common/RuntimeErrorCodes.cs` nếu còn (đã xóa Task 2), thư mục `V2/` rỗng.

**Interfaces:**
- Produces: `AddDecisionRuntime(this IServiceCollection, IConfiguration)`; vị trí đích của mọi kiểu (Task 5–11 dùng đường dẫn đích).

**Yêu cầu:**
1. Chỉ di chuyển/đổi tên; không đổi hành vi. Nếu hai file trùng tên đích (ví dụ `IdempotencyConflictException`, `DatabaseOptions`) ⇒ một bản đã tồn tại từ v1: giữ bản đang dùng, xóa bản kia, ghi vào báo cáo.
2. `Program.cs`, `DependencyInjection.cs` (đổi tên phương thức), `appsettings*` không đổi nội dung cấu hình.
3. `NoLegacyV1Tests` không dùng danh sách tên cứng: quét assembly Runtime.Api/SDK/Strategies bằng reflection (namespace và tên kiểu, trừ tên kiểu test helper vì nằm ở project test) và quét thư mục nguồn tìm thư mục tên `V2`.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Assemblies_NoNamespaceContainsV2Segment_Theory` | SDK, Runtime.Api, mỗi Strategies | Không có namespace có đoạn `V2` | UT-09 |
| `SourceTree_HasNoV2Folders` | Quét `src/` | Không có thư mục tên `V2` | UT-09 |
| `Runtime_DoesNotReferenceRemovedV1Types` | Tên v1 đã gỡ (`IStrategyResolver`, `IDecisionResiliencePipeline`, `IDecisionCacheService`, `MediatR`) | Không còn trong assembly Runtime | UT-09 |
| Toàn bộ test hiện có | — | Xanh, số lượng không đổi | — |

**Nghiệm thu:** cây thư mục Runtime.Api khớp bảng spec 4.2.3 (còn thiếu `Controllers/DecisionsController` — Task 8, và store EF — Task 5/6); không còn `V2` trong đường dẫn/namespace ở `src/` và `tests/` (trừ tên kiểu helper, C-12); commit đề xuất `refactor: move v2 runtime and tests to target layout`.

---

### Task 5: Model EF 3 bảng và migration v2 (cổng G-1)

**Files:**
- Create: `Infrastructure/Data/Entities/{DecisionExecution,DecisionOverride,DecisionOutcome}.cs` (thay `DecisionExecution` v1); `Infrastructure/Data/Configurations/{DecisionExecutionConfiguration,DecisionOverrideConfiguration,DecisionOutcomeConfiguration}.cs`; migration `Infrastructure/Data/Migrations/*_DecisionPersistenceV2.cs` (+ `.Designer.cs`, cập nhật `TadssDbContextModelSnapshot.cs`).
- Modify: `Infrastructure/Data/TadssDbContext.cs` (3 `DbSet`); `Configuration/DecisionRuntimeServiceCollectionExtensions.cs` hoặc `DependencyInjection.cs` (bỏ `AuditTimestampInterceptor`); `Program.cs` (cảnh báo khi `ApplyMigrationsOnStartup` bật).
- Delete: `Infrastructure/Data/Interceptors/AuditTimestampInterceptor.cs`; phần test interceptor trong `MiddlewareAndInterceptorTests`.
- Test: `tests/TADSS.UnitTests/Infrastructure/Data/DecisionPersistenceModelTests.cs`; `tests/TADSS.IntegrationTests/Migrations/{MigrationIntegrationTests,SchemaConstraintTests,AppendOnlyTriggerTests}.cs`; sửa `Infrastructure/{TadssWebApplicationFactory,IntegrationTestBase}.cs` bỏ interceptor.

**Interfaces:**
- Produces: `TadssDbContext` với `DbSet<DecisionExecution>`, `DbSet<DecisionOverride>`, `DbSet<DecisionOutcome>`; thuộc tính entity 1-1 với cột B.1–B.3 (kiểu CLR: `Guid`, `string`, `decimal?`, `int`, `bool`, `DateTimeOffset`; enum lưu `varchar` qua `HasConversion<string>`; payload `jsonb` ánh xạ `string`); schema `tadss`, lịch sử migration `tadss.__EFMigrationsHistory`.

**Yêu cầu:**
1. **Model khớp DECISION_PERSISTENCE_INVARIANTS B.1–B.3 từng cột** (tên snake_case, kiểu, độ dài, nullability), mọi `CHECK` (`ck_de_deposit`, `ck_de_approval`, `role`, `status`, `execution_type`, `execution_time_ms`, `deposit_*`, `ck_do_*`, outcome…), unique (`decision_id`, `uq_decision_idempotency_key` partial `WHERE idempotency_key IS NOT NULL AND role = 'PRIMARY'`), `FOREIGN KEY` override/outcome → `decision_executions(decision_id)`, và 4 index B.1 (`idx_de_reservation_created` có `created_at DESC`, `idx_de_pending` partial, `idx_de_request_hash`, `idx_de_created_at`). Entity **không có** `updated_at`/`version`/`is_overridden` (append-only). `id` mặc định `gen_random_uuid()`.
2. **Cổng G-1:** hoàn tất bước 1 và test model (UT) **trước**, rồi **dừng và hỏi người dùng** xin phép chạy `dotnet ef migrations add DecisionPersistenceV2` (tên do plan chốt). Chỉ tiếp tục khi được phép.
3. **Migration (D-05):** một migration, `Up` có thao tác tường minh theo thứ tự: `DropTable` bảng v1 `decision_executions`; `CreateTable` 3 bảng; `CreateIndex`/ràng buộc; `migrationBuilder.Sql` tạo hàm `tadss.reject_mutation()` (ERRCODE `P0001`, thông báo B.4) và **6 trigger** (row `BEFORE UPDATE OR DELETE` + statement `BEFORE TRUNCATE` cho từng bảng). `Down` đảo ngược đầy đủ (xóa trigger/hàm/3 bảng, dựng lại bảng v1 đúng snapshot trước). Không dùng `EnsureDeleted/EnsureCreated`, không xóa cả schema. Mã sinh tự động được phép chỉnh tay **chỉ** để thêm SQL hàm/trigger và sắp xếp thứ tự an toàn FK.
4. Bỏ `AuditTimestampInterceptor` và mọi chỗ gán `CreatedAt/UpdatedAt` tự động: thời gian do `DecisionService` (`nowUtc`, C-08) quyết định. Mọi `DateTimeOffset` ghi xuống phải offset 0 (Npgsql yêu cầu).
5. `Program.cs`: khi `Tadss:Database:ApplyMigrationsOnStartup = true` ghi log **Warning** ("chỉ dùng cho E-LOCAL, không bật cho Neon/staging"); logic áp migration giữ nguyên.
6. IT dùng `CREATE DATABASE … TEMPLATE` từ DB mẫu đã `Migrate()` (fixture hiện có) nên trigger không làm hỏng việc dọn dẹp; không dùng `TRUNCATE`/`DELETE` để dọn bảng audit trong test.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Model_DecisionExecutions_MatchesPersistenceInvariantsColumns` | Dùng Npgsql provider **không** kết nối, đọc relational model | Đủ 23 cột + `id`, đúng kiểu/độ dài/null; index và filter đúng | UT-01 |
| `Model_CheckConstraintsAndUniqueIndexes_Declared_Theory` | 3 bảng | Mọi `CHECK`/unique/FK trong B.1–B.3 có trong model | UT-01 |
| `Migrate_EmptyDatabase_AppliesAllMigrationsWithoutPendingModelChanges` | DB trống | Áp thành công; `HasDifferences` = false | IT-15 |
| `Startup_ApplyMigrationsOnStartupEnabled_LogsWarningAndMigrates` | Cờ bật, bắt log | Có log Warning; DB có bảng v2 | IT-15 |
| `Startup_ApplyMigrationsOnStartupDefault_DoesNotTouchDatabase` | Cờ mặc định | DB trống nguyên | IT-15 |
| `Migrate_FromV1Schema_DropsLegacyTableAndCreatesV2` | Áp 3 migration cũ rồi migration mới | Bảng v1 mất, 3 bảng v2 có | IT-15 |
| `Migrate_DownThenUp_RoundTrips` | `Down` rồi `Up` trên Testcontainers | Không lỗi; schema như trước | IT-15 |
| `AppendOnly_UpdateDeleteTruncateOnAnyAuditTable_Rejected_Theory` | `UPDATE`/`DELETE`/`TRUNCATE` trên cả 3 bảng bằng SQL trực tiếp | Lỗi SqlState `P0001`; dữ liệu không đổi | IT-05 |
| `Constraints_InvalidRows_Rejected_Theory` | Mỗi `CHECK` (APPROVED thiếu deposit, NEED_APPROVAL thiếu expires, role/status lạ, rate > 1, `execution_time_ms < 0`, reason < 5 ký tự…) | Từ chối (23514) | IT-05 |
| `IdempotencyIndex_PrimaryUniqueButShadowAllowed` | Hai PRIMARY cùng key; một PRIMARY + một SHADOW cùng key | 23505 với `uq_decision_idempotency_key`; trường hợp SHADOW được phép | IT-05 |

**Nghiệm thu:** IT-15 và IT-05 xanh trên Testcontainers; model snapshot không còn thay đổi chưa tạo migration; **không** áp migration lên Neon; người dùng review mã migration (đưa đường dẫn vào báo cáo); commit đề xuất `feat(persistence): v2 audit schema model and migration`.

---

### Task 6: `EfDecisionAuditStore` và dịch lỗi PostgreSQL

**Files:**
- Create: `Infrastructure/Data/EfDecisionAuditStore.cs`; `Infrastructure/Data/{DecisionDetailRecord,DecisionOverrideRecord,DecisionOutcomeRecord}.cs`.
- Modify: `Infrastructure/Data/IDecisionAuditStore.cs` (thêm `GetDecisionDetailAsync`; sửa XML doc); `Configuration/DecisionRuntimeServiceCollectionExtensions.cs` (đăng ký store **scoped**).
- Test: `tests/TADSS.IntegrationTests/Persistence/EfDecisionAuditStoreTests.cs`; `tests/TADSS.UnitTests/Infrastructure/Data/EfDecisionAuditStoreMappingTests.cs`; cập nhật `TestDoubles/FakeDecisionAuditStore.cs`.

**Interfaces:**
- Consumes: `TadssDbContext` (Task 5), `DecisionExecutionRecord`, `InsertExecutionResult`, `PersistenceUnavailableException`, `PersistenceFailureDetector`.
- Produces:
  - `sealed record DecisionOverrideRecord(Guid DecisionId, OverrideAction Action, decimal? DepositRate, decimal? DepositAmount, string ActorId, string Reason, string IdempotencyKey, DateTimeOffset CreatedAt)`.
  - `sealed record DecisionOutcomeRecord(Guid DecisionId, DecisionOutcome Outcome, DateTimeOffset OccurredAt, DateTimeOffset ReportedAt)`.
  - `sealed record DecisionDetailRecord(DecisionExecutionRecord Execution, DecisionOverrideRecord? Override, DecisionOutcomeRecord? Outcome)`.
  - `IDecisionAuditStore.GetDecisionDetailAsync(Guid decisionId, CancellationToken cancellationToken) : Task<DecisionDetailRecord?>`.
  - `EfDecisionAuditStore(TadssDbContext db, ILogger<EfDecisionAuditStore> logger)` : `IDecisionAuditStore`.

**Yêu cầu:** (hợp đồng chi tiết nằm trong XML doc của `IDecisionAuditStore` từ Task 8 của 5.2b — hiện thực đúng, không đổi nghĩa)
1. `FindPrimaryByIdempotencyKeyAsync`: truy vấn `AsNoTracking`, chỉ `role = 'PRIMARY'`; trả bản ghi hoặc `null`.
2. `InsertExecutionAsync`: một `INSERT` duy nhất (một `SaveChangesAsync`), rồi trả `InsertExecutionResult(stored, IsExistingWinner: false)`. Bắt `DbUpdateException` có `PostgresException` với `SqlState = 23505` **và** `ConstraintName = "uq_decision_idempotency_key"` ⇒ gỡ entity thất bại khỏi change tracker, đọc bản thắng (`AsNoTracking`) và trả `IsExistingWinner = true`; nếu đọc không thấy bản thắng ⇒ `InvalidOperationException` (không bao giờ xảy ra trên bảng append-only; không nuốt lỗi).
3. Lỗi `PersistenceFailureDetector.IsRetryable` (kết nối/timeout/transient, `40001`, `40P01`) ở **mọi** method ⇒ `PersistenceUnavailableException(message tiếng Việt không chứa dữ liệu nhạy cảm, innerException)`. Mọi lỗi khác (kể cả `23505` ở constraint khác như `decision_id`, `23514`, `22001`) ⇒ để nguyên exception gốc (thành 500). Không bao giờ ném `PersistenceUnavailableException` cho vi phạm ràng buộc.
4. `GetDecisionDetailAsync`: một truy vấn (hoặc tối đa hai) lấy execution theo `decision_id` kèm override và outcome (`LEFT JOIN`); `null` nếu không có execution; `AsNoTracking`; **không lọc theo `role`** (chi tiết tra theo `decisionId`, cả PRIMARY lẫn SHADOW).
5. Ánh xạ record ↔ entity là hàm riêng, thuần, có test; không để `DbContext`/`IQueryable`/entity lọt ra ngoài store. Không có method sửa/xóa (INV-4).
6. Hủy: `OperationCanceledException` của caller lan ra nguyên vẹn (không bọc thành persistence lỗi).
7. `FakeDecisionAuditStore` cập nhật cho method mới (tra theo `decisionId`, cho phép `Seed` override/outcome).

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Map_RecordToEntityAndBack_RoundTrips_Theory` | Mỗi `status` × `executionType` × role | Record → entity → record bằng nhau (kể cả `decimal`, `DateTimeOffset` µs, jsonb) | UT-01 |
| `Insert_NewExecution_StoresExactlyOneRowEqualToRecord` | DB thật | 1 hàng; đọc lại bằng nhau | IT-01 |
| `Insert_DuplicateIdempotencyKey_ReturnsExistingWinner` | Hai insert cùng key | Lần 2 `IsExistingWinner = true`, `Stored` = bản đầu, không ném | IT-04 |
| `Insert_FiftyConcurrentSameKey_OneRowAllReturnWinner` | 50 task song song, mỗi task scope/DbContext riêng | Đúng 1 hàng; 49 `IsExistingWinner = true` cùng `decisionId` | IT-04 |
| `Insert_DuplicateDecisionIdDifferentKey_PropagatesPostgresException` | Trùng `decision_id` | Exception gốc 23505 (ConstraintName khác), **không** thành winner/503 | IT-05 |
| `Insert_CheckViolation_PropagatesOriginalException` | Bản ghi vi phạm `ck_de_deposit` | Exception gốc 23514 | IT-05 |
| `Insert_ConnectionLost_ThrowsPersistenceUnavailable` | Toxiproxy ngắt hoặc chuỗi kết nối sai cổng | `PersistenceUnavailableException`, inner được giữ | FI-08 |
| `Insert_SerializationFailureOrDeadlock_ThrowsPersistenceUnavailable_Theory` | `PostgresException` 40001/40P01 qua `DbContext` giả lỗi | `PersistenceUnavailableException` | UT-08 |
| `FindPrimary_ShadowRecordWithSameKey_Ignored` | Có SHADOW cùng key | Trả `null` | IT-04 |
| `GetDetail_WithOverrideAndOutcomeRows_ReturnsJoined` | Dựng override/outcome bằng SQL trực tiếp | `DecisionDetailRecord` đủ 3 phần | IT-01 |
| `GetDetail_Unknown_ReturnsNull` | Id không có | `null` | IT-01 |
| `Store_CancelledToken_PropagatesOperationCanceled` | Token đã hủy | `OperationCanceledException` | UT-08 |

**Nghiệm thu:** mọi test IT trên Testcontainers xanh; tuyên bố "không có method sửa/xóa" được khóa bằng test phản chiếu trên `IDecisionAuditStore`; commit đề xuất `feat(persistence): ef decision audit store with postgres error translation`.

---

### Task 7: `DecisionQueryService` — chi tiết quyết định

**Files:**
- Create: `Services/IDecisionQueryService.cs`, `Services/DecisionQueryService.cs`, `Middlewares/DecisionNotFoundException.cs`; `docs/contracts/schemas/v2/decision-detail.schema.json` (C-13).
- Modify: `Middlewares/ExceptionClassifier.cs` (thêm `DecisionNotFoundException` → 404 `DECISION_NOT_FOUND`, ExposeMessage có); `Configuration/DecisionRuntimeServiceCollectionExtensions.cs` (đăng ký scoped).
- Test: `tests/TADSS.UnitTests/Services/DecisionQueryServiceTests.cs`; test schema ở `tests/TADSS.UnitTests/Contracts/DecisionDetailSchemaTests.cs`; cập nhật test classifier (UT-08).

**Interfaces:**
- Consumes: `IDecisionAuditStore.GetDecisionDetailAsync`, `TadssJson.Options`, `DecisionDetailDto`, `DecisionDetailResultDto`, `ExecutionDetailDto`, `OverrideResponseDto`, `OutcomeResponseDto`, `DecisionRequestDto`, `DecisionResultDto`.
- Produces: `IDecisionQueryService.GetDetailAsync(Guid decisionId, CancellationToken cancellationToken) : Task<DecisionDetailDto>` — ném `DecisionNotFoundException(Guid decisionId)` khi không có; `DecisionQueryService(IDecisionAuditStore store)`.

**Yêu cầu:**
1. Dựng `DecisionDetailDto` đúng INTEGRATION_SPEC §3.4: `request` = deserialize `RequestPayload` bằng `TadssJson.Options`; `result` lấy `status`, `deposit`, `reasonCode`, `reasons`, `recommendations`, `approvalExpiresAt` từ **bản ghi/response payload đã lưu** và `strategyStatus` từ cột `strategy_status`; `execution` từ các cột telemetry; `override`/`outcome` từ bản ghi tương ứng hoặc `null`; `createdAt` offset 0; `correlationId` từ cột (có thể `null`). Không chạy lại strategy, không tính lại gì.
2. Không đọc đồng hồ; không ghi gì. Payload hỏng/không giải mã được ⇒ để `JsonException` lan ra (thành 500) — không che lỗi dữ liệu.
3. Không gọi `TadssDbContext`; chỉ qua store.
4. `decision-detail.schema.json`: viết tay đúng §3.4 (`additionalProperties: false`, `override`/`outcome` nullable theo hình dạng §3.6/§3.7); thêm vào bộ test schema (positive: ví dụ §3.4; negative: trường thừa, thiếu trường, `override` sai hình dạng).

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `GetDetailAsync_ExistingDecision_MapsEveryField` | Bản ghi đủ trường | DTO khớp từng trường (kể cả `strategyStatus`, `failureReason`, `cacheHit`) | UT-11 |
| `GetDetailAsync_WindowUnavailable_ShowsStrategyStatusSeparately` | `status = APPROVED`, `strategy_status = NEED_APPROVAL` | `result.status` APPROVED, `strategyStatus` NEED_APPROVAL | UT-11 |
| `GetDetailAsync_WithOverrideAndOutcome_MapsBoth` | Có override + outcome | `override`/`outcome` đúng hình dạng §3.6/§3.7 | UT-11 |
| `GetDetailAsync_Unknown_ThrowsDecisionNotFound` | Store trả `null` | `DecisionNotFoundException` | UT-11 |
| `GetDetailAsync_ResultSerializesValidAgainstDetailSchema` | DTO → `TadssJson` | Hợp lệ theo `decision-detail.schema.json` | CT-01 |
| `Classify_DecisionNotFound_Is404` | Exception mới | 404 `DECISION_NOT_FOUND`, ExposeMessage | UT-08 |
| `DetailSchema_NegativeCases_Rejected_Theory` | Trường thừa/thiếu/sai kiểu | Không hợp lệ | CT-01 |

**Nghiệm thu:** commit đề xuất `feat(runtime): decision query service and decision-detail schema`.

---

### Task 8: `DecisionsController` v2 và nối HTTP

**Files:**
- Create: `Controllers/DecisionsController.cs`.
- Modify: `Configuration/DependencyInjection.cs` (`AddInfrastructureServices`: `AddDbContext`, health check, `AddDecisionRuntime`; `AddApiServices`: JSON v2, ApiBehavior, Swagger); `Configuration/RequiredHeaderOperationFilter.cs`; `Program.cs` (thứ tự middleware giữ: correlation id → exception handler → …); `src/TADSS.Runtime.Api/TADSS.Runtime.Api.http` (ví dụ gọi v2); `Common/HttpHeaderNames.cs` (bỏ `DecisionId` nếu không còn dùng).
- Test: `tests/TADSS.UnitTests/Controllers/DecisionsControllerTests.cs`; `tests/TADSS.UnitTests/Configuration/HttpPipelineTests.cs` (host thử nghiệm không DB); cập nhật test cấu hình DI.

**Interfaces:**
- Consumes: `IDecisionService.EvaluateAsync(DecisionRequestDto, string idempotencyKey, string? correlationId, CancellationToken)`, `IDecisionQueryService.GetDetailAsync`, `TadssJson.Apply(JsonSerializerOptions)`.
- Produces: `POST api/v2/decisions` → `200 DecisionResultDto`; `GET api/v2/decisions/{decisionId:guid}` → `200 DecisionDetailDto`. `DecisionsController(IDecisionService, IDecisionQueryService)`.

**Yêu cầu:**
1. **Controller chỉ chuyển tiếp** (spec 4.2.3): `[ApiController]`, route `api/v2/decisions`; `POST` đọc header `X-Idempotency-Key` — thiếu/trống ⇒ ném `ContractValidationException(MISSING_HEADER, …, "X-Idempotency-Key")`; dài > 128 ⇒ `ContractValidationException(VALIDATION_ERROR, …)`; truyền `HttpContext.TraceIdentifier` làm `correlationId` (đã được middleware chuẩn hóa, C-07). Mọi lỗi trả qua `GlobalExceptionHandler` (không tự tạo `ProblemDetails` trong controller). Không thêm header `X-Decision-ID` (C-06). Không có endpoint nào khác (pending/override/outcome thuộc 5.3).
2. **JSON v2:** gỡ `JsonStringEnumConverter` khỏi `AddJsonOptions` và dùng `TadssJson.Apply` (đã ghi ở spec 4.2.0: `Apply` ném lỗi nếu gặp converter ngoài). Đọc và ghi body **chỉ** bằng cấu hình đó (camelCase, enum chặt, thời gian bắt buộc offset, trường lạ bị từ chối). `PropertyNameCaseInsensitive` không bật.
3. **Lỗi giải mã body → 400 `VALIDATION_ERROR` RFC 7807** qua đường duy nhất (`ConfigureApiBehaviorOptions.InvalidModelStateResponseFactory` hoặc formatter riêng — người hiện thực chọn, miễn mọi trường hợp dưới cho cùng một dạng response): JSON hỏng, body rỗng, thiếu trường bắt buộc, trường lạ, enum sai (hoa/thường, số), thời gian thiếu offset, kiểu sai, `X-Idempotency-Key` sai. Content-Type khác JSON ⇒ 415 theo mặc định MVC được phép (không thuộc 13 mã; chấp nhận, ghi vào báo cáo) hoặc 400 — chọn một và khóa bằng test.
4. `Program.cs`/DI: `ValidateOnBuild` không được làm app Development vỡ; `IDecisionService`/`IDecisionQueryService`/`IDecisionAuditStore` đăng ký scoped; strategies/pipeline/cache giữ singleton. `Tadss:Strategy:ActiveStrategyName` sai ⇒ không khởi động (đã có).
5. Swagger (Development): tài liệu `v2` (title/description cập nhật), `RequiredHeaderOperationFilter` mô tả header `X-Idempotency-Key` (bắt buộc ở POST), `X-Correlation-ID` (tùy chọn); `ProducesResponseType` đủ 200/400/404/409/503/504/500 (`ProblemDetails`).
6. Cập nhật file `.http` mẫu cho v2.

**Test cases:** (host thử nghiệm in-process **không** cần PostgreSQL/Redis: thay `IDecisionService`/`IDecisionQueryService` bằng double)

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Post_ValidRequest_ForwardsToServiceAndReturns200` | Request mẫu hợp lệ + key | 200, body = `DecisionResultDto` serialize bằng `TadssJson`; service nhận đúng key và correlation id | UT-11 |
| `Post_MissingOrBlankIdempotencyKey_Is400MissingHeader_Theory` | Không header / rỗng / khoảng trắng | 400 `MISSING_HEADER`, `errors["X-Idempotency-Key"]` | UT-08 |
| `Post_IdempotencyKeyOver128Chars_Is400ValidationError_And128Succeeds` | 129 / 128 ký tự | 400 `VALIDATION_ERROR` / 200 | UT-08 |
| `Post_MalformedBody_Is400ValidationError_Theory` | Bảng ca lấy từ `RequestSchemaNegativeCases` (JSON hỏng, rỗng, trường lạ, enum số/chữ thường, thiếu offset, thiếu trường) | 400 `VALIDATION_ERROR` problem+json, `traceId` | UT-02 |
| `Get_ExistingDecision_Returns200WithDetail` | Double trả DTO | 200 + body `TadssJson` | UT-11 |
| `Get_Unknown_Is404DecisionNotFound` | Double ném `DecisionNotFoundException` | 404 `DECISION_NOT_FOUND` | UT-08 |
| `Get_InvalidGuidRoute_Is404OrBadRequest` | `/decisions/abc` | Theo hành vi route `:guid` (khóa bằng test) | UT-08 |
| `Pipeline_CorrelationIdEchoedOnSuccessAndError` | Header `X-Correlation-ID` | Echo ở response header và `traceId` của problem+json | UT-08 |
| `JsonOptions_DoNotContainJsonStringEnumConverter` | Resolve `JsonOptions` của MVC | Không có converter ngoài; `TadssJson.Apply` không ném | UT-02 |
| `Routes_OnlyV2DecisionEndpointsExist` | Liệt kê endpoint | Đúng 2 endpoint; không có `/api/v1/*`, pending, override, outcome | UT-09 |

**Nghiệm thu:** build 0/0; unit test xanh; app chạy với cấu hình mặc định (Testing) và Swagger sinh được tài liệu; commit đề xuất `feat(api): v2 decisions endpoints`. (Kiểm thử với PostgreSQL/Redis thật ở Task 9.)

---

### Task 9: Integration test v2 (IT-01…05, IT-13, IT-15, CT-01)

**Files:**
- Create/Rewrite: `tests/TADSS.IntegrationTests/{DecisionsEndpointTests,IdempotencyIntegrationTests,CacheIntegrationTests,HttpErrorMappingTests,DecisionDetailEndpointTests}.cs`; helper `Infrastructure/{ResponseSchemaAssert,DecisionRequests}.cs`.
- Modify: `TADSS.IntegrationTests.csproj` (`Compile Link` tới `ContractSchemas`/`RepositoryPaths` + `JsonSchema.Net`, C-11); `Infrastructure/{TadssInfrastructureFixture,IntegrationTestBase,TadssWebApplicationFactory}.cs` (flush Redis giữa test; tắt `EnableFallbackToMemory` ở Testing); `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md` nếu mô tả IT-04/IT-05/IT-13 lệch hiện thực.
- Delete: các file IT v1 còn sót (Phụ lục A, nhóm "Task 9" — thực tế đã bị xóa ở Task 2).

**Interfaces:**
- Consumes: toàn bộ API v2, fixture Testcontainers hiện có, `ContractSchemas.Evaluate`.
- Produces: bộ IT v2 chạy trên container thật; helper `ResponseSchemaAssert.IsValid…` dùng bởi Task 10.

**Yêu cầu:**
1. **Mọi response 200 trong IT** đi qua helper kiểm tra schema (`decision-result` cho POST, `decision-detail` cho GET) — đây là CT-01; test không gọi helper không được tính CT-01.
2. **Flush Redis giữa test:** `IntegrationTestBase.InitializeAsync` làm sạch Redis của container (container chỉ phục vụ test, được phép flush) để cache không rò rỉ giữa test.
3. **IT-13 trên Redis thật:** môi trường Testing đặt `Tadss:Cache:EnableFallbackToMemory = false`, nên `CACHE_HIT` chỉ có thể đến từ Redis; kiểm tra trực tiếp key `tadss:v2:…` tồn tại trong Redis và giá trị là `StrategyDecision` (không phải response, không chứa `decisionId`).
4. **IT-04:** 50 request song song cùng key/payload qua HTTP (không gọi store trực tiếp).
5. Dùng strategy có `CachePolicy` thật (Default bật cache 60 phút); ca "strategy tắt cache" dùng host test thay registry bằng strategy `Disabled` (double chỉ trong test, C-10).
6. Kiểm tra DB bằng `DbContext` đọc trực tiếp (không qua API) cho số bản ghi và nội dung snapshot.
7. Không có test nào phụ thuộc thứ tự chạy hay dữ liệu của test khác; mỗi test có DB riêng (template) như hiện nay.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `PostDecision_ValidRequest_Returns200AndPersistsOneRecordWithRequestSnapshot` | Request mẫu hợp lệ | 200; đúng 1 hàng `decision_executions` (`role PRIMARY`, `request_payload` = request chuẩn hóa, `response_payload` = response); response hợp lệ theo schema; `X-Correlation-ID` echo | IT-01 |
| `GetDecision_ExistingDecision_ReturnsDetailValidAgainstSchema` | POST rồi GET | 200; `override = null`, `outcome = null`; hợp lệ `decision-detail` | IT-01 |
| `GetDecision_UnknownId_Returns404DecisionNotFound` | Guid ngẫu nhiên | 404 problem+json `DECISION_NOT_FOUND` | IT-01 |
| `PostDecision_WindowUnavailableRequest_PersistsStrategyStatusSeparately` | Start gần hơn `MinLeadMinutes` + strategy NEED_APPROVAL | Response APPROVED 100%; cột `strategy_status = NEED_APPROVAL`; GET hiển thị cả hai | IT-01 |
| `PostDecision_SameKeySamePayload_ReplaysStoredResponseWithoutRerunningStrategy` | Hai lần cùng key/payload; registry đếm lời gọi | Body hai lần **giống từng byte**; 1 hàng; strategy chạy 1 lần | IT-02 |
| `PostDecision_SameKeyOnlyDemandRequestedAtMetadataChanged_Replays` | Đổi `demand`/`requestedAt`/`metadata` | Replay, không 409 | IT-02 |
| `PostDecision_SameKeyDifferentPayload_Returns409IdempotencyKeyReuse` | Đổi `totalPrice` | 409 `IDEMPOTENCY_KEY_REUSE`; vẫn 1 hàng | IT-03 |
| `PostDecision_FiftyParallelSameKey_OneRecordAndIdenticalResponses` | 50 request song song | Đúng 1 hàng; 50 response giống nhau (cùng `decisionId`) | IT-04 |
| `PostDecision_DifferentKeysSamePayload_SecondIsCacheHitFromRealRedisWithNewDecisionId` | Hai key khác nhau | Lần 2 `CACHE_HIT`, `decisionId` khác; key Redis tồn tại | IT-13 |
| `PostDecision_SameKeySamePayload_ReplayTakesPrecedenceOverCacheHit` | Lần 2 cùng key | Replay lần 1 (`AUTOMATED`), không `CACHE_HIT` | IT-13 |
| `PostDecision_StrategyWithCacheDisabled_NeverHitsCache` | Registry thay bằng strategy `Disabled` | Không có key Redis; không bao giờ `CACHE_HIT` | IT-13 |
| `PostDecision_CacheHitRecomputesApprovalWindow` | NEED_APPROVAL từ cache, hai thời điểm khác | `approvalExpiresAt` theo `now` từng lần | IT-13 |
| `PostDecision_MissingIdempotencyKey_Returns400MissingHeader` | Không header | 400 `MISSING_HEADER`, không có bản ghi | UT-08 |
| `PostDecision_KeyOver128Chars_Returns400_While128Succeeds` | 129/128 | 400 / 200 | UT-08 |
| `PostDecision_InvalidBody_Returns400ValidationError_Theory` | Bảng ca từ `RequestSchemaNegativeCases` | 400 `VALIDATION_ERROR`; không bản ghi | UT-02 |
| `PostDecision_DomainInvariantViolation_Returns400DomainInvariant` | Item 15 phút (P-01) / `endTime <= startTime` | 400 `DOMAIN_INVARIANT_VIOLATION`; không bản ghi, strategy không chạy | UT-08 |
| `PostDecision_CorrelationIdOver128_ReplacedAndPersistedWithinColumnLimit` | Header 200 ký tự | 200; `correlation_id` trong DB = GUID mới ≤ 128 ký tự; không 500 | UT-08 |

IT-05, IT-15 đã có ở Task 5; IT-04 mức store ở Task 6.

**Nghiệm thu:** toàn bộ IT v2 xanh khi chạy thật trên Docker (ghi số lượng và thời gian); mọi response 200 đã qua schema (CT-01: 0 vi phạm); không còn test v1; commit đề xuất `test(it): v2 integration tests on real postgres and redis`.

---

### Task 10: Fault injection v2 (FI-01…04, FI-06, FI-08, FI-10)

**Files:**
- Rewrite: `tests/TADSS.IntegrationTests/FaultInjection/FaultInjectionTests.cs`, `FaultInjection/PersistenceFaultInjectionTests.cs`; helper test double dưới `tests/TADSS.IntegrationTests/Infrastructure/` (strategy giả, registry thay thế, cache hỏng, store có điểm cắt).
- Modify: `docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md` (chỉ nếu số/nhãn lệch hiện thực — F-ID ↔ FI-ID).

**Interfaces:**
- Consumes: `IStrategyRegistry`, `RegisteredStrategy`, `IStrategyDecisionCache`, `IDecisionAuditStore`, `ToxiproxyControl`, `ResilienceOptions`, `ResilienceOptionsValidator`.
- Produces: test chạy trên host thật có thay thế DI **chỉ trong host test** (C-10).

**Yêu cầu:**
1. Chuyển F1–F5, F7 sang v2 giữ **ý nghĩa** từng kịch bản (RESILIENCE_AND_FAULT_TAXONOMY): strategy giả là strategy v2 thật (`IReservationDecisionStrategy` v2) được bọc thành `RegisteredStrategy` do registry giả trả về. Cấu hình `Tadss:Resilience` của host test phải qua `ResilienceOptionsValidator` (ngân sách thời gian — đã từng buộc F7 phải nâng `OuterTimeoutMs` lên 7000; giữ cấu hình hiện có nếu còn hợp lệ).
2. F6 (SaveChanges ném `DbUpdateException` → 500) **không chuyển**; thay bằng hai test persistence ở bảng dưới, và ghi trong báo cáo: lỗi non-transient của store → 500 được phủ bởi test store (Task 6) + classifier (UT-08).
3. FI-08 hiện có trên Toxiproxy (kết nối PostgreSQL bị ngắt) chuyển sang payload v2, giữ khẳng định 503 `PERSISTENCE_UNAVAILABLE` + header `Retry-After` + không bản ghi dở dang. **Biến thể mới:** cắt kết nối **đúng lúc ghi audit** (sau khi strategy đã chạy xong: bọc `IDecisionAuditStore` bằng double kích hoạt Toxiproxy ngay trước `InsertExecutionAsync`) ⇒ 503, 0 hàng, strategy đã chạy, client không nhận quyết định (INV-10 trên đường ghi).
4. Không trùng `CatalogId` giữa hai test khác nhau mà cùng mô tả một kịch bản; test biến thể dùng `FI-08` kèm hậu tố trong tên.
5. F5 (Redis hỏng): cache double ném ở cả `GetAsync`/`SetAsync` ⇒ quyết định vẫn `AUTOMATED`, 200, ghi audit; không fallback strategy (FI-06 phần cô lập lỗi; breaker Redis và Toxiproxy thuộc 5.4–5.5).
6. F7 (client hủy giữa chừng): không tạo bản ghi audit; không treo.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `F1_PerAttemptTimeoutInBudget_RetriesAndReturnsAutomated` | Lần đầu chậm hơn inner timeout, lần hai nhanh | `AUTOMATED`, không fallback, strategy gọi 2 lần | FI-01 |
| `F2_AlwaysSlowStrategy_FallsBackWithTimeoutReason` | Strategy luôn chậm | `NEED_APPROVAL`, `FALLBACK`, `FALLBACK_STRATEGY_TIMEOUT`, tổng thời gian ≤ 500 ms (+ dung sai ghi rõ) | FI-02 |
| `F3_StrategyThrows_FallsBackWithExceptionReason` | Strategy ném | Fallback `FALLBACK_STRATEGY_EXCEPTION`; message gốc không lộ ra response | FI-03 |
| `F4_RepeatedFailures_OpensBreakerAndFastFails_ThenRecovers` | Lỗi liên tục rồi hồi phục sau `BreakDuration` | Breaker mở (fast-fail); half-open → closed khi strategy tốt | FI-04 |
| `F5_RedisDown_CacheErrorsAreMissAndRequestSucceeds` | Cache double ném cả Get/Set | 200 `AUTOMATED`; có bản ghi audit | FI-06 |
| `F7_CallerCancellation_AbortsWithoutAuditRecord` | Hủy giữa chừng | Không bản ghi; yêu cầu kết thúc nhanh | FI-10 |
| `PostDecision_PostgresConnectionLost_Returns503WithRetryAfterAndNoRecord` | Toxiproxy ngắt trước khi xử lý | 503 `PERSISTENCE_UNAVAILABLE`, `Retry-After: 5`, 0 hàng | FI-08 |
| `PostDecision_PostgresCutDuringAuditInsert_Returns503AndNoRecord` | Cắt đúng lúc `InsertExecutionAsync` | 503, 0 hàng, strategy đã chạy, không trả quyết định | FI-08 |

**Nghiệm thu:** toàn bộ test trên xanh khi chạy thật (ghi số lượng); không còn tham chiếu kiểu v1; FI-05 (strategy không hợp tác), FI-07, FI-09 **không** thuộc gói này (5.4–5.5); commit đề xuất `test(fi): port fault injection matrix to v2`.

---

### Task 11: `openapi-v2.yaml` và kiểm tra lệch

**Files:**
- Create: `docs/contracts/openapi-v2.yaml`; test `tests/TADSS.IntegrationTests/Contracts/OpenApiDriftTests.cs` (không cần container: host Testing với chuỗi kết nối giả, chỉ gọi `ISwaggerProvider`) hoặc đặt ở UnitTests nếu khả thi.
- Modify: cấu hình Swagger (`AddApiServices`) để sinh ổn định (thứ tự, server, version `2.0.0-draft`); `docs/contracts/schemas/README.md` (mô tả cặp schema v2).

**Interfaces:**
- Produces: `openapi-v2.yaml` sinh từ Swashbuckle của chính app (spec 4.2.1): 2 operation (`POST /api/v2/decisions`, `GET /api/v2/decisions/{decisionId}`).

**Yêu cầu:**
1. File YAML được **sinh** từ app (không viết tay): dùng writer YAML của `Microsoft.OpenApi` (đi kèm Swashbuckle). Cách sinh lại ghi trong một dòng hướng dẫn ở đầu test (không thêm script/tool mới).
2. Test drift: sinh tài liệu từ app, so với file đã commit sau chuẩn hóa xuống dòng; lệch ⇒ ghi bản thực tế ra `TestResults/openapi-v2.actual.yaml` rồi **fail** (giống cơ chế baseline; không bao giờ tự ghi đè file chuẩn).
3. Nội dung tối thiểu cần có: `X-Idempotency-Key` (required, POST), `X-Correlation-ID` (optional), schema request/response từ DTO v2 (enum dạng chuỗi tên, `additionalProperties: false` nếu Swashbuckle biểu diễn được; nếu không, ghi sai lệch vào báo cáo — schema JSON viết tay vẫn là nguồn chuẩn), response lỗi `application/problem+json` có thuộc tính `code`; **không** có operation v1, pending, override, outcome.
4. Tài liệu `openapi-v1.yaml` và schema v1 giữ nguyên làm hồ sơ lịch sử (đã có banner "đóng băng/hồ sơ").

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `OpenApi_GeneratedDocument_MatchesCommittedFile` | Sinh từ app vs `openapi-v2.yaml` | Khớp (sau chuẩn hóa) | CT-01 |
| `OpenApi_ContainsOnlyV2DecisionOperations` | Duyệt `paths` | Đúng 2 operation, base path `/api/v2` | CT-01 |
| `OpenApi_DocumentsRequiredHeadersAndProblemResponses` | Tham số + response | Header đúng; có 400/404/409/503/504/500 problem+json | CT-01 |

**Nghiệm thu:** file YAML được commit; test drift xanh; commit đề xuất `docs(contract): openapi v2 draft generated from the app`.

---

### Task 12: Đồng bộ tài liệu và kiểm chứng cuối gói

**Files:**
- Modify: `docs/contracts/INTEGRATION_SPEC.md` (§3.2 `X-Correlation-ID` ≤ 128 ký tự, dài hơn thì server cấp mới — C-07; §3.1 bỏ ghi chú "khi hiện thực T5.1"; ghi chú không xác thực ở 5.2c — C-14); `docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md` (Mục A: v1 đã gỡ; Mục B: đã hiện thực ở 5.2c + ghi chú `numeric(5,4)`/µs); `docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md` (Mục 4.2 trạng thái: v2 là duy nhất); `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md` (trạng thái IT/FI nếu có cột trạng thái); `docs/planning/BACKLOG_SPRINTS.md` (T5.1–T5.4, T5.7 phần map lỗi, T5.8 — cập nhật theo thực tế); `CLAUDE.md`, `README.md` (bỏ BenchmarkRunner/InMemory/v1; cây thư mục mới; lệnh); `../.claude/skills/tadss-architecture/SKILL.md` (bỏ mô tả v1/V2; vị trí đích; 5.2d sẽ viết lại đầy đủ); `spec/PHASE5_DESIGN_SPEC.md` (chỉ ghi chú "đã hoàn thành" nếu người dùng muốn — mặc định **không** sửa spec).
- Modify (nếu cần): `.github/workflows/ci.yml` (không còn tham chiếu BenchmarkRunner/IT v1), `Dockerfile` (không còn csproj đã xóa), `docker-compose.yml` (giữ nếu vẫn đúng).

**Yêu cầu:**
1. Tài liệu chỉ ghi điều **đã hiện thực**; sửa ở file sở hữu; không chép lại spec.
2. Kiểm tra link như gói 5.2a/5.2b (vòng `for` trên `docs/planning/BACKLOG_SPRINTS.md docs/contracts/INTEGRATION_SPEC.md spec/*.md`) → chỉ in `done`.
3. **Kiểm chứng cuối gói** (ghi số liệu thật vào báo cáo):
   - `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror --no-incremental` → 0/0.
   - `dotnet test tests/TADSS.UnitTests -c Release --no-build -nologo` → xanh toàn bộ (ghi số; so sánh số test v2 với trước Task 2).
   - **Bật Docker** rồi `dotnet test tests/TADSS.IntegrationTests -c Release --no-build -nologo` → xanh toàn bộ (ghi số lượng theo CatalogId: IT-01…05, IT-13, IT-15, FI-01…04, 06, 08, 10, UT-09 guard…).
   - `git diff --stat da48529 -- docs/contracts/baseline_public_api.txt` → đúng bản baseline nháp v2 (commit riêng ở Task 3), không thêm sửa nào khác.
   - Không còn từ khóa v1: `grep -ri "api/v1" src tests` rỗng (trừ nội dung mô tả lịch sử trong docs); không còn `MediatR`, `InMemory`, `BenchmarkRunner` trong csproj/slnx/CI.
   - Khởi động app bằng cấu hình mặc định (Production hoặc Testing, ≤ 20 giây, cổng trống): không lỗi validate options; Swagger (Development) sinh được; thử `GET /health/live`.
   - `docker build` (nếu Docker bật) thành công với Dockerfile hiện tại; `docker compose config` hợp lệ.
4. **Báo cáo gói**: bảng 6.1 phần 5.2c (bất biến → test khóa → trạng thái), danh sách `CatalogId` đã phủ (UT-01, 02, 05, 08, 09, 10, 11, 13, CT-01, CT-02, IT-01…05, IT-13, IT-15, FI-01…04, 06, 08, 10), danh sách việc người dùng cần làm (review baseline, review migration, review openapi, áp migration lên Neon), sai lệch từng task, các mục chuyển sang 5.2d/5.3.
5. Đề xuất commit `docs: cutover to v2 complete (package 5.2c)` và đề xuất PR vào `develop` (người dùng tạo/merge, CI xanh).

**Nghiệm thu:** mọi bất biến 6.1 của 5.1–5.2 có test khóa đang xanh; không còn kiểu/endpoint/dependency v1; baseline nháp v2; `openapi-v2.yaml`; tài liệu khớp mã.

---

## Phụ lục A — Số phận test và file v1

| Nhóm v1 | Số phận | Task | Thay bởi |
| :--- | :--- | :---: | :--- |
| `UnitTests/Benchmarks/*`, `BenchmarkRunner/*` | Xóa (D-09) | 2 | — |
| `UnitTests/Consumer/SmashCourtConsumerReferenceTests` | Xóa (CT-04 ở SmashCourt-BE) | 2 | — |
| `UnitTests/Services/{DecisionService*,StrategyResolver*,RequestHasher*,CrossCulture*}` | Xóa | 2 | `Services/DecisionService*`, `RequestHasher*`, `StrategyRegistry*` (v2) |
| `UnitTests/Infrastructure/Caching/DecisionCacheTests` | Xóa | 2 | `Infrastructure/Caching/StrategyDecisionCache*` |
| `UnitTests/Infrastructure/Resilience/*` (2 file) | Xóa; phần cấu hình đã có `ResilienceOptionsValidatorTests`, `ResilienceStartupTests` | 2 | `Infrastructure/Resilience/*` |
| `UnitTests/Infrastructure/MiddlewareAndInterceptorTests` | Handler: thay ở Task 2; interceptor: xóa ở Task 5; `CorrelationIdMiddleware`: giữ | 2, 5 | Handler tests mới; Correlation tests |
| `UnitTests/Contracts/*` v1 (8 file), `Domain/*` v1 (7), `Strategies/*` v1 (2), `TestData/{DecisionRequestFactory,ValidationHelper}` | Xóa | 3 | Test v2 đã có (UT-01…03, CT-01) |
| `UnitTests/Architecture/{SdkDependencyPurity,StrategyDependencyPurity}` | Giữ | — | — |
| `UnitTests/Architecture/PublicApiSurfaceTests` | Giữ cơ chế; baseline nháp v2 | 3 | — |
| `IntegrationTests/{DecisionsEndpointTests,IdempotencyIntegrationTests,CacheIntegrationTests,GovernanceE2ETests}` | Xóa ở Task 2 (không biên dịch được sau khi gỡ v1) rồi viết lại v2; `GovernanceE2ETests` (IT-06, IT-09 override/hàng chờ): **không** viết lại — quay lại ở 5.3; các ca correlation id/404 chuyển vào `DecisionsEndpointTests` v2 | 2 → 9 | IT v2 (Task 9) |
| `IntegrationTests/FaultInjection/*` | Xóa ở Task 2 rồi viết lại | 2 → 10 | FI v2 (Task 10) |
| `IntegrationTests/{Infrastructure/*,Migrations/*,TadssWebApplicationFactory}` | Giữ; sửa theo Task 5/9 | 5, 9 | — |

Khoảng trống tạm thời: từ Task 2 đến Task 9 bộ IT chỉ còn guard + migration (cùng lúc app không có endpoint); từ 5.2c đến 5.3 không có override/hàng chờ (IT-06…IT-10 quay lại ở 5.3).

## Ghi chú cho gói 5.2d và 5.3 (không làm ở 5.2c)

- **5.2d:** kiểm toán SOLID, tách `DecisionService` phần truy cập cache, chia `IDecisionAuditStore` theo use case (hiện có 3 method), gom `Options` về `Configuration/`, gom exception (hiện `Middlewares/`), đăng ký DI theo nhóm, xóa tên kiểu chứa "V2" còn lại (`V2Samples`…), UT-14, viết lại skill + `CLAUDE.md`.
- **5.3:** `X-Api-Key`, `X-Actor-Id`, override/outcome/hàng chờ (`DecisionStateConflictException`, store: `GetPendingApprovalsAsync`, `InsertOverrideAsync`, `InsertOutcomeAsync`), `DepositCalculator` quy đổi `amount → rate`; expose ngrok chỉ sau khi có xác thực (C-14).
- **5.4:** breaker + metric `bypass` cho Redis (cache ném lỗi liên tục ở FI-06), `IsolatedStrategyInvoker`, router thay `ResolveActive`, shadow; cache key chưa gồm `demand`/`requestedAt` ⇒ strategy nhạy với chúng phải tắt cache hoặc mở rộng key; warm-up Redis lần resolve đầu; chạy CI một lần với múi giờ `Asia/Ho_Chi_Minh` cho UT-06.
- **Từ review cuối 5.2b còn mở sau 5.2c:** log đầy đủ exception của cache có thể chứa giá trị payload thù địch (xét lại ở 5.4 cùng breaker).
