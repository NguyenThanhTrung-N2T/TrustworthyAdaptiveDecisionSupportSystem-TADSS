# BACKLOG TRIỂN KHAI THEO PHASE (IMPLEMENTATION BACKLOG) — v2.0

> **Vai trò của tài liệu:** phân rã mỗi phase trong [`MASTER_EXECUTION_PLAN.md`](MASTER_EXECUTION_PLAN.md) thành task có mã, owner và tiêu chí nghiệm thu. Lịch trình, gate và phân vai **chỉ** được định nghĩa ở MASTER_EXECUTION_PLAN. Mã test case (`UT-`, `IT-`, `CT-`, `FI-`, `LT-`, `E2E-`, `RP-`, `PT-`, `MT-`, `UAT-`, `BE-`, `FE-`) tham chiếu danh mục trong [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).
>
> Quy ước: **SV1** = Lead Backend (TADSS, SmashCourt-BE, hạ tầng kiểm thử) · **SV2** = Frontend (smashcourt-fe, Playwright, k6).

---

## PHASE 1–4 — ĐÃ HOÀN THÀNH (W01–W05)

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
| T4.3 | Audit đồng bộ qua MediatR; bộ test tiêm lỗi F1–F7 | SV1 | ✅ |
| T4.4 | Micro-benchmark lab (19 kịch bản × 4 mức tải, EF InMemory) | SV1 | ✅ (chỉ mô tả overhead pipeline — xem ghi chú Phase 4 trong MASTER_EXECUTION_PLAN) |

---

## PHASE R — RE-BASELINE (W05–W06)

| Mã | Task | Owner | Tiêu chí nghiệm thu |
|:---|:---|:---:|:---|
| TR.1 | Audit mã nguồn + tài liệu, chốt nghiệp vụ cọc | SV1 | ✅ Danh sách phát hiện được ghi trong Mục 0 của MASTER_EXECUTION_PLAN |
| TR.2 | Đồng bộ toàn bộ tài liệu theo kế hoạch v2.0 (mỗi chủ đề một nguồn) | SV1 | ✅ Không còn mâu thuẫn tham số/tên file/con số giữa tài liệu và mã nguồn |
| TR.3 | Gỡ module `Normalization`, 6 dataset tổng hợp và báo cáo Tuần 9 | SV1 | ✅ Build `--warnaserror` + toàn bộ test còn lại pass (473 unit + 27 integration) |
| TR.4 | Review tài liệu luồng nghiệp vụ cọc/duyệt từ góc nhìn FE | SV2 | ⏳ Ghi nhận góp ý, chốt luồng (đến 11/10; góp ý gộp vào T5.24) |
| TR.5 | Tạo tag `phase4-baseline`, commit kết quả re-baseline | Cả hai | ✅ `git status` sạch ở cả 4 repo |

---

## PHASE 5 — HOÀN THIỆN TADSS + HẠ TẦNG KIỂM THỬ (W07–W10)

### Epic 5A — Contract v2 & lõi TADSS (SV1)

| Mã | Task | Tuần | Test case liên quan |
|:---|:---|:---:|:---|
| T5.1 | Contract v2: OpenAPI v2, JSON schema v2 (`docs/contracts/schemas/v2/`), DTO v2 trong SDK, public API baseline v2 (theo [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md)) | W07 | CT-01, CT-02, UT-02 |
| T5.2 | Domain v2: nhiều sân/booking, khách vãng lai, lịch sử khách, thời gian `DateTimeOffset` + múi giờ chi nhánh | W07–W08 | UT-01, UT-05 |
| T5.3 | Migration DB v2: `decision_executions` bất biến (trigger), `decision_overrides`, `decision_outcomes`, snapshot request, `branch_id`, `approval_expires_at`, `role` | W07–W08 | IT-05, IT-15 |
| T5.4 | Strategies v2: `DefaultDecisionStrategy` 2.0.0 (cọc theo hạng), `MockAlternativeDecisionStrategy` 2.0.0 (cọc theo khung giờ), tham số qua options | W08 | UT-03, UT-05 |
| T5.5 | Override v2 (APPROVE kèm mức cọc / REJECT, `X-Actor-Id`, chống ghi đè đồng thời bằng `UNIQUE(decision_id)`), outcome endpoint | W08 | IT-06, IT-07, IT-08, IT-10 |
| T5.6 | Hàng chờ duyệt: chỉ bản mới nhất của mỗi reservation, lọc `branchId`, loại bản hết hạn, partial index | W08 | IT-09 |
| T5.7 | Xác thực service-to-service bằng `X-Api-Key`; sửa map exception (lỗi server không thành 400) | W08 | IT-11, UT-08 |
| T5.8 | `RequestHasher` độc lập múi giờ máy chủ; canonical hóa theo contract v2 | W08 | UT-06 |
| T5.9 | Định tuyến strategy theo từng request (`Tadss:Routing`) + shadow mode | W09 | UT-04, IT-12, IT-14 |
| T5.10 | Cách ly timeout cho strategy không hợp tác (không tôn trọng `CancellationToken`) | W09 | FI-05 |
| T5.11 | Chính sách cache do strategy khai báo (bật/tắt, TTL) + circuit breaker cho Redis | W09 | IT-13, FI-06, FI-07 |

### Epic 5B — Hạ tầng kiểm thử & observability (SV1)

| Mã | Task | Tuần | Đầu ra |
|:---|:---|:---:|:---|
| T5.12 | Chuyển integration test sang **Testcontainers** (PostgreSQL 16 + Redis 7); gỡ `Microsoft.EntityFrameworkCore.InMemory` | W07 | ✅ (gói 5.1) Toàn bộ IT chạy trên container: 475 unit + 32 integration |
| T5.13 | `docker-compose` cho môi trường kiểm thử: TADSS, PostgreSQL, Redis, **Toxiproxy**, **`grafana/otel-lgtm`** | W07 | ✅ (gói 5.1) `docker compose up -d --build` một lệnh; môi trường E-LOCAL tự chứa |
| T5.14 | Gắn **OpenTelemetry** (traces, metrics, logs) cho TADSS; dashboard Grafana (tỷ lệ trạng thái quyết định, p50/p95/p99, fallback, trạng thái circuit, độ dài hàng chờ) | W09 | Dashboard JSON lưu trong repo |
| T5.15 | Kịch bản lỗi Toxiproxy cho FI-01…FI-10 (tự động bật/tắt toxic) | W09 | Script chạy ma trận FI |
| T5.16 | **Schemathesis** chạy trên OpenAPI v2 | W09 | CT-03 |
| T5.17 | **Stryker.NET** cho SDK, Strategies, service lõi; báo cáo coverage bằng ReportGenerator | W09 | MT-01…MT-03 |
| T5.18 | Harness **replay**: truy vấn chỉ đọc xuất 215 booking SmashCourt (ẩn danh) → request v2 → kết quả | W10 | RP-01, RP-02 |
| T5.19 | Adapter **Hotel Booking Demand** → contract v2 (chứng minh độc lập domain) | W10 | PT-01, PT-02 |
| T5.20 | **Notebook** (Python, pandas, plotly) sinh báo cáo minh chứng HTML từ Evidence Pack; mẫu Evidence Pack + manifest SHA-256 | W10 | Notebook chạy lại được |
| T5.21 | Chạy toàn bộ danh mục TADSS; đóng băng contract v2 | W10 | Gate M5 |
| T5.22 | Chốt **API contract BE ↔ FE** cho Phase 6 (endpoint SmashCourt cho cọc, chờ duyệt, duyệt) | W10 | OpenAPI SmashCourt cập nhật |
| T5.23 | Chốt môi trường staging cho pilot (máy chủ, tên miền, VNPay sandbox) | W10 | Ghi trong MASTER_EXECUTION_PLAN |
| T5.30 | **E-NGROK** — expose TADSS qua ngrok và truy cập được database: `docker-compose.ngrok.yml` (+ `docker-compose.neon.yml`), `X-Api-Key` fail-closed ngoài Development/Testing, `ForwardedHeaders`, tham số hóa cổng/image của compose (theo [`PHASE5_DESIGN_SPEC.md`](../../spec/PHASE5_DESIGN_SPEC.md) Mục 4.3.1) | W08 | NG-1…NG-5 |

> **Ghi chú gói 5.1 (T5.12, T5.13):** hai lỗi V-1 và V-8 được phát hiện khi phân tích (spec Mục 2), cả hai đã sửa. **V-8:** mất kết nối PostgreSQL bị ánh xạ thành 400 (nay `GlobalExceptionHandler` trả 503 `PERSISTENCE_UNAVAILABLE`); FI-08 chạy trên PostgreSQL thật qua Toxiproxy xác nhận chuỗi exception thật `InvalidOperationException → NpgsqlException(IsTransient) → EndOfStreamException`, nếu không sửa sẽ bị ánh xạ thành 400 (chưa quan sát trực tiếp trạng thái 400 trên hạ tầng thật). **V-1:** `docker-compose` truyền `Redis__ConnectionString` (không được bind), nay là `Tadss__Cache__RedisConnectionString`; xác nhận bằng compose (thấy khóa Redis `tadss:decision:*`). 27 test cũ chuyển sang hạ tầng thật đều đạt mà không sửa kỳ vọng và không lộ lỗi mới.

### Epic 5C — Chuẩn bị kiểm thử phía FE/E2E (SV2)

| Mã | Task | Tuần | Đầu ra |
|:---|:---|:---:|:---|
| T5.24 | Review contract v2 từ phía consumer | W07 | Góp ý được chốt trước khi đóng băng |
| T5.25 | Dựng **Playwright** trong `smashcourt-fe` (cấu hình, trace, video, chạy trên CI) | W07 | Một test mẫu chạy được |
| T5.26 | Dựng dự án **k6**; script LT-01…LT-05 | W07–W08 | Script chạy được với docker-compose |
| T5.27 | Đặc tả từng bước kịch bản E2E-01…E2E-10 | W08–W09 | Danh mục E2E hoàn chỉnh |
| T5.28 | Kịch bản pilot UAT + phiếu khảo sát **SUS** | W09 | UAT-01…UAT-03 |
| T5.29 | Review API contract BE ↔ FE | W10 | Chốt cùng T5.22 |

---

## PHASE 6 — SMASHCOURT-BE ∥ SMASHCOURT-FE (W11–W14)

### Epic 6A — SmashCourt-BE (SV1)

| Mã | Task | Tuần | Test case |
|:---|:---|:---:|:---|
| T6.1 | DB SmashCourt: giá trị `PENDING_APPROVAL` cho `booking_status`; trường cọc và `decision_id` trên hóa đơn/booking | W11 | BE-IT-01 |
| T6.2 | `TadssClient` (HttpClient, timeout, idempotency key, correlation id, API key) + options `TadssConfig` qua `IOptionsMonitor` | W11 | BE-UT-01, CT-04 |
| T6.3 | Ánh xạ booking SmashCourt → request v2 (nhiều sân, khách vãng lai, hạng thành viên, lịch sử khách) | W11 | BE-UT-02 |
| T6.4 | Luồng đặt sân online theo cọc: APPROVED → VNPay theo số tiền cọc; NEED_APPROVAL → `PENDING_APPROVAL` + giữ slot; tắt flag/TADSS lỗi → 100% | W12 | BE-IT-02…BE-IT-04 |
| T6.5 | Endpoint cho Manager: hàng chờ theo chi nhánh, chi tiết quyết định, duyệt (kèm mức cọc) / từ chối — proxy sang TADSS với `X-Actor-Id` lấy từ JWT | W12 | BE-IT-05, BE-IT-06 |
| T6.6 | Sau duyệt: tạo link thanh toán cọc, thông báo khách (SignalR/email) | W12–W13 | BE-IT-07 |
| T6.7 | Job hết hạn duyệt: hủy booking, giải phóng sân, báo outcome `APPROVAL_EXPIRED` (không tự duyệt) | W13 | BE-IT-08 |
| T6.8 | Job báo outcome khi booking kết thúc (COMPLETED / NO_SHOW / CANCELLED…) | W13 | BE-IT-09 |
| T6.9 | Thu phần còn lại khi checkout; hoàn cọc theo chính sách hủy hiện có | W13 | BE-IT-10 |
| T6.10 | Bộ test BE theo danh mục (unit + integration Testcontainers); script k6 E2E cùng SV2 | W14 | Gate M6 |

### Epic 6B — smashcourt-fe (SV2)

| Mã | Task | Tuần | Test case |
|:---|:---|:---:|:---|
| T6.11 | Thiết kế UI/UX: hiển thị cọc, trạng thái chờ duyệt, dashboard duyệt, chi tiết quyết định | W11 | — |
| T6.12 | Màn hình khách: tóm tắt cọc trước thanh toán, thanh toán cọc, trang chờ duyệt, thông báo kết quả | W12 | E2E-01…E2E-06 |
| T6.13 | Dashboard Manager: hàng chờ theo chi nhánh, duyệt kèm mức cọc + lý do, từ chối, xử lý xung đột, realtime | W13 | E2E-02, E2E-03, E2E-07, E2E-08, E2E-10 |
| T6.14 | Thu phần còn lại tại quầy (màn hình staff) | W13 | E2E-09 |
| T6.15 | Playwright trên mock API; build + lint | W14 | Gate M6 |

---

## PHASE 7 — INTEGRATE (W15)

| Mã | Task | Owner |
|:---|:---|:---:|
| T7.1 | Ghép FE ↔ BE ↔ TADSS trên docker-compose; chạy smoke E2E | Cả hai |
| T7.2 | Triển khai staging; cấu hình VNPay sandbox, tài khoản pilot, dữ liệu chi nhánh/sân | SV1 |
| T7.3 | Chuyển Playwright từ mock sang staging | SV2 |
| T7.4 | Feature freeze cuối W15; hoàn thành Chương 1–2 báo cáo | Cả hai |

---

## PHASE 8 — TEST & FIX (W16–W17)

| Mã | Task | Owner |
|:---|:---|:---:|
| T8.1 | Chạy UT/IT/CT/MT trên cả 3 repo | SV1 |
| T8.2 | Ma trận lỗi FI-01…FI-14 (Toxiproxy) + truy vấn bất biến booking sau mỗi kịch bản | SV1 |
| T8.3 | Tải LT-01…LT-05 (k6) | SV2 |
| T8.4 | Playwright E2E-01…E2E-10 trên staging (video + trace) | SV2 |
| T8.5 | Replay RP-01, RP-02; portability PT-01, PT-02 | SV1 |
| T8.6 | Pilot UAT-01…UAT-03 (2 tuần) | Cả hai |
| T8.7 | Sửa lỗi; chạy lại test case liên quan sau mỗi lần sửa | Cả hai |
| T8.8 | Sinh Evidence Pack + báo cáo notebook | SV1 |

---

## PHASE 9 — ĐÓNG GÓI & BẢO VỆ (W18)

| Mã | Task | Owner |
|:---|:---|:---:|
| T9.1 | Hoàn thiện Chương 3–4 từ Evidence Pack; sơ đồ class/sequence/DB | Cả hai |
| T9.2 | Slide 20–25 trang; video demo; diễn tập bảo vệ | Cả hai |
| T9.3 | Đóng gói repo: README hướng dẫn chạy docker-compose và tái tạo minh chứng | SV1 |

---

## KHÓA LUẬN (NGOÀI PHẠM VI ĐỒ ÁN 2)

Backlog Khóa luận (chiến lược TADS, mô hình học máy, mô phỏng) được mô tả tại [`RESEARCH_THESIS_DEFENSE.md`](../research/RESEARCH_THESIS_DEFENSE.md) và không thuộc kế hoạch 18 tuần này.
