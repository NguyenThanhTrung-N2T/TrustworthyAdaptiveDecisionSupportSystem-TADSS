# Gói 5.3 — Quản trị và bảo mật: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hoàn thiện bốn nhóm việc để TADSS đủ an toàn khi expose ra ngoài qua ngrok:
- Ba endpoint quản trị còn thiếu của contract v2: hàng chờ duyệt, override, outcome (INTEGRATION_SPEC §3.5–3.7).
- Xác thực service-to-service bằng `X-Api-Key`.
- Gói cơ chế bảo mật SEC-1…SEC-7.
- Expose qua ngrok kèm truy cập database (E-NGROK, NG-1…NG-5).

**Architecture:** Giữ Clean Architecture của 5.2d. Phần quản trị gồm ba lớp:
- **Port:** đọc hàng chờ nằm ở `IDecisionQueryStore`; ghi override/outcome nằm ở port mới `IDecisionGovernanceStore`.
- **Use case:** `OverrideService`, `OutcomeService`, và hàng chờ trong `DecisionQueryService` (Application).
- **Adapter:** EF Core/PostgreSQL ở Infrastructure.

Bảo mật nằm ở tầng presentation (`TADSS.Runtime.Api`):
- Middleware xác thực, security headers, giới hạn kích thước, rate limiting.
- Mọi phản hồi lỗi đi qua **một** bộ ghi problem+json.

Phần expose gồm hai file compose override, kèm script kiểm chứng chạy được ở local và CI.

**Tech Stack:**
- Không thêm NuGet vào `src/`. Rate limiting, forwarded headers, HSTS đều có sẵn trong ASP.NET Core 8; băm SHA-256 và `CryptographicOperations.FixedTimeEquals` thuộc BCL.
- Nền tảng như 5.2d: .NET 8, EF Core 8 + Npgsql 8, StackExchange.Redis 2.8.16, Polly.Core 8.4.2, Swashbuckle 6.6.2.
- Kiểm thử như 5.2d: Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy), xUnit 2.5.3, FluentAssertions 6.12.1.
- Hạ tầng triển khai: Docker Compose ≥ 2.24.4 (`!override`/`!reset`), ngrok agent v3 (image ghim tag + digest), Dependabot.

**Spec:** [`PHASE5_DESIGN_SPEC.md`](PHASE5_DESIGN_SPEC.md):
- Mục 3 (dòng 5.3).
- Mục 4.2.3: bảng map lỗi, dòng `DecisionStateConflictException`.
- Mục 4.2.4: việc mang từ review cuối 5.2c sang 5.3.
- Mục 4.3, 4.3.1, 4.3.2.
- Mục 6, 6.1, 8, 9.

Nguồn sở hữu:
- [`INTEGRATION_SPEC.md`](../docs/contracts/INTEGRATION_SPEC.md) §3.2, §3.4–3.9, §4.3.
- [`DECISION_PERSISTENCE_INVARIANTS.md`](../docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md) Mục B.2, B.3, B.5, C (INV-5, INV-6, INV-8).
- [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) (IT-06…IT-11, UT-08, UT-09, UT-14).
- [`SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](../docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md) Mục 2, 4.2, 4.4.

Gói trước: [`PHASE5_PLAN_5.2d_CLEAN_ARCHITECTURE.md`](PHASE5_PLAN_5.2d_CLEAN_ARCHITECTURE.md) (mục "Ghi chú cho gói 5.3" và các mục "Không xử lý ở 5.2d").

> **Cách đọc plan này:**
>
> - Plan mô tả **yêu cầu kỹ thuật**: trách nhiệm, vị trí, chữ ký, quy tắc, bảng test case và tiêu chí nghiệm thu. Plan **không** chứa mã hiện thực.
> - Mục **Interfaces** là ràng buộc, vì task sau dựa vào đúng tên và kiểu đó.
> - Đường dẫn viết từ gốc repo. Tên kiểu và namespace viết theo trạng thái **sau** task đó.
> - Gói này **có chủ đích đổi hành vi và bổ sung contract**. Mọi thay đổi như vậy được liệt kê ở bảng "Quyết định thiết kế" và Phụ lục B. Người dùng duyệt các bảng đó trước Task 1.

## Global Constraints

**Build và test sau mỗi task:**
- `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror` → **0 warning, 0 error**.
- Unit test và **toàn bộ** integration test xanh (Docker bật).

**Số lượng test:** test hiện có không được mất (đầu gói: **1112 unit / 121 IT**, commit `05aa840`). Mỗi task ghi vào báo cáo số test trước và sau của cả hai project (đếm bằng `dotnet test --list-tests`).
- Ngoại lệ: test bị **thay thế** có chủ đích (ví dụ `Routes_OnlyV2DecisionEndpointsExist` đổi tập route). Ghi tên test cũ và test thay thế vào báo cáo.

**Thay đổi hành vi và contract:** chỉ những thay đổi có trong bảng Quyết định và Phụ lục B.
- Mọi thay đổi khác của wire, mã lỗi hoặc status HTTP ⇒ dừng và hỏi.
- `docs/contracts/openapi-v2.yaml` **được sinh lại có chủ đích** ở các task đổi bề mặt HTTP trong Swagger: 4, 5, 6, 7. Health endpoint không nằm trong Swagger nên Task 8 không đổi file này. Cách sinh: chạy `OpenApi_GeneratedDocument_MatchesCommittedFile`, kiểm tra `TestResults/openapi-v2.actual.yaml`, rồi copy đè. Diff YAML phải có trong báo cáo task và chỉ chứa thay đổi task đó gây ra.
- `docs/contracts/baseline_public_api.txt` **chỉ đổi ở Task 6**: thêm đúng hai hằng `ErrorCodes.RateLimited`, `ErrorCodes.PayloadTooLarge`, trong cùng commit với mã và có ghi lý do (D-06). Không task nào khác được đổi baseline.
- `docs/contracts/schemas/v2/*.json` là nguồn chuẩn. Thêm schema mới ở Task 4; sửa schema cũ chỉ khi Phụ lục B cho phép.

**Database:**
- **Không** tạo migration.
- **Không** `dotnet ef database update`.
- **Không** kết nối Neon.
- Bảng `decision_overrides`, `decision_outcomes` và index `idx_de_pending`, `idx_de_reservation_created` đã có từ 5.2c. Nếu IT-09 cho thấy cần index mới ⇒ dừng và hỏi (D-05).

**Cấm nới kiểm tra:** không `Skip`, không nới assertion, không `#pragma warning disable`, không `!` null-forgiving, không xóa test để chữa test đỏ.

**Quy tắc kiến trúc (UT-14, phải xanh sau mọi task):**
- Phụ thuộc hướng vào trong. Chỉ `Program.cs` và `Composition/` dùng kiểu Infrastructure.
- Mỗi kiểu public một file, tên file = tên kiểu, namespace = project + thư mục.
- Options và validator ở `Options/` của project sở hữu. Exception chỉ ở `TADSS.Application.Exceptions`. Port chỉ ở `TADSS.Application/Abstractions/**`.
- Mỗi file `src/**/*.cs` ≤ **250 dòng** (trừ `Migrations/`). File sắp vượt trần thì tách theo trách nhiệm (plan đã chỉ chỗ tách).
- Mọi options có `SectionName` phải có validator đăng ký cùng `ValidateOnStart`.

**Quy ước:**
- Options là `{X}Options` + `{X}OptionsValidator : IValidateOptions<{X}Options>`.
- Hằng do contract cố định nằm ở `TADSS.Application.Common.ContractLimits` (giới hạn) hoặc `TADSS.Runtime.Api.Common.HttpHeaderNames` (tên header). Mỗi hằng một nơi.
- Thời gian lấy từ `TimeProvider`. Mốc ghi vào DB cắt về bội số µs bằng `AuditTimestamps.TruncateToMicroseconds` (Task 1).
- Comment và message tiếng Việt như mã hiện có; định danh tiếng Anh. Áp dụng "Bẫy namespace `Options`" của plan 5.2d: viết đủ `Microsoft.Extensions.Options.Options.Create(...)`.

**Test:**
- Tên `{Method}_{Scenario}_{Expected}`, có `[Trait("CatalogId", …)]` và XML summary tiếng Việt. IT thêm `[Trait("Category","Integration")]`.
- Mã danh mục mới của gói: UT-15, SEC-1…SEC-7, NG-1…NG-5 (thêm vào danh mục ở Task 1).
- Test bảo mật **không cần database** chạy trong `TADSS.UnitTests` qua host in-process `DecisionsApiFactory`, mang `CatalogId` là mã SEC tương ứng (F-11).

**Bảo mật:**
- Không in, không ghi vào file commit bất kỳ giá trị secret nào: chuỗi kết nối Neon/Redis thật, `NGROK_AUTHTOKEN`, API key thật.
- Người dùng cho phép đọc file secret dev (`appsettings.Development.json`, `.env`) khi cần đối chiếu **tên khóa**. Dùng lệnh chỉ in tên khóa hoặc boolean.
- API key dùng trong test là chuỗi giả cố định trong mã test, không phải secret.
- Lệnh `dotnet ef` (chỉ đọc) chạy với `ASPNETCORE_ENVIRONMENT=Testing`.

**Git:**
- Stage bằng đường dẫn tường minh.
- **Không** stage `spec/PHASE5_*.md`.
- Không push, stash, reset, `checkout --`, clean.
- Subject commit theo Conventional Commits, tiếng Anh ASCII.

**Sai lệch so với giả định:** nếu .NET hoặc thư viện chạy khác giả định trong plan (ví dụ chữ ký overload, hành vi TestServer), sửa tối thiểu để giữ đúng **ý định**, ghi "sai lệch" vào báo cáo task, không nới test.

## Quyết định thiết kế của gói

| Mã | Quyết định | Lý do |
| :--- | :--- | :--- |
| **F-01** | Tách port theo chiều đọc/ghi (ISP). `GetPendingApprovalsAsync` thêm vào `IDecisionQueryStore` (đọc). `IDecisionGovernanceStore` mới chỉ có `InsertOverrideAsync`, `InsertOutcomeAsync` (ghi). Hiện thực: `EfDecisionAuditStore` (đọc) và `EfDecisionGovernanceStore` (ghi, file mới để giữ trần 250 dòng) | Ghi chú cuối plan 5.2d đặt cả ba method vào port quản trị. Nhưng hàng chờ là truy vấn đọc, do `DecisionQueryService` dùng (spec D-02). Đặt vào port ghi thì service đọc phải phụ thuộc port ghi |
| **F-02** | SQL hàng chờ **tương đương** Mục B.5 nhưng viết theo dạng dùng được partial index (xem Task 2):<br>• Ứng viên `PRIMARY` + `NEED_APPROVAL` của chi nhánh, lọc bằng `idx_de_pending`.<br>• `NOT EXISTS` bản `PRIMARY` mới hơn cùng `reservation_id` và chi nhánh (dùng `idx_de_reservation_created`).<br>• `NOT EXISTS` override/outcome.<br>• `approval_expires_at > @nowUtc`: tham số lấy từ `TimeProvider`, không dùng `now()`.<br>• Sắp `approval_expires_at, decision_id`.<br>Cập nhật B.5 theo câu SQL mới | `DISTINCT ON` trên toàn bộ bản `PRIMARY` của chi nhánh không dùng được `idx_de_pending`, vì partial index chỉ chứa bản `NEED_APPROVAL`. IT-09 yêu cầu `EXPLAIN` dùng index. Dùng `@nowUtc` để test điều khiển được đồng hồ và nhất quán với kiểm tra hạn của override. `decision_id` phá thế hòa nên phân trang tất định |
| **F-03** | Thứ tự kiểm tra của override, dừng ở bước đầu tiên không đạt:<br>(1) Request sai hình thức → 400.<br>(2) Không có quyết định, hoặc bản ghi là `SHADOW` → 404 `DECISION_NOT_FOUND`.<br>(3) Đã có override: cùng `X-Idempotency-Key` và cùng payload → 200 trả bản cũ; cùng key khác payload → 409 `IDEMPOTENCY_KEY_REUSE`; khác key → 409 `ALREADY_OVERRIDDEN`.<br>(4) `status ≠ NEED_APPROVAL` hoặc đã có outcome → 409 `NOT_PENDING_APPROVAL`.<br>(5) `approvalExpiresAt ≤ now` → 409 `APPROVAL_EXPIRED`.<br>(6) Insert; thua tranh chấp `UNIQUE(decision_id)` → xử lý như (3) với bản thắng.<br>"Cùng payload" = cùng `action`, `reason` (so ordinal), `actorId`, và cùng giá trị cọc mà request gửi: `rate` so với `deposit_rate` đã lưu, `amount` so với `deposit_amount` đã lưu | INTEGRATION_SPEC §3.6, §3.8 và spec 4.3. Hai dòng 409 của contract (`IDEMPOTENCY_KEY_REUSE`, `ALREADY_OVERRIDDEN`) được dùng đúng nghĩa. `SHADOW` không bao giờ được trả cho client, nên với client nó là "không tồn tại" |
| **F-04** | Quy đổi cọc khi APPROVE:<br>• Gửi `rate` → `amount = DepositCalculator.Calculate(...)`.<br>• Gửi `amount` → `rate = round(amount / totalPrice, 4, AwayFromZero)` bằng method mới `DepositCalculator.FromAmount`; `amount` giữ nguyên. `totalPrice = 0` thì `amount` phải là 0 và `rate = 0`.<br>Trả 400 `VALIDATION_ERROR` khi:<br>• `rate` ngoài [0, 1] hoặc nhiều hơn 4 chữ số thập phân.<br>• `amount` âm, lớn hơn `totalPrice`, hoặc nhiều chữ số thập phân hơn tiền tệ cho phép.<br>`totalPrice`, `currency` lấy từ `request_payload` của quyết định | Cột `numeric(5,4)` (đã chốt ở 5.2c) và nguyên tắc "một nơi quy đổi cọc" (spec 4.2.3). Từ chối thay vì để PostgreSQL làm tròn âm thầm |
| **F-05** | Outcome:<br>• Áp cho mọi quyết định `PRIMARY` bất kể trạng thái; `SHADOW` → 404.<br>• Chống trùng theo payload (`outcome` và `occurredAt` sau khi cắt về µs): trùng → 200 trả bản đã lưu; khác → 409 `OUTCOME_ALREADY_RECORDED`.<br>• `X-Idempotency-Key` bắt buộc và được kiểm tra theo §3.2 nhưng **không lưu**, vì bảng không có cột này và gói không tạo migration.<br>• Thời điểm trong response trả theo UTC | INTEGRATION_SPEC §3.7. Cắt µs để lần gửi lại khớp giá trị đã đọc lại từ `timestamptz` |
| **F-06** | Tranh chấp override ↔ outcome trên cùng quyết định (kiểm tra "chưa có outcome" rồi mới insert) **không** được khóa nguyên tử. Ghi giới hạn này vào DECISION_PERSISTENCE_INVARIANTS (INV-5) và INTEGRATION_SPEC §4.3. Tranh chấp override ↔ override thì DB bảo đảm bằng `UNIQUE` (IT-07) | Khóa nguyên tử cần khóa hàng `FOR UPDATE` và đưa quy tắc nghiệp vụ vào adapter, phá ranh giới tầng. Cửa sổ tranh chấp chỉ có ở đúng thời điểm hết hạn duyệt; BE xử lý theo trạng thái booking |
| **F-07** | API key:<br>• Bắt buộc ở **mọi** môi trường cho `/api/*`.<br>• `Development`/`Testing` được khởi động khi `ApiKeys` rỗng (ghi Warning) nhưng mọi request `/api/*` bị 401. Các môi trường khác thì không khởi động.<br>• Miễn trừ `/health/live`, `/health/ready` ở mọi môi trường; `/swagger/*` chỉ ở `Development`.<br>• Cấu hình `{ ClientId, KeySha256 }` (hex 64 ký tự, không phân biệt hoa thường).<br>• So sánh: băm SHA-256 khóa nhận được rồi `FixedTimeEquals` với **mọi** mục, không thoát sớm.<br>• Ngoài `Development`/`Testing`, validator từ chối mã băm của khóa E-LOCAL công khai (F-18) | Spec 4.3, 4.3.1: "không có đường nào để chạy mở hoàn toàn sau tunnel". Health phải gọi được không cần key (healthcheck của container, NG-2, NG-4). Chặn mã băm khóa công khai để không lỡ expose bằng khóa ai cũng biết |
| **F-08** | Một bộ ghi lỗi `ProblemResponseWriter` (Runtime.Api/ErrorHandling) dựng **mọi** body problem+json: `GlobalExceptionHandler` (exception), 401 (middleware xác thực), 413 (middleware kích thước), 429 (callback `OnRejected` của rate limiter). Các phản hồi từ chối do middleware tạo **không** ném exception | "Xử lý lỗi một đường" (spec 4.2.4). Cùng dạng body: `code`, `traceId`, `retryAfterSeconds`. Không tốn chi phí exception cho mỗi request bị từ chối |
| **F-09** | Thứ tự pipeline, khai báo tại `UseTadssRequestPipeline`:<br>1. forwarded headers (khi bật F-10)<br>2. correlation id<br>3. exception handler<br>4. security headers + HSTS (ngoài `Development`/`Testing`)<br>5. Serilog request logging<br>6. https redirection<br>7. giới hạn kích thước<br>8. Swagger (chỉ `Development`)<br>9. xác thực API key<br>10. rate limiter<br>11. `UseRouting()` tường minh<br>Bỏ `UseAuthorization()` | Giữ đúng thứ tự tương đối của spec 4.3.2. Logging đứng trước các bước từ chối nên ghi được cả 401/413/429. Swagger đứng trước xác thực nên miễn trừ được ở Development. Gọi `UseRouting` tường minh để định tuyến chạy **sau** các bước bảo mật (`WebApplication` mặc định chèn routing ở đầu). `UseAuthorization` không có policy nào, là mã chết |
| **F-10** | `ForwardedHeaders` (XFor, XProto, XHost; xóa `KnownNetworks`/`KnownProxies` như SmashCourt-BE) chỉ bật khi `Tadss:Security:TrustForwardedHeaders = true`. Mặc định `false`; `docker-compose.ngrok.yml` đặt `true` | Tin header chuyển tiếp chỉ an toàn khi chỉ proxy chạm được app (spec 4.3.1). Khi chạy `dotnet run` hoặc E-LOCAL, client có thể giả header |
| **F-11** | Test bảo mật không cần DB chạy trong `TADSS.UnitTests` (host in-process với service double): SEC-1, SEC-2, SEC-3, SEC-4, phần log của SEC-7, thứ tự pipeline. SEC-5 và IT-11 chạy trong `TADSS.IntegrationTests` | Nhanh, tất định, không cần Docker. Vẫn đi qua pipeline HTTP thật của app |
| **F-12** | `ErrorCodes.RateLimited = "RATE_LIMITED"` và `ErrorCodes.PayloadTooLarge = "PAYLOAD_TOO_LARGE"` thêm vào SDK, cập nhật baseline trong cùng commit (Task 6) | Người dùng đã duyệt hai mã này ở P-04. `ErrorCodes` là danh mục mã của contract. SmashCourt-BE không tham chiếu SDK nên không bị ảnh hưởng |
| **F-13** | Thiếu chuỗi kết nối thì dừng ngay ở **mọi** môi trường:<br>• Chuỗi đi qua `DatabaseOptions.ConnectionString` (bind từ `ConnectionStrings:DefaultConnection` của cấu hình cuối cùng trong DI).<br>• `DatabaseOptionsValidator` từ chối khi rỗng, không parse được bằng `NpgsqlConnectionStringBuilder`, hoặc thiếu `Host`. Chuỗi giữ chỗ `your-connection-string` bị từ chối theo cách này.<br>• DbContext và health check PostgreSQL đọc chuỗi từ options (không chụp chuỗi lúc đăng ký).<br>• Message không chứa giá trị | Việc mang từ 5.2c và SEC-4. `Development`/`Testing` luôn có chuỗi thật hoặc chuỗi test, nên áp ở mọi môi trường an toàn hơn và đơn giản hơn. Đọc qua options để host test thay chuỗi bằng cấu hình là đủ |
| **F-14** | Đọc `request_payload`/`response_payload` bằng **options đọc nới lỏng** của Application: bản sao `TadssJson.Options` có thêm modifier `UnmappedMemberHandling = Skip` và `IsRequired = false`, vẫn giữ converter enum/thời gian chặt. Đường ghi giữ `TadssJson.Options` chặt | Việc mang từ 5.2c: DTO thêm trường trước M5 không làm GET/replay của bản ghi cũ trả 500. Không đụng SDK |
| **F-15** | Hủy của caller trong pha SQL: khi token của caller đã hủy, lỗi lưu trữ có thể thử lại được dịch thành `OperationCanceledException(token)` (giữ inner exception) thay vì `PersistenceUnavailableException`. Logic nằm ở `PersistenceFailureTranslator` (Infrastructure, dùng chung cho hai store) | Việc mang từ 5.2c: không còn log 503 Error giả khi client đóng kết nối |
| **F-16** | Health:<br>• `/health/live`: không check nào.<br>• `/health/ready`: PostgreSQL bắt buộc (`Unhealthy` → 503); Redis có `failureStatus = Degraded` → 200, chỉ đăng ký khi `Provider = Redis`, dùng multiplexer có sẵn.<br>• Body chỉ là chữ trạng thái.<br>• **Gỡ** endpoint gộp `/health` | SEC-5. `/health` trùng `/health/ready` và làm rộng bề mặt |
| **F-17** | Giới hạn contract bổ sung (`ContractLimits`):<br>• `page ≥ 1` (mặc định 1).<br>• `pageSize` 1..100 (mặc định 20).<br>• `X-Actor-Id` 1..100 ký tự (khớp `varchar(100)`).<br>• `reason` 5..500 ký tự.<br>Độ dài của `X-Actor-Id` và `reason` đếm theo **Unicode code point** (`EnumerateRunes`), vì PostgreSQL đếm ký tự như vậy. Sai → 400 `VALIDATION_ERROR` | Contract §3.5 chưa có trần `pageSize`. `actor_id varchar(100)`. Đếm theo UTF-16 có thể lệch với `char_length` của PostgreSQL khi có emoji, làm CHECK của DB nổ thành 500 |
| **F-18** | `docker-compose.yml` (E-LOCAL) có sẵn một API key công khai cho môi trường local: khóa `tadss-local-dev-key` (ghi trong README), mã băm làm mặc định của `TADSS_API_KEY_SHA256`. `docker-compose.ngrok.yml` **bắt buộc** biến này (`${TADSS_API_KEY_SHA256:?…}`) và chạy `Staging`; validator ở `Staging` từ chối mã băm công khai (F-07) | k6 của SV2 và script FI chạy được ngay trên E-LOCAL. Lỡ expose bằng khóa công khai bị chặn hai lớp: compose báo lỗi, app không khởi động |
| **F-19** | Chuỗi cung ứng:<br>• `Directory.Build.props` bật `RestorePackagesWithLockFile`; commit mọi `packages.lock.json`.<br>• CI và Dockerfile restore `--locked-mode`.<br>• CI chạy `dotnet list package --vulnerable --include-transitive` qua script; có mức High/Critical → đỏ.<br>• `.github/dependabot.yml` cho `nuget`, `docker`, `github-actions` | SEC-7 |
| **F-20** | Image ngrok ghim `ngrok/ngrok:<3.x.y>-alpine@sha256:<digest>`; implementer chọn bản 3.x mới nhất khi làm và ghi vào báo cáo. Lệnh chạy qua `sh -c`: có `NGROK_DOMAIN` thì `ngrok http --url "$NGROK_DOMAIN" tadss-runtime:8080`, không có thì bỏ `--url`. Giao diện kiểm tra bind `127.0.0.1:4040` | Spec 4.3.1. Compose không truyền tham số có điều kiện được nếu không dùng shell |
| **F-21** | Tách `TruncateToMicroseconds` (đang private trong `DecisionService`) thành `TADSS.Application.Common.AuditTimestamps.TruncateToMicroseconds`, dùng chung cho đánh giá, override và outcome | DRY. Ba luồng cùng ghi `timestamptz` |
| **F-22** | Thêm vào danh mục kiểm thử (Task 1):<br>• UT-15: quy tắc quản trị của override/outcome bằng test double.<br>• SEC-1…SEC-7: nguyên văn spec 4.3.2.<br>• NG-1…NG-5: nguyên văn spec 4.3.1.<br>Sửa mô tả UT-14: bỏ ngoại lệ `DatabaseOptions`, thêm thứ tự middleware bảo mật | D-10: mọi test gắn mã danh mục. Spec Mục 9 giao việc bổ sung mã cho 5.3 |
| **F-23** | Làm sạch `detail` và `errors` của 400 do lỗi giải mã body: không bao giờ trả `Exception.Message` của System.Text.Json (lộ tên kiểu .NET, `LineNumber`, `BytePositionInLine`). Thay bằng câu tiếng Việt theo trường (giữ đường dẫn JSON do client gửi) | Mục hoãn từ 5.2c (Task 8 minor) sang gói bảo mật: tránh lộ chi tiết nội bộ |

Nếu người dùng không đồng ý mục nào, sửa plan trước khi chạy Task 1.

## Chuẩn bị (người dùng thực hiện)

1. Bật Docker Desktop; kiểm tra `docker compose version` ≥ **2.24.4**.
2. Duyệt bảng Quyết định và Phụ lục B (thay đổi contract và hành vi).
3. Từ Task 5, app ở `Development` trả 401 cho `/api/*` nếu chưa có API key (F-07). Tạo khóa dev rồi đặt **mã băm** vào `Tadss:Security:ApiKeys` của `appsettings.Development.json` hoặc User Secrets (quy ước hướng A: secret chỉ ở file dev hoặc biến môi trường). Lệnh tạo khóa và mã băm có trong README sau Task 10. Swagger UI có nút "Authorize" để nhập khóa.
4. Cuối gói:
   - (a) Review diff `openapi-v2.yaml` và `baseline_public_api.txt`.
   - (b) Chạy **NG-4** với `NGROK_AUTHTOKEN` của mình (hướng dẫn trong README). NG-3 chế độ B (Neon) cũng do người dùng chạy, sau khi Neon đã có migration `DecisionPersistenceV2`.

## Cấu trúc đích

Chỉ liệt kê file mới hoặc file đổi đáng kể.

```text
src/
  Shared.DecisionContractSDK/Common/ErrorCodes.cs           + RateLimited, PayloadTooLarge (Task 6)
  TADSS.Application/
    Abstractions/Persistence/  IDecisionGovernanceStore, InsertOverrideResult, InsertOutcomeResult, PendingApprovalsPage (Task 2);
                               IDecisionQueryStore + GetPendingApprovalsAsync (Task 2)
    Common/                    AuditTimestamps (Task 1); ContractLimits + MaxActorIdLength, MinReasonLength, MaxReasonLength,
                               DefaultPageSize, MaxPageSize (Task 3)
    Decisions/                 DecisionPayloadSerializer (đọc nới lỏng, Task 1); DepositCalculator.FromAmount (Task 3);
                               IDecisionQueryService/DecisionQueryService + hàng chờ (Task 3)
    Exceptions/                DecisionStateConflictException (Task 3)
    Governance/                IOverrideService, OverrideService, OverrideRequestValidator, GovernanceResponseMapper (Task 3)
    Outcomes/                  IOutcomeService, OutcomeService (Task 3)
  TADSS.Infrastructure/
    Caching/                   RedisHealthCheck (Task 8)
    Options/                   DatabaseOptions + ConnectionString, DatabaseOptionsValidator (Task 1)
    Persistence/               PersistenceFailureTranslator (Task 1), EfDecisionGovernanceStore, PendingApprovalsQuery (Task 2)
  TADSS.Runtime.Api/
    Common/                    HttpHeaderNames + ActorId, ApiKey; RequestHeaders (Task 4)
    Composition/               SecurityServiceCollectionExtensions (Task 5–7), EndpointMappingExtensions (Task 8)
    Controllers/               DecisionApprovalsController, DecisionOutcomesController (Task 4)
    ErrorHandling/             ProblemResponse, ProblemResponseWriter (Task 5)
    Middlewares/               ApiKeyAuthenticationMiddleware (Task 5), SecurityHeadersMiddleware, RequestSizeLimitMiddleware (Task 6)
    OpenApi/                   SecurityResponsesOperationFilter (Task 5)
    Options/                   SecurityOptions, ApiKeyClientOptions, SecurityOptionsValidator (Task 5), RateLimitingOptions (Task 7)
    Security/                  ApiKeyVerifier, ApiClientIdentity, SecurityPaths (Task 5)
docs/contracts/schemas/v2/     pending-approvals, override-request, override-response, outcome-request, outcome-response (Task 4)
docker-compose.yml             tham số hóa cổng/image, API key E-LOCAL, healthcheck (Task 10)
docker-compose.ngrok.yml, docker-compose.neon.yml, .env.example (Task 10)
Directory.Build.props, **/packages.lock.json, .github/dependabot.yml, tools/ci/check-vulnerable-packages.sh (Task 9)
tools/expose/verify-expose.sh  (Task 10)
tests/TADSS.UnitTests/         Api/Security/, Application/Governance/, Application/Outcomes/, TestData/TestApiKeys.cs
tests/TADSS.IntegrationTests/  OverrideEndpointTests, OutcomeEndpointTests, PendingApprovalsEndpointTests, Persistence/GovernanceStoreTests,
                               Persistence/PendingApprovalsQueryTests, Security/ApiKeyIntegrationTests, Security/HealthEndpointTests,
                               Infrastructure/ComposeFixtureAlignmentTests
```

Thứ tự phụ thuộc giữa các task: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11.

---

### Task 1: Việc mang từ 5.2c và mã danh mục mới

**Files:**
- Create:
  - `src/TADSS.Application/Common/AuditTimestamps.cs`
  - `src/TADSS.Infrastructure/Options/DatabaseOptionsValidator.cs`
  - `src/TADSS.Infrastructure/Persistence/PersistenceFailureTranslator.cs`
- Modify:
  - `src/TADSS.Application/Decisions/DecisionService.cs`: dùng `AuditTimestamps`.
  - `src/TADSS.Application/Decisions/DecisionPayloadSerializer.cs`: đọc nới lỏng (F-14).
  - `src/TADSS.Infrastructure/Options/DatabaseOptions.cs`: thêm `ConnectionString`.
  - `src/TADSS.Infrastructure/InfrastructureServiceCollectionExtensions.cs`, `Persistence/PersistenceServiceCollectionExtensions.cs`, `Persistence/PersistenceHealthChecks.cs`: đọc chuỗi kết nối từ options (F-13).
  - `src/TADSS.Infrastructure/Persistence/EfDecisionAuditStore.cs`: dùng `PersistenceFailureTranslator`.
  - Test host: `tests/TADSS.IntegrationTests/TadssWebApplicationFactory.cs` và `tests/TADSS.UnitTests/TestDoubles/DecisionsApiFactory.cs` đặt `ConnectionStrings:DefaultConnection` (chuỗi container / `TestConnectionStrings`).
  - Mọi test dựng `ServiceCollection` có resolve `DatabaseOptions` thì cung cấp chuỗi test.
  - `tests/TADSS.UnitTests/Api/Composition/AppSettingsValidationTests.cs`: chuỗi kết nối là ô giữ chỗ nên test thay bằng chuỗi test. Bỏ ngoại lệ `DatabaseOptions` khỏi test "mọi options có validator".
  - `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`: mã mới (F-22).
- Test: `tests/TADSS.UnitTests/Application/Decisions/DecisionPayloadSerializerTests.cs` (thêm ca), `tests/TADSS.UnitTests/Infrastructure/Options/DatabaseOptionsValidatorTests.cs`, `tests/TADSS.UnitTests/Infrastructure/Persistence/PersistenceFailureTranslatorTests.cs`.

**Interfaces:**
- Produces:
  - `public static class AuditTimestamps { public static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value); }`. Kết quả có offset 0.
  - `DatabaseOptions.ConnectionString : string?`. `DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>`.
  - `internal static class PersistenceFailureTranslator`:
    - `Exception? Translate(Exception exception, CancellationToken cancellationToken)`: trả exception thay thế, hoặc `null` nếu giữ nguyên exception gốc.
    - `Task<T> ExecuteAsync<T>(Func<Task<T>> operation, ILogger logger, CancellationToken cancellationToken)`.

**Yêu cầu:**
1. **F-21:** chỉ dời hàm. Giá trị `created_at` sinh ra giữ nguyên.
2. **F-14 (đọc nới lỏng):**
   - Options đọc là `internal static readonly` trong `DecisionPayloadSerializer`.
   - Dựng từ `new JsonSerializerOptions(TadssJson.Options)`, đặt `TypeInfoResolver` = resolver hiện có (hoặc `DefaultJsonTypeInfoResolver`) `.WithAddedModifier(...)`. Modifier đặt `UnmappedMemberHandling = Skip` cho kiểu object và `IsRequired = false` cho mọi property.
   - Mọi method `Deserialize*` dùng options đọc. `Serialize*` giữ `TadssJson.Options`.
   - Payload JSON `null` hoặc hỏng vẫn ném `JsonException` như cũ.
3. **F-13:**
   - Bind: `AddOptions<DatabaseOptions>().Bind(section).Configure<IConfiguration>((o, cfg) => o.ConnectionString = cfg.GetConnectionString("DefaultConnection")).ValidateOnStart()`. `IConfiguration` lấy từ DI, tức cấu hình cuối cùng gồm cả override của host test.
   - `AddDbContext((sp, options) => …UseNpgsql(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString, …))`.
   - Health check dùng overload `AddNpgSql(Func<IServiceProvider, string> connectionStringFactory, …)` của `AspNetCore.HealthChecks.NpgSql` 8.0.2. Nếu overload không tồn tại, ghi sai lệch, giữ ý định "không chụp chuỗi lúc đăng ký".
   - Message lỗi: `"ConnectionStrings:DefaultConnection thiếu hoặc không phải chuỗi kết nối PostgreSQL hợp lệ (cần Host)."`. Không nối `ex.Message` của Npgsql, không nối giá trị.
   - `TadssWebApplicationFactory.ReplaceDbContext` giữ nguyên, vì test interceptor vẫn dùng.
4. **F-15:** `Translate(ex, ct)` trả kết quả theo thứ tự:
   - `ex` là `OperationCanceledException` → `null`.
   - Có thể thử lại và `ct.IsCancellationRequested` → `new OperationCanceledException(message, ex, ct)`.
   - Có thể thử lại → `PersistenceUnavailableException` (message như cũ).
   - Còn lại → `null`.

   `EfDecisionAuditStore.TranslateFailuresAsync` thay bằng `ExecuteAsync`. Log Warning như cũ, chỉ khi dịch thành 503.
5. **F-22:** thêm bảng mã mới vào EXPERIMENTAL_DESIGN, theo cấu trúc mục hiện có:
   - UT-15 vào 4.1.
   - Mục con mới "SEC — Bảo mật" (SEC-1…7) và "NG — Expose qua ngrok" (NG-1…5), chép nguyên tiêu chí từ spec 4.3.2 / 4.3.1, cột "Xây dựng" = P5.
   - Sửa mô tả UT-14 theo F-22.
   - **Không stage** file nếu người dùng đang có thay đổi chưa commit trong file đó; ghi vào báo cáo.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `TruncateToMicroseconds_DropsSubMicrosecondTicks_Theory` | Tick lẻ 0..9; offset +07:00 | Bội số 10 tick, offset 0, cùng thời điểm (trừ phần dư) | UT-11 |
| `DeserializeRequest_UnknownProperty_IsIgnored` | `request_payload` có trường lạ ở gốc và trong `customer` | Đọc được, giá trị khác giữ nguyên | UT-02 |
| `DeserializeResponse_MissingRequiredProperty_UsesDefault` | Bỏ một trường `required` khỏi `response_payload` | Đọc được, trường bị thiếu có giá trị mặc định | UT-02 |
| `DeserializeRequest_StrictEnumAndOffset_StillEnforced_Theory` | Enum chữ thường; thời gian thiếu offset | `JsonException` | UT-02 |
| `SerializeRequest_UsesStrictOptions` | Serialize rồi so với `TadssJson.Options` | Byte bằng nhau | UT-02 |
| `Validate_ConnectionString_Theory` | `null`, rỗng, `your-connection-string`, `Port=5432` (thiếu Host), chuỗi test hợp lệ | 4 ca đầu fail; message không chứa giá trị; ca cuối đạt | SEC-4 |
| `Host_PlaceholderConnectionString_FailsOnStart` | Host `Testing`, chuỗi `your-connection-string` | `OptionsValidationException` khi start; message không chứa chuỗi | SEC-4 |
| `Translate_Theory` | (transient Npgsql, token chưa hủy) / (transient, token đã hủy) / (OCE) / (lỗi CHECK 23514) | 503-exception / OCE gắn token, inner là gốc / `null` / `null` | UT-08 |
| `AppSettings_PassAllValidators` (sửa) | `appsettings.json` + chuỗi kết nối test | Xanh | UT-14 |
| Toàn bộ unit + IT | — | Xanh | — |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh, số test ghi vào báo cáo.
- Commit đề xuất: `fix: fail fast on missing connection string and harden audit reads` và `docs: add governance and security catalog ids`.

---

### Task 2: Port và adapter quản trị (đọc hàng chờ, ghi override/outcome)

**Files:**
- Create (`src/TADSS.Application/Abstractions/Persistence/`): `IDecisionGovernanceStore.cs`, `InsertOverrideResult.cs`, `InsertOutcomeResult.cs`, `PendingApprovalsPage.cs`.
- Modify: `IDecisionQueryStore.cs` (thêm method).
- Create (`src/TADSS.Infrastructure/Persistence/`):
  - `EfDecisionGovernanceStore.cs`.
  - `PendingApprovalsQuery.cs` (internal; câu SQL và đọc kết quả).
- Modify:
  - `EfDecisionAuditStore.cs`: hiện thực `GetPendingApprovalsAsync` bằng cách gọi `PendingApprovalsQuery`.
  - `DecisionAuditMapper.cs`: thêm `ToEntity` cho override và outcome.
  - `InfrastructureServiceCollectionExtensions.cs`: `IDecisionGovernanceStore → EfDecisionGovernanceStore`, scoped.
- Modify test double: `tests/TADSS.UnitTests/TestDoubles/FakeDecisionAuditStore.cs`. Hiện thực đủ ba port, có bộ nhớ trong và cờ giả lập "thua tranh chấp".
- Test: `tests/TADSS.IntegrationTests/Persistence/GovernanceStoreTests.cs`, `tests/TADSS.IntegrationTests/Persistence/PendingApprovalsQueryTests.cs`; cập nhật test reflection "store chỉ append-only" để phủ cả `IDecisionGovernanceStore`.

**Interfaces:**
- Produces:
  - `IDecisionGovernanceStore`:
    - `Task<InsertOverrideResult> InsertOverrideAsync(DecisionOverrideRecord record, CancellationToken cancellationToken)`
    - `Task<InsertOutcomeResult> InsertOutcomeAsync(DecisionOutcomeRecord record, CancellationToken cancellationToken)`
  - `sealed record InsertOverrideResult(DecisionOverrideRecord Stored, bool IsExistingWinner)`, `sealed record InsertOutcomeResult(DecisionOutcomeRecord Stored, bool IsExistingWinner)`.
  - `IDecisionQueryStore.GetPendingApprovalsAsync(Guid branchId, int page, int pageSize, DateTimeOffset nowUtc, CancellationToken cancellationToken) : Task<PendingApprovalsPage>`.
  - `sealed record PendingApprovalsPage(IReadOnlyList<DecisionExecutionRecord> Items, int TotalItems)`.
- Consumes: `PersistenceFailureTranslator` (Task 1).

**Yêu cầu:**
1. **Ghi:**
   - Insert một hàng.
   - `PostgresException` `23505` của **đúng** constraint `uq_decision_overrides_decision_id` / `uq_decision_outcomes_decision_id` → đọc lại bản đã có (`AsNoTracking`), trả `IsExistingWinner = true`.
   - Constraint khác (CHECK, FK) → exception gốc lan ra (500).
   - Lỗi có thể thử lại → qua `PersistenceFailureTranslator`.
   - Gỡ entity khỏi change tracker trong `finally`, như `InsertExecutionAsync`.
2. **SQL hàng chờ (F-02).** Viết bằng `FromSql`/`SqlQuery` với `FormattableString`. Không nối chuỗi. Cùng `@nowUtc` cho cả hai câu:

   ```sql
   -- trang
   SELECT c.* FROM tadss.decision_executions c
   WHERE c.role = 'PRIMARY' AND c.branch_id = @branchId AND c.status = 'NEED_APPROVAL'
     AND c.approval_expires_at > @nowUtc
     AND NOT EXISTS (SELECT 1 FROM tadss.decision_executions n
                     WHERE n.reservation_id = c.reservation_id AND n.branch_id = c.branch_id AND n.role = 'PRIMARY'
                       AND (n.created_at, n.decision_id) > (c.created_at, c.decision_id))
     AND NOT EXISTS (SELECT 1 FROM tadss.decision_overrides o WHERE o.decision_id = c.decision_id)
     AND NOT EXISTS (SELECT 1 FROM tadss.decision_outcomes  r WHERE r.decision_id = c.decision_id)
   ORDER BY c.approval_expires_at, c.decision_id
   LIMIT @pageSize OFFSET @offset;
   -- tổng: SELECT count(*) với cùng điều kiện WHERE
   ```

   - `offset = (long)(page − 1) × pageSize`.
   - Hai câu không cần cùng transaction. Ghi chú này vào XML doc.
3. Không lộ `IQueryable`, DbContext hay entity ra khỏi Infrastructure. Không có `UPDATE`/`DELETE` (INV-4).
4. `EfDecisionAuditStore.cs` và `EfDecisionGovernanceStore.cs` mỗi file ≤ 250 dòng.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `InsertOverride_ThenDetail_ReturnsOverride` | Quyết định `NEED_APPROVAL` + override APPROVE | Có hàng; `GetDecisionDetailAsync` trả override đúng từng cột | IT-06 |
| `InsertOverride_Duplicate_ReturnsExistingWinner` | Hai lần insert cùng `decision_id`, payload khác | Lần hai `IsExistingWinner = true`, `Stored` = bản đầu; vẫn 1 hàng | IT-07 |
| `InsertOverride_ConcurrentTwenty_ExactlyOneRow` | 20 scope song song | 1 hàng; 1 kết quả `IsExistingWinner = false`, 19 kết quả `true` cùng `Stored` | IT-07 |
| `InsertOutcome_Duplicate_ReturnsExistingWinner` | Như trên cho outcome | Như trên | IT-10 |
| `InsertOverride_UnknownDecision_PropagatesForeignKeyViolation` | `decision_id` không tồn tại | Exception gốc (không phải 503) | UT-08 |
| `PendingApprovals_Semantics_Theory` | Mỗi ca dựng dữ liệu riêng:<br>(a) bản mới hơn cùng reservation thay bản cũ — mới là `APPROVED` thì không còn trong hàng chờ, mới là `NEED_APPROVAL` thì chỉ bản mới có mặt;<br>(b) khác chi nhánh;<br>(c) `approval_expires_at = nowUtc` và `nowUtc − 1µs`;<br>(d) đã override;<br>(e) đã có outcome;<br>(f) bản ghi `SHADOW`;<br>(g) hai bản cùng `created_at` | Đúng tập kết quả theo B.5; (c) loại cả hai (`>` nghiêm ngặt); (f) bị loại (INV-8); (g) bản có `decision_id` lớn hơn thắng | IT-09 |
| `PendingApprovals_Paging_OrderAndTotal` | 45 bản chờ, `pageSize = 20`, trang 1/2/3/4 | 20/20/5/0 item; `TotalItems = 45` ở mọi trang; thứ tự `approval_expires_at, decision_id` | IT-09 |
| `PendingApprovals_Explain_UsesPartialIndex` | Sinh **≥ 5.000** bản bằng `INSERT … SELECT generate_series` (≥ 50 chi nhánh, ~10% `NEED_APPROVAL`), `ANALYZE`; `EXPLAIN (FORMAT JSON)` câu trang | Kế hoạch có `"Index Name": "idx_de_pending"` | IT-09 |
| `Stores_HaveNoUpdateOrDeleteMembers` (mở rộng) | Reflection ba port | Không có method tên Update/Delete/Remove | UT-14 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh.
- Câu SQL cuối cùng và output `EXPLAIN` (rút gọn) có trong báo cáo.
- Commit đề xuất: `feat: governance store and pending approvals query`.

---

### Task 3: Use case quản trị (override, outcome, hàng chờ)

**Files:**
- Create:
  - `src/TADSS.Application/Exceptions/DecisionStateConflictException.cs`.
  - `src/TADSS.Application/Governance/{IOverrideService,OverrideService,OverrideRequestValidator,GovernanceResponseMapper}.cs`.
  - `src/TADSS.Application/Outcomes/{IOutcomeService,OutcomeService}.cs`.
- Modify:
  - `Common/ContractLimits.cs` (F-17).
  - `Decisions/DepositCalculator.cs` (`FromAmount`).
  - `Decisions/IDecisionQueryService.cs`, `Decisions/DecisionQueryService.cs`: hàng chờ, nhận `TimeProvider`, dùng `GovernanceResponseMapper` thay hai `ToResponse` private.
  - `ApplicationServiceCollectionExtensions.cs`: hai service scoped.
  - `src/TADSS.Runtime.Api/ErrorHandling/ExceptionClassifier.cs`: 409 theo `ErrorCode` của exception.
- Test:
  - `tests/TADSS.UnitTests/Application/Governance/{OverrideServiceTests,OverrideRequestValidatorTests}.cs`.
  - `tests/TADSS.UnitTests/Application/Outcomes/OutcomeServiceTests.cs`.
  - Thêm ca cho `DepositCalculatorTests`, `DecisionQueryServiceTests`, `ExceptionClassifierTests`.

**Interfaces:**
- Produces:
  - `DecisionStateConflictException` (sealed):
    - Thuộc tính `string ErrorCode`, `Guid DecisionId`. Constructor private.
    - Factory `NotPendingApproval(Guid)`, `AlreadyOverridden(Guid)`, `ApprovalExpired(Guid)`, `OutcomeAlreadyRecorded(Guid)`. Mã lấy từ `ErrorCodes` của SDK; message tiếng Việt, chỉ chứa `decisionId`.
  - `IOverrideService.OverrideAsync(Guid decisionId, OverrideRequestDto request, string actorId, string idempotencyKey, CancellationToken cancellationToken) : Task<OverrideResponseDto>`.
  - `IOutcomeService.RecordAsync(Guid decisionId, OutcomeRequestDto request, CancellationToken cancellationToken) : Task<OutcomeResponseDto>`.
  - `IDecisionQueryService.GetPendingApprovalsAsync(Guid branchId, int page, int pageSize, CancellationToken cancellationToken) : Task<PagedResult<PendingApprovalItemDto>>`.
  - `DepositCalculator.FromAmount(decimal totalPrice, decimal amount, string currency) : DepositDto`.
  - `ContractLimits.MaxActorIdLength = 100`, `MinReasonLength = 5`, `MaxReasonLength = 500`, `DefaultPageSize = 20`, `MaxPageSize = 100`.
- Consumes: `IDecisionQueryStore`, `IDecisionGovernanceStore` (Task 2), `AuditTimestamps` (Task 1), `DecisionPayloadSerializer`.

**Yêu cầu:**
1. **Override** theo đúng thứ tự F-03:
   - `OverrideRequestValidator` (thuần) kiểm tra hình thức và F-04, F-17. Lỗi ném `ContractValidationException(VALIDATION_ERROR, message, field)`; `field` là đường dẫn JSON (`deposit.rate`, `reason`…).
   - `actorId` và `idempotencyKey` đến từ controller, đã được kiểm tra độ dài.
   - `overriddenAt = AuditTimestamps.TruncateToMicroseconds(timeProvider.GetUtcNow())`.
   - Dùng **một** `nowUtc` cho cả kiểm tra hạn lẫn `created_at`.
2. **Outcome** theo F-05. `occurredAt` và `reportedAt` cắt µs trước khi so sánh và ghi.
3. **Hàng chờ:**
   - Kiểm tra `page ≥ 1`, `1 ≤ pageSize ≤ MaxPageSize`; sai → `ContractValidationException(VALIDATION_ERROR, …, "page" | "pageSize")`.
   - Mỗi item dựng từ cột của bản ghi (`decisionId`, `reservationId`, `branchId`, `reasonCode`, `fallbackUsed`, `approvalExpiresAt`, `createdAt`) và từ `request_payload` đọc nới lỏng (`totalPrice`, `currency`, `customer.isGuest`, `customer.tier`, `earliestStart` = `start` sớm nhất theo thời điểm, giữ offset của item đó).
   - Kết quả bọc bằng `PagedResult<T>.Create`.
4. `ExceptionClassifier`: `DecisionStateConflictException` → `ErrorClassification(409, ex.ErrorCode, "Decision State Conflict", ExposeMessage: true, HasRetryAfter: false)`. Không đổi nhánh nào khác.
5. Log Information khi override hoặc outcome thành công: `decisionId`, `action`/`outcome`, cờ "replay". **Không** log `reason`.
6. Mỗi file ≤ 250 dòng. Nếu `OverrideService` vượt trần, tách bước so khớp payload thành `OverridePayloadComparer` (internal) trong cùng thư mục.

**Test cases** (test double `FakeDecisionAuditStore`, `FakeTimeProvider`):

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Override_ApproveWithRate_PersistsConvertedDeposit` | `rate 0.3`, VND, total 228000 | Record có rate 0.3, amount 68400; response khớp | UT-15 |
| `Override_ApproveWithAmount_DerivesRate_Theory` | amount 50000/total 228000; amount = total; total 0 + amount 0 | rate 0.2193 / 1 / 0 | UT-15 |
| `Override_Reject_HasNoDeposit` | REJECT | Record không có rate/amount; response `deposit = null` | UT-15 |
| `Validate_InvalidRequest_Throws400_Theory` | APPROVE thiếu deposit; có cả rate lẫn amount; REJECT kèm deposit; rate 1.00001, -0.1, 0.12345; amount > total; amount VND có phần lẻ; reason 4 code point, 501 code point, chỉ khoảng trắng; reason 5 emoji (10 đơn vị UTF-16) hợp lệ | `ContractValidationException` `VALIDATION_ERROR` đúng `field`; ca emoji đạt | UT-15 |
| `Override_CheckOrder_Theory` | Không có; `SHADOW`; đã override (cùng key + cùng payload / cùng key khác payload / khác key); `APPROVED`; đã có outcome; hết hạn đúng bằng `now` | 404; 404; 200 replay / `IdempotencyConflictException` / `ALREADY_OVERRIDDEN`; `NOT_PENDING_APPROVAL`; `NOT_PENDING_APPROVAL`; `APPROVAL_EXPIRED` | UT-15 |
| `Override_LostRace_TreatedAsExisting_Theory` | Store trả `IsExistingWinner = true` (cùng key + payload / khác key) | 200 với bản thắng / `ALREADY_OVERRIDDEN` | UT-15 |
| `Outcome_Record_Replay_Conflict_Theory` | Mới; trùng (khác tick dưới µs); khác `outcome`; khác `occurredAt`; thua tranh chấp | 200; 200 cùng `reportedAt`; 409 `OUTCOME_ALREADY_RECORDED` ×2; xử lý theo payload của bản thắng | UT-15 |
| `Outcome_ShadowOrMissing_IsNotFound_Theory` | — | `DecisionNotFoundException` | UT-15 |
| `PendingApprovals_ComposesItemsFromRecordAndPayload` | 2 bản ghi, item có offset +07:00 | Đủ 12 trường; `earliestStart` đúng item sớm nhất | UT-15 |
| `PendingApprovals_InvalidPaging_Throws400_Theory` | page 0; pageSize 0, 101 | `VALIDATION_ERROR` đúng field | UT-15 |
| `FromAmount_Theory` | Làm tròn `AwayFromZero` ở chữ số thứ 4; tham số sai | Đúng giá trị; `ArgumentOutOfRangeException` | UT-12 |
| `Classify_DecisionStateConflict_Is409WithItsCode_Theory` | 4 factory | 409, `code` = mã của factory, `ExposeMessage = true` | UT-08 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh.
- Commit đề xuất: `feat: override, outcome and pending approval use cases`.

---

### Task 4: Endpoint quản trị, schema và OpenAPI

**Files:**
- Create:
  - `src/TADSS.Runtime.Api/Controllers/DecisionApprovalsController.cs`: `GET pending-approvals`, `POST {decisionId}/override`.
  - `src/TADSS.Runtime.Api/Controllers/DecisionOutcomesController.cs`: `POST {decisionId}/outcome`.
  - `src/TADSS.Runtime.Api/Common/RequestHeaders.cs` (internal static).
  - `docs/contracts/schemas/v2/{pending-approvals,override-request,override-response,outcome-request,outcome-response}.schema.json`.
- Modify:
  - `Common/HttpHeaderNames.cs`: `ActorId = "X-Actor-Id"`.
  - `Controllers/DecisionsController.cs`: đọc idempotency key qua `RequestHeaders`, hành vi giữ nguyên.
  - `OpenApi/RequiredHeaderOperationFilter.cs`: `X-Idempotency-Key` cho override/outcome, `X-Actor-Id` cho override.
  - `Composition/PresentationServiceCollectionExtensions.cs`: mô tả Swagger liệt kê đủ 5 endpoint.
  - `docs/contracts/openapi-v2.yaml`: sinh lại.
  - `docs/contracts/INTEGRATION_SPEC.md` §3.1, §3.2, §3.5–3.8, §4.3: theo Phụ lục B.
  - `docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md`: B.5 (SQL mới, F-02), trạng thái sau 5.3, INV-5 (F-06).
  - Test helper: `tests/TADSS.UnitTests/TestData/ContractSchemas.cs`, `tests/TADSS.IntegrationTests/Infrastructure/ResponseSchemaAssert.cs` (schema mới).
  - `tests/TADSS.UnitTests/Api/Composition/HttpPipelineTests.cs`: test route được thay thế.
  - Test double: tạo `tests/TADSS.UnitTests/TestDoubles/{FakeOverrideService,FakeOutcomeService}.cs`; thêm `GetPendingApprovalsAsync` vào `FakeDecisionQueryService`; `DecisionsApiFactory` thay hai service mới bằng double và `ResetDoubles` reset cả bốn.
- Test:
  - `tests/TADSS.IntegrationTests/{OverrideEndpointTests,OutcomeEndpointTests,PendingApprovalsEndpointTests}.cs`.
  - `tests/TADSS.UnitTests/Api/Controllers/{DecisionApprovalsControllerTests,DecisionOutcomesControllerTests}.cs`: header và chuyển tiếp, có double.
  - Ca âm cho schema request mới.

**Interfaces:**
- Produces:
  - Route:
    - `GET api/v2/decisions/pending-approvals?branchId&page&pageSize`
    - `POST api/v2/decisions/{decisionId:guid}/override`
    - `POST api/v2/decisions/{decisionId:guid}/outcome`
  - `RequestHeaders.RequireIdempotencyKey(HttpRequest) : string` và `RequestHeaders.RequireActorId(HttpRequest) : string`. Thiếu hoặc trắng → `MISSING_HEADER`; quá dài → `VALIDATION_ERROR`; độ dài actor đếm theo code point.
  - `DecisionsApiFactory` có thêm double `FakeOverrideService`, `FakeOutcomeService`.
- Consumes: `IOverrideService`, `IOutcomeService`, `IDecisionQueryService.GetPendingApprovalsAsync` (Task 3).

**Yêu cầu:**
1. Controller chỉ đọc header, gọi service và trả `Ok(...)`; không có logic nghiệp vụ.
2. Query `pending-approvals`:
   - `branchId` là `Guid?`. Thiếu → `ContractValidationException(VALIDATION_ERROR, …, "branchId")`.
   - `page`, `pageSize` là `int?`, mặc định theo `ContractLimits`.
   - Giá trị không parse được → lỗi model binding → `InvalidModelStateTranslator` → 400 `VALIDATION_ERROR`.
3. `[ProducesResponseType]` đầy đủ cho mỗi action:
   - Mọi action: 200, 400, 503, 500.
   - Override và outcome: thêm 404, 409, 415.
   - (401/413/429 thêm bằng filter ở các task sau.)
4. **Schema viết tay:**
   - Mỗi file tự đủ (`$defs` nội bộ), `additionalProperties: false`, kiểu theo DTO SDK.
   - `override-request`: `if action = APPROVE then deposit bắt buộc với oneOf {rate} | {amount}; else deposit không có`.
   - Ràng buộc độ dài trong schema (`minLength`/`maxLength`) đếm theo ký tự (JSON Schema đếm theo code point) và phải khớp F-17.
5. **INTEGRATION_SPEC** chỉ ghi điều đã hiện thực: phạm vi §3.1, giới hạn F-17, quy tắc F-03/F-04/F-05, `SHADOW` → 404, câu về tranh chấp (F-06). Mã 409 ở §3.8 bỏ ghi chú "chưa hiện thực".

**Test cases** (IT trên host thật, đồng hồ giả):

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Override_Approve_ShowsInDetailAndLeavesQueue_Theory` | Tạo `NEED_APPROVAL` (`DecisionRequests.HighValue`), override APPROVE theo `rate` và theo `amount` | 200 hợp lệ schema; `GET` chi tiết có override (CT-01); hàng chờ không còn quyết định đó; 1 hàng `decision_overrides` | IT-06 |
| `Override_Reject_Succeeds` | REJECT | 200, `deposit = null` | IT-06 |
| `Override_TwentyConcurrentDistinctKeys_OneWins` | 20 request song song, key/actor khác nhau | Đúng 1 × 200, 19 × 409 `ALREADY_OVERRIDDEN`; 1 hàng | IT-07 |
| `Override_TwentyConcurrentSameKeyAndPayload_AllSucceedIdentically` | 20 request song song giống hệt nhau | 20 × 200, body giống nhau; 1 hàng | IT-07 |
| `Override_NotPendingOrExpired_Returns409_Theory` | Quyết định `APPROVED`; `Clock` tiến quá `approvalExpiresAt`; đã có outcome | `NOT_PENDING_APPROVAL` / `APPROVAL_EXPIRED` / `NOT_PENDING_APPROVAL`; problem+json có `code`, `traceId` | IT-08 |
| `Override_MissingActorId_Returns400MissingHeader` | Không có `X-Actor-Id` | 400 `MISSING_HEADER` | IT-11 |
| `Override_UnknownDecision_Returns404` | GUID ngẫu nhiên | 404 `DECISION_NOT_FOUND` | IT-08 |
| `PendingApprovals_Endpoint_PagesAndValidates_Theory` | 3 quyết định chờ ở chi nhánh A, 1 ở B; `pageSize = 2`; thiếu `branchId`; `pageSize = 101`; `page = abc` | Envelope đúng, hợp lệ schema; 400 `VALIDATION_ERROR` ở 3 ca sai | IT-09 |
| `Outcome_RecordReplayConflict` | Ghi; gửi lại y hệt; gửi khác | 200; 200 cùng `reportedAt`; 409 `OUTCOME_ALREADY_RECORDED`; chi tiết hiện outcome | IT-10 |
| `Routes_OnlyV2DecisionEndpointsExist` (thay thế) | Liệt kê route | Đúng 5 route v2 | UT-09 |
| `OverrideRequest_SchemaNegativeCases_Theory` | Các ca sai của F-04/F-17 ở mức schema | Schema từ chối | CT-01 |
| `OpenApi_GeneratedDocument_MatchesCommittedFile` | — | Xanh sau khi sinh lại | CT-01 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh.
- Diff `openapi-v2.yaml` có trong báo cáo.
- Commit đề xuất: `feat: governance endpoints with v2 schemas`.

---

### Task 5: Bộ ghi problem+json và xác thực `X-Api-Key`

**Files:**
- Create:
  - `src/TADSS.Runtime.Api/ErrorHandling/{ProblemResponse,ProblemResponseWriter}.cs`.
  - `src/TADSS.Runtime.Api/Options/{SecurityOptions,ApiKeyClientOptions,SecurityOptionsValidator}.cs`.
  - `src/TADSS.Runtime.Api/Security/{ApiKeyVerifier,ApiClientIdentity,SecurityPaths}.cs`.
  - `src/TADSS.Runtime.Api/Middlewares/ApiKeyAuthenticationMiddleware.cs`.
  - `src/TADSS.Runtime.Api/Composition/SecurityServiceCollectionExtensions.cs`.
  - `src/TADSS.Runtime.Api/OpenApi/SecurityResponsesOperationFilter.cs`.
- Modify:
  - `Middlewares/GlobalExceptionHandler.cs`: dựng body qua writer. Body và header **không đổi**, test hiện có là lưới an toàn.
  - `Common/HttpHeaderNames.cs`: `ApiKey = "X-Api-Key"`.
  - `Composition/PresentationServiceCollectionExtensions.cs`: gọi `AddTadssSecurity`; Swagger có security scheme `ApiKey` (header `X-Api-Key`) và requirement cho mọi operation.
  - `Composition/RequestPipelineExtensions.cs`: chèn middleware xác thực theo F-09.
  - `src/TADSS.Runtime.Api/appsettings.json`: section `Tadss:Security` có `"ApiKeys": []` kèm comment (giá trị thật qua biến môi trường hoặc file dev).
  - `docs/contracts/openapi-v2.yaml`: sinh lại.
  - `docs/contracts/INTEGRATION_SPEC.md` §3.2, §3.8: `X-Api-Key` đã hiện thực, 401 `UNAUTHORIZED` kèm `WWW-Authenticate`.
- Test helper:
  - `tests/TADSS.UnitTests/TestData/TestApiKeys.cs`: khóa giả cố định, mã băm, `ClientId`; link vào IT bằng `Compile Include … Link`.
  - `DecisionsApiFactory` và `TadssWebApplicationFactory`: cấu hình `Tadss:Security:ApiKeys:0:*`, override `ConfigureClient` để mọi `HttpClient` mặc định gửi `X-Api-Key`. **Test hiện có không phải sửa từng chỗ.**
- Test: `tests/TADSS.UnitTests/Api/Security/{ApiKeyVerifierTests,SecurityOptionsValidatorTests,ApiKeyAuthenticationTests,ProblemResponseWriterTests}.cs`, `tests/TADSS.IntegrationTests/Security/ApiKeyIntegrationTests.cs`.

**Interfaces:**
- Produces:
  - `sealed record ProblemResponse(int StatusCode, string Code, string Title, string Detail, int? RetryAfterSeconds = null, IReadOnlyDictionary<string, string[]>? Errors = null)`.
  - `static class ProblemResponseWriter`: `Task WriteAsync(HttpContext context, ProblemResponse problem, CancellationToken cancellationToken)`.
    - Đặt status, `Content-Type: application/problem+json`.
    - Body gồm `type` (`https://tadss.smashcourt.vn/errors/{code-kebab}`), `title`, `status`, `detail`, `instance` = path, `code`, `traceId` = `TraceIdentifier`.
    - Khi có `RetryAfterSeconds`: `retryAfterSeconds` trong body và header `Retry-After`. Khi có `Errors`: `errors`.
  - `SecurityOptions` (`SectionName = "Tadss:Security"`): `List<ApiKeyClientOptions> ApiKeys`. Task 6/7 thêm thuộc tính.
  - `ApiKeyClientOptions { string ClientId; string KeySha256; }`.
  - `SecurityOptionsValidator(IHostEnvironment environment) : IValidateOptions<SecurityOptions>`. Có hằng `public const string LocalDevelopmentKeySha256` (mã băm của `tadss-local-dev-key`).
  - `sealed class ApiKeyVerifier` (singleton, đọc `IOptions<SecurityOptions>`): `bool TryAuthenticate(string? presentedKey, out string clientId)`.
  - `static class ApiClientIdentity`: `void Set(HttpContext, string clientId)`, `string? Get(HttpContext)`.
  - `static class SecurityPaths`: `bool IsHealth(PathString)`, `bool IsSwagger(PathString)`.
  - `AddTadssSecurity(this IServiceCollection services, IConfiguration configuration) : IServiceCollection`.

**Yêu cầu:**
1. **Validator** (ClientId so sánh không phân biệt hoa thường):
   - Mỗi `ClientId` khớp `^[A-Za-z0-9._-]{1,64}$` và là duy nhất.
   - Mỗi `KeySha256` khớp `^[0-9a-fA-F]{64}$` và là duy nhất.
   - Môi trường khác `Development`/`Testing`: `ApiKeys` rỗng → fail; có `LocalDevelopmentKeySha256` → fail.
   - Message chỉ nêu chỉ số phần tử và tên trường, **không** chứa mã băm.
2. **Verifier:**
   - Chuẩn hóa mã băm cấu hình về byte một lần.
   - Khóa nhận được: null, rỗng hoặc dài hơn 256 ký tự → `false`. Ngược lại băm SHA-256 (UTF-8) rồi `FixedTimeEquals` với **mọi** mục; giữ kết quả khớp, không `break`.
3. **Middleware:**
   - `SecurityPaths.IsHealth` → bỏ qua.
   - Ở `Development` và `IsSwagger` → bỏ qua.
   - Không có hoặc sai khóa → `WWW-Authenticate: ApiKey header="X-Api-Key"` và writer với 401 `UNAUTHORIZED`, detail chung tiếng Việt. Không gọi `next`.
   - Đúng khóa → `ApiClientIdentity.Set`, `LogContext.PushProperty("ClientId", …)`, `Activity.Current?.SetTag("tadss.client_id", …)`, gọi `next`.
   - **Không bao giờ** log hay đưa vào problem giá trị khóa hoặc mã băm của khóa nhận được.
4. Khởi động ở `Development`/`Testing` với `ApiKeys` rỗng: một log Warning "mọi request /api/* sẽ bị 401".
5. `GlobalExceptionHandler` sau refactor ≤ 70 dòng. Toàn bộ test `GlobalExceptionHandlerTests`, `ExceptionClassifierTests`, `HttpErrorMappingTests` xanh, không sửa assertion.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `TryAuthenticate_Theory` | Đúng khóa (mã băm cấu hình viết hoa); sai một ký tự; rỗng; `null`; 257 ký tự; hai client | Đúng `clientId` / `false` ×4 / đúng client tương ứng | SEC-4 |
| `Validate_Theory` | `Staging` rỗng; `Staging` có khóa local; `Development` rỗng; `ClientId` trùng; hex 63 ký tự; ký tự lạ trong `ClientId` | fail / fail / đạt / fail / fail / fail; mọi message không chứa mã băm | SEC-4 |
| `Host_StagingWithoutApiKeys_FailsOnStart` | Host `Staging`, chuỗi kết nối test, `ApiKeys` rỗng | `OptionsValidationException` khi start | SEC-4 |
| `Request_WithoutOrWithWrongKey_Is401Problem_Theory` | Không header; khóa sai; khóa đúng của client khác bị xóa khỏi cấu hình | 401, `code = UNAUTHORIZED`, `traceId` = `X-Correlation-ID` trả về, có `WWW-Authenticate`; service double không được gọi | IT-11 |
| `Health_IsExemptFromApiKey_Theory` | `/health/live`, `/health/ready` không header (double health) | Không phải 401 | SEC-4 |
| `Swagger_ExemptOnlyInDevelopment_Theory` | `Development` / `Testing`, không header | 200 / 404 | SEC-4 |
| `Request_WithKey_LogsClientIdButNeverKey` | Bắt log bằng `CapturingLogger` hoặc sink Serilog trong bộ nhớ; gửi khóa đúng và khóa sai | Log có `ClientId`; không dòng log hoặc property nào chứa chuỗi khóa hay mã băm | SEC-7 |
| `WriteAsync_ShapesProblem_Theory` | Có và không có `RetryAfterSeconds`, `Errors` | Đúng tập trường; `Retry-After` chỉ khi có | UT-08 |
| `ApiKeyIntegration_MissingAndInvalidKey_Return401` | Host thật (Testcontainers), client bỏ header mặc định | 401 cả hai ca; có khóa → 200 | IT-11 |
| Toàn bộ unit + IT | Client mặc định gửi khóa | Xanh, không sửa test cũ | — |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh.
- Diff OpenAPI (security scheme, 401) có trong báo cáo.
- Commit đề xuất: `refactor: single problem response writer` và `feat: api key authentication`.

---

### Task 6: Security headers, giới hạn kích thước, forwarded headers, thứ tự pipeline

**Files:**
- Modify (SDK):
  - `src/Shared.DecisionContractSDK/Common/ErrorCodes.cs`: `RateLimited`, `PayloadTooLarge`, kèm XML doc.
  - `docs/contracts/baseline_public_api.txt`: đúng 2 dòng thêm (F-12, D-06). Lý do ghi trong thân commit.
- Create: `src/TADSS.Runtime.Api/Middlewares/{SecurityHeadersMiddleware,RequestSizeLimitMiddleware}.cs`.
- Modify:
  - `Options/SecurityOptions.cs`: `MaxRequestBodyBytes` (mặc định 65536), `TrustForwardedHeaders` (mặc định `false`); validator thêm khoảng `1024..1048576`.
  - `Composition/SecurityServiceCollectionExtensions.cs`:
    - `ForwardedHeadersOptions` (F-10).
    - `AddHsts` mặc định.
    - `services.Configure<KestrelServerOptions>(o => o.AddServerHeader = false)`.
  - `Composition/RequestPipelineExtensions.cs`: thứ tự F-09 đầy đủ (trừ rate limiter của Task 7), `UseRouting()` tường minh, bỏ `UseAuthorization()`.
  - `ErrorHandling/ExceptionClassifier.cs`: `BadHttpRequestException { StatusCode: 413 }` → 413 `PAYLOAD_TOO_LARGE`, `ExposeMessage: false`.
  - `ErrorHandling/InvalidModelStateTranslator.cs` (F-23).
  - `Middlewares/GlobalExceptionHandler.cs`: câu chung cho 413.
  - `OpenApi/SecurityResponsesOperationFilter.cs`: thêm 413.
  - `OpenApi/ProblemDetailsSchemaFilter.cs`: mô tả `retryAfterSeconds` có cả 429.
  - `appsettings.json`.
  - `docs/contracts/openapi-v2.yaml`: sinh lại.
  - `docs/contracts/INTEGRATION_SPEC.md` §3.8: dòng 413, 429 (429 hiện thực ở Task 7, ghi một lần ở đây), header bảo mật; §4.3: BE áp cọc 100% cho cả 413/429.
- Test: `tests/TADSS.UnitTests/Api/Security/{SecurityHeadersTests,RequestSizeLimitTests,ForwardedHeadersTests,SecurityPipelineOrderTests}.cs`; thêm ca cho `ExceptionClassifierTests` và `HttpErrorMappingTests`.

**Interfaces:**
- Produces: `ErrorCodes.RateLimited`, `ErrorCodes.PayloadTooLarge`; `SecurityOptions.MaxRequestBodyBytes : long`, `SecurityOptions.TrustForwardedHeaders : bool`.

**Yêu cầu:**
1. **SecurityHeadersMiddleware:**
   - Đăng ký `Response.OnStarting`: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`; thêm `Cache-Control: no-store` khi path bắt đầu `/api/`.
   - Header phải có cả trên phản hồi do exception handler ghi, vì callback `OnStarting` vẫn còn sau `Response.Clear()`. Test chứng minh điều này.
   - `UseHsts()` đặt ngay sau middleware này, chỉ ngoài `Development`/`Testing`.
2. **RequestSizeLimitMiddleware:**
   - `Content-Length > MaxRequestBodyBytes` → writer 413 `PAYLOAD_TOO_LARGE`, không gọi `next`.
   - Ngược lại, nếu có `IHttpMaxRequestBodySizeFeature` và `!IsReadOnly` → đặt `MaxRequestBodySize`. Body chunked vượt trần thì Kestrel ném `BadHttpRequestException(413)`, classifier map sang 413.
   - Request không có body không bị ảnh hưởng.
3. **F-23:** `InvalidModelStateTranslator` không dùng `ModelError.Exception.Message`.
   - Có trường: `"Giá trị của trường '{field}' không hợp lệ."`.
   - Không xác định được trường: `"Request body không phải JSON hợp lệ theo contract."`.
   - Giữ `errors[field]`, tối đa 3 lỗi.
4. Pipeline đúng F-09 (rate limiter là bước 10, thêm ở Task 7). `Program.cs` ≤ 40 dòng.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `SecurityHeaders_PresentOnAllResponseKinds_Theory` | 200; 401; 404 `DECISION_NOT_FOUND`; 415; 500 (double ném); 413 | Đủ 3 header; `Cache-Control: no-store` ở `/api/*` | SEC-3 |
| `Hsts_OnlyOutsideDevelopmentAndTesting_Theory` | `Staging` + `TrustForwardedHeaders` + `X-Forwarded-Proto: https` + `Host: tadss.example` (HSTS bỏ qua `localhost`); `Testing`; `Development` | Có `Strict-Transport-Security` / không / không | SEC-3 |
| `Kestrel_ServerHeaderDisabled` | Resolve `IOptions<KestrelServerOptions>` | `AddServerHeader = false` | SEC-3 |
| `RequestSize_ContentLengthOverLimit_Is413BeforeAuth` | Body 64 KB + 1, không có khóa | 413 `PAYLOAD_TOO_LARGE`, có `traceId`; service double không được gọi | SEC-2 |
| `RequestSize_AtLimit_IsProcessed` | Body đúng 65536 byte (JSON hợp lệ, đệm bằng khoảng trắng) | Đi tới service double | SEC-2 |
| `Classify_BadHttpRequest413_IsPayloadTooLarge` | `BadHttpRequestException` với status 413 | 413, `ExposeMessage = false` | UT-08 |
| `ForwardedHeaders_Theory` | Bật / tắt, gửi `X-Forwarded-Proto: https` | `Request.IsHttps` (quan sát qua HSTS) đúng / sai | SEC-3 |
| `InvalidBody_DetailHasNoFrameworkInternals_Theory` | Trường lạ; sai kiểu; JSON hỏng; enum số | 400 `VALIDATION_ERROR`; `detail` và `errors` không chứa `Shared.DecisionContractSDK`, `System.`, `LineNumber`, `BytePositionInLine`, `Path:` | SEC-3 |
| `PipelineOrder_ObservableBehaviour_Theory` (UT-14) | (a) 413 không có khóa → 413, không phải 401; (b) 401 có correlation id và security headers; (c) 500 có security headers; (d) Swagger ở Development không cần khóa; (e) health không cần khóa | Đúng từng ca | UT-14 |
| `PublicApiSurface_MatchesBaseline` | Baseline mới | Xanh | CT-02 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh.
- Diff baseline đúng 2 dòng; diff OpenAPI có trong báo cáo.
- Commit đề xuất: `feat(contract): add RATE_LIMITED and PAYLOAD_TOO_LARGE error codes` và `feat: security headers, request size limit and pipeline order`.

---

### Task 7: Rate limiting theo client (SEC-1)

**Files:**
- Create: `src/TADSS.Runtime.Api/Options/RateLimitingOptions.cs`.
- Modify:
  - `Options/SecurityOptions.cs`: `RateLimiting`.
  - `Options/SecurityOptionsValidator.cs`.
  - `Composition/SecurityServiceCollectionExtensions.cs`: `AddRateLimiter`.
  - `Composition/RequestPipelineExtensions.cs`: `UseRateLimiter()` ở bước 10.
  - `OpenApi/SecurityResponsesOperationFilter.cs`: 429.
  - `appsettings.json`.
  - `docs/contracts/openapi-v2.yaml`: sinh lại.
- Test: `tests/TADSS.UnitTests/Api/Security/RateLimitingTests.cs`.

**Interfaces:**
- Produces: `RateLimitingOptions { int PermitLimit = 100; int TokensPerPeriod = 100; int ReplenishmentPeriodSeconds = 1; }`, không có `SectionName` riêng (thuộc `SecurityOptions.RateLimiting`).

**Yêu cầu:**
1. `GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>`:
   - Health → `RateLimitPartition.GetNoLimiter`.
   - Còn lại → token bucket theo `ApiClientIdentity.Get(ctx) ?? "anonymous"`, với `TokenLimit = PermitLimit`, `TokensPerPeriod`, `ReplenishmentPeriod`, `QueueLimit = 0` (hằng, theo spec), `AutoReplenishment = true`.
2. `OnRejected`:
   - `Retry-After` = trần số giây của metadata `MetadataName.RetryAfter` nếu có, ngược lại `ReplenishmentPeriodSeconds`; tối thiểu 1.
   - Writer 429 `RATE_LIMITED` có `retryAfterSeconds`.
3. **Validator:** `1 ≤ PermitLimit ≤ 100000`; `1 ≤ TokensPerPeriod ≤ PermitLimit`; `1 ≤ ReplenishmentPeriodSeconds ≤ 3600`.
4. **Ghi chú vận hành vào README** (Task 11 gom): giới hạn tính theo instance; k6 chỉnh qua biến môi trường `Tadss__Security__RateLimiting__*`.

**Test cases** (host in-process, `PermitLimit = 2`, `TokensPerPeriod = 2`, `ReplenishmentPeriodSeconds = 3600`):

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `ExceedingLimit_Returns429Problem` | 3 request cùng client | 200, 200, 429 problem+json `code = RATE_LIMITED`, `Retry-After` ≥ 1, `retryAfterSeconds`, `traceId`, security headers | SEC-1 |
| `Clients_AreIsolated` | Client A hết quota; client B gửi | B vẫn 200 | SEC-1 |
| `Health_IsNeverLimited` | 10 lần `/health/live` | Không có 429 | SEC-1 |
| `Unauthenticated_Gets401Not429` | 5 request không có khóa | Đều 401 (xác thực đứng trước rate limiter) | UT-14 |
| `Validate_RateLimiting_Theory` | Biên của 3 tham số | Đạt / fail đúng biên | SEC-1 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh. IT dùng cấu hình mặc định (100/giây) nên không bị 429.
- Commit đề xuất: `feat: per-client rate limiting`.

---

### Task 8: Health live/ready (SEC-5)

**Files:**
- Create:
  - `src/TADSS.Infrastructure/Caching/RedisHealthCheck.cs` (internal sealed).
  - `src/TADSS.Runtime.Api/Composition/EndpointMappingExtensions.cs`: `MapTadssEndpoints`, gồm controllers và hai health endpoint.
- Modify:
  - `InfrastructureServiceCollectionExtensions.cs`: đăng ký Redis check khi `Provider = Redis`.
  - `Program.cs`: gọi `MapTadssEndpoints`; gỡ `/health` (F-16).
- Test: `tests/TADSS.IntegrationTests/Security/HealthEndpointTests.cs`, `tests/TADSS.UnitTests/Infrastructure/Caching/RedisHealthCheckTests.cs`.

**Interfaces:**
- Produces: health check `"redis"` (tag `ready`, `failureStatus: Degraded`, timeout = `CacheOptions.RedisConnectTimeoutMs`); `MapTadssEndpoints(this WebApplication app) : WebApplication`.

**Yêu cầu:**
1. `RedisHealthCheck`:
   - Lấy multiplexer qua `IServiceProvider` (có thể không tạo được). Không có multiplexer hoặc `PING` lỗi/timeout → `Degraded`.
   - Description của kết quả **không** chứa chuỗi kết nối hay message của exception; chỉ ghi tên loại exception vào log.
2. Response giữ writer mặc định (chỉ chữ trạng thái). Không thêm writer in chi tiết.
3. `/health/live` không check nào. `/health/ready` lọc theo tag `ready`; mã status mặc định là Healthy/Degraded 200, Unhealthy 503.

**Test cases** (Toxiproxy):

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Ready_AllHealthy_Is200Healthy` | Bình thường | 200, body `Healthy` | SEC-5 |
| `Ready_PostgresCut_Is503_LiveStays200` | Ngắt proxy `postgres` | ready 503 `Unhealthy`; live 200 | SEC-5 |
| `Ready_RedisCut_Is200Degraded` | Ngắt proxy `redis` | 200, body `Degraded` | SEC-5 |
| `HealthBodies_NeverLeakInfrastructureDetails_Theory` | Ba ca trên | Body không chứa `Host=`, `password`, tên exception | SEC-5 |
| `AggregateHealthEndpoint_IsRemoved` | `GET /health` | 404 | SEC-5 |
| `CheckHealth_NoMultiplexer_IsDegraded` | Unit, không có multiplexer | `Degraded`, description không chứa dữ liệu kết nối | SEC-5 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh.
- Commit đề xuất: `feat: readiness with degraded redis and leak-free health output`.

---

### Task 9: Container cứng và chuỗi cung ứng (SEC-6, SEC-7)

**Files:**
- Create:
  - `Directory.Build.props` (`RestorePackagesWithLockFile = true`).
  - `packages.lock.json` của mọi project trong `TADSS.slnx`.
  - `.github/dependabot.yml`.
  - `tools/ci/check-vulnerable-packages.sh`.
- Modify:
  - `Dockerfile`:
    - Copy lock files cùng csproj.
    - `dotnet restore --locked-mode`.
    - Stage runtime: `apk add` trước, rồi `USER $APP_UID`.
  - `.dockerignore`: thêm `.env`, `.env.*`, `**/appsettings.Development.json`, `**/appsettings.*.local.json`, `**/*.pfx`.
  - `.github/workflows/ci.yml`:
    - `dotnet restore --locked-mode`.
    - Bước kiểm package có lỗ hổng.
    - Build image với `load: true`, rồi kiểm `Config.User`.
- Test: `tests/TADSS.UnitTests/Architecture/ContainerHardeningTests.cs` (đọc file, không cần Docker).

**Yêu cầu:**
1. Lock file sinh bằng `dotnet restore --force-evaluate`. Diff chỉ gồm file lock mới. Mọi project restore được ở `--locked-mode` trên Windows và Linux. Lock file không chứa RID riêng; nếu có, ghi sai lệch.
2. Script lỗ hổng:
   - Chạy `dotnet list TADSS.slnx package --vulnerable --include-transitive`.
   - In nguyên output.
   - Exit 1 khi có dòng mức `High` hoặc `Critical`; exit 0 khi không có. Lỗi của chính lệnh (ví dụ mất mạng) cũng exit khác 0.
3. Dependabot: ba hệ sinh thái, `schedule.interval: weekly`, `target-branch: develop`, `open-pull-requests-limit: 5`.
4. Image chạy được với user non-root: `docker run` image + `ASPNETCORE_ENVIRONMENT=Testing` + chuỗi kết nối trỏ cổng không có dịch vụ + một khóa test → `/health/live` 200. Kiểm thủ công một lần, ghi vào báo cáo.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Dockerfile_FinalStage_RunsAsNonRootOnAspNetImage` | Phân tích `Dockerfile` | Stage cuối `FROM mcr.microsoft.com/dotnet/aspnet:`; có `USER $APP_UID` (hoặc UID ≠ 0) sau `apk add` và trước `ENTRYPOINT` | SEC-6 |
| `DockerIgnore_ExcludesSecretFiles_Theory` | `.dockerignore` | Có đủ mẫu ở mục Modify | SEC-6 |
| `AllProjects_HaveLockFiles` | Mọi csproj trong slnx | Có `packages.lock.json` cạnh csproj | SEC-7 |
| CI: kiểm `Config.User` | `docker inspect` | Không rỗng, không phải `root`/`0` | SEC-6 |
| CI: quét lỗ hổng | Script | Xanh với phụ thuộc hiện tại | SEC-7 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh.
- `docker build` thành công (xóa image sau đó); kiểm thủ công ở yêu cầu 4 đạt.
- Commit đề xuất: `build: lock files, vulnerability scan and dependabot` và `build: run container as non-root`.

---

### Task 10: Compose tham số hóa, expose qua ngrok, database hai chế độ (T5.30)

**Files:**
- Modify:
  - `docker-compose.yml`:
    - Cổng `${TADSS_PORT:-8081}`, `${TADSS_PG_PORT:-5433}`, `${TADSS_REDIS_PORT:-6380}`, `${TADSS_TOXIPROXY_PORT:-8474}`, `${TADSS_GRAFANA_PORT:-3001}`.
    - Image `${TADSS_POSTGRES_IMAGE:-postgres:16.15-alpine}`, `${TADSS_REDIS_IMAGE:-redis:7.4-alpine}`, `${TADSS_TOXIPROXY_IMAGE:-ghcr.io/shopify/toxiproxy:2.12.0}`, `${TADSS_GRAFANA_IMAGE:-grafana/otel-lgtm:0.34.0}`.
    - `tadss-runtime`: API key E-LOCAL (F-18: `Tadss__Security__ApiKeys__0__ClientId: local`, `__KeySha256: ${TADSS_API_KEY_SHA256:-<LocalDevelopmentKeySha256>}`), `healthcheck` bằng `wget -qO- http://127.0.0.1:8080/health/live`.
  - `.env.example`: viết lại. Đủ biến của spec 4.3.1 với giá trị rỗng (`NGROK_AUTHTOKEN`, `NGROK_DOMAIN`, `TADSS_DB_CONNECTION`, `TADSS_API_KEY_SHA256`), thêm `TADSS_REDIS_CONNECTION` (chế độ B, tùy chọn) và các biến cổng/image dạng comment kèm mặc định. Thay khối comment "compose chưa đọc các biến này" hiện có.
  - `.github/workflows/ci.yml`: job `expose-check` (`needs: build-and-test`, chạy script).
- Create:
  - `docker-compose.ngrok.yml`:
    - Service `ngrok` (profile `ngrok`, F-20, `NGROK_AUTHTOKEN: ${NGROK_AUTHTOKEN:?…}`, cổng `127.0.0.1:4040:4040`, `depends_on: tadss-runtime: condition: service_healthy`).
    - `tadss-runtime`: `ASPNETCORE_ENVIRONMENT: Staging`, `Tadss__Security__ApiKeys__0__ClientId: ${TADSS_API_CLIENT_ID:-smashcourt-be}`, `__KeySha256: ${TADSS_API_KEY_SHA256:?…}`, `Tadss__Security__TrustForwardedHeaders: "true"`.
    - Cổng **mọi** service dùng `!override` thành `127.0.0.1:…`.
  - `docker-compose.neon.yml`: `tadss-runtime` dùng `ConnectionStrings__DefaultConnection: ${TADSS_DB_CONNECTION:?…}`, `Tadss__Database__ApplyMigrationsOnStartup: "false"` (ép cứng), `Tadss__Cache__RedisConnectionString: ${TADSS_REDIS_CONNECTION:-tadss-redis:6379}`, `depends_on: !reset` rồi khai báo lại chỉ `tadss-redis: condition: service_healthy`.
  - `tools/expose/verify-expose.sh`.
  - `tests/TADSS.IntegrationTests/Infrastructure/ComposeFixtureAlignmentTests.cs`.

**Yêu cầu:**
1. Chạy compose base không có `.env` phải ra **đúng** cấu hình hiện tại: cùng cổng, cùng image (so `docker compose config` trước/sau, chỉ khác phần thêm ở mục Modify).
2. **`verify-expose.sh`** (bash, chạy được trên Git Bash/Windows và Ubuntu CI):
   - Project name riêng (`tadss-expose-check`) và cổng host riêng qua biến, để không đụng stack đang chạy của người dùng. `trap` dọn bằng `docker compose … down -v` của **đúng** project đó.
   - Tự sinh khóa ngẫu nhiên (`openssl rand -hex 32`) và mã băm. Không in khóa.
   - Kiểm tra `docker compose version` ≥ 2.24.4.
   - Các bước:
     - **NG-1:** `docker compose -f docker-compose.yml -f docker-compose.ngrok.yml config --quiet` (đặt biến giả cho `NGROK_AUTHTOKEN`) và thêm `-f docker-compose.neon.yml` (đặt `TADSS_DB_CONNECTION` giả).
     - **NG-2, NG-3, NG-5:** chế độ A, không bật profile `ngrok`. `up -d --build --wait tadss-runtime`. Container thăm dò (`curlimages/curl` ghim tag) trong network của project:
       - `GET /health/ready` = 200.
       - `POST /api/v2/decisions` không khóa = 401; có khóa = 200.
       - `GET /api/v2/decisions/{id}` = 200 cùng `decisionId`.
       - `GET /swagger/index.html` = 404.
       - `psql` trong `tadss-postgres` đếm `tadss.decision_executions` ≥ 1.
       - `docker compose ps --format json`: mọi `Publishers[].URL` là `127.0.0.1`.
     - **NG-2 (không khởi động):** `docker run` image vừa build với `ASPNETCORE_ENVIRONMENT=Staging`, chuỗi kết nối hợp lệ và **không** có khóa → container thoát mã khác 0 trong ≤ 30 giây; log có `Tadss:Security` và không có chuỗi kết nối.
     - **SEC-6:** `docker inspect` image → user non-root.
   - In bảng kết quả từng mã; có mã nào fail thì exit 1.
3. `ComposeFixtureAlignmentTests`: đọc `docker-compose.yml` bằng regex.
   - Mặc định của ba biến image PostgreSQL/Redis/Toxiproxy bằng hằng của `TadssInfrastructureFixture`.
   - Mặc định của `TADSS_API_KEY_SHA256` bằng `SecurityOptionsValidator.LocalDevelopmentKeySha256`.
4. `docker-compose.ngrok.yml` không chứa `0.0.0.0` và không có cổng thiếu `127.0.0.1`. `docker-compose.neon.yml` có `ApplyMigrationsOnStartup: "false"` dạng literal. Test đọc file khóa cả hai điều này.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `ComposeImageDefaults_MatchFixture_Theory` | 3 image | Bằng nhau | UT-09 |
| `ComposeLocalApiKey_MatchesValidatorConstant` | — | Bằng nhau | UT-09 |
| `NgrokOverride_BindsOnlyLoopback` | Đọc file | Mọi cổng `127.0.0.1:` | NG-5 |
| `NeonOverride_ForcesMigrationsOff` | Đọc file | Literal `"false"`, có `:?` cho `TADSS_DB_CONNECTION` | NG-1 |
| `verify-expose.sh` (local + CI) | Như yêu cầu 2 | NG-1, NG-2, NG-3 (A), NG-5, SEC-6 đạt | NG-1…3, NG-5, SEC-6 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh.
- Script đạt ở local; output bảng kết quả có trong báo cáo.
- Image ngrok (tag + digest) ghi vào báo cáo.
- Commit đề xuất: `feat: parameterized compose with ngrok and neon overrides`.

---

### Task 11: Tài liệu, skill và kiểm chứng cuối gói

**Files:**
- Modify:
  - `README.md`: mục **"Expose qua ngrok"**:
    - Hai chế độ database, bảng biến môi trường.
    - Tạo API key và mã băm: bash `openssl rand -hex 32`, `printf '%s' "$KEY" | sha256sum`; PowerShell 7 `[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($key))).ToLower()`.
    - Khóa E-LOCAL công khai và cảnh báo.
    - Các bước NG-4 cho người dùng.
    - Cấu hình rate limit cho k6.
    - Lệnh `verify-expose.sh`.
  - `README.md`: bảng cấu hình `Tadss:Security:*`.
  - `docs/contracts/INTEGRATION_SPEC.md`: ghi chú triển khai cho BE (spec Mục 9): `Tadss:BaseUrl` trỏ URL ngrok, `X-Api-Key`, header `ngrok-skip-browser-warning: true`, `NGROK_DOMAIN` cố định.
  - `docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md`:
    - Mục 4.2: thành phần mới (`OverrideService`, `OutcomeService`, `IDecisionGovernanceStore`, middleware bảo mật).
    - Mục 4.4: sơ đồ tuần tự thêm các bước bảo mật theo F-09 và nhánh 401/413/429; thêm sơ đồ ngắn cho override (F-03).
    - Một đoạn về thành phần theo instance (rate limit, breaker, cache L1). Phần đánh giá SC-3 vẫn ở 5.5.
  - `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`: chỉ khi mô tả lệch hiện thực.
  - `docs/planning/BACKLOG_SPRINTS.md`: T5.5, T5.6, T5.7, T5.30 → trạng thái và số test.
  - `TADSS-Runtime/CLAUDE.md`: cây thư mục mới (`Security/`, `Governance/`, `Outcomes/`), cấu hình bảo mật, file compose.
  - `../CLAUDE.md` (ngoài repo) nếu dòng TADSS cần đổi.
  - `../.claude/skills/tadss-architecture/SKILL.md` (ngoài repo): thêm hàng "middleware bảo mật", "port quản trị", "options bảo mật", quy tắc "phản hồi từ chối qua `ProblemResponseWriter`", "test bảo mật trong UnitTests".

**Yêu cầu:**
1. Tài liệu chỉ ghi điều đã hiện thực, sửa ở file sở hữu, không chép spec. Kiểm link nội bộ của mọi file `.md` đã sửa.
2. **Kiểm chứng cuối gói** (ghi số liệu thật vào báo cáo):
   - `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror --no-incremental` → 0/0.
   - Unit và IT (Docker) xanh. So sánh với đầu gói (1112 / 121), liệt kê số test mới theo mã danh mục.
   - `git diff 05aa840 HEAD -- docs/contracts/baseline_public_api.txt`: đúng 2 dòng (F-12).
   - `openapi-v2.yaml` khớp mã (drift test xanh).
   - `dotnet ef migrations list --no-connect` vẫn đúng 4 ID; `has-pending-model-changes` báo không có thay đổi (môi trường `Testing`).
   - `tools/expose/verify-expose.sh` đạt.
   - `docker compose config -q` hợp lệ cho chế độ base, A và B.
   - Khởi động app thật `Staging` với cổng không có dịch vụ và một khóa test: `/health/live` 200 trong ≤ 20 giây; không khóa → không khởi động. Chỉ dừng tiến trình do mình tạo (kiểm PID theo cổng trước khi dừng).
3. **Báo cáo gói gồm:**
   - Phụ lục A có cột trạng thái.
   - Bảng 6.1 của spec cho các dòng của 5.3.
   - Việc của người dùng: NG-4, NG-3 chế độ B, review diff OpenAPI/baseline, thêm API key cho môi trường dev.
   - Mục chuyển sang 5.4.

**Nghiệm thu:**
- Mọi mục kiểm chứng đạt; tài liệu khớp mã.
- Commit đề xuất: `docs: governance, security and ngrok exposure (package 5.3)`.
- Đề xuất PR vào `develop` (người dùng tạo và merge).

---

## Phụ lục A — Đối chiếu spec → task

| Yêu cầu (spec) | Task |
| :--- | :---: |
| 4.3 `OverrideService` (tồn tại, `PRIMARY`, `NEED_APPROVAL`, chưa hết hạn, chưa có outcome; `UNIQUE`; replay cùng key + payload; quy đổi cọc) | 2, 3, 4 |
| 4.3 `OutcomeService` (trùng cùng payload → 200; khác → 409) | 2, 3, 4 |
| 4.3 `DecisionQueryService` hàng chờ theo B.5, IT-09 trên ≥ 5.000 bản ghi | 2, 3, 4 |
| 4.3 `ApiKeyAuthenticationMiddleware` (`{clientId, keySha256}`, `FixedTimeEquals`, miễn trừ, `clientId` vào log và trace) | 5 |
| 4.3 `X-Actor-Id` bắt buộc với override, lưu `actor_id` | 3, 4 |
| 4.2.3 `DecisionStateConflictException` → 409 theo mã | 3 |
| 4.2.4 việc mang từ 5.2c: fail-fast chuỗi kết nối, đọc payload nới lỏng, 503 giả khi client hủy | 1 |
| 4.3.1 fail-closed ngoài Development/Testing; Swagger chỉ Development; `ForwardedHeaders` | 5, 6 |
| 4.3.1 `docker-compose.ngrok.yml`, `docker-compose.neon.yml`, `.env.example`, tham số hóa cổng/image, UT-09 compose ↔ fixture | 10 |
| 4.3.1 NG-1, NG-2, NG-3 (A), NG-5 tự động; NG-4, NG-3 (B) do người dùng chạy | 10, 11 |
| 4.3.2 SEC-1 rate limiting | 7 |
| 4.3.2 SEC-2 giới hạn kích thước | 6 |
| 4.3.2 SEC-3 security headers, HSTS, tắt header `Server` | 6 |
| 4.3.2 SEC-4 fail-closed (API key, chuỗi kết nối) | 1, 5 |
| 4.3.2 SEC-5 health live/ready | 8 |
| 4.3.2 SEC-6 container cứng | 9, 10 |
| 4.3.2 SEC-7 lỗ hổng, lock file, Dependabot, log không chứa khóa | 5, 9 |
| 4.3.2 thứ tự middleware khóa bằng UT-14 | 6, 7 |
| 4.3.2 `RATE_LIMITED`, `PAYLOAD_TOO_LARGE` vào §3.8, schema lỗi, OpenAPI | 6, 7 |
| 6.1 "thiếu API key hoặc chuỗi kết nối → không khởi động" (NG-2, SEC-4) | 1, 5, 10 |
| 9 tài liệu: BACKLOG (T5.30), README/`.env.example`, INTEGRATION_SPEC (ghi chú triển khai, §3.8), EXPERIMENTAL_DESIGN (mã mới) | 1, 4, 6, 10, 11 |
| Mục hoãn 5.2c: `detail` 400 chứa thông điệp STJ | 6 |

Không thuộc 5.3 (giữ theo spec và ADR-013):
- Tiền tố key cache `tadss:v2:` và option chết `InstanceName`/`TtlHours` → 5.4.
- EF log ERR cho mỗi `23505` mong đợi → 5.4.
- Rate limit phân tán và đánh giá scale SC-3 → 5.5.

## Phụ lục B — Thay đổi contract và hành vi cần duyệt

| # | Thay đổi | Nơi ghi |
| :--- | :--- | :--- |
| B-1 | Mọi `/api/*` cần `X-Api-Key`; 401 `UNAUTHORIZED` kèm `WWW-Authenticate: ApiKey header="X-Api-Key"` | INTEGRATION_SPEC §3.2, §3.8; OpenAPI |
| B-2 | Thêm 413 `PAYLOAD_TOO_LARGE` (body > 64 KB, cấu hình được) và 429 `RATE_LIMITED` kèm `Retry-After` | §3.8; OpenAPI; SDK `ErrorCodes` |
| B-3 | Header bảo mật trên mọi response; `Cache-Control: no-store` cho `/api/*`; HSTS ngoài Development/Testing | §3.8 (ghi chú) |
| B-4 | Hàng chờ: `pageSize` 1..100 (mặc định 20), `page ≥ 1`; thiếu `branchId` hoặc giá trị sai → 400; thứ tự `approvalExpiresAt` tăng dần rồi `decisionId` | §3.5 |
| B-5 | Override: thứ tự kiểm tra và mã theo F-03 (gồm `IDEMPOTENCY_KEY_REUSE` khi cùng key khác payload); quy đổi cọc theo F-04; `X-Actor-Id` và `reason` theo F-17 | §3.2, §3.6 |
| B-6 | Outcome: chống trùng theo payload; `X-Idempotency-Key` bắt buộc nhưng không lưu; thời điểm trả về theo UTC (F-05) | §3.7 |
| B-7 | `SHADOW` → 404 với override/outcome; `GET` chi tiết giữ nguyên | §3.6, §3.7 |
| B-8 | Tranh chấp override ↔ outcome không nguyên tử (F-06) | §4.3; INVARIANTS INV-5 |
| B-9 | Gỡ `/health`; `/health/ready` trả 200 `Degraded` khi Redis lỗi | README |
| B-10 | `detail` của 400 do lỗi giải mã body đổi thành câu tiếng Việt theo trường (F-23) | — (nội dung `detail` không thuộc contract) |
| B-11 | App không khởi động khi chuỗi kết nối thiếu hoặc là chuỗi giữ chỗ (mọi môi trường), hoặc khi thiếu API key (ngoài Development/Testing) | README |
| B-12 | Payload audit đọc nới lỏng (F-14); client hủy trong pha SQL không còn log 503 (F-15) | — |

## Ghi chú cho gói 5.4

- Shadow ghi bản `SHADOW` qua `IDecisionEvaluationStore.InsertExecutionAsync`. Hàng chờ đã loại `SHADOW` (IT-09 ca f), override/outcome trả 404 cho `SHADOW`.
- Nhiều instance (SC-1): rate limit SEC-1 tính theo instance. Nêu rõ trong kịch bản SC-2 và SC-3 (gói 5.5).
- Các mục giữ từ ADR-013: tiền tố key cache `tadss:v2:` đưa vào options, `InstanceName`/`TtlHours`, log ERR của `23505`.
