# Gói 5.4 — Runtime thích ứng: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Biến runtime từ "một strategy cố định cho mọi request" thành runtime thích ứng:
- Chọn strategy **theo từng request** bằng quy tắc cấu hình (`Tadss:Routing`), nạp lại nóng (T5.9).
- Chạy **shadow** một strategy ứng viên sau khi bản ghi `PRIMARY` đã lưu, không ảnh hưởng response (T5.9, INV-8).
- **Pipeline Polly riêng cho từng strategy** (sửa V-2) và **cách ly strategy không hợp tác** để inner timeout cắt được cả strategy bỏ qua token (T5.10, FI-05).
- **Circuit breaker cho Redis**: Redis hỏng thì bỏ qua Redis ngay, không chờ timeout ở mỗi request (T5.11).
- Chứng minh **idempotency và cache đúng khi chạy nhiều instance** (SCALE-1, P-04).
- Có decorator **tiêm lỗi strategy bằng cấu hình** (chỉ môi trường `Testing`) để gói 5.5 dùng cho kịch bản sự cố dưới tải (LT-03).

**Architecture:** Giữ Clean Architecture của 5.2d/5.3.
- **Application:** quy tắc định tuyến (`Routing/`), shadow (`Shadow/`: hàng đợi, điều phối, thực thi một mục), `TadssMetrics` (Meter `TADSS.Runtime`, chỉ dùng BCL).
- **Infrastructure:** pipeline Polly theo strategy (registry của Polly), `IsolatedStrategyInvoker`, decorator tiêm lỗi (`FaultInjection/`), adapter đọc/theo dõi cấu hình định tuyến (`Configuration/`), Redis qua pipeline riêng (timeout + breaker), worker nền chạy shadow.
- **Runtime.Api:** chỉ lắp ráp (composition) và kiểm tra sớm khi khởi động.

**Tech Stack:**
- **Không thêm NuGet** vào `src/`. Dùng sẵn có:
  - `ResiliencePipelineRegistry<TKey>` của Polly.Core 8.4.2.
  - `System.Threading.Channels`, `System.Diagnostics.Metrics` (BCL .NET 8).
  - `BackgroundService` của `Microsoft.Extensions.Hosting.Abstractions` (Infrastructure đã có qua phụ thuộc bắc cầu của HealthChecks, bản 8.0.0 trong lock file).
- Nền tảng như 5.3: .NET 8, EF Core 8 + Npgsql 8, StackExchange.Redis 2.8.16, Swashbuckle 6.6.2.
- Kiểm thử như 5.3: Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy), xUnit 2.5.3, FluentAssertions 6.12.1, Moq 4.20.72, `FakeTimeProvider`.

**Spec:** [`PHASE5_DESIGN_SPEC.md`](PHASE5_DESIGN_SPEC.md):
- Mục 2 (V-2), Mục 3 (dòng 5.4), **Mục 4.4** (thiết kế chính của gói).
- Mục 6, 6.1 (các dòng gói 5.4), 8 (rủi ro shadow/thread pool), 9 (tài liệu cập nhật ở 5.4).

Nguồn sở hữu:
- [`SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](../docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md) Mục 2, 4.2 (dòng `StrategyRouter`, `ShadowExecutor`, `IsolatedStrategyInvoker`, pipeline), 4.4, 4.5 (tên metric).
- [`RESILIENCE_AND_FAULT_TAXONOMY.md`](../docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md) (nguyên tắc 2: lỗi cache không kích hoạt fallback của strategy).
- [`DECISION_PERSISTENCE_INVARIANTS.md`](../docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md) B.1 (`role`, `uq_decision_idempotency_key` chỉ cho `PRIMARY`), INV-8.
- [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) (UT-04, UT-07, UT-09, UT-13, IT-12…14, FI-01…06, Mục 8).

Gói trước: [`PHASE5_PLAN_5.3_GOVERNANCE_SECURITY.md`](PHASE5_PLAN_5.3_GOVERNANCE_SECURITY.md) (mục "Ghi chú cho gói 5.4") và [`PHASE5_REPORT_5.3.md`](PHASE5_REPORT_5.3.md) Mục 6. Việc ADR-013 để lại (tiền tố khóa cache, `InstanceName`/`TtlHours`, tên health check).

> **Cách đọc plan này:**
>
> - Plan mô tả **yêu cầu kỹ thuật**: trách nhiệm, vị trí, chữ ký, quy tắc, bảng test case và tiêu chí nghiệm thu. Plan **không** chứa mã hiện thực.
> - Mục **Interfaces** là ràng buộc, vì task sau dựa vào đúng tên và kiểu đó.
> - Đường dẫn viết từ gốc repo. Tên kiểu và namespace viết theo trạng thái **sau** task đó.
> - Gói này **không đổi wire** (OpenAPI, JSON schema, SDK giữ nguyên) nhưng **đổi cấu hình và hành vi nội bộ**. Mọi thay đổi như vậy nằm ở bảng "Quyết định thiết kế" và Phụ lục B. Người dùng duyệt hai bảng đó trước Task 1.

## Global Constraints

**Build và test sau mỗi task:**
- `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror` → **0 warning, 0 error**.
- Unit test và **toàn bộ** integration test xanh (Docker bật).

**Số lượng test:** test hiện có không được mất (đầu gói: **1518 unit / 197 IT**, commit `aa5fa34`). Mỗi task ghi vào báo cáo số test trước và sau của cả hai project (đếm bằng `dotnet test --list-tests`).
- Ngoại lệ: test bị **thay thế** có chủ đích (ví dụ test của `StrategyOptions`/`ResolveActive` thay bằng test định tuyến ở Task 4; test khóa cache chứa `tadss:` ở Task 5). Ghi tên test cũ và test thay thế vào báo cáo.

**Contract:**
- `docs/contracts/openapi-v2.yaml`, `docs/contracts/schemas/v2/*.json`, `docs/contracts/baseline_public_api.txt` **không đổi**. Drift test OpenAPI hoặc `PublicApiSurfaceTests` đỏ ⇒ dừng và hỏi.
- Không đổi SDK (`Shared.DecisionContractSDK`), không đổi mã lỗi, status HTTP hay nội dung response của luồng hiện có.

**Database:**
- **Không** tạo migration, **không** `dotnet ef database update`, **không** kết nối Neon.
- Bản ghi `SHADOW` dùng đúng schema B.1 hiện có (`role = 'SHADOW'`, `idempotency_key` NULL). Cần cột mới ⇒ dừng và hỏi (D-05).

**Cấm nới kiểm tra:** không `Skip`, không nới assertion, không `#pragma warning disable`, không `!` null-forgiving, không xóa test để chữa test đỏ.

**Quy tắc kiến trúc (UT-14, phải xanh sau mọi task):**
- Phụ thuộc hướng vào trong. Application không tham chiếu Polly, Redis, EF, ASP.NET Core, hosting hạ tầng. Chỉ `Program.cs` và `Composition/` dùng kiểu Infrastructure.
- Mỗi kiểu public một file, tên file = tên kiểu, namespace = project + thư mục.
- Options và validator ở `Options/` của project sở hữu. Exception chỉ ở `TADSS.Application.Exceptions`. Port chỉ ở `TADSS.Application/Abstractions/**`.
- Mỗi file `src/**/*.cs` ≤ **250 dòng** (trừ `Migrations/`). `StrategyPipeline.cs` (225) và `StrategyDecisionCache.cs` (156) **phải tách** theo Task 2 và Task 5.
- Mọi options có `SectionName` phải có validator đăng ký cùng `ValidateOnStart`, kể cả options chỉ dùng ở môi trường `Testing`.

**Quy ước:**
- Options là `{X}Options` + `{X}OptionsValidator : IValidateOptions<{X}Options>`.
- Thời gian lấy từ `TimeProvider`; Polly gán `TimeProvider` tiêm vào. Mốc ghi DB cắt về µs bằng `AuditTimestamps.TruncateToMicroseconds`.
- Tên metric khai báo **một nơi** (`TadssMetrics`), không viết literal tên metric ở chỗ khác (gói 5.5 kiểm OB-1 dựa vào đây).
- Comment và message tiếng Việt như mã hiện có; định danh tiếng Anh. Viết đủ `Microsoft.Extensions.Options.Options.Create(...)` (bẫy namespace `Options` của plan 5.2d).

**Test:**
- Tên `{Method}_{Scenario}_{Expected}`, có `[Trait("CatalogId", …)]` và XML summary tiếng Việt. IT thêm `[Trait("Category","Integration")]`.
- Mã danh mục của gói: UT-04, UT-07, UT-09, UT-13, IT-12, IT-13, IT-14, FI-01…FI-06, **SCALE-1** (đổi tên từ SC-1, quyết định A-12, thêm vào danh mục ở Task 1).
- Test cần thời gian thật (timeout, breaker, worker nền) dùng đồng hồ thật như `FaultInjectionTestBase` và **chờ bằng polling có hạn** (không `Task.Delay` cố định rồi khẳng định). Hạn chờ tối đa 10 giây mỗi điều kiện.
- Đo metric trong test bằng `MeterListener` lọc theo **đúng instance** `Meter` của `TadssMetrics` trong test (helper `MetricRecorder`, Task 2), để test chạy song song không lẫn số đếm.

**Bảo mật:**
- Không in, không ghi vào file commit bất kỳ giá trị secret nào. Không đọc `appsettings.Development.json`, `.env` trừ khi cần đối chiếu **tên khóa** (chỉ in tên khóa hoặc boolean).
- Không sửa `appsettings.Development.json` của người dùng; nếu file đó còn khóa `Tadss:Strategy:ActiveStrategyName` thì chỉ ghi vào báo cáo để người dùng tự xóa (A-03).

**Git:**
- Stage bằng đường dẫn tường minh. **Không** stage `spec/PHASE5_*.md`.
- Không push, stash, reset, `checkout --`, clean.
- Subject commit theo Conventional Commits, tiếng Anh ASCII.

**Sai lệch so với giả định:** nếu .NET, Polly hoặc StackExchange.Redis chạy khác giả định (chữ ký overload, hành vi `OptionsMonitor`, `ResiliencePipelineRegistry`), sửa tối thiểu để giữ đúng **ý định**, ghi "sai lệch" vào báo cáo task, không nới test.

## Quyết định thiết kế của gói

| Mã | Quyết định | Lý do |
| :--- | :--- | :--- |
| **A-01** | **Quy tắc định tuyến** (`Tadss:Routing`):<br>• `DefaultStrategy` (bắt buộc) và `Rules[]`, mỗi rule có `Name`, `Strategy`, `When`.<br>• `When` gồm `PeakHour` (bool?), `BranchIds` (Guid[]), `Channels` (`ONLINE`/`WALK_IN`), `Tiers` (`CustomerTier`), `IsGuest` (bool?). Danh sách rỗng hoặc `null` = không ràng buộc.<br>• Mọi điều kiện trong một rule là AND; **rule đầu tiên khớp** được chọn; không rule nào khớp → `DefaultStrategy`.<br>• `PeakHour` dùng `BranchCalendar.IsPeak(reservation)` (lịch cao điểm dùng chung, theo giờ chi nhánh).<br>• Validator: `DefaultStrategy` không rỗng; mỗi rule có `Name` không rỗng, ≤ 64 ký tự, **duy nhất** (không phân biệt hoa thường); `Strategy` không rỗng; `When` có **ít nhất một** điều kiện. Tên strategy phải có trong registry (kiểm ở `StrategyRouter`, vì validator không thấy registry).<br>• `appsettings.json` để `Rules: []`, `DefaultStrategy: "DefaultDecisionStrategy"` → **hành vi mặc định không đổi**. README có ví dụ rule `peak-hour` → `MockAlternativeDecisionStrategy` | Spec 4.4. Rule không điều kiện sẽ khớp mọi request và che các rule sau, gần như chắc chắn là cấu hình nhầm. Giữ hành vi mặc định để triển khai gói này không làm đổi mức cọc đang chạy |
| **A-02** | **Nạp lại nóng không đi qua `IOptionsMonitor`** (*06/10/2026: cơ chế đọc + theo dõi cấu hình nằm ở adapter Infrastructure `ConfigurationRoutingRulesSource` qua port `IRoutingRulesSource`; Application không giữ `IConfiguration`*)**:**<br>• `StrategyRoutingOptions` vẫn đăng ký `Bind` + validator + `ValidateOnStart` (UT-14). Cấu hình sai lúc khởi động → không khởi động.<br>• `StrategyRouter` tự đăng ký `ChangeToken.OnChange` trên `IConfiguration` lấy từ DI (cấu hình cuối cùng, gồm override của host test). Mỗi lần đổi: bind bản mới từ section → chạy cùng validator → kiểm tên strategy trong registry → biên dịch thành bảng quy tắc bất biến → thay **nguyên khối** (một tham chiếu, `Volatile`).<br>• Bất kỳ bước nào lỗi (validator, binder ném lỗi giá trị enum/Guid sai, tên strategy lạ) → **giữ bảng cũ**, log Error kèm danh sách lỗi (không kèm giá trị cấu hình khác), tăng `tadss.config.reload_failures{section="Tadss:Routing"}`; callback **không bao giờ ném**.<br>• Bảng mới giống bảng cũ → log Debug, không log Information | Spec 4.4 yêu cầu "reload sai → giữ cấu hình cũ, log, tăng metric". Với `IOptionsMonitor` có validator, lần reload sai xóa giá trị đã cache rồi ném `OptionsValidationException` **trước** khi gọi listener, và từ đó mọi lần đọc `CurrentValue` đều ném → không giữ được bảng cũ, không ghi được metric. Đọc `IConfiguration` từ DI để host test thay cấu hình được, như F-13 của 5.3 |
| **A-03** | **Gỡ `Tadss:Strategy:ActiveStrategyName`:** xóa `StrategyOptions`, `StrategyOptionsValidator` và `IStrategyRegistry.ResolveActive()`; strategy mặc định là `Routing.DefaultStrategy`. Gỡ section `Tadss:Strategy` khỏi `appsettings.json`. Nếu cấu hình cuối cùng còn khóa `Tadss:Strategy:ActiveStrategyName` → `StartupTasks` log Warning một lần ("khóa đã thay bằng `Tadss:Routing:DefaultStrategy`, bị bỏ qua"), **không** dừng khởi động | Spec 4.4: "`ActiveStrategyName` bị thay bởi `Routing.DefaultStrategy`". Không dừng khởi động vì file dev của người dùng có thể còn khóa cũ; Warning đủ để phát hiện |
| **A-04** | Kết quả định tuyến `StrategyRoute(RegisteredStrategy Strategy, string? RuleName)`. `RuleName` chỉ vào log tóm tắt quyết định (`DecisionLogging`), **không** lưu DB (không có cột, không migration). Response giữ trường `strategy` như cũ (đã mang tên strategy được chọn) | Không đổi schema; tên rule tra lại được từ log theo `decisionId` |
| **A-05** | **Pipeline theo strategy (sửa V-2):** `StrategyPipeline` giữ một `ResiliencePipelineRegistry<string>` của Polly, khóa = `StrategyName` (đã duy nhất trong registry), tạo **lười** lần đầu dùng, an toàn đa luồng. Tham số `Tadss:Resilience` dùng chung cho mọi strategy (spec: "cấu hình giữ nguyên"). Mọi log của pipeline (retry, timeout, breaker mở/đóng/nửa mở, fallback) mang `{StrategyName}`. Chữ ký `IStrategyPipeline.ExecuteAsync` giữ nguyên. Registry được dispose cùng singleton | Dùng registry có sẵn của Polly thay vì tự viết dictionary + khóa. Breaker của strategy A mở không còn làm strategy B fast-fail; shadow lỗi không làm mở breaker của strategy chính |
| **A-06** | **`IsolatedStrategyInvoker`** (Infrastructure/Resilience) là bước **trong cùng** của pipeline cho **mọi** strategy:<br>• Gọi strategy bằng `Task.Run(..., CancellationToken.None)` rồi chờ bằng `task.WaitAsync(token)` (token Polly truyền vào, đã gộp inner timeout, outer timeout và hủy của caller).<br>• Token hủy khi task chưa xong → task bị **bỏ rơi**: tăng `tadss.strategy.abandoned{strategy}`, gắn continuation quan sát exception (log Debug) để không sinh `UnobservedTaskException`, rồi để `OperationCanceledException` lan ra như cũ (Polly dịch thành timeout).<br>• Strategy trả `null` vẫn là lỗi hợp đồng như hiện nay.<br>• Giới hạn: CPU của task bị bỏ rơi vẫn bị tiêu thụ tới khi nó tự kết thúc; nhiều task bị bỏ rơi cùng lúc có thể làm cạn thread pool. Ghi vào EXPERIMENTAL_DESIGN Mục 8 và SAS. Cách phát biểu: TADSS **giới hạn thời gian chờ** và cô lập luồng request khỏi strategy không hợp tác; TADSS **không dừng được** strategy đó | Spec 4.4 (FI-05). Áp cho mọi strategy để hành vi đồng nhất, không cần strategy "khai báo" mình không hợp tác. Chi phí một lần chuyển sang thread pool mỗi request là chấp nhận được so với ngân sách 500 ms |
| **A-07** | **Decorator tiêm lỗi** `FaultInjectionStrategyDecorator` (`src/TADSS.Infrastructure/FaultInjection/`), cấu hình `Tadss:Testing:FaultInjection`:<br>• `Enabled` (false), `Profile` (`None`/`Delay`/`Throw`/`IgnoreToken`/`BusyCpu`), `DelayMs` (1000, hợp lệ 1..60000), `TargetStrategy` (rỗng = mọi strategy).<br>• `Delay`: chờ `DelayMs` **có** tôn trọng token. `Throw`: ném `InvalidOperationException` với message cố định "Lỗi tiêm có chủ đích (FaultInjection).". `IgnoreToken`: chờ `DelayMs` **không** truyền token. `BusyCpu`: quay vòng đồng bộ `DelayMs` theo `TimeProvider`, không kiểm token. Xong phần lỗi (trừ `Throw`) thì gọi strategy thật.<br>• Giữ nguyên `StrategyName`/`StrategyVersion` của strategy thật (audit, khóa cache không đổi); `CachePolicy` trả `Disabled` khi `Profile ≠ None` để lỗi luôn tới được pipeline.<br>• Options + validator **luôn** đăng ký (UT-14). Chỉ bọc strategy khi môi trường là `Testing` **và** `Enabled = true`. `Enabled = true` ở môi trường khác → log Warning, không bọc | Spec 4.4 ("chỉ đăng ký khi `Testing` và `Enabled`"; nằm trong app vì FI-02 phải bật được trong compose cho LT-03). Tắt cache để kịch bản LT-03 không bị cache che mất lỗi |
| **A-08** | **Redis qua pipeline riêng:**<br>• Tách `RedisDecisionCache` (internal, Infrastructure/Caching) khỏi `StrategyDecisionCache`. Mọi lệnh Redis chạy trong pipeline Polly riêng: Timeout `RedisTimeoutMs` (chờ bằng `task.WaitAsync(token)` vì StackExchange.Redis không nhận token) → Circuit breaker.<br>• Tham số breaker mới trong `CacheOptions`: `RedisBreakerFailureRatio` (0.5, khoảng (0,1]), `RedisBreakerSamplingSeconds` (30, ≥ 1), `RedisBreakerMinimumThroughput` (10, ≥ 2), `RedisBreakerBreakDurationSeconds` (30, ≥ 1).<br>• Tính là lỗi: mọi exception của Redis và timeout. **Không** tính là lỗi: payload hỏng (vẫn là miss như hiện nay), hủy của caller.<br>• Breaker mở → bỏ qua Redis cho cả đọc và ghi, dùng `IMemoryCache` (nếu `EnableFallbackToMemory`), không chờ timeout.<br>• Lỗi cache **không bao giờ** kích hoạt fallback của strategy và không làm request thất bại | Spec 4.4, RESILIENCE_AND_FAULT_TAXONOMY nguyên tắc 2, SAS dòng 103 ("Redis lỗi thì mỗi lần gọi cache chờ tới `RedisTimeoutMs`"). Tách file để giữ trần 250 dòng và test được breaker bằng test double |
| **A-09** | **Tiền tố khóa cache vào cấu hình** (việc ADR-013 để lại): `StrategyDecisionCacheKey.Build` trả `v2:{strategyName}:{strategyVersion}:{optionsFingerprint}:{requestHash}` (bỏ `tadss:`); `StrategyDecisionCache` cộng `CacheOptions.InstanceName` (mặc định `tadss:`) cho cả Redis và memory → khóa đầy đủ mặc định **giữ nguyên** `tadss:v2:...`. Validator: `InstanceName` không rỗng, ≤ 32 ký tự, không chứa khoảng trắng. **Xóa** `CacheOptions.TtlHours` (không mã nào đọc) khỏi options, validator, `appsettings.json`, README | Hoàn tất mục (i) và (iii) của ADR-013. Giữ nguyên khóa mặc định để dữ liệu cache đang có vẫn dùng được |
| **A-10** | **Meter `TADSS.Runtime`** (`TADSS.Application/Common/TadssMetrics.cs`, singleton, `IDisposable`): một `Meter` tạo bằng `new Meter(MeterName)`; tên metric là hằng trong lớp. Instrument tạo ở gói này (đều `Counter<long>`):<br>• `tadss.config.reload_failures` {`section`}<br>• `tadss.strategy.abandoned` {`strategy`}<br>• `tadss.cache.requests` {`result` = `hit`/`miss`/`bypass`}: mỗi lần `GetAsync` ghi **đúng một** giá trị; `bypass` khi breaker Redis đang mở (bất kể memory có hay không)<br>• `tadss.shadow.dropped` {`reason` = `queue_full`}<br>• `tadss.shadow.executions` {`outcome` = `recorded`/`failed`}<br>Chưa có exporter; gói 5.5 thêm OpenTelemetry (`AddMeter("TADSS.Runtime")`) và các metric còn lại của SAS 4.5 | Spec 4.4 nêu các metric này. Meter thuộc BCL nên Application dùng được mà không phá UT-14. Một nơi khai báo tên để OB-1 (5.5) kiểm được dashboard |
| **A-11** | **Shadow** (`Tadss:Shadow`):<br>• Options: `Enabled` (false), `Strategy` (rỗng), `SampleRate` (1.0, khoảng [0,1]), `QueueCapacity` (256, 1..10000). Bật thì `Strategy` bắt buộc và phải có trong registry (kiểm khi khởi động). **Không** nạp lại nóng (đổi phải khởi động lại).<br>• `DecisionService` chỉ gửi việc shadow sau khi **chính request này** vừa ghi xong bản `PRIMARY` mới (không gửi khi replay idempotency, khi thua tranh chấp, khi lỗi). Strategy shadow trùng tên strategy chính của request → bỏ qua (log Debug). Lấy mẫu: `SampleRate` = 1 luôn gửi, = 0 không gửi, còn lại dùng `Random.Shared`.<br>• Hàng đợi `Channel` có giới hạn (`FullMode = Wait`, một reader); `TryWrite` thất bại → bỏ việc, tăng `tadss.shadow.dropped{reason="queue_full"}`. Gửi việc **không bao giờ ném** ra `DecisionService` và không chờ.<br>• `ShadowExecutionWorker` (`BackgroundService`) đọc tuần tự; mỗi việc một DI scope; chạy strategy shadow qua **pipeline của chính nó** (A-05), **không** cache, dùng **cùng `nowUtc`** của bản `PRIMARY`; ghép bản ghi bằng chính `DecisionResponseComposer` với `role = SHADOW`, `idempotency_key = NULL`, cùng `reservation_id`, `request_hash`, `correlation_id`, `created_at = nowUtc` của bản `PRIMARY`; ghi qua `IDecisionEvaluationStore`.<br>• Lỗi của shadow: strategy lỗi/treo → bản `SHADOW` là `FALLBACK` như mọi strategy; outer timeout, lỗi lưu trữ hay lỗi bất ngờ → không có bản ghi, log Warning, `tadss.shadow.executions{outcome="failed"}`. Worker không bao giờ dừng vì một việc lỗi. Khi tắt ứng dụng, việc còn trong hàng đợi bị bỏ (log Information số việc bỏ) | Spec 4.4 và INV-8. Ghép cặp `PRIMARY`–`SHADOW` qua (`reservation_id`, `request_hash`, `created_at`) và `correlation_id` mà không cần cột mới. Cùng `nowUtc` để hai bản so sánh được trên cùng ngữ cảnh. Pipeline riêng để shadow lỗi không làm mở breaker của strategy chính |
| **A-12** | **Đổi mã SC-1…SC-3 thành SCALE-1…SCALE-3** trong spec, danh mục kiểm thử và tài liệu kế hoạch, để không nhầm với KPI SC1…SC8. Task 1 thêm dòng **SCALE-1** vào danh mục (mục mới "SCALE — Nhiều instance"); SCALE-2, SCALE-3 thêm ở gói 5.5 | Hai hệ mã gần giống nhau (`SC-1` là test nhiều instance, `SC1` là KPI độ trễ) dễ đọc nhầm trong báo cáo và Evidence Pack |
| **A-13** | Log ERR của EF cho mỗi `23505` mong đợi (việc 5.3 để lại): `AddPersistenceDbContext` hạ `RelationalEventId.CommandError` và `CoreEventId.SaveChangesFailed` xuống **Debug**. Lỗi thật vẫn được ghi: 503 bởi `PersistenceFailureTranslator` (Warning), lỗi khác bởi `GlobalExceptionHandler` (Error) | Tranh chấp idempotency là luồng bình thường (store đọc lại bản thắng) nhưng hiện sinh hai dòng Error gây nhiễu, sẽ làm sai dashboard lỗi ở 5.5 |
| **A-14** | Dọn chuỗi **E-LOCAL** (môi trường đã gỡ theo D-12): comment và message ở `StartupTasks`, XML doc của `DatabaseOptions.ApplyMigrationsOnStartup`, assertion ở `MigrationIntegrationTests` đổi sang "database thử dùng một lần (Testcontainers, verify-expose.sh, chế độ đánh giá)". `EndpointMappingExtensions` dùng `HealthCheckTags.Ready` thay hằng riêng `ReadyTag = "ready"`. Tên health check đã là hằng (`PersistenceHealthChecks.Name`, `RedisHealthCheck.Name`) nên ADR-013 (ii) chỉ cần ghi "đã là hằng một nơi", không đổi mã | Việc 5.3 và ADR-013 (ii) để lại; tài liệu không mô tả thứ đã gỡ (DOC-2 ở 5.7) |

Ngoài phạm vi 5.4 (ghi vào báo cáo, không làm):
- `UseSerilogRequestLogging` đứng trong exception handler nên ghi "500" cho 4xx/503 do exception → gói 5.5 (observability).
- Gauge trạng thái breaker theo strategy, OpenTelemetry exporter, dashboard → 5.5.
- Rate limit phân tán, SCALE-2 (hai replica sau reverse proxy), SCALE-3 → 5.5.

Nếu người dùng không đồng ý mục nào, sửa plan trước khi chạy Task 1.

## Chuẩn bị (người dùng thực hiện)

1. Bật Docker Desktop (IT và SCALE-1 cần Testcontainers).
2. Duyệt bảng Quyết định và Phụ lục B. Đặc biệt A-03 (gỡ `ActiveStrategyName`) và A-12 (đổi mã SCALE).
3. Sau Task 4: nếu `appsettings.Development.json` còn `Tadss:Strategy:ActiveStrategyName` thì tự xóa khóa đó; muốn dùng strategy khác mặc định thì đặt `Tadss:Routing:DefaultStrategy`.
4. Cuối gói: đề xuất PR vào `develop` (người dùng tạo và merge).

## Cấu trúc đích

Chỉ liệt kê file mới hoặc file đổi đáng kể.

```text
src/
  TADSS.Application/
    Common/                    TadssMetrics (Task 2)
    Options/                   StrategyRoutingOptions, RoutingRuleOptions, RoutingConditionOptions, StrategyRoutingOptionsValidator (Task 4);
                               ShadowOptions, ShadowOptionsValidator (Task 6); − StrategyOptions, StrategyOptionsValidator (Task 4)
    Routing/                   IStrategyRouter, StrategyRouter, StrategyRoute, RoutingTable (Task 4)
    Shadow/                    ShadowWorkItem, IShadowDispatcher, ShadowDispatcher, ShadowExecutor (Task 6)
    Strategies/                IStrategyRegistry − ResolveActive, + TryResolve; StrategyRegistry không còn đọc StrategyOptions (Task 4)
    Decisions/                 DecisionService dùng router + shadow; DecisionComposition + Role, IdempotencyKey nullable;
                               DecisionResponseComposer ghép theo Role; DecisionLogging + RuleName (Task 4, 6)
    Abstractions/Caching/      StrategyDecisionCacheKey bỏ tiền tố tadss: (Task 5)
  TADSS.Infrastructure/
    Caching/                   RedisDecisionCache, RedisCacheResilience (Task 5); StrategyDecisionCache gọn lại (Task 5)
    Options/                   CacheOptions + RedisBreaker*, − TtlHours (Task 5); FaultInjectionOptions, FaultInjectionProfile,
                               FaultInjectionOptionsValidator (Task 3)
    Persistence/               PersistenceServiceCollectionExtensions: hạ mức log EF (Task 1)
    Resilience/                StrategyPipeline (thực thi + ánh xạ fallback), StrategyResilienceBuilder (dựng pipeline theo strategy),
                               IsolatedStrategyInvoker (Task 2)
    Shadow/                    ShadowExecutionWorker (Task 6)
    Testing/                   FaultInjectionStrategyDecorator (Task 3)
  TADSS.Runtime.Api/
    Composition/               StrategyServiceCollectionExtensions: bọc decorator theo môi trường (Task 3); StartupTasks: kiểm sớm router,
                               shadow, Warning khóa cũ, dọn E-LOCAL (Task 1, 4, 6)
    appsettings.json           + Tadss:Routing, Tadss:Shadow, Tadss:Testing:FaultInjection, Tadss:Cache:RedisBreaker*; − Tadss:Strategy, − TtlHours
tests/TADSS.UnitTests/         Application/Routing/, Application/Shadow/, Infrastructure/Resilience/ (invoker, pipeline theo strategy),
                               Infrastructure/Caching/ (Redis breaker), Infrastructure/FaultInjection/, Infrastructure/Configuration/, Architecture/ (decorator không đăng ký ở Production),
                               TestDoubles/ (MetricRecorder, FakeStrategyRouter, FakeShadowDispatcher)
tests/TADSS.IntegrationTests/  Routing/RoutingEndpointTests, Shadow/ShadowExecutionTests, FaultInjection/ (FI-05, decorator, Redis breaker),
                               Scale/MultiInstanceTests
docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md   SCALE-1, IT-12, Mục 8 (Task 1, 8)
```

Thứ tự phụ thuộc giữa các task: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8.

---

### Task 1: Việc mang sang, dọn E-LOCAL và mã danh mục

**Files:**
- Modify:
  - `src/TADSS.Runtime.Api/Composition/StartupTasks.cs`, `src/TADSS.Infrastructure/Options/DatabaseOptions.cs`: dọn E-LOCAL (A-14).
  - `src/TADSS.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs`: hạ mức log EF (A-13).
  - `src/TADSS.Runtime.Api/Composition/EndpointMappingExtensions.cs`: bỏ `ReadyTag`, dùng `HealthCheckTags.Ready` (A-14).
  - `tests/TADSS.IntegrationTests/Migrations/MigrationIntegrationTests.cs`: assertion theo message mới.
  - `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`, `spec/PHASE5_DESIGN_SPEC.md` (chỉ đổi mã, A-12), `docs/planning/*.md` nếu có `SC-1…3`.
- Test: `tests/TADSS.UnitTests/Infrastructure/Persistence/PersistenceLoggingTests.cs`.

**Interfaces:**
- Không thêm kiểu public mới.

**Yêu cầu:**
1. **A-14 (E-LOCAL):** message Warning của `StartupTasks` khi `ApplyMigrationsOnStartup = true` đổi thành câu nêu "chỉ dùng cho database thử dùng một lần (Testcontainers, verify-expose.sh, chế độ đánh giá), không bật cho Neon". Không còn chuỗi `E-LOCAL` trong `src/` và `tests/` (kiểm bằng grep, ghi vào báo cáo).
2. **A-13:** trong `AddPersistenceDbContext`, cấu hình `ConfigureWarnings` để `RelationalEventId.CommandError` và `CoreEventId.SaveChangesFailed` log ở `LogLevel.Debug`. Không đổi gì khác của DbContext.
3. **A-14 (hằng):** `EndpointMappingExtensions` dùng `HealthCheckTags.Ready`; không còn literal `"ready"` ngoài `HealthCheckTags`.
4. **A-12 (danh mục):**
   - Đổi `SC-1`/`SC-2`/`SC-3` → `SCALE-1`/`SCALE-2`/`SCALE-3` trong `spec/PHASE5_DESIGN_SPEC.md`, EXPERIMENTAL_DESIGN và tài liệu kế hoạch (grep `SC-[123]`). KPI `SC1…SC8` giữ nguyên.
   - Thêm mục con "SCALE — Nhiều instance" vào EXPERIMENTAL_DESIGN Mục 4 với dòng **SCALE-1**: "Hai host dùng chung PostgreSQL/Redis (Testcontainers): 50 request song song cùng idempotency key chia đều hai host → đúng 1 bản ghi `PRIMARY`, mọi response giống nhau; khóa cache ghi ở host A được host B đọc (`CACHE_HIT`); host A mở breaker của strategy thì host B vẫn chạy strategy bình thường" — Xây dựng P5.
   - Sửa tiêu chí IT-12: thêm "strategy shadow ném lỗi hoặc treo: response `PRIMARY` giống hệt khi tắt shadow, bản `PRIMARY` vẫn lưu, lỗi chỉ thể hiện ở bản `SHADOW` (`FALLBACK`) hoặc metric" (spec Mục 9).
   - **Không stage** file tài liệu nếu người dùng đang có thay đổi chưa commit trong file đó; ghi vào báo cáo.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `AddPersistenceDbContext_CommandErrorAndSaveChangesFailed_LoggedAtDebug` | Dựng `DbContextOptions` từ DI đã đăng ký, đọc `CoreOptionsExtension.WarningsConfiguration` | Mức log của hai event là Debug | UT-08 |
| `Migrate_OnStartupFlagTrue_LogsSingleUseDatabaseWarning` (sửa) | Như test hiện có | Message chứa "database thử dùng một lần", không chứa "E-LOCAL" | IT-15 |
| Test health hiện có | `/health/ready` lọc theo `HealthCheckTags.Ready` | Vẫn xanh | SEC-5 |
| Toàn bộ unit + IT | — | Xanh | — |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh, số test ghi vào báo cáo; grep `E-LOCAL` trong `src/`, `tests/` rỗng; grep `SC-[123]` trong tài liệu hiện hành rỗng.
- Commit đề xuất: `chore: quiet expected idempotency db errors and drop stale e-local wording` và `docs: rename scale test ids and extend shadow criteria`.

---

### Task 2: Meter `TADSS.Runtime`, pipeline theo strategy và cách ly strategy không hợp tác

**Files:**
- Create:
  - `src/TADSS.Application/Common/TadssMetrics.cs`
  - `src/TADSS.Infrastructure/Resilience/StrategyResilienceBuilder.cs` (internal static: dựng các lớp Polly cho một strategy)
  - `src/TADSS.Infrastructure/Resilience/IsolatedStrategyInvoker.cs` (internal sealed)
  - `tests/TADSS.UnitTests/TestDoubles/MetricRecorder.cs`
- Modify:
  - `src/TADSS.Infrastructure/Resilience/StrategyPipeline.cs`: dùng `ResiliencePipelineRegistry<string>`; còn lại phần thực thi, dịch exception, ánh xạ fallback.
  - `src/TADSS.Application/ApplicationServiceCollectionExtensions.cs`: đăng ký `TadssMetrics` singleton.
  - `src/TADSS.Infrastructure/InfrastructureServiceCollectionExtensions.cs`: comment nhóm 4 bỏ câu "circuit breaker dùng chung cho mọi request".
- Test: `tests/TADSS.UnitTests/Application/Common/TadssMetricsTests.cs`, `tests/TADSS.UnitTests/Infrastructure/Resilience/IsolatedStrategyInvokerTests.cs`, bổ sung `StrategyPipelineTests`, thêm IT trong `tests/TADSS.IntegrationTests/FaultInjection/FaultInjectionTests.cs`.

**Interfaces:**
- Produces:
  - `public sealed class TadssMetrics : IDisposable`:
    - Hằng: `MeterName = "TADSS.Runtime"`; `ConfigReloadFailures = "tadss.config.reload_failures"`, `StrategyAbandoned = "tadss.strategy.abandoned"`, `CacheRequests = "tadss.cache.requests"`, `ShadowDropped = "tadss.shadow.dropped"`, `ShadowExecutions = "tadss.shadow.executions"`; giá trị nhãn `CacheResultHit = "hit"`, `CacheResultMiss = "miss"`, `CacheResultBypass = "bypass"`, `ShadowOutcomeRecorded = "recorded"`, `ShadowOutcomeFailed = "failed"`.
    - `Meter Meter { get; }`.
    - `void RecordConfigReloadFailure(string section)`, `void RecordStrategyAbandoned(string strategyName)`, `void RecordCacheRequest(string result)`, `void RecordShadowDropped()`, `void RecordShadowExecution(string outcome)`.
  - `internal sealed class IsolatedStrategyInvoker(TadssMetrics metrics, ILogger logger)`: `Task<StrategyDecision?> InvokeAsync(IReservationDecisionStrategy strategy, Reservation reservation, DecisionContext context, CancellationToken cancellationToken)`.
  - `StrategyPipeline` constructor: `(IOptions<ResilienceOptions> options, TimeProvider timeProvider, TadssMetrics metrics, ILogger<StrategyPipeline> logger)`; implement `IDisposable` (dispose registry).
  - Test double `MetricRecorder : IDisposable` (UnitTests/TestDoubles, dùng chung cho IT qua tham chiếu project như các test double khác): `MetricRecorder(Meter meter)`; `long Sum(string instrumentName, params (string Key, string Value)[] tags)`.

**Yêu cầu:**
1. **`TadssMetrics` (A-10):** tạo `Meter` và năm `Counter<long>` trong constructor; `Dispose` dispose `Meter`. Nhãn truyền dạng `KeyValuePair<string, object?>`. Không phụ thuộc gì ngoài BCL.
2. **Pipeline theo strategy (A-05):**
   - `ExecuteAsync` lấy pipeline bằng `registry.GetOrAddPipeline<StrategyExecutionOutcome>(strategy.StrategyName, (builder, _) => StrategyResilienceBuilder.Configure(...))`. `builder.TimeProvider` = `TimeProvider` tiêm vào.
   - Thứ tự lớp giữ nguyên: Outer timeout → Fallback → Retry → Circuit breaker → Inner timeout. Lambda trong cùng gọi `IsolatedStrategyInvoker.InvokeAsync`.
   - Mọi template log của pipeline có `{StrategyName}`; giữ mức log hiện có.
   - Hai file sau tách đều ≤ 250 dòng.
3. **`IsolatedStrategyInvoker` (A-06):**
   - `var task = Task.Run(() => strategy.EvaluateAsync(reservation, context, cancellationToken), CancellationToken.None);` rồi `await task.WaitAsync(cancellationToken)`.
   - Bắt `OperationCanceledException` khi `cancellationToken.IsCancellationRequested` **và** `!task.IsCompleted`: ghi metric abandoned, gắn `task.ContinueWith(..., TaskContinuationOptions.ExecuteSynchronously)` đọc `t.Exception` (log Debug nếu có), rồi `throw;`.
   - Strategy ném exception đồng bộ hoặc bất đồng bộ → exception lan ra nguyên vẹn (pipeline xử lý như cũ). Strategy trả `null` → trả `null` (pipeline ném lỗi hợp đồng như hiện nay).
4. Hành vi mọi kịch bản FI-01…FI-04 hiện có **không đổi** (cùng response, cùng reason code, cùng thời gian trong ngân sách).

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Counters_Record_ExpectedNamesAndTags_Theory` | Gọi từng `Record*` | `MetricRecorder` thấy đúng tên và nhãn; meter tên `TADSS.Runtime` | UT-07 |
| `InvokeAsync_CooperativeStrategy_ReturnsDecision` | Strategy trả quyết định | Trả đúng quyết định; abandoned = 0 | FI-05 |
| `InvokeAsync_SyncBlockingStrategyIgnoringToken_ReturnsWhenTokenCancelled` | Strategy `Thread.Sleep(2000)` đồng bộ; token hủy sau 100 ms | `OperationCanceledException` trong ≤ 1 s; abandoned = 1 | FI-05 |
| `InvokeAsync_AsyncStrategyIgnoringToken_ReturnsWhenTokenCancelled` | `await Task.Delay(2000)` không token | Như trên | FI-05 |
| `InvokeAsync_AbandonedTaskFaultsLater_NoUnobservedException` | Task bị bỏ rơi ném lỗi sau khi hủy; ép GC + `WaitForPendingFinalizers` | `TaskScheduler.UnobservedTaskException` không được gọi cho task đó | FI-05 |
| `InvokeAsync_StrategyThrows_PropagatesOriginalException` | Ném đồng bộ và bất đồng bộ | Cùng kiểu exception | UT-07 |
| `ExecuteAsync_BreakerOpenForStrategyA_StrategyBStillAutomated` | `MinimumThroughput` nhỏ; A luôn lỗi tới khi breaker mở; sau đó gọi B khỏe | A: `FALLBACK_CIRCUIT_OPEN`; B: `AUTOMATED` | UT-07 |
| `ExecuteAsync_SameStrategyName_ReusesPipeline` | Gọi hai lần cùng tên, đếm số lần builder được gọi (log khởi tạo hoặc callback test) | Dựng một lần | UT-07 |
| `ExecuteAsync_PipelineLogs_IncludeStrategyName` | Kịch bản retry + fallback | Mọi log Warning/Error của pipeline có thuộc tính `StrategyName` | UT-07 |
| `F5_UncooperativeStrategy_IsCutWithinBudget` (IT) | `ScriptedStrategy` chặn đồng bộ 3 s, bỏ qua token; cấu hình mặc định | 200 `APPROVED` cọc 100%, `FALLBACK_STRATEGY_TIMEOUT`; request kết thúc trong ≤ 500 ms + 300 ms dung sai | FI-05 |
| Test FI-01…FI-04, pipeline hiện có | — | Xanh, không sửa kỳ vọng | FI-01…04, UT-07 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh, số test ghi vào báo cáo.
- Commit đề xuất: `feat(resilience): per-strategy pipelines and isolation of uncooperative strategies`.

---

### Task 3: Decorator tiêm lỗi strategy theo cấu hình (chỉ `Testing`)

**Files:**
- Create:
  - `src/TADSS.Infrastructure/FaultInjection/FaultInjectionStrategyDecorator.cs`
  - `src/TADSS.Infrastructure/Options/FaultInjectionOptions.cs`, `FaultInjectionProfile.cs` (enum), `FaultInjectionOptionsValidator.cs`
- Modify:
  - `src/TADSS.Infrastructure/InfrastructureServiceCollectionExtensions.cs`: đăng ký options + validator + `ValidateOnStart`.
  - `src/TADSS.Runtime.Api/Composition/StrategyServiceCollectionExtensions.cs`: `AddStrategies(IServiceCollection, IConfiguration, IHostEnvironment)` bọc `RegisteredStrategy` khi đủ điều kiện (A-07); `Program.cs` truyền `builder.Environment`.
  - `src/TADSS.Runtime.Api/appsettings.json`: section `Tadss:Testing:FaultInjection` với `Enabled: false`.
- Test: `tests/TADSS.UnitTests/Infrastructure/FaultInjection/FaultInjectionStrategyDecoratorTests.cs`, `tests/TADSS.UnitTests/Infrastructure/Options/FaultInjectionOptionsValidatorTests.cs`, `tests/TADSS.UnitTests/Architecture/FaultInjectionRegistrationTests.cs`, IT `tests/TADSS.IntegrationTests/FaultInjection/ConfiguredFaultInjectionTests.cs`.

**Interfaces:**
- Produces:
  - `public sealed class FaultInjectionOptions { public const string SectionName = "Tadss:Testing:FaultInjection"; bool Enabled; FaultInjectionProfile Profile; int DelayMs = 1000; string? TargetStrategy; }`.
  - `public enum FaultInjectionProfile { None, Delay, Throw, IgnoreToken, BusyCpu }`.
  - `public sealed class FaultInjectionStrategyDecorator : IReservationDecisionStrategy` — constructor `(IReservationDecisionStrategy inner, FaultInjectionOptions options, TimeProvider timeProvider)`.

**Yêu cầu:**
1. Hành vi từng profile theo A-07. `StrategyName`, `StrategyVersion` lấy từ strategy thật; `CachePolicy` = `StrategyCachePolicy.Disabled` khi `Profile ≠ None`, ngược lại lấy từ strategy thật.
2. Validator: `DelayMs` 1..60000; `TargetStrategy` nếu có thì không chỉ gồm khoảng trắng. (Tên có trong registry hay không: không bắt buộc — tên lạ chỉ làm decorator không áp cho strategy nào, ghi Warning khi khởi động.)
3. Bọc ở composition root: chỉ khi `environment.IsEnvironment("Testing")` và `Enabled`; áp cho strategy có tên khớp `TargetStrategy` (không phân biệt hoa thường) hoặc mọi strategy khi rỗng. `OptionsFingerprint` giữ nguyên. Ngoài `Testing` mà `Enabled = true` → log Warning một lần khi khởi động (trong `StartupTasks`), không bọc.
4. Decorator không bao giờ có mặt ở `Production`, `Staging`, `Development` (UT-09).

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `EvaluateAsync_Profile_Behaves_Theory` | `None`, `Delay` (token hủy giữa chừng), `Throw`, `IgnoreToken`, `BusyCpu` với `FakeTimeProvider`/token | Đúng hành vi A-07; `Throw` có message cố định; `Delay` ném `OperationCanceledException` khi token hủy | FI-02, FI-03, FI-05 |
| `Identity_MatchesInnerStrategy` | Bọc `DefaultDecisionStrategy` | Cùng tên, phiên bản; cache tắt khi profile ≠ None | UT-09 |
| `Validate_Theory` | `DelayMs` 0, 60001, 1000; `TargetStrategy` "  " | Hai ca biên và ca khoảng trắng fail; 1000 đạt | UT-09 |
| `Host_ProductionWithEnabledTrue_DoesNotDecorate_Theory` | Host `Production`/`Staging`/`Development` (cấu hình hợp lệ tối thiểu như test hiện có) với `Enabled = true` | Không `RegisteredStrategy` nào là decorator | UT-09 |
| `Host_TestingWithEnabledTrueAndTarget_DecoratesOnlyTarget` | `Testing`, `TargetStrategy = MockAlternativeDecisionStrategy` | Chỉ strategy đó được bọc | UT-09 |
| `PostDecision_ConfiguredThrowProfile_FallsBackFullDeposit` (IT) | Host `Testing`, `Enabled = true`, `Profile = Throw`, strategy thật | 200 `APPROVED` cọc 100%, `FALLBACK_STRATEGY_EXCEPTION` (dùng `AssertFallbackFullDeposit`) | FI-03 |
| `PostDecision_ConfiguredDelayProfile_FallsBackWithinBudget` (IT) | `Profile = Delay`, `DelayMs = 2000` | `FALLBACK_STRATEGY_TIMEOUT`; ≤ 500 ms + dung sai | FI-02 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh, số test ghi vào báo cáo.
- Commit đề xuất: `feat(testing): configurable strategy fault injection for testing environment`.

---

### Task 4: Định tuyến strategy theo request và nạp lại nóng

**Files:**
- Create:
  - `src/TADSS.Application/Options/StrategyRoutingOptions.cs`, `RoutingRuleOptions.cs`, `RoutingConditionOptions.cs`, `StrategyRoutingOptionsValidator.cs`
  - `src/TADSS.Application/Routing/IStrategyRouter.cs`, `StrategyRouter.cs`, `StrategyRoute.cs`, `RoutingTable.cs` (internal sealed, bất biến)
  - `tests/TADSS.UnitTests/TestDoubles/FakeStrategyRouter.cs`
- Delete: `src/TADSS.Application/Options/StrategyOptions.cs`, `StrategyOptionsValidator.cs` và test riêng của chúng (thay thế có chủ đích).
- Modify:
  - `src/TADSS.Application/Strategies/IStrategyRegistry.cs`, `StrategyRegistry.cs`: bỏ `ResolveActive` và phụ thuộc `StrategyOptions`; thêm `TryResolve`.
  - `src/TADSS.Application/Decisions/DecisionService.cs`: dùng `IStrategyRouter`; `DecisionLogging` thêm `RuleName`.
  - `src/TADSS.Application/ApplicationServiceCollectionExtensions.cs`: đăng ký options + validator + router; gỡ `StrategyOptions`.
  - `src/TADSS.Runtime.Api/Composition/StartupTasks.cs`: resolve sớm `IStrategyRouter`; Warning khóa cũ (A-03).
  - `src/TADSS.Runtime.Api/appsettings.json`: thêm `Tadss:Routing`, gỡ `Tadss:Strategy`.
  - Test double/test hiện có dùng `ResolveActive`/`ActiveStrategyName`: `FakeStrategyRegistry`, `ScriptedStrategyRegistry`, `ProbeStrategyRegistry`, `DecisionApiTestBase`, `FaultInjectionTestBase`, `StrategyRegistryTests`, `StrategyRegistryStartupTests`, `AppSettingsValidationTests`, `DecisionServiceTests`.
- Test: `tests/TADSS.UnitTests/Application/Routing/StrategyRouterTests.cs`, `StrategyRouterReloadTests.cs`, `tests/TADSS.UnitTests/Application/Options/StrategyRoutingOptionsValidatorTests.cs`, IT `tests/TADSS.IntegrationTests/Routing/RoutingEndpointTests.cs`.

**Interfaces:**
- Consumes: `TadssMetrics` (Task 2), `BranchCalendar` (singleton có sẵn), `IStrategyRegistry`.
- Produces:
  - `public sealed class StrategyRoutingOptions { public const string SectionName = "Tadss:Routing"; string DefaultStrategy = "DefaultDecisionStrategy"; List<RoutingRuleOptions> Rules = []; }`.
  - `public sealed class RoutingRuleOptions { string Name = ""; string Strategy = ""; RoutingConditionOptions When = new(); }`.
  - `public sealed class RoutingConditionOptions { bool? PeakHour; List<Guid> BranchIds = []; List<Channel> Channels = []; List<CustomerTier> Tiers = []; bool? IsGuest; }`.
  - `public interface IStrategyRouter { StrategyRoute Route(Reservation reservation); }`.
  - `public sealed record StrategyRoute(RegisteredStrategy Strategy, string? RuleName);` (`RuleName` = `null` khi dùng strategy mặc định).
  - `public sealed class StrategyRouter : IStrategyRouter, IDisposable` — constructor `(IOptions<StrategyRoutingOptions> options, IConfiguration configuration, IEnumerable<IValidateOptions<StrategyRoutingOptions>> validators, IStrategyRegistry registry, BranchCalendar calendar, TadssMetrics metrics, ILogger<StrategyRouter> logger)`.
  - `IStrategyRegistry`: `bool TryResolve(string strategyName, [NotNullWhen(true)] out RegisteredStrategy? strategy);`, `RegisteredStrategy ResolveByName(string)`, `IReadOnlyList<RegisteredStrategy> All` (bỏ `ResolveActive`).
  - `DecisionService` constructor thay `IStrategyRegistry` bằng `IStrategyRouter` (các tham số khác giữ thứ tự).

**Yêu cầu:**
1. **Ngữ nghĩa A-01.** Router biên dịch options thành `RoutingTable` (mảng rule đã phân giải `RegisteredStrategy`, tập `BranchIds`/`Channels`/`Tiers` dạng `HashSet`). `Route` không cấp phát ngoài kết quả, không I/O, an toàn đa luồng.
2. **Khởi động:** constructor biên dịch từ `options.Value` (đã qua validator). Tên strategy lạ (mặc định hoặc trong rule) → `InvalidOperationException` liệt kê tên lạ và danh sách strategy hợp lệ → app không khởi động (`StartupTasks` resolve sớm).
3. **Nạp lại (A-02)** — *sửa kiến trúc 06/10/2026: việc đọc section và theo dõi `IConfiguration` dời sang adapter `ConfigurationRoutingRulesSource` (Infrastructure/Configuration) qua port `IRoutingRulesSource` (`Read(failures)`, `Subscribe(onChanged)`); router chỉ validate, biên dịch, thay bảng. Mô tả gốc:* đăng ký `ChangeToken.OnChange(configuration.GetReloadToken, ...)`; trong callback bind `new StrategyRoutingOptions()` từ `configuration.GetSection(SectionName)`, chạy mọi validator, biên dịch; mọi exception được bắt (không ném khỏi callback). Thành công → thay bảng; thất bại → giữ bảng cũ, log Error, `RecordConfigReloadFailure("Tadss:Routing")`. `Dispose` hủy đăng ký.
4. **A-03:** `StartupTasks` kiểm `app.Configuration["Tadss:Strategy:ActiveStrategyName"]` không rỗng → Warning (không in giá trị).
5. `DecisionService` gọi `Route(reservation)` sau bước kiểm thời lượng item (đúng vị trí `ResolveActive` cũ); `DecisionLogging.LogDecisionSummary` thêm `{RuleName}` (giá trị `default` khi `null`).
6. Test double IT: `ScriptedStrategyRegistry` và `ProbeStrategyRegistry` bỏ `ResolveActive`, thêm `TryResolve`. Host test của `FaultInjectionTestBase` đặt `Tadss:Routing:DefaultStrategy = FaultInjectionStrategy` qua cấu hình host (không thay `IStrategyRouter`), để router thật được dùng trong IT.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Route_RuleTable_Theory` | Bảng: mỗi điều kiện riêng lẻ khớp/không khớp; AND hai điều kiện; hai rule cùng khớp (rule đầu thắng); không rule nào khớp; danh sách rỗng = không ràng buộc | Đúng strategy và `RuleName` từng dòng | UT-04 |
| `Route_PeakHour_UsesBranchLocalTime_Theory` | 16:59/17:00/20:59/21:00 giờ chi nhánh, chi nhánh `Asia/Ho_Chi_Minh` và một múi giờ khác | Đúng ranh giới cao điểm | UT-04, UT-05 |
| `Route_StrategyNames_CaseInsensitive` | Rule ghi `mockalternativedecisionstrategy` | Phân giải đúng strategy | UT-04 |
| `Validate_Theory` | `DefaultStrategy` rỗng; rule thiếu `Name`; trùng `Name` khác hoa thường; `Name` 65 ký tự; `Strategy` rỗng; `When` không điều kiện; cấu hình hợp lệ | 6 ca fail kèm message nêu đường dẫn khóa; ca cuối đạt | UT-04 |
| `Constructor_UnknownStrategy_Throws_Theory` | Tên lạ ở `DefaultStrategy` và ở rule | `InvalidOperationException` nêu tên lạ và danh sách hợp lệ | UT-04 |
| `Host_InvalidRouting_FailsOnStart_Theory` | Host với cấu hình routing sai (validator) và tên strategy lạ | Không khởi động | UT-04 |
| `Reload_ValidChange_SwapsTable` | `IConfigurationRoot` in-memory; đổi rule rồi `Reload()` | `Route` trả strategy mới; reload_failures = 0 | UT-04 |
| `Reload_InvalidChange_KeepsPreviousTableAndCountsFailure_Theory` | Reload với: rule không điều kiện; tên strategy lạ; giá trị enum sai (`Channels: ["PHONE"]`); Guid sai | Không exception lọt ra; `Route` giữ kết quả cũ; reload_failures tăng 1 mỗi lần; có log Error | UT-04 |
| `Reload_ThenValidAgain_Recovers` | Sai rồi sửa đúng | Bảng mới được dùng | UT-04 |
| `EvaluateAsync_UsesRoutedStrategy` (sửa `DecisionServiceTests`) | `FakeStrategyRouter` trả strategy B | Strategy B được đánh giá; log có `RuleName` | UT-11 |
| `Startup_LegacyActiveStrategyNameKey_LogsWarning` | Host có khóa cũ | Có Warning, app vẫn khởi động, giá trị không xuất hiện trong log | UT-04 |
| `PostDecision_PeakHourRule_RoutesByBranchLocalTime` (IT) | Rule `peak-hour` → MockAlternative; hai request giờ chi nhánh 18:00 và 10:00 thứ Ba | Response `strategy` lần lượt `MockAlternativeDecisionStrategy`, `DefaultDecisionStrategy`; audit cùng tên | IT-14 |
| `PostDecision_BranchRule_RoutesSelectedBranchOnly` (IT) | Rule theo `BranchIds` | Chỉ chi nhánh đó dùng strategy của rule | IT-14 |
| Toàn bộ test hiện có | Đã chuyển sang router | Xanh | — |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh; số test và danh sách test thay thế (từ `StrategyOptions`/`ResolveActive`) ghi vào báo cáo.
- grep `ActiveStrategyName` trong `src/` chỉ còn ở message Warning của A-03.
- Commit đề xuất: `feat(routing): per-request strategy routing with hot reload`.

---

### Task 5: Circuit breaker cho Redis và tiền tố khóa cache theo cấu hình

**Files:**
- Create: `src/TADSS.Infrastructure/Caching/RedisDecisionCache.cs` (internal sealed), `RedisCacheResilience.cs` (internal static: dựng pipeline Redis), `RedisReadResult.cs` (internal record), `RedisCacheStatus.cs` (internal enum).
- Modify:
  - `src/TADSS.Infrastructure/Caching/StrategyDecisionCache.cs`: điều phối memory + `RedisDecisionCache`, cộng `InstanceName`, ghi metric.
  - `src/TADSS.Application/Abstractions/Caching/StrategyDecisionCacheKey.cs`: bỏ `tadss:` (A-09).
  - `src/TADSS.Infrastructure/Options/CacheOptions.cs`, `CacheOptionsValidator.cs`: thêm `RedisBreaker*`, kiểm `InstanceName`, xóa `TtlHours`.
  - `src/TADSS.Infrastructure/InfrastructureServiceCollectionExtensions.cs`: dựng cache với `TadssMetrics`, `TimeProvider`.
  - `src/TADSS.Runtime.Api/appsettings.json`: `RedisBreaker*`, gỡ `TtlHours`.
  - Test hiện có khẳng định tiền tố `tadss:v2:` của `Build` (thay thế có chủ đích).
- Test: `tests/TADSS.UnitTests/Infrastructure/Caching/RedisDecisionCacheTests.cs`, bổ sung `StrategyDecisionCacheTests`, `CacheOptionsValidatorTests`, `StrategyDecisionCacheKeyTests`; IT trong `tests/TADSS.IntegrationTests/FaultInjection/FaultInjectionTests.cs`.

**Interfaces:**
- Consumes: `TadssMetrics` (Task 2).
- Produces:
  - `internal enum RedisCacheStatus { Hit, Miss, Written, Bypassed, Failed }`; `internal sealed record RedisReadResult(RedisCacheStatus Status, StrategyDecision? Decision);`.
  - `internal sealed class RedisDecisionCache` — constructor `(IConnectionMultiplexer redis, CacheOptions options, TimeProvider timeProvider, ILogger logger)`; `Task<RedisReadResult> GetAsync(string fullKey, CancellationToken)`; `Task<RedisCacheStatus> SetAsync(string fullKey, StrategyDecision decision, TimeSpan ttl, CancellationToken)` trả `Written`, `Bypassed` hoặc `Failed`.
  - `StrategyDecisionCacheKey.Build(...)` trả `v2:{strategyName}:{strategyVersion}:{optionsFingerprint}:{requestHash}`.
  - `CacheOptions`: `double RedisBreakerFailureRatio = 0.5`, `int RedisBreakerSamplingSeconds = 30`, `int RedisBreakerMinimumThroughput = 10`, `int RedisBreakerBreakDurationSeconds = 30`; bỏ `TtlHours`.

**Yêu cầu:**
1. **Pipeline Redis (A-08):** Timeout(`RedisTimeoutMs`) → Circuit breaker theo `RedisBreaker*`; `TimeProvider` tiêm vào. Lệnh Redis chờ bằng `.WaitAsync(token)` để timeout cắt được. Breaker mở → `GetAsync` trả `Bypassed`, `SetAsync` trả `Bypassed`, **không** gọi Redis. Exception Redis/timeout → `Failed` (log Warning, không kèm message có thể chứa chuỗi kết nối — chỉ kiểu exception). Payload hỏng → `Miss` + log Warning như hiện nay, **không** tính vào breaker. Hủy của caller → lan ra. Log khi breaker mở/đóng/nửa mở (mức như pipeline strategy).
2. **`StrategyDecisionCache`:** khóa đầy đủ = `InstanceName + key`. Đọc: Redis (khi có và `Provider = Redis`) → nếu `Hit` trả; nếu `Miss`/`Failed`/`Bypassed` và `EnableFallbackToMemory` thì đọc memory. Ghi metric **đúng một** lần mỗi `GetAsync`: `bypass` nếu Redis `Bypassed`, ngược lại `hit`/`miss` theo kết quả cuối. Ghi: như hiện nay, bỏ qua Redis khi `Bypassed`. Hợp đồng `IStrategyDecisionCache` (lỗi chỉ là miss, chỉ hủy của caller lan ra) giữ nguyên.
3. Validator `CacheOptions`: thêm `RedisBreakerFailureRatio` trong (0, 1], `RedisBreakerSamplingSeconds` ≥ 1, `RedisBreakerMinimumThroughput` ≥ 2, `RedisBreakerBreakDurationSeconds` ≥ 1, `InstanceName` theo A-09; bỏ kiểm `TtlHours`.
4. Khóa đầy đủ mặc định khớp đúng chuỗi trước gói (`tadss:v2:...`) — có test khóa.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `GetAsync_RedisFailuresReachThreshold_OpensBreakerAndBypasses` | Moq `IDatabase.StringGetAsync` luôn ném `RedisConnectionException`; `MinimumThroughput = 2`, `FakeTimeProvider` | Hai lần đầu `Failed`; lần sau `Bypassed` và **không** gọi Redis | UT-13 |
| `GetAsync_SlowRedis_TimesOutWithinRedisTimeout` | Task Redis không bao giờ xong; `RedisTimeoutMs = 50` | `Failed` sau ~50 ms (đồng hồ giả tiến đủ) | UT-13 |
| `GetAsync_AfterBreakDuration_HalfOpenThenClosesOnSuccess` | Mở breaker; tiến đồng hồ quá `BreakDuration`; Redis khỏe lại | Lần thăm dò gọi Redis, trả `Hit`; breaker đóng | UT-13 |
| `GetAsync_CorruptPayload_IsMissAndDoesNotTripBreaker` | Payload hỏng nhiều lần | Luôn `Miss`; không lần nào `Bypassed` | UT-13 |
| `SetAsync_BreakerOpen_SkipsRedisWritesMemory` | Breaker mở, `EnableFallbackToMemory = true` | Không gọi Redis; đọc lại từ memory là hit | UT-13 |
| `GetAsync_RecordsExactlyOneCacheMetric_Theory` | Hit Redis; hit memory; miss; bypass + memory hit; bypass + memory miss | Tổng `tadss.cache.requests` = 1 mỗi lần, đúng nhãn (`bypass` cho hai ca cuối) | UT-13 |
| `FullKey_DefaultInstanceName_MatchesPreviousFormat` | `InstanceName` mặc định | Khóa = `tadss:v2:{...}` như trước gói | UT-13 |
| `Build_ReturnsVersionedKeyWithoutNamespace` (thay test cũ) | Như test cũ | `v2:` ở đầu, không có `tadss:` | UT-13 |
| `Validate_Theory` (bổ sung) | Từng giá trị biên của `RedisBreaker*`, `InstanceName` rỗng/33 ký tự/có khoảng trắng | Biên sai fail; giá trị mặc định đạt | UT-13 |
| `F6_RedisCut_BreakerOpens_RequestsBypassRedis` (IT) | Redis qua Toxiproxy; `RedisBreakerMinimumThroughput = 2`; ngắt Redis; gửi request khác payload cho tới khi breaker mở | Mọi request 200, không fallback strategy; sau khi mở, `tadss.cache.requests{bypass}` tăng; log "breaker Redis MỞ" | FI-06 |
| Test FI-06 (`F5_RedisDown_...`) và IT-13 hiện có | — | Xanh | FI-06, IT-13 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh, số test ghi vào báo cáo; `StrategyDecisionCache.cs` ≤ 250 dòng.
- grep `TtlHours` trong `src/`, `README.md` rỗng.
- Commit đề xuất: `feat(cache): redis circuit breaker and configurable key namespace`.

---

### Task 6: Shadow mode

**Files:**
- Create:
  - `src/TADSS.Application/Options/ShadowOptions.cs`, `ShadowOptionsValidator.cs`
  - `src/TADSS.Application/Shadow/ShadowWorkItem.cs`, `IShadowDispatcher.cs`, `ShadowDispatcher.cs`, `ShadowExecutor.cs`
  - `src/TADSS.Infrastructure/Shadow/ShadowExecutionWorker.cs`
  - `tests/TADSS.UnitTests/TestDoubles/FakeShadowDispatcher.cs`
- Modify:
  - `src/TADSS.Application/Decisions/DecisionComposition.cs`: thêm `ExecutionRole Role`; `IdempotencyKey` thành `string?`.
  - `src/TADSS.Application/Decisions/DecisionResponseComposer.cs`: bản ghi lấy `Role` và `IdempotencyKey` từ composition (không còn cố định `PRIMARY`).
  - `src/TADSS.Application/Decisions/DecisionService.cs`: gửi việc shadow (A-11).
  - `src/TADSS.Application/ApplicationServiceCollectionExtensions.cs`, `src/TADSS.Infrastructure/InfrastructureServiceCollectionExtensions.cs`, `src/TADSS.Runtime.Api/Composition/StartupTasks.cs` (resolve sớm `IShadowDispatcher`).
  - `src/TADSS.Runtime.Api/appsettings.json`: `Tadss:Shadow` với `Enabled: false`.
- Test: `tests/TADSS.UnitTests/Application/Shadow/ShadowDispatcherTests.cs`, `ShadowExecutorTests.cs`, `tests/TADSS.UnitTests/Application/Options/ShadowOptionsValidatorTests.cs`, bổ sung `DecisionServiceTests`, `DecisionResponseComposerTests`; IT `tests/TADSS.IntegrationTests/Shadow/ShadowExecutionTests.cs`.

**Interfaces:**
- Consumes: `IStrategyRegistry.TryResolve` (Task 4), `StrategyRoute` (Task 4), `IStrategyPipeline` (Task 2), `TadssMetrics` (Task 2), `IDecisionEvaluationStore`, `DecisionResponseComposer`.
- Produces:
  - `public sealed class ShadowOptions { public const string SectionName = "Tadss:Shadow"; bool Enabled; string Strategy = ""; double SampleRate = 1.0; int QueueCapacity = 256; }`.
  - `public sealed record ShadowWorkItem(DecisionRequestDto Request, Reservation Reservation, string RequestHash, DateTimeOffset NowUtc, string? CorrelationId);`.
  - `public interface IShadowDispatcher { void Dispatch(string primaryStrategyName, ShadowWorkItem item); }` — không bao giờ ném.
  - `public sealed class ShadowDispatcher : IShadowDispatcher` — singleton; `ChannelReader<ShadowWorkItem> Reader { get; }`; constructor `(IOptions<ShadowOptions>, IStrategyRegistry, TadssMetrics, ILogger<ShadowDispatcher>)`.
  - `public sealed class ShadowExecutor` — scoped; `Task ExecuteAsync(ShadowWorkItem item, CancellationToken cancellationToken)`; ném ra ngoài chỉ khi `cancellationToken` của worker bị hủy.
  - `public sealed class ShadowExecutionWorker : BackgroundService` (Infrastructure/Shadow) — constructor `(ShadowDispatcher dispatcher, IServiceScopeFactory scopeFactory, IOptions<ShadowOptions> options, ILogger<ShadowExecutionWorker> logger)`.

**Yêu cầu:**
1. **Options/validator (A-11):** `Enabled` → `Strategy` không rỗng; `SampleRate` trong [0, 1]; `QueueCapacity` 1..10000. `ShadowDispatcher` (khi `Enabled`) kiểm `Strategy` có trong registry, sai → `InvalidOperationException` → không khởi động.
2. **Dispatcher:** `Enabled = false` → `Dispatch` không làm gì (không tạo channel). Bật: bỏ qua khi trùng tên strategy chính (không phân biệt hoa thường); lấy mẫu; `TryWrite` thất bại → `RecordShadowDropped()` + log Debug. Mọi exception trong `Dispatch` bị bắt, log Warning.
3. **`DecisionService`:** chỉ sau khi `InsertExecutionAsync` trả bản **mới** (không phải `IsExistingWinner`) và trước khi trả response, gọi `Dispatch(route.Strategy.Strategy.StrategyName, new ShadowWorkItem(request, reservation, requestHash, nowUtc, correlationId))`. Không gọi ở nhánh replay idempotency, nhánh thua tranh chấp, nhánh lỗi.
4. **`ShadowExecutor`:** phân giải strategy shadow; `context = new DecisionContext(item.NowUtc, calendar)`; chạy `IStrategyPipeline.ExecuteAsync` (không qua cache); ghép bằng `DecisionResponseComposer` với `DecisionId` mới, `Role = SHADOW`, `IdempotencyKey = null`, `CacheHit = false`, `NowUtc = item.NowUtc`, `CorrelationId = item.CorrelationId`; ghi `InsertExecutionAsync`. Thành công → `RecordShadowExecution("recorded")`; mọi lỗi (trừ hủy của worker) → log Warning (kiểu exception, `ReservationId`, `CorrelationId`) + `RecordShadowExecution("failed")`, không ném.
5. **Worker:** `Enabled = false` → kết thúc ngay. Bật: `await foreach` trên `Reader.ReadAllAsync(stoppingToken)`, mỗi việc một scope, gọi `ShadowExecutor`; bắt mọi exception của một việc để vòng lặp tiếp tục. Khi dừng: log Information số việc còn lại trong hàng đợi (`Reader.Count` nếu hỗ trợ, nếu không thì bỏ qua và ghi sai lệch).
6. Response, bản `PRIMARY`, hàng chờ duyệt, override/outcome **không đổi** khi bật shadow (INV-8). Bản `SHADOW` thỏa mọi CHECK của B.1 (composer giữ đúng INV-7).

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Validate_Theory` | Bật mà thiếu `Strategy`; `SampleRate` −0.1/1.1; `QueueCapacity` 0/10001; cấu hình hợp lệ | Ca sai fail; hợp lệ đạt | IT-12 |
| `Constructor_EnabledWithUnknownStrategy_Throws` | Tên lạ | `InvalidOperationException` | IT-12 |
| `Dispatch_Disabled_NoOp` | `Enabled = false` | Không có việc trong hàng đợi | IT-12 |
| `Dispatch_SameStrategyAsPrimary_Skips` | Trùng tên khác hoa thường | Không có việc | IT-12 |
| `Dispatch_SampleRateZeroAndOne_Theory` | 0 và 1 | 0 việc / 1 việc | IT-12 |
| `Dispatch_QueueFull_DropsAndCounts` | `QueueCapacity = 1`, gửi 3 việc, không ai đọc | 1 việc trong hàng đợi; `tadss.shadow.dropped` = 2 | IT-12 |
| `EvaluateAsync_FreshPrimary_DispatchesOnce` | `FakeShadowDispatcher` | Một lần, đúng `NowUtc`, `RequestHash`, `CorrelationId` và tên strategy chính | UT-11 |
| `EvaluateAsync_ReplayOrRaceLoserOrFailure_DoesNotDispatch_Theory` | Replay idempotency; thua tranh chấp; ghi audit lỗi | Không gửi việc | UT-11 |
| `ExecuteAsync_HealthyStrategy_InsertsShadowRecord` | Store giả | Bản ghi `SHADOW`, `IdempotencyKey = null`, cùng `ReservationId`, `RequestHash`, `CorrelationId`, `CreatedAt = NowUtc`, `CacheHit = false`; metric `recorded` | IT-12 |
| `ExecuteAsync_PersistenceFails_LogsAndCountsFailed` | Store ném `PersistenceUnavailableException` | Không ném; metric `failed`; có log Warning | IT-12 |
| `Compose_ShadowRole_RecordHasRoleAndNullIdempotencyKey` | Composer | Đúng `Role`, `IdempotencyKey`; trạng thái và cọc như bản `PRIMARY` cùng đầu vào | UT-11 |
| `PostDecision_ShadowEnabled_StoresShadowRecordAndKeepsResponse` (IT) | Primary Default, shadow MockAlternative, `SampleRate = 1`; so với lượt cùng payload khi tắt shadow | Response giống nhau (trừ `decisionId`, `evaluatedAt`); chờ bằng polling tới khi có bản `SHADOW` cùng `reservation_id`/`request_hash`/`created_at` | IT-12 |
| `PostDecision_ShadowStrategyThrowsOrHangs_PrimaryUnaffected_Theory` (IT) | Shadow là `ScriptedStrategy` ném lỗi / treo bỏ qua token | Response `PRIMARY` như khi tắt shadow; bản `PRIMARY` lưu; bản `SHADOW` là `FALLBACK` (`APPROVED` cọc 100%); breaker của strategy chính không đổi | IT-12 |
| `ShadowRecord_NotInPendingQueue_AndNotOverridable` (IT) | Shadow trả `NEED_APPROVAL` | Hàng chờ chi nhánh không có bản `SHADOW`; override/outcome với `decisionId` của bản `SHADOW` → 404 | IT-12 |
| `PostDecision_IdempotentReplay_NoSecondShadow` (IT) | Gửi lại cùng key | Chỉ một bản `SHADOW` | IT-12 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh, số test ghi vào báo cáo.
- Commit đề xuất: `feat(shadow): asynchronous shadow evaluation with bounded queue`.

---

### Task 7: Nhiều instance (SCALE-1)

**Files:**
- Create: `tests/TADSS.IntegrationTests/Scale/MultiInstanceTests.cs` (và helper dựng host thứ hai nếu `DecisionApiTestBase` chưa hỗ trợ: `tests/TADSS.IntegrationTests/Infrastructure/SecondaryHost.cs`).
- Modify: chỉ hạ tầng test nếu cần; **không** sửa `src/` trừ khi phát hiện lỗi thật (khi đó dừng, mô tả lỗi, đề xuất cách sửa trong báo cáo trước khi sửa).

**Interfaces:**
- Consumes: hạ tầng IT hiện có (`TadssInfrastructureFixture`, `DecisionApiTestBase`, `ScriptedStrategy`), `MetricRecorder`.

**Yêu cầu:**
1. Hai host `TadssWebApplicationFactory` độc lập (DI, `IMemoryCache`, breaker riêng) dùng **chung** database của test và **chung** Redis container. Mỗi host có `ScriptedStrategy` riêng.
2. Ba kịch bản đúng dòng SCALE-1 của danh mục (Task 1).
3. Gửi song song bằng `Task.WhenAll`, chia đều hai host; không phụ thuộc thứ tự.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `SameIdempotencyKey_50ParallelAcrossTwoHosts_SingleRecordSameResponse` | 50 request cùng key và payload, 25/25 | Mọi response 200, cùng `decisionId` và nội dung; DB có đúng 1 bản `PRIMARY` cho key | SCALE-1 |
| `CacheWrittenOnHostA_ReadOnHostB` | Strategy bật cache; host A ghi (khóa X); host B request khác key, cùng payload | Host B trả `CACHE_HIT`; strategy của host B không được gọi | SCALE-1 |
| `BreakerOpenOnHostA_HostBRunsStrategy` | Strategy host A luôn lỗi tới khi breaker mở; strategy host B khỏe | Host A: `FALLBACK_CIRCUIT_OPEN`; host B: `AUTOMATED` | SCALE-1 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh (chạy 3 lần liên tiếp không flaky; ghi thời gian chạy), số test ghi vào báo cáo.
- Commit đề xuất: `test: multi-instance idempotency, cache and breaker isolation`.

---

### Task 8: Tài liệu, skill và kiểm chứng cuối gói

**Files:**
- Modify:
  - `README.md`: bảng cấu hình `Tadss:Routing` (kèm ví dụ `peak-hour`), `Tadss:Shadow`, `Tadss:Testing:FaultInjection` (ghi rõ chỉ `Testing`), `Tadss:Cache:RedisBreaker*` và `InstanceName`; gỡ `ActiveStrategyName`, `TtlHours`; câu trạng thái và số test.
  - `docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md`: Mục 4.2 (router, shadow, invoker, pipeline theo strategy, Redis breaker, `TadssMetrics` — bỏ chữ "dự kiến"/"chưa hiện thực" cho phần đã làm); Mục 4.4 sơ đồ tuần tự có router và shadow; các dòng giới hạn 99–104 (bỏ dòng đã giải quyết, thêm giới hạn của A-06, trạng thái theo instance của breaker Redis).
  - `docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md`: Redis breaker, cách ly strategy, trạng thái FI-05.
  - `docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md`: INV-8 có đường ghi `SHADOW` và IT-12; cách ghép cặp `PRIMARY`–`SHADOW` (A-11).
  - `docs/architecture/ARCHITECTURE_DECISION_RECORDS.md`: ADR-013 mục "để lại cho 5.4" → đã xử lý (A-09, A-14).
  - `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`: Mục 8 thêm giới hạn của cách ly strategy (A-06); chỉ sửa thêm khi mô tả lệch hiện thực.
  - `docs/planning/BACKLOG_SPRINTS.md`: T5.9, T5.10, T5.11 → trạng thái và số test.
  - `TADSS-Runtime/CLAUDE.md`: thư mục mới (`Routing/`, `Shadow/`, `FaultInjection/`, `Configuration/`), câu "strategy active chọn qua `ActiveStrategyName`" → `Tadss:Routing`.
  - `../.claude/skills/tadss-architecture/SKILL.md` (ngoài repo): bước "strategy mới" chọn bằng `Tadss:Routing` thay `ActiveStrategyName`; hàng "metric → `TadssMetrics`".
  - `../CLAUDE.md` (ngoài repo) chỉ khi dòng TADSS cần đổi.

**Yêu cầu:**
1. Tài liệu chỉ ghi điều đã hiện thực, sửa ở file sở hữu, không chép spec. Kiểm link nội bộ của mọi file `.md` đã sửa.
2. **Kiểm chứng cuối gói** (ghi số liệu thật vào báo cáo):
   - `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror --no-incremental` → 0/0.
   - Unit và IT (Docker) xanh. So sánh với đầu gói (1518 / 197), liệt kê số test mới theo mã danh mục.
   - `git diff aa5fa34 HEAD -- docs/contracts/` rỗng (không đổi wire).
   - `dotnet ef migrations list --no-connect` vẫn đúng 4 ID; `has-pending-model-changes` báo không có thay đổi (môi trường `Testing`).
   - `tools/expose/verify-expose.sh` đạt (compose không đổi nhưng cấu hình app đổi).
   - Khởi động app thật `Testing` với `Tadss:Shadow:Enabled = true` và routing có một rule: `/health/live` 200; một `POST` tạo cả bản `PRIMARY` và `SHADOW` (chạy trên PostgreSQL/Redis tạm như `verify-expose.sh`, không Neon). Chỉ dừng tiến trình/container do mình tạo.
   - grep: `E-LOCAL` (src, tests), `ActiveStrategyName` (src, trừ message A-03), `TtlHours` (src, README), `SC-[123]` (tài liệu hiện hành) đều rỗng.
3. **Báo cáo gói (`spec/PHASE5_REPORT_5.4.md`) gồm:**
   - Phụ lục A có cột trạng thái.
   - Bảng 6.1 của spec cho các dòng của 5.4 (INV-8/IT-12, decorator không ở Production, cấu hình routing sai không khởi động, SCALE-1).
   - Việc của người dùng: xóa khóa `ActiveStrategyName` khỏi file dev nếu còn; PR vào `develop`.
   - Mục chuyển sang 5.5 (gauge breaker, request logging, exporter, SCALE-2/3, LT-03 dùng decorator).

**Nghiệm thu:**
- Mọi mục kiểm chứng đạt; tài liệu khớp mã.
- Commit đề xuất: `docs: adaptive runtime routing, shadow and isolation (package 5.4)`.
- Đề xuất PR vào `develop` (người dùng tạo và merge).

---

## Phụ lục A — Đối chiếu spec → task

| Yêu cầu (spec 4.4 và liên quan) | Task |
| :--- | :---: |
| Routing `Tadss:Routing`, điều kiện `PeakHour`/`BranchIds`/`Channels`/`Tiers`/`IsGuest`, AND, rule đầu khớp | 4 |
| Cấu hình sai lúc khởi động → fail-fast; reload sai → giữ cũ, log, `tadss.config.reload_failures` | 4 |
| `ActiveStrategyName` thay bằng `Routing.DefaultStrategy`; registry theo tên | 4 |
| Pipeline theo strategy (V-2) | 2 |
| `FaultInjectionStrategyDecorator` chỉ ở `Testing` + `Enabled`; test kiến trúc Production | 3 |
| `IsolatedStrategyInvoker`, `tadss.strategy.abandoned`, giới hạn ghi vào Mục 8 | 2, 8 |
| Redis bọc pipeline riêng (timeout + breaker), bypass dùng memory, metric `bypass`; lỗi cache không kích hoạt fallback | 5 |
| Shadow `Tadss:Shadow`, `Channel` có giới hạn, đếm việc bị bỏ, `ShadowExecutionWorker`, bản `SHADOW` | 6 |
| IT-12 gồm shadow lỗi/treo không ảnh hưởng `PRIMARY` (INV-8) | 1 (danh mục), 6 |
| Nhiều instance: 50 request song song hai host, cache chéo host, breaker theo host | 1 (danh mục), 7 |
| 6.1: shadow không ảnh hưởng PRIMARY; decorator không ở Production; routing sai không khởi động; idempotency/cache nhiều instance | 3, 4, 6, 7 |
| 8: shadow / task bị bỏ rơi gây cạn thread pool → channel giới hạn + metric | 2, 6 |
| 9: SAS (pipeline theo strategy), EXPERIMENTAL_DESIGN (IT-12, Mục 8) | 1, 8 |
| Mang từ 5.3/ADR-013: tiền tố khóa cache, `InstanceName`/`TtlHours`, log ERR `23505`, literal `"ready"`, chuỗi E-LOCAL (tên health check đã là hằng) | 1, 5 |

Không thuộc 5.4: OpenTelemetry exporter, dashboard, gauge breaker, thứ tự request logging, SCALE-2/3, rate limit phân tán (→ 5.5).

## Phụ lục B — Thay đổi cấu hình và hành vi cần duyệt

| # | Thay đổi | Nơi ghi |
| :--- | :--- | :--- |
| B-1 | Gỡ `Tadss:Strategy:ActiveStrategyName`; strategy mặc định đặt ở `Tadss:Routing:DefaultStrategy`; khóa cũ chỉ còn sinh Warning | README, CLAUDE.md, skill |
| B-2 | Thêm `Tadss:Routing` (nạp lại nóng), `Tadss:Shadow` (mặc định tắt), `Tadss:Testing:FaultInjection` (chỉ `Testing`), `Tadss:Cache:RedisBreaker*`; gỡ `Tadss:Cache:TtlHours`; `InstanceName` có tác dụng | README, `appsettings.json` |
| B-3 | Mỗi strategy có circuit breaker riêng: strategy này lỗi không còn làm strategy khác fast-fail | SAS 4.2, RESILIENCE_AND_FAULT_TAXONOMY |
| B-4 | Strategy bỏ qua token / chạy đồng bộ lâu giờ bị cắt ở inner timeout → fallback `APPROVED` cọc 100% thay vì giữ request tới khi strategy xong | RESILIENCE_AND_FAULT_TAXONOMY, EXPERIMENTAL_DESIGN Mục 8 |
| B-5 | Redis lỗi liên tục → breaker mở, request bỏ qua Redis ngay (không chờ `RedisTimeoutMs` mỗi lần) | SAS, RESILIENCE_AND_FAULT_TAXONOMY |
| B-6 | Khi bật shadow: có thêm bản ghi `role = SHADOW` (không khóa idempotency, ghép cặp với `PRIMARY` qua `reservation_id`, `request_hash`, `created_at`, `correlation_id`) | INVARIANTS INV-8 |
| B-7 | Log Error của EF cho mỗi lỗi lệnh SQL hạ xuống Debug (lỗi thật vẫn được ghi bởi store/handler) | README (mục log) |
| B-8 | Mã test SC-1…SC-3 đổi thành SCALE-1…SCALE-3 | EXPERIMENTAL_DESIGN, spec |

Không đổi: wire (OpenAPI, JSON schema), SDK và baseline public API, mã lỗi, status HTTP, schema DB, khóa cache mặc định.

## Ghi chú cho gói 5.5

- `TadssMetrics` là nơi khai báo tên metric duy nhất: 5.5 thêm `tadss.decisions`, `tadss.decision.duration`, `tadss.pipeline.duration`, `tadss.fallbacks`, `tadss.circuit.state` (gauge theo strategy, cần đọc trạng thái breaker từ registry pipeline của Task 2), `tadss.pending_approvals`, `tadss.overrides`, rồi `AddMeter(TadssMetrics.MeterName)` ở OpenTelemetry.
- LT-03 (sự cố dưới tải) bật decorator bằng `ASPNETCORE_ENVIRONMENT=Testing` + `Tadss:Testing:FaultInjection:*` trong stack đánh giá.
- `UseSerilogRequestLogging` ghi "500" cho phản hồi do exception tạo ra → xem lại thứ tự pipeline khi gắn observability.
- Breaker strategy, breaker Redis và cache L1 đều theo từng instance (SCALE-1 đã chứng minh); SCALE-2 cần dashboard tách theo `service.instance.id`.
