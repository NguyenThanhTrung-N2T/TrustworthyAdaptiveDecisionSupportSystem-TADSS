# PHASE 6 — GÓI 6C-1: ĐÓNG GÓI VÀ CẤU HÌNH (BE + TADSS + AI)

| | |
| :--- | :--- |
| **Thuộc** | [PHASE6_PLAN_6C_DEVOPS_OBSERVABILITY_PERF.md](PHASE6_PLAN_6C_DEVOPS_OBSERVABILITY_PERF.md), giai đoạn 1A (task 1.1–1.15, 1.43–1.48) |
| **Trạng thái** | Bản 1 (31/10/2026), chờ duyệt; task 1.1, 1.2, 1.43 (danh mục cấu hình) đã làm, kết quả ở Mục 2 |
| **Mục tiêu** | Mỗi commit build ra **một image không chứa bí mật**, tự khai phiên bản, chạy được ở mọi môi trường chỉ bằng biến môi trường; BE có health check và báo lỗi cấu hình ngay khi khởi động. Không đổi hành vi nghiệp vụ |
| **Không làm ở gói này** | Đẩy image lên GHCR (6C-2), OpenTelemetry (6C-3), compose triển khai `deploy/` (1.27) |
| **Xong khi (cổng 1A)** | OPS-01…06, AI-UT-01…05, AI-CT-01, SEC-AI-1 đạt; toàn bộ suite BE, TADSS, AI xanh; lệnh chạy local hằng ngày của người dùng vẫn chạy được |

---

## 1. Quyết định của gói

| Mã | Quyết định |
| :--- | :--- |
| K1 | **Chạy local vẫn như cũ:** compose trong từng repo (dùng hằng ngày) **mount chỉ đọc** `appsettings.Development.json` vào container (`./appsettings.Development.json:/app/appsettings.Development.json:ro`); file không vào image. Môi trường triển khai (`deploy/`, chạy bởi `deploy.yml`) không mount gì, chỉ dùng biến môi trường (P6-34) |
| K2 | TADSS: luật NG-1 (hiện "tadss-runtime không mount") nới thành "chỉ cho phép đúng một mount chỉ đọc là `appsettings.Development.json`"; sửa `verify-expose.sh` và mô tả NG-1 trong methodology cho khớp |
| K3 | Tên image (chữ thường, GHCR): `ghcr.io/nguyenthanhtrung-n2t/smashcourt-be`, `…/tadss-runtime`, `…/smashcourt-ai`; compose local dùng `image: …:${IMAGE_TAG:-local}` + `build:` (build tại chỗ thì gắn tag `local`) |
| K4 | Kiểm cấu hình khi khởi động (BE): chỉ khóa **cần ngay để phục vụ** (DB, JWT, OTP) chặn khởi động; khóa của tính năng phụ (SMTP, VNPay, Cloudinary, Google) **cảnh báo** ở log nếu thiếu, không chặn — để môi trường đo (`perf`) và test không phải điền khóa thật. Không in giá trị |
| K5 | Health của BE: `/health/live`, `/health/ready` (PostgreSQL), `/health/dependencies` (TADSS `/health/live`, AI `/health`, timeout 3 giây mỗi bên, luôn trả 200 kèm trạng thái từng bên); không cần đăng nhập, miễn rate limit, không lộ URL hay cấu hình |
| K6 | Phiên bản: build nhận `GIT_SHA`, `BUILD_VERSION` ⇒ nhãn OCI + biến `APP_VERSION`, `APP_REVISION` trong image; metric `build_info` đọc hai biến này. CD kiểm sha bằng nhãn image (`docker inspect`), không thêm endpoint vào TADSS |
| K7 | Một script kiểm image dùng chung (chép vào ba repo): `tools/ci/check-image.sh <image>` — liệt kê file trong image (`docker create` + `docker export`) và báo đỏ nếu có `appsettings.Development.json`, `appsettings.*.local.json`, `.env`, `*.pfx`, `*.pem`; kiểm user chạy không phải root; kiểm có nhãn `org.opencontainers.image.revision` |

---

## 2. Danh mục cấu hình (task 1.1, 1.2, 1.43 — đã làm)

Cột *Nguồn khi triển khai*: **S** = GitHub Environment secret, **V** = Environment variable (không bí mật), **M** = mặc định trong `appsettings.json` đủ dùng. Cột *Khi thiếu*: hành vi theo K4.

### 2.1. SmashCourt-BE (quét `Configuration[...]`, `GetSection`, `GetValue`, các lớp `*Settings`)

| Khóa | Bí mật | Code đọc ở | Khi thiếu | Nguồn khi triển khai | Ghi chú |
| :--- | :---: | :--- | :--- | :---: | :--- |
| `ConnectionStrings:DefaultConnection` | ✔ | `Program.cs` (EF, Hangfire) | **Chặn khởi động** | S | Mỗi môi trường một database |
| `Jwt:Key` | ✔ | `JwtSettings` | **Chặn** | S | |
| `Jwt:Issuer`, `Jwt:Audience` | | `JwtSettings` | **Chặn** | V | |
| `Jwt:AccessTokenExpirationMinutes`, `Jwt:RefreshTokenExpirationDays` | | `JwtSettings` | — | M | |
| `Otp:HmacSecret` | ✔ | OTP | **Chặn** | S | |
| `Smtp:Host`, `Smtp:Port`, `Smtp:EnableSsl` | | `EmailService` | Cảnh báo | M / V | `perf` trỏ Mailpit |
| `Smtp:Username`, `Smtp:Password` | ✔ | `EmailService` | Cảnh báo (lỗi lúc gửi) | S | |
| `Smtp:From` | | `EmailService` | Dùng Username | V | |
| `VnPay:TmnCode`, `VnPay:HashSecret` | ✔ | `VnPayService` | Cảnh báo (lỗi lúc thanh toán) | S | `perf` dùng khóa giả |
| `VnPay:CallbackUrl` | | `VnPayService` | Cảnh báo | V | |
| `VnPay:BaseUrl`, `VnPay:Version`, `VnPay:OrderType` | | `VnPayService` | — | M | |
| `VnPay:IpnUrl` | | **không code nào đọc** | — | — | Giữ làm ghi chú (khai ở trang merchant VNPay); theo quy tắc người dùng không xóa |
| `Google:ClientId`, `Google:RedirectUri` | | `GoogleSettings` | Cảnh báo | V | |
| `Google:ClientSecret` | ✔ | `GoogleSettings` | Cảnh báo | S | |
| `Cloudinary:CloudName` | | `CloudinarySettings` | Cảnh báo | V | Đã thêm vào `appsettings.json` (bản mẫu) 31/10 |
| `Cloudinary:ApiKey`, `Cloudinary:ApiSecret` | ✔ | `CloudinarySettings` | Cảnh báo | S | |
| `FrontendBaseUrl` | | 6 chỗ | Mặc định `http://localhost:3000` | V | |
| `AIService:BaseUrl` | | `Program.cs` | Mặc định `http://localhost:8000` | V | URL ngrok của AI |
| `TadssConfig:EnableTadssDecision`, `TimeoutMs`, `ApprovedPaymentWindowMinutes`, `BranchTimeZone` | | `TadssSettings` | — | M / V | Cờ theo môi trường |
| `TadssConfig:BaseUrl` | | `TadssSettings` | Bắt buộc khi cờ bật (validator có sẵn) | V | `.env` BE: `TADSS_BASE_URL` |
| `TadssConfig:ApiKey` | ✔ | `TadssSettings` | Bắt buộc khi cờ bật | S | `.env` BE: `TADSS_API_KEY`; đã thêm vào `appsettings.json` (bản mẫu, trống) 31/10 |
| `Cookie:Secure`, `Cookie:RefreshTokenExpirationDays` | | `CookieSettings` | — | M / V | |
| `Hangfire:Enabled`, `Hangfire:Dashboard`, `Hangfire:DashboardPath` | | `HangfireExtensions` | — | M | |
| `BookingPayment:*` (6 khóa) | | `BookingPaymentSettings` (+ validator) | — | M | |
| `RateLimiting:*` (6 policy), `IpRateLimiting`, `IpRateLimitPolicies` | | `RateLimitSettings`, AspNetCoreRateLimit | — | M | `perf` nâng giới hạn |
| `BCrypt:WorkFactor` | | Auth | Mặc định trong code | M | |
| `Logging:*` | | ASP.NET | — | M | |

### 2.2. TADSS-Runtime

Danh mục đầy đủ đã có và được kiểm tự động (DOC-4): [`docs/guides/CONFIGURATION_REFERENCE.md`](../docs/guides/CONFIGURATION_REFERENCE.md). Khóa bí mật khi triển khai: `ConnectionStrings:DefaultConnection` (S), `Tadss:Cache:RedisConnectionString` (S), `Tadss:Security:ApiKeys:0:ClientId` (V) + `KeySha256` (S). TADSS đã kiểm cấu hình khi khởi động (fail-closed ngoài `Development`/`Testing`); gói này không đổi.

### 2.3. SmashCourt-AI

| Khóa | Bí mật | Code đọc ở | Khi thiếu | Nguồn |
| :--- | :---: | :--- | :--- | :---: |
| `GEMINI_API_KEY` | ✔ | `app/services/llm_service.py` | Gọi Gemini lỗi | S |
| `GEMINI_MODEL` | | `llm_service.py` (mặc định trong code `gemini-2.0-flash`, đã deprecate; `.env` đặt `gemini-3.6-flash`) | Dùng mặc định trong code | V |
| `APP_ENV` | | `app/main.py` | `development` | V |
| `GEMINI_BASE_URL` (mới, task 1.46) | | `llm_service.py` | Gọi Google thật | V (chỉ `perf`) |

---

## 3. Task và các bước

Mọi task có code: viết test trước (thấy đỏ), sửa, build + test xanh, không tự commit.

### T1.4 — `.dockerignore` BE + script kiểm image (OPS-01)
1. Viết `tools/ci/check-image.sh` (K7) và chạy trên image BE hiện tại ⇒ **phải đỏ** (đang có `appsettings.Development.json`).
2. Thêm vào `.dockerignore` của BE: `appsettings.Development.json`, `appsettings.*.local.json` (`.env` đã có).
3. Build lại ⇒ script xanh. `dotnet run` local không đổi.

### T1.5 — Như T1.4 cho TADSS
Sửa dòng chú thích SEC-6 trong `.dockerignore` (bỏ "CÓ vào image"); thêm loại trừ; script xanh.

### T1.6 — BE non-root (OPS-02)
1. Script T1.4 đã kiểm user ⇒ đỏ với image hiện tại.
2. `Dockerfile` BE: `USER $APP_UID` ở stage cuối (image `aspnet:8.0-alpine` có sẵn user `app`), như TADSS.
3. Chạy container, kiểm BE khởi động, ghi được những gì cần ghi (DataProtection key nằm trong home của user `app`).

### T1.7 — Nhãn phiên bản (cả ba repo)
`ARG GIT_SHA`, `ARG BUILD_VERSION` ⇒ `LABEL org.opencontainers.image.revision/version/source/created` và `ENV APP_REVISION`, `APP_VERSION`. Build với `--build-arg GIT_SHA=$(git rev-parse HEAD)`; `docker inspect` thấy đúng.

### T1.8 — BE kiểm cấu hình khi khởi động (OPS-03)
1. Test: thiếu `ConnectionStrings:DefaultConnection` / `Jwt:Key` / `Jwt:Issuer` / `Jwt:Audience` / `Otp:HmacSecret` ⇒ host không khởi động, thông báo nêu đúng tên khóa, **không chứa giá trị**; thiếu khóa nhóm "cảnh báo" ⇒ host khởi động, log Warning nêu tên khóa.
2. Lớp `Configurations/RequiredConfiguration.cs` (danh sách khóa theo Mục 2.1) gọi trong `Program.cs` trước `Build()`; host test HTTP (`Testing`) đã đặt đủ khóa nên không bị ảnh hưởng (kiểm bằng toàn suite).

### T1.9 — `/health/live`, `/health/ready` (OPS-04)
1. Test HTTP: live 200 luôn; ready 200 khi DB có; DB sai chuỗi kết nối ⇒ ready 503, live 200; không cần token; không bị rate limit.
2. `AddHealthChecks()` với một check DB (`Database.CanConnectAsync`), `MapHealthChecks` hai đường dẫn (live: không check nào).

### T1.10 — `/health/dependencies` (OPS-04)
1. Test (WireMock giả TADSS và AI): cả hai sống ⇒ `{tadss: up, ai: up}`; TADSS dừng ⇒ `tadss: down`, HTTP vẫn 200, `/health/ready` vẫn 200; quá 3 giây ⇒ `down`; body không chứa URL.
2. Endpoint dùng `IHttpClientFactory`, timeout riêng.

### T1.11 — `build_info` BE
Meter `SmashCourt.BE`, gauge `build_info{service,version,revision}` = 1, đọc `APP_VERSION`/`APP_REVISION` (thiếu ⇒ `unknown`). Test bằng `MeterListener`. Chưa có exporter (6C-3 thêm).

### T1.12 — `build_info` TADSS
Thêm tên vào `TadssMetricNames`, instrument vào `TadssMetrics` (quy ước có sẵn), đọc từ cấu hình/biến môi trường ở composition root. Test OB-5 kiểu cũ (`MetricRecorder`). Test kiến trúc UT-14 phải xanh. Không đổi hợp đồng v2.

### T1.13 — Compose BE dùng image + mount (K1, K3)
`image: ghcr.io/nguyenthanhtrung-n2t/smashcourt-be:${IMAGE_TAG:-local}` + `build:` + mount chỉ đọc `appsettings.Development.json`. Chạy `docker compose up -d --build` như người dùng vẫn làm ⇒ BE khỏe, đọc đủ cấu hình.

### T1.14 — Compose TADSS (K1, K2, K3)
Như T1.13; nới NG-1 trong `verify-expose.sh` + methodology; chạy `verify-expose.sh` (NG-1…5) và `ComposeFixtureAlignmentTests`.

### T1.15 — Cùng image, ba cấu hình (OPS-06)
Chạy một image BE ba lần với ba bộ biến môi trường (giả `stable`, `preview`, `perf`, DB tạm), không mount gì ⇒ cả ba khỏe; xóa container tạm.

### T1.43–1.48 — SmashCourt-AI
| Task | Bước |
| :--- | :--- |
| 1.43 | Bảng 2.3 (đã làm) |
| 1.44 | Nhãn phiên bản (T1.7); `tools/ci/check-image.sh` cho AI (`.env` đã ngoài image) |
| 1.45 | Dựng `pytest` + `httpx` test client + LLM giả (thay hàm gọi Gemini); AI-UT-01 dựng prompt, 02 đọc/kiểm response, 03 lỗi/timeout/429 của Gemini, 04 schema vào/ra, 05 `/health` |
| 1.46 | `llm_service.py` đọc `GEMINI_BASE_URL` (tùy chọn) để trỏ LLM giả ở `perf`; **không** đổi model mặc định trong code (người dùng chốt: model đổi trong `.env`) |
| 1.47 | AI-CT-01 trong test BE: so DTO `Integrations/AI/DTOs` với `app/models/ai_schemas.py` (tự bỏ qua khi không có thư mục AI cạnh repo, như các test đối chiếu khác) |
| 1.48 | Compose AI: `image: …/smashcourt-ai:${IMAGE_TAG:-local}` + `build:`; giữ `ngrok-ai`; cổng `8000` chỉ bind `127.0.0.1` (SEC-AI-1) |

---

## 4. Rủi ro

| Rủi ro | Giảm thiểu |
| :--- | :--- |
| Bỏ file khỏi image làm hỏng lệnh chạy local | K1 mount chỉ đọc; T1.13, T1.14 kiểm bằng đúng lệnh người dùng dùng |
| Kiểm cấu hình làm đỏ test HTTP | Chỉ chặn 5 khóa cốt lõi mà host test đã đặt; chạy toàn suite BE |
| Non-root làm BE không ghi được file | Kiểm khởi động + một luồng có ghi (DataProtection) ở T1.6 |
| Đổi NG-1 của TADSS | Nới đúng một mount chỉ đọc, sửa script + methodology cùng lúc, chạy lại NG-1…5 |

## 5. Kết quả

(ghi khi làm)
