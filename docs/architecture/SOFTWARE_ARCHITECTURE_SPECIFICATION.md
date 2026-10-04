# ĐẶC TẢ KIẾN TRÚC PHẦN MỀM TADSS (SAS) — v2.0

> **Vai trò của tài liệu:** nguồn duy nhất về cấu trúc solution, thành phần runtime, strategy và luồng xử lý — gồm **hiện trạng sau gói 5.3** (v2 là phiên bản duy nhất, solution theo Clean Architecture nhiều project — ADR-013; đã có quản trị override/outcome/hàng chờ và lớp bảo mật) và **mục tiêu v2** còn lại của Phase 5. Contract HTTP: [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md). Schema DB: [`DECISION_PERSISTENCE_INVARIANTS.md`](DECISION_PERSISTENCE_INVARIANTS.md). Pipeline chịu lỗi: [`RESILIENCE_AND_FAULT_TAXONOMY.md`](RESILIENCE_AND_FAULT_TAXONOMY.md).

---

## 1. NGỮ CẢNH HỆ THỐNG (C4 — CONTAINER)

```mermaid
flowchart LR
    subgraph SC["SmashCourt"]
        FE["smashcourt-fe<br/>Next.js"]
        BE["SmashCourt-BE<br/>ASP.NET Core 8"]
    end
    subgraph T["TADSS"]
        API["TADSS.Runtime.Api<br/>ASP.NET Core 8"]
    end
    PG[("PostgreSQL 16<br/>schema public + tadss")]
    RD[("Redis 7")]
    OT["OpenTelemetry → grafana/otel-lgtm"]
    VN["VNPay"]

    FE -- "REST + JWT cookie" --> BE
    BE -- "REST /api/v2 + X-Api-Key" --> API
    BE --> PG
    BE --> VN
    API --> PG
    API --> RD
    API -. "traces, metrics, logs" .-> OT
    BE -. "traces" .-> OT
```

- FE chỉ gọi BE; BE là client duy nhất của TADSS trong SmashCourt.
- TADSS và SmashCourt dùng chung máy chủ PostgreSQL nhưng tách schema; không bên nào truy cập bảng của bên kia.

---

## 2. CẤU TRÚC SOLUTION (`TADSS.slnx`)

Clean Architecture nhiều project (ADR-013): `Shared.DecisionContractSDK` ← `TADSS.Application` ← `TADSS.Infrastructure` ← `TADSS.Runtime.Api`. Hai project strategy chỉ phụ thuộc SDK và được `TADSS.Runtime.Api` lắp vào lúc khởi động. Namespace bằng tên project cộng đường dẫn thư mục; mỗi file một kiểu public, tên file bằng tên kiểu.

```text
src/
├── Shared.DecisionContractSDK/        # Hợp đồng dùng chung v2 — chỉ BCL, 0 NuGet (README.md mô tả cây)
│   ├── Common/                        # ErrorCodes, ReasonCodes, PagedResult, TadssJson, converter JSON, ContractValidationException
│   ├── Decisions/                     # StrategyDecision, DecisionContext, StrategyCachePolicy, DecisionRecommendation
│   ├── Domain/                        # Reservation (aggregate root), ReservationItem, CustomerProfile, CustomerHistory, DemandSnapshot, BranchCalendar, PeakWindow + DomainInvariantException
│   ├── DTOs/{Decisions,Governance,Outcomes,Mappings}/
│   ├── Enums/                         # Channel, CustomerTier, DecisionStatus, ExecutionType, RecommendationType, OverrideAction, DecisionOutcome
│   └── Interfaces/                    # IReservationDecisionStrategy
├── TADSS.Application/                 # Use case + port + options nghiệp vụ; chỉ phụ thuộc SDK và abstraction Microsoft.Extensions.*
│   ├── ApplicationServiceCollectionExtensions.cs   # AddApplication(IServiceCollection, IConfiguration)
│   ├── Abstractions/
│   │   ├── Caching/                   # IStrategyDecisionCache, StrategyDecisionCacheKey
│   │   ├── Persistence/               # IDecisionEvaluationStore, IDecisionQueryStore, IDecisionGovernanceStore, DecisionExecutionRecord, DecisionDetailRecord, DecisionOverrideRecord, DecisionOutcomeRecord, ExecutionRole, InsertExecutionResult, InsertOverrideResult, InsertOutcomeResult, PendingApprovalsPage
│   │   └── Resilience/                # IStrategyPipeline, StrategyExecutionOutcome, FailureReasonPrefixes
│   ├── Common/                        # ContractLimits (giới hạn của contract), AuditTimestamps (cắt về µs)
│   ├── Decisions/                     # IDecisionService/DecisionService, IDecisionQueryService/DecisionQueryService (chi tiết + hàng chờ), StrategyEvaluator, DecisionPayloadSerializer, DecisionResponseComposer, ApprovalWindowPolicy, DepositCalculator, ItemDurationPolicy, RequestHasher, CanonicalNumberFormatter
│   ├── Governance/                    # IOverrideService/OverrideService, OverrideRequestValidator, OverridePayloadComparer, GovernanceResponseMapper
│   ├── Outcomes/                      # IOutcomeService/OutcomeService
│   ├── Exceptions/                    # DecisionNotFoundException, DecisionStateConflictException, IdempotencyConflictException, PersistenceUnavailableException, DecisionTimeoutException
│   ├── Options/                       # ApprovalOptions, ValidationOptions, CalendarOptions, StrategyOptions + validator (mỗi kiểu một file)
│   └── Strategies/                    # IStrategyRegistry, StrategyRegistry, RegisteredStrategy, OptionsFingerprintBuilder
├── TADSS.Infrastructure/              # Adapter của port; phụ thuộc Application + SDK + EF Core/Npgsql, Redis, Polly
│   ├── InfrastructureServiceCollectionExtensions.cs   # AddInfrastructure(IServiceCollection, IConfiguration)
│   ├── Caching/                       # StrategyDecisionCache (Redis + memory), CachedStrategyDecision, CachedRecommendation, RedisConnectionFactory, RedisHealthCheck, CacheHealthChecks
│   ├── HealthCheckTags.cs             # Tag `ready` của health check
│   ├── Options/                       # CacheOptions(+Validator), CacheProviders, DatabaseOptions(+Validator: chuỗi kết nối bắt buộc ở mọi môi trường), ResilienceOptions(+Validator)
│   ├── Persistence/                   # TadssDbContext, EfDecisionAuditStore (đọc: chi tiết, hàng chờ qua PendingApprovalsQuery; ghi audit), EfDecisionGovernanceStore (ghi override/outcome), DecisionAuditMapper, PersistenceFailureDetector + PersistenceFailureTranslator (internal), PersistenceHealthChecks, PersistenceServiceCollectionExtensions
│   │   ├── Entities/                  # DecisionExecutionEntity, DecisionOverrideEntity, DecisionOutcomeEntity
│   │   ├── Configurations/            # DecisionExecutionConfiguration, DecisionOverrideConfiguration, DecisionOutcomeConfiguration
│   │   └── Migrations/                # 4 migration + TadssDbContextModelSnapshot (mã sinh tự động, giữ nguyên ID)
│   └── Resilience/                    # StrategyPipeline (Polly v8), FallbackDecisionPolicy, FallbackCause
├── TADSS.Runtime.Api/                 # Presentation + composition root; được dùng mọi project
│   ├── Program.cs                     # Chỉ gọi AddApplication/AddInfrastructure/AddStrategies/AddPresentation, StartupTasks, UseTadssRequestPipeline, map endpoint
│   ├── Composition/                   # PresentationServiceCollectionExtensions (AddPresentation), SecurityServiceCollectionExtensions (AddTadssSecurity), StrategyServiceCollectionExtensions (AddStrategies), StrategyRegistrations, RequestPipelineExtensions (UseTadssRequestPipeline), EndpointMappingExtensions (MapTadssEndpoints), StartupTasks — chỗ duy nhất (cùng Program.cs) được dùng kiểu của Infrastructure
│   ├── Controllers/                   # DecisionsController (POST + GET /api/v2/decisions), DecisionApprovalsController (pending-approvals, override), DecisionOutcomesController (outcome)
│   ├── ErrorHandling/                 # ExceptionClassifier, ErrorClassification, ProblemResponse, ProblemResponseWriter (bộ ghi problem+json duy nhất), InvalidModelStateTranslator
│   ├── Middlewares/                   # CorrelationIdMiddleware, GlobalExceptionHandler, SecurityHeadersMiddleware, RequestSizeLimitMiddleware, ApiKeyAuthenticationMiddleware
│   ├── OpenApi/                       # RequiredHeaderOperationFilter, SecurityResponsesOperationFilter, ProblemDetailsSchemaFilter, LineEndingDocumentFilter
│   ├── Options/                       # ErrorHandlingOptions(+Validator), SecurityOptions(+Validator), ApiKeyClientOptions, RateLimitingOptions
│   ├── Security/                      # ApiKeyVerifier (SHA-256 + FixedTimeEquals), ApiClientIdentity, SecurityPaths
│   └── Common/                        # HttpHeaderNames, RequestHeaders
├── TADSS.Strategies.Default/          # DefaultDecisionStrategy + DefaultStrategyOptions — chỉ phụ thuộc SDK
└── TADSS.Strategies.MockAlternative/  # MockAlternativeDecisionStrategy + MockAlternativeStrategyOptions — chỉ phụ thuộc SDK
tests/
├── TADSS.UnitTests/                   # Cây mirror src: Architecture, Sdk/{Common,Contracts,Decisions,Domain}, Strategies, Application/{Abstractions,Decisions,Options,Strategies}, Infrastructure/{Caching,Persistence,Resilience}, Api/{Composition,Controllers,ErrorHandling,Middlewares,OpenApi,Security}, TestData, TestDoubles
└── TADSS.IntegrationTests/            # API qua WebApplicationFactory trên Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy): Migrations, Persistence (gồm store quản trị, hàng chờ), FaultInjection, Security (health, API key), endpoint (decisions, override, outcome, hàng chờ)/idempotency/cache, ComposeFixtureAlignment
```

Health check: `/health/live` (không check nào) và `/health/ready` (PostgreSQL, tên check `postgresql`, lỗi → 503; Redis, tên check `redis`, lỗi → `Degraded` vẫn 200) — map trong `Composition/EndpointMappingExtensions.MapTadssEndpoints`, đăng ký trong `AddInfrastructure`. Không còn endpoint `/health` gộp (F-16); body chỉ là chữ trạng thái.

Thứ tự middleware của request pipeline khai báo tại một nơi: `Composition/RequestPipelineExtensions.UseTadssRequestPipeline` — forwarded headers (chỉ khi `Tadss:Security:TrustForwardedHeaders`) → correlation id → exception handler → security headers + HSTS → request logging → https redirection → giới hạn kích thước body → Swagger (chỉ `Development`) → xác thực `X-Api-Key` → rate limiter → `UseRouting`. Không có `UseAuthorization` (không có policy). Thứ tự được khóa bằng test UT-14 (`SecurityPipelineOrderTests`, `RequestPipelineTests`); luồng chi tiết ở Mục 4.4.3.

---

## 3. HẠN CHẾ CÒN MỞ (sau gói 5.3)

- Các thành phần có **trạng thái theo từng instance** (rate limit SEC-1, circuit breaker, cache L1 `IMemoryCache`) không chia sẻ giữa nhiều instance của runtime: hạn mức thực tế nhân với số instance, breaker mở độc lập, L1 có thể lệch nhau (L2 Redis và idempotency qua PostgreSQL thì dùng chung). Đánh giá khi scale (SC-1, SC-3) thuộc gói 5.4–5.5; rate limit phân tán thuộc 5.5.
- Tranh chấp override ↔ outcome trên cùng quyết định không được khóa nguyên tử (INV-5, INTEGRATION_SPEC §4.3); tranh chấp override ↔ override thì do `UNIQUE(decision_id)` bảo đảm.
- Dòng log hoàn tất request của Serilog không mang `ClientId` (request logging đứng trước bước xác thực); `ClientId` có ở log scope của log trong controller/service và ở tag `tadss.client_id` của trace. Ngoài ra `UseSerilogRequestLogging` nằm trong exception handler nên phản hồi do exception tạo ra (400, 404, 409, 503, 504) bị ghi là "responded 500" ở mức Error (401/413/429 do middleware tạo thì đúng); sửa sẽ đổi thứ tự pipeline (F-09), để gói 5.4/5.5.
- Inner timeout chỉ có hiệu lực khi strategy tôn trọng `CancellationToken` (`IsolatedStrategyInvoker`, 5.4).
- Redis lỗi thì mỗi lần gọi cache chờ tới `RedisTimeoutMs` (circuit breaker + metric `bypass`, 5.4).
- Chọn strategy theo cấu hình toàn cục `Tadss:Strategy:ActiveStrategyName`; định tuyến theo request thuộc 5.4.

Test `NoLegacyV1Tests` (UT-09) chặn việc đưa lại kiểu, namespace hoặc package của phiên bản cũ. Ranh giới tầng và quy ước đặt tên/đặt chỗ được khóa bởi các test UT-14 (Mục 5).

---

## 4. KIẾN TRÚC MỤC TIÊU v2 (Phase 5)

### 4.1. Hợp đồng strategy v2

Strategy chỉ trả **quyết định nghiệp vụ**; runtime chịu trách nhiệm định danh, thời gian, quy đổi số tiền và telemetry.

```csharp
public interface IReservationDecisionStrategy
{
    string StrategyName { get; }
    string StrategyVersion { get; }
    StrategyCachePolicy CachePolicy { get; }          // Disabled | Enabled(Ttl)
    Task<StrategyDecision> EvaluateAsync(Reservation reservation, DecisionContext context, CancellationToken ct);
}

public sealed record StrategyDecision(
    DecisionStatus Status,
    decimal? DepositRate,                              // bắt buộc khi Approved
    string ReasonCode,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<DecisionRecommendation> Recommendations);
```

`DecisionContext` cung cấp thời điểm hiện tại theo giờ chi nhánh (`IClock` + `branchTimeZone`) để strategy không tự đọc đồng hồ hệ thống — giúp test tất định.

### 4.2. Thành phần runtime v2

> **Tiến độ (sau gói 5.3):** v2 là runtime duy nhất; mỗi thành phần nằm ở project/thư mục ghi ở cột **Vị trí** (cây ở Mục 2). Mỗi tầng tự đăng ký phần của mình: `AddApplication`, `AddInfrastructure`, `AddStrategies`, `AddPresentation` (trong đó `AddTadssSecurity`) (Mục 2, `Program.cs`). Hiện thực và có test: `DecisionService`, `DecisionQueryService` (chi tiết và hàng chờ), `OverrideService`, `OutcomeService`, `StrategyEvaluator`, `EfDecisionAuditStore`, `EfDecisionGovernanceStore`, `StrategyPipeline`, `StrategyDecisionCache`, `ExceptionClassifier` + `ProblemResponseWriter`, lớp bảo mật (API key, giới hạn kích thước, security headers, rate limiter), health live/ready, `DecisionsController`, `DecisionApprovalsController`, `DecisionOutcomesController` (năm endpoint, INTEGRATION_SPEC Mục 3). Chưa có: `StrategyRouter`/`ShadowExecutor`/`IsolatedStrategyInvoker`/breaker Redis (5.4) và OpenTelemetry (5.5) — các hàng dưới đây đánh dấu rõ phần đã có. Runtime chỉ dùng kiểu SDK v2 và mọi thời gian lấy từ `TimeProvider` (không gọi đồng hồ hệ thống trực tiếp).

| Thành phần | Vị trí | Trách nhiệm |
| :--- | :--- | :--- |
| `ApiKeyAuthenticationMiddleware` + `ApiKeyVerifier` | `TADSS.Runtime.Api/Middlewares`, `TADSS.Runtime.Api/Security` | Xác thực `X-Api-Key` cho `/api/*`: băm SHA-256 khóa nhận được rồi `CryptographicOperations.FixedTimeEquals` với **mọi** mục cấu hình (không thoát sớm); thiếu, sai hoặc lặp header → 401 `UNAUTHORIZED` + `WWW-Authenticate` qua `ProblemResponseWriter`. Miễn `/health/live`, `/health/ready` (mọi môi trường) và `/swagger/*` (chỉ `Development`). Khóa đúng thì `ClientId` vào `ApiClientIdentity` (cho rate limiter), log scope và tag `tadss.client_id`. Không bao giờ log khóa hay mã băm. *Đã hiện thực* |
| `RequestSizeLimitMiddleware`, `SecurityHeadersMiddleware`, rate limiter | `TADSS.Runtime.Api/Middlewares`, `TADSS.Runtime.Api/Composition/SecurityServiceCollectionExtensions` | Giới hạn body `Tadss:Security:MaxRequestBodyBytes` (413 `PAYLOAD_TOO_LARGE`, kiểm tra `Content-Length` rồi đặt trần cho body chunked); header bảo mật + HSTS gắn qua `OnStarting` nên có cả trên response lỗi; token bucket theo `ClientId` (429 `RATE_LIMITED` + `Retry-After`, `QueueLimit = 0`, `/health/*` không giới hạn). Options `SecurityOptions` + `SecurityOptionsValidator` (fail-fast, ngoài `Development`/`Testing` bắt buộc có khóa và từ chối khóa local công khai `tadss-local-dev-key`). *Đã hiện thực* |
| `ProblemResponseWriter` + `ProblemResponse` | `TADSS.Runtime.Api/ErrorHandling` | Bộ ghi duy nhất dựng body `application/problem+json` (`code`, `traceId`, `retryAfterSeconds`) cho `GlobalExceptionHandler`, 401, 413, 429; các từ chối do middleware tạo không ném exception. *Đã hiện thực* |
| `IDecisionService` / `DecisionService` | `TADSS.Application/Decisions` | Điều phối: idempotency → định tuyến → cache → pipeline → approval window → ghi audit → response. *Đã hiện thực*: một mốc `nowUtc` từ `TimeProvider` (cắt về µs) cho cả lần đánh giá; lỗi cache chỉ là miss; lỗi ghi audit lan ra (fail-closed, INV-10); chọn strategy tạm bằng `IStrategyRegistry.ResolveActive()` cho tới khi có `StrategyRouter` (5.4). Đăng ký scoped trong `AddApplication` |
| `StrategyEvaluator` | `TADSS.Application/Decisions` | Dựng khóa cache → đọc cache (lỗi cache chỉ là miss) → chạy `IStrategyPipeline` khi miss → ghi cache khi kết quả là `AUTOMATED`; trả `StrategyEvaluation` (kết quả + cờ cache hit). Chỉ hủy của chính caller được lan ra. Singleton |
| `DecisionPayloadSerializer` | `TADSS.Application/Decisions` | Serialize `request_payload`/`response_payload` bằng `TadssJson.Options` (chặt); **đọc** bằng bản sao nới lỏng (bỏ qua trường lạ, không bắt buộc trường) để bản ghi cũ không thành 500 khi DTO thêm trường; dùng chung cho composer, replay, `DecisionQueryService` và `OverrideService` |
| `IDecisionEvaluationStore`, `IDecisionQueryStore` | `TADSS.Application/Abstractions/Persistence` | Port lưu audit tách theo consumer (ISP). `IDecisionEvaluationStore` (cho `DecisionService`): `FindPrimaryByIdempotencyKeyAsync`, `InsertExecutionAsync` (append-only; tranh chấp `uq_decision_idempotency_key` trả bản đã thắng; lỗi tạm thời → `PersistenceUnavailableException` → 503). `IDecisionQueryStore` (cho `DecisionQueryService`): `GetDecisionDetailAsync`. `GetPendingApprovalsAsync` (hàng chờ). `IDecisionGovernanceStore` (cho `OverrideService`/`OutcomeService`, chỉ ghi): `InsertOverrideAsync`, `InsertOutcomeAsync` (`UNIQUE(decision_id)`; thua tranh chấp trả bản đã thắng với `IsExistingWinner`) |
| `EfDecisionAuditStore`, `EfDecisionGovernanceStore` | `TADSS.Infrastructure/Persistence` | `EfDecisionAuditStore` hiện thực `IDecisionEvaluationStore` và `IDecisionQueryStore` bằng EF Core (scoped, một instance mỗi scope); hàng chờ chạy bằng SQL có tham số trong `PendingApprovalsQuery` (dạng dùng được partial index `idx_de_pending`, IT-09). `EfDecisionGovernanceStore` hiện thực `IDecisionGovernanceStore` (tách file để giữ trần 250 dòng). Dịch lỗi tạm thời của PostgreSQL thành `PersistenceUnavailableException` (hoặc `OperationCanceledException` khi caller đã hủy) tại ranh giới adapter qua `PersistenceFailureDetector`/`PersistenceFailureTranslator` (`internal`) |
| `DecisionResponseComposer` | `TADSS.Application/Decisions` | Ghép `DecisionResultDto` và `DecisionExecutionRecord` từ cùng một nguồn (response ⇔ `response_payload`); áp approval window và quy đổi cọc; `CACHE_HIT` sinh `decisionId`/`created_at` mới |
| `StrategyRouter` (`Tadss:Routing`) | `TADSS.Application/Strategies` (dự kiến) | Chọn strategy theo từng request dựa trên quy tắc cấu hình (ví dụ giờ cao điểm → `MockAlternativeDecisionStrategy`; mặc định → `DefaultDecisionStrategy`); reload cấu hình không cần restart. *Chưa hiện thực — gói 5.4* |
| `ShadowExecutor` (`Tadss:Shadow`) | `TADSS.Application/Decisions` (dự kiến) | Chạy strategy ứng viên song song sau khi có kết quả chính; lưu bản ghi `SHADOW`; không ảnh hưởng response và độ trễ trả về. *Chưa hiện thực — gói 5.4* |
| `IsolatedStrategyInvoker` | `TADSS.Infrastructure/Resilience` (dự kiến) | Thực thi strategy sao cho timeout cắt được cả strategy không hợp tác. *Chưa hiện thực — gói 5.4* |
| `IStrategyPipeline` / `StrategyPipeline` | Port: `TADSS.Application/Abstractions/Resilience`; hiện thực: `TADSS.Infrastructure/Resilience` | Pipeline Polly v8 quanh lời gọi strategy (outer timeout → fallback → retry → circuit breaker → inner timeout). *Đã hiện thực* (`ResilienceOptions`; `ResilienceOptionsValidator` kiểm tra ngân sách `OuterTimeoutMs` ≥ tổng thời gian tối đa của inner timeout + retry và chặn khởi động nếu sai). Outer timeout được dịch thành `DecisionTimeoutException` (Application) ngay tại adapter; pipeline theo strategy và `IsolatedStrategyInvoker` thuộc 5.4 |
| `ApprovalWindowPolicy` (`Tadss:Approval`) | `TADSS.Application/Decisions` | Tính `approvalExpiresAt`; đổi `NEED_APPROVAL` → `APPROVED` 100% khi không còn cửa sổ duyệt (`reasonCode` = `APPROVAL_WINDOW_UNAVAILABLE`, `reasons` là một câu giải thích, kết quả gốc của strategy giữ ở `strategy_status`). *Đã hiện thực* |
| `DepositCalculator` | `TADSS.Application/Decisions` | Một nơi quy đổi cọc: `Calculate` (`amount = round(totalPrice × rate)` theo đơn vị tiền tệ) và `FromAmount` (`rate = round(amount / totalPrice, 4)` cho override gửi `amount`). *Đã hiện thực* |
| `IStrategyDecisionCache` / `StrategyDecisionCache` | Port + `StrategyDecisionCacheKey`: `TADSS.Application/Abstractions/Caching`; hiện thực + `CachedStrategyDecision`: `TADSS.Infrastructure/Caching` | Chỉ cache khi strategy khai báo `Enabled`; key gồm hash request + tên/phiên bản strategy. *Đã hiện thực*: chỉ lưu `StrategyDecision` (không lưu response — mỗi lần cache hit nhận `decisionId` mới), chỉ ghi kết quả `AUTOMATED` (không ghi fallback), key `tadss:v2:{tên}:{phiên bản}:{optionsFingerprint}:{request_hash}` (fingerprint của mọi strategy gồm cả lịch cao điểm `Tadss:Calendar`), hai tầng Redis/memory (`Tadss:Cache:Provider`), lỗi cache chỉ là miss. Circuit breaker Redis và metric `bypass` thuộc 5.4 |
| `IStrategyRegistry` / `StrategyRegistry`, `StrategyRegistrations` | Registry: `TADSS.Application/Strategies`; dựng strategy cụ thể từ cấu hình: `TADSS.Runtime.Api/Composition` | Registry giữ strategy đã đăng ký cùng `optionsFingerprint`; việc chọn và dựng `DefaultDecisionStrategy`/`MockAlternativeDecisionStrategy` là việc lắp ráp nên nằm ở composition root (Application không tham chiếu Strategies) |
| `ExceptionClassifier` + `GlobalExceptionHandler` | `TADSS.Runtime.Api/ErrorHandling`, `TADSS.Runtime.Api/Middlewares` | Bảng map exception → HTTP/`code` v2 (INTEGRATION_SPEC §3.8). *Hàm thuần, nguồn duy nhất của `GlobalExceptionHandler`*; chỉ biết exception của SDK và `TADSS.Application.Exceptions` (không biết Polly/Npgsql): `PersistenceUnavailableException` → 503, `DecisionTimeoutException` → 504, cả hai kèm `Retry-After` lấy từ `Tadss:ErrorHandling:RetryAfterSeconds` (mặc định 5; hợp lệ 1..3600). Gồm `MISSING_HEADER`, `DECISION_NOT_FOUND` (404), `DecisionStateConflictException` → 409 theo mã (`NOT_PENDING_APPROVAL`, `ALREADY_OVERRIDDEN`, `APPROVAL_EXPIRED`, `OUTCOME_ALREADY_RECORDED`), `BadHttpRequestException` 413 → `PAYLOAD_TOO_LARGE` (body chunked vượt trần); duyệt cả `AggregateException`. 415 do MVC trả mặc định, không đi qua bảng này |
| `IOverrideService` / `OverrideService`, `OverrideRequestValidator` | `TADSS.Application/Governance` | Override của Manager (INTEGRATION_SPEC §3.6): kiểm tra hình thức → tồn tại và `PRIMARY` → số tiền cọc so với đơn → replay/xung đột nếu đã có override → `NEED_APPROVAL` và chưa có outcome → chưa hết hạn → ghi qua `IDecisionGovernanceStore`; thua `UNIQUE(decision_id)` thì xử lý như đã có override. Quy đổi cọc bằng `DepositCalculator`. *Đã hiện thực* (sơ đồ ở 4.4.4) |
| `IOutcomeService` / `OutcomeService` | `TADSS.Application/Outcomes` | Ghi nhãn kết quả (§3.7): áp cho mọi quyết định `PRIMARY`; chống trùng theo nội dung (cùng → 200, khác → 409 `OUTCOME_ALREADY_RECORDED`); `X-Idempotency-Key` chỉ kiểm tra, không lưu. *Đã hiện thực* |
| Hàng chờ duyệt | `DecisionQueryService.GetPendingApprovalsAsync` (Application), `PendingApprovalsQuery` (Infrastructure) | Truy vấn theo Mục B.5 của DECISION_PERSISTENCE_INVARIANTS: bản `PRIMARY` mới nhất của mỗi `reservationId`, `NEED_APPROVAL`, chưa override/outcome, `approvalExpiresAt > now` (`now` từ `TimeProvider`), phân trang `page ≥ 1`, `pageSize` 1..100. *Đã hiện thực* |
| Health check | `TADSS.Infrastructure` (`PersistenceHealthChecks`, `CacheHealthChecks`, `RedisHealthCheck`), map ở `EndpointMappingExtensions` | `/health/live` không check; `/health/ready`: PostgreSQL bắt buộc (503), Redis `Degraded` (200). *Đã hiện thực* |
| OpenTelemetry | `TADSS.Runtime.Api/Composition` (dự kiến) | Trace (ASP.NET Core, HttpClient, Npgsql, Redis), metric, log (Serilog → OTLP). *Chưa hiện thực* |

Ghi audit là lời gọi đồng bộ qua `IDecisionEvaluationStore` (không qua message bus, D-02); `created_at` do `DecisionService` gán từ `TimeProvider`.

### 4.3. Strategy v2 (tham số mặc định, chỉnh qua cấu hình)

**`DefaultDecisionStrategy` 2.0.0 — cọc theo hạng thành viên** (`Tadss:Strategies:Default`)

| Điều kiện (kiểm tra theo thứ tự) | Kết quả | `reasonCode` |
| :--- | :--- | :--- |
| `totalPrice ≥ 2.000.000` | `NEED_APPROVAL` | `HIGH_VALUE_REVIEW` |
| `history.noShowCount ≥ 2` | `NEED_APPROVAL` | `NO_SHOW_HISTORY_REVIEW` |
| Khách vãng lai hoặc `STANDARD` | `APPROVED`, cọc 50% | `TIER_DEPOSIT` |
| `SILVER` | `APPROVED`, cọc 40% | `TIER_DEPOSIT` |
| `GOLD` | `APPROVED`, cọc 30% | `TIER_DEPOSIT` |
| `PLATINUM`, `DIAMOND` | `APPROVED`, cọc 20% | `TIER_DEPOSIT` |

**`MockAlternativeDecisionStrategy` 2.0.0 — cọc theo khung giờ** (`Tadss:Strategies:MockAlternative`)

| Điều kiện | Kết quả | `reasonCode` |
| :--- | :--- | :--- |
| Giờ cao điểm và `totalPrice ≥ 1.500.000` | `NEED_APPROVAL` | `HIGH_VALUE_REVIEW` |
| Giờ cao điểm | `APPROVED`, cọc 50% | `PEAK_HOUR_DEPOSIT` |
| Ngoài giờ cao điểm | `APPROVED`, cọc 30% | `OFF_PEAK_DEPOSIT` |

Giờ cao điểm (theo giờ chi nhánh, `Tadss:Calendar:PeakWindows`): thứ Hai–thứ Sáu 17:00–21:00; thứ Bảy–Chủ Nhật 08:00–21:00. Với booking nhiều sân, giờ cao điểm xét theo khung giờ bắt đầu sớm nhất.

**Định tuyến mặc định** (`Tadss:Routing`): request có giờ cao điểm → `MockAlternativeDecisionStrategy`; còn lại → `DefaultDecisionStrategy`. Shadow mode tắt mặc định.

**Fallback (`FallbackDecisionPolicy`)**: luôn `APPROVED` với cọc 100% (tỷ lệ 1, `deposit.amount = totalPrice`), không có khuyến nghị và không có hạn duyệt; `reasonCode` theo nguyên nhân (`FALLBACK_STRATEGY_TIMEOUT`, `FALLBACK_STRATEGY_EXCEPTION`, `FALLBACK_CIRCUIT_OPEN`). Mọi sự cố kỹ thuật dùng chung quy tắc cọc 100% (như khi SmashCourt tắt flag hoặc không gọi được TADSS); `NEED_APPROVAL` chỉ dành cho bất định nghiệp vụ do strategy quyết định, nên quyết định fallback không vào hàng chờ duyệt và không override được (D-13, 04/10/2026; trước đó fallback trả `NEED_APPROVAL`). Khi fallback thì `strategyName`/`strategyVersion` trong response và audit vẫn là của **strategy đã được chọn** (không đổi thành tên fallback); nhận biết fallback qua `executionType = FALLBACK`, `fallbackUsed = true` và `reasonCode` `FALLBACK_*`. Approval window không tác động lên fallback (chỉ áp cho `NEED_APPROVAL`), nên `strategy_status` trong audit cũng là `APPROVED`. Kết quả fallback không được ghi cache.

### 4.4. Luồng xử lý `POST /api/v2/decisions`

#### 4.4.1. Hiện thực — qua các tầng (đánh giá quyết định)

Request tới đây đã qua lớp bảo mật (Mục 4.4.3). Mỗi `participant` ghi kèm project chứa nó. Mũi tên xuyên ranh giới tầng luôn đi qua port của `TADSS.Application` (`IStrategyPipeline`, `IStrategyDecisionCache`, `IDecisionEvaluationStore`); `DecisionService` không biết Polly, Redis hay EF Core.

```mermaid
sequenceDiagram
    autonumber
    participant BE as SmashCourt-BE
    participant MW as Middlewares / ExceptionHandler (Runtime.Api)
    participant CT as DecisionsController (Runtime.Api)
    participant DS as DecisionService (Application)
    participant EV as StrategyEvaluator (Application)
    participant CA as StrategyDecisionCache (Infrastructure)
    participant PL as StrategyPipeline (Infrastructure)
    participant ST as Strategy (SDK contract)
    participant AU as EfDecisionAuditStore (Infrastructure)
    participant DB as PostgreSQL (tadss)

    BE->>MW: POST /api/v2/decisions (X-Idempotency-Key)
    MW->>CT: CorrelationIdMiddleware gán traceId
    CT->>CT: Kiểm tra header (thiếu → 400 MISSING_HEADER, quá 128 ký tự → 400)
    CT->>DS: EvaluateAsync(request, key, correlationId, ct)
    DS->>DS: ToDomain + RequestHasher (sai hình thức → 400, chưa chạm I/O)
    DS->>AU: FindPrimaryByIdempotencyKeyAsync(key)
    AU->>DB: SELECT decision_executions
    alt Đã có bản ghi PRIMARY (replay)
        DB-->>AU: Bản ghi cũ
        AU-->>DS: DecisionExecutionRecord
        alt Cùng request_hash
            DS-->>CT: Response đã lưu (DecisionPayloadSerializer)
            CT-->>BE: 200 (kết quả cũ, strategy không chạy lại)
        else Khác request_hash
            DS-->>MW: IdempotencyConflictException
            MW-->>BE: 409 IDEMPOTENCY_KEY_REUSE
        end
    else Chưa có
        DS->>DS: ItemDurationPolicy (P-01), IStrategyRegistry.ResolveActive, nowUtc từ TimeProvider
        DS->>EV: EvaluateAsync(strategy, reservation, context, requestHash)
        alt Cache miss và pipeline vượt outer timeout
            EV->>PL: ExecuteAsync
            PL-->>MW: DecisionTimeoutException
            MW-->>BE: 504 GATEWAY_TIMEOUT + Retry-After (kết thúc, không ghi audit)
        else Có kết quả đánh giá
            opt Strategy khai báo cache Enabled
                EV->>CA: GetAsync(key)
                CA-->>EV: StrategyDecision hoặc miss (lỗi cache cũng chỉ là miss)
            end
            alt Cache hit
                EV-->>DS: StrategyEvaluation (CACHE_HIT)
            else Miss
                EV->>PL: ExecuteAsync (outer timeout, fallback, retry, breaker, inner timeout)
                PL->>ST: EvaluateAsync
                alt Strategy trả kết quả
                    ST-->>PL: StrategyDecision
                    PL-->>EV: AUTOMATED
                    opt Strategy cho phép cache
                        EV->>CA: SetAsync (lỗi ghi cache bị bỏ qua)
                    end
                else Strategy lỗi, quá hạn inner timeout hoặc breaker mở
                    PL-->>EV: Fallback APPROVED cọc 100% (FALLBACK, không ghi cache)
                end
                EV-->>DS: StrategyEvaluation
            end
            DS->>DS: DecisionResponseComposer (approval window, quy đổi cọc, response ⇔ response_payload)
            DS->>AU: InsertExecutionAsync(record) (fail-closed, INV-10)
            AU->>DB: INSERT decision_executions
            alt Ghi thành công
                AU-->>DS: Inserted
                DS-->>CT: DecisionResultDto
                CT-->>BE: 200 DecisionResult v2
            else Tranh chấp uq_decision_idempotency_key (23505)
                AU-->>DS: Bản ghi của request thắng
                alt Cùng request_hash
                    DS-->>CT: Response đã lưu
                    CT-->>BE: 200 (cùng kết quả với request thắng)
                else Khác request_hash
                    DS-->>MW: IdempotencyConflictException
                    MW-->>BE: 409 IDEMPOTENCY_KEY_REUSE
                end
            else Lỗi tạm thời của PostgreSQL (40001, 40P01, mất kết nối)
                AU-->>MW: PersistenceUnavailableException
                MW-->>BE: 503 PERSISTENCE_UNAVAILABLE + Retry-After, không có response nào được trả
            end
        end
    end
    Note over BE,MW: Caller hủy (client ngắt kết nối) ở bất kỳ bước nào: OperationCanceledException lan ra,<br/>GlobalExceptionHandler chỉ ghi log mức thông tin và không ghi response
```

Quy ước của luồng:

- Lỗi trước bước ghi audit (validation, P-01, xung đột idempotency, outer timeout) không ghi bản ghi audit.
- Mọi exception đi tới `GlobalExceptionHandler` và được `ExceptionClassifier` ánh xạ sang HTTP/`code`; `Retry-After` của 503/504 lấy từ `Tadss:ErrorHandling:RetryAfterSeconds`.
- Hủy của caller thắng outer timeout khi hai việc cùng xảy ra (`StrategyPipeline` ném `OperationCanceledException` thay vì `DecisionTimeoutException`).

#### 4.4.2. Luồng đích v2 (gồm phần chưa hiện thực)

Sơ đồ dưới thêm các bước chưa có mã: `StrategyRouter` và shadow (gói 5.4). Xác thực `X-Api-Key` đã hiện thực (Mục 4.4.3).

```mermaid
sequenceDiagram
    autonumber
    participant BE as SmashCourt-BE
    participant API as TADSS API
    participant R as StrategyRouter
    participant C as Cache (Redis)
    participant P as Polly pipeline
    participant S as Strategy
    participant DB as PostgreSQL (tadss)

    BE->>API: POST /api/v2/decisions (X-Api-Key, X-Idempotency-Key)
    API->>DB: Tra idempotency_key
    alt Đã có, cùng request_hash
        DB-->>API: Bản ghi cũ
        API-->>BE: 200 (kết quả cũ)
    else Mới
        API->>R: Chọn strategy theo request
        API->>C: Đọc cache (nếu strategy cho phép)
        alt Cache hit
            C-->>API: StrategyDecision
        else Miss
            API->>P: Thực thi (outer 500ms)
            P->>S: EvaluateAsync (inner 120ms, retry, circuit)
            S-->>P: StrategyDecision hoặc lỗi → Fallback
            P-->>API: Kết quả
            API->>C: Ghi cache (nếu cho phép)
        end
        API->>API: Approval window + tính số tiền cọc
        API->>DB: INSERT decision_executions (fail-closed)
        API-->>BE: 200 DecisionResult v2
        API-)S: (shadow, nếu bật) chạy strategy ứng viên, INSERT bản ghi SHADOW
    end
```

#### 4.4.3. Lớp bảo mật trước controller (mọi request `/api/*`)

Thứ tự khai báo tại `UseTadssRequestPipeline` (Mục 2). Mọi từ chối do middleware tạo đều đi qua `ProblemResponseWriter` (không ném exception); security headers và request logging đứng trước các bước từ chối nên có cả trên 401, 413, 429.

```mermaid
sequenceDiagram
    autonumber
    participant BE as SmashCourt-BE
    participant PRE as Forwarded headers, correlation id, exception handler,<br/>security headers + HSTS, request logging, https redirection
    participant SZ as RequestSizeLimitMiddleware
    participant AK as ApiKeyAuthenticationMiddleware
    participant RL as Rate limiter (token bucket theo ClientId)
    participant RT as Routing → Controller

    BE->>PRE: request
    PRE->>SZ: tiếp tục
    alt Content-Length > Tadss:Security:MaxRequestBodyBytes
        SZ-->>BE: 413 PAYLOAD_TOO_LARGE (chưa cần khóa, không đọc body)
    else Vừa trần
        SZ->>AK: tiếp tục (body chunked vượt trần thì Kestrel báo 413 qua ExceptionClassifier)
        alt /api/* thiếu, sai hoặc lặp X-Api-Key
            AK-->>BE: 401 UNAUTHORIZED + WWW-Authenticate
        else Khóa đúng (hoặc /health/*, /swagger/* ở Development)
            AK->>RL: ClientId vào ApiClientIdentity, log scope, tag tadss.client_id
            alt Hết token của ClientId (không áp dụng /health/*)
                RL-->>BE: 429 RATE_LIMITED + Retry-After
            else Còn token
                RL->>RT: định tuyến → controller → service
            end
        end
    end
```

#### 4.4.4. Luồng override của Manager (`POST /api/v2/decisions/{decisionId}/override`)

Thứ tự kiểm tra theo INTEGRATION_SPEC §3.6; dừng ở bước đầu tiên không đạt. `X-Idempotency-Key` và `X-Actor-Id` do controller kiểm tra trước khi gọi service.

```mermaid
sequenceDiagram
    autonumber
    participant BE as SmashCourt-BE
    participant CT as DecisionApprovalsController
    participant OS as OverrideService (Application)
    participant QS as IDecisionQueryStore
    participant GS as IDecisionGovernanceStore
    participant DB as PostgreSQL (tadss)

    BE->>CT: POST override (X-Api-Key, X-Idempotency-Key, X-Actor-Id)
    CT->>OS: OverrideAsync(decisionId, request, actorId, key)
    OS->>OS: Validate hình thức (400 VALIDATION_ERROR)
    OS->>QS: GetDecisionDetailAsync
    alt Không có hoặc là bản SHADOW
        OS-->>BE: 404 DECISION_NOT_FOUND
    else Có bản PRIMARY
        OS->>OS: ValidateAgainstOrder (amount ≤ totalPrice, số chữ số thập phân; 400)
        alt Đã có override
            alt Cùng key và cùng nội dung
                OS-->>BE: 200 bản đã lưu (replay)
            else Cùng key, khác nội dung
                OS-->>BE: 409 IDEMPOTENCY_KEY_REUSE
            else Khác key
                OS-->>BE: 409 ALREADY_OVERRIDDEN
            end
        else Chưa có override
            alt status ≠ NEED_APPROVAL hoặc đã có outcome
                OS-->>BE: 409 NOT_PENDING_APPROVAL
            else approvalExpiresAt ≤ now
                OS-->>BE: 409 APPROVAL_EXPIRED
            else Hợp lệ
                OS->>GS: InsertOverrideAsync (DepositCalculator quy đổi rate/amount)
                GS->>DB: INSERT decision_overrides
                alt Thua tranh chấp UNIQUE(decision_id)
                    GS-->>OS: bản đã thắng
                    OS-->>BE: xử lý như "đã có override" với bản thắng (200 hoặc 409)
                else Ghi được
                    OS-->>BE: 200 (deposit đã quy đổi, overriddenAt UTC)
                end
            end
        end
    end
```

Kiểm tra "chưa có outcome" rồi mới ghi override không khóa nguyên tử với việc ghi outcome (F-06); giới hạn này và cách BE xử lý ghi ở INTEGRATION_SPEC §4.3 và INV-5.

### 4.5. Metric OpenTelemetry

| Metric | Loại | Nhãn |
| :--- | :--- | :--- |
| `tadss.decisions` | Counter | `status`, `strategy`, `execution_type` |
| `tadss.decision.duration` | Histogram (ms) | `endpoint`, `status_code` |
| `tadss.pipeline.duration` | Histogram (ms) | `strategy`, `outcome` |
| `tadss.fallbacks` | Counter | `reason` |
| `tadss.circuit.state` | Gauge | `strategy` |
| `tadss.cache.requests` | Counter | `result` (`hit`, `miss`, `bypass`) |
| `tadss.pending_approvals` | Gauge | `branch` |
| `tadss.overrides` | Counter | `action` |

---

## 5. NGUYÊN TẮC PHỤ THUỘC

| Project | Được phụ thuộc | Cấm |
| :--- | :--- | :--- |
| `Shared.DecisionContractSDK` | Chỉ BCL | Mọi project, mọi NuGet |
| `TADSS.Strategies.*` | Chỉ SDK | Application, Infrastructure, Runtime.Api, NuGet |
| `TADSS.Application` | SDK + gói `Microsoft.Extensions.{Logging.Abstractions, Options.ConfigurationExtensions}` (kéo theo `DependencyInjection.Abstractions`, `Options`) | Infrastructure, Runtime.Api, Strategies, ASP.NET Core, EF Core, Npgsql, StackExchange.Redis, Polly |
| `TADSS.Infrastructure` | Application + SDK + EF Core/Npgsql, StackExchange.Redis, Polly, `Microsoft.Extensions.*` | Runtime.Api, ASP.NET Core (assembly `Microsoft.AspNetCore.*`, `FrameworkReference`, Web SDK), Swashbuckle |
| `TADSS.Runtime.Api` | Tất cả project `src/` + Swashbuckle, Serilog, EF Core Design. Chỉ `Program.cs` và `Composition/` được dùng kiểu của Infrastructure | Controller, middleware, `ErrorHandling/`, `OpenApi/` dùng `TADSS.Infrastructure`, Npgsql, Polly, Redis, EF Core |
| `tests/*` | Mọi project trên | — |

Quy ước đi kèm: interface của **port** (mà Infrastructure hiện thực) nằm ở `TADSS.Application/Abstractions/**`; interface của service nội bộ Application nằm cùng thư mục với hiện thực; exception của runtime chỉ ở `TADSS.Application/Exceptions`; mỗi project tự đăng ký phần của mình qua `Add{Layer}`.

Được kiểm tra tự động (UT-09 và UT-14):

| Test | Quy tắc khóa |
| :--- | :--- |
| `SdkDependencyPurityTests`, `StrategyDependencyPurityTests` (UT-09) | SDK chỉ BCL; Strategies chỉ SDK |
| `ProjectReferenceRulesTests` | Mỗi project `src/` chỉ có `ProjectReference` trong allowlist; Application và Infrastructure chỉ dùng `PackageReference` trong allowlist, dùng `Microsoft.NET.Sdk` (không Web SDK) và không có `FrameworkReference` (đọc thẳng csproj) |
| `LayerDependencyTests` | Application/Infrastructure không tham chiếu assembly hạ tầng/presentation bị cấm (Infrastructure: không `Microsoft.AspNetCore.*`, Swashbuckle, Runtime.Api); EF Core chỉ trong `Persistence/`; port chỉ ở `Application.Abstractions.*` |
| `CompositionRootBoundaryTests` | Runtime.Api ngoài `Program.cs`/`Composition/` không dùng kiểu Infrastructure, Npgsql, Polly, Redis, EF Core (văn bản nguồn và reflection trên chữ ký/trường); không file nào, kể cả `Composition/`, có `global using` tới các namespace đó và csproj không có `<Using Include>` tới chúng |
| `NamingConventionTests` | Namespace khớp project + thư mục; không còn "V2" trong tên kiểu/namespace; `*Options`/`*OptionsValidator` ở `Options/`; exception chỉ ở `Application.Exceptions` |
| `SourceFileConventionTests` | Mỗi file một kiểu public, tên file = tên kiểu; file nguồn ≤ 250 dòng (trừ `Migrations/`); số kiểu public bộ quét văn bản tìm thấy bằng số kiểu public không lồng của assembly (bắt kiểu thụt lề/namespace dạng khối) |
| `SecurityPipelineOrderTests`, `RequestPipelineTests` | Thứ tự middleware qua hành vi quan sát được: 413 trước 401, 401 trước 429, rate limiter đứng sau xác thực, routing sau các bước bảo mật; test bảo mật không cần DB nằm trong `TADSS.UnitTests/Api/Security` (host in-process), không phải `IntegrationTests` |
| `AppSettingsValidationTests` | `appsettings.json` qua mọi validator khi host khởi động; mọi options có hằng `SectionName` (tìm bằng reflection) có đúng một `IValidateOptions<T>` và đăng ký `ValidateOnStart` (không có ngoại lệ; `DatabaseOptions` kiểm chuỗi kết nối, chuỗi giữ chỗ trong `appsettings.json` được test thay bằng chuỗi test) |

Danh mục đầy đủ các test UT-14: [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).

---

## 6. KIẾN TRÚC KIỂM THỬ

Danh mục kiểm thử, môi trường và công cụ: xem [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).
