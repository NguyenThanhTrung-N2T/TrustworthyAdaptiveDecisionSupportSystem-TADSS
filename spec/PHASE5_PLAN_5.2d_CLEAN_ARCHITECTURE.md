# Gói 5.2d — Clean Architecture và SOLID: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tách `TADSS.Runtime.Api` thành ba project theo Clean Architecture: `TADSS.Application` (use case + port), `TADSS.Infrastructure` (adapter) và `TADSS.Runtime.Api` (presentation + composition root). Phụ thuộc phải hướng vào trong và được compiler cùng test kiến trúc UT-14 khóa. Gói làm sạch mã theo SOLID, bỏ hardcode, gom exception và options. Hành vi, wire, schema, mã lỗi, migration ID và bề mặt public của SDK đều giữ nguyên.

**Architecture:** Giữ nguyên `Shared.DecisionContractSDK` (Domain + contract) và `TADSS.Strategies.*` (plugin). Gói đi theo thứ tự sau:

1. Dựng khung project và cây test.
2. Dời lớp use case/port sang Application.
3. Dời adapter sang Infrastructure.
4. Dịch exception hạ tầng ngay tại adapter để tầng HTTP không còn biết Npgsql/Polly.
5. Dựng lại presentation và composition root.
6. Tách trách nhiệm (SRP/ISP/DRY).
7. Bỏ hardcode, đổi tên.
8. Hoàn thiện bộ test kiến trúc.
9. Đồng bộ tài liệu và kiểm chứng cuối.

Mỗi task dời file nằm trong commit riêng, tách khỏi commit đổi mã.

**Tech Stack:**
- Nền tảng: .NET 8, ASP.NET Core 8, EF Core 8 + Npgsql 8, StackExchange.Redis 2.8.16, Polly.Core 8.4.2, Swashbuckle 6.6.2.
- Gói mới cho thư viện lớp: `Microsoft.Extensions.*` 8.0.x (Logging.Abstractions, Options.ConfigurationExtensions, Caching.Memory, Diagnostics.HealthChecks).
- Kiểm thử: Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy), xUnit 2.5.3, FluentAssertions 6.12.1.

**Spec:** [`PHASE5_DESIGN_SPEC.md`](PHASE5_DESIGN_SPEC.md):
- Mục 1.2 (mục tiêu, P-03).
- Mục 4.2.4 (gói 5.2d: solution đích, quy tắc phụ thuộc, bảng ánh xạ, nguyên tắc mã, UT-14, việc mang từ 5.2c, ràng buộc).
- Mục 6, 6.1, 9.

Nguồn sở hữu:
- [`SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](../docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md) Mục 2, 4.4, 5.
- [`INTEGRATION_SPEC.md`](../docs/contracts/INTEGRATION_SPEC.md) §3.2, §3.8.
- [`DECISION_PERSISTENCE_INVARIANTS.md`](../docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md) Mục A–B.
- [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) (UT-14).

Gói trước: [`PHASE5_PLAN_5.2c_CUTOVER_TO_V2.md`](PHASE5_PLAN_5.2c_CUTOVER_TO_V2.md).

> **Cách đọc plan này:**
>
> - Plan mô tả **yêu cầu kỹ thuật**: trách nhiệm, vị trí, chữ ký, quy tắc, bảng test case và tiêu chí nghiệm thu. Plan **không** chứa mã hiện thực.
> - Mục **Interfaces** là ràng buộc vì task sau dựa vào đúng tên và kiểu đó.
> - Đường dẫn viết từ gốc repo. Tên kiểu và namespace viết theo **trạng thái sau task đó**.
> - Phụ lục A là kết quả kiểm toán SOLID/hardcode (việc 1 của spec 4.2.4). Mỗi dòng ghi task xử lý.

## Global Constraints

**Build và test sau mỗi task:**
- `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror` → **0 warning, 0 error**.
- Unit test và **toàn bộ** integration test xanh (Docker bật). Gói chủ yếu dời mã nên IT là lưới an toàn bắt buộc ở mọi task.

**Không đổi hành vi:**
- Wire, JSON schema, mã lỗi và status HTTP giữ nguyên. Cùng request phải cho cùng response và cùng bản ghi audit như trước.
- `docs/contracts/openapi-v2.yaml` không đổi: `OpenApiDriftTests` phải xanh **mà không sinh lại file**. Nếu YAML lệch chỉ vì XML doc của kiểu đổi namespace, sửa XML doc (dùng `<c>` thay cho `<see cref>`), không sinh lại.
- `docs/contracts/baseline_public_api.txt` không đổi; mọi kiểu SDK giữ tên và namespace.
- Migration giữ nguyên **ID và nội dung** của cả 4 migration (`[Migration("…")]` không đổi). **Không** tạo migration mới, **không** `dotnet ef database update`, **không** áp lên Neon. IT-15 (`HasDifferences = false`) phải xanh sau mọi task.
- Khóa cấu hình hiện có và giá trị mặc định không đổi. Được thêm khóa mới nếu mặc định của khóa mới tái tạo đúng hành vi cũ.
- Chỉ được đổi hành vi trong 3 trường hợp, mỗi trường hợp phải ghi vào báo cáo task:
  - (a) Thêm validator cho options chưa có validator: chỉ cấu hình sai mới bị chặn khi khởi động.
  - (b) Category của logger đổi theo namespace mới.
  - (c) Tên health check nội bộ đổi (không lộ ra response).

**Số lượng test:** test hiện có không được mất. Mỗi task ghi vào báo cáo **số test trước/sau** của cả hai project (đếm bằng `dotnet test --list-tests`); số sau phải ≥ số trước. Test bị dời hoặc đổi tên vẫn tính, chỉ test UT-14 mới làm tăng số.

**Cấm nới kiểm tra:** không `Skip`, không nới assertion, không `#pragma warning disable`, không `!` null-forgiving, không `?.Should()`, không xóa test để chữa test đỏ.

**Quy tắc phụ thuộc** (spec 4.2.4, compiler + UT-14):
- SDK chỉ phụ thuộc BCL. Strategies chỉ phụ thuộc SDK.
- `TADSS.Application` được phụ thuộc SDK và gói `Microsoft.Extensions.{DependencyInjection.Abstractions,Logging.Abstractions,Options,Options.ConfigurationExtensions}`. **Cấm** Infrastructure, Runtime.Api, Strategies, ASP.NET Core, EF Core, Npgsql, StackExchange.Redis, Polly.
- `TADSS.Infrastructure` được phụ thuộc Application, SDK, EF Core/Npgsql, StackExchange.Redis, Polly, `Microsoft.Extensions.*`. **Cấm** Runtime.Api và ASP.NET Core.
- `TADSS.Runtime.Api` được phụ thuộc tất cả. Chỉ `Program.cs` và thư mục `Composition/` được dùng kiểu của `TADSS.Infrastructure`.

**Package:**
- SDK và Strategies giữ 0 NuGet (UT-09).
- Package mới chỉ khai báo version ở `Directory.Packages.props`.
- Version `Microsoft.Extensions.*` mới chọn bản 8.0.x **không thấp hơn** bản đang được restore bắc cầu (kiểm bằng `dotnet list <csproj> package --include-transitive`), để tránh NU1605. Ghi version đã chọn vào báo cáo.

**Quy ước:**
- Namespace = tên project + đường dẫn thư mục. Mỗi kiểu public (cấp cao nhất) một file, tên file = tên kiểu.
- Options là `{X}Options` kèm `SectionName = "Tadss:{X}"`, validator `{X}OptionsValidator : IValidateOptions<{X}Options>` và `ValidateOnStart()`.
- Interface của **port** nằm ở `TADSS.Application/Abstractions/**`, hiện thực nằm ở Infrastructure. Interface của service nội bộ Application nằm cùng thư mục với hiện thực.
- Comment và message tiếng Việt như mã hiện có, định danh tiếng Anh. XML doc của SDK và Strategies giữ nguyên.
- Thời gian lấy từ `TimeProvider`. Serialize payload chỉ bằng `TadssJson.Options`.

**Bẫy namespace `Options`** (áp dụng mọi task):
- Gói tạo các namespace `TADSS.Application.Options`, `TADSS.Infrastructure.Options`, `TADSS.Runtime.Api.Options` và namespace test tương ứng (`TADSS.UnitTests.Application.Options`…).
- Bên trong `TADSS.Application.*`, `TADSS.Infrastructure.*`, `TADSS.Runtime.Api.*` và `TADSS.UnitTests.{Application,Infrastructure,Api}.*`, định danh `Options` (ví dụ `Options.Create(...)`) được compiler phân giải thành **namespace con** đó, không phải lớp `Microsoft.Extensions.Options.Options`.
- Phải viết đầy đủ `Microsoft.Extensions.Options.Options.Create(...)` hoặc dùng alias `using MsOptions = Microsoft.Extensions.Options.Options;`.
- Hiện 9 file test gọi `Options.Create`; chúng phải được sửa trong task dời chúng.

**Test:**
- Tên `{Method}_{Scenario}_{Expected}`, có `[Trait("CatalogId", …)]` (IT thêm `[Trait("Category","Integration")]`) và XML summary tiếng Việt.
- Không EF InMemory. Test mới của gói mang `CatalogId = UT-14`, trừ khi ghi khác.

**Git:**
- Stage bằng đường dẫn tường minh; dời file bằng `git mv` để lịch sử theo được.
- **Không** stage `spec/PHASE5_*.md`. Không push/stash/reset/`checkout --`/clean.
- Subject commit theo Conventional Commits, tiếng Anh ASCII.

**Bảo mật:** không đọc/in/sửa `.env`, `appsettings.Development.json`, User Secrets. Lệnh `dotnet ef` (chỉ đọc) chạy với `ASPNETCORE_ENVIRONMENT=Testing`.

**Sai lệch so với giả định:** nếu hành vi .NET/thư viện khác giả định trong plan, sửa tối thiểu để giữ đúng **ý định**, ghi "sai lệch" vào báo cáo task, không nới test.

## Quyết định thiết kế của gói

| Mã | Quyết định | Lý do |
| :--- | :--- | :--- |
| **E-01** | Thứ tự: khung project + cây test SDK/Strategies → dời Application → dời Infrastructure → ranh giới exception → presentation + composition root → SRP/ISP/DRY → hardcode/options/đổi tên → UT-14 đầy đủ → tài liệu + kiểm chứng | Dời trước, đổi mã sau để diff review được. Mỗi bước build + test xanh |
| **E-02** | Application tham chiếu `Microsoft.Extensions.Options.ConfigurationExtensions` (kéo theo `Configuration.Abstractions`/`Binder`) để `AddApplication(IConfiguration)` tự bind options của mình | Đây là abstraction của nền tảng, không phải hạ tầng. Mỗi project tự đăng ký phần của mình (spec 4.2.4) |
| **E-03** | `StrategyRegistrations` (dựng `DefaultDecisionStrategy`/`MockAlternativeDecisionStrategy` từ cấu hình) chuyển vào composition root `TADSS.Runtime.Api/Composition/`. Application chỉ giữ `IStrategyRegistry`, `StrategyRegistry`, `RegisteredStrategy`, `OptionsFingerprintBuilder` | Application bị cấm tham chiếu Strategies. Chọn strategy cụ thể là việc lắp ráp, không phải use case |
| **E-04** | Ranh giới exception: Infrastructure dịch mọi exception hạ tầng ngay tại adapter. `StrategyPipeline` chuyển `TimeoutRejectedException` của outer timeout thành `DecisionTimeoutException` (mới, Application). `PersistenceFailureDetector` thành `internal` của Infrastructure. `ExceptionClassifier` chỉ biết exception của SDK và Application (bỏ Polly và Npgsql khỏi Runtime.Api ngoài composition root). Kết quả HTTP giữ nguyên: 504 `GATEWAY_TIMEOUT` và 503 `PERSISTENCE_UNAVAILABLE` cùng `Retry-After` | Presentation không phụ thuộc chi tiết hạ tầng. Hiện mọi truy cập DB đều đi qua `EfDecisionAuditStore`, vốn đã dịch lỗi tạm thời thành `PersistenceUnavailableException` (FI-08 chứng minh) |
| **E-05** | `CachedStrategyDecision`/`CachedRecommendation` (định dạng lưu trên Redis) thuộc **Infrastructure**. `IStrategyDecisionCache` và `StrategyDecisionCacheKey` thuộc Application. Bảng ánh xạ của spec 4.2.4 ghi `CachedStrategyDecision` ở Application, controller sửa dòng đó trong spec | Định dạng serialize cache là chi tiết của adapter, chỉ `StrategyDecisionCache` dùng |
| **E-06** | Thư mục composition root của Runtime.Api tên `Composition/` (spec ghi `DependencyInjection/`) | Tránh namespace `TADSS.Runtime.Api.DependencyInjection` trùng đoạn cuối với `Microsoft.Extensions.DependencyInjection` |
| **E-07** | Tách port audit theo consumer (ISP): `IDecisionEvaluationStore` (`FindPrimaryByIdempotencyKeyAsync`, `InsertExecutionAsync`; `DecisionService` dùng) và `IDecisionQueryStore` (`GetDecisionDetailAsync`; `DecisionQueryService` dùng). Xóa `IDecisionAuditStore`. Một `EfDecisionAuditStore` (scoped) hiện thực cả hai; hai interface trỏ về cùng instance trong một scope. Gói 5.3 thêm `IDecisionGovernanceStore` | Service chỉ thấy method mình dùng; port không phình khi 5.3 thêm override/outcome/hàng chờ |
| **E-08** | Tách khỏi `DecisionService` (SRP): `StrategyEvaluator` (dựng key cache → đọc cache, cô lập lỗi → pipeline khi miss → ghi cache khi `AUTOMATED`) trả về `StrategyEvaluation`; `DecisionPayloadSerializer` (serialize/deserialize `request_payload`/`response_payload` bằng `TadssJson.Options`), dùng chung cho composer, replay và query (DRY) | `DecisionService` hiện 299 dòng, gánh điều phối + cache + log. Deserialize payload đang lặp ở hai service |
| **E-09** | Đổi tên entity EF thành `DecisionExecutionEntity`, `DecisionOverrideEntity`, `DecisionOutcomeEntity`. Configuration giữ tên `DecisionExecutionConfiguration`… Chuỗi tên kiểu trong `TadssDbContextModelSnapshot`/`*.Designer.cs` sửa theo; ID migration và DDL không đổi | `DecisionOutcome` trùng enum SDK `Enums.DecisionOutcome` (review cuối 5.2c). Hậu tố đồng loạt cho ba entity để nhất quán |
| **E-10** | Bỏ hardcode theo Phụ lục A: `Retry-After` thành `Tadss:ErrorHandling:RetryAfterSeconds` (mặc định 5); timeout kết nối Redis tối thiểu thành `Tadss:Cache:RedisConnectTimeoutMs` (mặc định 3000); giới hạn 128 ký tự của contract gom về `ContractLimits`; tên health check thành hằng `"postgresql"`; xóa option chết `MaxMemoryCacheSize`; thêm validator cho `CacheOptions`, `DatabaseOptions`, `StrategyOptions` | Spec 4.2.4 "không hardcode". Giá trị do contract cố định giữ là hằng, mỗi hằng một nơi. `ContractLimits` đặt ở Application, không ở SDK: thêm kiểu public vào SDK làm đổi baseline `2.0.0-draft`, mà SmashCourt-BE không tham chiếu SDK nên không hưởng lợi. `RetryAfterSeconds ≤ 3600`: client của BE retry trong vòng một booking, chờ quá 1 giờ không còn ý nghĩa; giá trị lớn hơn gần như chắc chắn là cấu hình nhầm đơn vị |
| **E-11** | Đổi tên helper test còn chữ "V2": `V2Samples` → `ContractSamples`, `V2TestData` → `DomainTestData`, `SdkV2LayeringTests` → `SdkLayeringTests`; `TadssCacheOptions` → `CacheOptions` | UT-14: không còn kiểu tên "V2" (trừ class migration `DecisionPersistenceV2`, vì tên gắn với ID) |
| **E-12** | Thứ tự middleware khai báo tại **một** nơi (`RequestPipelineExtensions.UseTadssRequestPipeline`). Test khóa thứ tự bằng **hành vi quan sát được** (correlation id có trên mọi response lỗi; exception handler bọc controller; Swagger chỉ ở Development) | Không có API ổn định để đọc thứ tự middleware bằng reflection. Gói 5.3 chèn middleware bảo mật vào cùng chỗ |

Nếu người dùng không đồng ý mục nào, sửa plan trước khi chạy Task 1.

## Chuẩn bị (người dùng thực hiện)

1. Bật Docker Desktop: mọi task đều chạy integration test.
2. Không có cổng migration: gói không tạo migration. Lệnh `dotnet ef migrations list --no-connect` và `dotnet ef migrations has-pending-model-changes` (chỉ đọc, không kết nối DB) được phép chạy ở Task 3, 7 và 9.
3. Cuối gói review:
   - (a) Cây project mới.
   - (b) Diff `TadssDbContextModelSnapshot`/Designer (chỉ đổi namespace/tên kiểu).
   - (c) Skill `tadss-architecture` viết lại (file ngoài repo, không commit được).

## Cấu trúc đích

```text
src/
  Shared.DecisionContractSDK/                 (không đổi)
  TADSS.Strategies.Default/, .MockAlternative/ (không đổi)
  TADSS.Application/
    ApplicationServiceCollectionExtensions.cs  AddApplication(IServiceCollection, IConfiguration)
    Abstractions/Caching/      IStrategyDecisionCache, StrategyDecisionCacheKey
    Abstractions/Persistence/  IDecisionEvaluationStore, IDecisionQueryStore (Task 6; trước đó IDecisionAuditStore),
                               DecisionExecutionRecord, DecisionDetailRecord, DecisionOverrideRecord, DecisionOutcomeRecord,
                               ExecutionRole, InsertExecutionResult
    Abstractions/Resilience/   IStrategyPipeline, StrategyExecutionOutcome, FailureReasonPrefixes
    Common/                    ContractLimits (Task 7)
    Decisions/                 IDecisionService, DecisionService, IDecisionQueryService, DecisionQueryService,
                               DecisionResponseComposer, DecisionComposition, ComposedDecision, ApprovalWindowPolicy,
                               DepositCalculator, ItemDurationPolicy, RequestHasher, CanonicalNumberFormatter,
                               StrategyEvaluator, StrategyEvaluation, DecisionPayloadSerializer (Task 6)
    Exceptions/                DecisionNotFoundException, IdempotencyConflictException, PersistenceUnavailableException,
                               DecisionTimeoutException (Task 4)
    Options/                   ApprovalOptions(+Validator), ValidationOptions(+Validator), CalendarOptions, PeakWindowOptions,
                               CalendarOptionsValidator, StrategyOptions(+Validator, Task 7)
    Strategies/                IStrategyRegistry, StrategyRegistry, RegisteredStrategy, OptionsFingerprintBuilder
  TADSS.Infrastructure/
    InfrastructureServiceCollectionExtensions.cs  AddInfrastructure(IServiceCollection, IConfiguration)
    Caching/                   StrategyDecisionCache, CachedStrategyDecision, CachedRecommendation, RedisConnectionFactory
    Options/                   DatabaseOptions(+Validator, Task 7), CacheOptions(+Validator, Task 7), ResilienceOptions, ResilienceOptionsValidator
    Persistence/               TadssDbContext, EfDecisionAuditStore, DecisionAuditMapper (internal), PersistenceFailureDetector (internal),
                               PersistenceHealthChecks, Entities/, Configurations/, Migrations/
    Resilience/                StrategyPipeline, FallbackDecisionPolicy, FallbackCause
  TADSS.Runtime.Api/
    Program.cs
    Common/                    HttpHeaderNames
    Composition/               PresentationServiceCollectionExtensions (AddPresentation), StrategyServiceCollectionExtensions (AddStrategies),
                               StrategyRegistrations, RequestPipelineExtensions (UseTadssRequestPipeline), StartupTasks
    Controllers/               DecisionsController
    ErrorHandling/             ExceptionClassifier, ErrorClassification, InvalidModelStateTranslator
    Middlewares/               CorrelationIdMiddleware, GlobalExceptionHandler
    OpenApi/                   RequiredHeaderOperationFilter, ProblemDetailsSchemaFilter, LineEndingDocumentFilter
    Options/                   ErrorHandlingOptions, ErrorHandlingOptionsValidator (Task 7)
tests/TADSS.UnitTests/
  Architecture/  Sdk/{Common,Contracts,Decisions,Domain}/  Strategies/
  Application/{Abstractions,Decisions,Options,Strategies}/  Infrastructure/{Caching,Persistence,Resilience}/
  Api/{Composition,Controllers,ErrorHandling,Middlewares,OpenApi}/  TestData/  TestDoubles/
tests/TADSS.IntegrationTests/   (giữ cây hiện có; chỉ sửa using/namespace và link helper)
```

Namespace test: `TADSS.UnitTests.{Thư mục}` (ví dụ `TADSS.UnitTests.Application.Decisions`).

Thứ tự phụ thuộc giữa các task: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9.

---

### Task 1: Khung project mới, cây test SDK/Strategies và đổi tên helper test

**Files:**
- Create: `src/TADSS.Application/TADSS.Application.csproj`, `src/TADSS.Infrastructure/TADSS.Infrastructure.csproj` (`net8.0`, `Nullable`, `ImplicitUsings`, `GenerateDocumentationFile` với `NoWarn 1591` như Runtime.Api; `InternalsVisibleTo TADSS.UnitTests`; chưa có mã).
- Modify:
  - `TADSS.slnx`: thêm 2 project vào `/src/`.
  - `Dockerfile`: thêm 2 dòng `COPY` csproj trước bước restore.
  - `Directory.Packages.props`: thêm version cho package sẽ dùng ở Task 2–3 (`Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Diagnostics.HealthChecks`).
  - `tests/TADSS.UnitTests/TADSS.UnitTests.csproj`: thêm `ProjectReference` tới Application, Infrastructure và 2 project Strategies.
- Move (test, `git mv`):
  - `Common/` → `Sdk/Common/`; `Domain/` → `Sdk/Domain/`; `Decisions/` → `Sdk/Decisions/`.
  - `Contracts/*` (trừ `OpenApiDriftTests.cs`) → `Sdk/Contracts/`.
  - `Strategies/` giữ vị trí.
- Rename: `TestData/V2Samples.cs` → `TestData/ContractSamples.cs`; `TestData/V2TestData.cs` → `TestData/DomainTestData.cs`; `Architecture/SdkV2LayeringTests.cs` → `Architecture/SdkLayeringTests.cs` (kèm tên kiểu, E-11). Cập nhật `Compile Include … Link` trong `TADSS.IntegrationTests.csproj` **trong cùng commit** với việc đổi tên (nếu tách, project IT không biên dịch được ở commit giữa).
- Create test: `tests/TADSS.UnitTests/Architecture/ProjectReferenceRulesTests.cs`.

**Interfaces:**
- Produces: hai project rỗng có `ProjectReference` đúng quy tắc:
  - Application → SDK.
  - Infrastructure → Application, SDK.
  - Runtime.Api → Application, Infrastructure (thêm ngay ở task này; chưa có kiểu nào được dùng).
- Produces: tên helper test mới `ContractSamples`, `DomainTestData`.

**Yêu cầu:**
1. Chỉ dựng khung và dời file test. Không đổi mã `src/` nào ngoài csproj/slnx/Dockerfile/props.
2. `ProjectReferenceRulesTests` đọc **file csproj** bằng XML (không dùng reflection, vì assembly rỗng không ghi tham chiếu). Mỗi project src có một allowlist `ProjectReference` và một allowlist `PackageReference` lấy từ Global Constraints. Ngoài allowlist là đỏ.
3. Namespace test dời theo thư mục mới (`TADSS.UnitTests.Sdk.Domain`…).

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `ProjectReferences_EachSrcProject_MatchesAllowList_Theory` | 5 project src (SDK, Application, Infrastructure, Runtime.Api, mỗi Strategies) | `ProjectReference` ⊆ allowlist của project đó | UT-14 |
| `PackageReferences_ApplicationAndInfrastructure_MatchAllowList_Theory` | Application, Infrastructure | Không có gói ASP.NET Core/EF/Npgsql/Redis/Polly trong Application; không có `Microsoft.AspNetCore.*`/Swashbuckle trong Infrastructure | UT-14 |
| `Application_DoesNotReferenceInfrastructureOrApi` | csproj Application | Không có `ProjectReference` tới Infrastructure/Runtime.Api/Strategies | UT-14 |
| Toàn bộ test hiện có | — | Xanh, số lượng không đổi | — |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh, số test trước/sau ghi vào báo cáo.
- `docker build` thành công (Dockerfile mới restore được).
- Commit đề xuất: `build: add application and infrastructure projects` và `test: mirror sdk tests and drop v2 helper names`.

---

### Task 2: Dời lớp use case và port sang `TADSS.Application`

**Files** (`git mv` từ `src/TADSS.Runtime.Api/`):
- `Services/{DecisionService,IDecisionService,DecisionQueryService,IDecisionQueryService,RequestHasher,CanonicalNumberFormatter}.cs`, `Services/Decisions/*` → `src/TADSS.Application/Decisions/`.
  - Tách `DecisionComposition`, `ComposedDecision` khỏi `DecisionResponseComposer.cs` thành file riêng.
- `Services/Strategies/{IStrategyRegistry,StrategyRegistry,RegisteredStrategy,OptionsFingerprintBuilder}.cs` → `src/TADSS.Application/Strategies/`.
  - `StrategyRegistrations.cs` **tạm** giữ ở Runtime.Api (`Services/Strategies/`); Task 5 dời về `Composition/` (E-03).
- `Infrastructure/Data/{IDecisionAuditStore,DecisionExecutionRecord,DecisionDetailRecord,DecisionOverrideRecord,DecisionOutcomeRecord,ExecutionRole,InsertExecutionResult}.cs` → `src/TADSS.Application/Abstractions/Persistence/`.
- `Infrastructure/Caching/{IStrategyDecisionCache,StrategyDecisionCacheKey}.cs` → `src/TADSS.Application/Abstractions/Caching/`.
- `Infrastructure/Resilience/{IStrategyPipeline,StrategyExecutionOutcome,FailureReasonPrefixes}.cs` → `src/TADSS.Application/Abstractions/Resilience/`.
- `Middlewares/{DecisionNotFoundException,IdempotencyConflictException,PersistenceUnavailableException}.cs` → `src/TADSS.Application/Exceptions/`.
- `Configuration/{ApprovalOptions,ValidationOptions,CalendarOptions,StrategyOptions}.cs` → `src/TADSS.Application/Options/`.
  - Tách `PeakWindowOptions` khỏi `CalendarOptions.cs`.
  - Tách `Configuration/OptionsValidators.cs` thành `ApprovalOptionsValidator.cs`, `ValidationOptionsValidator.cs`, `CalendarOptionsValidator.cs` trong cùng thư mục.
- Create: `src/TADSS.Application/ApplicationServiceCollectionExtensions.cs`.
- Modify: `Configuration/DecisionRuntimeServiceCollectionExtensions.cs` (gọi `AddApplication` cho phần đã dời, giữ phần hạ tầng), các file Runtime.Api còn lại (`using`).
- Test (`git mv`):
  - Test của các kiểu trên → `tests/TADSS.UnitTests/Application/{Decisions,Strategies,Options}/`; `Infrastructure/Caching/StrategyDecisionCacheKeyTests.cs` → `Application/Abstractions/`.
  - `Configuration/RuntimeOptionsTests.cs` → `Application/Options/`.
  - `TestDoubles/*` cập nhật `using`.

**Interfaces:**
- Produces:
  - `ApplicationServiceCollectionExtensions.AddApplication(this IServiceCollection services, IConfiguration configuration) : IServiceCollection`. Đăng ký `TimeProvider.System` (TryAdd), các options Application kèm validator + `ValidateOnStart`, `BranchCalendar` (singleton từ `CalendarOptions`), `ItemDurationPolicy`, `DepositCalculator`, `ApprovalWindowPolicy`, `DecisionResponseComposer` (singleton), `IStrategyRegistry` (singleton), `IDecisionService`/`IDecisionQueryService` (scoped).
  - Namespace: `TADSS.Application.Decisions`, `.Strategies`, `.Abstractions.{Persistence,Caching,Resilience}`, `.Exceptions`, `.Options`.
- Consumes: không đổi chữ ký kiểu nào (chỉ đổi namespace).

**Yêu cầu:**
1. **Commit 1 chỉ dời** (`git mv` + sửa `namespace`/`using`). Commit 2 (nếu cần) là phần sửa để biên dịch được (tách file, đăng ký DI). Không đổi thân hàm.
2. Thư viện lớp không có global using của Web SDK: thêm `using Microsoft.Extensions.Logging;` v.v. tường minh trong file cần dùng.
3. Áp dụng mục "Bẫy namespace `Options`" của Global Constraints cho mọi file Application và test được dời ở task này.
4. `AddDecisionRuntime` (Runtime.Api) còn tồn tại và gọi `AddApplication` + phần hạ tầng. Hành vi đăng ký (lifetime, thứ tự `TryAdd`) giữ nguyên. Task 5 xóa nó.
5. Application chỉ có `ProjectReference` tới SDK, `ProjectReferenceRulesTests` của Task 1 vẫn xanh. Nếu cần kiểu của Runtime.Api/Infrastructure (ví dụ `ILogger<T>` là Microsoft.Extensions, được phép) thì dừng, ghi sai lệch, không thêm tham chiếu cấm.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `AddApplication_RegistersUseCasesWithExpectedLifetimes_Theory` | `ServiceCollection` + cấu hình mẫu từ `appsettings.json` | `IDecisionService`/`IDecisionQueryService` scoped; `IStrategyRegistry`, `DecisionResponseComposer`, `ApprovalWindowPolicy`, `DepositCalculator`, `ItemDurationPolicy` singleton | UT-14 |
| `AddApplication_InvalidOptions_FailsOnStart_Theory` | `Approval:WindowMinutes = 0`, `Validation:MinItemMinutes > MaxItemMinutes`, lịch sai | `OptionsValidationException` khi build host | UT-01 |
| Toàn bộ test hiện có (đã dời) | — | Xanh, số lượng không đổi | — |

**Nghiệm thu:**
- Thư mục `Services/`, `Configuration/{Approval,Validation,Calendar,Strategy}Options.cs`, `OptionsValidators.cs` và các port/record/exception đã rời Runtime.Api.
- Build 0/0, unit + IT xanh, `OpenApiDriftTests` xanh không sinh lại.
- Commit đề xuất: `refactor: move use cases and ports to TADSS.Application`.

---

### Task 3: Dời adapter sang `TADSS.Infrastructure` (gồm migration)

**Files** (`git mv` từ `src/TADSS.Runtime.Api/Infrastructure/`):
- `Data/{TadssDbContext,EfDecisionAuditStore,DecisionAuditMapper}.cs`, `Data/Entities/*`, `Data/Configurations/*`, `Data/Migrations/*` → `src/TADSS.Infrastructure/Persistence/{…,Entities,Configurations,Migrations}/`.
- `Middlewares/PersistenceFailureDetector.cs` → `src/TADSS.Infrastructure/Persistence/`. Tạm để `public` vì `ExceptionClassifier` còn dùng; Task 4 chuyển `internal`.
- `Caching/{StrategyDecisionCache,CachedStrategyDecision,TadssCacheOptions}.cs` → `src/TADSS.Infrastructure/Caching/` (options → `Options/`). Tách `CachedRecommendation` thành file riêng.
- `Resilience/{StrategyPipeline,FallbackDecisionPolicy,FallbackCause,ResilienceOptions,ResilienceOptionsValidator}.cs` → `src/TADSS.Infrastructure/Resilience/` (options + validator → `Options/`).
- `Configuration/DatabaseOptions.cs` → `src/TADSS.Infrastructure/Options/`.
- Create: `src/TADSS.Infrastructure/InfrastructureServiceCollectionExtensions.cs`, `Caching/RedisConnectionFactory.cs` (dời nguyên logic `CreateRedisMultiplexer`/`ResolveRedisOrWarn` từ `DecisionRuntimeServiceCollectionExtensions`), `Persistence/PersistenceHealthChecks.cs` (đăng ký health check PostgreSQL, tag `ready`/`db` như cũ).
- Modify: `Program.cs` (`using`), `Configuration/DependencyInjection.cs` (DbContext/health check chuyển vào `AddInfrastructure`), `TADSS.Runtime.Api.csproj` (bỏ gói EF Npgsql/Redis/Polly/HealthChecks.NpgSql nếu Runtime.Api không còn dùng trực tiếp; **giữ** `Microsoft.EntityFrameworkCore.Design` vì Runtime.Api là startup project của `dotnet ef`), `TADSS.Infrastructure.csproj` (gói EF Npgsql, Redis, Polly, `AspNetCore.HealthChecks.NpgSql`, `Microsoft.Extensions.Caching.Memory`).
- Test (`git mv`): `tests/TADSS.UnitTests/Infrastructure/Data/*` → `Infrastructure/Persistence/`; `Infrastructure/{Caching,Resilience}` giữ thư mục, sửa namespace; `Middlewares/PersistenceFailureDetectorTests.cs` → `Infrastructure/Persistence/`. IT: sửa `using`.

**Interfaces:**
- Produces: `InfrastructureServiceCollectionExtensions.AddInfrastructure(this IServiceCollection services, IConfiguration configuration) : IServiceCollection`. Hàm đăng ký:
  - `TadssDbContext` (Npgsql, `MigrationsHistoryTable("__EFMigrationsHistory","tadss")`, `MigrationsAssembly` = assembly Infrastructure).
  - `DatabaseOptions`; health check PostgreSQL (tag `ready`, `db`).
  - `ResilienceOptions` + validator + `ValidateOnStart`; `IStrategyPipeline → StrategyPipeline` (singleton).
  - `IMemoryCache`, `TadssCacheOptions`, `IConnectionMultiplexer` (TryAdd, lazy), `IStrategyDecisionCache` (singleton).
  - `IDecisionAuditStore → EfDecisionAuditStore` (scoped).
- Namespace: `TADSS.Infrastructure.{Persistence,Persistence.Entities,Persistence.Configurations,Persistence.Migrations,Caching,Resilience,Options}`.

**Yêu cầu:**
1. **Migration:**
   - Chỉ đổi `namespace`/`using` trong 4 file migration, 4 file Designer và `TadssDbContextModelSnapshot`.
   - Chuỗi `[Migration("…")]` và thân `Up`/`Down` không đổi một ký tự. Kiểm bằng `git diff -M --stat` và `git diff -M` (chỉ dòng namespace/using).
   - Chuỗi tên kiểu entity trong snapshot (`"TADSS.Runtime.Api.Infrastructure.Data.Entities.X"`) đổi sang namespace mới cho khớp model.
2. **Kiểm chứng migration** (chỉ đọc), với `ASPNETCORE_ENVIRONMENT=Testing`:
   - `dotnet ef migrations list --no-connect --project src/TADSS.Infrastructure --startup-project src/TADSS.Runtime.Api` liệt kê đúng 4 ID cũ.
   - `dotnet ef migrations has-pending-model-changes --project … --startup-project …` báo không có thay đổi. Nếu bản `dotnet-ef` không có lệnh này, ghi vào báo cáo và dựa vào IT-15.
3. Thứ tự và lifetime đăng ký giữ đúng như `AddDecisionRuntime` hiện tại. `AddDecisionRuntime` gọi `AddApplication` + `AddInfrastructure`.
4. Không đổi tên kiểu ở task này (`TadssCacheOptions`, entity giữ tên; Task 7 đổi).
5. Runtime.Api không còn file nào trong `Infrastructure/`; thư mục bị xóa.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `AddInfrastructure_RegistersAdaptersWithExpectedLifetimes_Theory` | `ServiceCollection` + cấu hình mẫu (`Provider = Memory`) | Store scoped; pipeline, cache singleton; `TadssDbContext` scoped; không kết nối Redis/PostgreSQL khi resolve | UT-14 |
| `Migrations_AssemblyIsInfrastructure_AndIdsUnchanged` | Đọc `IMigrationsAssembly` từ `TadssDbContext` (Npgsql, không kết nối) | `Assembly` = `TADSS.Infrastructure`; tập ID = 4 ID hiện có | UT-14 |
| IT-15 và các test migration (Up/Down v1↔v2, trigger) | Testcontainers | Xanh, không đổi | IT-15, IT-05 |
| Toàn bộ test hiện có (đã dời) | — | Xanh, số lượng không đổi | — |

**Nghiệm thu:**
- Lệnh `dotnet ef` ở yêu cầu 2 cho kết quả đúng (dán output vào báo cáo).
- Build 0/0, unit + IT xanh, drift OpenAPI xanh.
- Commit đề xuất: `refactor: move adapters and migrations to TADSS.Infrastructure`.

---

### Task 4: Dịch exception hạ tầng tại adapter (E-04)

**Files:**
- Create: `src/TADSS.Application/Exceptions/DecisionTimeoutException.cs`.
- Modify:
  - `src/TADSS.Infrastructure/Resilience/StrategyPipeline.cs`: bắt `TimeoutRejectedException` của **outer timeout** (không phải hủy của caller) và ném `DecisionTimeoutException` (giữ inner exception, message tiếng Việt không chứa chi tiết nội bộ).
  - `src/TADSS.Infrastructure/Persistence/PersistenceFailureDetector.cs`: `public` → `internal`.
  - `src/TADSS.Runtime.Api/Middlewares/ExceptionClassifier.cs`:
    - 503 khi gặp `PersistenceUnavailableException` ở bất kỳ đâu trong chuỗi inner exception, kể cả `AggregateException.InnerExceptions`.
    - 504 khi gặp `DecisionTimeoutException`.
    - Bỏ `using Polly.Timeout`, bỏ gọi `PersistenceFailureDetector`.
- Test:
  - `tests/TADSS.UnitTests/Middlewares/ExceptionClassifierTests.cs` (UT-08) thay `TimeoutRejectedException` bằng `DecisionTimeoutException`, `NpgsqlException` thô bằng `PersistenceUnavailableException`. Thêm ca "exception thô của Npgsql không còn được classifier nhận" → 500.
  - `tests/TADSS.UnitTests/Infrastructure/Resilience/StrategyPipelineTests.cs` thêm ca outer timeout.

**Interfaces:**
- Produces: `DecisionTimeoutException(string message, Exception? innerException)` (`sealed`, kế thừa `Exception`, namespace `TADSS.Application.Exceptions`).
- Consumes: `PersistenceUnavailableException` (Task 2), `StrategyPipeline` (Task 3).

**Yêu cầu:**
1. HTTP giữ nguyên: 504 `GATEWAY_TIMEOUT` + `Retry-After` (cùng số giây), 503 `PERSISTENCE_UNAVAILABLE` + `Retry-After`; message ẩn như cũ.
2. Hủy do caller (`OperationCanceledException` với token của request đã hủy) **không** bị đổi thành `DecisionTimeoutException`. **Ưu tiên hủy của caller:** chỉ dịch sang `DecisionTimeoutException` khi token của caller **chưa** bị hủy tại thời điểm bắt exception. Nếu outer timeout và hủy của caller cùng xảy ra (token caller đã hủy khi `TimeoutRejectedException` lan ra), ném `OperationCanceledException` gắn token của caller, để handler xử lý như client hủy (không ghi response).
3. Sau task: không file nào của `TADSS.Runtime.Api` (ngoài `Program.cs`, `Composition/`, `Configuration/` còn tạm) có `using Polly`, `using Npgsql`, `using Microsoft.EntityFrameworkCore`, `using StackExchange.Redis`. Kiểm bằng `grep` và ghi vào báo cáo. UT-14 khóa ở Task 8.
4. Rà mọi lời gọi DB trong luồng request: chỉ qua `EfDecisionAuditStore` (store đã dịch lỗi). Nếu phát hiện đường khác ném exception Npgsql thô ra controller, dừng và báo; không tự nới classifier.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Classify_DecisionTimeout_Is504WithRetryAfter` | `DecisionTimeoutException` | 504 `GATEWAY_TIMEOUT`, `ExposeMessage = false`, có `RetryAfterSeconds` | UT-08 |
| `Classify_PersistenceUnavailableNestedOrAggregate_Is503_Theory` | Trực tiếp; bọc trong `InvalidOperationException`; trong `AggregateException` nhánh thứ hai | 503 `PERSISTENCE_UNAVAILABLE` | UT-08 |
| `Classify_RawNpgsqlException_IsInternalError` | `NpgsqlException` thô (store đã chịu trách nhiệm dịch) | 500 `INTERNAL_ERROR` | UT-08 |
| `ExecuteAsync_OuterTimeoutExceeded_ThrowsDecisionTimeoutException` | Ngân sách outer nhỏ hơn tổng thời gian chặn của strategy | `DecisionTimeoutException`, inner là `TimeoutRejectedException` | UT-07 |
| `ExecuteAsync_CallerCancelled_PropagatesOperationCanceled` | Token caller hủy giữa chừng | `OperationCanceledException`, không phải `DecisionTimeoutException` | UT-07 |
| `ExecuteAsync_OuterTimeoutWhileCallerCancelled_PrefersCancellation` | Tất định. Cấu hình resilience riêng cho test sao cho **outer timeout là timeout đầu tiên kích hoạt**. Strategy double chờ `Task.Delay` có tôn trọng token; khi token của nó bị hủy (do timeout), nó hủy `CancellationTokenSource` của caller **trước** khi ném lại. Lưu ý: timeout của Polly v8 là kiểu hợp tác, nên strategy bỏ qua token sẽ không tạo được kịch bản này | `OperationCanceledException`, token của caller đã hủy; **không** `DecisionTimeoutException` | UT-07 |
| FI-08 (2 biến thể), FI-10 | Testcontainers + Toxiproxy | Xanh, không đổi | FI-08, FI-10 |

**Nghiệm thu:**
- Grep ở yêu cầu 3 rỗng (ngoài vùng được phép).
- Build 0/0, unit + IT xanh.
- Commit đề xuất: `refactor: translate infrastructure exceptions at the adapter boundary`.

---

### Task 5: Presentation và composition root của `TADSS.Runtime.Api`

**Files:**
- Move (`git mv`, trong `src/TADSS.Runtime.Api/`):
  - `Middlewares/{ExceptionClassifier,ErrorClassification}.cs` và `Configuration/InvalidModelStateTranslator.cs` → `ErrorHandling/`.
  - `Configuration/{RequiredHeaderOperationFilter,ProblemDetailsSchemaFilter,LineEndingDocumentFilter}.cs` → `OpenApi/`.
  - `Services/Strategies/StrategyRegistrations.cs` → `Composition/`.
- Create (`Composition/`):
  - `PresentationServiceCollectionExtensions.cs`: `AddPresentation`, nội dung là `AddApiServices` hiện tại.
  - `StrategyServiceCollectionExtensions.cs`: `AddStrategies`, đăng ký 2 `RegisteredStrategy` từ `StrategyRegistrations`.
  - `RequestPipelineExtensions.cs`: `UseTadssRequestPipeline`, thứ tự middleware hiện tại của `Program.cs`.
  - `StartupTasks.cs`: resolve sớm `IStrategyRegistry`; áp migration khi `ApplyMigrationsOnStartup` kèm Warning.
- Delete: `Configuration/DependencyInjection.cs`, `Configuration/DecisionRuntimeServiceCollectionExtensions.cs` (`AddDecisionRuntime`), thư mục `Configuration/`, `Services/`.
- Modify: `Program.cs` chỉ còn: Serilog; `AddApplication` → `AddInfrastructure` → `AddStrategies` → `AddPresentation`; `StartupTasks`; `UseTadssRequestPipeline`; `MapControllers` + health endpoint; `Run`; `public partial class Program`.
- Test (`git mv`):
  - `Controllers/` → `Api/Controllers/`; `Middlewares/` → `Api/Middlewares/`, riêng `ExceptionClassifierTests` → `Api/ErrorHandling/`.
  - `Configuration/{HttpPipelineTests,StrategyRegistryStartupTests}.cs` → `Api/Composition/`.
  - `Contracts/OpenApiDriftTests.cs` → `Api/OpenApi/`.
  - Mọi test đang gọi `AddDecisionRuntime` chuyển sang `AddApplication` + `AddInfrastructure` + `AddStrategies` (hoặc helper test `RuntimeServiceProviderBuilder` trong `TestDoubles/` gói ba lời gọi này, dùng chung).

**Interfaces:**
- Produces:
  - `AddPresentation(this IServiceCollection services) : IServiceCollection`.
  - `AddStrategies(this IServiceCollection services, IConfiguration configuration) : IServiceCollection`.
  - `UseTadssRequestPipeline(this WebApplication app) : WebApplication`.
  - `StartupTasks.RunAsync(WebApplication app) : Task`.
  - Namespace `TADSS.Runtime.Api.{Composition,ErrorHandling,OpenApi,Middlewares,Controllers,Common}`.
- Consumes: `AddApplication` (Task 2), `AddInfrastructure` (Task 3).

**Yêu cầu:**
1. Thứ tự middleware và endpoint **y hệt** hiện tại:
   1. correlation id
   2. exception handler
   3. Swagger (chỉ Development)
   4. request logging
   5. https redirection
   6. authorization
   7. controllers
   8. health `/health/live` (không check nào), `/health/ready` (tag `ready`), `/health`
2. Swagger: tên tài liệu `v2`, `Info` (title, version `2.0.0-draft`, description), filter giữ nguyên. Đường XML doc của Runtime.Api và SDK nạp như cũ.
3. Controller và middleware không `using TADSS.Infrastructure`. Chỉ `Program.cs` và `Composition/` được phép.
4. `Program.cs` ≤ 40 dòng.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `ServiceGraph_ValidatesOnBuildAndScopes_AsInDevelopment` (đã có, dời) | Host thật với `ValidateOnBuild`/`ValidateScopes` | Xanh | UT-14 |
| `RequestPipeline_ErrorResponseCarriesCorrelationId` | Controller ném lỗi (double) | Response lỗi có `X-Correlation-ID`, `traceId` = correlation id | UT-08 |
| `RequestPipeline_SwaggerOnlyInDevelopment_Theory` | Môi trường `Testing` / `Development` (không đọc `appsettings.Development.json`: test host xóa nguồn file đó) | 404 / 200 tại `/swagger/v2/swagger.json` | UT-14 |
| `Program_HasNoInfrastructureUsingsOutsideCompositionRoot` | Quét nguồn `src/TADSS.Runtime.Api/**` trừ `Program.cs`, `Composition/` | Không có `TADSS.Infrastructure` | UT-14 |
| `OpenApi_GeneratedDocument_MatchesCommittedFile` (đã có) | — | Xanh, file không sinh lại | CT-01 |
| Toàn bộ unit + IT | — | Xanh, số lượng không đổi | — |

**Nghiệm thu:**
- Cây Runtime.Api đúng "Cấu trúc đích" (trừ `Options/` của Task 7). Không còn `Configuration/`, `Services/`, `Infrastructure/`.
- Build 0/0, unit + IT xanh.
- Commit đề xuất: `refactor: presentation layer and composition root`.

---

### Task 6: Tách trách nhiệm trong Application (SRP, ISP, DRY — E-07, E-08)

**Files:**
- Create (`src/TADSS.Application/`):
  - `Decisions/StrategyEvaluator.cs`, `Decisions/StrategyEvaluation.cs`, `Decisions/DecisionPayloadSerializer.cs`.
  - `Abstractions/Persistence/IDecisionEvaluationStore.cs`, `Abstractions/Persistence/IDecisionQueryStore.cs`.
- Delete: `Abstractions/Persistence/IDecisionAuditStore.cs`.
- Modify:
  - `Decisions/DecisionService.cs` (dùng `StrategyEvaluator`, `DecisionPayloadSerializer`, `IDecisionEvaluationStore`).
  - `Decisions/DecisionQueryService.cs` (dùng `IDecisionQueryStore`, `DecisionPayloadSerializer`).
  - `Decisions/DecisionResponseComposer.cs` (serialize qua `DecisionPayloadSerializer`).
  - `src/TADSS.Infrastructure/Persistence/EfDecisionAuditStore.cs` (hiện thực hai interface).
  - Đăng ký DI ở `AddApplication`/`AddInfrastructure`.
  - `tests/**/TestDoubles/FakeDecisionAuditStore.cs` (hiện thực hai interface).
- Test: `tests/TADSS.UnitTests/Application/Decisions/{StrategyEvaluatorTests,DecisionPayloadSerializerTests}.cs`. Dời các test cache trong `DecisionServiceTests` có kịch bản thuộc evaluator sang `StrategyEvaluatorTests` (giữ tên, giữ `CatalogId`). Cập nhật test reflection "store chỉ append-only" cho hai interface.

**Interfaces:**
- Produces:
  - `IDecisionEvaluationStore`:
    - `Task<DecisionExecutionRecord?> FindPrimaryByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)`
    - `Task<InsertExecutionResult> InsertExecutionAsync(DecisionExecutionRecord record, CancellationToken cancellationToken)`
  - `IDecisionQueryStore`:
    - `Task<DecisionDetailRecord?> GetDecisionDetailAsync(Guid decisionId, CancellationToken cancellationToken)`
  - `StrategyEvaluator` (sealed, singleton), constructor `(IStrategyPipeline pipeline, IStrategyDecisionCache cache, TimeProvider timeProvider, ILogger<StrategyEvaluator> logger)`; method `Task<StrategyEvaluation> EvaluateAsync(RegisteredStrategy strategy, Reservation reservation, DecisionContext context, string requestHash, string idempotencyKey, CancellationToken cancellationToken)`.
  - `sealed record StrategyEvaluation(StrategyExecutionOutcome Outcome, bool CacheHit)`.
  - `DecisionPayloadSerializer` (static, thuần):
    - `string SerializeRequest(DecisionRequestDto request)`
    - `string SerializeResponse(DecisionResultDto response)`
    - `DecisionRequestDto DeserializeRequest(string payload)`
    - `DecisionResultDto DeserializeResponse(string payload)`
  - Payload hỏng/`null` → `JsonException` như hành vi hiện tại của `DecisionQueryService`. Replay hiện ném `InvalidOperationException` khi payload `null`: giữ nguyên ngữ nghĩa từng nơi gọi bằng cách để nơi gọi chuyển exception nếu cần, ghi vào báo cáo.
- Consumes: kiểu của Task 2–4.

**Yêu cầu:**
1. **Không đổi hành vi:**
   - Thứ tự replay → P-01 → `nowUtc` → evaluator → compose → insert giữ nguyên.
   - Lỗi cache vẫn chỉ là miss và được log như cũ.
   - Thời gian `CACHE_HIT` vẫn đo bằng `TimeProvider`.
   - Fallback không ghi cache.
   - Byte của `request_payload`/`response_payload` không đổi (cùng `TadssJson.Options`).
2. `DecisionService` sau task ≤ 180 dòng, constructor ≤ 8 tham số (hiện 9). `DecisionService` không còn tham chiếu `IStrategyDecisionCache`/`StrategyDecisionCacheKey`.
3. DI:
   - `EfDecisionAuditStore` đăng ký scoped một lần.
   - `IDecisionEvaluationStore` và `IDecisionQueryStore` forward tới cùng instance trong scope.
   - `StrategyEvaluator` singleton.
4. Cả hai interface giữ ràng buộc append-only: không có method `Update*/Delete*/Remove*/Add*`, không trả `IQueryable`/`DbContext`/entity.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `EvaluateAsync_CacheHit_ReturnsCachedWithoutPipeline` | Cache double trả quyết định | `CacheHit = true`, `CACHE_HIT`, pipeline không được gọi | UT-11 |
| `EvaluateAsync_CacheReadOrWriteThrows_TreatedAsMiss_Theory` | Get/Set ném | Pipeline chạy; kết quả `AUTOMATED`; không ném | UT-13 |
| `EvaluateAsync_FallbackOutcome_IsNotCached` | Pipeline trả `FALLBACK` | `SetAsync` không được gọi | UT-13 |
| `EvaluateAsync_CachePolicyDisabled_NeverTouchesCache` | Strategy `Disabled` | Không Get/Set | UT-13 |
| `EvaluateAsync_CallerCancelled_Propagates` | Token hủy khi đọc cache | `OperationCanceledException` | UT-11 |
| `Serializer_RoundTrip_PreservesBytesOfComposerOutput` | Response/request mẫu | Serialize bằng serializer = serialize trực tiếp bằng `TadssJson.Options` (từng byte); deserialize khôi phục bằng nhau | UT-11 |
| `AuditStores_ResolveSameScopedInstance` | Scope DI | Hai interface → cùng `EfDecisionAuditStore` trong một scope, khác giữa hai scope | UT-14 |
| `AuditStoreInterfaces_AreAppendOnly_Theory` | Reflection trên hai interface | Không có method/kiểu trả về bị cấm | UT-14 |
| Toàn bộ `DecisionServiceTests`, `DecisionServiceEndToEndTests`, IT-01…04, IT-13 | — | Xanh, không đổi kỳ vọng | UT-11, IT-* |

**Nghiệm thu:**
- Yêu cầu 2 đạt (ghi số dòng và số tham số vào báo cáo).
- Build 0/0, unit + IT xanh.
- Commit đề xuất: `refactor: split strategy evaluation, payload serialization and audit ports`.

---

### Task 7: Bỏ hardcode, gom hằng contract, đổi tên (E-09, E-10, E-11)

**Files:**
- Create:
  - `src/TADSS.Application/Common/ContractLimits.cs`.
  - `src/TADSS.Application/Options/StrategyOptionsValidator.cs`.
  - `src/TADSS.Infrastructure/Options/{CacheOptionsValidator,DatabaseOptionsValidator}.cs`.
  - `src/TADSS.Runtime.Api/Options/{ErrorHandlingOptions,ErrorHandlingOptionsValidator}.cs`.
- Rename:
  - `TadssCacheOptions` → `CacheOptions` (file + kiểu; `SectionName` vẫn `"Tadss:Cache"`).
  - Entity `DecisionExecution`/`DecisionOverride`/`DecisionOutcome` → `DecisionExecutionEntity`/`DecisionOverrideEntity`/`DecisionOutcomeEntity` (file + kiểu + chuỗi tên kiểu trong snapshot/Designer).
- Modify:
  - `DecisionService`, `DecisionsController`, `CorrelationIdMiddleware`: dùng `ContractLimits`.
  - `ExceptionClassifier`/`GlobalExceptionHandler`: `Retry-After` lấy từ `ErrorHandlingOptions`; classifier trả cờ "có Retry-After", handler điền số giây.
  - `RedisConnectionFactory`: `RedisConnectTimeoutMs` từ `CacheOptions`; quy tắc placeholder giữ nguyên, chuỗi placeholder thành hằng có tên.
  - `PersistenceHealthChecks`: tên `"postgresql"`.
  - Xóa `CacheOptions.MaxMemoryCacheSize` và khóa tương ứng trong `appsettings.json`.
  - Thêm khóa mới vào `appsettings.json` với giá trị mặc định: `Tadss:ErrorHandling:RetryAfterSeconds = 5`, `Tadss:Cache:RedisConnectTimeoutMs = 3000`.
  - Xóa comment chỉ mang tên gói/phiên bản ("v2", "5.2b", "5.2c"… mô tả lịch sử) trong `src/**`, giữ comment mô tả hành vi.
- Test:
  - `tests/TADSS.UnitTests/Application/Options/*`, `Infrastructure/Caching/*`, `Api/ErrorHandling/*` (validator, Retry-After từ options).
  - `tests/TADSS.UnitTests/Architecture/ContractLimitsTests.cs`.

**Interfaces:**
- Produces:
  - `ContractLimits` (static): `public const int MaxIdempotencyKeyLength = 128;`, `public const int MaxCorrelationIdLength = 128;`.
  - `ErrorHandlingOptions { const SectionName = "Tadss:ErrorHandling"; int RetryAfterSeconds = 5 }`; validator `1 ≤ RetryAfterSeconds ≤ 3600`.
  - `CacheOptions.RedisConnectTimeoutMs` (mặc định 3000, > 0).
  - `CacheOptionsValidator`: `Provider` là `Redis` hoặc `Memory` (không phân biệt hoa thường, như phép so sánh hiện tại); `RedisTimeoutMs > 0`; `RedisConnectTimeoutMs > 0`; `TtlHours > 0`.
  - `DatabaseOptionsValidator` (không có ràng buộc số; chỉ để đủ mẫu, có thể bỏ nếu options không có trường cần kiểm, ghi lý do).
  - `StrategyOptionsValidator`: `ActiveStrategyName` không rỗng.

**Yêu cầu:**
1. Mặc định mới tái tạo đúng hành vi cũ: Retry-After = 5; timeout kết nối = `max(RedisTimeoutMs, 3000)` trừ host placeholder.
2. Validator mới chỉ chặn cấu hình sai, thuộc ngoại lệ (a) của Global Constraints; ghi rõ vào báo cáo. `appsettings.json` và cấu hình test hiện có phải qua validator.
3. **Đổi tên entity:**
   - Bảng, cột, constraint, index, trigger giữ nguyên (`ToTable`, `HasColumnName`… trong configuration giữ nguyên).
   - IT-15 xanh; `dotnet ef migrations has-pending-model-changes` không có thay đổi (Testing, chỉ đọc).
   - `git diff` của 9 file migration chỉ là đổi chuỗi tên kiểu.
4. Bảng `Phụ lục A` cập nhật cột "Trạng thái" cho các dòng Task 7 trong báo cáo (không sửa plan).

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `ErrorHandlingOptions_InvalidRetryAfter_FailsOnStart_Theory` | 0, -1, 3601 | `OptionsValidationException` | UT-08 |
| `Handle_RetryableError_UsesConfiguredRetryAfter_Theory` | 503/504 với `RetryAfterSeconds = 7` | Header `Retry-After: 7` | UT-08 |
| `Handle_DefaultConfig_RetryAfterIsFive` | Cấu hình `appsettings.json` | `Retry-After: 5` | UT-08 |
| `CacheOptions_Invalid_FailsOnStart_Theory` | Provider lạ, `RedisTimeoutMs = 0`, `RedisConnectTimeoutMs = 0` | `OptionsValidationException` | UT-13 |
| `RedisConnectionFactory_ConnectTimeout_UsesMaxOfOptions_Theory` | `RedisTimeoutMs = 150`/`5000`; host thật/placeholder | 3000/5000; placeholder dùng `RedisTimeoutMs` | UT-13 |
| `ContractLimits_MatchControllerServiceAndMiddleware` | Gửi 128/129 ký tự cho key và correlation id qua host thử nghiệm | 128 chấp nhận; 129 → 400 (key) / thay GUID (correlation) — hành vi cũ | UT-08 |
| `AppSettings_PassAllValidators` | Nạp `appsettings.json` + toàn bộ options | Không lỗi | UT-14 |
| IT-15, test migration | Testcontainers | Xanh | IT-15 |

**Nghiệm thu:**
- Không còn `TadssCacheOptions`, entity tên cũ, chuỗi `"Neon-PostgreSQL"`, số `5` cứng cho `Retry-After`, số `128` lặp ngoài `ContractLimits`, `3000` cứng (grep, ghi vào báo cáo).
- Build 0/0, unit + IT xanh, drift OpenAPI xanh.
- Commit đề xuất: `refactor: move tunables to options and rename persistence entities`.

---

### Task 8: UT-14 đầy đủ — test kiến trúc khóa quy ước

**Files:**
- Create (`tests/TADSS.UnitTests/Architecture/`): `LayerDependencyTests.cs`, `NamingConventionTests.cs`, `SourceFileConventionTests.cs`, `RateScaleInvariantTests.cs`.
- Modify: `NoLegacyV1Tests.cs` (mở rộng phạm vi quét theo minor Task 4 của 5.2c: mọi namespace của Application, Infrastructure và Runtime.Api; EF chỉ được dùng trong `TADSS.Infrastructure.Persistence`), `SdkLayeringTests.cs` (gộp ràng buộc trùng với `LayerDependencyTests` nếu có, không mất kiểm tra).
- Test helper: `TestData/SourceTree.cs` (liệt kê file `.cs` của `src/` qua `RepositoryPaths`, bỏ `obj/`, `bin/`).

**Interfaces:**
- Consumes: `RepositoryPaths`, assembly của 5 project src.

**Yêu cầu:**
1. Mỗi quy tắc một test, có message chỉ rõ file/kiểu vi phạm. Quét động (reflection/nguồn); không dùng danh sách tên kiểu cứng, trừ allowlist ngoại lệ có lý do.
2. **Ngoại lệ được phép** (khai báo trong test kèm lý do):
   - Class migration `DecisionPersistenceV2` (tên gắn với ID).
   - Options của strategy (`DefaultStrategyOptions`, `MockAlternativeStrategyOptions`) nằm ở gốc project strategy (spec 4.2.2).
   - Exception của SDK (`ContractValidationException`, `DomainInvariantException`) nằm trong SDK.
   - File sinh tự động (`*.Designer.cs`, `Migrations/*`) được miễn quy tắc kích thước và "một kiểu/file".
3. Trần kích thước: **250 dòng** mỗi file `.cs` trong `src/`, trừ `Migrations/`.
4. Test chạy không cần Docker.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Application_ReferencesNoInfrastructureAssemblies` | `GetReferencedAssemblies` của Application | Không có `TADSS.Infrastructure`, `TADSS.Runtime.Api`, `TADSS.Strategies.*`, `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore*`, `Npgsql*`, `StackExchange.Redis`, `Polly*` | UT-14 |
| `Infrastructure_ReferencesNoPresentationAssemblies` | Infrastructure | Không có `TADSS.Runtime.Api`, `Microsoft.AspNetCore.Mvc*`, `Swashbuckle*` | UT-14 |
| `ApiOutsideCompositionRoot_UsesNoInfrastructureTypes` | Nguồn Runtime.Api trừ `Program.cs`, `Composition/` | Không có `TADSS.Infrastructure`, `Npgsql`, `Polly`, `StackExchange.Redis`, `Microsoft.EntityFrameworkCore` | UT-14 |
| `EfCore_UsedOnlyInPersistence` | Nguồn Infrastructure | `Microsoft.EntityFrameworkCore` chỉ trong `Persistence/` | UT-14 |
| `Namespace_MatchesProjectAndFolder_Theory` | Mọi file `src/**/*.cs` | `namespace` = tên project + thư mục | UT-14 |
| `Types_HaveNoV2InNameOrNamespace_Theory` | Mọi kiểu của 5 assembly (trừ ngoại lệ) | Không có "V2" | UT-14 |
| `SourceFiles_DeclareOnePublicTopLevelType_Theory` | Mọi file `src/**` (trừ ngoại lệ) | ≤ 1 kiểu public cấp cao nhất; tên file = tên kiểu | UT-14 |
| `OptionsTypes_LiveInOptionsFolder_Theory` | Kiểu tên `*Options`/`*OptionsValidator` của Application, Infrastructure, Runtime.Api | Namespace kết thúc `.Options` | UT-14 |
| `ExceptionTypes_LiveInApplicationExceptions` | Kiểu kế thừa `Exception` trong Application, Infrastructure, Runtime.Api | Chỉ ở `TADSS.Application.Exceptions` | UT-14 |
| `SourceFiles_AreWithinSizeCap_Theory` | `src/**` trừ `Migrations/` | ≤ 250 dòng | UT-14 |
| `PortInterfaces_LiveInApplicationAbstractions` | Interface mà Infrastructure hiện thực | Khai báo trong `TADSS.Application.Abstractions.*` | UT-14 |
| `RateDecimals_AreFourEverywhere` | Hằng `MaxRateDecimals` của 2 options strategy + composer (reflection, kể cả private) và scale của cột `deposit_rate` trong model EF | Đều = 4 | UT-14 |
| `NoLegacyV1Tests` (mở rộng) | Mọi namespace runtime | Không có kiểu/package v1; EF chỉ ở Persistence | UT-09 |

**Nghiệm thu:**
- Mỗi test UT-14 đã được thấy **đỏ** bằng một vi phạm thử rồi hoàn tác.
- Báo cáo có bảng minh chứng, mỗi quy tắc một dòng: `test → vi phạm tạm đã đưa vào (file/dòng cụ thể, ví dụ thêm using Npgsql vào DecisionsController.cs) → message đỏ thực tế (trích) → đã hoàn tác`.
- Sau khi hoàn tác, `git status` sạch phần `src/`.
- Build 0/0, unit + IT xanh.
- Commit đề xuất: `test(arch): lock clean architecture conventions (UT-14)`.

---

### Task 9: Tài liệu, skill và kiểm chứng cuối gói

**Files:**
- Modify:
  - `docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md`:
    - Mục 2: cây 5 project + bảng quy tắc phụ thuộc.
    - Mục 4.2: cột vị trí theo project.
    - Mục 4.4: **sơ đồ tuần tự** `POST /api/v2/decisions` qua các tầng, gồm nhánh replay, cache hit, fallback, 503 khi ghi audit, hủy của caller.
    - Mục 5: nguyên tắc phụ thuộc.
  - `docs/architecture/ARCHITECTURE_DECISION_RECORDS.md`: ADR-013 "Clean Architecture nhiều project" (bối cảnh, quyết định P-03, hệ quả), thêm dòng vào bảng chỉ mục.
  - `docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md`: vị trí entity/configuration/migration mới, lệnh `dotnet ef` mới.
  - `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`: chỉ khi mô tả UT-14 lệch hiện thực. **Không stage** nếu file đang có thay đổi chưa commit của người dùng; ghi vào báo cáo.
  - `docs/planning/BACKLOG_SPRINTS.md`: trạng thái 5.2d.
  - `README.md`, `TADSS-Runtime/CLAUDE.md`: bảng project, cây thư mục, lệnh `dotnet ef --project src/TADSS.Infrastructure --startup-project src/TADSS.Runtime.Api`.
  - `../CLAUDE.md` (gốc workspace, ngoài repo): dòng TADSS trong bảng repo.
  - `../.claude/skills/tadss-architecture/SKILL.md` (ngoài repo): **viết lại** thành bảng "thứ cần tạo → project/thư mục → tên" đầy đủ như skill BE (use case, port, adapter, options, exception, controller, middleware, migration, strategy, test theo cây mirror), quy tắc phụ thuộc, lệnh build/test/ef, sai lầm thường gặp.
- Modify (nếu cần): `.github/workflows/ci.yml`, `Dockerfile`, `docker-compose.yml`.

**Yêu cầu:**
1. Tài liệu chỉ ghi điều đã hiện thực, sửa ở file sở hữu, không chép spec.
2. Kiểm tra link nội bộ của mọi file `.md` đã sửa: không có link hỏng.
3. **Kiểm chứng cuối gói** (ghi số liệu thật vào báo cáo):
   - `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror --no-incremental` → 0/0.
   - `dotnet test tests/TADSS.UnitTests -c Release --no-build -nologo` và `dotnet test tests/TADSS.IntegrationTests -c Release --no-build -nologo` (Docker) → xanh. So sánh số test với đầu gói (965 unit / 121 IT); số tăng chỉ đến từ UT-14 và test mới của Task 2–7.
   - `git diff --stat cc1254e HEAD -- docs/contracts/baseline_public_api.txt` rỗng; `git diff <đầu gói> HEAD -- docs/contracts/openapi-v2.yaml` rỗng.
   - Chuỗi `[Migration("…")]` của 4 migration không đổi so với đầu gói; `dotnet ef migrations list --no-connect` và `has-pending-model-changes` (Testing) đúng.
   - Khởi động app: `ASPNETCORE_ENVIRONMENT=Testing`, cổng trống trên `127.0.0.1`, chuỗi kết nối trỏ tới cổng **không có dịch vụ** (ví dụ `Host=127.0.0.1;Port=1`). Yêu cầu: lên trong ≤ 20 giây, không lỗi validate options, `GET /health/live` = 200. Chỉ dừng tiến trình mình tạo.
   - `docker build` thành công (xóa image sau đó); `docker compose config -q` hợp lệ.
4. Báo cáo gói gồm:
   - Bảng Phụ lục A với cột trạng thái (đã xử lý ở task nào / chuyển gói nào).
   - Danh sách UT-14 và quy tắc mỗi test khóa.
   - Việc người dùng cần làm: review cây project, review diff snapshot, đọc skill mới.
   - Mục chuyển sang 5.3.

**Nghiệm thu:**
- Mọi mục kiểm chứng đạt; tài liệu khớp mã.
- Commit đề xuất: `docs: clean architecture layout and conventions (package 5.2d)`.
- Đề xuất PR vào `develop` (người dùng tạo/merge).

---

## Phụ lục A — Kiểm toán SOLID và hardcode (spec 4.2.4 việc 1)

| # | File / kiểu (sau 5.2c) | Vấn đề | Cách sửa | Project đích | Task |
| :--- | :--- | :--- | :--- | :--- | :---: |
| A-1 | `Infrastructure/Data/IDecisionAuditStore.cs` + record | Port nằm trong hạ tầng → use case phụ thuộc Infrastructure (DIP) | Dời vào `Application/Abstractions/Persistence` | Application | 2 |
| A-2 | `Middlewares/{DecisionNotFound,IdempotencyConflict,PersistenceUnavailable}Exception.cs` | Exception nghiệp vụ nằm ở tầng HTTP; service/store tham chiếu ngược | Dời vào `Application/Exceptions` | Application | 2 |
| A-3 | `Configuration/OptionsValidators.cs` | 3 kiểu public trong 1 file | Tách mỗi validator một file | Application | 2 |
| A-4 | `Configuration/CalendarOptions.cs` | 2 kiểu public (`PeakWindowOptions`) | Tách file | Application | 2 |
| A-5 | `Services/Decisions/DecisionResponseComposer.cs` | 3 kiểu public (`DecisionComposition`, `ComposedDecision`) | Tách file | Application | 2 |
| A-6 | `Services/Strategies/StrategyRegistrations.cs` | Use case biết strategy cụ thể (phụ thuộc Strategies) | Dời về composition root (E-03) | Runtime.Api | 5 |
| A-7 | `Infrastructure/Caching/CachedStrategyDecision.cs` | 2 kiểu trong 1 file | Tách `CachedRecommendation` | Infrastructure | 3 |
| A-8 | `Configuration/DecisionRuntimeServiceCollectionExtensions.cs` | Một hàm đăng ký mọi tầng; lambda `new StrategyDecisionCache(...)`; logic tạo Redis nằm trong DI | `AddApplication`/`AddInfrastructure`/`AddStrategies`/`AddPresentation`; tạo Redis ở `RedisConnectionFactory` | Cả ba | 2, 3, 5 |
| A-9 | `Middlewares/ExceptionClassifier.cs` | Tầng HTTP phụ thuộc Polly (`TimeoutRejectedException`) và Npgsql (qua detector) | Dịch tại adapter (E-04) | Runtime.Api / Infrastructure | 4 |
| A-10 | `Services/DecisionService.cs` (299 dòng, 9 tham số) | Điều phối + cache + key + log (SRP) | `StrategyEvaluator`, `DecisionPayloadSerializer` (E-08) | Application | 6 |
| A-11 | `DecisionService.ReplayStored` + `DecisionQueryService.Deserialize` + composer | Serialize/deserialize payload lặp (DRY) | `DecisionPayloadSerializer` | Application | 6 |
| A-12 | `IDecisionAuditStore` (3 method, 2 consumer) | ISP; sẽ phình ở 5.3 | `IDecisionEvaluationStore` + `IDecisionQueryStore` (E-07) | Application | 6 |
| A-13 | `ExceptionClassifier.RetryAfterSeconds = 5` | Giá trị vận hành hardcode | `Tadss:ErrorHandling:RetryAfterSeconds` | Runtime.Api | 7 |
| A-14 | `DecisionService.MaxIdempotencyKeyLength`, `DecisionsController.MaxIdempotencyKeyLength`, `CorrelationIdMiddleware.MaxCorrelationIdLength` (128) | Hằng contract lặp 3 nơi | `ContractLimits` | Application | 7 |
| A-15 | `Math.Max(RedisTimeoutMs, 3000)`, chuỗi `"your-redis-host"` | Timeout hardcode; chuỗi ma thuật | `RedisConnectTimeoutMs`; hằng có tên | Infrastructure | 7 |
| A-16 | Health check `"Neon-PostgreSQL"` | Tên gắn nhà cung cấp, sai khi chạy compose/Testcontainers | Hằng `"postgresql"` | Infrastructure | 7 |
| A-17 | `TadssCacheOptions.MaxMemoryCacheSize` | Option chết (không mã nào đọc) | Xóa khỏi options và `appsettings.json` | Infrastructure | 7 |
| A-18 | `TadssCacheOptions`, `DatabaseOptions`, `StrategyOptions` | Không có validator | Thêm validator + `ValidateOnStart` | Infrastructure / Application | 7 |
| A-19 | `"Redis"`/`"Memory"` so sánh chuỗi ở 2 nơi | Chuỗi lặp | Hằng `CacheProviders.Redis/Memory` trong `CacheOptions` | Infrastructure | 7 |
| A-20 | Entity `DecisionOutcome` | Trùng tên enum SDK `DecisionOutcome` | Hậu tố `Entity` cho 3 entity (E-09) | Infrastructure | 7 |
| A-21 | `TadssCacheOptions`, `V2Samples`, `V2TestData`, `SdkV2LayeringTests` | Tên lệch quy ước / còn "V2" | Đổi tên (E-11) | Infrastructure / tests | 1, 7 |
| A-22 | `MaxRateDecimals = 4` ở 2 options strategy + composer | Bất biến chỉ ràng bằng comment (minor 5.2c Task 1) | Test bất biến | tests | 8 |
| A-23 | `NoLegacyV1Tests` quét hẹp | Minor 5.2c Task 4 | Mở rộng phạm vi | tests | 8 |
| A-24 | Comment mô tả theo gói/phiên bản ("runtime v2", "5.2b", "5.2c") | Comment lịch sử, sai dần theo thời gian | Viết lại thành mô tả hành vi | Cả ba | 7 |
| A-25 | `Program.cs` (69 dòng, gồm migration + resolve sớm + pipeline) | Composition root lẫn tác vụ khởi động | `StartupTasks`, `UseTadssRequestPipeline` | Runtime.Api | 5 |

Không xử lý ở 5.2d (đổi hành vi, theo spec 4.2.4):
- Fail-fast khi thiếu connection string; options đọc payload audit nới lỏng; log 503 giả khi client hủy trong pha SQL → 5.3.
- `detail` của 400 chứa thông điệp STJ → 5.3 (gói bảo mật).
- EF log ERR cho mỗi 23505 mong đợi; prefix key cache cứng trong IT → 5.4.

## Ghi chú cho gói 5.3

- **Port quản trị:** `IDecisionGovernanceStore` (`GetPendingApprovalsAsync`, `InsertOverrideAsync`, `InsertOutcomeAsync`) đặt ở `Application/Abstractions/Persistence/`; `EfDecisionAuditStore` hiện thực thêm interface này.
- **Exception mới:** `DecisionStateConflictException` đặt ở `Application/Exceptions/`.
- **Middleware bảo mật** (SEC-1…7) chèn vào `UseTadssRequestPipeline` theo thứ tự ở spec 4.3.2. Test UT-14/pipeline mở rộng tương ứng.
