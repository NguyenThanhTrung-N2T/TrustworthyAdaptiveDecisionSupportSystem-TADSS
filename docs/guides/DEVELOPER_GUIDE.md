# Hướng dẫn cho nhà phát triển TADSS Runtime

| | |
| :--- | :--- |
| **Dành cho** | Người phát triển và tích hợp TADSS: build, test, cấu hình chạy local, API key, hành vi bảo mật, cấu hình runtime thích ứng, observability, chạy bằng Docker và expose qua ngrok |
| **Không chứa** | Lệnh vận hành bản deploy, CD, đo hiệu năng ([RUNBOOK](RUNBOOK.md)); bảng khóa cấu hình đầy đủ ([CONFIGURATION_REFERENCE](CONFIGURATION_REFERENCE.md)); hợp đồng API ([INTEGRATION_SPEC](../contracts/INTEGRATION_SPEC.md)) |
| **Nguồn** | Tách nguyên văn từ `README.md` ngày 20/11/2026 để README chỉ còn phần giới thiệu |

## Mục lục

1. [Build, test, chạy](#build-test-chạy)
2. [Bảo mật](#bảo-mật)
3. [Runtime thích ứng: định tuyến, shadow, cache và cách ly lỗi](#runtime-thích-ứng-định-tuyến-shadow-cache-và-cách-ly-lỗi)
4. [Observability và chế độ đánh giá](#observability-và-chế-độ-đánh-giá)
5. [Chạy bằng Docker và expose qua ngrok](#chạy-bằng-docker-và-expose-qua-ngrok)

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

`docker-compose.yml` là file compose **duy nhất**, chỉ dùng để chạy TADSS trong Docker và expose qua ngrok (`tadss-runtime` chạy ở `Development` như SmashCourt-BE, cùng `ngrok` và `grafana`, không profile: một lệnh `docker compose up -d --build` chạy cả ba; database Neon, Redis Cloud và API key đọc từ `appsettings.Development.json` được mount chỉ đọc vào container; file không nằm trong image), xem mục Chạy bằng Docker và expose qua ngrok. Chế độ đánh giá nằm cùng file, cũng không profile: các service `eval-*` và `evaluator` khởi động cùng ba service trên bằng một lệnh `docker compose up -d --build` (D-14), xem "Chế độ đánh giá một lệnh" bên dưới.

**Ba chế độ chạy (D-12; D-14: chế độ vận hành và đánh giá chung một lệnh `up`):**

| Chế độ | Lệnh | Dùng để | Trạng thái |
| :--- | :--- | :--- | :--- |
| Dev hằng ngày | `dotnet run --project src/TADSS.Runtime.Api` | Neon + Redis Cloud qua `appsettings.Development.json`; chỉ app + Swagger để kiểm tra luồng, không Docker, không Grafana | Đã có |
| Vận hành / demo **+ đánh giá** | `docker compose up -d --build` | TADSS + ngrok + Grafana (`grafana/otel-lgtm`, `localhost:3001`), **và** tự chạy một lượt đánh giá (dòng dưới) | Đã có (D-11, D-14) |
| Đánh giá cho hội đồng | Chạy kèm `docker compose up -d --build` (D-14); chạy lại riêng: `bash tools/evaluation/run.sh` | Stack cô lập, **không bao giờ chạm Neon**: TADSS (Release) × 2 sau reverse proxy + PostgreSQL 16 + Redis 7 + Toxiproxy + Grafana + container evaluator chạy toàn bộ test, k6, sự cố dưới tải, Schemathesis, Stryker rồi sinh `evidence/<run_id>/` (`report.html`, dữ liệu thô, `manifest.json`); bản nhanh ~30 phút, bản đầy đủ ≈ 3 giờ 25 phút (đo thật); chạy trên một máy nên là số liệu phòng lab | Đã có (gói 5.5, mở rộng ở 5.6), xem mục "Chế độ đánh giá một lệnh" và [hướng dẫn chạy](RUNBOOK.md) |

> **Một lệnh chạy hết (D-14, 10/10/2026).** `docker compose up -d --build` khởi động app + ngrok + Grafana **và** stack đánh giá; evaluator tự chạy các bước a…f (bản nhanh, ~30 phút) rồi dừng, kết quả ở `evidence/<run_id>/report.html` (theo dõi: `docker compose logs -f evaluator`). Muốn bản đầy đủ: `EVAL_MODE=full docker compose up -d --build` (PowerShell: `$env:EVAL_MODE="full"; docker compose up -d --build`). Mỗi lần `up` lại là chạy lại một lượt đánh giá. Stack đánh giá vẫn cô lập: mạng riêng, PostgreSQL/Redis dùng một lần, không chạm Neon; service vận hành không phụ thuộc nó. Đổi lại: cần thêm RAM (≥ 8 GB cho Docker) và lúc đánh giá chạy, TADSS vận hành cùng máy làm số liệu nhiễu thêm một chút (ghi vào giới hạn).

Test tự động trên CI vẫn dùng Testcontainers (`dotnet test`). Chi tiết: quyết định D-12 trong [`spec/archive/phase5/PHASE5_DESIGN_SPEC.md`](../../spec/archive/phase5/PHASE5_DESIGN_SPEC.md) và Mục 6.1 của [`docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).

**Khóa local công khai.** Khóa `tadss-local-dev-key` (client `local`, mã băm `0472e99d9ccd889b6e6781cf0920a9b39910c1c430393d62839465c4901a6268`) chỉ dành cho môi trường `Development` qua `appsettings.Development.json` (đặt mã băm này vào `Tadss:Security:ApiKeys`). Ai đọc tài liệu này cũng biết khóa đó nên **không bao giờ** expose ra mạng ngoài với khóa này: ngoài `Development`/`Testing`, validator của `Tadss:Security` từ chối mã băm này và app không khởi động.

---

## Bảo mật

Mọi request `/api/*` phải mang `X-Api-Key`. Thứ tự middleware (khai báo tại `Composition/RequestPipelineExtensions.UseTadssRequestPipeline`, khóa bằng test UT-14): forwarded headers (chỉ khi `TrustForwardedHeaders`) → correlation id → request logging → request metrics → exception handler → security headers + HSTS → https redirection → giới hạn kích thước → Swagger (chỉ `Development`) → xác thực API key → rate limiter → routing. Mã lỗi và contract: [`docs/contracts/INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md) mục 3.2 và 3.8. Các phản hồi từ chối do middleware tạo (401, 413, 429) và mọi lỗi do exception (qua `GlobalExceptionHandler`) đi qua một bộ ghi `ProblemResponseWriter` nên cùng dạng `application/problem+json`; ngoại lệ là 415 (do MVC), 404/405 của framework và một số lỗi mức Kestrel, không có `code` (INTEGRATION_SPEC §3.8).

### Cấu hình `Tadss:Security:*`

Khai báo trong `appsettings.json`, ghi đè bằng biến môi trường dạng `Tadss__Security__...`; có validator và `ValidateOnStart` nên giá trị sai làm app không khởi động (thông báo chỉ nêu tên khóa, không in mã băm). Danh sách khóa, mặc định và ràng buộc: [`docs/guides/CONFIGURATION_REFERENCE.md`](CONFIGURATION_REFERENCE.md#tadsssecurity). Khi `ApiKeys` rỗng: mọi `/api/*` bị 401 ở `Development`/`Testing`, các môi trường khác không khởi động.

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
- Docker/expose: cùng `appsettings.Development.json` ở trên (compose mount chỉ đọc vào container, file không nằm trong image; container chạy `Development` như SmashCourt-BE; xem mục Chạy bằng Docker và expose qua ngrok); khóa phải là khóa riêng, không dùng khóa local công khai.

Mỗi client một phần tử (chỉ số `0`, `1`, ...); thu hồi khóa bằng cách xóa phần tử rồi khởi động lại.

### Health, giới hạn tốc độ, header, log

- **Health:** `/health/live` (không kiểm tra gì, 200 khi tiến trình còn chạy) và `/health/ready` (PostgreSQL bắt buộc: lỗi → 503; Redis: lỗi → 200 `Degraded` vì cache có fallback memory). Cả hai miễn `X-Api-Key` ở mọi môi trường, không bị rate limit, body chỉ là chữ trạng thái (không lộ chuỗi kết nối). Không còn endpoint `/health` gộp.
- **Rate limit** (token bucket, một xô cho mỗi `ClientId`; hết token là 429 `RATE_LIMITED` kèm `Retry-After`): tính **theo từng instance** của app, không chia sẻ giữa nhiều instance (đánh giá khi scale thuộc gói 5.5). Mặc định cho phép bùng nổ 100 request và nạp 100 request mỗi giây cho một client. Để k6 tải cao hơn, tăng bằng biến môi trường của tiến trình app: `Tadss__Security__RateLimiting__PermitLimit`, `Tadss__Security__RateLimiting__TokensPerPeriod`, `Tadss__Security__RateLimiting__ReplenishmentPeriodSeconds` (với compose: thêm vào `environment` của `tadss-runtime` trong một file override cục bộ).
- **Header bảo mật** trên mọi response: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`; `Cache-Control: no-store` cho `/api/*`; `Strict-Transport-Security` với request https ngoài `Development`/`Testing`; Kestrel không gửi header `Server`.
- **Log:** khóa `X-Api-Key` và mã băm không bao giờ vào log. `ClientId` của khóa hợp lệ có ở thuộc tính log scope (thấy trong `{Properties:j}` của log phát sinh trong controller/service) và ở tag `tadss.client_id` của trace. Dòng log hoàn tất request của Serilog được làm giàu `ClientId` lúc kết thúc request (request bị từ chối ở bước xác thực thì không có). Request logging đứng **ngoài** exception handler nên ghi đúng status cuối (400, 404, 409, 503, 504); status ≥ 500 ghi mức Error, còn lại Information.

### Chuỗi cung ứng và container

- Mọi project có `packages.lock.json` (`RestorePackagesWithLockFile` trong `Directory.Build.props`); CI và `Dockerfile` restore bằng `--locked-mode`, nên lock file lệch đồ thị phụ thuộc thì build đỏ. **Dependabot** (NuGet, GitHub Actions, Docker; mỗi tuần, nhánh `develop`) có thể mở PR nâng package mà không cập nhật `packages.lock.json`: khi đó CI đỏ ở bước restore cho tới khi chạy `dotnet restore --force-evaluate` rồi commit các `packages.lock.json` đã đổi vào PR.
- CI chạy `tools/ci/check-vulnerable-packages.sh` (`dotnet list package --vulnerable --include-transitive` cho cả solution): mức High/Critical hoặc không quét được thì job đỏ. `System.Net.Http` 4.3.4 và `System.Text.RegularExpressions` 4.3.1 được ghim bắc cầu trong `Directory.Packages.props` (`CentralPackageTransitivePinningEnabled`) vì xunit 2.5.3 kéo bản 4.3.0 có lỗ hổng; chỉ ảnh hưởng project test, gỡ khi nâng xunit.
- Image chạy bằng user non-root (`USER $APP_UID`); `.dockerignore` loại `.env`, khóa riêng và `appsettings.Development.json` (Epic 6C: image đẩy lên GHCR nên không chứa bí mật); `tools/ci/check-image.sh <image>` kiểm image không chứa file bí mật, không chạy root, có nhãn revision; CI kiểm `Config.User` của image.

---

## Runtime thích ứng: định tuyến, shadow, cache và cách ly lỗi

Cấu hình dưới đây đều qua validator + `ValidateOnStart`: sai thì app không khởi động. Giá trị mặc định giữ nguyên hành vi (một strategy mặc định, shadow tắt, khóa cache như cũ).

### `Tadss:Routing` — chọn strategy theo từng request (nạp lại nóng)

Khóa, mặc định và ràng buộc (`DefaultStrategy`, `Rules`, `Rules[].When`): [bảng tham chiếu](CONFIGURATION_REFERENCE.md#tadssrouting).

Ví dụ: giờ cao điểm dùng `MockAlternativeDecisionStrategy`, còn lại dùng mặc định:

```json
"Tadss": { "Routing": { "DefaultStrategy": "DefaultDecisionStrategy",
  "Rules": [ { "Name": "peak-hour", "Strategy": "MockAlternativeDecisionStrategy", "When": { "PeakHour": true } } ] } }
```

Tên strategy phải có trong registry (sai thì không khởi động). Đổi `Tadss:Routing` trong file cấu hình đang chạy (`appsettings*.json` có `reloadOnChange`; biến môi trường không nạp lại khi chạy) có hiệu lực ngay; bản mới sai thì **giữ bảng cũ**, ghi log Error và tăng `tadss.config.reload_failures{section="Tadss:Routing"}`. Mỗi strategy có pipeline chịu lỗi và circuit breaker riêng nên strategy này lỗi không làm strategy khác fast-fail.

### `Tadss:Shadow` — chạy thêm một strategy ứng viên ở nền

Khóa, mặc định và ràng buộc: [bảng tham chiếu](CONFIGURATION_REFERENCE.md#tadssshadow). Ví dụ bật shadow với `MockAlternativeDecisionStrategy` cho mọi request (cần khởi động lại):

```json
"Tadss": { "Shadow": { "Enabled": true, "Strategy": "MockAlternativeDecisionStrategy" } }
```

Sau khi bản `PRIMARY` của chính request đã lưu, một worker nền chạy strategy shadow (pipeline riêng, không cache, cùng mốc thời gian) và ghi bản `role = SHADOW` (không khóa idempotency; ghép cặp với `PRIMARY` qua `reservation_id`, `request_hash`, `created_at`, `correlation_id`). Shadow không đổi response, không vào hàng chờ duyệt, không override/outcome được (404). Shadow lỗi/treo thì có bản `SHADOW` là `FALLBACK` hoặc chỉ ghi log + `tadss.shadow.executions{outcome="failed"}`.

### `Tadss:Cache` — khóa và circuit breaker Redis

Khóa, mặc định và ràng buộc (gồm `InstanceName` và bốn tham số breaker `RedisBreaker*`): [bảng tham chiếu](CONFIGURATION_REFERENCE.md#tadsscache). Ví dụ đặt tiền tố khóa riêng bằng biến môi trường: `Tadss__Cache__InstanceName=tadss-pt:`.

Breaker mở thì request bỏ qua Redis ngay (dùng memory nếu `EnableFallbackToMemory`), không chờ `RedisTimeoutMs`; `tadss.cache.requests{result="bypass"}` đếm các lần đó. Lỗi cache không bao giờ kích hoạt fallback của strategy. Breaker (Redis và strategy) và cache memory đều **theo từng instance**.

### `Tadss:Testing:FaultInjection` — tiêm lỗi strategy (chỉ môi trường `Testing`)

Chỉ có tác dụng khi `ASPNETCORE_ENVIRONMENT=Testing` và `Enabled=true`; ở môi trường khác bật cờ này chỉ sinh Warning, không bọc strategy. Khóa, mặc định và ràng buộc (`Profile`, `DelayMs`, `TargetStrategy`): [bảng tham chiếu](CONFIGURATION_REFERENCE.md#tadsstestingfaultinjection). Ví dụ làm strategy `DefaultDecisionStrategy` treo 1 giây: `Tadss__Testing__FaultInjection__Enabled=true`, `Tadss__Testing__FaultInjection__Profile=Delay`, `Tadss__Testing__FaultInjection__TargetStrategy=DefaultDecisionStrategy`.

Dùng cho kịch bản sự cố dưới tải (LT-03). Strategy bị bỏ qua token vẫn bị cắt đúng hạn (pipeline cách ly nó khỏi luồng request) nhưng CPU của nó chỉ dừng khi nó tự kết thúc — xem giới hạn ở `docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md`.

### Metric và log

Meter `TADSS.Runtime` (13 instrument, khai báo ở `TadssMetricNames`): quyết định, thời gian request và pipeline, fallback, trạng thái breaker, cache, hàng chờ duyệt, override, shadow, nạp lại cấu hình. Xuất qua OpenTelemetry (OTLP) khi `Tadss:Telemetry:Enabled = true` (mặc định tắt); `docker compose up` bật sẵn và gửi tới Grafana. Lỗi lệnh SQL mong đợi (tranh chấp idempotency `23505`) chỉ ghi ở mức Debug; lỗi thật vẫn ghi ở Warning/Error bởi store và exception handler.

---

## Observability và chế độ đánh giá

### Observability (OpenTelemetry + Grafana)

- **Chế độ vận hành** (`docker compose up -d --build`): TADSS gửi trace, metric và log tới `grafana/otel-lgtm` (cổng OTLP `4317`); mở `http://localhost:3001`, thư mục **TADSS** có dashboard **TADSS Runtime** nạp sẵn (29 panel hiển thị trong 7 nhóm: tổng quan, quyết định, chịu lỗi, cache, quản trị và shadow, tải k6, log và trace; biến `instance`; annotation `chaos`).
- **`dotnet run` hằng ngày:** telemetry tắt (không cần collector). Muốn xem dashboard khi chạy tay, bật collector riêng và trỏ TADSS tới nó:
  `MSYS_NO_PATHCONV=1 docker run --rm -p 3001:3000 -p 4317:4317 -v "$(pwd -W)/docker/grafana/provisioning/dashboards/tadss.yaml:/otel-lgtm/grafana/conf/provisioning/dashboards/tadss.yaml:ro" -v "$(pwd -W)/docker/grafana/dashboards:/otel-lgtm/tadss-dashboards:ro" grafana/otel-lgtm:0.34.0` (Git Bash; thiếu `MSYS_NO_PATHCONV=1` và `$(pwd -W)` thì Git Bash đổi đường dẫn trong container thành đường dẫn Windows; trong PowerShell dùng `"${PWD}/…"` và bỏ `MSYS_NO_PATHCONV=1`), rồi đặt `Tadss__Telemetry__Enabled=true` và `Tadss__Telemetry__OtlpEndpoint=http://localhost:4317`. Collector chết hoặc không tới được không ảnh hưởng request.
- Kiểm tự động (không cần Neon/ngrok): `bash tools/observability/verify-observability.sh` (OB-2 dashboard đã nạp, OB-4 có `tadss_decisions_total` sau vài request); bước (a) của lượt đánh giá đã chạy sẵn script này.

Khóa `Tadss:Telemetry:*`, mặc định và ràng buộc: [bảng tham chiếu](CONFIGURATION_REFERENCE.md#tadsstelemetry). Mặc định `Enabled=false` (không đăng ký provider OpenTelemetry nào).

### Chế độ đánh giá một lệnh

Dựng stack cô lập (hai instance TADSS sau nginx, instance riêng cho dữ liệu khách sạn, PostgreSQL 16, Redis 7, Toxiproxy, Grafana) và một container **evaluator** chạy tuần tự sáu bước: (a) toàn bộ test gộp theo mã danh mục + `verify-expose.sh` + `verify-observability.sh` (OB-2/OB-4 của chế độ vận hành) + `verify-export.sh` + ba bộ pytest của `tools/` (`evaluation`, `evidence`, `docs`) + `python tools/docs/check_docs.py` (DOC-1…3) + kiểm dashboard; (b) k6 LT-01, LT-02 (và LT-05 ở bản đầy đủ); (c) SCALE-2 rồi sự cố dưới tải LT-03; (d) Schemathesis CT-03, coverage gộp, Stryker MT-01…03 (chỉ bản đầy đủ); (e) **dữ liệu thực tế**: replay booking SmashCourt (RP) và dataset khách sạn (PT); (f) gom Evidence Pack `evidence/<run_id>/`. Dữ liệu SmashCourt (`data/private/smashcourt_bookings.csv`) là **tùy chọn**: thiếu thì phần RP bị bỏ qua, các phần khác vẫn chạy.

**Hướng dẫn chạy từng bước, đọc kết quả, chạy lại một bước, xử lý sự cố, dọn Docker: [`docs/guides/RUNBOOK.md`](RUNBOOK.md).** Các lệnh chính (chạy trong thư mục `TADSS-Runtime/`):

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

---

## Chạy bằng Docker và expose qua ngrok

Chỉ **một lệnh** `docker compose up -d --build` chạy TADSS, ngrok và Grafana cùng lúc (SmashCourt-BE ở máy khác, demo), kèm một lượt đánh giá tự chạy trong stack cô lập (D-14, mục "Chế độ đánh giá một lệnh"). `docker-compose.yml` là file compose duy nhất, không profile, cấu hình **giống SmashCourt-BE**: `tadss-runtime` chạy `ASPNETCORE_ENVIRONMENT=Development` (cổng host chỉ `127.0.0.1`), `ngrok` chỉ cần `NGROK_AUTHTOKEN` (không domain tĩnh), Grafana (`grafana/otel-lgtm`). Database là Neon, cache là Redis Cloud và API key đều khai báo trong `src/TADSS.Runtime.Api/appsettings.Development.json` (file duy nhất `dotnet run` và Docker dùng chung): từ Epic 6C (P6-33) file đó **không vào image** (`.dockerignore` loại nó) mà được compose **mount chỉ đọc** vào container lúc chạy. `.env` chỉ chứa `NGROK_AUTHTOKEN`.

1. Thêm API key **thật** vào `src/TADSS.Runtime.Api/appsettings.Development.json` (không commit), mục `Tadss:Security:ApiKeys`: một phần tử `{ "ClientId": "smashcourt-be", "KeySha256": "<mã băm>" }` (cách sinh khóa và mã băm: [Tạo API key và mã băm](#tạo-api-key-và-mã-băm)). **Không** dùng khóa local công khai `tadss-local-dev-key`: ở `Development` validator vẫn chấp nhận nó nên không có gì chặn bạn expose nhầm. SmashCourt-BE dùng cùng một khóa cho cả `dotnet run` lẫn Docker. Chuỗi kết nối Neon và Redis Cloud trong file đó cũng được container dùng; schema `tadss` trên Neon do bạn migrate sẵn (`dotnet ef database update`), container **không bao giờ tự migrate**.
2. Tạo mạng dùng chung với SmashCourt-BE/FE (một lần): `docker network create smashcourt-network`.
3. Tạo `.env` từ `.env.example` (không commit) và điền `NGROK_AUTHTOKEN` (lấy ở dashboard.ngrok.com). Biến `TADSS_API_KEY_*` trong `.env` do `tools/security/hash_api_key.py` ghi (key gốc cho BE), compose không dùng.
4. Chạy tất cả: `docker compose up -d --build`. TADSS ở `http://localhost:8081`, Grafana ở `http://localhost:3001`. Đổi `appsettings.Development.json` thì chỉ cần tạo lại container (`docker compose up -d --force-recreate tadss-runtime`), không phải build lại image (file được mount, không nằm trong image).
5. Lấy URL công khai của ngrok: mở `http://127.0.0.1:4042` (cổng host 4042 vì ngrok của SmashCourt-BE dùng 4040, của SmashCourt-AI dùng 4041) hoặc chạy `docker compose logs ngrok`. Gói miễn phí của ngrok cấp cho mỗi tài khoản một domain dev cố định, nên URL giữ nguyên qua các lần khởi động lại; TADSS dùng tài khoản ngrok riêng (không chung token với BE). SmashCourt-BE gọi TADSS qua URL này: đặt vào `TADSS_BASE_URL` trong `.env` của BE (compose BE truyền thành `TadssConfig:BaseUrl`).
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

- **Secret không nằm trong image (Epic 6C).** `appsettings.Development.json` (chuỗi kết nối Neon, Redis Cloud, mã băm API key) được compose mount chỉ đọc vào container lúc chạy; image `ghcr.io/nguyenthanhtrung-n2t/tadss-runtime` không chứa file này nên đẩy lên registry được (kiểm bằng `bash tools/ci/check-image.sh <image>`). Image build trước Epic 6C (tên `tadss-tadss-runtime`) vẫn chứa file: xóa đi, không chia sẻ.
- **Container expose chạy `Development`.** Swagger (`/swagger`) công khai qua URL ngrok và validator `Tadss:Security` ở chế độ nới lỏng, nên API key trong file phải là khóa riêng thật; tuyệt đối không dùng `tadss-local-dev-key` khi expose.

Lưu ý khác:
- Trang theo dõi của ngrok ở `http://127.0.0.1:4042` hiển thị header của mọi request, kể cả `X-Api-Key`, và cho phép phát lại request. Chỉ dùng khi thử và dừng stack ngay sau đó.
- Nâng phiên bản ngrok hoặc Grafana bằng cách sửa tag của `image` trong `docker-compose.yml`.
- Cần Docker Compose >= 2.24.4 (`tools/expose/verify-expose.sh` kiểm tra).

### SmashCourt-BE gọi TADSS như thế nào

| BE chạy | TADSS chạy | `TadssConfig.BaseUrl` của BE |
| :--- | :--- | :--- |
| Trong Docker (cùng `smashcourt-network`) | Compose | `http://tadss-runtime:8080` |
| `dotnet run` trên máy host | Compose | `http://localhost:8081` |
| `dotnet run` trên máy host | `dotnet run` (không container) | `http://localhost:5296` |
| Trong Docker | `dotnet run` trên máy host | `http://host.docker.internal:5296` |
| Máy khác / demo | Compose (service ngrok) | URL ngrok của TADSS (domain dev cố định của tài khoản), xem `http://127.0.0.1:4042` (xem NG-4) |

### Cấu hình và biến

Cấu hình của `tadss-runtime` (không biến môi trường nào chứa secret). Toàn bộ khóa `Tadss:*` và thứ tự nguồn cấu hình: [`docs/guides/CONFIGURATION_REFERENCE.md`](CONFIGURATION_REFERENCE.md):

| Mục trong `appsettings.Development.json` | Ý nghĩa |
| :--- | :--- |
| `ConnectionStrings:DefaultConnection` | Chuỗi kết nối PostgreSQL của Neon dạng Npgsql `key=value` với `SSL Mode=Require` (URI `postgresql://...` không được chấp nhận) |
| `Tadss:Cache:RedisConnectionString` | Kết nối Redis Cloud (dạng và ràng buộc: [bảng tham chiếu](CONFIGURATION_REFERENCE.md#tadsscache)) |
| `Tadss:Security:ApiKeys` | `ClientId` `smashcourt-be` + SHA-256 hex của khóa riêng (không dùng mã băm của khóa local công khai); ràng buộc: [bảng tham chiếu](CONFIGURATION_REFERENCE.md#tadsssecurity) |

Biến trong `.env` (mẫu: `.env.example`):

| Biến | Ý nghĩa |
| :--- | :--- |
| `NGROK_AUTHTOKEN` | Authtoken tài khoản ngrok (biến bắt buộc duy nhất của compose) |

Ở `Development` (compose) thiếu API key thì mọi `/api/*` trả 401; ở `Staging`/`Production` thiếu API key thì app không khởi động (log nêu `Tadss:Security`). Không ghi giá trị thật vào file nào trong repo.

### Tự kiểm tra cấu hình (không cần tài khoản ngrok)

```bash
bash tools/expose/verify-expose.sh
```

Script dựng project compose riêng `tadss-expose-check` (không đụng project `tadss` đang chạy của bạn; khóa ngẫu nhiên không in ra, không mở tunnel, chỉ khởi động `tadss-runtime` (không khởi động `ngrok`/`grafana`), không kết nối database ngoài) rồi in bảng kết quả và dọn container, volume, image. Mạng `smashcourt-network` chỉ được tạo nếu chưa có và chỉ bị xóa khi chính script tạo ra nó. Image dựng ra không chứa `appsettings.Development.json` (Epic 6C) và container thử không mount file đó (máy CI không có file này): script sinh một file override tạm (thư mục tạm, xóa khi thoát) thêm PostgreSQL/Redis dùng một lần và đặt biến môi trường `ConnectionStrings__DefaultConnection`, `Tadss__Cache__RedisConnectionString`, `Tadss__Security__ApiKeys__0__*` (mã băm của khóa ngẫu nhiên) cho `tadss-runtime` để ghi đè file json, đổi cổng publish sang một cổng trống và bật migration CHỈ cho database tạm đó, nên không bao giờ chạm Neon/Redis thật. Image build ra bị xóa khi script thoát. Kiểm theo danh mục NG: NG-1 (`docker compose config` hợp lệ với `NGROK_AUTHTOKEN` giả và `config --services` liệt kê đủ `tadss-runtime`, `ngrok`, `grafana`; `Development`, chỉ mount đúng `appsettings.Development.json` ở chế độ chỉ đọc, migration `false`, compose không chứa biến secret), NG-2 (dòng `NG-2a`: `/health/ready` 200, POST không khóa 401 và có khóa 200 (Swagger được phục vụ ở Development nên không kiểm 404); dòng `NG-2b`: Staging không có khóa thì không khởi động, log nêu `Tadss:Security`, không lộ chuỗi kết nối), NG-3 (GET theo `decisionId` 200 đúng quyết định vừa ghi, DB có bản ghi), NG-5 (mọi cổng của compose, cả cấu hình lẫn đang publish, chỉ ở `127.0.0.1`), SEC-6 (user non-root). CI chạy script này ở job `expose-check`.

### NG-4: thử qua tunnel thật (người dùng tự chạy)

1. Sinh khóa và mã băm (mục Bảo mật) rồi đặt mã băm vào `appsettings.Development.json` (`Tadss:Security:ApiKeys`, ClientId `smashcourt-be`; cùng file có chuỗi kết nối Neon/Redis). Tạo `.env` từ `.env.example`, điền `NGROK_AUTHTOKEN`. Đảm bảo schema `tadss` trên Neon đã migrate.
2. Tạo mạng nếu chưa có (`docker network create smashcourt-network`) rồi chạy `docker compose up -d --build`; chờ `tadss-runtime` healthy.
3. Lấy URL công khai: `curl -s http://127.0.0.1:4042/api/tunnels` hoặc mở `http://127.0.0.1:4042` (giao diện kiểm tra của ngrok, chỉ loopback), hoặc `docker compose logs ngrok`. **Lưu ý bảo mật:** giao diện này lưu header của mọi request đi qua tunnel, kể cả `X-Api-Key`, ở dạng chữ thường (đọc được qua `http://127.0.0.1:4042/api/requests/http`) và cho phép phát lại request; vì vậy chỉ nghe loopback, và nên dừng stack ngay sau khi thử NG-4 (bước 5).
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

   Rồi `GET $URL/api/v2/decisions/<decisionId>` với `X-Api-Key` (200) và kiểm tra request xuất hiện ở `http://127.0.0.1:4042`.
5. Dừng: `docker compose down`.

Phía SmashCourt-BE: ghi chú triển khai ở [`docs/contracts/INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md) mục 4.5.
