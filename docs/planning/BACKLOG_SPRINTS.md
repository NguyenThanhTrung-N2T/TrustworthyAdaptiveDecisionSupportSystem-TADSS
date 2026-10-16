# BACKLOG TRIỂN KHAI THEO PHASE (IMPLEMENTATION BACKLOG) — v2.1

> **Vai trò của tài liệu:** phân rã mỗi phase trong [`MASTER_EXECUTION_PLAN.md`](MASTER_EXECUTION_PLAN.md) thành task có mã, owner và tiêu chí nghiệm thu. Lịch trình, gate và phân vai **chỉ** được định nghĩa ở MASTER_EXECUTION_PLAN. Mã test case (`UT-`, `IT-`, `CT-`, `FI-`, `LT-`, `E2E-`, `RP-`, `PT-`, `MT-`, `BE-`, `FE-`) tham chiếu danh mục trong [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).
>
> Quy ước: **SV1** = Lead Backend (TADSS, SmashCourt-BE, hạ tầng kiểm thử) · **SV2** = Frontend (smashcourt-fe, Playwright, k6).

---

## PHASE 1–4 — ĐÃ HOÀN THÀNH (W01–W04)

| Mã | Task | Owner | Kết quả |
|:---|:---|:---:|:---|
| T1.1 | Khởi tạo solution .NET 8, CI build + Docker | SV1 | ✅ |
| T1.2 | Schema `tadss`, bảng `decision_executions`, migration EF Core | SV1 | ✅ |
| T1.3 | Đặc tả contract nháp v1 (request/result/recommendation) | SV1 | ✅ |
| T2.1 | `Shared.DecisionContractSDK`: `Reservation` + value objects + invariant | SV1 | ✅ |
| T2.2 | DTO, enum, `IReservationDecisionStrategy`, 7 JSON schema | SV1 | ✅ |
| T2.3 | Kiểm thử kiến trúc (dependency purity, public API baseline 32 type) — đóng băng v1 | SV1 | ✅ |
| T3.1 | 4 endpoint v1, `StrategyResolver`, correlation id, RFC 7807 | SV1 | ✅ |
| T3.2 | `DefaultDecisionStrategy`, `MockAlternativeDecisionStrategy` | SV1 | ✅ |
| T3.3 | Idempotency (`X-Idempotency-Key` + `request_hash`), optimistic concurrency (`version`) | SV1 | ✅ |
| T4.1 | Pipeline Polly v8 + `FallbackDecisionStrategy` | SV1 | ✅ |
| T4.2 | Cache theo request-hash (Redis + `IMemoryCache`) | SV1 | ✅ |
| T4.3 | Audit đồng bộ qua MediatR in-process events (cơ chế đã gỡ ở Phase 5, thay bằng ghi audit đồng bộ trong `DecisionService`); bộ test tiêm lỗi F1–F7 | SV1 | ✅ |
| T4.4 | Micro-benchmark lab (19 kịch bản × 4 mức tải, chạy trên EF InMemory — đã gỡ khỏi solution ở gói 5.1) | SV1 | ✅ (chỉ mô tả overhead pipeline — xem ghi chú Phase 4 trong MASTER_EXECUTION_PLAN) |

---

## PHASE 5 — BƯỚC 0: RÀ SOÁT VÀ ĐIỀU CHỈNH KẾ HOẠCH (W05, 01/10 – 11/10, Gate MR đạt 01/10)

| Mã | Task | Owner | Tiêu chí nghiệm thu |
|:---|:---|:---:|:---|
| TR.1 | Audit mã nguồn + tài liệu, chốt nghiệp vụ cọc | SV1 | ✅ Danh sách phát hiện được ghi trong Mục 0 của MASTER_EXECUTION_PLAN |
| TR.2 | Đồng bộ toàn bộ tài liệu theo kế hoạch v2.0 (mỗi chủ đề một nguồn) | SV1 | ✅ Không còn mâu thuẫn tham số/tên file/con số giữa tài liệu và mã nguồn |
| TR.3 | Gỡ module `Normalization`, 6 dataset tổng hợp và báo cáo Tuần 9 | SV1 | ✅ Build `--warnaserror` + toàn bộ test còn lại pass (473 unit + 27 integration) |
| TR.4 | Review tài liệu luồng nghiệp vụ cọc/duyệt từ góc nhìn FE | SV2 | ⏳ Ghi nhận góp ý, chốt luồng (đến 11/10; góp ý gộp vào T5.24) |
| TR.5 | Tạo tag `phase4-baseline`, commit kết quả re-baseline | Cả hai | ✅ `git status` sạch ở cả 4 repo |

---

## PHASE 5 — HOÀN THIỆN TADSS + HẠ TẦNG KIỂM THỬ (W05–W08, 05/10 – 01/11; bắt đầu thực tế 02/10): CÁC GÓI 5.1–5.7

### Epic 5A — Contract v2 & lõi TADSS (SV1)

> **Gói 5.2d — Clean Architecture và SOLID: ✅ hoàn tất (03/10/2026).** Tách `TADSS.Runtime.Api` thành `TADSS.Application`, `TADSS.Infrastructure`, `TADSS.Runtime.Api` (ADR-013); port audit tách theo consumer, `StrategyEvaluator`/`DecisionPayloadSerializer` tách khỏi `DecisionService`, bỏ hardcode, đổi tên entity EF; thêm bộ test kiến trúc UT-14. Không đổi wire, JSON schema, `openapi-v2.yaml`, baseline SDK và ID migration. Khi xong gói có 1112 unit + 121 integration test (trước gói: 965 + 121). Tiếp theo: gói 5.3.

> **Gói 5.3 — Quản trị và bảo mật: ✅ hoàn tất (04/10/2026).** Override, outcome, hàng chờ duyệt (T5.5, T5.6); `X-Api-Key`, rate limiting, giới hạn kích thước, security headers, health live/ready, container non-root, lock file + quét lỗ hổng (T5.7); compose + ngrok + `tools/expose/verify-expose.sh` (T5.30; sau D-11 cùng ngày: một `docker-compose.yml` không profile chạy `tadss-runtime` + `ngrok` + `grafana`, `appsettings.Development.json` copy vào image local). Thêm hai mã lỗi `RATE_LIMITED` (429), `PAYLOAD_TOO_LARGE` (413), 5 JSON schema và 3 endpoint vào `openapi-v2.yaml`; không đổi `baseline_public_api.txt` và migration. Hiện có 1516 unit + 196 integration test (sau điều chỉnh D-11; trước gói: 1112 + 121). **Chờ người dùng:** NG-4 (tunnel ngrok thật với Neon thật, cần `NGROK_AUTHTOKEN`). Tiếp theo: gói 5.4.

| Mã | Task | Tuần | Test case liên quan |
|:---|:---|:---:|:---|
| T5.1 | Contract v2: OpenAPI v2, JSON schema v2 (`docs/contracts/schemas/v2/`), DTO v2 trong SDK, public API baseline v2 (theo [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md)). *Tiến độ: ✅ gói 5.2a–5.2c — SDK v2 ở namespace gốc (v1 đã gỡ), JSON schema v2 (`decision-request`, `decision-result`, `decision-detail`), `openapi-v2.yaml` sinh từ mã (`2.0.0`, có test chống lệch), baseline public API v2 (44 kiểu); ✅ đã **đóng băng v2.0.0 ngày 11/10/2026** trong gói 5.7 (M-07, gói 5.7, 11/10/2026), trước lần chạy đánh giá đầy đủ (T5.21)* | W05 | CT-01, CT-02, UT-02 |
| T5.2 | Domain v2: nhiều sân/booking, khách vãng lai, lịch sử khách, thời gian `DateTimeOffset` + múi giờ chi nhánh. *Tiến độ: ✅ gói 5.2a–5.2c — domain v2 là domain duy nhất của SDK và đã nối vào runtime* | W05 | UT-01, UT-05 |
| T5.3 | Migration DB v2: `decision_executions` bất biến (trigger), `decision_overrides`, `decision_outcomes`, snapshot request, `branch_id`, `approval_expires_at`, `role`. *Tiến độ: ✅ gói 5.2c — migration `20261002101457_DecisionPersistenceV2` (xóa bảng v1, tạo 3 bảng + 6 trigger; `Down` khôi phục v1) kiểm chứng trên PostgreSQL thật; áp lên Neon 10/10/2026 (người dùng cho phép)* | W05 | IT-05, IT-15 |
| T5.4 | Strategies v2: `DefaultDecisionStrategy` 2.0.0 (cọc theo hạng), `MockAlternativeDecisionStrategy` 2.0.0 (cọc theo khung giờ), tham số qua options. *Tiến độ: ✅ gói 5.2a–5.2c — strategies v2 là bản duy nhất, chạy qua `POST /api/v2/decisions`* | W05 | UT-03, UT-05 |
| T5.5 | Override v2 (APPROVE kèm mức cọc / REJECT, `X-Actor-Id`, chống ghi đè đồng thời bằng `UNIQUE(decision_id)`), outcome endpoint. *Tiến độ: ✅ gói 5.3 — `OverrideService` (thứ tự kiểm tra theo INTEGRATION_SPEC §3.6, replay theo key + nội dung, quy đổi `rate`/`amount` bằng `DepositCalculator`), `OutcomeService`, `POST /api/v2/decisions/{id}/override` và `/outcome`, `X-Actor-Id` lưu `actor_id`; test: IT-06 (5), IT-07 (4), IT-08 (3), IT-10 (6), UT-15 (27)* | W05 | IT-06, IT-07, IT-08, IT-10, UT-15 |
| T5.6 | Hàng chờ duyệt: chỉ bản mới nhất của mỗi reservation, lọc `branchId`, loại bản hết hạn, partial index. *Tiến độ: ✅ gói 5.3 — `GET /api/v2/decisions/pending-approvals` (`pageSize` 1..100), SQL có tham số dùng `idx_de_pending`, `EXPLAIN` kiểm trên ≥ 5.000 bản ghi; IT-09 (5)* | W05 | IT-09 |
| T5.7 | Xác thực service-to-service bằng `X-Api-Key`; sửa map exception (lỗi server không thành 400). *Tiến độ: phần map lỗi ✅ gói 5.2c — `ExceptionClassifier` là nguồn duy nhất của `GlobalExceptionHandler` (503 + `Retry-After` cho `40001`/`40P01`/lỗi tạm thời, `MISSING_HEADER`, `DECISION_NOT_FOUND`, lỗi server không còn thành 400; UT-08, FI-08); ✅ `X-Api-Key` gói 5.3 — `ApiKeyAuthenticationMiddleware` (băm SHA-256 + `FixedTimeEquals`, fail-closed ngoài Development/Testing), cùng gói bảo mật SEC-1…SEC-7 (rate limiting, 413, security headers, health live/ready, container non-root, lock file, Dependabot); test: IT-11 (4), SEC-1 (7), SEC-2 (9), SEC-3 (9), SEC-4 (17), SEC-5 (11), SEC-6 (2), SEC-7 (4)* | W05 | IT-11, UT-08, SEC-1…SEC-7 |
| T5.8 | `RequestHasher` độc lập múi giờ máy chủ; canonical hóa theo contract v2. *Tiến độ: ✅ `RequestHasher` v2 ở gói 5.2b (canonical theo contract v2, `items` sắp xếp tất định, không phụ thuộc múi giờ máy chủ; UT-06), đã nối endpoint ở gói 5.2c* | W05 | UT-06 |
| T5.9 | Định tuyến strategy theo từng request (`Tadss:Routing`) + shadow mode. *Tiến độ: ✅ gói 5.4 — `StrategyRouter` (rule đầu tiên khớp theo `PeakHour`/`BranchIds`/`Channels`/`Tiers`/`IsGuest`, nạp lại nóng, sai thì giữ bảng cũ) và shadow (`Tadss:Shadow`: `ShadowDispatcher` + `ShadowExecutor` + `ShadowExecutionWorker`, bản `SHADOW` không ảnh hưởng response); SCALE-1 chứng minh idempotency/cache/breaker đúng khi chạy hai instance* | W05 | UT-04, IT-12, IT-14, SCALE-1 |
| T5.10 | Cách ly timeout cho strategy không hợp tác (không tôn trọng `CancellationToken`). *Tiến độ: ✅ gói 5.4 — `IsolatedStrategyInvoker`, pipeline Polly riêng cho từng strategy, decorator tiêm lỗi chỉ ở môi trường `Testing`* | W05 | FI-05 |
| T5.11 | Chính sách cache do strategy khai báo (bật/tắt, TTL) + circuit breaker cho Redis. *Tiến độ: ✅ chính sách cache ở gói 5.2b; circuit breaker Redis, `InstanceName` và metric `tadss.cache.requests` ở gói 5.4 (FI-07 — Redis chậm dưới tải — thuộc gói 5.5)* | W05 | IT-13, FI-06, FI-07 |

### Epic 5B — Hạ tầng kiểm thử & observability (SV1)

> **Chế độ đánh giá một lệnh (D-12, 04/10/2026):** T5.14…T5.20 được hiện thực quanh container evaluator của `docker-compose.yml` (gói 5.5, **đã làm**; từ D-14 ngày 10/10/2026 chạy cùng lệnh `docker compose up -d --build`): một container evaluator điều phối test gộp, k6, sự cố dưới tải qua Toxiproxy, Schemathesis, Stryker rồi dựng `evidence/<run_id>/` (`report.html`, dữ liệu thô, `manifest.json`). Các script rời đã dự kiến trước đây được evaluator thay thế; profile `lab` / môi trường E-LOCAL được thay bằng stack đánh giá / E-EVAL (ban đầu sau profile `evaluation`, D-14 bỏ profile). Gói 5.5 dựng chế độ đánh giá; 5.6 thêm dữ liệu (replay, adapter khách sạn); 5.7 chạy bản đầy đủ để đóng băng (Gate M5).

| Mã | Task | Tuần | Đầu ra |
|:---|:---|:---:|:---|
| T5.12 | Chuyển integration test sang **Testcontainers** (PostgreSQL 16 + Redis 7); gỡ `Microsoft.EntityFrameworkCore.InMemory` | W05 | ✅ (gói 5.1) Toàn bộ IT chạy trên container: 475 unit + 32 integration; sau gói 5.2c: 965 unit + 121 integration; sau gói 5.2d: 1112 unit + 121 integration |
| T5.13 | `docker-compose` cho môi trường kiểm thử: TADSS, PostgreSQL, Redis, **Toxiproxy**, **`grafana/otel-lgtm`** | W05 | ✅ (gói 5.1) đã dựng môi trường E-LOCAL tự chứa; *04/10/2026 đã gỡ khỏi compose* (khi đó compose chỉ để deploy/expose). Các service (PostgreSQL, Redis, Toxiproxy, Grafana) được dựng lại trong stack đánh giá của `docker-compose.yml` (D-12; thay cho profile `lab` đã dự kiến; từ D-14 không còn profile, chạy cùng lệnh `docker compose up -d --build`), cùng container evaluator — ✅ đã làm ở gói 5.5 |
| T5.14 | Gắn **OpenTelemetry** (traces, metrics, logs) cho TADSS, gửi về `grafana/otel-lgtm` (Prometheus lưu metric, Tempo lưu trace, Loki lưu log — không thêm container Prometheus riêng); dashboard Grafana (tỷ lệ trạng thái quyết định, p50/p95/p99, fallback, trạng thái circuit, cache, độ dài hàng chờ) nạp tự động ở cả chế độ vận hành (`docker compose up`) và chế độ đánh giá — dashboard là nơi xem sự cố trực tiếp khi evaluator chạy kịch bản dưới tải. *Tiến độ: ✅ gói 5.5 — OpenTelemetry ở `Composition/`, 13 instrument, dashboard 29 panel hiển thị trong 7 nhóm nạp sẵn (OB-1…OB-4)* | W06 | Dashboard JSON lưu trong repo; OB-1, OB-2, OB-4 |
| T5.15 | Kịch bản lỗi Toxiproxy cho FI-01…FI-10 (tự động bật/tắt toxic) — một bước của evaluator (test gộp + sự cố dưới tải, ghi `chaos_log`), không còn script rời. *Tiến độ: ✅ gói 5.5 — FI-07, FI-09 có test; LT-03 bật/tắt FI-02/03/07/08 theo lịch cố định, `chaos_log` + annotation Grafana* | W07 | Ma trận FI chạy trong chế độ đánh giá |
| T5.16 | **Schemathesis** chạy trên OpenAPI v2 — một bước của evaluator. *Tiến độ: ✅ gói 5.5 — bước (d) của evaluator; Schemathesis tìm một lỗi contract thật (decisionId không phải GUID → 404 rỗng) và đã sửa* | W07 | CT-03 |
| T5.17 | **Stryker.NET** cho SDK, Strategies, service lõi; báo cáo coverage bằng ReportGenerator — một bước của evaluator. *Tiến độ: ✅ gói 5.5 — bước (d) bản đầy đủ; MT-01…MT-03 ≥ 70 % (xem `spec/PHASE5_REPORT_5.5.md`)* | W07 | MT-01…MT-03 |
| T5.18 | Harness **replay**: truy vấn chỉ đọc xuất 215 booking SmashCourt (ẩn danh) → request v2 → kết quả; đổ vào cùng Evidence Pack/báo cáo của chế độ đánh giá. *Tiến độ: ✅ gói 5.6 (mã) — `tools/evidence/sql/export_smashcourt_bookings.sql` (kiểm bằng `verify-export.sh`), `replay.py`, `time_shift.py`, `contract_check.py`, bước (e) của evaluator, D4 và phân tích RP-02 trong `report.html`. **Chờ người dùng (U-3):** tự xuất CSV vào `data/private/` rồi chạy `--steps e,f`; chưa có CSV thật nên số liệu RP mới chạy trên fixture* | W08 | RP-01, RP-02 |
| T5.19 | Adapter **Hotel Booking Demand** → contract v2 (chứng minh độc lập domain); đổ vào cùng Evidence Pack/báo cáo. *Tiến độ: ✅ gói 5.6 — `hotel_adapter.py` (bản TidyTuesday ghim SHA-256), instance riêng `eval-tadss-portability` (chỉ cấu hình, không sửa mã), D5, SC7 tính được; bản nhanh 10.000 request 0 lỗi contract, SC7 `PASS` (lượt `EV-20261011-EVAL-quick-r1`). U-2 (giấy phép CC BY 4.0) ✅ người dùng xác nhận 17/10/2026* | W08 | PT-01, PT-02 |
| T5.20 | **Evidence/report builder** (Python, pandas, plotly) nằm trong chế độ đánh giá: sinh `report.html` (đạt/không đạt SC1…SC8, p50/p95/p99, biểu đồ dòng thời gian sự cố) + `manifest.json` SHA-256 trong `evidence/<run_id>/`. *Tiến độ: ✅ gói 5.5 (Jinja2 + SVG tự vẽ thay cho pandas/plotly); gói 5.6 thêm mục "Dữ liệu thực tế", SC7 và lệnh `python -m evidence.report <pack>` thay cho notebook (Q-10)* | W07 | Một lệnh sinh trọn Evidence Pack |
| T5.21 | Chạy **bản đầy đủ** của chế độ đánh giá (toàn bộ danh mục TADSS, ước tính ~2,8 giờ); đóng băng contract v2 *(đã xong 11/10/2026 trong gói 5.7, trước lần chạy đầy đủ; phần còn lại là chạy đánh giá đầy đủ, chờ người dùng: U-3 xuất CSV SmashCourt, U-4 chạy lượt đầy đủ và lưu Evidence Pack)* | W08 | Gate M5 |
| T5.22 | Chốt **API contract BE ↔ FE** cho Phase 6 (endpoint SmashCourt cho cọc, chờ duyệt, duyệt). *Đã chốt hướng (11/10/2026): đặc tả markdown trong repo SmashCourt-BE (`docs/api/DEPOSIT_APPROVAL_API.md`; OpenAPI sinh từ mã ở Phase 6); route theo `bookingId`, hàng chờ đọc từ DB của BE, Manager nhập cọc dạng tỷ lệ (INTEGRATION_SPEC §2.2). Tiến độ: ✅ gói 5.7 — đã viết `SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md` (ba route theo `bookingId`, mã lỗi và sự kiện SignalR đề xuất, truy vết E2E-01…E2E-10, câu hỏi mở cho SV2); chờ SV2 review (T5.29)* | W08 | Đặc tả API BE ↔ FE được SV2 review (T5.29) |
| T5.23 | Chốt môi trường tích hợp E-STAGING (Docker Compose FE/BE/TADSS trên máy nhóm, ngrok, VNPay sandbox). *Đã chốt dữ liệu (11/10/2026): nhánh Neon riêng `staging` cho DB SmashCourt và schema `tadss`, không dùng chung DB chính*. *Tiến độ: ✅ gói 5.7 — đã viết mục [Môi trường tích hợp E-STAGING](MASTER_EXECUTION_PLAN.md#môi-trường-tích-hợp-e-staging) (máy và cách chạy, nhánh Neon `staging` và cách làm mới, VNPay sandbox, ai chạy gì, thu Evidence, thứ tự với xuất CSV RP, rủi ro); việc dựng thật thuộc T7.2* | W08 | Ghi trong MASTER_EXECUTION_PLAN |
| T5.30 | **E-NGROK** — expose TADSS qua ngrok và truy cập được database: một `docker-compose.yml` duy nhất (tadss-runtime + ngrok + grafana, cấu hình như SmashCourt-BE: `appsettings.Development.json` copy vào image local, container chạy Development; một lệnh `docker compose up -d --build` chạy cả ba, không profile; ngrok chỉ cần `NGROK_AUTHTOKEN`, URL đổi mỗi lần khởi động lại; cổng chỉ loopback), `X-Api-Key` fail-closed ngoài Development/Testing, `ForwardedHeaders`, image ngrok/Grafana ghim tag (theo [`PHASE5_DESIGN_SPEC.md`](../../spec/PHASE5_DESIGN_SPEC.md) Mục 4.3.1 và D-11). *Tiến độ: ✅ gói 5.3 — NG-1, NG-2, NG-3, NG-5 tự động (trên PostgreSQL/Redis tạm do `verify-expose.sh` dựng) bằng `tools/expose/verify-expose.sh` (job CI `expose-check`) và `ComposeFixtureAlignmentTests`; **chờ người dùng:** NG-4 (tunnel thật, kèm Neon thật)* | W05 | NG-1…NG-5 |

> **Ghi chú gói 5.1 (T5.12, T5.13):** hai lỗi V-1 và V-8 được phát hiện khi phân tích (spec Mục 2), cả hai đã sửa. **V-8:** mất kết nối PostgreSQL bị ánh xạ thành 400 (nay `GlobalExceptionHandler` trả 503 `PERSISTENCE_UNAVAILABLE`); FI-08 chạy trên PostgreSQL thật qua Toxiproxy xác nhận chuỗi exception thật `InvalidOperationException → NpgsqlException(IsTransient) → EndOfStreamException`, nếu không sửa sẽ bị ánh xạ thành 400 (chưa quan sát trực tiếp trạng thái 400 trên hạ tầng thật). **V-1:** `docker-compose` truyền `Redis__ConnectionString` (không được bind), nay là `Tadss__Cache__RedisConnectionString`; xác nhận bằng compose (thấy khóa Redis `tadss:decision:*`). 27 test cũ chuyển sang hạ tầng thật đều đạt mà không sửa kỳ vọng và không lộ lỗi mới.

### Epic 5C — Chuẩn bị kiểm thử phía FE/E2E (SV2)

| Mã | Task | Tuần | Đầu ra |
|:---|:---|:---:|:---|
| T5.24 | Review contract v2 từ phía consumer. *Tiến độ: contract đã đóng băng v2.0.0 (11/10/2026, gói 5.7); góp ý của consumer từ nay đi theo [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md) Mục 6* | W05 | Góp ý được chốt trước khi đóng băng |
| T5.25 | Dựng **Playwright** trong `smashcourt-fe` (cấu hình, trace, video, chạy trên CI). *Tiến độ 15/10/2026: ⏳ chưa bắt đầu — `smashcourt-fe` chưa có Playwright (`package.json`, CI); cần có trước W12 (Playwright trên mock, Gate M6)* | W05–W06 | Một test mẫu chạy được |
| T5.26 | Dựng dự án **k6**; script LT-01…LT-05; đẩy metric k6 vào Prometheus của `grafana/otel-lgtm` để tải hiện cùng dashboard TADSS. *Tiến độ: ✅ phần SV1 ở gói 5.5 — `tests/load/` (LT-01/02/03/05, SCALE-2), LT-04 để Phase 6* | W05–W07 | Script chạy được; evaluator của chế độ đánh giá (D-12) chạy LT-01/02/03/05, LT-04 để Phase 6 |
| T5.27 | Đặc tả từng bước kịch bản E2E-01…E2E-13. *Tiến độ: bản đặc tả đầu do SV1 viết theo yêu cầu người dùng 12/10/2026 (gói 5.7): [`E2E_TEST_SPECIFICATION.md`](../research/E2E_TEST_SPECIFICATION.md), phủ E2E-01…E2E-13 (thêm E2E-11 duyệt khi TADSS dừng, E2E-12 bấm duyệt lần hai, E2E-13 đặt tại quầy); danh mục BE/FE/E2E trong methodology Mục 4.7–4.8 cập nhật cùng đợt; SV2 review sau Phase 5* | W06–W08 | Danh mục E2E hoàn chỉnh |
| T5.29 | Review API contract BE ↔ FE | W08 | Chốt cùng T5.22 |

> **Gói 5.7 — trạng thái (12/10/2026): phần mã và tài liệu ✅ xong, chờ người dùng chạy lượt đầy đủ.** Đã xong: đóng băng contract v2.0.0 (11/10/2026, trước lượt chạy đầy đủ); kiểm tra tài liệu tự động DOC-1…DOC-4 (DOC-1…3 bằng `tools/docs/check_docs.py` chạy ở CI job `docs` và bước (a) của evaluator, DOC-4 là unit test `ConfigurationDocumentationTests`; bảng kiểm kê ở mục "Tài liệu" của `README.md`, bảng cấu hình `docs/guides/CONFIGURATION_REFERENCE.md`); T5.22 (đặc tả BE ↔ FE) và T5.23 (E-STAGING); sửa ghi chú k6 theo pha và quy tắc KPI. Hiện có 1874 unit test, Python `tools/evaluation` 240, `tools/evidence` 230, `tools/docs` 34. Chưa làm: T5.21 (lượt đánh giá đầy đủ), lưu trữ Evidence Pack (M-10), báo cáo Gate M5, T5.29 (chờ SV2 review).

> **Gói phụ 5.7b — Kinh tế mô phỏng chính sách cọc (PT-03) và shadow trong đánh giá (PT-04): phần mã và tài liệu ✅ xong (15/10/2026), làm trước lượt đầy đủ để Evidence Pack chính thức có luôn PT-03/PT-04.** Bước (e) bật shadow `MockAlternative` trên instance `eval-tadss-portability`, xả hàng đợi và đối soát counter (`RECONCILED`/`UNKNOWN`/`FAILED`), ghép `PRIMARY`/`SHADOW` theo `correlation_id`; bước (f) mô phỏng kinh tế các chính sách cọc trên Hotel Booking Demand theo ảnh chụp chính sách hủy thật của SmashCourt (`tools/evidence/config/smashcourt_cancel_policies.json`), bootstrap ghép cặp, mục PT-03/PT-04 trong `report.html`. Chỉ là minh họa phương pháp đo trên dữ liệu proxy, không phải kết quả tài chính. Không đổi `src/`, contract 2.0.0, migration, SmashCourt-BE. Python `tools/evidence` 355, `tools/evaluation` 266 + 3 bỏ qua (269 khi có Docker), `tools/docs` 34. Kế hoạch `spec/PHASE5_PLAN_5.7b_TASKS.md`, báo cáo `spec/PHASE5_REPORT_5.7b.md`. Lượt bản nhanh `EV-20261015-EVAL-quick-r2` (15/10/2026) qua cổng kiểm chứng cơ chế: `PAIRED` 10.000/10.000, nhãn `RECONCILED`, tên metric `tadss_shadow_*` đúng, bước (e) 92 s, SC1…SC8 không đổi kết luận; cùng đợt sửa kiểm mã SC7 báo oan commit merge PR. **Chưa làm:** lượt đầy đủ (T5.21). Giấy phép dataset đã xác nhận (U-2, 17/10/2026) nên số liệu PT tổng hợp được đưa vào pack chính thức kèm trích dẫn.

---

## PHASE 6 — SMASHCOURT-BE ∥ SMASHCOURT-FE (W09–W12)

### Epic 6A — SmashCourt-BE (SV1)

Cột *Test case* liệt kê mọi mã mà cột *Task* của methodology Mục 4.7–4.8 gán cho task đó (hai chiều khớp nhau; mã gán cho hai task xuất hiện ở cả hai dòng).

| Mã | Task | Tuần | Test case |
|:---|:---|:---:|:---|
| T6.1 | DB SmashCourt: giá trị `PENDING_APPROVAL` cho `booking_status`; trường cọc và `decision_id` trên hóa đơn/booking | W09 | BE-UT-06, BE-IT-01 |
| T6.2 | `TadssClient` (HttpClient, timeout, idempotency key, correlation id, API key) + options `TadssConfig` qua `IOptionsMonitor` | W09 | BE-UT-01, BE-IT-19, CT-04 |
| T6.3 | Ánh xạ booking SmashCourt → request v2 (nhiều sân, khách vãng lai, hạng thành viên, lịch sử khách) | W09 | BE-UT-02 |
| T6.4 | Luồng đặt sân online theo cọc: APPROVED → VNPay theo số tiền cọc; NEED_APPROVAL → `PENDING_APPROVAL` + giữ slot; tắt flag/TADSS lỗi → 100% | W10 | BE-UT-07, BE-IT-02…BE-IT-04, BE-IT-16, BE-IT-17, BE-IT-19 |
| T6.5 | Endpoint cho Manager: hàng chờ theo chi nhánh, chi tiết quyết định, duyệt (kèm mức cọc) / từ chối — proxy sang TADSS với `X-Actor-Id` lấy từ JWT | W10 | BE-UT-03…BE-UT-05, BE-UT-07, BE-IT-05, BE-IT-06, BE-IT-11…BE-IT-15 |
| T6.6 | Sau duyệt: tạo link thanh toán cọc, thông báo khách (SignalR/email) | W10–W11 | BE-IT-07, BE-IT-18 |
| T6.7 | Job hết hạn duyệt: hủy booking, giải phóng sân, báo outcome `APPROVAL_EXPIRED` (không tự duyệt) | W11 | BE-IT-08, BE-IT-15 |
| T6.8 | Job báo outcome khi booking kết thúc (COMPLETED / NO_SHOW / CANCELLED…) | W11 | BE-IT-09 |
| T6.9 | Thu phần còn lại khi checkout; hoàn cọc theo chính sách hủy hiện có | W11 | BE-IT-10 |
| T6.10 | Bộ test BE theo danh mục (unit + integration Testcontainers); script k6 E2E cùng SV2 | W12 | BE-IT-20, Gate M6 |

### Epic 6B — smashcourt-fe (SV2)

| Mã | Task | Tuần | Test case |
|:---|:---|:---:|:---|
| T6.11 | Thiết kế UI/UX: hiển thị cọc, trạng thái chờ duyệt, dashboard duyệt, chi tiết quyết định | W09 | — |
| T6.12 | Màn hình khách: tóm tắt cọc trước thanh toán, thanh toán cọc, trang chờ duyệt, thông báo kết quả | W10 | FE-04, FE-08, FE-09; E2E-01…E2E-06, E2E-11 |
| T6.13 | Dashboard Manager: hàng chờ theo chi nhánh, duyệt kèm mức cọc + lý do, từ chối, xử lý xung đột, realtime | W11 | FE-05…FE-10; E2E-02, E2E-03, E2E-07, E2E-08, E2E-10…E2E-12 |
| T6.14 | Thu phần còn lại tại quầy (màn hình staff) | W11 | FE-09; E2E-09, E2E-13 |
| T6.15 | Playwright trên mock API; build + lint | W12 | FE-01…FE-03, Gate M6 |

---

## PHASE 7 — INTEGRATE (W13)

| Mã | Task | Owner |
|:---|:---|:---:|
| T7.1 | Ghép FE ↔ BE ↔ TADSS trên docker-compose; chạy smoke E2E | Cả hai |
| T7.2 | Dựng môi trường tích hợp E-STAGING (Docker Compose trên máy nhóm, dữ liệu trên nhánh Neon `staging` theo T5.23); cấu hình VNPay sandbox, tài khoản thử, dữ liệu chi nhánh/sân; chuẩn bị dữ liệu import (215 booking ẩn danh, dataset khách sạn) | SV1 |
| T7.3 | Chuyển Playwright từ mock sang môi trường tích hợp | SV2 |
| T7.4 | Feature freeze cuối W13; hoàn thành Chương 1–2 báo cáo | Cả hai |

---

## PHASE 8 — KIỂM THỬ & ĐÁNH GIÁ (W14–W15)

| Mã | Task | Owner |
|:---|:---|:---:|
| T8.1 | Chạy UT/IT/CT/MT trên cả 3 repo (phần TADSS bằng bản đầy đủ của chế độ đánh giá một lệnh `EVAL_MODE=full docker compose up -d --build`, D-12, D-14) | SV1 |
| T8.2 | Ma trận lỗi FI-01…FI-14 (Toxiproxy) + truy vấn bất biến booking sau mỗi kịch bản | SV1 |
| T8.3 | Tải LT-01…LT-05 (k6) | SV2 |
| T8.4 | Playwright E2E-01…E2E-13 trên môi trường tích hợp (video + trace) | SV2 |
| T8.5 | Đánh giá bằng dữ liệu import: replay RP-01, RP-02 (215 booking); portability PT-01, PT-02 (Hotel Booking Demand) | SV1 |
| T8.7 | Sửa lỗi; chạy lại test case liên quan sau mỗi lần sửa | Cả hai |
| T8.8 | Sinh Evidence Pack + báo cáo notebook | SV1 |

---

## PHASE 9 — ĐÓNG GÓI & BẢO VỆ (W16)

| Mã | Task | Owner |
|:---|:---|:---:|
| T9.1 | Hoàn thiện Chương 3–4 từ Evidence Pack; sơ đồ class/sequence/DB | Cả hai |
| T9.2 | Slide 20–25 trang; video demo; diễn tập bảo vệ | Cả hai |
| T9.3 | Đóng gói repo: README hướng dẫn chạy docker-compose và tái tạo minh chứng | SV1 |

---

## KHÓA LUẬN (NGOÀI PHẠM VI ĐỒ ÁN 2)

Backlog Khóa luận (chiến lược TADS, mô hình học máy, mô phỏng) được mô tả tại [`RESEARCH_THESIS_DEFENSE.md`](../research/RESEARCH_THESIS_DEFENSE.md) và không thuộc kế hoạch 16 tuần này.
