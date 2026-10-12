# Task 11 report — tài liệu, skill và kiểm chứng cuối gói 5.3 (báo cáo gói)

Trạng thái: DONE (kèm vài ghi chú ở mục Concerns). Nhánh `feat/phase-5-contract-v2-test-infra`, BASE 6bff080, không push.

Commit:
- `257db1c` docs: remove stale package 5.3 wording from override contract (XML doc `OverrideRequestDto` + tái sinh `openapi-v2.yaml` + 1 ca âm schema `reject-with-null-deposit`)
- `7150628` docs: governance, security and ngrok exposure (package 5.3)

## 1. Kiểm chứng cuối gói (số liệu thật)

| # | Kiểm chứng | Kết quả |
| :-: | :--- | :--- |
| 1 | `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror --no-incremental` (chạy lại sau commit cuối) | 0 Warning, 0 Error |
| 2 | Unit (`TADSS.UnitTests`) | **1504 passed**, 0 failed, 0 skipped (đầu gói 1112: +392) |
| 2 | Integration (Docker/Testcontainers) | **186 passed**, 0 failed, 0 skipped (đầu gói 121: +65) |
| 3 | `git diff 05aa840 HEAD -- docs/contracts/baseline_public_api.txt` | **rỗng** (0 dòng). Lệch so với brief gốc "đúng 2 dòng" theo ruling R6: hằng `ErrorCodes.RateLimited`/`PayloadTooLarge` không thuộc bề mặt baseline (`PublicApiSurfaceTests` chỉ liệt kê ctor/property/method); giá trị được khóa bởi `WireCodeValuesTests` |
| 3 | `openapi-v2.yaml` so với 05aa840 | +537/−3 dòng, thuần cộng thêm; `OpenApiDriftTests` xanh (trong 1504 unit) |
| 4 | `dotnet ef migrations list --no-connect` (env R4 + `Testing`) | đúng 4 ID: `20260912105336_InitialCreate`, `20260915152502_UpdateUniqueIndexes`, `20260930190021_AddCreatedAtIndex`, `20261002101457_DecisionPersistenceV2` |
| 4 | `dotnet ef migrations has-pending-model-changes` | "No changes have been made to the model since the last migration." |
| 5 | `bash tools/expose/verify-expose.sh` | TẤT CẢ ĐẠT (bảng bên dưới) |
| 6 | `docker compose config -q` (biến giả) cho base, base+ngrok, base+neon, base+ngrok+neon | cả 4 rc=0 |
| 7 | App thật `Staging` (Release build), chuỗi kết nối tới cổng không có dịch vụ (59998), Redis `Memory`, 1 mã băm test | `/health/live` = 200 sau ~3 giây; `/health/ready` 503 (không có DB, đúng); không khóa → 401. Không khóa API ở `Staging`: thoát mã **127** sau 1 giây, log `OptionsValidationException: Tadss:Security:ApiKeys ... không được rỗng ngoài môi trường Development/Testing`, không chứa chuỗi kết nối/mật khẩu/cổng. Mọi tiến trình `dotnet` do mình khởi động đã dừng (PID kiểm theo chủ cổng 18555 trước khi `taskkill`) |
| 8 | Dọn dẹp | không còn container/volume/network của kiểm chứng; image tạm `t11-logcheck` đã xóa; file tạm ngoài repo đã xóa; `git status` sạch (không có thay đổi chưa commit) |

Kết quả `verify-expose.sh` (lần chạy cuối):

```
Code    State Detail
NG-1    PASS  config hợp lệ cho ngrok, ngrok+neon và neon riêng; thiếu TADSS_DB_CONNECTION/TADSS_API_KEY_SHA256 bị từ chối
NG-3    PASS  ready=200 không-khóa=401 có-khóa=200 GET=200 swagger=404 bản-ghi=1
NG-5    PASS  4 cổng publish; URL: 127.0.0.1
NG-2    PASS  thoát mã 139 sau 1s; log nêu Tadss:Security, không lộ chuỗi kết nối
SEC-6   PASS  Config.User='1654'
TẤT CẢ ĐẠT.
```

Test mới theo mã danh mục (đếm `[Trait("CatalogId", ...)]` ở `tests/` giữa 05aa840 và HEAD; Theory tính một):

| Mã | Trước → sau | Mã | Trước → sau |
| :--- | :--- | :--- | :--- |
| UT-15 | 0 → 27 | SEC-1 | 0 → 7 |
| UT-08 | 58 → 80 (+22) | SEC-2 | 0 → 9 |
| UT-14 | 34 → 36 (+2) | SEC-3 | 0 → 9 |
| UT-11 | 46 → 51 (+5) | SEC-4 | 0 → 17 |
| UT-02 | 39 → 43 (+4) | SEC-5 | 0 → 11 |
| UT-01 | 67 → 69 (+2) | SEC-6 | 0 → 2 |
| UT-09 | 13 → 15 (+2) | SEC-7 | 0 → 4 |
| UT-12 | 8 → 10 (+2) | NG-1 | 0 → 1 |
| CT-01 | 21 → 30 (+9) | NG-4 | 0 → 1 |
| IT-06 | 0 → 5 | NG-5 | 0 → 2 |
| IT-07 | 0 → 4 | IT-09 | 0 → 5 |
| IT-08 | 0 → 3 | IT-10 | 0 → 6 |
| IT-11 | 0 → 4 | | |

NG-2 và NG-3 (chế độ A) kiểm bằng `verify-expose.sh` (không có `[Trait]`); NG-4 thật và NG-3 chế độ B do người dùng chạy.

## 2. Phụ lục A — đối chiếu spec → task, có trạng thái

| Yêu cầu (spec) | Task | Trạng thái / nơi |
| :--- | :---: | :--- |
| 4.3 `OverrideService` (tồn tại, `PRIMARY`, `NEED_APPROVAL`, chưa hết hạn, chưa có outcome; `UNIQUE`; replay; quy đổi cọc) | 2, 3, 4 | Xong: `Application/Governance/OverrideService`, `EfDecisionGovernanceStore`; IT-06/07/08, UT-15; INTEGRATION_SPEC §3.6, SAS 4.4.4 |
| 4.3 `OutcomeService` (trùng cùng payload → 200; khác → 409) | 2, 3, 4 | Xong: `Application/Outcomes/OutcomeService`; IT-10, UT-15; §3.7 |
| 4.3 hàng chờ theo B.5, IT-09 ≥ 5.000 bản ghi | 2, 3, 4 | Xong: `PendingApprovalsQuery` (dùng `idx_de_pending`), IT-09 kiểm `EXPLAIN`; §3.5 |
| 4.3 `ApiKeyAuthenticationMiddleware` (`{clientId, keySha256}`, `FixedTimeEquals`, miễn trừ, `clientId` vào log/trace) | 5 | Xong: `Middlewares/ApiKeyAuthenticationMiddleware`, `Security/ApiKeyVerifier`; IT-11, SEC-4. Lưu ý: dòng log hoàn tất request của Serilog không mang `ClientId` (README, SAS Mục 3) |
| 4.3 `X-Actor-Id` bắt buộc với override, lưu `actor_id` | 3, 4 | Xong: `Common/RequestHeaders`, §3.2 |
| 4.2.3 `DecisionStateConflictException` → 409 theo mã | 3 | Xong: `ExceptionClassifier`; §3.8 |
| 4.2.4 việc mang từ 5.2c (fail-fast chuỗi kết nối, đọc payload nới lỏng, 503 giả khi client hủy) | 1 | Xong: `DatabaseOptionsValidator`, `DecisionPayloadSerializer`, `PersistenceFailureTranslator` |
| 4.3.1 fail-closed ngoài Development/Testing; Swagger chỉ Development; `ForwardedHeaders` | 5, 6 | Xong: `SecurityOptionsValidator`, `UseTadssRequestPipeline`; NG-2 |
| 4.3.1 `docker-compose.ngrok.yml`, `docker-compose.neon.yml`, `.env.example`, tham số hóa, UT-09 compose ↔ fixture | 10 | Xong; ghi chú wording neon đã sửa ở task này |
| 4.3.1 NG-1, NG-2, NG-3 (A), NG-5 tự động; NG-4, NG-3 (B) người dùng | 10, 11 | Tự động: xong (`verify-expose.sh`, job CI `expose-check`). Người dùng: NG-4 và NG-3 chế độ B **chờ bạn chạy** (README, mục "Expose qua ngrok") |
| 4.3.2 SEC-1 rate limiting | 7 | Xong (7 test) |
| 4.3.2 SEC-2 giới hạn kích thước | 6 | Xong (9 test) |
| 4.3.2 SEC-3 security headers, HSTS, tắt `Server` | 6 | Xong (9 test) |
| 4.3.2 SEC-4 fail-closed (API key, chuỗi kết nối) | 1, 5 | Xong (17 test) |
| 4.3.2 SEC-5 health live/ready | 8 | Xong (11 test; `/health` gộp đã gỡ) |
| 4.3.2 SEC-6 container cứng | 9, 10 | Xong (`USER $APP_UID`, `.dockerignore`, CI + `verify-expose.sh`) |
| 4.3.2 SEC-7 lỗ hổng, lock file, Dependabot, log không chứa khóa | 5, 9 | Xong (lock file, `check-vulnerable-packages.sh`, ghim bắc cầu R7, `dependabot.yml`) |
| 4.3.2 thứ tự middleware khóa bằng UT-14 | 6, 7 | Xong (`SecurityPipelineOrderTests`, `RequestPipelineTests`) |
| 4.3.2 `RATE_LIMITED`, `PAYLOAD_TOO_LARGE` vào §3.8, schema lỗi, OpenAPI | 6, 7 | Xong (SDK `ErrorCodes`, §3.8, OpenAPI). Baseline không đổi (R6) |
| 6.1 "thiếu API key hoặc chuỗi kết nối → không khởi động" (NG-2, SEC-4) | 1, 5, 10 | Xong; kiểm lại thủ công ở bước 7 |
| 9 tài liệu (BACKLOG T5.30, README/`.env.example`, INTEGRATION_SPEC, EXPERIMENTAL_DESIGN) | 1, 4, 6, 10, 11 | Xong (xem mục 5) |
| Mục hoãn 5.2c: `detail` 400 chứa thông điệp STJ | 6 | Xong (F-23, `InvalidModelStateTranslator`) |

## 3. Bảng 6.1 của spec (các dòng thuộc 5.3)

| Bất biến | Test khóa | Kết quả |
| :--- | :--- | :--- |
| Thứ tự middleware cố định (gồm các middleware bảo mật) | UT-14: `SecurityPipelineOrderTests`, `RequestPipelineTests` | Đạt, xanh |
| Ngoài `Development`/`Testing`, thiếu API key hoặc connection string → app không khởi động | NG-2 (`verify-expose.sh`), SEC-4 (17 test), kiểm tay bước 7: thoát 127, log nêu `Tadss:Security`, không lộ chuỗi kết nối | Đạt |
| Phụ thuộc hướng vào trong, controller/middleware không dùng kiểu Infrastructure (dòng của 5.2d, còn hiệu lực) | UT-14 | Đạt (UT-14: 34 → 36 test) |
| Không tự áp migration lên Neon | IT-15 + `ComposeFixtureAlignmentTests` (neon ép `ApplyMigrationsOnStartup=false`) | Đạt |
| Public API SDK chỉ đổi có chủ đích | CT-02 (`PublicApiSurfaceTests`) | Đạt; baseline không đổi |

Các dòng của gói 5.4, 5.5, 5.7 (IT-12, OB-1, DOC-2/3, SCALE-1…) chưa thuộc 5.3.

## 4. Việc của người dùng

1. **NG-4 (tunnel ngrok thật):** tạo `.env` với `NGROK_AUTHTOKEN` và `TADSS_API_KEY_SHA256` (mã băm của khóa riêng), chạy lệnh chế độ A trong README rồi làm theo 5 bước "NG-4" (health, POST/GET có khóa, 401 khi thiếu khóa, thấy request ở `127.0.0.1:4040`). Chưa ai kiểm chứng ngrok thật (image đã kiểm offline: inspector nghe `0.0.0.0:4040` trong container).
2. **NG-3 chế độ B (Neon):** thêm `TADSS_DB_CONNECTION` (dạng Npgsql `key=value`, `SSL Mode=Require`) và `-f docker-compose.neon.yml`. Schema `tadss` phải đã migrate (migration `DecisionPersistenceV2` **chưa áp lên Neon**, việc của bạn theo D-05).
3. **Review diff `openapi-v2.yaml` (+537/−3 so với 05aa840) và `baseline_public_api.txt` (không đổi).** Nếu muốn hai hằng `RateLimited`/`PayloadTooLarge` xuất hiện trong baseline thì phải đổi `PublicApiSurfaceTests` (ruling R6).
4. **Thêm API key dev:** ở `Development` mà `appsettings.Development.json` không có `Tadss:Security:ApiKeys` thì **mọi `/api/*` trả 401** (app vẫn khởi động, có Warning). Thêm `{ "ClientId": "dev", "KeySha256": "<sha256 hex của khóa của bạn>" }` (cách tạo: README mục Bảo mật). Chạy docker compose E-LOCAL thì dùng khóa công khai `tadss-local-dev-key`.
5. Tùy chọn: xoay mật khẩu Neon/Redis nếu từng xuất hiện ở nơi khác; kiểm CI trên GitHub (job `expose-check` chưa từng chạy trên Ubuntu runner thật).
6. Đề xuất PR vào `develop` (bạn tạo và merge).

## 5. Thay đổi tài liệu / skill ở task này, theo file

Trong repo:
- `README.md`: trạng thái (1504 + 186), bảng cấu trúc, khóa E-LOCAL công khai + cảnh báo, tham số hóa cổng/image, mục **Bảo mật** (bảng `Tadss:Security:*`, tạo khóa và mã băm bash/PowerShell, health, rate limit per instance và chỉnh cho k6, header, log, chuỗi cung ứng gồm lỗi lock file của Dependabot, ghim bắc cầu, container) và mục **Expose qua ngrok** (bảng biến, hai chế độ, lưu ý neon riêng, `verify-expose.sh`, các bước NG-4).
- `.env.example`: sửa wording neon (chỉ cổng `tadss-runtime` là loopback khi base+neon), lệnh băm dùng `sha256sum`.
- `docker-compose.neon.yml`: chỉ comment đầu file (cùng lỗi wording); ngữ nghĩa compose không đổi, `ComposeFixtureAlignmentTests` xanh.
- `docs/contracts/INTEGRATION_SPEC.md`: §3.6 ghi chú `REJECT` + `"deposit": null` (runtime chấp nhận, schema từ chối); §4.5 mới "Ghi chú triển khai: kết nối tới TADSS qua ngrok" (`TadssConfig.BaseUrl`, `X-Api-Key`, `ngrok-skip-browser-warning`, `NGROK_DOMAIN`).
- `docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md`: cây thư mục, health + pipeline, Mục 3 (hạn chế: trạng thái theo instance, tranh chấp override↔outcome, log `ClientId`), 4.2 (thành phần mới, hàng "Chưa hiện thực" đã đổi thành "Đã hiện thực"), 4.4.3 (sơ đồ bảo mật 401/413/429), 4.4.4 (sơ đồ override), Mục 5 (test thứ tự middleware).
- `docs/architecture/ARCHITECTURE_DECISION_RECORDS.md`: một dòng "Việc mang sang 5.3" đánh dấu đã hiện thực.
- `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`: sửa SEC-1 (tên tham số, per instance), SEC-1/2/3 (test host in-process), SEC-6 (`USER $APP_UID`), NG-1, NG-5 (lệch hiện thực).
- `docs/planning/BACKLOG_SPRINTS.md`: banner gói 5.3, T5.5, T5.6, T5.7, T5.30 (trạng thái + số test).
- `CLAUDE.md` (TADSS-Runtime): mô tả, lệnh, cấu trúc, bảo mật, compose.
- Commit riêng: `src/Shared.DecisionContractSDK/DTOs/Governance/OverrideRequestDto.cs` (XML doc), `docs/contracts/openapi-v2.yaml` (chỉ dòng description đó đổi, đã đối chiếu diff), `tests/TADSS.UnitTests/Sdk/Contracts/GovernanceSchemaTests.cs` (+1 ca âm `reject-with-null-deposit`).

Ngoài repo (không git-tracked, đã sửa):
- `D:\SINH VIEN\HK6\DA1\SmashCourt\.claude\skills\tadss-architecture\SKILL.md`: endpoint và `X-Api-Key` đã có; hàng Use case/Port; Middleware (thứ tự đầy đủ); **hàng mới** "Middleware bảo mật" và "Options bảo mật"; quy tắc "phản hồi từ chối qua `ProblemResponseWriter`"; "test bảo mật không cần DB nằm trong `UnitTests/Api/Security`"; cây mirror test; sai lầm thường gặp. Bỏ qua hàng "port quản trị" vì không có cổng quản trị. Câu về ngoại lệ `DatabaseOptions` đã đúng sẵn trong skill (đã nói "không có ngoại lệ"), không còn câu cũ.
- `D:\SINH VIEN\HK6\DA1\SmashCourt\CLAUDE.md`: **không đổi** (dòng TADSS đã có `POST /api/v2/decisions (HTTP + X-Api-Key)` và cổng 5296/8081).

Kiểm link nội bộ (script tạm ngoài repo, đã xóa) cho 7 file `.md` đã sửa: 0 link hỏng.

Các mục deferred của brief:
- (a) làm: ghi chú §3.6 + test schema.
- (b) làm: XML doc, SAS, skill, `docs`; còn lại các chuỗi "(gói 5.3)" mang nghĩa nguồn gốc (nhãn đầu comment compose/`.env.example`, tiêu đề mục SEC/NG, trạng thái "sau gói 5.3") là có chủ ý.
- (c) **bỏ qua**: curl với `X-Actor-Id: Nguyễn Văn A` (UTF-8) vào app thật không bị Kestrel từ chối (request đi tiếp tới tầng DB, 503 do DB giả); chưa kiểm chứng việc giải mã Latin1 nên không ghi vào spec.
- (d) làm (comment `docker-compose.neon.yml`, `.env.example`, README).
- (e) đã đúng sẵn (xem trên).
- (f) làm: kiểm chứng trên Docker thật (image build từ HEAD): log trong controller/service mang `ClientId`, dòng `HTTP GET ... responded` của `Serilog.AspNetCore.RequestLoggingMiddleware` không mang `ClientId`; ghi ở README và SAS Mục 3.

## 6. Mục chuyển sang gói 5.4 / 5.5 (không thuộc 5.3)

- 5.4: tiền tố key cache `tadss:v2:` đưa vào options, option chết `InstanceName`/`TtlHours`, log ERR của EF cho mỗi `23505` mong đợi; định tuyến theo request, shadow (`SHADOW` đã bị loại khỏi hàng chờ, override/outcome trả 404 cho `SHADOW`), `IsolatedStrategyInvoker`, breaker Redis, IT-12.
- 5.5: rate limit phân tán và đánh giá scale SCALE-3 (SEC-1 hiện tính theo từng instance; nêu rõ ở kịch bản SCALE-2/SCALE-3), observability.
- Minor còn mở từ các task (không chặn gói): test thiếu cho tải thật của kịch bản chunked 413; `neg_c`/`neg_key` trong `verify-expose.sh` chưa kiểm lý do thất bại; `RedisHealthCheck` log Warning mỗi lần kiểm khi Redis chết; literal `"ready"` lặp giữa `EndpointMappingExtensions` và `HealthCheckTags.Ready`; Dockerignore theory không bắt `!` phủ định về sau.

## 7. Concerns / quan sát

- Quan sát (đã xác minh trên Docker thật, không sửa vì ngoài phạm vi): `UseSerilogRequestLogging` đứng **trong** exception handler nên dòng "responded" ghi mã **500** cho request mà client nhận 503/4xx do exception; 401/413/429 do middleware tạo thì ghi đúng. Có thể xem ở 5.4 khi làm observability.
- Quan sát: trên máy Windows này, app chạy từ `dotnet <dll>` với stdout chuyển hướng không in dòng Serilog Console nào (Docker `docker logs` có đủ). Không liên quan thay đổi của gói; chỉ ảnh hưởng việc đọc log local.
- Image Docker nền (postgres, redis, toxiproxy, ryuk, curl, ngrok) là cache có sẵn từ test/task trước, không xóa.
- CI job `expose-check` chưa chạy trên Ubuntu runner thật; NG-4 chưa kiểm chứng (cần tài khoản ngrok).
