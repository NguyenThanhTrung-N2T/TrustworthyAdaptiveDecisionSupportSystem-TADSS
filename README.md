# TADSS Runtime

**Trustworthy Adaptive Decision Support System — Decision Runtime Engine** (Đồ án 2)

TADSS là nền tảng runtime độc lập ra quyết định cho hệ thống đặt chỗ dịch vụ. Hệ thống nhận yêu cầu quyết định (ví dụ mức đặt cọc cho một booking), chọn strategy phù hợp, thực thi strategy trong pipeline chịu lỗi Polly v8, lưu audit và cho phép người quản lý phê duyệt hoặc ghi đè. Ứng dụng đối chứng là **SmashCourt** (Đồ án 1). Chiến lược học máy (TADS) thuộc Khóa luận và nằm ngoài phạm vi repo ở Đồ án 2.

> **Trạng thái (04/10/2026):** Phase 1–4 hoàn thành trên contract v1. **Phase 5** (W05–W08, 05/10 – 01/11/2026 theo kế hoạch v2.1; bắt đầu thực tế 02/10) mở đầu bằng bước 0 — rà soát và điều chỉnh kế hoạch (re-baseline v2.0, Gate MR đạt 01/10/2026); contract v1 đã gỡ hoàn toàn khỏi mã và tài liệu, tra lại qua tag `phase4-baseline`. Phase 5 hiện thực contract v2 và chuyển toàn bộ kiểm thử sang hạ tầng thật. **Gói 5.1 (nền móng kiểm thử), 5.2a–5.2d (SDK v2, lõi runtime v2, Clean Architecture nhiều project), 5.3 (quản trị và bảo mật) và 5.4 (runtime thích ứng) đã xong:** năm endpoint `POST /api/v2/decisions`, `GET /api/v2/decisions/{decisionId}`, `GET /api/v2/decisions/pending-approvals`, `POST /api/v2/decisions/{decisionId}/override`, `POST /api/v2/decisions/{decisionId}/outcome`; xác thực `X-Api-Key`, giới hạn tốc độ theo client, giới hạn kích thước body, security headers, health `live`/`ready`, container non-root, lock file và quét lỗ hổng; cấu hình chạy Docker và expose qua ngrok (một file `docker-compose.yml`, một lệnh `docker compose up -d --build` chạy TADSS + ngrok + Grafana); migration `DecisionPersistenceV2` (chưa áp lên Neon); integration test chạy trên Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy); định tuyến strategy theo từng request (`Tadss:Routing`, nạp lại nóng), shadow mode, pipeline và circuit breaker riêng cho từng strategy, cách ly strategy không hợp tác, circuit breaker Redis (gói 5.4); hiện có **1707 unit + 211 integration test**. Chưa có: observability (gói 5.5). Kế hoạch: [`docs/planning/MASTER_EXECUTION_PLAN.md`](docs/planning/MASTER_EXECUTION_PLAN.md).

---

## Nghiệp vụ cốt lõi

- TADSS xác định được mức cọc → `APPROVED` kèm số tiền cọc.
- Nghiệp vụ cần người xem xét (giá trị lớn, lịch sử không đến…) → `NEED_APPROVAL`, Manager duyệt kèm mức cọc hoặc từ chối; không bao giờ tự duyệt.
- Mọi sự cố đều cọc 100%: strategy lỗi / timeout / circuit mở → fallback `APPROVED` cọc 100% (`executionType = FALLBACK`, không vào hàng chờ duyệt);
  SmashCourt áp cọc 100% khi tắt feature flag hoặc TADSS lỗi / không phản hồi; không kịp duyệt trước giờ chơi → `APPROVED` cọc 100%.

Chi tiết và contract v2: [`docs/contracts/INTEGRATION_SPEC.md`](docs/contracts/INTEGRATION_SPEC.md).

---

## Cấu trúc

| Project | Vai trò | Phụ thuộc |
| :--- | :--- | :--- |
| `src/Shared.DecisionContractSDK` | Hợp đồng dùng chung v2 (`2.0.0-draft`): `Reservation` (rich domain model), DTO, enum, `StrategyDecision`/`DecisionContext`, `IReservationDecisionStrategy` | Chỉ BCL |
| `src/TADSS.Application` | Use case (`DecisionService`, `DecisionQueryService`, `StrategyEvaluator`, `Governance/` = `OverrideService`, `Outcomes/` = `OutcomeService`), port (`Abstractions/{Caching,Persistence,Resilience}`), `IStrategyRegistry`, options nghiệp vụ + validator, exception của runtime | SDK |
| `src/TADSS.Infrastructure` | Adapter: audit và quản trị PostgreSQL schema `tadss` (EF Core, migration, hàng chờ duyệt), cache `StrategyDecision` Redis/`IMemoryCache`, pipeline Polly v8, health check | Application, SDK |
| `src/TADSS.Runtime.Api` | Presentation + composition root: `Program.cs`, `Composition/`, controller, middleware (gồm bảo mật: API key, giới hạn kích thước, security headers), `Security/`, xử lý lỗi RFC 7807, OpenAPI | Tất cả |
| `src/TADSS.Strategies.Default` | `DefaultDecisionStrategy` | SDK |
| `src/TADSS.Strategies.MockAlternative` | `MockAlternativeDecisionStrategy` | SDK |
| `tests/TADSS.UnitTests` | Cây mirror `src/`: SDK, strategy, Application, Infrastructure, Api (gồm drift OpenAPI), kiểm thử kiến trúc | |
| `tests/TADSS.IntegrationTests` | API qua `WebApplicationFactory` trên Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy): endpoint, idempotency, cache, persistence, tiêm lỗi, migration | |

Hướng phụ thuộc: `SDK ← Application ← Infrastructure ← Runtime.Api`; chỉ `Program.cs` và `Composition/` của Runtime.Api được dùng kiểu của Infrastructure. Quy tắc do compiler và test UT-14 cưỡng chế (ADR-013).

Kiến trúc chi tiết: [`docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md).

---

## Công nghệ

- **.NET 8**, ASP.NET Core Web API, EF Core 8 + Npgsql, PostgreSQL 16
- **Polly 8.4**, StackExchange.Redis 2.8, Swashbuckle 6.6, Serilog 8
- Kiểm thử hiện tại: xUnit 2.5.3, FluentAssertions 6.12.1, Moq 4.20, `Microsoft.AspNetCore.Mvc.Testing` 8.0, Testcontainers (PostgreSQL 16.15, Redis 7.4, Toxiproxy 2.12.0)
- Bổ sung ở Phase 5: OpenTelemetry + `grafana/otel-lgtm`, Schemathesis, Stryker.NET, k6 (xem [`docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md))

Phiên bản package khai báo tập trung trong `Directory.Packages.props`.

---

## Build, test, chạy

```bash
dotnet restore TADSS.slnx
dotnet build TADSS.slnx -c Release --warnaserror        # bắt buộc 0 warning
dotnet test  TADSS.slnx -c Release --no-build -m:1       # 1516 unit + 196 integration; integration cần Docker Desktop đang chạy
dotnet run --project src/TADSS.Runtime.Api               # http://localhost:5296/swagger (mọi /api/* cần X-Api-Key, xem mục Bảo mật)
```

**Integration test cần Docker Desktop** (Testcontainers tự dựng PostgreSQL 16.15, Redis 7.4, Toxiproxy 2.12.0; mỗi test dùng một database riêng, xóa sau khi chạy). Chỉ chạy unit test (không cần Docker): `dotnet test tests/TADSS.UnitTests -c Release --no-build`.

Lệnh EF Core (chỉ đọc, không kết nối database):

```bash
ASPNETCORE_ENVIRONMENT=Testing ConnectionStrings__DefaultConnection="Host=localhost;Database=x" dotnet ef migrations list --no-connect --project src/TADSS.Infrastructure --startup-project src/TADSS.Runtime.Api
```

Biến `ConnectionStrings__DefaultConnection` ở trên là chuỗi giả (không phải secret, không mở kết nối): công cụ EF khởi động host ở design-time, mà môi trường `Testing` không nạp `appsettings.Development.json` và validator từ chối chuỗi giữ chỗ trong `appsettings.json`.

Cấu hình kết nối PostgreSQL/Redis và API key đặt trong user-secrets hoặc `appsettings.Development.json` (không commit). **Chạy `dotnet run` cần có ít nhất một API key** trong `Tadss:Security:ApiKeys` (xem mục Bảo mật): không có thì app vẫn khởi động ở `Development` nhưng mọi request `/api/*` bị 401. `appsettings.json` chỉ chứa giá trị mẫu. Chuỗi kết nối PostgreSQL (`ConnectionStrings:DefaultConnection`, bắt buộc ở mọi môi trường, thiếu hoặc sai thì app không khởi động) phải ở dạng Npgsql `key=value` (ví dụ `Host=...;Port=5432;Database=...;Username=...;Password=...`); URI dạng `postgresql://...` (kiểu Neon hay cấp) **không** được chấp nhận, cần đổi sang dạng `key=value`.

### Phát triển hằng ngày và triển khai (docker compose)

Phát triển hằng ngày dùng `dotnet run` với `appsettings.Development.json` (PostgreSQL Neon + Redis Cloud, khóa API của bạn), **không dùng compose** (quyết định D-04). Schema `tadss` trên Neon do người dùng tự migrate bằng `dotnet ef database update` (quy tắc D-05); ứng dụng không tự migrate: `Tadss:Database:ApplyMigrationsOnStartup` mặc định `false`, chỉ bật cho database thử dùng một lần (Testcontainers, `tools/expose/verify-expose.sh`).

`docker-compose.yml` là file compose **duy nhất**, chỉ dùng để chạy TADSS trong Docker và expose qua ngrok (`tadss-runtime` chạy ở `Development` như SmashCourt-BE, cùng `ngrok` và `grafana`, không profile: một lệnh `docker compose up -d --build` chạy cả ba; database Neon, Redis Cloud và API key đọc từ `appsettings.Development.json` được copy vào image local), xem mục Chạy bằng Docker và expose qua ngrok. Chế độ đánh giá nằm cùng file nhưng sau profile `evaluation` (không bao giờ được `up` thường khởi động), xem "Chế độ đánh giá một lệnh" bên dưới.

**Ba chế độ chạy (D-12):**

| Chế độ | Lệnh | Dùng để | Trạng thái |
| :--- | :--- | :--- | :--- |
| Dev hằng ngày | `dotnet run --project src/TADSS.Runtime.Api` | Neon + Redis Cloud qua `appsettings.Development.json`; chỉ app + Swagger để kiểm tra luồng, không Docker, không Grafana | Đã có |
| Vận hành / demo | `docker compose up -d --build` | TADSS + ngrok + Grafana (`grafana/otel-lgtm`, `localhost:3001`) | Đã có (D-11) |
| Đánh giá cho hội đồng | `docker compose -p tadss-eval --profile evaluation run --rm --build evaluator` | Stack cô lập, **không bao giờ chạm Neon**: TADSS (Release) × 2 sau reverse proxy + PostgreSQL 16 + Redis 7 + Toxiproxy + Grafana + container evaluator chạy toàn bộ test, k6, sự cố dưới tải, Schemathesis, Stryker rồi sinh `evidence/<run_id>/` (`report.html`, dữ liệu thô, `manifest.json`); bản nhanh ~25 phút, bản đầy đủ ~2,8 giờ; chạy trên một máy nên là số liệu phòng lab | Đã có (gói 5.5), xem mục "Chế độ đánh giá một lệnh" |

> **Không chạy chế độ đánh giá bằng `docker compose --profile evaluation up`.** Service không gắn profile (`tadss-runtime` ở `Development` đọc Neon, và `ngrok`) luôn được `up` khởi động bất kể profile, nên lệnh đó sẽ nối Neon thật và mở tunnel Internet (đã kiểm chứng ở spike S-5 của gói 5.5). Dùng `run evaluator` trong project riêng `tadss-eval` như bảng trên; `docker compose -p tadss-eval --profile evaluation down -v` chỉ dọn stack đánh giá, không dừng stack vận hành.

Test tự động trên CI vẫn dùng Testcontainers (`dotnet test`). Chi tiết: quyết định D-12 trong [`spec/PHASE5_DESIGN_SPEC.md`](spec/PHASE5_DESIGN_SPEC.md) và Mục 6.1 của [`docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).

**Khóa local công khai.** Khóa `tadss-local-dev-key` (client `local`, mã băm `0472e99d9ccd889b6e6781cf0920a9b39910c1c430393d62839465c4901a6268`) chỉ dành cho môi trường `Development` qua `appsettings.Development.json` (đặt mã băm này vào `Tadss:Security:ApiKeys`). Ai đọc README này cũng biết khóa đó nên **không bao giờ** expose ra mạng ngoài với khóa này: ngoài `Development`/`Testing`, validator của `Tadss:Security` từ chối mã băm này và app không khởi động.

---

## Bảo mật

Mọi request `/api/*` phải mang `X-Api-Key`. Thứ tự middleware (khai báo tại `Composition/RequestPipelineExtensions.UseTadssRequestPipeline`, khóa bằng test UT-14): forwarded headers (chỉ khi `TrustForwardedHeaders`) → correlation id → request logging → request metrics → exception handler → security headers + HSTS → https redirection → giới hạn kích thước → Swagger (chỉ `Development`) → xác thực API key → rate limiter → routing. Mã lỗi và contract: [`docs/contracts/INTEGRATION_SPEC.md`](docs/contracts/INTEGRATION_SPEC.md) mục 3.2 và 3.8. Các phản hồi từ chối do middleware tạo (401, 413, 429) và mọi lỗi do exception (qua `GlobalExceptionHandler`) đi qua một bộ ghi `ProblemResponseWriter` nên cùng dạng `application/problem+json`; ngoại lệ là 415 (do MVC), 404/405 của framework và một số lỗi mức Kestrel, không có `code` (INTEGRATION_SPEC §3.8).

### Cấu hình `Tadss:Security:*`

Khai báo trong `appsettings.json`, ghi đè bằng biến môi trường dạng `Tadss__Security__...`; có validator và `ValidateOnStart` nên giá trị sai làm app không khởi động (thông báo chỉ nêu tên khóa, không in mã băm).

| Khóa | Mặc định | Ý nghĩa |
| :--- | :---: | :--- |
| `ApiKeys` | `[]` | Danh sách `{ ClientId, KeySha256 }`. `ClientId` 1..64 ký tự `A-Za-z0-9._-`, duy nhất (không phân biệt hoa thường), không dùng `health`/`anonymous`. `KeySha256` là SHA-256 dạng hex 64 ký tự của khóa, duy nhất. Rỗng thì mọi `/api/*` bị 401 (`Development`/`Testing` vẫn khởi động và ghi Warning); môi trường khác thì app **không khởi động** và cũng từ chối mã băm của khóa local công khai |
| `MaxRequestBodyBytes` | `65536` | Trần kích thước body (1024..1048576 byte). Vượt thì 413 `PAYLOAD_TOO_LARGE`, kiểm tra trước xác thực |
| `RateLimiting:PermitLimit` | `100` | Dung lượng xô token của mỗi client (1..100000), cũng là số request liên tiếp tối đa khi xô đầy |
| `RateLimiting:TokensPerPeriod` | `100` | Số token nạp lại mỗi chu kỳ (1..`PermitLimit`) |
| `RateLimiting:ReplenishmentPeriodSeconds` | `1` | Chu kỳ nạp token (1..3600 giây) |
| `TrustForwardedHeaders` | `false` | Tin `X-Forwarded-For/Proto/Host`. Chỉ bật khi chỉ reverse proxy (ngrok) chạm được app; `docker-compose.yml` đặt `true` |

### Tạo API key và mã băm

Khóa là chuỗi bất kỳ do bạn sinh; app chỉ lưu SHA-256 của khóa. Đưa **khóa** cho client (SmashCourt-BE), đặt **mã băm** vào cấu hình TADSS.

```bash
# bash (Git Bash, Linux, macOS)
KEY=$(openssl rand -hex 32)
echo "Khóa (đưa cho client, không commit): $KEY"
printf '%s' "$KEY" | sha256sum | cut -d' ' -f1        # macOS: printf '%s' "$KEY" | shasum -a 256 | cut -d' ' -f1
```

```powershell
# PowerShell 7
$key = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLower()
"Khóa (đưa cho client, không commit): $key"
[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($key))).ToLower()
```

Mã băm phải tính trên đúng chuỗi khóa client sẽ gửi (không thêm ký tự xuống dòng). Đặt vào:

- `Development` (`dotnet run`): `appsettings.Development.json` (không commit) hoặc user-secrets, dạng `{ "Tadss": { "Security": { "ApiKeys": [ { "ClientId": "dev", "KeySha256": "<mã băm>" } ] } } }`; hoặc biến môi trường `Tadss__Security__ApiKeys__0__ClientId` và `Tadss__Security__ApiKeys__0__KeySha256`.
- Docker/expose: cùng `appsettings.Development.json` ở trên (được copy vào image local, container chạy `Development` như SmashCourt-BE; xem mục Chạy bằng Docker và expose qua ngrok); khóa phải là khóa riêng, không dùng khóa local công khai.

Mỗi client một phần tử (chỉ số `0`, `1`, ...); thu hồi khóa bằng cách xóa phần tử rồi khởi động lại.

### Health, giới hạn tốc độ, header, log

- **Health:** `/health/live` (không kiểm tra gì, 200 khi tiến trình còn chạy) và `/health/ready` (PostgreSQL bắt buộc: lỗi → 503; Redis: lỗi → 200 `Degraded` vì cache có fallback memory). Cả hai miễn `X-Api-Key` ở mọi môi trường, không bị rate limit, body chỉ là chữ trạng thái (không lộ chuỗi kết nối). Không còn endpoint `/health` gộp.
- **Rate limit** (token bucket, một xô cho mỗi `ClientId`; hết token là 429 `RATE_LIMITED` kèm `Retry-After`): tính **theo từng instance** của app, không chia sẻ giữa nhiều instance (đánh giá khi scale thuộc gói 5.5). Mặc định cho phép bùng nổ 100 request và nạp 100 request mỗi giây cho một client. Để k6 tải cao hơn, tăng bằng biến môi trường của tiến trình app: `Tadss__Security__RateLimiting__PermitLimit`, `Tadss__Security__RateLimiting__TokensPerPeriod`, `Tadss__Security__RateLimiting__ReplenishmentPeriodSeconds` (với compose: thêm vào `environment` của `tadss-runtime` trong một file override cục bộ).
- **Header bảo mật** trên mọi response: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`; `Cache-Control: no-store` cho `/api/*`; `Strict-Transport-Security` với request https ngoài `Development`/`Testing`; Kestrel không gửi header `Server`.
- **Log:** khóa `X-Api-Key` và mã băm không bao giờ vào log. `ClientId` của khóa hợp lệ có ở thuộc tính log scope (thấy trong `{Properties:j}` của log phát sinh trong controller/service) và ở tag `tadss.client_id` của trace. Dòng log hoàn tất request của Serilog được làm giàu `ClientId` lúc kết thúc request (request bị từ chối ở bước xác thực thì không có). Request logging đứng **ngoài** exception handler nên ghi đúng status cuối (400, 404, 409, 503, 504); status ≥ 500 ghi mức Error, còn lại Information.

### Chuỗi cung ứng và container

- Mọi project có `packages.lock.json` (`RestorePackagesWithLockFile` trong `Directory.Build.props`); CI và `Dockerfile` restore bằng `--locked-mode`, nên lock file lệch đồ thị phụ thuộc thì build đỏ. **Dependabot** (NuGet, GitHub Actions, Docker; mỗi tuần, nhánh `develop`) có thể mở PR nâng package mà không cập nhật `packages.lock.json`: khi đó CI đỏ ở bước restore cho tới khi chạy `dotnet restore --force-evaluate` rồi commit các `packages.lock.json` đã đổi vào PR.
- CI chạy `tools/ci/check-vulnerable-packages.sh` (`dotnet list package --vulnerable --include-transitive` cho cả solution): mức High/Critical hoặc không quét được thì job đỏ. `System.Net.Http` 4.3.4 và `System.Text.RegularExpressions` 4.3.1 được ghim bắc cầu trong `Directory.Packages.props` (`CentralPackageTransitivePinningEnabled`) vì xunit 2.5.3 kéo bản 4.3.0 có lỗ hổng; chỉ ảnh hưởng project test, gỡ khi nâng xunit.
- Image chạy bằng user non-root (`USER $APP_UID`); `.dockerignore` loại `.env`/khóa riêng (`appsettings.Development.json` vào image local như SmashCourt-BE, nên image không bao giờ được push); CI kiểm `Config.User` của image.

---

## Runtime thích ứng: định tuyến, shadow, cache và cách ly lỗi

Cấu hình dưới đây đều qua validator + `ValidateOnStart`: sai thì app không khởi động. Giá trị mặc định giữ nguyên hành vi (một strategy mặc định, shadow tắt, khóa cache như cũ).

### `Tadss:Routing` — chọn strategy theo từng request (nạp lại nóng)

| Khóa | Mặc định | Ý nghĩa |
| :--- | :---: | :--- |
| `DefaultStrategy` | `DefaultDecisionStrategy` | Strategy dùng khi không rule nào khớp (thay khóa cũ `Tadss:Strategy:ActiveStrategyName`, nay chỉ còn sinh Warning khi gặp khóa đó) |
| `Rules[]` | `[]` | Danh sách rule, **rule đầu tiên khớp** được chọn. Mỗi rule: `Name` (duy nhất, ≤ 64 ký tự), `Strategy`, `When` |
| `Rules[].When` | — | Phải có ít nhất một điều kiện; mọi điều kiện là AND; danh sách rỗng hoặc bỏ trống = không ràng buộc: `PeakHour` (bool, theo lịch cao điểm giờ chi nhánh), `BranchIds`, `Channels` (`ONLINE`/`WALK_IN`), `Tiers` (`CustomerTier`), `IsGuest` (bool) |

Ví dụ: giờ cao điểm dùng `MockAlternativeDecisionStrategy`, còn lại dùng mặc định:

```json
"Tadss": { "Routing": { "DefaultStrategy": "DefaultDecisionStrategy",
  "Rules": [ { "Name": "peak-hour", "Strategy": "MockAlternativeDecisionStrategy", "When": { "PeakHour": true } } ] } }
```

Tên strategy phải có trong registry (sai thì không khởi động). Đổi `Tadss:Routing` trong file cấu hình đang chạy (`appsettings*.json` có `reloadOnChange`; biến môi trường không nạp lại khi chạy) có hiệu lực ngay; bản mới sai thì **giữ bảng cũ**, ghi log Error và tăng `tadss.config.reload_failures{section="Tadss:Routing"}`. Mỗi strategy có pipeline chịu lỗi và circuit breaker riêng nên strategy này lỗi không làm strategy khác fast-fail.

### `Tadss:Shadow` — chạy thêm một strategy ứng viên ở nền

| Khóa | Mặc định | Ý nghĩa |
| :--- | :---: | :--- |
| `Enabled` | `false` | Bật shadow (không nạp lại nóng: đổi phải khởi động lại) |
| `Strategy` | `""` | Strategy chạy shadow; bắt buộc và phải có trong registry khi bật; trùng strategy chính của request thì bỏ qua |
| `SampleRate` | `1.0` | Tỷ lệ request được chạy shadow, [0, 1] |
| `QueueCapacity` | `256` | Sức chứa hàng đợi (1..10000); đầy thì bỏ việc và tăng `tadss.shadow.dropped{reason="queue_full"}` |

Sau khi bản `PRIMARY` của chính request đã lưu, một worker nền chạy strategy shadow (pipeline riêng, không cache, cùng mốc thời gian) và ghi bản `role = SHADOW` (không khóa idempotency; ghép cặp với `PRIMARY` qua `reservation_id`, `request_hash`, `created_at`, `correlation_id`). Shadow không đổi response, không vào hàng chờ duyệt, không override/outcome được (404). Shadow lỗi/treo thì có bản `SHADOW` là `FALLBACK` hoặc chỉ ghi log + `tadss.shadow.executions{outcome="failed"}`.

### `Tadss:Cache` — khóa và circuit breaker Redis

| Khóa | Mặc định | Ý nghĩa |
| :--- | :---: | :--- |
| `InstanceName` | `tadss:` | Tiền tố cộng vào mọi khóa cache (Redis và memory), không rỗng, ≤ 32 ký tự, không khoảng trắng; khóa đầy đủ mặc định `tadss:v2:...` (khóa TTL cũ của section này đã gỡ: TTL do strategy khai báo) |
| `RedisBreakerFailureRatio` | `0.5` | Tỷ lệ lệnh Redis lỗi/quá hạn trong khung lấy mẫu để breaker mở, (0, 1] |
| `RedisBreakerSamplingSeconds` | `30` | Khung lấy mẫu (giây), ≥ 1 |
| `RedisBreakerMinimumThroughput` | `10` | Số lệnh tối thiểu trước khi được mở, ≥ 2 |
| `RedisBreakerBreakDurationSeconds` | `30` | Thời gian mở (giây), ≥ 1 |

Breaker mở thì request bỏ qua Redis ngay (dùng memory nếu `EnableFallbackToMemory`), không chờ `RedisTimeoutMs`; `tadss.cache.requests{result="bypass"}` đếm các lần đó. Lỗi cache không bao giờ kích hoạt fallback của strategy. Breaker (Redis và strategy) và cache memory đều **theo từng instance**.

### `Tadss:Testing:FaultInjection` — tiêm lỗi strategy (chỉ môi trường `Testing`)

| Khóa | Mặc định | Ý nghĩa |
| :--- | :---: | :--- |
| `Enabled` | `false` | Chỉ có tác dụng khi `ASPNETCORE_ENVIRONMENT=Testing`; ở môi trường khác bật cờ này chỉ sinh Warning, không bọc strategy |
| `Profile` | `None` | `None`, `Delay` (chờ có tôn trọng token), `Throw`, `IgnoreToken` (chờ không tôn trọng token), `BusyCpu` (quay vòng CPU) |
| `DelayMs` | `1000` | 1..60000 |
| `TargetStrategy` | rỗng | Rỗng = mọi strategy |

Dùng cho kịch bản sự cố dưới tải (LT-03). Strategy bị bỏ qua token vẫn bị cắt đúng hạn (pipeline cách ly nó khỏi luồng request) nhưng CPU của nó chỉ dừng khi nó tự kết thúc — xem giới hạn ở `docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md`.

### Metric và log

Meter `TADSS.Runtime` (13 instrument, khai báo ở `TadssMetricNames`): quyết định, thời gian request và pipeline, fallback, trạng thái breaker, cache, hàng chờ duyệt, override, shadow, nạp lại cấu hình. Xuất qua OpenTelemetry (OTLP) khi `Tadss:Telemetry:Enabled = true` (mặc định tắt); `docker compose up` bật sẵn và gửi tới Grafana. Lỗi lệnh SQL mong đợi (tranh chấp idempotency `23505`) chỉ ghi ở mức Debug; lỗi thật vẫn ghi ở Warning/Error bởi store và exception handler.

## Observability và chế độ đánh giá

### Observability (OpenTelemetry + Grafana)

- **Chế độ vận hành** (`docker compose up -d --build`): TADSS gửi trace, metric và log tới `grafana/otel-lgtm` (cổng OTLP `4317`); mở `http://localhost:3001`, thư mục **TADSS** có dashboard **TADSS Runtime** nạp sẵn (36 panel: tổng quan, quyết định, chịu lỗi, cache, quản trị và shadow, tải k6; biến `instance`; annotation `chaos`).
- **`dotnet run` hằng ngày:** telemetry tắt (không cần collector). Muốn xem dashboard khi chạy tay, bật collector riêng và trỏ TADSS tới nó:
  `docker run --rm -p 3001:3000 -p 4317:4317 -v "$PWD/docker/grafana/provisioning/dashboards/tadss.yaml:/otel-lgtm/grafana/conf/provisioning/dashboards/tadss.yaml:ro" -v "$PWD/docker/grafana/dashboards:/otel-lgtm/tadss-dashboards:ro" grafana/otel-lgtm:0.34.0`, rồi đặt `Tadss__Telemetry__Enabled=true` và `Tadss__Telemetry__OtlpEndpoint=http://localhost:4317`. Collector chết hoặc không tới được không ảnh hưởng request.
- Kiểm tự động (không cần Neon/ngrok): `bash tools/observability/verify-observability.sh` (OB-2 dashboard đã nạp, OB-4 có `tadss_decisions_total` sau vài request).

| Khóa `Tadss:Telemetry:*` | Mặc định | Ý nghĩa |
| :--- | :---: | :--- |
| `Enabled` | `false` | `false` ⇒ không đăng ký provider OpenTelemetry nào, worker gauge hàng chờ không truy vấn |
| `OtlpEndpoint` | `http://localhost:4317` | URI tuyệt đối http/https khi `Enabled` |
| `OtlpProtocol` | `Grpc` | `Grpc` hoặc `HttpProtobuf` |
| `ServiceName` | `tadss-runtime` | Nhãn `job`; không rỗng, tối đa 64 ký tự |
| `ServiceInstanceId` | rỗng | Nhãn `instance`; rỗng ⇒ tên máy/container |
| `TraceSampleRatio` | `1.0` | 0..1, lấy mẫu trace `ParentBased(TraceIdRatioBased)` |
| `ExportLogs` | `true` | Thêm sink log OTLP (Loki) |
| `MetricExportIntervalMs` | `10000` | 1000..60000 |
| `PendingApprovalsRefreshSeconds` | `30` | 5..3600, chu kỳ làm mới gauge hàng chờ duyệt |

### Chế độ đánh giá một lệnh

Dựng stack cô lập (hai instance TADSS sau nginx, PostgreSQL 16, Redis 7, Toxiproxy, Grafana) và một container **evaluator** chạy tuần tự: (a) toàn bộ test gộp theo mã danh mục + `verify-expose.sh` + kiểm dashboard; (b) k6 LT-01, LT-02 (và LT-05 ở bản đầy đủ); (c) sự cố dưới tải LT-03 (FI-02, FI-03, FI-08, FI-07 qua file profile và Toxiproxy) + SCALE-2; (d) Schemathesis CT-03, coverage gộp, Stryker MT-01…03 (chỉ bản đầy đủ); (e) gom `evidence/<run_id>/`.

```bash
# Cách gọn nhất (tự build/tải image ở lần đầu, in đường dẫn report.html):
bash tools/evaluation/run.sh                 # bản nhanh
bash tools/evaluation/run.sh full            # bản đầy đủ
bash tools/evaluation/run.sh quick --clean   # chạy xong dọn sạch container/volume/image/build cache (~6 GB)
bash tools/evaluation/run.sh --clean-only    # chỉ dọn
# Hoặc gọi docker compose trực tiếp — bản nhanh (mặc định), chạy trong thư mục TADSS-Runtime/
docker compose -p tadss-eval --profile evaluation run --rm --build evaluator
# Bản đầy đủ (số liệu chính thức)
docker compose -p tadss-eval --profile evaluation run --rm --build -e EVAL_MODE=full evaluator
# Chạy lại một vài bước (ví dụ chỉ tải và sự cố)
docker compose -p tadss-eval --profile evaluation run --rm --build -e EVAL_STEPS=b,c evaluator
# Dọn stack đánh giá (không đụng stack vận hành)
docker compose -p tadss-eval --profile evaluation down -v
```

- **Kết quả:** `evidence/<run_id>/` (`run_id` = `EV-{yyyyMMdd}-EVAL-{quick|full}-r{n}`; thư mục làm việc, không commit): `report.html` (mở thẳng bằng trình duyệt, tự chứa), `manifest.json` (SHA-256 từng file, commit, máy, KPI SC1…SC8, EV-1…EV-5), `raw/` (trx theo `CatalogId`, k6 từng request, `chaos_log.csv`, metric, Schemathesis, Stryker, coverage, D1 `decision_audit.csv`), `grafana/dashboard-snapshot.json` (không lưu ảnh dashboard), `config/`.
- **Mã thoát:** `0` đạt mọi tiêu chí; `1` chạy hết nhưng có tiêu chí không đạt; `2` lỗi hạ tầng hoặc không chạy hết. Sau khi evaluator thoát, Grafana của stack đánh giá (`http://127.0.0.1:3002`) vẫn chạy để xem lại tới khi `down -v`.
- **Thời lượng đo thật:** bản nhanh **22–25 phút** (lượt cuối 21,9 phút; một lượt điển hình: a 532 s, b 400 s, c 509 s, d 38 s, e 34 s; chưa tính lần đầu dựng image/stack); bước (d) bản đầy đủ **25,9 phút** (CT-03 500 ví dụ 190 s + Stryker ≈ 21 phút); bản đầy đủ trọn vẹn **ước tính ≈ 2,8 giờ** từ các bước đã đo (a 9 phút, b ≈ 110 phút gồm LT-05 61 phút, c ≈ 20 phút, d 26 phút, e ≈ 1,5 phút) và sẽ đo trọn vẹn ở gói 5.7
- **Tài nguyên:** nên có ≥ 8 GB RAM cấp cho Docker; image cần ~6 GB (otel-lgtm 3,7 GB, evaluator 1,65 GB); `run.sh --clean` xóa lại sau khi chạy. Tắt chế độ ngủ của máy khi chạy (máy ngủ giữa lượt làm số đo sai). Bước (a) chạy unit test tuần tự để kết quả tất định trong container.
- **Không dùng `docker compose --profile evaluation up`** (xem cảnh báo ở mục "Ba chế độ chạy").
- **Cảnh báo bảo mật:** evaluator mount `/var/run/docker.sock` (Testcontainers cho integration test) nên có quyền điều khiển Docker của máy; chỉ chạy trên máy của chính bạn. Mã nguồn được mount chỉ đọc và copy sang `/work` **loại trừ** `appsettings.Development.json`, `.env*`; stack đánh giá dùng khóa API cố định không bí mật (`tadss-evaluation-key`) và chỉ có hiệu lực ở đó.
- **Giới hạn:** một máy (k6, TADSS, DB tranh CPU), môi trường `Testing`, rate limit nâng, trace lấy mẫu 10 % — xem Mục 8 của methodology và phần "Giới hạn" của `report.html`.

## Chạy bằng Docker và expose qua ngrok

Chỉ **một lệnh** `docker compose up -d --build` chạy TADSS, ngrok và Grafana cùng lúc (SmashCourt-BE ở máy khác, demo). `docker-compose.yml` là file compose duy nhất, không profile, cấu hình **giống SmashCourt-BE**: `tadss-runtime` chạy `ASPNETCORE_ENVIRONMENT=Development` (cổng host chỉ `127.0.0.1`), `ngrok` chỉ cần `NGROK_AUTHTOKEN` (không domain tĩnh), Grafana (`grafana/otel-lgtm`). Database là Neon, cache là Redis Cloud và API key đều khai báo trong `src/TADSS.Runtime.Api/appsettings.Development.json` (file duy nhất `dotnet run` và Docker dùng chung): file đó được **copy vào image** lúc build (`.dockerignore` không loại nó), không mount thêm gì lúc chạy. `.env` chỉ chứa `NGROK_AUTHTOKEN`.

1. Thêm API key **thật** vào `src/TADSS.Runtime.Api/appsettings.Development.json` (không commit), mục `Tadss:Security:ApiKeys`: một phần tử `{ "ClientId": "smashcourt-be", "KeySha256": "<mã băm>" }` (cách sinh khóa và mã băm: [Tạo API key và mã băm](#tạo-api-key-và-mã-băm)). **Không** dùng khóa local công khai `tadss-local-dev-key`: ở `Development` validator vẫn chấp nhận nó nên không có gì chặn bạn expose nhầm. SmashCourt-BE dùng cùng một khóa cho cả `dotnet run` lẫn Docker. Chuỗi kết nối Neon và Redis Cloud trong file đó cũng được container dùng; schema `tadss` trên Neon do bạn migrate sẵn (`dotnet ef database update`), container **không bao giờ tự migrate**.
2. Tạo mạng dùng chung với SmashCourt-BE/FE (một lần): `docker network create smashcourt-network`.
3. Tạo `.env` từ `.env.example` (không commit) và điền `NGROK_AUTHTOKEN` (lấy ở dashboard.ngrok.com).
4. Chạy tất cả: `docker compose up -d --build`. TADSS ở `http://localhost:8081`, Grafana ở `http://localhost:3001`. Mỗi lần đổi `appsettings.Development.json` phải build lại image (`--build`).
5. Lấy URL công khai của ngrok: mở `http://localhost:4040` hoặc chạy `docker compose logs ngrok`. URL **đổi mỗi lần khởi động lại** ngrok, nên phải cập nhật `Tadss:BaseUrl` của SmashCourt-BE mỗi lần. Muốn URL cố định, lấy domain tĩnh miễn phí ở dashboard ngrok rồi thêm `--url=<domain>` vào cuối dòng `command` của service `ngrok` trong `docker-compose.yml` (sửa một dòng).
6. Grafana: mở `http://localhost:3001`. Image `grafana/otel-lgtm` bật sẵn truy cập ẩn danh với quyền Admin nên vào thẳng không cần đăng nhập; nếu cần đăng nhập thì mặc định là `admin` / `admin` (đây là mặc định công khai của Grafana, không phải secret của dự án). Cổng OTLP `4317` (gRPC) và `4318` (HTTP) chỉ bind `127.0.0.1`. TADSS gửi trace, metric và log tới Grafana qua OTLP: dashboard **TADSS Runtime** (thư mục "TADSS", uid `tadss-runtime`, nạp sẵn từ `docker/grafana/dashboards`, chỉ đọc) có số liệu sau vài request. Kiểm tự động: `bash tools/observability/verify-observability.sh` (cần Docker; lần đầu tải image ~3,7 GB).
7. Dừng tất cả: `docker compose down`.

### Lệnh Docker thường dùng

Chạy trong thư mục `TADSS-Runtime/`.

| Việc | Lệnh |
| :--- | :--- |
| Tạo mạng dùng chung (chỉ lần đầu, có thể đã có do SmashCourt-BE tạo) | `docker network create smashcourt-network` |
| Khởi động / build lại chế độ vận hành | `docker compose up -d --build` |
| Trạng thái; log TADSS; log ngrok | `docker compose ps`; `docker compose logs -f tadss-runtime`; `docker compose logs ngrok` |
| Dừng chế độ vận hành (giữ image) | `docker compose down` |
| Kiểm tra cấu hình expose (không đụng Neon) | `bash tools/expose/verify-expose.sh` |
| Kiểm tra observability (không đụng Neon) | `bash tools/observability/verify-observability.sh` |
| Chạy chế độ đánh giá (bản nhanh; đầy đủ thêm `-e EVAL_MODE=full`) | `docker compose -p tadss-eval --profile evaluation run --rm --build evaluator` |
| Xem lại Grafana sau khi đánh giá xong | mở `http://127.0.0.1:3002` (stack đánh giá còn chạy tới khi dọn) |
| Log một service của stack đánh giá | `docker compose -p tadss-eval --profile evaluation logs eval-tadss-1` |
| Dọn stack đánh giá (container, volume, mạng) | `docker compose -p tadss-eval --profile evaluation down -v` |
| Unit test (không cần Docker) | `dotnet test tests/TADSS.UnitTests -c Release --no-build -nologo` |
| Integration test (cần Docker; tự tải PostgreSQL, Redis, Toxiproxy, ryuk) | `dotnet test tests/TADSS.IntegrationTests -c Release --no-build -nologo` |
| Xem dung lượng Docker | `docker system df` |
| Xóa container đã dừng | `docker container prune -f` |
| Xóa image không container nào dùng | `docker image prune -a -f` |
| Xóa build cache | `docker builder prune -a -f` |

**Bẫy khi dọn dẹp:**
- **Không dùng `docker system prune`.** Nó xóa cả mạng không có container nào gắn vào, kể cả `smashcourt-network` (mạng `external` mà `docker-compose.yml` của TADSS và SmashCourt-BE cần); sau đó `docker compose up` báo "declared as external, but could not be found" cho tới khi tạo lại mạng.
- `docker image prune -a -f` xóa mọi image không có container (kể cả đang dừng) dùng, **gồm image của SmashCourt-BE/AI khi các stack đó đang tắt** (phải build lại lần sau). Muốn giữ image của một stack thì để stack đó chạy khi dọn, hoặc chỉ dùng `docker image prune -f` (chỉ image treo).
- Image test (`postgres`, `redis`, `toxiproxy`, `ryuk`) và image của chế độ đánh giá tự tải lại khi cần, nên xóa được an toàn; chỉ tốn dữ liệu mạng khi tải lại.

### Lưu ý bảo mật

- **Secret nằm trong image local.** `appsettings.Development.json` (chuỗi kết nối Neon, Redis Cloud, mã băm API key) được copy vào image `tadss-runtime`, giống cách SmashCourt-BE làm. Image này chỉ dùng trên máy bạn: **không bao giờ** `docker push` hay chia sẻ image/layer lên registry, và đừng tải nó lên CI.
- **Container expose chạy `Development`.** Swagger (`/swagger`) công khai qua URL ngrok và validator `Tadss:Security` ở chế độ nới lỏng, nên API key trong file phải là khóa riêng thật; tuyệt đối không dùng `tadss-local-dev-key` khi expose.

Lưu ý khác:
- Trang theo dõi của ngrok ở `http://127.0.0.1:4040` hiển thị header của mọi request, kể cả `X-Api-Key`, và cho phép phát lại request. Chỉ dùng khi thử và dừng stack ngay sau đó.
- Nâng phiên bản ngrok hoặc Grafana bằng cách sửa tag của `image` trong `docker-compose.yml`.
- Cần Docker Compose >= 2.24.4 (`tools/expose/verify-expose.sh` kiểm tra).

### SmashCourt-BE gọi TADSS như thế nào

| BE chạy | TADSS chạy | `TadssConfig.BaseUrl` của BE |
| :--- | :--- | :--- |
| Trong Docker (cùng `smashcourt-network`) | Compose | `http://tadss-runtime:8080` |
| `dotnet run` trên máy host | Compose | `http://localhost:8081` |
| `dotnet run` trên máy host | `dotnet run` (không container) | `http://localhost:5296` |
| Trong Docker | `dotnet run` trên máy host | `http://host.docker.internal:5296` |
| Máy khác / demo | Compose (service ngrok) | URL ngrok hiện tại, xem `http://localhost:4040`; đổi mỗi lần khởi động lại (xem NG-4) |

### Cấu hình và biến

Cấu hình của `tadss-runtime` (không biến môi trường nào chứa secret):

| Mục trong `appsettings.Development.json` | Ý nghĩa |
| :--- | :--- |
| `ConnectionStrings:DefaultConnection` | Chuỗi kết nối PostgreSQL của Neon dạng Npgsql `key=value` với `SSL Mode=Require` (URI `postgresql://...` không được chấp nhận) |
| `Tadss:Cache:RedisConnectionString` | Kết nối Redis Cloud `host:port,user=...,password=...` |
| `Tadss:Security:ApiKeys` | `ClientId` `smashcourt-be` + SHA-256 hex của khóa riêng (không dùng mã băm của khóa local công khai) |

Biến trong `.env` (mẫu: [`.env.example`](.env.example)):

| Biến | Ý nghĩa |
| :--- | :--- |
| `NGROK_AUTHTOKEN` | Authtoken tài khoản ngrok (biến bắt buộc duy nhất của compose) |

Ở `Development` (compose) thiếu API key thì mọi `/api/*` trả 401; ở `Staging`/`Production` thiếu API key thì app không khởi động (log nêu `Tadss:Security`). Không ghi giá trị thật vào file nào trong repo.

### Tự kiểm tra cấu hình (không cần tài khoản ngrok)

```bash
bash tools/expose/verify-expose.sh
```

Script dựng project compose riêng `tadss-expose-check` (không đụng project `tadss` đang chạy của bạn; khóa ngẫu nhiên không in ra, không mở tunnel, chỉ khởi động `tadss-runtime` (không khởi động `ngrok`/`grafana`), không kết nối database ngoài) rồi in bảng kết quả và dọn container, volume, image. Mạng `smashcourt-network` chỉ được tạo nếu chưa có và chỉ bị xóa khi chính script tạo ra nó. Image dựng ra có thể chứa `appsettings.Development.json` thật (secret) nhưng script không đọc file đó (có thể không tồn tại trên CI): nó sinh một file override tạm (thư mục tạm, xóa khi thoát) thêm PostgreSQL/Redis dùng một lần và đặt biến môi trường `ConnectionStrings__DefaultConnection`, `Tadss__Cache__RedisConnectionString`, `Tadss__Security__ApiKeys__0__*` (mã băm của khóa ngẫu nhiên) cho `tadss-runtime` để ghi đè file json, đổi cổng publish sang một cổng trống và bật migration CHỈ cho database tạm đó, nên không bao giờ chạm Neon/Redis thật. Image build ra bị xóa khi script thoát. Kiểm theo danh mục NG: NG-1 (`docker compose config` hợp lệ với `NGROK_AUTHTOKEN` giả và `config --services` liệt kê đủ `tadss-runtime`, `ngrok`, `grafana`; `Development`, không mount file, migration `false`, compose không chứa biến secret), NG-2 (dòng `NG-2a`: `/health/ready` 200, POST không khóa 401 và có khóa 200 (Swagger được phục vụ ở Development nên không kiểm 404); dòng `NG-2b`: Staging không có khóa thì không khởi động, log nêu `Tadss:Security`, không lộ chuỗi kết nối), NG-3 (GET theo `decisionId` 200 đúng quyết định vừa ghi, DB có bản ghi), NG-5 (mọi cổng của compose, cả cấu hình lẫn đang publish, chỉ ở `127.0.0.1`), SEC-6 (user non-root). CI chạy script này ở job `expose-check`.

### NG-4: thử qua tunnel thật (người dùng tự chạy)

1. Sinh khóa và mã băm (mục Bảo mật) rồi đặt mã băm vào `appsettings.Development.json` (`Tadss:Security:ApiKeys`, ClientId `smashcourt-be`; cùng file có chuỗi kết nối Neon/Redis). Tạo `.env` từ `.env.example`, điền `NGROK_AUTHTOKEN`. Đảm bảo schema `tadss` trên Neon đã migrate.
2. Tạo mạng nếu chưa có (`docker network create smashcourt-network`) rồi chạy `docker compose up -d --build`; chờ `tadss-runtime` healthy.
3. Lấy URL công khai: `curl -s http://127.0.0.1:4040/api/tunnels` hoặc mở `http://127.0.0.1:4040` (giao diện kiểm tra của ngrok, chỉ loopback), hoặc `docker compose logs ngrok`. **Lưu ý bảo mật:** giao diện này lưu header của mọi request đi qua tunnel, kể cả `X-Api-Key`, ở dạng chữ thường (đọc được qua `http://127.0.0.1:4040/api/requests/http`) và cho phép phát lại request; vì vậy chỉ nghe loopback, và nên dừng stack ngay sau khi thử NG-4 (bước 5).
4. Thử:

```bash
URL=https://<domain-ngrok>; KEY=<khóa ở bước 1>
curl -sS -H "ngrok-skip-browser-warning: true" "$URL/health/ready"                        # 200
hex=$(openssl rand -hex 16); RES="${hex:0:8}-${hex:8:4}-4${hex:13:3}-8${hex:17:3}-${hex:20:12}"
if date -u -d '+7 days' >/dev/null 2>&1; then   # GNU date (Linux, Git Bash)
  START=$(date -u -d '+7 days' +%Y-%m-%dT%H:00:00Z); END=$(date -u -d '+7 days +2 hours' +%Y-%m-%dT%H:00:00Z)
else                                              # BSD date (macOS)
  START=$(date -u -v+7d +%Y-%m-%dT%H:00:00Z); END=$(date -u -v+7d -v+2H +%Y-%m-%dT%H:00:00Z)
fi
body=$(sed -e "s/__RESERVATION_ID__/$RES/" -e "s/__REQUESTED_AT__/$(date -u +%Y-%m-%dT%H:%M:%SZ)/" \
  -e "s/__START_TIME__/$START/" -e "s/__END_TIME__/$END/" tools/expose/sample-decision-request.json)
curl -sS -X POST "$URL/api/v2/decisions" -H "Content-Type: application/json" -H "X-Api-Key: $KEY" \
  -H "X-Idempotency-Key: $RES" -H "ngrok-skip-browser-warning: true" -d "$body"            # 200 + decisionId
curl -sS -X POST "$URL/api/v2/decisions" -H "Content-Type: application/json" -d "$body"   # 401 UNAUTHORIZED
```

   Rồi `GET $URL/api/v2/decisions/<decisionId>` với `X-Api-Key` (200) và kiểm tra request xuất hiện ở `http://127.0.0.1:4040`.
5. Dừng: `docker compose down`.

Phía SmashCourt-BE: ghi chú triển khai ở [`docs/contracts/INTEGRATION_SPEC.md`](docs/contracts/INTEGRATION_SPEC.md) mục 4.5.

---

## Tài liệu

Bản đồ tài liệu (mỗi chủ đề một file sở hữu): Mục III của [`docs/planning/THESIS_DEVELOPMENT_PLAYBOOK.md`](docs/planning/THESIS_DEVELOPMENT_PLAYBOOK.md).

| Cần tìm | Tài liệu |
| :--- | :--- |
| Đề cương | [`docs/planning/PROPOSAL_DO_AN_2.md`](docs/planning/PROPOSAL_DO_AN_2.md) |
| Lịch trình, phase, gate | [`docs/planning/MASTER_EXECUTION_PLAN.md`](docs/planning/MASTER_EXECUTION_PLAN.md) |
| Tổng quan & sơ đồ | [`docs/PROJECT_BLUEPRINT_AND_SYSTEM_OVERVIEW.md`](docs/PROJECT_BLUEPRINT_AND_SYSTEM_OVERVIEW.md) |
| Contract v2 & tích hợp SmashCourt | [`docs/contracts/INTEGRATION_SPEC.md`](docs/contracts/INTEGRATION_SPEC.md) |
| Danh mục kiểm thử & KPI | [`docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) |

---

## License

Phát triển cho mục đích học thuật trong khuôn khổ Đồ án 2 (và mở rộng cho Khóa luận tốt nghiệp).
