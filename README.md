# TADSS Runtime

**Trustworthy Adaptive Decision Support System — Decision Runtime Engine** (Đồ án 2)

TADSS là nền tảng runtime độc lập ra quyết định cho hệ thống đặt chỗ dịch vụ. Hệ thống nhận yêu cầu quyết định (ví dụ mức đặt cọc cho một booking), chọn strategy phù hợp, thực thi strategy trong pipeline chịu lỗi Polly v8, lưu audit và cho phép người quản lý phê duyệt hoặc ghi đè. Ứng dụng đối chứng là **SmashCourt** (Đồ án 1). Chiến lược học máy (TADS) thuộc Khóa luận và nằm ngoài phạm vi repo ở Đồ án 2.

> **Trạng thái (04/10/2026):** Phase 1–4 hoàn thành trên contract v1; **Phase R — Re-baseline** đã đóng Gate MR (01/10/2026). Contract v1 đã gỡ hoàn toàn khỏi mã và tài liệu, tra lại qua tag `phase4-baseline`. **Phase 5** hiện thực contract v2 và chuyển toàn bộ kiểm thử sang hạ tầng thật. **Gói 5.1 (nền móng kiểm thử), 5.2a–5.2d (SDK v2, lõi runtime v2, Clean Architecture nhiều project) và 5.3 (quản trị và bảo mật) đã xong:** năm endpoint `POST /api/v2/decisions`, `GET /api/v2/decisions/{decisionId}`, `GET /api/v2/decisions/pending-approvals`, `POST /api/v2/decisions/{decisionId}/override`, `POST /api/v2/decisions/{decisionId}/outcome`; xác thực `X-Api-Key`, giới hạn tốc độ theo client, giới hạn kích thước body, security headers, health `live`/`ready`, container non-root, lock file và quét lỗ hổng; cấu hình expose qua ngrok (`docker-compose.ngrok.yml`, `docker-compose.neon.yml`); migration `DecisionPersistenceV2` (chưa áp lên Neon); integration test chạy trên Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy); hiện có **1517 unit + 188 integration test**. Chưa có: định tuyến strategy theo request và shadow (gói 5.4), observability (gói 5.5). Kế hoạch: [`docs/planning/MASTER_EXECUTION_PLAN.md`](docs/planning/MASTER_EXECUTION_PLAN.md).

---

## Nghiệp vụ cốt lõi

- TADSS xác định được mức cọc → `APPROVED` kèm số tiền cọc.
- Không xác định được hoặc strategy lỗi → `NEED_APPROVAL`, Manager duyệt kèm mức cọc hoặc từ chối; không bao giờ tự duyệt.
- SmashCourt áp cọc 100% khi tắt feature flag hoặc TADSS không phản hồi.

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
dotnet test  TADSS.slnx -c Release --no-build -m:1       # 1517 unit + 188 integration; integration cần Docker Desktop đang chạy
dotnet run --project src/TADSS.Runtime.Api               # http://localhost:5296/swagger (mọi /api/* cần X-Api-Key, xem mục Bảo mật)
```

**Integration test cần Docker Desktop** (Testcontainers tự dựng PostgreSQL 16.15, Redis 7.4, Toxiproxy 2.12.0; mỗi test dùng một database riêng, xóa sau khi chạy). Chỉ chạy unit test (không cần Docker): `dotnet test tests/TADSS.UnitTests -c Release --no-build`.

Lệnh EF Core (chỉ đọc, không kết nối database):

```bash
ASPNETCORE_ENVIRONMENT=Testing ConnectionStrings__DefaultConnection="Host=localhost;Database=x" dotnet ef migrations list --no-connect --project src/TADSS.Infrastructure --startup-project src/TADSS.Runtime.Api
```

Biến `ConnectionStrings__DefaultConnection` ở trên là chuỗi giả (không phải secret, không mở kết nối): công cụ EF khởi động host ở design-time, mà môi trường `Testing` không nạp `appsettings.Development.json` và validator từ chối chuỗi giữ chỗ trong `appsettings.json`.

Cấu hình kết nối PostgreSQL/Redis và API key đặt trong user-secrets hoặc `appsettings.Development.json` (không commit). **Chạy `dotnet run` cần có ít nhất một API key** trong `Tadss:Security:ApiKeys` (xem mục Bảo mật): không có thì app vẫn khởi động ở `Development` nhưng mọi request `/api/*` bị 401. `appsettings.json` chỉ chứa giá trị mẫu. Chuỗi kết nối PostgreSQL (`ConnectionStrings:DefaultConnection`, bắt buộc ở mọi môi trường, thiếu hoặc sai thì app không khởi động) phải ở dạng Npgsql `key=value` (ví dụ `Host=...;Port=5432;Database=...;Username=...;Password=...`); URI dạng `postgresql://...` (kiểu Neon hay cấp) **không** được chấp nhận, cần đổi sang dạng `key=value`.

### Môi trường E-LOCAL (docker compose)

`docker-compose.yml` là môi trường tự chứa (self-contained): không cần cài PostgreSQL/Redis trên máy. TADSS kết nối PostgreSQL/Redis **qua Toxiproxy** để kịch bản lỗi (FI) tiêm được mà không phải khởi động lại.

```bash
docker compose up -d --build     # dựng toàn bộ môi trường
docker compose down -v           # dừng và xóa dữ liệu (volume)
```

| Dịch vụ | Cổng trên máy host | Ghi chú |
| :--- | :---: | :--- |
| `tadss-runtime` | `8081` | Swagger: `http://localhost:8081/swagger` (bấm Authorize, nhập `tadss-local-dev-key`); healthcheck của container gọi `/health/live` |
| `tadss-postgres` | `5433` | PostgreSQL 16.15, database `tadss` |
| `tadss-redis` | `6380` | Redis 7.4 |
| `toxiproxy` | `8474` | API điều khiển toxic; proxy nội bộ `15432` (PostgreSQL), `16379` (Redis) |
| `otel-lgtm` (Grafana) | `3001` | OTLP gRPC `4317`, OTLP HTTP `4318` |

Cờ `Tadss:Database:ApplyMigrationsOnStartup` mặc định `false`; chỉ compose đặt `true` (biến `Tadss__Database__ApplyMigrationsOnStartup`) nên migration tự áp dụng khi container khởi động.

Mọi cổng host và image đọc từ biến môi trường (mặc định như bảng trên) để chạy song song với stack khác: `TADSS_PORT`, `TADSS_PG_PORT`, `TADSS_REDIS_PORT`, `TADSS_TOXIPROXY_PORT`, `TADSS_GRAFANA_PORT`, `TADSS_POSTGRES_IMAGE`, `TADSS_REDIS_IMAGE`, `TADSS_TOXIPROXY_IMAGE`, `TADSS_GRAFANA_IMAGE` (xem [`.env.example`](.env.example)). Không có `.env` thì compose chạy đúng môi trường E-LOCAL mặc định.

**Khóa E-LOCAL công khai.** Để k6 và kịch bản FI chạy được ngay, `docker-compose.yml` cấp sẵn một API key **công khai**: khóa `tadss-local-dev-key` (client `local`, mã băm `0472e99d9ccd889b6e6781cf0920a9b39910c1c430393d62839465c4901a6268`, đặt qua `TADSS_API_KEY_SHA256`). Ai đọc README này cũng biết khóa đó, nên chỉ dùng cho máy cá nhân ở môi trường `Development` và **không bao giờ** expose ra mạng ngoài với khóa này. `docker-compose.yml` bind cổng host trên mọi interface (như thường lệ của Docker), nên chỉ dùng trên mạng tin cậy. Ngoài `Development`/`Testing`, validator của `Tadss:Security` từ chối mã băm này và app không khởi động; `docker-compose.ngrok.yml` còn bắt buộc đặt `TADSS_API_KEY_SHA256` riêng.

---

## Bảo mật

Mọi request `/api/*` phải mang `X-Api-Key`. Thứ tự middleware (khai báo tại `Composition/RequestPipelineExtensions.UseTadssRequestPipeline`, khóa bằng test UT-14): forwarded headers (chỉ khi `TrustForwardedHeaders`) → correlation id → exception handler → security headers + HSTS → request logging → https redirection → giới hạn kích thước → Swagger (chỉ `Development`) → xác thực API key → rate limiter → routing. Mã lỗi và contract: [`docs/contracts/INTEGRATION_SPEC.md`](docs/contracts/INTEGRATION_SPEC.md) mục 3.2 và 3.8. Các phản hồi từ chối do middleware tạo (401, 413, 429) và mọi lỗi do exception (qua `GlobalExceptionHandler`) đi qua một bộ ghi `ProblemResponseWriter` nên cùng dạng `application/problem+json`; ngoại lệ là 415 (do MVC), 404/405 của framework và một số lỗi mức Kestrel, không có `code` (INTEGRATION_SPEC §3.8).

### Cấu hình `Tadss:Security:*`

Khai báo trong `appsettings.json`, ghi đè bằng biến môi trường dạng `Tadss__Security__...`; có validator và `ValidateOnStart` nên giá trị sai làm app không khởi động (thông báo chỉ nêu tên khóa, không in mã băm).

| Khóa | Mặc định | Ý nghĩa |
| :--- | :---: | :--- |
| `ApiKeys` | `[]` | Danh sách `{ ClientId, KeySha256 }`. `ClientId` 1..64 ký tự `A-Za-z0-9._-`, duy nhất (không phân biệt hoa thường), không dùng `health`/`anonymous`. `KeySha256` là SHA-256 dạng hex 64 ký tự của khóa, duy nhất. Rỗng thì mọi `/api/*` bị 401 (`Development`/`Testing` vẫn khởi động và ghi Warning); môi trường khác thì app **không khởi động** và cũng từ chối mã băm của khóa E-LOCAL công khai |
| `MaxRequestBodyBytes` | `65536` | Trần kích thước body (1024..1048576 byte). Vượt thì 413 `PAYLOAD_TOO_LARGE`, kiểm tra trước xác thực |
| `RateLimiting:PermitLimit` | `100` | Dung lượng xô token của mỗi client (1..100000), cũng là số request liên tiếp tối đa khi xô đầy |
| `RateLimiting:TokensPerPeriod` | `100` | Số token nạp lại mỗi chu kỳ (1..`PermitLimit`) |
| `RateLimiting:ReplenishmentPeriodSeconds` | `1` | Chu kỳ nạp token (1..3600 giây) |
| `TrustForwardedHeaders` | `false` | Tin `X-Forwarded-For/Proto/Host`. Chỉ bật khi chỉ reverse proxy (ngrok) chạm được app; `docker-compose.ngrok.yml` đặt `true` |

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
- Docker/expose: biến `TADSS_API_KEY_SHA256` trong `.env` (xem mục Expose qua ngrok).

Mỗi client một phần tử (chỉ số `0`, `1`, ...); thu hồi khóa bằng cách xóa phần tử rồi khởi động lại.

### Health, giới hạn tốc độ, header, log

- **Health:** `/health/live` (không kiểm tra gì, 200 khi tiến trình còn chạy) và `/health/ready` (PostgreSQL bắt buộc: lỗi → 503; Redis: lỗi → 200 `Degraded` vì cache có fallback memory). Cả hai miễn `X-Api-Key` ở mọi môi trường, không bị rate limit, body chỉ là chữ trạng thái (không lộ chuỗi kết nối). Không còn endpoint `/health` gộp.
- **Rate limit** (token bucket, một xô cho mỗi `ClientId`; hết token là 429 `RATE_LIMITED` kèm `Retry-After`): tính **theo từng instance** của app, không chia sẻ giữa nhiều instance (đánh giá khi scale thuộc gói 5.5). Mặc định cho phép bùng nổ 100 request và nạp 100 request mỗi giây cho một client. Để k6 tải cao hơn, tăng bằng biến môi trường của tiến trình app: `Tadss__Security__RateLimiting__PermitLimit`, `Tadss__Security__RateLimiting__TokensPerPeriod`, `Tadss__Security__RateLimiting__ReplenishmentPeriodSeconds` (với compose: thêm vào `environment` của `tadss-runtime` trong một file override cục bộ).
- **Header bảo mật** trên mọi response: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`; `Cache-Control: no-store` cho `/api/*`; `Strict-Transport-Security` với request https ngoài `Development`/`Testing`; Kestrel không gửi header `Server`.
- **Log:** khóa `X-Api-Key` và mã băm không bao giờ vào log. `ClientId` của khóa hợp lệ có ở thuộc tính log scope (thấy trong `{Properties:j}` của log phát sinh trong controller/service) và ở tag `tadss.client_id` của trace. Dòng log hoàn tất request của Serilog **không** mang `ClientId` vì bước request logging đứng trước bước xác thực. Ngoài ra `UseSerilogRequestLogging` nằm **trong** exception handler nên phản hồi do exception tạo ra (400, 404, 409, 503, 504) bị ghi là "responded 500" ở mức Error; 401/413/429 do middleware tạo thì ghi đúng. Đây là hạn chế đã biết, xử lý ở gói 5.4/5.5 vì sửa sẽ đổi thứ tự pipeline (F-09).

### Chuỗi cung ứng và container

- Mọi project có `packages.lock.json` (`RestorePackagesWithLockFile` trong `Directory.Build.props`); CI và `Dockerfile` restore bằng `--locked-mode`, nên lock file lệch đồ thị phụ thuộc thì build đỏ. **Dependabot** (NuGet, GitHub Actions, Docker; mỗi tuần, nhánh `develop`) có thể mở PR nâng package mà không cập nhật `packages.lock.json`: khi đó CI đỏ ở bước restore cho tới khi chạy `dotnet restore --force-evaluate` rồi commit các `packages.lock.json` đã đổi vào PR.
- CI chạy `tools/ci/check-vulnerable-packages.sh` (`dotnet list package --vulnerable --include-transitive` cho cả solution): mức High/Critical hoặc không quét được thì job đỏ. `System.Net.Http` 4.3.4 và `System.Text.RegularExpressions` 4.3.1 được ghim bắc cầu trong `Directory.Packages.props` (`CentralPackageTransitivePinningEnabled`) vì xunit 2.5.3 kéo bản 4.3.0 có lỗ hổng; chỉ ảnh hưởng project test, gỡ khi nâng xunit.
- Image chạy bằng user non-root (`USER $APP_UID`); `.dockerignore` loại file bí mật; CI kiểm `Config.User` của image.

---

## Expose qua ngrok

Mục đích: cho SmashCourt-BE ở máy khác (hoặc demo/pilot) gọi TADSS đang chạy trong Docker qua một URL công khai của ngrok. Cấu hình expose **luôn** chạy `Staging`: Swagger tắt, API key bắt buộc (khóa E-LOCAL công khai bị từ chối), `TrustForwardedHeaders=true`, mọi cổng host bind `127.0.0.1`, image ngrok ghim `ngrok/ngrok:3.39.11-alpine@sha256:...`. Khi BE và TADSS cùng Docker network (Phase 7) thì không cần ngrok.

### Biến môi trường (đặt trong `.env`, không commit; mẫu: [`.env.example`](.env.example))

Compose chỉ đọc `NGROK_AUTHTOKEN` và các biến dưới đây từ môi trường hoặc `.env`; không ghi giá trị thật vào file nào trong repo.

| Biến | Bắt buộc | Ý nghĩa |
| :--- | :---: | :--- |
| `NGROK_AUTHTOKEN` | ngrok | Authtoken tài khoản ngrok |
| `NGROK_DOMAIN` | không | Domain tĩnh của ngrok; để trống thì ngrok cấp domain ngẫu nhiên, đổi mỗi lần chạy |
| `TADSS_API_KEY_SHA256` | ngrok, neon | SHA-256 hex của khóa riêng (không dùng mã băm của khóa E-LOCAL) |
| `TADSS_API_CLIENT_ID` | không | `ClientId` của khóa (mặc định `smashcourt-be`) |
| `TADSS_DB_CONNECTION` | neon | Chuỗi kết nối PostgreSQL ngoài dạng Npgsql `key=value` với `SSL Mode=Require` (URI `postgresql://...` không được chấp nhận) |
| `TADSS_REDIS_CONNECTION` | không (neon) | Redis ngoài `host:port,user=...,password=...`; trống thì dùng `tadss-redis` của compose |
| `TADSS_PG_USER`, `TADSS_PG_PASSWORD` | không | Tài khoản PostgreSQL local của E-LOCAL |
| `TADSS_PORT`, `TADSS_PG_PORT`, `TADSS_REDIS_PORT`, `TADSS_TOXIPROXY_PORT`, `TADSS_GRAFANA_PORT` | không | Cổng host (mặc định `8081`, `5433`, `6380`, `8474`, `3001`) |
| `TADSS_*_IMAGE`, `TADSS_NGROK_IMAGE` | không | Image/tag (mặc định như `.env.example`) |

### Hai chế độ database

| Chế độ | Lệnh | Database | Migration |
| :--- | :--- | :--- | :--- |
| A (mặc định) | `docker compose -f docker-compose.yml -f docker-compose.ngrok.yml --profile ngrok up -d --build` | PostgreSQL trong compose (volume bền, qua Toxiproxy) | Tự áp bằng cờ `ApplyMigrationsOnStartup` (chỉ DB local) |
| B (Neon) | `docker compose -f docker-compose.yml -f docker-compose.ngrok.yml -f docker-compose.neon.yml --profile ngrok up -d --build tadss-runtime ngrok` | PostgreSQL ngoài từ `TADSS_DB_CONNECTION` | **Không bao giờ tự áp** (cờ bị ép `false`); schema `tadss` phải đã migrate, người dùng tự chạy `database update` |

`docker-compose.neon.yml` dùng riêng với `docker-compose.yml` (không ngrok) cũng được và an toàn: nó ép `Staging`, bắt buộc `TADSS_API_KEY_SHA256` và bind cổng host của `tadss-runtime` vào `127.0.0.1`. Nhưng khi chỉ có base + neon, cổng PostgreSQL (5433), Redis (6380), Toxiproxy (8474), Grafana (3001) và OTLP (4317/4318) vẫn bind mọi interface như E-LOCAL; chỉ `docker-compose.ngrok.yml` đưa mọi cổng host về `127.0.0.1`. Cần Docker Compose >= 2.24.4 (các file override dùng `!override`).

### Tự kiểm tra cấu hình (không cần tài khoản ngrok)

```bash
bash tools/expose/verify-expose.sh
```

Script dựng project compose riêng (`tadss-expose-check`, cổng `18081`...; khóa ngẫu nhiên không in ra, không mở tunnel, không kết nối database ngoài) rồi in bảng kết quả và dọn container, volume, network, image. Kiểm theo danh mục NG: NG-1 (`docker compose config` hợp lệ cho ngrok, ngrok + neon, neon riêng; thiếu biến bắt buộc bị từ chối), NG-2 (dòng `NG-2a`: `/health/ready` 200, POST không khóa 401 và có khóa 200, Swagger 404; dòng `NG-2b`: Staging không có khóa thì không khởi động, log nêu `Tadss:Security`, không lộ chuỗi kết nối), NG-3 (GET theo `decisionId` 200 đúng quyết định vừa ghi, DB có bản ghi), NG-5 (mọi cổng publish chỉ ở `127.0.0.1`), SEC-6 (user non-root). CI chạy script này ở job `expose-check`.

### NG-4: thử qua tunnel thật (người dùng tự chạy)

1. Sinh khóa và mã băm (mục Bảo mật). Tạo `.env` từ `.env.example`, điền `NGROK_AUTHTOKEN` và `TADSS_API_KEY_SHA256` (và `NGROK_DOMAIN` nếu có domain tĩnh; với chế độ B thêm `TADSS_DB_CONNECTION`).
2. Chạy lệnh của chế độ A (hoặc B) ở bảng trên; chờ `tadss-runtime` healthy.
3. Lấy URL công khai: `curl -s http://127.0.0.1:4040/api/tunnels` hoặc mở `http://127.0.0.1:4040` (giao diện kiểm tra của ngrok, chỉ loopback), hoặc dùng `https://$NGROK_DOMAIN`. **Lưu ý bảo mật:** giao diện này lưu header của mọi request đi qua tunnel, kể cả `X-Api-Key`, ở dạng chữ thường (đọc được qua `http://127.0.0.1:4040/api/requests/http`) và cho phép phát lại request; vì vậy chỉ nghe loopback, và nên dừng stack ngay sau khi thử NG-4 (bước 5).
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
5. Dừng: thêm `down` (kèm `-v` để xóa dữ liệu E-LOCAL) vào đúng bộ file `-f ... --profile ngrok` đã dùng.

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
