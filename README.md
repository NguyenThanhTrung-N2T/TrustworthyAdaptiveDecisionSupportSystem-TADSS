# TADSS Runtime

**Trustworthy Adaptive Decision Support System — Decision Runtime Engine** (Đồ án 2)

TADSS là nền tảng runtime độc lập ra quyết định cho hệ thống đặt chỗ dịch vụ. Hệ thống nhận yêu cầu quyết định (ví dụ mức đặt cọc cho một booking), chọn strategy phù hợp, thực thi strategy trong pipeline chịu lỗi Polly v8, lưu audit và cho phép người quản lý phê duyệt hoặc ghi đè. Ứng dụng đối chứng là **SmashCourt** (Đồ án 1). Chiến lược học máy (TADS) thuộc Khóa luận và nằm ngoài phạm vi repo ở Đồ án 2.

> **Trạng thái (12/10/2026):** Phase 1–4 hoàn thành trên contract v1. **Phase 5** (W05–W08, 05/10 – 01/11/2026 theo kế hoạch v2.1; bắt đầu thực tế 02/10) mở đầu bằng bước 0 — rà soát và điều chỉnh kế hoạch (re-baseline v2.0, Gate MR đạt 01/10/2026); contract v1 đã gỡ hoàn toàn khỏi mã và tài liệu, tra lại qua tag `phase4-baseline`. Phase 5 hiện thực contract v2 và chuyển toàn bộ kiểm thử sang hạ tầng thật. **Gói 5.1 (nền móng kiểm thử), 5.2a–5.2d (SDK v2, lõi runtime v2, Clean Architecture nhiều project), 5.3 (quản trị và bảo mật), 5.4 (runtime thích ứng), 5.5 (observability, chế độ đánh giá một lệnh) và phần mã của 5.6 (replay dữ liệu SmashCourt, adapter Hotel Booking Demand, hướng dẫn chạy từng bước) đã xong:** năm endpoint `POST /api/v2/decisions`, `GET /api/v2/decisions/{decisionId}`, `GET /api/v2/decisions/pending-approvals`, `POST /api/v2/decisions/{decisionId}/override`, `POST /api/v2/decisions/{decisionId}/outcome`; xác thực `X-Api-Key`, giới hạn tốc độ theo client, giới hạn kích thước body, security headers, health `live`/`ready`, container non-root, lock file và quét lỗ hổng; cấu hình chạy Docker và expose qua ngrok (một file `docker-compose.yml`, một lệnh `docker compose up -d --build` chạy TADSS + ngrok + Grafana và tự chạy một lượt đánh giá, D-14); migration `DecisionPersistenceV2` (đã áp lên Neon 10/10/2026); integration test chạy trên Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy); định tuyến strategy theo từng request (`Tadss:Routing`, nạp lại nóng), shadow mode, pipeline và circuit breaker riêng cho từng strategy, cách ly strategy không hợp tác, circuit breaker Redis (gói 5.4); hiện có **1874 unit + 240 integration test** (240 là số của lần chạy trước; chưa chạy lại 12/10/2026 vì Docker tắt) và **504 test Python** (`tools/evaluation` 240, `tools/evidence` 230, `tools/docs` 34; máy không có Docker/k6 thì 3 test của `tools/evaluation` được bỏ qua, còn 501 đạt). Bản nhanh của chế độ đánh giá đạt mọi tiêu chí áp dụng được (lượt `EV-20261011-EVAL-quick-r1`, ~28 phút). Gói 5.6 còn chờ người dùng xuất CSV SmashCourt và xác nhận giấy phép dataset khách sạn (xem [guide](docs/guides/EVALUATION_RUN_GUIDE.md)). **Phần mã và tài liệu của gói 5.7 đã xong:** contract v2 đã đóng băng ở `2.0.0` (11/10/2026), kiểm tra tài liệu tự động DOC-1…4 (`python tools/docs/check_docs.py`, bảng kiểm kê tài liệu ở mục [Tài liệu](#tài-liệu) của README này, bảng cấu hình [`docs/guides/CONFIGURATION_REFERENCE.md`](docs/guides/CONFIGURATION_REFERENCE.md)). **Gate M5 đạt 18/10/2026** kèm một sai lệch đã giải thích (lượt đầy đủ `EV-20261017-EVAL-full-r1`, ≈ 3 giờ 25 phút; 0,04–0,11 % response 504 ở bước tải do máy bận; báo cáo `spec/PHASE5_REPORT_5.7.md`). Sau Gate: strategy mặc định 2.1.0 từ chối khách lặp lại việc giữ chỗ không thanh toán; review API BE ↔ FE (T5.29). Còn: chạy lại lượt đầy đủ trên 2.1.0 khi máy rảnh rồi lưu Evidence Pack. **Phase 6** (tích hợp cọc vào SmashCourt, 25/10/2026): SmashCourt-BE xong gói 6A-1…6A-4, 6A-5 plan đã chốt 27/10 (chưa code), FE chưa bắt đầu; TADSS không đổi mã; đặc tả [`spec/PHASE6_DESIGN_SPEC.md`](spec/PHASE6_DESIGN_SPEC.md). Kế hoạch: [`docs/planning/MASTER_EXECUTION_PLAN.md`](docs/planning/MASTER_EXECUTION_PLAN.md).

**Tổng hợp Phase 5** (để review, trình bày với hội đồng và tra lệnh chạy): [`docs/PHASE5_SUMMARY.md`](docs/PHASE5_SUMMARY.md). Khi khác tài liệu sở hữu, tài liệu sở hữu thắng.

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
| `src/Shared.DecisionContractSDK` | Hợp đồng dùng chung v2 (`2.0.0`, đã đóng băng trong gói 5.7 (11/10/2026), trước lượt đánh giá đầy đủ; Gate M5 chờ lượt đầy đủ; đổi public API ⇒ phiên bản lớn mới, trừ thay đổi nội bộ được phép ở `INTEGRATION_SPEC.md` Mục 6): `Reservation` (rich domain model), DTO, enum, `StrategyDecision`/`DecisionContext`, `IReservationDecisionStrategy` | Chỉ BCL |
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
dotnet test  TADSS.slnx -c Release --no-build -m:1       # 1874 unit + 240 integration (240 đạt ở bước (a) của `quick-r2`, 15/10/2026); integration cần Docker Desktop đang chạy
python tools/docs/check_docs.py                         # DOC-1…3 (kiểm kê, thuật ngữ đã gỡ, link nội bộ); DOC-4 là unit test .NET
python -m pytest -q tools/evaluation                    # chạy riêng từng bộ: tools/evaluation, tools/evidence, tools/docs, tools/database
python tools/database/schema_snapshot.py --target main --target tadss-dev   # chỉ đọc (chuỗi kết nối trong data/private/db-urls.env): dump schema các branch Neon, so với docs/database/schema_smashcourt.sql, kiểm script Phase 6 và migration tadss; kết quả ở data/private/schema-snapshots/
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

`docker-compose.yml` là file compose **duy nhất**, chỉ dùng để chạy TADSS trong Docker và expose qua ngrok (`tadss-runtime` chạy ở `Development` như SmashCourt-BE, cùng `ngrok` và `grafana`, không profile: một lệnh `docker compose up -d --build` chạy cả ba; database Neon, Redis Cloud và API key đọc từ `appsettings.Development.json` được copy vào image local), xem mục Chạy bằng Docker và expose qua ngrok. Chế độ đánh giá nằm cùng file, cũng không profile: các service `eval-*` và `evaluator` khởi động cùng ba service trên bằng một lệnh `docker compose up -d --build` (D-14), xem "Chế độ đánh giá một lệnh" bên dưới.

**Ba chế độ chạy (D-12; D-14: chế độ vận hành và đánh giá chung một lệnh `up`):**

| Chế độ | Lệnh | Dùng để | Trạng thái |
| :--- | :--- | :--- | :--- |
| Dev hằng ngày | `dotnet run --project src/TADSS.Runtime.Api` | Neon + Redis Cloud qua `appsettings.Development.json`; chỉ app + Swagger để kiểm tra luồng, không Docker, không Grafana | Đã có |
| Vận hành / demo **+ đánh giá** | `docker compose up -d --build` | TADSS + ngrok + Grafana (`grafana/otel-lgtm`, `localhost:3001`), **và** tự chạy một lượt đánh giá (dòng dưới) | Đã có (D-11, D-14) |
| Đánh giá cho hội đồng | Chạy kèm `docker compose up -d --build` (D-14); chạy lại riêng: `bash tools/evaluation/run.sh` | Stack cô lập, **không bao giờ chạm Neon**: TADSS (Release) × 2 sau reverse proxy + PostgreSQL 16 + Redis 7 + Toxiproxy + Grafana + container evaluator chạy toàn bộ test, k6, sự cố dưới tải, Schemathesis, Stryker rồi sinh `evidence/<run_id>/` (`report.html`, dữ liệu thô, `manifest.json`); bản nhanh ~30 phút, bản đầy đủ ~2,8 giờ; chạy trên một máy nên là số liệu phòng lab | Đã có (gói 5.5, mở rộng ở 5.6), xem mục "Chế độ đánh giá một lệnh" và [hướng dẫn chạy](docs/guides/EVALUATION_RUN_GUIDE.md) |

> **Một lệnh chạy hết (D-14, 10/10/2026).** `docker compose up -d --build` khởi động app + ngrok + Grafana **và** stack đánh giá; evaluator tự chạy các bước a…f (bản nhanh, ~30 phút) rồi dừng, kết quả ở `evidence/<run_id>/report.html` (theo dõi: `docker compose logs -f evaluator`). Muốn bản đầy đủ: `EVAL_MODE=full docker compose up -d --build` (PowerShell: `$env:EVAL_MODE="full"; docker compose up -d --build`). Mỗi lần `up` lại là chạy lại một lượt đánh giá. Stack đánh giá vẫn cô lập: mạng riêng, PostgreSQL/Redis dùng một lần, không chạm Neon; service vận hành không phụ thuộc nó. Đổi lại: cần thêm RAM (≥ 8 GB cho Docker) và lúc đánh giá chạy, TADSS vận hành cùng máy làm số liệu nhiễu thêm một chút (ghi vào giới hạn).

Test tự động trên CI vẫn dùng Testcontainers (`dotnet test`). Chi tiết: quyết định D-12 trong [`spec/PHASE5_DESIGN_SPEC.md`](spec/PHASE5_DESIGN_SPEC.md) và Mục 6.1 của [`docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).

**Khóa local công khai.** Khóa `tadss-local-dev-key` (client `local`, mã băm `0472e99d9ccd889b6e6781cf0920a9b39910c1c430393d62839465c4901a6268`) chỉ dành cho môi trường `Development` qua `appsettings.Development.json` (đặt mã băm này vào `Tadss:Security:ApiKeys`). Ai đọc README này cũng biết khóa đó nên **không bao giờ** expose ra mạng ngoài với khóa này: ngoài `Development`/`Testing`, validator của `Tadss:Security` từ chối mã băm này và app không khởi động.

---

## Bảo mật

Mọi request `/api/*` phải mang `X-Api-Key`. Thứ tự middleware (khai báo tại `Composition/RequestPipelineExtensions.UseTadssRequestPipeline`, khóa bằng test UT-14): forwarded headers (chỉ khi `TrustForwardedHeaders`) → correlation id → request logging → request metrics → exception handler → security headers + HSTS → https redirection → giới hạn kích thước → Swagger (chỉ `Development`) → xác thực API key → rate limiter → routing. Mã lỗi và contract: [`docs/contracts/INTEGRATION_SPEC.md`](docs/contracts/INTEGRATION_SPEC.md) mục 3.2 và 3.8. Các phản hồi từ chối do middleware tạo (401, 413, 429) và mọi lỗi do exception (qua `GlobalExceptionHandler`) đi qua một bộ ghi `ProblemResponseWriter` nên cùng dạng `application/problem+json`; ngoại lệ là 415 (do MVC), 404/405 của framework và một số lỗi mức Kestrel, không có `code` (INTEGRATION_SPEC §3.8).

### Cấu hình `Tadss:Security:*`

Khai báo trong `appsettings.json`, ghi đè bằng biến môi trường dạng `Tadss__Security__...`; có validator và `ValidateOnStart` nên giá trị sai làm app không khởi động (thông báo chỉ nêu tên khóa, không in mã băm). Danh sách khóa, mặc định và ràng buộc: [`docs/guides/CONFIGURATION_REFERENCE.md`](docs/guides/CONFIGURATION_REFERENCE.md#tadsssecurity). Khi `ApiKeys` rỗng: mọi `/api/*` bị 401 ở `Development`/`Testing`, các môi trường khác không khởi động.

### Tạo API key và mã băm

Khóa là chuỗi bất kỳ do bạn sinh; app chỉ lưu SHA-256 của khóa. Đưa **khóa** cho client (SmashCourt-BE), đặt **mã băm** vào cấu hình TADSS.
Ô `KeySha256` **chỉ nhận mã băm** (64 ký tự hex): dán key gốc vào đó thì app không khởi động (`KeySha256 phải là chuỗi hex 64 ký tự`).

**Cách khuyến nghị: để script làm** (không phải băm tay):

```bash
python tools/security/hash_api_key.py           # lỡ dán key gốc vào KeySha256: tự băm, chuyển key gốc vào .env (TADSS_API_KEY_<CLIENTID>)
python tools/security/hash_api_key.py --new     # sinh key mới (đổi key) cho client đầu tiên; thêm --client <ClientId> để chọn client
docker compose up -d --build                    # build lại để image dùng cấu hình mới
```

Script chỉ thay đúng giá trị `KeySha256` trong `appsettings.Development.json` (giữ nguyên phần còn lại), không bao giờ in key hay mã băm; chạy lại khi đã là mã băm thì bỏ qua.
Key gốc nằm trong `.env` của repo, biến `TADSS_API_KEY_<CLIENTID>` (ví dụ `smashcourt-be` → `TADSS_API_KEY_SMASHCOURT_BE`) — lấy giá trị đó để cấu hình SmashCourt-BE.
`.env` không vào git, image Docker hay thư mục làm việc của evaluator; không service compose nào dùng biến này. Script giữ nguyên các dòng khác (như `NGROK_AUTHTOKEN`) và kiểu xuống dòng;
gặp key khác đã có trong `.env` thì dừng, không ghi đè.

Băm tay (khi không dùng script):

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

Khóa, mặc định và ràng buộc (`DefaultStrategy`, `Rules`, `Rules[].When`): [bảng tham chiếu](docs/guides/CONFIGURATION_REFERENCE.md#tadssrouting).

Ví dụ: giờ cao điểm dùng `MockAlternativeDecisionStrategy`, còn lại dùng mặc định:

```json
"Tadss": { "Routing": { "DefaultStrategy": "DefaultDecisionStrategy",
  "Rules": [ { "Name": "peak-hour", "Strategy": "MockAlternativeDecisionStrategy", "When": { "PeakHour": true } } ] } }
```

Tên strategy phải có trong registry (sai thì không khởi động). Đổi `Tadss:Routing` trong file cấu hình đang chạy (`appsettings*.json` có `reloadOnChange`; biến môi trường không nạp lại khi chạy) có hiệu lực ngay; bản mới sai thì **giữ bảng cũ**, ghi log Error và tăng `tadss.config.reload_failures{section="Tadss:Routing"}`. Mỗi strategy có pipeline chịu lỗi và circuit breaker riêng nên strategy này lỗi không làm strategy khác fast-fail.

### `Tadss:Shadow` — chạy thêm một strategy ứng viên ở nền

Khóa, mặc định và ràng buộc: [bảng tham chiếu](docs/guides/CONFIGURATION_REFERENCE.md#tadssshadow). Ví dụ bật shadow với `MockAlternativeDecisionStrategy` cho mọi request (cần khởi động lại):

```json
"Tadss": { "Shadow": { "Enabled": true, "Strategy": "MockAlternativeDecisionStrategy" } }
```

Sau khi bản `PRIMARY` của chính request đã lưu, một worker nền chạy strategy shadow (pipeline riêng, không cache, cùng mốc thời gian) và ghi bản `role = SHADOW` (không khóa idempotency; ghép cặp với `PRIMARY` qua `reservation_id`, `request_hash`, `created_at`, `correlation_id`). Shadow không đổi response, không vào hàng chờ duyệt, không override/outcome được (404). Shadow lỗi/treo thì có bản `SHADOW` là `FALLBACK` hoặc chỉ ghi log + `tadss.shadow.executions{outcome="failed"}`.

### `Tadss:Cache` — khóa và circuit breaker Redis

Khóa, mặc định và ràng buộc (gồm `InstanceName` và bốn tham số breaker `RedisBreaker*`): [bảng tham chiếu](docs/guides/CONFIGURATION_REFERENCE.md#tadsscache). Ví dụ đặt tiền tố khóa riêng bằng biến môi trường: `Tadss__Cache__InstanceName=tadss-pt:`.

Breaker mở thì request bỏ qua Redis ngay (dùng memory nếu `EnableFallbackToMemory`), không chờ `RedisTimeoutMs`; `tadss.cache.requests{result="bypass"}` đếm các lần đó. Lỗi cache không bao giờ kích hoạt fallback của strategy. Breaker (Redis và strategy) và cache memory đều **theo từng instance**.

### `Tadss:Testing:FaultInjection` — tiêm lỗi strategy (chỉ môi trường `Testing`)

Chỉ có tác dụng khi `ASPNETCORE_ENVIRONMENT=Testing` và `Enabled=true`; ở môi trường khác bật cờ này chỉ sinh Warning, không bọc strategy. Khóa, mặc định và ràng buộc (`Profile`, `DelayMs`, `TargetStrategy`): [bảng tham chiếu](docs/guides/CONFIGURATION_REFERENCE.md#tadsstestingfaultinjection). Ví dụ làm strategy `DefaultDecisionStrategy` treo 1 giây: `Tadss__Testing__FaultInjection__Enabled=true`, `Tadss__Testing__FaultInjection__Profile=Delay`, `Tadss__Testing__FaultInjection__TargetStrategy=DefaultDecisionStrategy`.

Dùng cho kịch bản sự cố dưới tải (LT-03). Strategy bị bỏ qua token vẫn bị cắt đúng hạn (pipeline cách ly nó khỏi luồng request) nhưng CPU của nó chỉ dừng khi nó tự kết thúc — xem giới hạn ở `docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md`.

### Metric và log

Meter `TADSS.Runtime` (13 instrument, khai báo ở `TadssMetricNames`): quyết định, thời gian request và pipeline, fallback, trạng thái breaker, cache, hàng chờ duyệt, override, shadow, nạp lại cấu hình. Xuất qua OpenTelemetry (OTLP) khi `Tadss:Telemetry:Enabled = true` (mặc định tắt); `docker compose up` bật sẵn và gửi tới Grafana. Lỗi lệnh SQL mong đợi (tranh chấp idempotency `23505`) chỉ ghi ở mức Debug; lỗi thật vẫn ghi ở Warning/Error bởi store và exception handler.

## Observability và chế độ đánh giá

### Observability (OpenTelemetry + Grafana)

- **Chế độ vận hành** (`docker compose up -d --build`): TADSS gửi trace, metric và log tới `grafana/otel-lgtm` (cổng OTLP `4317`); mở `http://localhost:3001`, thư mục **TADSS** có dashboard **TADSS Runtime** nạp sẵn (29 panel hiển thị trong 7 nhóm: tổng quan, quyết định, chịu lỗi, cache, quản trị và shadow, tải k6, log và trace; biến `instance`; annotation `chaos`).
- **`dotnet run` hằng ngày:** telemetry tắt (không cần collector). Muốn xem dashboard khi chạy tay, bật collector riêng và trỏ TADSS tới nó:
  `MSYS_NO_PATHCONV=1 docker run --rm -p 3001:3000 -p 4317:4317 -v "$(pwd -W)/docker/grafana/provisioning/dashboards/tadss.yaml:/otel-lgtm/grafana/conf/provisioning/dashboards/tadss.yaml:ro" -v "$(pwd -W)/docker/grafana/dashboards:/otel-lgtm/tadss-dashboards:ro" grafana/otel-lgtm:0.34.0` (Git Bash; thiếu `MSYS_NO_PATHCONV=1` và `$(pwd -W)` thì Git Bash đổi đường dẫn trong container thành đường dẫn Windows; trong PowerShell dùng `"${PWD}/…"` và bỏ `MSYS_NO_PATHCONV=1`), rồi đặt `Tadss__Telemetry__Enabled=true` và `Tadss__Telemetry__OtlpEndpoint=http://localhost:4317`. Collector chết hoặc không tới được không ảnh hưởng request.
- Kiểm tự động (không cần Neon/ngrok): `bash tools/observability/verify-observability.sh` (OB-2 dashboard đã nạp, OB-4 có `tadss_decisions_total` sau vài request); bước (a) của lượt đánh giá đã chạy sẵn script này.

Khóa `Tadss:Telemetry:*`, mặc định và ràng buộc: [bảng tham chiếu](docs/guides/CONFIGURATION_REFERENCE.md#tadsstelemetry). Mặc định `Enabled=false` (không đăng ký provider OpenTelemetry nào).

### Chế độ đánh giá một lệnh

Dựng stack cô lập (hai instance TADSS sau nginx, instance riêng cho dữ liệu khách sạn, PostgreSQL 16, Redis 7, Toxiproxy, Grafana) và một container **evaluator** chạy tuần tự sáu bước: (a) toàn bộ test gộp theo mã danh mục + `verify-expose.sh` + `verify-observability.sh` (OB-2/OB-4 của chế độ vận hành) + `verify-export.sh` + ba bộ pytest của `tools/` (`evaluation`, `evidence`, `docs`) + `python tools/docs/check_docs.py` (DOC-1…3) + kiểm dashboard; (b) k6 LT-01, LT-02 (và LT-05 ở bản đầy đủ); (c) SCALE-2 rồi sự cố dưới tải LT-03; (d) Schemathesis CT-03, coverage gộp, Stryker MT-01…03 (chỉ bản đầy đủ); (e) **dữ liệu thực tế**: replay booking SmashCourt (RP) và dataset khách sạn (PT); (f) gom Evidence Pack `evidence/<run_id>/`. Dữ liệu SmashCourt (`data/private/smashcourt_bookings.csv`) là **tùy chọn**: thiếu thì phần RP bị bỏ qua, các phần khác vẫn chạy.

**Hướng dẫn chạy từng bước, đọc kết quả, chạy lại một bước, xử lý sự cố, dọn Docker: [`docs/guides/EVALUATION_RUN_GUIDE.md`](docs/guides/EVALUATION_RUN_GUIDE.md).** Các lệnh chính (chạy trong thư mục `TADSS-Runtime/`):

```bash
docker compose up -d --build                       # cách chính: app + ngrok + Grafana + một lượt đánh giá bản nhanh (D-14)
docker compose logs -f evaluator                   # theo dõi; dòng cuối in đường dẫn report.html và mã thoát
# Chạy lại mà không khởi động lại app:
bash tools/evaluation/run.sh                       # bản nhanh (tự build/tải image ở lần đầu, in đường dẫn report.html)
bash tools/evaluation/run.sh full                  # bản đầy đủ (số liệu chính thức)
bash tools/evaluation/run.sh quick --steps e,f     # chỉ chạy một vài bước (a..f); kèm f để có report.html
bash tools/evaluation/run.sh quick --clean         # chạy xong dọn sạch phần đánh giá: container/volume/image/build cache (~6 GB); app vận hành giữ nguyên
bash tools/evaluation/run.sh --clean-only          # chỉ dọn phần đánh giá
```

- **Kết quả:** `evidence/<run_id>/` (`run_id` = `EV-{yyyyMMdd}-EVAL-{quick|full}-r{n}`; thư mục làm việc, không commit): `report.html` (tự chứa), `manifest.json` (SHA-256 từng file, commit, máy, KPI SC1…SC8, EV-1…EV-5), `raw/` (dữ liệu thô từng bước), `grafana/dashboard-snapshot.json`, `config/`.
- **Mã thoát:** `0` đạt mọi tiêu chí; `1` chạy hết nhưng có tiêu chí không đạt; `2` lỗi hạ tầng, tham số sai hoặc không chạy hết.
- **Thời lượng đo thật:** bản nhanh **~28 phút** cho cả sáu bước trên máy tác giả (chưa tính lần đầu dựng image); bản đầy đủ **≈ 3 giờ 25 phút** (lượt `EV-20261017-EVAL-full-r1`). Bảng từng bước nằm trong guide.
- **Tài nguyên:** ≥ 8 GB RAM cấp cho Docker, ≥ 15 GB ổ trống (image ~6 GB); tắt chế độ ngủ khi chạy.
- **Cảnh báo bảo mật:** evaluator mount `/var/run/docker.sock` (Testcontainers cho integration test) nên có quyền điều khiển Docker của máy; chỉ chạy trên máy của chính bạn. Mã nguồn được mount chỉ đọc và copy sang `/work` **loại trừ** `appsettings.Development.json`, `.env*`; stack đánh giá dùng khóa API cố định không bí mật (`tadss-evaluation-key`). CSV SmashCourt thô nằm ở `data/private/` (gitignore, mount chỉ đọc), mọi định danh được băm trước khi ghi vào `evidence/`.
- **Giới hạn:** một máy (k6, TADSS, DB tranh CPU), môi trường `Testing`, rate limit nâng, trace lấy mẫu 10 % — xem Mục 8 của methodology và phần "Giới hạn" của `report.html`.

## Chạy bằng Docker và expose qua ngrok

Chỉ **một lệnh** `docker compose up -d --build` chạy TADSS, ngrok và Grafana cùng lúc (SmashCourt-BE ở máy khác, demo), kèm một lượt đánh giá tự chạy trong stack cô lập (D-14, mục "Chế độ đánh giá một lệnh"). `docker-compose.yml` là file compose duy nhất, không profile, cấu hình **giống SmashCourt-BE**: `tadss-runtime` chạy `ASPNETCORE_ENVIRONMENT=Development` (cổng host chỉ `127.0.0.1`), `ngrok` chỉ cần `NGROK_AUTHTOKEN` (không domain tĩnh), Grafana (`grafana/otel-lgtm`). Database là Neon, cache là Redis Cloud và API key đều khai báo trong `src/TADSS.Runtime.Api/appsettings.Development.json` (file duy nhất `dotnet run` và Docker dùng chung): file đó được **copy vào image** lúc build (`.dockerignore` không loại nó), không mount thêm gì lúc chạy. `.env` chỉ chứa `NGROK_AUTHTOKEN`.

1. Thêm API key **thật** vào `src/TADSS.Runtime.Api/appsettings.Development.json` (không commit), mục `Tadss:Security:ApiKeys`: một phần tử `{ "ClientId": "smashcourt-be", "KeySha256": "<mã băm>" }` (cách sinh khóa và mã băm: [Tạo API key và mã băm](#tạo-api-key-và-mã-băm)). **Không** dùng khóa local công khai `tadss-local-dev-key`: ở `Development` validator vẫn chấp nhận nó nên không có gì chặn bạn expose nhầm. SmashCourt-BE dùng cùng một khóa cho cả `dotnet run` lẫn Docker. Chuỗi kết nối Neon và Redis Cloud trong file đó cũng được container dùng; schema `tadss` trên Neon do bạn migrate sẵn (`dotnet ef database update`), container **không bao giờ tự migrate**.
2. Tạo mạng dùng chung với SmashCourt-BE/FE (một lần): `docker network create smashcourt-network`.
3. Tạo `.env` từ `.env.example` (không commit) và điền `NGROK_AUTHTOKEN` (lấy ở dashboard.ngrok.com). Biến `TADSS_API_KEY_*` trong `.env` do `tools/security/hash_api_key.py` ghi (key gốc cho BE), compose không dùng.
4. Chạy tất cả: `docker compose up -d --build`. TADSS ở `http://localhost:8081`, Grafana ở `http://localhost:3001`. Mỗi lần đổi `appsettings.Development.json` phải build lại image (`--build`).
5. Lấy URL công khai của ngrok: mở `http://localhost:4040` hoặc chạy `docker compose logs ngrok`. URL **đổi mỗi lần khởi động lại** ngrok, nên phải cập nhật `Tadss:BaseUrl` của SmashCourt-BE mỗi lần. Muốn URL cố định, lấy domain tĩnh miễn phí ở dashboard ngrok rồi thêm `--url=<domain>` vào cuối dòng `command` của service `ngrok` trong `docker-compose.yml` (sửa một dòng).
6. Grafana: mở `http://localhost:3001`. Image `grafana/otel-lgtm` bật sẵn truy cập ẩn danh với quyền Admin nên vào thẳng không cần đăng nhập; nếu cần đăng nhập thì mặc định là `admin` / `admin` (đây là mặc định công khai của Grafana, không phải secret của dự án). Cổng OTLP `4317` (gRPC) và `4318` (HTTP) chỉ bind `127.0.0.1`. TADSS gửi trace, metric và log tới Grafana qua OTLP: dashboard **TADSS Runtime** (thư mục "TADSS", uid `tadss-runtime`, nạp sẵn từ `docker/grafana/dashboards`, chỉ đọc) có số liệu sau vài request. Kiểm tự động: `bash tools/observability/verify-observability.sh` (cần Docker; lần đầu tải image ~3,7 GB).
7. Dừng tất cả (gồm stack đánh giá): `docker compose down`.

### Lệnh Docker thường dùng

Chạy trong thư mục `TADSS-Runtime/`.

| Việc | Lệnh |
| :--- | :--- |
| Tạo mạng dùng chung (chỉ lần đầu, có thể đã có do SmashCourt-BE tạo) | `docker network create smashcourt-network` |
| Khởi động / build lại: vận hành + một lượt đánh giá bản nhanh | `docker compose up -d --build` |
| Như trên nhưng đánh giá bản đầy đủ | `EVAL_MODE=full docker compose up -d --build` (PowerShell: `$env:EVAL_MODE="full"; docker compose up -d --build`) |
| Theo dõi lượt đánh giá; kết quả | `docker compose logs -f evaluator`; `evidence/<run_id>/report.html` |
| Trạng thái; log TADSS; log ngrok | `docker compose ps`; `docker compose logs -f tadss-runtime`; `docker compose logs ngrok` |
| Dừng tất cả (giữ image) | `docker compose down` |
| Kiểm tra cấu hình expose (không đụng Neon) | `bash tools/expose/verify-expose.sh` |
| Kiểm tra observability (không đụng Neon) | `bash tools/observability/verify-observability.sh` |
| Chạy lại một lượt đánh giá / vài bước (không khởi động lại app) | `bash tools/evaluation/run.sh [quick\|full] [--steps e,f]` |
| Xem lại Grafana sau khi đánh giá xong | mở `http://127.0.0.1:3002` (stack đánh giá còn chạy tới khi dọn) |
| Log một service của stack đánh giá | `docker compose logs eval-tadss-1` |
| Dọn riêng stack đánh giá (container, volume, image, build cache), giữ app | `bash tools/evaluation/run.sh --clean-only` |
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

Cấu hình của `tadss-runtime` (không biến môi trường nào chứa secret). Toàn bộ khóa `Tadss:*` và thứ tự nguồn cấu hình: [`docs/guides/CONFIGURATION_REFERENCE.md`](docs/guides/CONFIGURATION_REFERENCE.md):

| Mục trong `appsettings.Development.json` | Ý nghĩa |
| :--- | :--- |
| `ConnectionStrings:DefaultConnection` | Chuỗi kết nối PostgreSQL của Neon dạng Npgsql `key=value` với `SSL Mode=Require` (URI `postgresql://...` không được chấp nhận) |
| `Tadss:Cache:RedisConnectionString` | Kết nối Redis Cloud (dạng và ràng buộc: [bảng tham chiếu](docs/guides/CONFIGURATION_REFERENCE.md#tadsscache)) |
| `Tadss:Security:ApiKeys` | `ClientId` `smashcourt-be` + SHA-256 hex của khóa riêng (không dùng mã băm của khóa local công khai); ràng buộc: [bảng tham chiếu](docs/guides/CONFIGURATION_REFERENCE.md#tadsssecurity) |

Biến trong `.env` (mẫu: `.env.example`):

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

Repo chỉ có hai file `README.md`: file này (điểm vào của repo, đồng thời giữ bảng kiểm kê tài liệu) và [`src/Shared.DecisionContractSDK/README.md`](src/Shared.DecisionContractSDK/README.md) (đóng gói vào NuGet của SDK qua `PackageReadmeFile`). Toàn bộ tài liệu của Đồ án 2 (kiến trúc, contract v2, kế hoạch, phương pháp thực nghiệm, hướng dẫn chạy) nằm ở `docs/`, cùng `CLAUDE.md` ở gốc repo. Bảng kiểm kê dưới đây là **danh sách tài liệu duy nhất** của repo: mỗi tài liệu có đúng một dòng, cho biết tài liệu thuộc nhóm nào và ai là nơi sở hữu chủ đề của nó. Bản đồ tài liệu theo chủ đề: Mục III của [`docs/planning/THESIS_DEVELOPMENT_PLAYBOOK.md`](docs/planning/THESIS_DEVELOPMENT_PLAYBOOK.md).

Tài liệu cấp workspace SmashCourt (kiến trúc hệ thống, use case, API của BE) nằm ở repo SmashCourt-BE, không thuộc bảng kiểm kê của repo này: [`SmashCourt-BE/docs/3.1-kien-truc-he-thong.md`](../SmashCourt-BE/docs/3.1-kien-truc-he-thong.md), [`SmashCourt-BE/docs/use-cases/`](../SmashCourt-BE/docs/use-cases/), [`SmashCourt-BE/docs/api/`](../SmashCourt-BE/docs/api/).

### Quy tắc "tài liệu sở hữu thắng"

- Mỗi chủ đề (kiến trúc, contract, schema DB, chịu lỗi, danh mục kiểm thử, cấu hình, cách chạy...) có **một** tài liệu sở hữu, là một tài liệu nhóm `hiện hành` trong bảng kiểm kê; cột *Ghi chú* của dòng đó mô tả ngắn nội dung và phạm vi của file (không phải danh sách chủ đề). Các tài liệu khác chỉ tóm tắt và dẫn link tới nơi sở hữu, không chép lại thông số.
- Khi hai tài liệu mâu thuẫn, nội dung của tài liệu sở hữu thắng; tài liệu còn lại phải sửa theo.
- Khi mã, contract hoặc kế hoạch đổi, sửa tài liệu sở hữu trong cùng đợt thay đổi.

### Hai nhóm tài liệu

| Nhóm | Ý nghĩa | Yêu cầu |
| :--- | :--- | :--- |
| `hiện hành` | Sở hữu một chủ đề và phải khớp mã, contract, kế hoạch hiện tại. | Không mô tả như đang dùng những thứ đã gỡ khỏi repo (DOC-2); link và anchor phải đúng (DOC-3). |
| `lịch sử` | Ảnh chụp tại một thời điểm, không cập nhật theo mã (bản đã nộp, khảo sát tiền đề...). | File `.md` mở đầu bằng banner `> **Tài liệu lịch sử** — ảnh chụp ngày dd/mm/yyyy, không cập nhật theo mã. Nguồn hiện hành: [...](...)`; cột *Ghi chú* nêu tài liệu thay thế. Được miễn DOC-2. |

Một file chỉ là `lịch sử` khi **toàn bộ** nội dung là ảnh chụp cũ. Nếu chỉ một đoạn nói về quá khứ (ví dụ mục lưu trữ Phase 4), file vẫn là `hiện hành` và đoạn đó được viết ở thì quá khứ.

### Bảng kiểm kê

Cột *File* ghi đường dẫn tính từ gốc repo. Bảng này do `tools/docs/check_docs.py` đọc nên giữ nguyên tiêu đề cột `File | Nhóm` và đúng hai giá trị `hiện hành` / `lịch sử` ở cột *Nhóm*.

| File | Nhóm | Ghi chú |
| :--- | :--- | :--- |
| `README.md` | hiện hành | Tổng quan repo, cách chạy, bảng kiểm kê tài liệu (DOC-1) và quy tắc phân nhóm |
| `CLAUDE.md` | hiện hành | Hướng dẫn làm việc với repo (quy ước, quyết định D-xx đang áp dụng) |
| `docs/PROJECT_BLUEPRINT_AND_SYSTEM_OVERVIEW.md` | hiện hành | Tổng quan hệ thống và luồng nghiệp vụ, trạng thái dự án |
| `docs/PHASE5_SUMMARY.md` | hiện hành | Tổng hợp Phase 5 để review và trình bày (gói, kiến trúc, hạ tầng, kiểm thử, lệnh); tài liệu sở hữu vẫn thắng |
| `docs/PROPOSAL_DO_AN_2.docx` | lịch sử | Bản đề cương đã nộp (file sửa lần cuối 07/09/2026), mô tả kiến trúc v1. Thay thế: `docs/planning/PROPOSAL_DO_AN_2.md` |
| `docs/23521683_23521709_Design_and_Development_of_a_Fault_Tolerant_Decision_Runtime_Engine_for_Service_Reservation_Systems.docx` | lịch sử | Bản đề cương đã nộp (file sửa lần cuối 05/10/2026), một số thông số chưa theo mã hiện tại. Thay thế: `docs/planning/PROPOSAL_DO_AN_2.md` |
| `docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md` | hiện hành | Cấu trúc solution, các project và quy tắc phụ thuộc |
| `docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md` | hiện hành | Schema `tadss` và bất biến lưu trữ quyết định |
| `docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md` | hiện hành | Pipeline chịu lỗi và phân loại lỗi (FI-xx) |
| `docs/architecture/ARCHITECTURE_DECISION_RECORDS.md` | hiện hành | Bảng tổng hợp ADR-001…013 |
| `docs/architecture/adr/ADR-001-why-strategy-pattern.md` | hiện hành | ADR-001, Strategy Pattern (Accepted) |
| `docs/architecture/adr/ADR-002-why-sdk-first-approach.md` | hiện hành | ADR-002, SDK-first (Accepted) |
| `docs/architecture/adr/ADR-003-why-polly-resilience.md` | hiện hành | ADR-003, Polly v8 (Accepted) |
| `docs/architecture/adr/ADR-004-why-rich-domain-model.md` | hiện hành | ADR-004, Rich Domain Model (Accepted) |
| `docs/architecture/adr/ADR-005-why-catboost-ml.md` | hiện hành | ADR-005, định hướng Khóa luận (ngoài phạm vi Đồ án 2) |
| `docs/contracts/INTEGRATION_SPEC.md` | hiện hành | Contract v2, luồng tích hợp BE ↔ TADSS và bộ JSON schema v2 theo endpoint |
| `docs/contracts/openapi-v2.yaml` | hiện hành | OpenAPI v2 sinh từ mã, khóa bởi `OpenApiDriftTests` |
| `docs/contracts/baseline_public_api.txt` | hiện hành | Baseline bề mặt public của SDK, khóa bởi `PublicApiSurfaceTests` |
| `docs/contracts/schemas/v2/decision-detail.schema.json` | hiện hành | JSON schema v2, chi tiết quyết định |
| `docs/contracts/schemas/v2/decision-request.schema.json` | hiện hành | JSON schema v2, request đánh giá |
| `docs/contracts/schemas/v2/decision-result.schema.json` | hiện hành | JSON schema v2, kết quả quyết định |
| `docs/contracts/schemas/v2/outcome-request.schema.json` | hiện hành | JSON schema v2, request ghi outcome |
| `docs/contracts/schemas/v2/outcome-response.schema.json` | hiện hành | JSON schema v2, response outcome |
| `docs/contracts/schemas/v2/override-request.schema.json` | hiện hành | JSON schema v2, request override |
| `docs/contracts/schemas/v2/override-response.schema.json` | hiện hành | JSON schema v2, response override |
| `docs/contracts/schemas/v2/pending-approvals.schema.json` | hiện hành | JSON schema v2, hàng chờ duyệt |
| `docs/database/schema_smashcourt.sql` | hiện hành | Bản `pg_dump` schema DB SmashCourt (đầu vào của `tools/evidence/sql/verify-export.sh`) |
| `docs/guides/CONFIGURATION_REFERENCE.md` | hiện hành | Bảng tham chiếu khóa cấu hình `Tadss:*` (kiểu, mặc định, ràng buộc), khớp mã bởi test DOC-4 |
| `docs/guides/EVALUATION_RUN_GUIDE.md` | hiện hành | Hướng dẫn chạy chế độ đánh giá từng bước, kèm kịch bản tải k6 (LT-01, LT-02, LT-05) |
| `docs/planning/MASTER_EXECUTION_PLAN.md` | hiện hành | Kế hoạch v2.1, nguồn duy nhất về lịch trình |
| `docs/planning/BACKLOG_SPRINTS.md` | hiện hành | Backlog theo phase và trạng thái task |
| `docs/planning/PROJECT_WBS_MAPPING.md` | hiện hành | WBS, phụ thuộc và rủi ro |
| `docs/planning/PROPOSAL_DO_AN_2.md` | hiện hành | Phạm vi chính thức của Đồ án 2 |
| `docs/planning/THESIS_DEVELOPMENT_PLAYBOOK.md` | hiện hành | Thuật ngữ chuẩn, ranh giới phạm vi, bản đồ tài liệu |
| `docs/report/ARCHITECTURE_DIAGRAMS.md` | hiện hành | Sơ đồ cho báo cáo (Mermaid): ngữ cảnh, container, trước/sau khi có TADSS, cấu trúc bên trong TADSS; chỉ tóm tắt, tài liệu sở hữu thắng |
| `docs/report/USE_CASES.md` | hiện hành | Sơ đồ và đặc tả use case mới/thay đổi khi SmashCourt dùng TADSS (UC-DEPOSIT-01…11) và use case của TADSS (UC-TADSS-01…08) |
| `docs/report/SEQUENCE_DIAGRAMS.md` | hiện hành | Sơ đồ tuần tự cho báo cáo: đặt sân có cọc, duyệt/từ chối, xử lý quyết định bên trong TADSS, job hết hạn và báo outcome |
| `docs/report/ACTIVITY_AND_STATE_DIAGRAMS.md` | hiện hành | Sơ đồ hoạt động (quy trình đặt cọc, duyệt cọc, pipeline quyết định) và sơ đồ trạng thái (booking, quyết định) cho báo cáo |
| `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md` | hiện hành | Danh mục kiểm thử, KPI SC1–SC8, quy tắc DOC-1…DOC-4 |
| `docs/research/E2E_TEST_SPECIFICATION.md` | hiện hành | Đặc tả từng bước các kịch bản E2E-01…E2E-14 (T5.27): dữ liệu thử, bước, quan sát kỳ vọng, minh chứng, truy vết |
| `docs/research/DATASET_SCHEMA_SPECIFICATION.md` | hiện hành | Cấu trúc dataset và Evidence Pack |
| `docs/research/RESEARCH_THESIS_DEFENSE.md` | hiện hành | Định hướng Khóa luận (ngoài phạm vi Đồ án 2) |
| `docs/research/RESEARCH_PROBLEM_BACKLOG.md` | lịch sử | Khảo sát tiền đề trước khi khóa phạm vi, ảnh chụp 27/09/2026. Thay thế: `docs/planning/PROPOSAL_DO_AN_2.md` |
| `src/Shared.DecisionContractSDK/README.md` | hiện hành | Giới thiệu SDK `Shared.DecisionContractSDK` (đóng gói vào NuGet) |

### Cách kiểm

Từ gốc repo:

```bash
python tools/docs/check_docs.py
```

Lệnh thoát 0 khi cả ba mã đạt, ngược lại liệt kê từng vấn đề kèm đường dẫn và số dòng:

- **DOC-1:** mọi file dưới `docs/` và mọi `README.md` / `CLAUDE.md` có đúng một dòng trong bảng trên; dòng không trỏ tới file không tồn tại; file `lịch sử` dạng `.md` mở đầu bằng banner.
- **DOC-2:** tài liệu `hiện hành` không nhắc những thứ đã gỡ khỏi repo như đang dùng. Dòng nói về quá khứ hoặc điều cấm (ví dụ "đã gỡ", "không còn", "cấm") không bị tính.
- **DOC-3:** link nội bộ và anchor trong mọi file `.md` đều trỏ tới nơi tồn tại (link ra ngoài repo và `http` được bỏ qua, chỉ đếm; các link tới tài liệu workspace ở repo SmashCourt-BE nói trên thuộc loại này).

Không có cơ chế bỏ qua từng dòng: khi báo lỗi, sửa câu cho đúng hiện trạng hoặc phân loại lại file, không chèn từ khóa để lọt kiểm tra. Khi thêm tài liệu mới (hoặc README mới), thêm luôn một dòng vào bảng kiểm kê. Bộ kiểm này chạy trong CI và ở bước (a) của evaluator (`tools/evaluation/evaluator/steps/tests_step.py` chạy nó trên bản sao `/work`), nên tài liệu không được link tới file bị loại khỏi bản sao (`.env*`, `appsettings.Development.json`, `evidence/`, `logs/`).

---

## License

Phát triển cho mục đích học thuật trong khuôn khổ Đồ án 2 (và mở rộng cho Khóa luận tốt nghiệp).
