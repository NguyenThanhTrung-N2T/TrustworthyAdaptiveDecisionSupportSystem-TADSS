# TỔNG HỢP DỰ ÁN — SMASHCOURT + TADSS (PHASE 5–6)

| | |
| :--- | :--- |
| **Vai trò** | **File tổng duy nhất** để review, trình bày với hội đồng và bàn giao: Phase 6 (tích hợp cọc TADSS vào SmashCourt, DevOps, hiệu năng) ở Mục 0, Phase 5 (TADSS v2 và hạ tầng kiểm thử) ở Mục 1–9. Chỉ **tóm tắt và dẫn link**; thông số chi tiết nằm ở tài liệu sở hữu (quy tắc "tài liệu sở hữu thắng", [README mục Tài liệu](../README.md#tài-liệu)) |
| **Phạm vi** | Phase 5 (W05–W08) và Phase 6 (W09–W12) theo kế hoạch v2.1, 4 repo của workspace; Phase 7–9 ở [MASTER_EXECUTION_PLAN](planning/MASTER_EXECUTION_PLAN.md) |
| **Số liệu tính đến** | Mục 0: 11/11/2026. Mục 1–9: 18/10/2026 (Gate M5), giữ nguyên làm ảnh chụp của Phase 5 |
| **Nguồn chính** | Phase 6: [`PHASE6_DESIGN_SPEC.md`](../spec/PHASE6_DESIGN_SPEC.md) (P6-01…P6-31), [plan 6C](../spec/PHASE6_PLAN_6C_DEVOPS_OBSERVABILITY_PERF.md) (P6-32…P6-55), [hướng dẫn FE](../spec/PHASE6_FE_HANDOFF.md). Phase 5: [`PHASE5_DESIGN_SPEC.md`](../spec/archive/phase5/PHASE5_DESIGN_SPEC.md) (D-xx, P-xx, R-xx), plan và báo cáo gói ở `spec/archive/phase5/`. Chung: [`MASTER_EXECUTION_PLAN.md`](planning/MASTER_EXECUTION_PLAN.md), [`BACKLOG_SPRINTS.md`](planning/BACKLOG_SPRINTS.md); lệnh chạy: [`RUNBOOK.md`](guides/RUNBOOK.md) |

Khi tài liệu này khác tài liệu sở hữu, tài liệu sở hữu đúng; sửa file này theo. Spec, plan và báo cáo của các gói **đã xong** nằm ở `spec/archive/phase5/` (Phase 5) và `spec/archive/phase6/` (gói 6A, 6C-1); `spec/` chỉ còn tài liệu của việc đang làm.

---

## 0. Phase 6 (W09–W12): tích hợp cọc TADSS vào SmashCourt

**Mục tiêu** ([spec Phase 6](../spec/PHASE6_DESIGN_SPEC.md)): SmashCourt-BE hỏi TADSS mức cọc khi khách đặt sân online; TADSS trả số tiền cọc, yêu cầu Manager duyệt, hoặc từ chối; **mọi sự cố đều cọc 100 %**, không tự duyệt. TADSS **không đổi mã** (contract v2.0.0 đóng băng). Cờ `TadssConfig:EnableTadssDecision`: người dùng chốt **bật** trên bản deploy 12/11 để kiểm BE + TADSS trước Phase 7 (P6-56); bật / tắt khi đang chạy bằng `bash deploy/tadss-flag.sh on|off` (RUNBOOK mục 2.4), không khởi động lại BE.

| Epic | Trạng thái 11/11/2026 | Nội dung | Tài liệu |
| :--- | :--- | :--- | :--- |
| **6A** BE (6A-1…6A-6) | ✅ 31/10 | Trạng thái `PENDING_APPROVAL`, đặt online hai transaction (TADSS ngoài transaction), duyệt / từ chối (override), thanh toán thất bại không hủy đơn, hoàn tiền mặt tại chi nhánh, job hết hạn thanh toán / hết hạn duyệt / báo outcome, bộ kiểm bất biến SC6 sau mỗi test HTTP; sau đó P6-31 (ai hủy), N-38…N-42 (đọc lại sau khóa), SC6-7 | Plan và kết quả từng gói: `spec/archive/phase6/PHASE6_PLAN_6A-*.md`; API BE ↔ FE: `SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md` |
| **6B** FE | ⏳ chưa bắt đầu (người làm FE) | Type, API, sự kiện SignalR mới; màn khách, hàng chờ duyệt, nhân viên; Playwright E2E-01…14 | [`PHASE6_FE_HANDOFF.md`](../spec/PHASE6_FE_HANDOFF.md) (danh sách A1–A32, hợp đồng chính xác Mục 4.5, chia việc logic / UI) |
| **6C** DevOps, quan sát, hiệu năng | Phần lớn ✅; còn diễn tập CD 1.41, runbook 1.42 | Image không bí mật trên GHCR, CI ba repo, CD tự triển khai + quay về (`deploy/cd.sh`, runner trong Docker), một bản deploy trên máy nhóm (PostgreSQL cục bộ, ngrok), Grafana Cloud "Vận hành" + cảnh báo email, dashboard "Kiểm thử" local, môi trường đo `perf`, SP-01…SP-10 | [plan 6C](../spec/PHASE6_PLAN_6C_DEVOPS_OBSERVABILITY_PERF.md), [báo cáo hiệu năng](report/PERFORMANCE_REPORT.md), [runbook](guides/RUNBOOK.md) |

**Kết quả hiệu năng chính** ([báo cáo](report/PERFORMANCE_REPORT.md)): ở tải danh nghĩa (khoảng 51 request/s) hệ thống đạt mọi SLO p95, 0 % lỗi khi máy ở trạng thái bình thường; gãy ở 2,5 lần tải danh nghĩa (nút cổ chai CPU BE); hồi phục sau spike ×5 trong 30 s; TADSS thêm 15–30 ms vào Đặt sân p95, ra quyết định p95 khoảng 15 ms; khi TADSS dừng / chậm / mất kết nối khách vẫn đặt được (cọc 100 %), dữ liệu đúng (SC6). Năng lực tăng khoảng 2,4 lần so với đầu Phase 6 (P6-53 bỏ index trùng, nút cổ chai chuyển từ khóa PostgreSQL sang CPU BE); P6-55 hạ bcrypt 12 ⇒ 11.

**Quyết định quan trọng của Phase 6:** P6-01…P6-31 ở spec Phase 6 (nghiệp vụ cọc, thời gian, hủy, thanh toán); P6-32…P6-55 ở plan 6C (đóng gói, CI/CD, một bản deploy, quan sát, đo hiệu năng, P6-53 index trùng, P6-54 auto-prepare chỉ cho đánh giá, P6-55 bcrypt 11). Đánh đổi đã chốt 11/11: giữ `TimeoutMs` 800 ms + một lần thử lại khi gọi TADSS; câu đọc chi tiết booking 12 bảng để làm hướng cải tiến.

**Việc còn mở:**

| Việc | Chủ | Ghi chú |
| :--- | :--- | :--- |
| Epic 6B (FE) và Playwright E2E | Người làm FE | Gate M6 phụ thuộc phần này |
| Diễn tập CD (1.41), runbook + cập nhật tài liệu (1.42) | SV1 | Plan 6C |
| Bật cờ `EnableTadssDecision` trên bản deploy (P6-56) | SV1 | Sau khi CD triển khai image BE có cờ đổi lúc chạy: `bash deploy/tadss-flag.sh on`. Với dữ liệu perf strategy mặc định từ chối khoảng 40 % lần đặt; FE chưa hiển thị `PENDING_APPROVAL` / `BOOKING_REJECTED` (6B) |
| Đổi mật khẩu Neon `main`; áp script Phase 6 + P6-53 lên `main` sau Gate M6 | Người dùng | |
| Kiểm cảnh báo Grafana Cloud bằng sự cố giả; kiểm URL BE của FE trên Vercel | Người dùng | Rủi ro C1 nếu FE production trỏ vào BE Phase 6 |
| ~~Đo lại SP-01 ×3 sau P6-55~~ ✅ 13/11 | SV1 | Báo cáo hiệu năng Mục 10.2: Đăng nhập nhanh khoảng 2 lần (p95 khoảng 190 ms), CPU BE −20 %, đuôi Đọc / Ghi / Đặt sân giảm; gói `PERF-6C-20261113` |
| Đo SP-09 FI-11, FI-12 với mạch ngắt tắt / bật (P6-57), rồi áp C (P6-58) | SV1 | Sau khi CD triển khai BE có P6-57 |
| Nợ kỹ thuật: `SmashCourt-BE/Services/BookingService.cs` dài 2.417 dòng | — | Người dùng chốt 12/11: **chưa tách** (ưu tiên 6B, Phase 7); ghi vào báo cáo tổng kết mục hướng cải tiến |

---

## 1. Phase 5 trong một trang

**Mục tiêu** (spec Mục 1.2): trước khi tích hợp vào SmashCourt ở Phase 6, (1) TADSS chạy ổn định với đủ chức năng contract v2, tài liệu khớp mã, mã/test/minh chứng đạt chất lượng đo được cao nhất; (2) kiến trúc Clean Architecture có compiler và test khóa, có bảo mật và khả năng chạy nhiều instance. Kế hoạch v2.1 **không có pilot với người dùng thật**: chất lượng quyết định được đánh giá bằng dữ liệu import (replay booking SmashCourt, dataset Hotel Booking Demand).

**Kết quả chính:**

- Contract v1 đã gỡ hoàn toàn (tra lại qua tag `phase4-baseline`); **contract v2 `2.0.0` đã đóng băng ngày 11/10/2026** (OpenAPI, JSON schema v2, baseline public API của SDK).
- Năm endpoint v2: `POST /api/v2/decisions`, `GET /api/v2/decisions/{decisionId}`, `GET /api/v2/decisions/pending-approvals`, `POST /api/v2/decisions/{decisionId}/override`, `POST /api/v2/decisions/{decisionId}/outcome`.
- Solution tách thành Clean Architecture nhiều project (P-03, ADR-013), ranh giới tầng khóa bằng compiler và test UT-14.
- Audit ba bảng chỉ-thêm có trigger chặn sửa/xóa; migration `DecisionPersistenceV2` đã áp lên Neon ngày 10/10/2026.
- Bảo mật để expose qua ngrok (API key băm SHA-256, rate limit, giới hạn body, security headers, container non-root, quét lỗ hổng).
- Runtime thích ứng: định tuyến strategy theo request, shadow mode, pipeline và breaker riêng từng strategy, breaker Redis.
- Observability (OpenTelemetry + Grafana) và **chế độ đánh giá một lệnh** `docker compose up -d --build`: evaluator chạy sáu bước a…f rồi sinh Evidence Pack.
- Gói phụ 5.7b (15/10/2026): bước (e) chạy shadow mode trên instance PT và mô phỏng kinh tế các chính sách cọc theo chính sách hủy thật của SmashCourt (PT-03, PT-04) — chỉ số mô phỏng theo kịch bản, không phải kết quả tài chính; lượt bản nhanh `EV-20261015-EVAL-quick-r2`: 10.000/10.000 cặp `PAIRED`, đối soát `RECONCILED`, 9.909 đơn so sánh (số chính thức lấy từ lượt đầy đủ).
- Toàn bộ integration test chạy trên PostgreSQL 16, Redis 7, Toxiproxy thật (Testcontainers).

**Số liệu mới nhất:**

| Chỉ số                                                   | Giá trị                                                                                                                        | Nguồn                                                          |
| :------------------------------------------------------- | :----------------------------------------------------------------------------------------------------------------------------- | :------------------------------------------------------------- |
| Unit test                                                | 1874 đạt                                                                                                                       | [README](../README.md) (12/10/2026)                            |
| Integration test                                         | 240, đạt ở bước (a) của `EV-20261015-EVAL-quick-r2` (15/10/2026, sau các thay đổi của gói 5.7)                                | README, [`PHASE5_REPORT_5.6.md`](../spec/archive/phase5/PHASE5_REPORT_5.6.md) |
| Pytest                                                   | `tools/evaluation` 257 đạt + 3 bỏ qua (260 khi có Docker/k6); `tools/evidence` 355; `tools/docs` 34 (15/10/2026, sau gói 5.7b) | [Runbook mục 1](guides/RUNBOOK.md)                  |
| Lượt bản nhanh                                           | `EV-20261015-EVAL-quick-r2` (sau 5.7b): mã thoát 0, đạt mọi tiêu chí áp dụng được, ~30 phút. Lượt `quick-r1` cùng ngày trượt SC7 (kiểm mã báo oan, đã sửa) và SC1 (instance lạnh treo, Mục 8)                                               | Báo cáo 5.6 Mục 3                                              |
| Mutation (bước d bản đầy đủ, `EV-20261008-EVAL-full-r4`) | MT-01 84,73 %, MT-02 98,32 %, MT-03 74,43 % (ngưỡng 70 %)                                                                      | [`PHASE5_REPORT_5.5.md`](../spec/archive/phase5/PHASE5_REPORT_5.5.md) 3k      |
| Schemathesis CT-03 bản đầy đủ                            | 6905 ca, 0 lỗi                                                                                                                 | Báo cáo 5.5 3k                                                 |
| Coverage gộp (unit + integration)                        | dòng 98,4 %, nhánh 89,8 %                                                                                                      | Báo cáo 5.5 3k, 5.6 Mục 3                                      |

**Trạng thái Gate M5 — "TADSS Complete & Test-Ready"** (tiêu chí ở [MASTER_EXECUTION_PLAN, Phase 5](planning/MASTER_EXECUTION_PLAN.md)):

| Tiêu chí                                                                                                                      | Trạng thái 18/10/2026                                                                                                                                                                        |
| :---------------------------------------------------------------------------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Toàn bộ test case TADSS (UT, IT, CT, FI, LT mức TADSS, MT) đạt trên hạ tầng thật; không còn EF InMemory (đã gỡ ở gói 5.1–5.2) | ✅ kèm một sai lệch đã giải thích: lượt đầy đủ `EV-20261017-EVAL-full-r1` đạt SC1–SC4, SC7, SC8 (73 mã danh mục đạt, 0 trượt); bước (b) có 0,04 % / 0,11 % response 504 ở LT-02 / LT-05 do máy bận (thí nghiệm A/B, [báo cáo 5.7](../spec/archive/phase5/PHASE5_REPORT_5.7.md) Mục 3); chạy lại khi máy rảnh, không nới tiêu chí |
| Contract v2 đóng băng                                                                                                         | ✅ 11/10/2026 (`2.0.0`, M-07)                                                                                                                                                                |
| Dashboard, Toxiproxy, k6, Schemathesis, Stryker, bộ sinh báo cáo chạy bằng một lệnh và **đã có lượt đầy đủ**                  | ✅ `EV-20261017-EVAL-full-r1` (17/10/2026, ≈ 3 giờ 25 phút); Evidence Pack chính thức chép vào repo ở lượt đầy đủ cuối trên strategy 2.1.0 |
| Danh mục kiểm thử toàn hệ thống (BE, FE, E2E) viết xong; API BE ↔ FE đã chốt                                                  | ✅ Danh mục BE-UT-01…07, BE-IT-01…20, FE-01…10, E2E-01…14 (số tại Gate M5; Phase 6 mở rộng thành BE-UT-01…14, BE-IT-01…27, FE-01…12, Mục 5.1); `DEPOSIT_APPROVAL_API.md` review 18/10 (T5.29): R-1…R-8 đã sửa, Q1…Q12 đã chốt |

Task 10 của plan 5.7 đã làm (18/10/2026): phân tích lượt đầy đủ, báo cáo Gate M5 ở [`spec/archive/phase5/PHASE5_REPORT_5.7.md`](../spec/archive/phase5/PHASE5_REPORT_5.7.md). Theo quyết định của người dùng, Evidence Pack chính thức (M-10) chỉ chép vào `docs/research/evidence-packs/{run_id}/` cho lượt đầy đủ cuối cùng trên strategy 2.1.0. Sau Gate: `DefaultDecisionStrategy` 2.1.0 từ chối khách lặp lại việc giữ chỗ không thanh toán (T5.31); review API BE ↔ FE (T5.29).

---

## 2. Các gói đã làm

Thứ tự phụ thuộc 5.1 → 5.2 → 5.3 → 5.4 → 5.5 → 5.6 → 5.7 (spec Mục 3). Bảng chia gói, test case từng gói và điều kiện xong: spec Mục 3.

**Tiến trình số test unit + integration** (theo [BACKLOG T5.12](planning/BACKLOG_SPRINTS.md) và các báo cáo gói; số lượng chỉ là chỉ số phụ, thước đo chính là độ phủ danh mục): Gate MR (01/10, contract v1) 473 + 27 → sau 5.1: 475 + 32 → 5.2c: 965 + 121 → 5.2d: 1112 + 121 → 5.3: 1504 + 186 (1516 + 196 sau điều chỉnh D-11) → 5.4: 1707 + 211 → 5.5: 1871 + 238 → 5.6: 1871 + 240 → 13/10: 1874 + 240.

### 2.1. Gói 5.1 — Nền móng kiểm thử

- **Làm gì:** chuyển integration test sang Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy); fixture một lượt chạy, mỗi test một database nhân bản từ template; CI tách unit và integration.
- **Vì sao:** minh chứng phải chạy trên hạ tầng thật, tái lập được trên CI (D-04); EF InMemory bị gỡ khỏi integration test.
- **Sửa lỗi (phát hiện khi phân tích, sửa ở 5.1):** V-1 (compose truyền sai tên khóa Redis nên container chưa từng dùng Redis) và V-8 (mất kết nối PostgreSQL bị ánh xạ thành 400, nay là 503 `PERSISTENCE_UNAVAILABLE`). Cả hai tìm ra khi đọc mã (spec Mục 2), không phải do hạ tầng thật làm lộ; FI-08 qua Toxiproxy chỉ xác nhận chuỗi exception thật mà bản sửa V-8 xử lý (ghi chú gói 5.1 trong BACKLOG).
- **Minh chứng:** [plan 5.1](../spec/archive/phase5/PHASE5_PLAN_5.1_TEST_FOUNDATION.md), ghi chú gói 5.1 trong BACKLOG.

### 2.2. Gói 5.2a — SDK v2 và strategies v2

- **Làm gì:** kiểu domain v2 (`Reservation` nhiều item, khách vãng lai, lịch sử, `DateTimeOffset` + múi giờ chi nhánh), DTO wire, JSON schema v2, `DefaultDecisionStrategy` 2.0.0 và `MockAlternativeDecisionStrategy` 2.0.0.
- **Vì sao:** strategy chỉ trả `StrategyDecision`; runtime lo định danh, thời gian, quy đổi tiền, approval window (D-03). SDK chỉ giữ bất biến `endTime > startTime`, giới hạn thời lượng item thành chính sách runtime cấu hình được (P-01, hiện thực ở 5.2b) để PT chạy được mà không sửa SDK; P-02 chốt `channel` = `ONLINE` | `WALK_IN`.
- **Minh chứng:** [plan 5.2a](../spec/archive/phase5/PHASE5_PLAN_5.2a_SDK_STRATEGIES_V2.md); điểm dừng người dùng review bề mặt SDK (D-06).

### 2.3. Gói 5.2b — Lõi runtime v2

- **Làm gì:** `ApprovalWindowPolicy`, `DepositCalculator`, `RequestHasher` v2, cache `StrategyDecision`, port audit, `DecisionService` v2, phân loại lỗi.
- **Vì sao:** cache response v1 sai khi response có `approvalExpiresAt` (V-3); map lỗi v1 sai (V-4): `InvalidOperationException` → 400, lỗi PostgreSQL → 500 thay vì 503, mọi `OperationCanceledException` → 504, message của exception 4xx trả thẳng cho client (ca "mất PostgreSQL → 400" là V-8, sửa ở 5.1).
- **Minh chứng:** [plan 5.2b](../spec/archive/phase5/PHASE5_PLAN_5.2b_RUNTIME_CORE_V2.md); UT-06, UT-10…UT-13.

### 2.4. Gói 5.2c — Chuyển hẳn sang v2

- **Làm gì:** migration `20261002101457_DecisionPersistenceV2` (xóa bảng v1, tạo 3 bảng + 6 trigger), `EfDecisionAuditStore`, mở `POST`/`GET /api/v2/decisions`, gỡ toàn bộ v1 (gỡ cả MediatR, BenchmarkRunner theo D-02, D-09), `openapi-v2.yaml` sinh từ mã, baseline public API nháp v2.
- **Vì sao:** thay thế tại chỗ vì v1 chưa có consumer (D-01); migration nối tiếp để lịch sử trên Neon khớp (D-05); nhận diện tranh chấp idempotency theo `SqlState` + tên constraint (V-5).
- **Minh chứng:** [plan 5.2c](../spec/archive/phase5/PHASE5_PLAN_5.2c_CUTOVER_TO_V2.md); IT-01…05, IT-15, CT-01, CT-02, FI-08.

### 2.5. Gói 5.2d — Clean Architecture và SOLID

- **Làm gì:** tách `TADSS.Runtime.Api` thành `TADSS.Application`, `TADSS.Infrastructure`, `TADSS.Runtime.Api`; port tách theo consumer; bỏ hardcode vào Options có validator; bộ test kiến trúc UT-14.
- **Vì sao:** ranh giới tầng do compiler cưỡng chế, không chỉ dựa vào quy ước (P-03); sửa phụ thuộc ngược (port nằm trong hạ tầng, exception nghiệp vụ nằm ở tầng HTTP).
- **Ràng buộc:** không đổi hành vi, wire, schema, mã lỗi, ID migration, bề mặt SDK.
- **Minh chứng:** [plan 5.2d](../spec/archive/phase5/PHASE5_PLAN_5.2d_CLEAN_ARCHITECTURE.md), ADR-013 trong [ARCHITECTURE_DECISION_RECORDS](architecture/ARCHITECTURE_DECISION_RECORDS.md).

### 2.6. Gói 5.3 — Quản trị và bảo mật

- **Làm gì:** override (APPROVE kèm mức cọc / REJECT, `X-Actor-Id`), outcome, hàng chờ duyệt theo chi nhánh; `X-Api-Key`; gói SEC-1…SEC-7; expose qua ngrok (NG-1…NG-5, `verify-expose.sh`).
- **Vì sao:** TADSS phải đủ an toàn trước khi expose (P-04); tranh chấp hai Manager giải bằng `UNIQUE(decision_id)`.
- **Thay đổi contract:** thêm hai mã lỗi `RATE_LIMITED` (429), `PAYLOAD_TOO_LARGE` (413) khi contract còn là bản nháp (người dùng duyệt ở P-04).
- **Minh chứng:** [`PHASE5_REPORT_5.3.md`](../spec/archive/phase5/PHASE5_REPORT_5.3.md); IT-06…11, SEC-1…7.

### 2.7. Gói 5.4 — Runtime thích ứng

- **Làm gì:** định tuyến strategy theo request (`Tadss:Routing`, nạp lại nóng), shadow mode, pipeline Polly riêng từng strategy (sửa V-2), `IsolatedStrategyInvoker` cho strategy không hợp tác (FI-05), breaker Redis, decorator tiêm lỗi chỉ ở môi trường `Testing`.
- **Vì sao:** một breaker chung làm strategy này lỗi kéo strategy khác fast-fail; cần chứng minh idempotency và cache đúng khi nhiều instance (SCALE-1).
- **Minh chứng:** [`PHASE5_REPORT_5.4.md`](../spec/archive/phase5/PHASE5_REPORT_5.4.md); IT-12 (shadow lỗi không ảnh hưởng `PRIMARY`), SCALE-1 (50 request song song hai host → đúng 1 bản ghi).

### 2.8. Gói 5.5 — Observability và chế độ đánh giá

- **Làm gì:** OpenTelemetry (trace, metric, log) tới `grafana/otel-lgtm`; meter `TADSS.Runtime` 13 instrument; dashboard 29 panel hiển thị (21 timeseries, 6 stat, 1 logs, 1 table) trong 7 nhóm; ma trận FI-01…FI-10 đủ; stack đánh giá cô lập + evaluator; k6 LT-01/02/03/05, SCALE-2; Schemathesis; Stryker; bộ sinh `report.html`.
- **Vì sao:** hội đồng cần một lệnh tái lập toàn bộ minh chứng trên hạ tầng không chạm Neon (D-12); dashboard bắt buộc (P-04).
- **Phát hiện thật:** SCALE-1 bắt một lỗi đua khi dựng pipeline đồng thời (sửa bằng `Lazy`); Schemathesis bắt 404 thân rỗng khi `decisionId` không phải GUID (sửa thành problem+json); LT-03 lộ ca "cam kết không chắc chắn" khi cắt PostgreSQL (mục 7.1).
- **Minh chứng:** [`PHASE5_REPORT_5.5.md`](../spec/archive/phase5/PHASE5_REPORT_5.5.md).

### 2.9. Gói 5.6 — Minh chứng dữ liệu

- **Làm gì:** replay booking SmashCourt (RP-01/02, truy vấn chỉ đọc, ẩn danh HMAC), adapter Hotel Booking Demand (PT-01/02, instance riêng `eval-tadss-portability`, chỉ đổi cấu hình), bước (e) "dữ liệu thực tế" (Q-01), hướng dẫn chạy từng bước.
- **Vì sao:** không pilot nên chất lượng quyết định đánh giá bằng dữ liệu có hình dạng thực tế; PT chứng minh độc lập domain (SC7).
- **Kết quả:** PT bản nhanh 10.000/10.000 request, 0 lỗi contract, SC7 `PASS`; RP chờ CSV của người dùng (U-3). D-14 (10/10) gộp vận hành và đánh giá vào một lệnh `up`.
- **Minh chứng:** [`PHASE5_REPORT_5.6.md`](../spec/archive/phase5/PHASE5_REPORT_5.6.md), [`RUNBOOK.md`](guides/RUNBOOK.md).

### 2.10. Gói 5.7 — Đóng băng M5

- **Làm gì (đã xong):** M-01 (bỏ log ERR giả cho 404), M-09 (k6 báo lượt bỏ theo pha, LT-02 400 VU), DOC-1…DOC-4 tự động (`tools/docs/check_docs.py`, bảng [`CONFIGURATION_REFERENCE.md`](guides/CONFIGURATION_REFERENCE.md)), đóng băng `2.0.0` (M-07), đặc tả API BE ↔ FE (T5.22, M-08), E-STAGING (T5.23), danh mục BE/FE/E2E và [đặc tả E2E](research/E2E_TEST_SPECIFICATION.md) (T5.27).
- **Quyết định:** R-01…R-07 (spec Mục 4.7): đóng băng trước lượt đầy đủ (R-03), lượt chính thức có RP (R-04), API duyệt định danh bằng `bookingId` (R-05), Manager nhập cọc dạng % (R-06), E-STAGING trên nhánh Neon `staging` (R-07; thay 19/10: staging là nhánh `tadss-dev`).
- **Đã xong 18/10:** lượt đầy đủ `full-r1`, báo cáo Gate M5. **Còn chờ:** lượt đầy đủ trên strategy 2.1.0 khi máy rảnh, rồi M-10 (lưu Evidence Pack).
- **Minh chứng:** [plan 5.7](../spec/archive/phase5/PHASE5_PLAN_5.7_M5_FREEZE.md).

### 2.11. Gói phụ 5.7b — Kinh tế mô phỏng chính sách cọc và shadow trong đánh giá

- **Làm gì (đã xong phần mã và tài liệu, 15/10/2026):**
  - **PT-04:** `eval-tadss-portability` chạy shadow `MockAlternative` cạnh `Default` (chỉ đổi cấu hình compose, không sửa `src/`); evaluator chụp counter Prometheus trước khi gửi, xả hàng đợi shadow sau khi gửi, đối soát tổng hợp (DB + hiệu counter) ra nhãn `RECONCILED`/`UNKNOWN`/`FAILED`, rồi ghép cặp `PRIMARY`/`SHADOW` theo `correlation_id` (bốn trạng thái `PAIRED`, `PRIMARY_FALLBACK`, `SHADOW_FALLBACK`, `UNPAIRED`; chỉ `PAIRED` được dùng để so hai strategy).
  - **PT-03:** mô phỏng trên Hotel Booking Demand theo chính sách hủy thật của SmashCourt (ảnh chụp `tools/evidence/config/smashcourt_cancel_policies.json`: 24 giờ hoàn 100 %, 2 giờ hoàn 50 %, 0 giờ hoàn 0 %). Sự kiện giữ cọc lấy từ `reservation_status` (không phải `is_canceled`); hai biến thể hoàn tiền báo ngang hàng vì gốc tính hoàn tiền khi chỉ trả cọc chưa chốt (Q-6); ngày hủy chỉ có ngày nên báo hai cận; `NEED_APPROVAL` báo cả quy ước 100 % lẫn 0 %; rủi ro hoàn vượt số cọc nếu giữ nguyên mã BE (B-1) đo riêng, không vào xếp hạng; bootstrap ghép cặp (hiệu tính trong từng mẫu); câu xếp hạng không nêu chính sách thắng khi kết luận phụ thuộc giả định hoặc khoảng tin cậy chứa 0.
  - Báo cáo `report.html` có mục PT-03/PT-04 và biểu đồ đánh đổi; D5 thêm 7 cột; `raw/d5/policy_economics.json` ghi mọi con số kèm giả định.
- **Vì sao:** Đồ án 2 chứng minh runtime bền, kiểm toán được, tích hợp được, nhưng chưa có số liệu cho câu hỏi chính sách cọc nào cân bằng tốt hơn; shadow mode có từ gói 5.4 nhưng chưa được trình diễn trong lượt đánh giá. Đây là **minh họa phương pháp đo trên dữ liệu proxy**, không phải tuyên bố strategy hiện tại là mô hình dự báo rủi ro tốt (việc của khóa luận).
- **Kết quả:** lượt bản nhanh `EV-20261015-EVAL-quick-r2` qua cổng quyết định (tỷ lệ `PAIRED` ≥ 99 % là ngưỡng vận hành, không phải ngưỡng thống kê): `PAIRED` 10.000/10.000, 0 `UNPAIRED`/`SHADOW_FALLBACK`, nhãn `RECONCILED` (counter `tadss_shadow_executions_total{instance="tadss-pt"}` khớp số bản `SHADOW`, phần dư 0), SC1…SC8 không đổi kết luận; bước (e) 92 s. Cùng đợt sửa kiểm mã SC7: commit merge chỉ bị xét file do chính nó sửa (`git diff-tree --cc`), không còn báo oan merge PR. Số liệu chính thức lấy từ lượt đầy đủ.
- **Giới hạn đã nêu:** giờ đến 14:00 `Europe/Lisbon` là giả định; chính sách hủy giữ ổn định trong Đồ án 2 và khóa luận; `AS_IS_BE` chỉ tính với `NEED_APPROVAL = 1`; tên metric Prometheus của shadow chỉ xác nhận trên lượt bản nhanh (tên sai thì nhãn `UNKNOWN`, không mất pack).
- **Minh chứng:** [plan 5.7b](../spec/archive/phase5/PHASE5_PLAN_5.7b_TASKS.md), [methodology §4.10](research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md), [DATASET_SCHEMA D5](research/DATASET_SCHEMA_SPECIFICATION.md).

---

## 3. Kiến trúc

Chi tiết: [SOFTWARE_ARCHITECTURE_SPECIFICATION](architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md) (Mục 2, 4.4), [ADR](architecture/ARCHITECTURE_DECISION_RECORDS.md) (ADR-006…013).

### 3.1. Clean Architecture nhiều project

```text
   TADSS.Strategies.Default ─┐
   TADSS.Strategies.MockAlternative ─┐     (plugin, chỉ phụ thuộc SDK)
                                     ▼
              Shared.DecisionContractSDK      (Domain + contract, chỉ BCL, 0 NuGet)
                                     ▲
              TADSS.Application               (use case, port, Options, exception)
                                     ▲
              TADSS.Infrastructure            (EF Core/Npgsql, Redis, Polly, migration)
                                     ▲
              TADSS.Runtime.Api               (controller, middleware, composition root)
```

- Mũi tên chỉ hướng phụ thuộc: vào trong. Chỉ `Program.cs` và `Composition/` của Runtime.Api được dùng kiểu của Infrastructure.
- Application cấm EF Core, Npgsql, Redis, Polly, ASP.NET Core; Infrastructure cấm ASP.NET Core.
- Khóa bằng `ProjectReference` (compiler) và UT-14 (namespace khớp thư mục, một kiểu public/file, ≤ 250 dòng, thứ tự middleware, Options chỉ ở `Options/`).

### 3.2. Luồng một request `POST /api/v2/decisions`

1. Middleware (thứ tự cố định, khóa bằng UT-14): forwarded headers → correlation id → request logging → request metrics → exception handler → security headers + HSTS → https redirection → giới hạn kích thước → Swagger (chỉ `Development`) → xác thực API key → rate limiter → routing.
2. `DecisionsController` kiểm header, gọi `DecisionService` (Application).
3. Dựng `Reservation` + `request_hash` (sai hình thức → 400, chưa chạm I/O) → tra idempotency (cùng hash: trả bản cũ; khác: 409).
4. Kiểm thời lượng item (P-01) → `StrategyRouter` chọn strategy → `StrategyEvaluator`: đọc cache nếu strategy cho phép → pipeline Polly nếu miss.
5. `DecisionResponseComposer`: approval window + quy đổi cọc → ghi audit đồng bộ (fail-closed) → 200.
6. Mọi exception đi qua `ExceptionClassifier` → RFC 7807 problem+json với `code` của contract.

Sơ đồ tuần tự đầy đủ (gồm nhánh lỗi, shadow, override): SAS Mục 4.4.

### 3.3. Chịu lỗi và fallback cọc 100 %

- Pipeline Polly v8 (ADR-003): outer timeout **500 ms** → fallback → retry 2 lần → circuit breaker → inner timeout 120 ms. Giá trị và ràng buộc: [`CONFIGURATION_REFERENCE.md` `Tadss:Resilience`](guides/CONFIGURATION_REFERENCE.md#tadssresilience).
- **D-13:** strategy lỗi / timeout / circuit mở → `APPROVED` cọc 100 % (`deposit.amount = totalPrice`), `executionType = FALLBACK`, `fallbackUsed = true`, `reasonCode` theo nguyên nhân (`FALLBACK_STRATEGY_TIMEOUT`, `FALLBACK_STRATEGY_EXCEPTION`, `FALLBACK_CIRCUIT_OPEN`); không vào hàng chờ, không override được, không ghi cache.
- `NEED_APPROVAL` chỉ dành cho bất định **nghiệp vụ** (giá trị lớn, lịch sử không đến…); không đủ thời gian duyệt → `APPROVED` cọc 100 % với `APPROVAL_WINDOW_UNAVAILABLE`.
- Lỗi cache không bao giờ kích hoạt fallback strategy; mất PostgreSQL → 503 `PERSISTENCE_UNAVAILABLE` kèm `Retry-After`.
- Phân loại lỗi đầy đủ: [RESILIENCE_AND_FAULT_TAXONOMY](architecture/RESILIENCE_AND_FAULT_TAXONOMY.md); nghiệp vụ cọc: [INTEGRATION_SPEC Mục 1](contracts/INTEGRATION_SPEC.md).

### 3.4. Audit bất biến và idempotency

- Ba bảng schema `tadss`: `decision_executions` (bất biến), `decision_overrides`, `decision_outcomes` (chỉ thêm); trigger chặn `UPDATE`/`DELETE`/`TRUNCATE` (INV-4, ADR-008).
- **INV-10:** request trả thành công khi và chỉ khi bản ghi audit đã lưu (fail-closed).
- Idempotency theo `X-Idempotency-Key`: cùng key + cùng `request_hash` → trả bản đã lưu; khác hash → 409 `IDEMPOTENCY_KEY_REUSE`; hai request đồng thời do unique index của PostgreSQL phân xử (INV-2).
- `request_hash` tất định, không phụ thuộc múi giờ/culture máy chủ (INV-3).
- Chi tiết: [DECISION_PERSISTENCE_INVARIANTS](architecture/DECISION_PERSISTENCE_INVARIANTS.md) (INV-1…INV-10), [INTEGRATION_SPEC §3.9](contracts/INTEGRATION_SPEC.md).

### 3.5. Cache, định tuyến và shadow

- Cache chỉ lưu `StrategyDecision` (không lưu response). Khi có Redis, đọc Redis trước; `IMemoryCache` chỉ là đường dự phòng, được đọc khi Redis miss hoặc bị bỏ qua và `EnableFallbackToMemory` bật (không có Redis thì chỉ dùng bộ nhớ) (`StrategyDecisionCache.GetAsync`). Khóa có `optionsFingerprint` để đổi tham số strategy không đọc nhầm kết quả cũ; breaker Redis mở thì bỏ qua Redis ngay.
- Định tuyến `Tadss:Routing`: rule đầu tiên khớp theo `PeakHour`, `BranchIds`, `Channels`, `Tiers`, `IsGuest`; nạp lại nóng, bản sai thì giữ bảng cũ.
- Shadow `Tadss:Shadow`: sau khi bản `PRIMARY` đã lưu, worker nền chạy strategy ứng viên và ghi bản `SHADOW`; không đổi response, không vào hàng chờ (INV-8). Bản `SHADOW` có `decision_id` riêng, ghép với `PRIMARY` qua `correlation_id`; trong lượt đánh giá, shadow chỉ bật ở instance PT (PT-04, gói 5.7b).
- Giới hạn khi scale ngang (SCALE-3): breaker, rate limit, cache dự phòng trong bộ nhớ, hàng đợi shadow là trạng thái theo từng instance; PostgreSQL và Redis dùng chung (SAS Mục 3).

### 3.6. Bảo mật

- `X-Api-Key` cho mọi `/api/*`; app chỉ lưu SHA-256 của khóa, so sánh thời gian hằng; ngoài `Development`/`Testing` thiếu khóa thì app **không khởi động** (SEC-4).
- Rate limit token bucket theo `ClientId`, theo từng instance (mặc định 100 request bùng nổ, nạp 100/giây) → 429 `RATE_LIMITED`; body > 64 KB → 413 `PAYLOAD_TOO_LARGE`.
- Security headers trên mọi response, HSTS ngoài `Development`/`Testing`, không gửi header `Server`; health tách `/health/live` và `/health/ready`.
- Container user non-root, lock file + `--locked-mode`, quét lỗ hổng High/Critical trong CI, Dependabot; log không chứa khóa hay mã băm.
- Chi tiết và cách tạo khóa: [README mục Bảo mật](../README.md#bảo-mật), [INTEGRATION_SPEC §3.8](contracts/INTEGRATION_SPEC.md).

### 3.7. Contract v2 đóng băng 2.0.0

- Đóng băng ngày 11/10/2026 gồm `openapi-v2.yaml`, `schemas/v2/`, `baseline_public_api.txt` (khóa bởi `OpenApiDriftTests`, `PublicApiSurfaceTests`).
- Quy tắc sau đóng băng ([INTEGRATION_SPEC Mục 6](contracts/INTEGRATION_SPEC.md)): **được phép** trong `2.x.y` các cải tiến nội bộ không đổi wire và thêm `reasonCode` mới; **bắt buộc lên phiên bản lớn** (`/api/v3`, `schemas/v3/`) khi thêm/xóa/đổi tên/đổi kiểu trường, đổi ràng buộc hay tập giá trị enum.
- SmashCourt-BE không reference SDK; BE tự định nghĩa DTO và kiểm tương thích bằng CT-04 (Phase 6).

---

## 4. Hạ tầng

### 4.1. Ba chế độ chạy

| Chế độ                                     | Lệnh                                                     | Dữ liệu                                                                | Quyết định |
| :----------------------------------------- | :------------------------------------------------------- | :--------------------------------------------------------------------- | :--------- |
| Dev hằng ngày                              | `dotnet run --project src/TADSS.Runtime.Api`             | Neon (schema `tadss`) + Redis Cloud qua `appsettings.Development.json` | D-04, D-12 |
| Vận hành / demo **+ một lượt đánh giá**    | `docker compose up -d --build`                           | Vận hành: Neon + Redis Cloud; đánh giá: PostgreSQL/Redis dùng một lần  | D-11, D-14 |
| Chạy lại đánh giá, không khởi động lại app | `bash tools/evaluation/run.sh [quick\|full] [--steps …]` | Stack đánh giá cô lập                                                  | D-14       |

Test tự động trên CI vẫn ở môi trường E-TC (Testcontainers trong `dotnet test`). Chỉ chế độ đánh giá (E-EVAL) sinh số liệu minh chứng ([methodology Mục 2](research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md)).

### 4.2. Dịch vụ trong `docker-compose.yml`

Một file compose duy nhất, project `tadss`, không profile; mọi cổng host chỉ bind `127.0.0.1` (NG-5).

| Nhóm     | Service                                         | Cổng host                             | Mạng                            | Vai trò                                                                                                                 |
| :------- | :---------------------------------------------- | :------------------------------------ | :------------------------------ | :---------------------------------------------------------------------------------------------------------------------- |
| Vận hành | `tadss-runtime`                                 | `127.0.0.1:8081`                      | `smashcourt-network` (external) | TADSS `Development`, migration lúc khởi động luôn `false`                                                               |
| Vận hành | `ngrok`                                         | `127.0.0.1:4042` (giao diện theo dõi) | `smashcourt-network`            | Expose TADSS; chỉ cần `NGROK_AUTHTOKEN`, domain dev cố định của tài khoản (cổng đổi 4040 ⇒ 4042 ngày 31/10/2026)                                                  |
| Vận hành | `grafana`                                       | `127.0.0.1:3001`, OTLP `4317`/`4318`  | `smashcourt-network`            | `grafana/otel-lgtm` (Prometheus, Tempo, Loki), dashboard nạp sẵn                                                        |
| Đánh giá | `eval-postgres`, `eval-redis`, `eval-toxiproxy` | không                                 | mạng mặc định riêng             | Hạ tầng dùng một lần; Toxiproxy đứng trước PostgreSQL/Redis                                                             |
| Đánh giá | `eval-tadss-1`, `eval-tadss-2`, `eval-proxy`    | không                                 | mạng mặc định riêng             | Hai TADSS `Testing` sau nginx (SCALE-2); instance 1 có tiêm lỗi                                                         |
| Đánh giá | `eval-tadss-portability`                        | không                                 | mạng mặc định riêng             | Instance riêng cho PT (database `tadss_portability`; compose đặt `MaxItemMinutes=100800`, `HighValueThreshold=750` EUR) |
| Đánh giá | `eval-grafana`                                  | `127.0.0.1:3002`                      | mạng mặc định riêng             | Grafana của stack đánh giá, xem lại sau lượt chạy                                                                       |
| Đánh giá | `evaluator`                                     | không                                 | mạng mặc định riêng             | `restart: "no"`, chạy một lượt a…f rồi dừng                                                                             |

Service vận hành không `depends_on` service đánh giá. Evaluator mount `/var/run/docker.sock` (cho Testcontainers), nên chỉ chạy trên máy của chính mình.

### 4.3. Dữ liệu: Neon, Redis Cloud và stack cô lập

- Dev và vận hành dùng Neon (chỉ schema `tadss`) và Redis Cloud; migration lên Neon chỉ chạy khi người dùng cho phép (D-05); `DecisionPersistenceV2` đã áp 10/10/2026.
- `appsettings.Development.json` (chuỗi kết nối, mã băm API key) được copy vào image local như SmashCourt-BE, nên image **không bao giờ** được push (D-11). *Cập nhật Epic 6C (31/10/2026, P6-33): file không còn vào image, compose mount chỉ đọc lúc chạy; image đẩy lên GHCR.*
- Stack đánh giá **không bao giờ chạm Neon**: khóa API cố định không bí mật, bản sao mã nguồn loại `appsettings.Development.json` và `.env*`; CSV SmashCourt thô ở `data/private/` (gitignore, mount chỉ đọc), định danh được băm trước khi vào `evidence/`.

### 4.4. CI (`.github/workflows/ci.yml`)

Chạy khi push hoặc pull request vào `master`, `develop`.

| Job                   | Nội dung                                                                                                                                                                                                                             |
| :-------------------- | :----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `build-and-test`      | Restore `--locked-mode` → quét lỗ hổng (`tools/ci/check-vulnerable-packages.sh`) → build Release `--warnaserror` (0 warning) → unit test → integration test (Testcontainers) → báo cáo trx → build image Docker → kiểm user non-root |
| `expose-check`        | `tools/expose/verify-expose.sh` (NG-1, NG-2, NG-3, NG-5, SEC-6)                                                                                                                                                                      |
| `observability-check` | `tools/observability/verify-observability.sh` (OB-2, OB-4)                                                                                                                                                                           |
| `evaluation-tools`    | Pytest `tools/evaluation` và `tools/evidence`; build thử image evaluator (không chạy)                                                                                                                                                |
| `docs`                | `python tools/docs/check_docs.py` (DOC-1…3) + pytest `tools/docs`                                                                                                                                                                    |

### 4.5. E-STAGING (Phase 7–8)

Môi trường tích hợp FE ↔ BE ↔ TADSS trên máy nhóm bằng Docker Compose, dữ liệu trên **nhánh Neon riêng `staging`** (R-07; thay 19/10/2026: staging là nhánh `tadss-dev`, DB TADSS là project Neon riêng), VNPay sandbox; chỉ bật `docker compose up -d --build tadss-runtime grafana` ở TADSS. Dựng ở W13 (T7.2); dùng cho E2E, LT-04, FI-11…14. Chi tiết: [MASTER_EXECUTION_PLAN, mục E-STAGING](planning/MASTER_EXECUTION_PLAN.md#môi-trường-tích-hợp-e-staging).

---

## 5. Kiểm thử và đánh giá

Nguồn sở hữu: [EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY](research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) (danh mục Mục 4, KPI Mục 5, Evidence Pack Mục 6, giới hạn Mục 8).

### 5.1. Danh mục theo nhóm

| Nhóm                     | Một dòng                                                                                                                                                                   | Chạy ở                                     | Phase        |
| :----------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | :----------------------------------------- | :----------- |
| UT-01…15                 | Domain, serialization, strategy, routing, hash, pipeline, map lỗi, kiến trúc (UT-09, UT-14), quản trị                                                                      | E-TC                                       | 5            |
| IT-01…15                 | API trên PostgreSQL/Redis thật: idempotency, override/outcome, hàng chờ, shadow, cache, migration                                                                          | E-TC                                       | 5            |
| CT-01…04                 | Schema JSON v2, baseline public API, Schemathesis (CT-03), `TadssClient` của BE (CT-04)                                                                                    | E-TC, E-EVAL                               | 5; CT-04 ở 6 |
| FI-01…14                 | Tiêm lỗi strategy/Redis/PostgreSQL/client hủy (FI-01…10); FI-11…14 ở mức E2E                                                                                               | E-TC, E-EVAL; FI-11…14 ở E-STAGING         | 5; 6–8       |
| LT-01…05                 | k6: 50 RPS, tăng dần, sự cố dưới tải, soak 60 phút; LT-04 qua BE                                                                                                           | E-EVAL; LT-04 ở E-STAGING                  | 5; LT-04 ở 8 |
| MT-01…03                 | Stryker trên SDK, Strategies, lõi runtime; ngưỡng 70 %                                                                                                                     | E-EVAL                                     | 5            |
| SEC-1…7                  | Rate limit, giới hạn body, header, fail-closed, health, container, chuỗi cung ứng                                                                                          | E-TC, CI                                   | 5            |
| NG-1…5                   | Cấu hình compose, khóa API, truy cập DB, tunnel thật (NG-4 người dùng chạy), cổng loopback                                                                                 | `verify-expose.sh`                         | 5            |
| SCALE-1…3                | Hai host (IT), hai replica sau nginx (E-EVAL), tài liệu giới hạn scale                                                                                                     | E-TC, E-EVAL                               | 5            |
| OB-1…5                   | Dashboard chỉ dùng metric thật, tự nạp, snapshot sau sự cố, có dữ liệu khi chạy Docker, telemetry trong tiến trình                                                         | test, `verify-observability.sh`, evaluator | 5            |
| DOC-1…4                  | Kiểm kê tài liệu, thuật ngữ đã gỡ, link nội bộ + anchor, bảng cấu hình khớp Options                                                                                        | `check_docs.py`, unit test                 | 5            |
| RP-01…02                 | Replay booking SmashCourt, so sánh với cọc 100 % tĩnh (mô tả)                                                                                                              | E-EVAL bước (e)                            | 5 (215/215, 16/10) |
| PT-01…02                 | Hotel Booking Demand ≥ 10.000 đơn qua contract v2, không sửa mã                                                                                                            | E-EVAL bước (e)                            | 5            |
| PT-03…04                 | Mô phỏng kinh tế chính sách cọc theo chính sách hủy SmashCourt; shadow `MockAlternative` cạnh `Default` kèm đối soát và ghép cặp (kết quả mô tả, không phải KPI đạt/trượt) | E-EVAL bước (e), (f)                       | 5 (5.7b)     |
| BE-UT-01…14, BE-IT-01…27 | SmashCourt-BE: ánh xạ, `TadssClient`, luồng cọc, endpoint duyệt, job, SignalR, mạch ngắt (P6-57)                                                                                              | E-TC (BE)                                  | 6            |
| FE-01…12                 | `tsc`/lint/build, luồng giao diện trên mock MSW                                                                                                                            | FE                                         | 6            |
| E2E-01…14                | Playwright qua FE → BE → TADSS ([đặc tả từng bước](research/E2E_TEST_SPECIFICATION.md))                                                                                    | E-STAGING                                  | 7–8          |

### 5.2. KPI SC1–SC8

| KPI | Mục tiêu thiết kế (methodology Mục 5)                                                                              | Kết quả hiện có                                                                                                         |
| :-- | :----------------------------------------------------------------------------------------------------------------- | :---------------------------------------------------------------------------------------------------------------------- |
| SC1 | p95 TADSS < 150 ms (LT-01); p95 tạo booking E2E < 1000 ms                                                          | `PASS` ở `quick-r2`: tỉ lệ ≤ 150 ms phía server thấp nhất 99,76 %, p95 phía client lớn nhất 34,7 ms (`quick-r1` cùng ngày `FAIL` vì instance lạnh treo lan sang pha chính, Mục 8); phần E2E ở Phase 8 |
| SC2 | 100 % request lỗi strategy kết thúc bằng `APPROVED` cọc 100 % `FALLBACK`, pipeline ≤ 500 ms; p99 endpoint ≤ 700 ms | `PASS` (`quick-r2`)                                                                                                     |
| SC3 | Số response 200 = số bản `PRIMARY`; 0 lần sửa audit thành công                                                     | `PASS` (`quick-r2`)                                                                                                     |
| SC4 | 0 quyết định trùng; 0 override kép                                                                                 | `PASS` (`quick-r2`)                                                                                                     |
| SC5 | Tắt feature flag có hiệu lực < 2 giây                                                                              | `NOT_APPLICABLE` (đo bằng FI-13 trên E-STAGING, Phase 7–8)                                                              |
| SC6 | 0 vi phạm bất biến booking                                                                                         | `NOT_APPLICABLE` (đo trên E-STAGING, Phase 7–8)                                                                         |
| SC7 | PT ≥ 10.000 bản ghi, 0 lỗi contract, 0 thay đổi mã runtime/SDK                                                     | `PASS` (`quick-r2`)                                                                                                     |
| SC8 | 100 % test case đạt; mutation ≥ 70 %                                                                               | `INSUFFICIENT_DATA` ở bản nhanh (không chạy Stryker); bước (d) bản đầy đủ riêng đã đạt MT-01…03 ≥ 70 %                  |

Số liệu bản nhanh chỉ để diễn tập; số liệu chính thức lấy từ lượt đầy đủ (methodology Mục 4.6). Lượt đầy đủ `EV-20261017-EVAL-full-r1` (17/10/2026, strategy 2.0.0): SC1–SC4, SC7, SC8 `PASS` (SC1: p95 phía client lớn nhất 56,2 ms; SC7: 118.674 bản ghi PT, 0 lỗi contract; SC8: MT-01 85,0 %, MT-02 98,3 %, MT-03 73,9 %), chi tiết ở [báo cáo 5.7](../spec/archive/phase5/PHASE5_REPORT_5.7.md) Mục 2. Sau bảng này (Phase 6): SC6 đã kiểm bằng bộ kiểm bất biến của SmashCourt-BE sau mỗi test HTTP và sau ba kịch bản lỗi dưới tải SP-09 ([báo cáo hiệu năng](report/PERFORMANCE_REPORT.md) Mục 6.8); SC5 (FI-13) chưa có số đo chính thức.

### 5.3. Sáu bước của evaluator

| Bước     | Làm gì                                                                                                                                                       |            `quick-r2` (đo thật) |
| :------- | :----------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------: |
| (a)      | Build, unit + integration gộp theo `CatalogId`, `verify-expose.sh`, `verify-observability.sh`, `verify-export.sh`, `check_docs.py`, ba bộ pytest, OB-2       |                           693 s |
| (b)      | k6 LT-01, LT-02 (bản đầy đủ thêm LT-05 soak và LT-01 × 3)                                                                                                    |                           400 s |
| (c)      | SCALE-2, rồi sự cố dưới tải LT-03 (FI-02/03/07/08 qua decorator và Toxiproxy) + snapshot dashboard                                                              |                           511 s |
| (d)      | Schemathesis CT-03, coverage gộp; Stryker MT-01…03 chỉ ở bản đầy đủ                                                                                          |                            48 s |
| (e)      | Dữ liệu thực tế: RP (cần CSV, không có thì `skipped`), PT (từ 5.7b kèm shadow và xả hàng đợi; bước (f) dựng PT-03/PT-04, bootstrap ước ~5 phút ở bản đầy đủ) | 92 s |
| (f)      | Evidence Pack: D1, SC1…SC8, EV-1…EV-5, `report.html`, `manifest.json`                                                                                        |                            61 s |
| **Tổng** |                                                                                                                                                              |           **1805 s (~30 phút)** |

Bản đầy đủ **đo thật ≈ 3 giờ 25 phút** (`full-r1`, 17/10/2026: a 550 s, b 6681 s, c 1069 s, d 2705 s, e 762 s, f 517 s); bước (b) chiếm gần 2 giờ vì có LT-05 60 phút và ba lượt LT-01. Mã thoát: `0` đạt mọi tiêu chí, `1` có tiêu chí không đạt, `2` lỗi hạ tầng hoặc không chạy hết.

### 5.4. Evidence Pack

- Thư mục `evidence/<run_id>/` (`run_id` = `EV-{yyyyMMdd}-EVAL-{quick|full}-r{n}`; thư mục làm việc, không commit): `report.html` tự chứa, `manifest.json` (SHA-256 từng file, commit, cấu hình, máy, KPI), `raw/` (trx theo `CatalogId`, k6, `chaos_log`, metric, D1, D2, D4, D5), `grafana/dashboard-snapshot.json` (không lưu ảnh), `config/`.
- Kiểm toàn vẹn EV-1…EV-5: SHA-256 khớp manifest; `decision_id` của D2 có trong D1; số response thành công = số bản `PRIMARY`; không có dữ liệu cá nhân; manifest đủ commit, cấu hình, thông số máy.
- Dựng lại `report.html` từ một pack có sẵn (không gửi lại request), theo [runbook mục 6.4](guides/RUNBOOK.md): `docker compose run --rm --no-deps --entrypoint python evaluator -m evidence.report /evidence/<run_id>`, hoặc ngoài Docker `cd tools && python -m evidence.report ../evidence/<run_id>` (module nằm trong `tools/`, chạy từ gốc repo không được).
- Pack chính thức của lượt đầy đủ sẽ chốt vào `docs/research/evidence-packs/{run_id}/` (M-10; file > 20 MB đính kèm GitHub Release). Từ 5.7b: D5, `pt_summary.json`, `policy_economics.json` và mục PT của `report.html` **chỉ vào pack chính thức sau khi xác minh giấy phép và điều kiện phân phối của đúng nguồn dataset đang ghim** (quyết định từng artifact); trước đó chúng chỉ sinh trong `evidence/<run_id>/` (gitignore).
- Định nghĩa: [DATASET_SCHEMA_SPECIFICATION](research/DATASET_SCHEMA_SPECIFICATION.md) Mục 2, 5.

---

## 6. Lệnh quan trọng

Mọi lệnh (dev 4 repo, bản deploy, CD, Grafana Cloud, lượt đánh giá TADSS, dashboard "Kiểm thử", đo hiệu năng, sự cố, dọn Docker) nằm ở **một nơi**: [`RUNBOOK.md`](guides/RUNBOOK.md). Lệnh hay dùng nhất của Phase 5: `docker compose up -d --build` (app + một lượt đánh giá bản nhanh, runbook Mục 6), `bash tools/evaluation/run.sh quick --steps e,f` (chạy lại vài bước), `dotnet build TADSS.slnx -c Release --warnaserror` + `dotnet test tests/TADSS.UnitTests -c Release --no-build` (runbook Mục 1), `python tools/docs/check_docs.py` (DOC-1…3).

---

## 7. Cách trình bày trước hội đồng

### 7.1. Luận điểm then chốt

|  #  | Luận điểm                                                                                                                                                                                                                                                                                                                                                                                                                                                                        | Minh chứng                                                      |
| :-: | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | :-------------------------------------------------------------- |
|  1  | **Mọi sự cố kỹ thuật cùng một quy tắc: cọc 100 %.** Fallback trả `APPROVED` cọc 100 % thay vì `NEED_APPROVAL` để circuit mở không dồn hàng loạt đơn cho Manager; `NEED_APPROVAL` chỉ cho bất định nghiệp vụ                                                                                                                                                                                                                                                                      | D-13; INTEGRATION_SPEC Mục 1; FI-02/03/04; SC2                  |
|  2  | **Audit fail-closed:** không có bản ghi thì không trả quyết định; ba bảng chỉ-thêm có trigger, nên mọi quyết định và mọi lần Manager can thiệp truy được                                                                                                                                                                                                                                                                                                                         | INV-4, INV-10; FI-08; SC3                                       |
|  3  | **Phát hiện "cam kết không chắc chắn":** cắt PostgreSQL đúng lúc `COMMIT` có thể cho 503 trong khi bản ghi đã lưu; hệ thống vẫn đúng vì client thử lại cùng `X-Idempotency-Key` nhận đúng quyết định đã lưu                                                                                                                                                                                                                                                                      | Báo cáo 5.5 mục 3j; INTEGRATION_SPEC §4.3 (retry cùng key)      |
|  4  | **Kiểm thử trên hạ tầng thật** (ADR-009) làm lộ các lỗi mà test giả lập không thấy: SCALE-1 (hai host trên PostgreSQL thật) bắt lỗi đua khi dựng pipeline đồng thời, sửa bằng `Lazy`; LT-03 cắt PostgreSQL qua Toxiproxy lộ ca "cam kết không chắc chắn" (503 nhưng bản ghi đã lưu); dưới tải, `eval-postgres` cạn `max_connections` (100 < 3 instance × pool 100, "too many clients") làm LT-01/02 trả 503/504, sửa bằng `max_connections=350`. EF InMemory đã gỡ khỏi solution | Báo cáo 5.5 mục 2, 3b, 3j; báo cáo 5.6 Mục 4; methodology Mục 0 |
|  5  | **Stack đánh giá cô lập:** không làm bẩn Neon dùng chung (audit chỉ-thêm không xóa được từng dòng), Toxiproxy phải đứng trước DB, mạng tới Neon làm nhiễu số đo                                                                                                                                                                                                                                                                                                                  | D-04, D-12, D-14; R-07                                          |
|  6  | **Một lệnh tái lập:** `docker compose up -d --build` chạy test, tải, sự cố, contract và sinh Evidence Pack có SHA-256 từng file, commit và cấu hình máy. Mặc định là **bản nhanh** (không Stryker, không LT-05); bản đầy đủ (thêm mutation, LT-05 soak, LT-01 × 3) cần `EVAL_MODE=full`, đã chạy trọn một lần (`EV-20261017-EVAL-full-r1`, 17/10/2026, ≈ 3 giờ 25 phút; chưa chạy lại trên strategy 2.1.0)                                                                                                                                                        | D-14; EV-1…EV-5; Mục 5.3                                        |
|  7  | **Kiến trúc được khóa, không chỉ quy ước:** compiler (`ProjectReference`) + UT-14; contract khóa bằng drift test OpenAPI và baseline public API                                                                                                                                                                                                                                                                                                                                  | P-03, ADR-013; CT-02                                            |
|  8  | **Bộ test bắt được lỗi thật:** mutation ≥ 70 % cả ba phạm vi (lượt đầy đủ `full-r1` ngày 17/10: 85,0 % / 98,3 % / 73,9 %; lượt bước (d) `EV-20261008-EVAL-full-r4` ngày 08/10, trước khi đóng băng: 84,73 % / 98,32 % / 74,43 %); Schemathesis tìm lỗi 404 sai contract; SCALE-1 tìm lỗi đua dựng pipeline                                                                                                                                                                                                                                              | Báo cáo 5.5 3k, mục 2                                           |
|  9  | **Độc lập domain:** cùng runtime xử lý 10.000 đơn khách sạn (EUR, `Europe/Lisbon`, lưu trú nhiều đêm) chỉ bằng cấu hình, 0 lỗi contract. 10.000 là mẫu của bản nhanh; lượt đầy đủ `full-r1` chạy 118.674 đơn, 0 lỗi contract                                                                                                                                                                                                                                                                     | PT-01, SC7 `PASS` (`quick-r1`, `full-r1`); P-01                            |
| 10  | **Trung thực về giới hạn:** số đo là số liệu phòng lab trên một máy, báo cáo kèm CPU/RAM; bản nhanh chỉ để diễn tập. Lượt đầy đủ `full-r1` đo trên strategy 2.0.0 và có 0,04–0,11 % response 504 ở bước tải do máy bận, chưa chạy lại trên 2.1.0; Evidence Pack chính thức (M-10) chưa chép vào repo; RP dùng 215 booking của môi trường phát triển nên chỉ minh họa cơ chế; SC5 (FI-13) chưa có số đo chính thức                                                                                  | Methodology Mục 8; Mục 1 (Gate M5), Mục 8 của tài liệu này      |

### 7.2. Câu hỏi khó dự kiến

| Câu hỏi                                                    | Trả lời ngắn (nguồn)                                                                                                                                                                                                                                                                                                                                                                                                         |
| :--------------------------------------------------------- | :--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Đã có lượt đánh giá đầy đủ chưa? | Có: `EV-20261017-EVAL-full-r1` (17/10/2026, ≈ 3 giờ 25 phút) đạt SC1–SC4, SC7, SC8; bước tải có 0,04–0,11 % response 504 do máy bận, đã giải thích bằng thí nghiệm A/B ([báo cáo 5.7](../spec/archive/phase5/PHASE5_REPORT_5.7.md)); sẽ chạy lại trên strategy 2.1.0 khi máy rảnh |
| Số đo trên một máy có tin được không?                      | Là số liệu phòng lab: k6, TADSS và DB tranh CPU; báo cáo ghi cấu hình máy, chỉ so sánh tương đối giữa kịch bản cùng máy (methodology Mục 8)                                                                                                                                                                                                                                                                                  |
| Không có pilot thì đánh giá chất lượng quyết định thế nào? | Bằng dữ liệu import (RP, PT) và chỉ báo cáo mô tả; mức độ dễ dùng và hành vi thật để Khóa luận (methodology Mục 8)                                                                                                                                                                                                                                                                                                           |
| Replay chứng minh được giảm no-show không?                 | Không: replay là phân tích phản thực tế, mức cọc có thể đổi chính hành vi khách; không kết luận nhân quả (methodology Mục 8)                                                                                                                                                                                                                                                                                                 |
| 215 booking có đại diện không?                             | Là dữ liệu môi trường phát triển, nhãn `NO_SHOW` do job gán; chỉ dùng làm input có hình dạng thực tế (methodology Mục 8)                                                                                                                                                                                                                                                                                                     |
| TADSS có dừng được strategy treo không?                    | Không. `IsolatedStrategyInvoker` giới hạn thời gian chờ và cô lập request (fallback trong ngân sách), task bị bỏ rơi vẫn tốn CPU tới khi tự kết thúc; đếm bằng `tadss.strategy.abandoned` (SAS Mục 3, methodology Mục 8)                                                                                                                                                                                                     |
| Scale ngang có vấn đề gì?                                  | Breaker, rate limit, cache dự phòng trong bộ nhớ theo từng instance; idempotency qua PostgreSQL và cache Redis dùng chung đúng khi nhiều instance (SCALE-1, SCALE-2, SCALE-3)                                                                                                                                                                                                                                                |
| Môi trường đánh giá khác production ở đâu?                 | Chạy `Testing` (decorator tiêm lỗi), rate limit nâng 100000, trace lấy mẫu 10 %; metric và audit không lấy mẫu nên KPI không bị ảnh hưởng; bảo mật kiểm riêng ở SEC-1…7 (methodology Mục 8)                                                                                                                                                                                                                                  |
| Tại sao đóng băng contract trước lượt đầy đủ?              | Các sửa của 5.7 không đổi wire nên không lệch số đo; Evidence Pack chính thức phải sinh từ đúng mã đã đóng băng (R-01, R-03)                                                                                                                                                                                                                                                                                                 |
| Phân vị độ trễ lấy từ histogram có chính xác?              | Chỉ chính xác ở biên bucket; SC1 kết luận bằng cả tỉ lệ bucket phía server và p95 phía client của k6 (methodology Mục 8)                                                                                                                                                                                                                                                                                                     |

---

## 8. Việc còn mở và bước tiếp theo

Ảnh chụp lúc khép Phase 5 (18/10/2026), giữ để tra lại; việc còn mở **hiện tại** ở Mục 0.

| Việc                                                                                                                                                                   | Chủ                            | Trạng thái           | Ghi chú                                                                                                                                                         |
| :--------------------------------------------------------------------------------------------------------------------------------------------------------------------- | :----------------------------- | :------------------- | :-------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| U-2: xác nhận giấy phép Hotel Booking Demand (CC BY 4.0) | Người dùng (SV1) | ✅ 17/10 | Bài báo truy cập mở ghi "open access article under the CC BY license" (PMC6297060); README TidyTuesday ở commit đang ghim ghi nguồn đúng bài báo. D5, `pt_summary.json`, `policy_economics.json` và mục PT của báo cáo vào pack chính thức kèm trích dẫn (`CITATION`, `hotel_adapter.py`) |
| Instance TADSS khởi động lạnh bị treo 5–56 s khi tải dồn vào (LT-01 warm-up; SCALE-2 ở `full-r3` ra một 504 → SC4 `FAIL`) | SV1 | ✅ đã sửa 16/10, chờ lượt đầy đủ xác nhận | Nguyên nhân (thí nghiệm 16/10, 13 lượt): những request đầu gánh chi phí khởi tạo (JIT, biên dịch truy vấn EF, mở kết nối); không phải thread pool (tăng min threads không đỡ), log chỉ khuếch đại. Sửa: `DecisionPathWarmup` + `Tadss:Warmup` làm nóng lúc khởi động, không ghi audit/cache (UT-11, IT-01). Sau sửa: 6/7 lượt treo ≤ 3,1 s, 0 lỗi 5xx; 1 lượt ngay sau khi build image treo 9,3 s |
| Q-6: gốc tính tiền hoàn khi khách chỉ trả cọc (hiện BE tính trên `FinalTotal`, B-1)                                                                                    | SV1                            | ✅ chốt 18/10        | Hoàn = tổng đã thanh toán × % hoàn, không vượt số đã trả (Q12 của `DEPOSIT_APPROVAL_API.md`, review T5.29); BE sửa ở Phase 6 (T6.9). PT-03: `REFUND_OF_PAID` là chính sách đã chốt, `REFUND_OF_TOTAL_CAPPED` còn lại là phân tích độ nhạy |
| B-2: nghi lệch +7 giờ trong `CalculateRefundPercentAsync` của BE                                                                                                       | SV1                            | ⏳ trace sau         | Người dùng báo vận hành đang đúng; chưa sửa, mô phỏng tính đúng múi giờ                                                                                         |
| U-3: xuất CSV SmashCourt vào `data/private/` | Người dùng (SV1) | ✅ 16/10 (215 booking) | Lần xuất đầu lộ lỗi câu SQL (gói 5.6): BE tắt `is_active` của mọi sân khi booking kết thúc (NO_SHOW, COMPLETED, PENDING_PAYMENT, không chỉ khi hủy) nên 118/215 booking bị loại, gồm mọi NO_SHOW; đã sửa (booking không còn sân hoạt động thì xuất mọi sân; dữ liệu mẫu `verify-export` theo đúng BE), RP 215/215, 0 lỗi contract. Cùng đợt: đối soát shadow PT báo `FAILED` oan khi `run.sh --steps e,f` tạo lại instance PT (truy vấn tức thời thấy số của tiến trình cũ) → truy vấn chỉ lấy series còn được xuất 15 s |
| U-4: commit, chạy lượt đầy đủ, gửi `logs --tail 60 evaluator` + `ps -a` | Người dùng (SV1) | ✅ 17/10 (`full-r1`) | Chạy lại trên 2.1.0 khi máy rảnh |
| Task 10 plan 5.7: phân tích lượt đầy đủ, chốt Evidence Pack (M-10), báo cáo Gate M5 | SV1 | ✅ 18/10 (báo cáo); M-10 chờ lượt cuối | [`spec/archive/phase5/PHASE5_REPORT_5.7.md`](../spec/archive/phase5/PHASE5_REPORT_5.7.md) |
| NG-4: thử qua tunnel ngrok thật với Neon thật                                                                                                                          | Người dùng (SV1)               | ⏳                   | BACKLOG T5.30                                                                                                                                                   |
| T5.24 review contract v2; T5.29 review API BE ↔ FE | SV2 / SV1 | T5.29 ✅ 18/10; T5.24 ⏳ | T5.29: R-1…R-8 sửa, Q1…Q12 chốt trong `DEPOSIT_APPROVAL_API.md` Mục 9–10 |
| T5.27 đặc tả E2E (bản đầu của SV1)                                                                                                                                     | SV2 review                     | ⏳                   | Bàn lại sau Phase 5                                                                                                                                             |
| Phase 6 (W09–W12): BE tích hợp TADSS (`TadssClient`, `PENDING_APPROVAL`, job hết hạn, CT-04), FE trên mock theo hợp đồng BE ↔ FE                                       | SV1 (BE), SV2 (FE)             | Kế hoạch             | [MASTER_EXECUTION_PLAN Phase 6](planning/MASTER_EXECUTION_PLAN.md), [`DEPOSIT_APPROVAL_API.md`](../../SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md)           |

Ghi chú cho Phase 6: SmashCourt-BE gặp 503/timeout phải thử lại với **cùng** khóa idempotency (báo cáo 5.5 mục 3j).

---

## 9. Bản đồ tài liệu

Danh sách đầy đủ và nhóm của từng tài liệu: [README mục Tài liệu](../README.md#tài-liệu).

| Cần gì                                                | Đọc                                                                                                                                                                                                                                  |
| :---------------------------------------------------- | :----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Contract v2, nghiệp vụ cọc, mã lỗi, quy tắc phiên bản | [INTEGRATION_SPEC](contracts/INTEGRATION_SPEC.md)                                                                                                                                                                                    |
| Cấu trúc project, luồng request, giới hạn             | [SOFTWARE_ARCHITECTURE_SPECIFICATION](architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md)                                                                                                                                           |
| Schema `tadss`, INV-1…INV-10, lệnh EF                 | [DECISION_PERSISTENCE_INVARIANTS](architecture/DECISION_PERSISTENCE_INVARIANTS.md)                                                                                                                                                   |
| Pipeline chịu lỗi, phân loại lỗi                      | [RESILIENCE_AND_FAULT_TAXONOMY](architecture/RESILIENCE_AND_FAULT_TAXONOMY.md)                                                                                                                                                       |
| Vì sao chọn kiến trúc                                 | [ARCHITECTURE_DECISION_RECORDS](architecture/ARCHITECTURE_DECISION_RECORDS.md)                                                                                                                                                       |
| Danh mục test, KPI, giới hạn                          | [EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY](research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md)                                                                                                                           |
| Evidence Pack, D1–D5, EV-1…EV-5                       | [DATASET_SCHEMA_SPECIFICATION](research/DATASET_SCHEMA_SPECIFICATION.md)                                                                                                                                                             |
| Kịch bản E2E từng bước                                | [E2E_TEST_SPECIFICATION](research/E2E_TEST_SPECIFICATION.md)                                                                                                                                                                         |
| Chạy đánh giá, đọc kết quả, sự cố                     | [RUNBOOK mục 6](guides/RUNBOOK.md)                                                                                                                                                                               |
| Khóa cấu hình `Tadss:*`                               | [CONFIGURATION_REFERENCE](guides/CONFIGURATION_REFERENCE.md)                                                                                                                                                                         |
| Lịch, gate, E-STAGING                                 | [MASTER_EXECUTION_PLAN](planning/MASTER_EXECUTION_PLAN.md)                                                                                                                                                                           |
| Trạng thái task T5.x                                  | [BACKLOG_SPRINTS](planning/BACKLOG_SPRINTS.md)                                                                                                                                                                                       |
| Lý do từng quyết định D-xx, P-xx, R-xx                | [PHASE5_DESIGN_SPEC](../spec/archive/phase5/PHASE5_DESIGN_SPEC.md)                                                                                                                                                                                  |
| Hợp đồng BE ↔ FE (Phase 6)                            | [`SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md`](../../SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md) (link sang repo khác chỉ mở được khi xem trong workspace chứa cả `SmashCourt-BE/`; trên GitHub thì mở repo `SmashCourt-BE`) |
| Spec Phase 6: quyết định P6-01…P6-31, thiết kế BE / FE, truy vết | [PHASE6_DESIGN_SPEC](../spec/PHASE6_DESIGN_SPEC.md) |
| Epic 6C: CI/CD, quan sát, môi trường đo, nhật ký đo, P6-32…P6-57 | [plan 6C](../spec/PHASE6_PLAN_6C_DEVOPS_OBSERVABILITY_PERF.md) |
| Việc FE theo từng file, hợp đồng chính xác BE ↔ FE | [PHASE6_FE_HANDOFF](../spec/PHASE6_FE_HANDOFF.md) |
| Kết quả hiệu năng (SP-01…10, nút cổ chai, trước / sau) | [PERFORMANCE_REPORT](report/PERFORMANCE_REPORT.md) |
| Lệnh chạy mọi thứ, sự cố, dọn Docker | [RUNBOOK](guides/RUNBOOK.md) |
| Spec, plan, báo cáo gói đã xong | `spec/archive/phase5/`, `spec/archive/phase6/` |
