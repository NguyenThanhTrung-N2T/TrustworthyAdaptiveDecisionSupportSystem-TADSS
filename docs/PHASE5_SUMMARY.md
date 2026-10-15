# TỔNG HỢP PHASE 5 — TADSS v2 VÀ HẠ TẦNG KIỂM THỬ

|                      |                                                                                                                                                                                                                                                                   |
| :------------------- | :---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Vai trò**          | Bản tổng hợp để SV1 review toàn bộ Phase 5, trình bày với hội đồng và SV2, tra lệnh chạy nhanh. Tài liệu chỉ **tóm tắt và dẫn link**; mọi thông số chi tiết nằm ở tài liệu sở hữu (quy tắc "tài liệu sở hữu thắng", [README mục Tài liệu](../README.md#tài-liệu)) |
| **Phạm vi**          | Phase 5 (W05–W08, 05/10 – 01/11/2026 theo kế hoạch v2.1; bắt đầu thực tế 02/10), repo `TADSS-Runtime`, phần việc của SV1                                                                                                                                          |
| **Số liệu tính đến** | 16/10/2026 (phần mã và tài liệu gói 5.7 và gói phụ 5.7b đã xong; lượt bản nhanh sau 5.7b `EV-20261015-EVAL-quick-r2` đạt mọi tiêu chí áp dụng được; lượt đánh giá đầy đủ chính thức chưa chạy)                                                                                                                            |
| **Nguồn chính**      | [`PHASE5_DESIGN_SPEC.md`](../spec/PHASE5_DESIGN_SPEC.md) (quyết định D-xx, P-xx, R-xx), các plan và báo cáo gói trong `spec/`, [`MASTER_EXECUTION_PLAN.md`](planning/MASTER_EXECUTION_PLAN.md), [`BACKLOG_SPRINTS.md`](planning/BACKLOG_SPRINTS.md)               |

Khi tài liệu này khác tài liệu sở hữu, tài liệu sở hữu đúng; sửa file này theo.

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
| Integration test                                         | 240, đạt ở bước (a) của `EV-20261015-EVAL-quick-r2` (15/10/2026, sau các thay đổi của gói 5.7)                                | README, [`PHASE5_REPORT_5.6.md`](../spec/PHASE5_REPORT_5.6.md) |
| Pytest                                                   | `tools/evaluation` 257 đạt + 3 bỏ qua (260 khi có Docker/k6); `tools/evidence` 355; `tools/docs` 34 (15/10/2026, sau gói 5.7b) | [Guide mục 9](guides/EVALUATION_RUN_GUIDE.md)                  |
| Lượt bản nhanh                                           | `EV-20261015-EVAL-quick-r2` (sau 5.7b): mã thoát 0, đạt mọi tiêu chí áp dụng được, ~30 phút. Lượt `quick-r1` cùng ngày trượt SC7 (kiểm mã báo oan, đã sửa) và SC1 (instance lạnh treo, Mục 8)                                               | Báo cáo 5.6 Mục 3                                              |
| Mutation (bước d bản đầy đủ, `EV-20261008-EVAL-full-r4`) | MT-01 84,73 %, MT-02 98,32 %, MT-03 74,43 % (ngưỡng 70 %)                                                                      | [`PHASE5_REPORT_5.5.md`](../spec/PHASE5_REPORT_5.5.md) 3k      |
| Schemathesis CT-03 bản đầy đủ                            | 6905 ca, 0 lỗi                                                                                                                 | Báo cáo 5.5 3k                                                 |
| Coverage gộp (unit + integration)                        | dòng 98,4 %, nhánh 89,8 %                                                                                                      | Báo cáo 5.5 3k, 5.6 Mục 3                                      |

**Trạng thái Gate M5 — "TADSS Complete & Test-Ready"** (tiêu chí ở [MASTER_EXECUTION_PLAN, Phase 5](planning/MASTER_EXECUTION_PLAN.md)):

| Tiêu chí                                                                                                                      | Trạng thái 15/10/2026                                                                                                                                                                        |
| :---------------------------------------------------------------------------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Toàn bộ test case TADSS (UT, IT, CT, FI, LT mức TADSS, MT) đạt trên hạ tầng thật; không còn EF InMemory (đã gỡ ở gói 5.1–5.2) | ⏳ Unit 1874, integration 240 và pytest xanh ở `EV-20261015-EVAL-quick-r2`; LT-05 và LT-01 × 3 chỉ có ở bản đầy đủ, chưa chạy trọn vẹn |
| Contract v2 đóng băng                                                                                                         | ✅ 11/10/2026 (`2.0.0`, M-07)                                                                                                                                                                |
| Dashboard, Toxiproxy, k6, Schemathesis, Stryker, bộ sinh báo cáo chạy bằng một lệnh và **đã có lượt đầy đủ**                  | ⏳ Một lệnh đã có; lượt đầy đủ chờ người dùng (U-3 xuất CSV, U-4 chạy)                                                                                                                       |
| Danh mục kiểm thử toàn hệ thống (BE, FE, E2E) viết xong; API BE ↔ FE đã chốt                                                  | ⏳ Đã viết (BE-UT-01…07, BE-IT-01…20, FE-01…10, E2E-01…13; T5.22, T5.27); chờ SV2 review (T5.24, T5.29)                                                                                      |

Sau lượt đầy đủ còn Task 10 của plan 5.7: phân tích lượt, chốt Evidence Pack vào `docs/research/evidence-packs/{run_id}/` (M-10), viết báo cáo Gate M5. Gói phụ 5.7b (mục 2.11) làm **trước** lượt đầy đủ để pack chính thức có luôn PT-03/PT-04; lượt bản nhanh kiểm chứng cơ chế (ghép cặp shadow, tên metric Prometheus, thời gian bước (e)) rồi mới quyết định chạy lượt đầy đủ.

---

## 2. Các gói đã làm

Thứ tự phụ thuộc 5.1 → 5.2 → 5.3 → 5.4 → 5.5 → 5.6 → 5.7 (spec Mục 3). Bảng chia gói, test case từng gói và điều kiện xong: spec Mục 3.

**Tiến trình số test unit + integration** (theo [BACKLOG T5.12](planning/BACKLOG_SPRINTS.md) và các báo cáo gói; số lượng chỉ là chỉ số phụ, thước đo chính là độ phủ danh mục): Gate MR (01/10, contract v1) 473 + 27 → sau 5.1: 475 + 32 → 5.2c: 965 + 121 → 5.2d: 1112 + 121 → 5.3: 1504 + 186 (1516 + 196 sau điều chỉnh D-11) → 5.4: 1707 + 211 → 5.5: 1871 + 238 → 5.6: 1871 + 240 → 13/10: 1874 + 240.

### 2.1. Gói 5.1 — Nền móng kiểm thử

- **Làm gì:** chuyển integration test sang Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy); fixture một lượt chạy, mỗi test một database nhân bản từ template; CI tách unit và integration.
- **Vì sao:** minh chứng phải chạy trên hạ tầng thật, tái lập được trên CI (D-04); EF InMemory bị gỡ khỏi integration test.
- **Sửa lỗi (phát hiện khi phân tích, sửa ở 5.1):** V-1 (compose truyền sai tên khóa Redis nên container chưa từng dùng Redis) và V-8 (mất kết nối PostgreSQL bị ánh xạ thành 400, nay là 503 `PERSISTENCE_UNAVAILABLE`). Cả hai tìm ra khi đọc mã (spec Mục 2), không phải do hạ tầng thật làm lộ; FI-08 qua Toxiproxy chỉ xác nhận chuỗi exception thật mà bản sửa V-8 xử lý (ghi chú gói 5.1 trong BACKLOG).
- **Minh chứng:** [plan 5.1](../spec/PHASE5_PLAN_5.1_TEST_FOUNDATION.md), ghi chú gói 5.1 trong BACKLOG.

### 2.2. Gói 5.2a — SDK v2 và strategies v2

- **Làm gì:** kiểu domain v2 (`Reservation` nhiều item, khách vãng lai, lịch sử, `DateTimeOffset` + múi giờ chi nhánh), DTO wire, JSON schema v2, `DefaultDecisionStrategy` 2.0.0 và `MockAlternativeDecisionStrategy` 2.0.0.
- **Vì sao:** strategy chỉ trả `StrategyDecision`; runtime lo định danh, thời gian, quy đổi tiền, approval window (D-03). SDK chỉ giữ bất biến `endTime > startTime`, giới hạn thời lượng item thành chính sách runtime cấu hình được (P-01, hiện thực ở 5.2b) để PT chạy được mà không sửa SDK; P-02 chốt `channel` = `ONLINE` | `WALK_IN`.
- **Minh chứng:** [plan 5.2a](../spec/PHASE5_PLAN_5.2a_SDK_STRATEGIES_V2.md); điểm dừng người dùng review bề mặt SDK (D-06).

### 2.3. Gói 5.2b — Lõi runtime v2

- **Làm gì:** `ApprovalWindowPolicy`, `DepositCalculator`, `RequestHasher` v2, cache `StrategyDecision`, port audit, `DecisionService` v2, phân loại lỗi.
- **Vì sao:** cache response v1 sai khi response có `approvalExpiresAt` (V-3); map lỗi v1 sai (V-4): `InvalidOperationException` → 400, lỗi PostgreSQL → 500 thay vì 503, mọi `OperationCanceledException` → 504, message của exception 4xx trả thẳng cho client (ca "mất PostgreSQL → 400" là V-8, sửa ở 5.1).
- **Minh chứng:** [plan 5.2b](../spec/PHASE5_PLAN_5.2b_RUNTIME_CORE_V2.md); UT-06, UT-10…UT-13.

### 2.4. Gói 5.2c — Chuyển hẳn sang v2

- **Làm gì:** migration `20261002101457_DecisionPersistenceV2` (xóa bảng v1, tạo 3 bảng + 6 trigger), `EfDecisionAuditStore`, mở `POST`/`GET /api/v2/decisions`, gỡ toàn bộ v1 (gỡ cả MediatR, BenchmarkRunner theo D-02, D-09), `openapi-v2.yaml` sinh từ mã, baseline public API nháp v2.
- **Vì sao:** thay thế tại chỗ vì v1 chưa có consumer (D-01); migration nối tiếp để lịch sử trên Neon khớp (D-05); nhận diện tranh chấp idempotency theo `SqlState` + tên constraint (V-5).
- **Minh chứng:** [plan 5.2c](../spec/PHASE5_PLAN_5.2c_CUTOVER_TO_V2.md); IT-01…05, IT-15, CT-01, CT-02, FI-08.

### 2.5. Gói 5.2d — Clean Architecture và SOLID

- **Làm gì:** tách `TADSS.Runtime.Api` thành `TADSS.Application`, `TADSS.Infrastructure`, `TADSS.Runtime.Api`; port tách theo consumer; bỏ hardcode vào Options có validator; bộ test kiến trúc UT-14.
- **Vì sao:** ranh giới tầng do compiler cưỡng chế, không chỉ dựa vào quy ước (P-03); sửa phụ thuộc ngược (port nằm trong hạ tầng, exception nghiệp vụ nằm ở tầng HTTP).
- **Ràng buộc:** không đổi hành vi, wire, schema, mã lỗi, ID migration, bề mặt SDK.
- **Minh chứng:** [plan 5.2d](../spec/PHASE5_PLAN_5.2d_CLEAN_ARCHITECTURE.md), ADR-013 trong [ARCHITECTURE_DECISION_RECORDS](architecture/ARCHITECTURE_DECISION_RECORDS.md).

### 2.6. Gói 5.3 — Quản trị và bảo mật

- **Làm gì:** override (APPROVE kèm mức cọc / REJECT, `X-Actor-Id`), outcome, hàng chờ duyệt theo chi nhánh; `X-Api-Key`; gói SEC-1…SEC-7; expose qua ngrok (NG-1…NG-5, `verify-expose.sh`).
- **Vì sao:** TADSS phải đủ an toàn trước khi expose (P-04); tranh chấp hai Manager giải bằng `UNIQUE(decision_id)`.
- **Thay đổi contract:** thêm hai mã lỗi `RATE_LIMITED` (429), `PAYLOAD_TOO_LARGE` (413) khi contract còn là bản nháp (người dùng duyệt ở P-04).
- **Minh chứng:** [`PHASE5_REPORT_5.3.md`](../spec/PHASE5_REPORT_5.3.md); IT-06…11, SEC-1…7.

### 2.7. Gói 5.4 — Runtime thích ứng

- **Làm gì:** định tuyến strategy theo request (`Tadss:Routing`, nạp lại nóng), shadow mode, pipeline Polly riêng từng strategy (sửa V-2), `IsolatedStrategyInvoker` cho strategy không hợp tác (FI-05), breaker Redis, decorator tiêm lỗi chỉ ở môi trường `Testing`.
- **Vì sao:** một breaker chung làm strategy này lỗi kéo strategy khác fast-fail; cần chứng minh idempotency và cache đúng khi nhiều instance (SCALE-1).
- **Minh chứng:** [`PHASE5_REPORT_5.4.md`](../spec/PHASE5_REPORT_5.4.md); IT-12 (shadow lỗi không ảnh hưởng `PRIMARY`), SCALE-1 (50 request song song hai host → đúng 1 bản ghi).

### 2.8. Gói 5.5 — Observability và chế độ đánh giá

- **Làm gì:** OpenTelemetry (trace, metric, log) tới `grafana/otel-lgtm`; meter `TADSS.Runtime` 13 instrument; dashboard 29 panel hiển thị (21 timeseries, 6 stat, 1 logs, 1 table) trong 7 nhóm; ma trận FI-01…FI-10 đủ; stack đánh giá cô lập + evaluator; k6 LT-01/02/03/05, SCALE-2; Schemathesis; Stryker; bộ sinh `report.html`.
- **Vì sao:** hội đồng cần một lệnh tái lập toàn bộ minh chứng trên hạ tầng không chạm Neon (D-12); dashboard bắt buộc (P-04).
- **Phát hiện thật:** SCALE-1 bắt một lỗi đua khi dựng pipeline đồng thời (sửa bằng `Lazy`); Schemathesis bắt 404 thân rỗng khi `decisionId` không phải GUID (sửa thành problem+json); LT-03 lộ ca "cam kết không chắc chắn" khi cắt PostgreSQL (mục 7.1).
- **Minh chứng:** [`PHASE5_REPORT_5.5.md`](../spec/PHASE5_REPORT_5.5.md).

### 2.9. Gói 5.6 — Minh chứng dữ liệu

- **Làm gì:** replay booking SmashCourt (RP-01/02, truy vấn chỉ đọc, ẩn danh HMAC), adapter Hotel Booking Demand (PT-01/02, instance riêng `eval-tadss-portability`, chỉ đổi cấu hình), bước (e) "dữ liệu thực tế" (Q-01), hướng dẫn chạy từng bước.
- **Vì sao:** không pilot nên chất lượng quyết định đánh giá bằng dữ liệu có hình dạng thực tế; PT chứng minh độc lập domain (SC7).
- **Kết quả:** PT bản nhanh 10.000/10.000 request, 0 lỗi contract, SC7 `PASS`; RP chờ CSV của người dùng (U-3). D-14 (10/10) gộp vận hành và đánh giá vào một lệnh `up`.
- **Minh chứng:** [`PHASE5_REPORT_5.6.md`](../spec/PHASE5_REPORT_5.6.md), [`EVALUATION_RUN_GUIDE.md`](guides/EVALUATION_RUN_GUIDE.md).

### 2.10. Gói 5.7 — Đóng băng M5

- **Làm gì (đã xong):** M-01 (bỏ log ERR giả cho 404), M-09 (k6 báo lượt bỏ theo pha, LT-02 400 VU), DOC-1…DOC-4 tự động (`tools/docs/check_docs.py`, bảng [`CONFIGURATION_REFERENCE.md`](guides/CONFIGURATION_REFERENCE.md)), đóng băng `2.0.0` (M-07), đặc tả API BE ↔ FE (T5.22, M-08), E-STAGING (T5.23), danh mục BE/FE/E2E và [đặc tả E2E](research/E2E_TEST_SPECIFICATION.md) (T5.27).
- **Quyết định:** R-01…R-07 (spec Mục 4.7): đóng băng trước lượt đầy đủ (R-03), lượt chính thức có RP (R-04), API duyệt định danh bằng `bookingId` (R-05), Manager nhập cọc dạng % (R-06), E-STAGING trên nhánh Neon `staging` (R-07).
- **Còn chờ:** lượt đầy đủ (U-3, U-4), M-10 (lưu Evidence Pack), báo cáo Gate M5.
- **Minh chứng:** [plan 5.7](../spec/PHASE5_PLAN_5.7_M5_FREEZE.md).

### 2.11. Gói phụ 5.7b — Kinh tế mô phỏng chính sách cọc và shadow trong đánh giá

- **Làm gì (đã xong phần mã và tài liệu, 15/10/2026):**
  - **PT-04:** `eval-tadss-portability` chạy shadow `MockAlternative` cạnh `Default` (chỉ đổi cấu hình compose, không sửa `src/`); evaluator chụp counter Prometheus trước khi gửi, xả hàng đợi shadow sau khi gửi, đối soát tổng hợp (DB + hiệu counter) ra nhãn `RECONCILED`/`UNKNOWN`/`FAILED`, rồi ghép cặp `PRIMARY`/`SHADOW` theo `correlation_id` (bốn trạng thái `PAIRED`, `PRIMARY_FALLBACK`, `SHADOW_FALLBACK`, `UNPAIRED`; chỉ `PAIRED` được dùng để so hai strategy).
  - **PT-03:** mô phỏng trên Hotel Booking Demand theo chính sách hủy thật của SmashCourt (ảnh chụp `tools/evidence/config/smashcourt_cancel_policies.json`: 24 giờ hoàn 100 %, 2 giờ hoàn 50 %, 0 giờ hoàn 0 %). Sự kiện giữ cọc lấy từ `reservation_status` (không phải `is_canceled`); hai biến thể hoàn tiền báo ngang hàng vì gốc tính hoàn tiền khi chỉ trả cọc chưa chốt (Q-6); ngày hủy chỉ có ngày nên báo hai cận; `NEED_APPROVAL` báo cả quy ước 100 % lẫn 0 %; rủi ro hoàn vượt số cọc nếu giữ nguyên mã BE (B-1) đo riêng, không vào xếp hạng; bootstrap ghép cặp (hiệu tính trong từng mẫu); câu xếp hạng không nêu chính sách thắng khi kết luận phụ thuộc giả định hoặc khoảng tin cậy chứa 0.
  - Báo cáo `report.html` có mục PT-03/PT-04 và biểu đồ đánh đổi; D5 thêm 7 cột; `raw/d5/policy_economics.json` ghi mọi con số kèm giả định.
- **Vì sao:** Đồ án 2 chứng minh runtime bền, kiểm toán được, tích hợp được, nhưng chưa có số liệu cho câu hỏi chính sách cọc nào cân bằng tốt hơn; shadow mode có từ gói 5.4 nhưng chưa được trình diễn trong lượt đánh giá. Đây là **minh họa phương pháp đo trên dữ liệu proxy**, không phải tuyên bố strategy hiện tại là mô hình dự báo rủi ro tốt (việc của khóa luận).
- **Kết quả:** lượt bản nhanh `EV-20261015-EVAL-quick-r2` qua cổng quyết định (tỷ lệ `PAIRED` ≥ 99 % là ngưỡng vận hành, không phải ngưỡng thống kê): `PAIRED` 10.000/10.000, 0 `UNPAIRED`/`SHADOW_FALLBACK`, nhãn `RECONCILED` (counter `tadss_shadow_executions_total{instance="tadss-pt"}` khớp số bản `SHADOW`, phần dư 0), SC1…SC8 không đổi kết luận; bước (e) 92 s. Cùng đợt sửa kiểm mã SC7: commit merge chỉ bị xét file do chính nó sửa (`git diff-tree --cc`), không còn báo oan merge PR. Số liệu chính thức lấy từ lượt đầy đủ.
- **Giới hạn đã nêu:** giờ đến 14:00 `Europe/Lisbon` là giả định; chính sách hủy giữ ổn định trong Đồ án 2 và khóa luận; `AS_IS_BE` chỉ tính với `NEED_APPROVAL = 1`; tên metric Prometheus của shadow chỉ xác nhận trên lượt bản nhanh (tên sai thì nhãn `UNKNOWN`, không mất pack).
- **Minh chứng:** [plan 5.7b](../spec/PHASE5_PLAN_5.7b_TASKS.md), [methodology §4.10](research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md), [DATASET_SCHEMA D5](research/DATASET_SCHEMA_SPECIFICATION.md).

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
| Vận hành | `ngrok`                                         | `127.0.0.1:4040` (giao diện theo dõi) | `smashcourt-network`            | Expose TADSS; chỉ cần `NGROK_AUTHTOKEN`, URL đổi mỗi lần khởi động lại                                                  |
| Vận hành | `grafana`                                       | `127.0.0.1:3001`, OTLP `4317`/`4318`  | `smashcourt-network`            | `grafana/otel-lgtm` (Prometheus, Tempo, Loki), dashboard nạp sẵn                                                        |
| Đánh giá | `eval-postgres`, `eval-redis`, `eval-toxiproxy` | không                                 | mạng mặc định riêng             | Hạ tầng dùng một lần; Toxiproxy đứng trước PostgreSQL/Redis                                                             |
| Đánh giá | `eval-tadss-1`, `eval-tadss-2`, `eval-proxy`    | không                                 | mạng mặc định riêng             | Hai TADSS `Testing` sau nginx (SCALE-2); instance 1 có tiêm lỗi                                                         |
| Đánh giá | `eval-tadss-portability`                        | không                                 | mạng mặc định riêng             | Instance riêng cho PT (database `tadss_portability`; compose đặt `MaxItemMinutes=100800`, `HighValueThreshold=750` EUR) |
| Đánh giá | `eval-grafana`                                  | `127.0.0.1:3002`                      | mạng mặc định riêng             | Grafana của stack đánh giá, xem lại sau lượt chạy                                                                       |
| Đánh giá | `evaluator`                                     | không                                 | mạng mặc định riêng             | `restart: "no"`, chạy một lượt a…f rồi dừng                                                                             |

Service vận hành không `depends_on` service đánh giá. Evaluator mount `/var/run/docker.sock` (cho Testcontainers), nên chỉ chạy trên máy của chính mình.

### 4.3. Dữ liệu: Neon, Redis Cloud và stack cô lập

- Dev và vận hành dùng Neon (chỉ schema `tadss`) và Redis Cloud; migration lên Neon chỉ chạy khi người dùng cho phép (D-05); `DecisionPersistenceV2` đã áp 10/10/2026.
- `appsettings.Development.json` (chuỗi kết nối, mã băm API key) được copy vào image local như SmashCourt-BE, nên image **không bao giờ** được push (D-11).
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

Môi trường tích hợp FE ↔ BE ↔ TADSS trên máy nhóm bằng Docker Compose, dữ liệu trên **nhánh Neon riêng `staging`** (R-07), VNPay sandbox; chỉ bật `docker compose up -d --build tadss-runtime grafana` ở TADSS. Dựng ở W13 (T7.2); dùng cho E2E, LT-04, FI-11…14. Chi tiết: [MASTER_EXECUTION_PLAN, mục E-STAGING](planning/MASTER_EXECUTION_PLAN.md#môi-trường-tích-hợp-e-staging).

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
| RP-01…02                 | Replay booking SmashCourt, so sánh với cọc 100 % tĩnh (mô tả)                                                                                                              | E-EVAL bước (e)                            | 5 (chờ CSV)  |
| PT-01…02                 | Hotel Booking Demand ≥ 10.000 đơn qua contract v2, không sửa mã                                                                                                            | E-EVAL bước (e)                            | 5            |
| PT-03…04                 | Mô phỏng kinh tế chính sách cọc theo chính sách hủy SmashCourt; shadow `MockAlternative` cạnh `Default` kèm đối soát và ghép cặp (kết quả mô tả, không phải KPI đạt/trượt) | E-EVAL bước (e), (f)                       | 5 (5.7b)     |
| BE-UT-01…07, BE-IT-01…20 | SmashCourt-BE: ánh xạ, `TadssClient`, luồng cọc, endpoint duyệt, job, SignalR                                                                                              | E-TC (BE)                                  | 6            |
| FE-01…10                 | `tsc`/lint/build, luồng giao diện trên mock MSW                                                                                                                            | FE                                         | 6            |
| E2E-01…13                | Playwright qua FE → BE → TADSS ([đặc tả từng bước](research/E2E_TEST_SPECIFICATION.md))                                                                                    | E-STAGING                                  | 7–8          |

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

Số liệu bản nhanh chỉ để diễn tập; số liệu chính thức lấy từ lượt đầy đủ (methodology Mục 4.6).

### 5.3. Sáu bước của evaluator

| Bước     | Làm gì                                                                                                                                                       |            `quick-r2` (đo thật) |
| :------- | :----------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------: |
| (a)      | Build, unit + integration gộp theo `CatalogId`, `verify-expose.sh`, `verify-observability.sh`, `verify-export.sh`, `check_docs.py`, ba bộ pytest, OB-2       |                           693 s |
| (b)      | k6 LT-01, LT-02 (bản đầy đủ thêm LT-05 soak và LT-01 × 3)                                                                                                    |                           400 s |
| (c)      | Sự cố dưới tải LT-03 (FI-02/03/07/08 qua decorator và Toxiproxy) + SCALE-2 + snapshot dashboard                                                              |                           511 s |
| (d)      | Schemathesis CT-03, coverage gộp; Stryker MT-01…03 chỉ ở bản đầy đủ                                                                                          |                            48 s |
| (e)      | Dữ liệu thực tế: RP (cần CSV, không có thì `skipped`), PT (từ 5.7b kèm shadow và xả hàng đợi; bước (f) dựng PT-03/PT-04, bootstrap ước ~5 phút ở bản đầy đủ) | 92 s |
| (f)      | Evidence Pack: D1, SC1…SC8, EV-1…EV-5, `report.html`, `manifest.json`                                                                                        |                            61 s |
| **Tổng** |                                                                                                                                                              |           **1805 s (~30 phút)** |

Bản đầy đủ: **ước tính ≈ 2,8 giờ** cộng khoảng 8–18 phút cho (e), chưa đo trọn vẹn; riêng bước (d) bản đầy đủ đã đo 25,9 phút (Schemathesis 190 s, Stryker ≈ 21 phút). Mã thoát: `0` đạt mọi tiêu chí, `1` có tiêu chí không đạt, `2` lỗi hạ tầng hoặc không chạy hết.

### 5.4. Evidence Pack

- Thư mục `evidence/<run_id>/` (`run_id` = `EV-{yyyyMMdd}-EVAL-{quick|full}-r{n}`; thư mục làm việc, không commit): `report.html` tự chứa, `manifest.json` (SHA-256 từng file, commit, cấu hình, máy, KPI), `raw/` (trx theo `CatalogId`, k6, `chaos_log`, metric, D1, D2, D4, D5), `grafana/dashboard-snapshot.json` (không lưu ảnh), `config/`.
- Kiểm toàn vẹn EV-1…EV-5: SHA-256 khớp manifest; `decision_id` của D2 có trong D1; số response thành công = số bản `PRIMARY`; không có dữ liệu cá nhân; manifest đủ commit, cấu hình, thông số máy.
- Dựng lại `report.html` từ một pack có sẵn (không gửi lại request), theo [guide mục 7](guides/EVALUATION_RUN_GUIDE.md): `docker compose run --rm --no-deps --entrypoint python evaluator -m evidence.report /evidence/<run_id>`, hoặc ngoài Docker `cd tools && python -m evidence.report ../evidence/<run_id>` (module nằm trong `tools/`, chạy từ gốc repo không được).
- Pack chính thức của lượt đầy đủ sẽ chốt vào `docs/research/evidence-packs/{run_id}/` (M-10; file > 20 MB đính kèm GitHub Release). Từ 5.7b: D5, `pt_summary.json`, `policy_economics.json` và mục PT của `report.html` **chỉ vào pack chính thức sau khi xác minh giấy phép và điều kiện phân phối của đúng nguồn dataset đang ghim** (quyết định từng artifact); trước đó chúng chỉ sinh trong `evidence/<run_id>/` (gitignore).
- Định nghĩa: [DATASET_SCHEMA_SPECIFICATION](research/DATASET_SCHEMA_SPECIFICATION.md) Mục 2, 5.

---

## 6. Lệnh quan trọng

Chạy trong thư mục `TADSS-Runtime/`. Các lệnh dưới đây chạy được trong Git Bash; lệnh `docker compose` chạy được cả trong PowerShell. Quy trình đầy đủ và cách xử lý sự cố: [EVALUATION_RUN_GUIDE](guides/EVALUATION_RUN_GUIDE.md).

### 6.1. Build, test và kiểm tài liệu

```bash
dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror           # bắt buộc 0 warning
dotnet test tests/TADSS.UnitTests -c Release --no-build -nologo         # không cần Docker; kỳ vọng 1874 đạt
dotnet test tests/TADSS.IntegrationTests -c Release --no-build -nologo  # Testcontainers, cần Docker Desktop; 240 đạt ở `quick-r2` (15/10/2026)
python -m venv .venv && source .venv/Scripts/activate                   # một lần (Python 3.11+)
pip install -r tools/evaluation/requirements.txt -r tools/evidence/requirements.txt
python -m pytest -q tools/evaluation                                    # ba bộ pytest chạy RIÊNG từng bộ, không gộp một lệnh
python -m pytest -q tools/evidence
python -m pytest -q tools/docs
python tools/docs/check_docs.py                                         # DOC-1…3, thoát 0 là đạt (DOC-4 là unit test .NET)
```

### 6.2. Docker: vận hành, đánh giá, xem kết quả, dọn

```bash
docker network create smashcourt-network          # một lần: mạng dùng chung với SmashCourt-BE/FE
docker compose up -d --build                       # app + ngrok + Grafana và một lượt đánh giá bản nhanh (~28 phút)
EVAL_MODE=full docker compose up -d --build        # bản đầy đủ (Git Bash: biến chỉ có hiệu lực cho đúng lệnh này)
docker compose logs -f evaluator                   # theo dõi; im lặng tới cuối, dòng cuối in đường dẫn report.html và mã thoát
docker compose logs --tail 60 evaluator            # bảng tóm tắt của lượt vừa chạy
docker compose ps -a                               # trạng thái mọi container, kể cả evaluator đã dừng
explorer.exe evidence                              # mở thư mục kết quả, bấm đúp evidence/<run_id>/report.html
bash tools/evaluation/run.sh quick --steps e,f     # chạy lại vài bước, không khởi động lại app (muốn có report.html phải kèm f)
bash tools/evaluation/run.sh full                  # cả lượt đầy đủ chỉ bằng stack đánh giá
bash tools/evaluation/run.sh --clean-only          # dọn riêng phần đánh giá (container, volume, image, build cache ~6 GB)
docker compose down                                # dừng tất cả, giữ image
```

```powershell
$env:EVAL_MODE="full"; docker compose up -d --build   # bản đầy đủ trong PowerShell; xong thì đóng cửa sổ (hoặc Remove-Item Env:EVAL_MODE), nếu không lần up sau lại chạy bản đầy đủ
```

Grafana vận hành `http://localhost:3001`, Grafana của stack đánh giá `http://127.0.0.1:3002`. Không dùng `docker system prune`: lệnh đó xóa cả mạng `smashcourt-network`.

### 6.3. Kiểm tra bằng Docker thật, API key, trước lượt đầy đủ

```bash
bash tools/expose/verify-expose.sh                 # NG-1/2/3/5 + SEC-6, project compose riêng, không chạm Neon (bước a đã chạy)
bash tools/observability/verify-observability.sh   # OB-2/OB-4: dashboard nạp, metric tới Prometheus (bước a đã chạy)
bash tools/evidence/sql/verify-export.sh           # truy vấn xuất SmashCourt trên database dùng một lần (bước a đã chạy)
python tools/security/hash_api_key.py              # băm khóa trong appsettings.Development.json (KeySha256 chỉ nhận mã băm), key gốc vào .env
python tools/security/hash_api_key.py --new        # sinh khóa mới cho client đầu tiên (--client <ClientId> để chọn client)
MSYS_NO_PATHCONV=1 docker run --rm --entrypoint k6 -v "$(pwd -W)/tests/load:/load" -w /load/scenarios tadss-evaluator:local inspect lt01.js   # threshold k6 theo pha, không gửi tải (plan 5.7 Task 2); kiểm cả lt02.js
```

```powershell
docker run --rm --entrypoint k6 -v "${PWD}/tests/load:/load" -w /load/scenarios tadss-evaluator:local inspect lt01.js   # cùng lệnh trong PowerShell; đổi lt01.js thành lt02.js để kiểm LT-02
```

Lệnh `k6 inspect` cần image `tadss-evaluator:local` đã có trên máy: image này được build khi chạy `docker compose up -d --build` hoặc `bash tools/evaluation/run.sh` (`run.sh --clean`/`--clean-only` xóa nó). Trong Git Bash phải giữ `MSYS_NO_PATHCONV=1` và `$(pwd -W)`: nếu không, Git Bash tự đổi các đường dẫn `/load…` của `-v`/`-w` thành đường dẫn Windows và lệnh hỏng.

Xuất CSV SmashCourt cho RP (U-3): theo [guide mục 8](guides/EVALUATION_RUN_GUIDE.md) (thông tin kết nối đặt trong biến môi trường của cửa sổ, không ghi vào file). Checklist trước lượt đầy đủ (guide mục 6): xuất CSV trước; tự commit mọi thay đổi để SC7 và manifest ghi đúng commit; Docker Desktop chạy, mạng đã có, tắt chế độ ngủ; đóng cửa sổ PowerShell sau khi xong.

### 6.4. Migration EF (chỉ đọc)

`dotnet ef` phải được cài dạng global tool (`dotnet tool install --global dotnet-ef`): tool manifest `.config/dotnet-tools.json` của repo chỉ có `dotnet-stryker` và `reportgenerator`, nên `dotnet tool restore` không cài `dotnet-ef`.

```bash
# Liệt kê migration không kết nối database, rồi kiểm model và snapshot còn khớp (chuỗi kết nối giả, không phải secret)
ASPNETCORE_ENVIRONMENT=Testing ConnectionStrings__DefaultConnection="Host=localhost;Database=x" dotnet ef migrations list --no-connect --project src/TADSS.Infrastructure --startup-project src/TADSS.Runtime.Api
ASPNETCORE_ENVIRONMENT=Testing ConnectionStrings__DefaultConnection="Host=localhost;Database=x" dotnet ef migrations has-pending-model-changes --project src/TADSS.Infrastructure --startup-project src/TADSS.Runtime.Api
```

Kỳ vọng: đúng 4 ID migration (3 của v1 giữ để lịch sử khớp Neon + `20261002101457_DecisionPersistenceV2`) và "No changes have been made to the model since the last migration.". `migrations add` và `database update` chỉ chạy khi người dùng yêu cầu.

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
|  6  | **Một lệnh tái lập:** `docker compose up -d --build` chạy test, tải, sự cố, contract và sinh Evidence Pack có SHA-256 từng file, commit và cấu hình máy. Mặc định là **bản nhanh** (không Stryker, không LT-05); bản đầy đủ (thêm mutation, LT-05 soak, LT-01 × 3) cần `EVAL_MODE=full` và **chưa chạy trọn vẹn lần nào**                                                                                                                                                        | D-14; EV-1…EV-5; Mục 5.3                                        |
|  7  | **Kiến trúc được khóa, không chỉ quy ước:** compiler (`ProjectReference`) + UT-14; contract khóa bằng drift test OpenAPI và baseline public API                                                                                                                                                                                                                                                                                                                                  | P-03, ADR-013; CT-02                                            |
|  8  | **Bộ test bắt được lỗi thật:** mutation ≥ 70 % cả ba phạm vi (số của lượt bước (d) bản đầy đủ `EV-20261008-EVAL-full-r4` ngày 08/10, trước khi đóng băng); Schemathesis tìm lỗi 404 sai contract; SCALE-1 tìm lỗi đua dựng pipeline                                                                                                                                                                                                                                              | Báo cáo 5.5 3k, mục 2                                           |
|  9  | **Độc lập domain:** cùng runtime xử lý 10.000 đơn khách sạn (EUR, `Europe/Lisbon`, lưu trú nhiều đêm) chỉ bằng cấu hình, 0 lỗi contract. 10.000 là mẫu của bản nhanh; bản đầy đủ (≈ 118 nghìn đơn) chưa chạy                                                                                                                                                                                                                                                                     | PT-01, SC7 `PASS` (`quick-r1`); P-01                            |
| 10  | **Trung thực về giới hạn:** số đo là số liệu phòng lab trên một máy, báo cáo kèm CPU/RAM; bản nhanh chỉ để diễn tập. Hiện **chưa có lượt đầy đủ chính thức** (U-4); RP chờ CSV SmashCourt (U-3); SC5/SC6 `NOT_APPLICABLE` cho tới khi đo trên E-STAGING (Phase 7–8); số mutation lấy từ lượt bước (d) ngày 08/10 trước đóng băng; PT 10.000 là mẫu bản nhanh (bản đầy đủ ≈ 118 nghìn chưa chạy)                                                                                  | Methodology Mục 8; Mục 1 (Gate M5), Mục 8 của tài liệu này      |

### 7.2. Câu hỏi khó dự kiến

| Câu hỏi                                                    | Trả lời ngắn (nguồn)                                                                                                                                                                                                                                                                                                                                                                                                         |
| :--------------------------------------------------------- | :--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Đã có lượt đánh giá đầy đủ chưa?                           | Chưa. Mới có lượt bản nhanh `EV-20261015-EVAL-quick-r2` (sau 5.7b, đạt mọi tiêu chí áp dụng được, chỉ để diễn tập) và lượt riêng bước (d) bản đầy đủ `EV-20261008-EVAL-full-r4` (mutation, Schemathesis) chạy trước khi đóng băng. Lượt đầy đủ chính thức (`EVAL_MODE=full`, ước tính ≈ 2,8 giờ + 8–18 phút cho bước (e)) chờ người dùng xuất CSV (U-3) và chạy (U-4); số liệu chính thức của Gate M5 sẽ lấy từ lượt đó (Mục 1, Mục 8) |
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

| Việc                                                                                                                                                                   | Chủ                            | Trạng thái           | Ghi chú                                                                                                                                                         |
| :--------------------------------------------------------------------------------------------------------------------------------------------------------------------- | :----------------------------- | :------------------- | :-------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| U-2: xác nhận giấy phép Hotel Booking Demand (CC BY 4.0)                                                                                                               | Người dùng (SV1)               | ⏳                   | Chưa xác nhận thì D5, `pt_summary.json`, `policy_economics.json` và mục PT của báo cáo không vào pack chính thức (M-10 đã thu hẹp ở 5.7b); không chặn việc chạy |
| Instance TADSS khởi động lạnh bị treo khoảng 14–56 s khi tải LT-01 đổ vào (cả `/health/live` không trả lời); mọi lượt đều có, thường nằm gọn trong 30 s warm-up, `quick-r1` 15/10 lan sang pha chính nên SC1 `FAIL` | SV1 | ⏳ trước lượt đầy đủ | Chưa rõ nguyên nhân gốc (nghi thread pool cạn khi lạnh, log console đồng bộ kèm câu SQL của EF Core ở mức Information); xác nhận bằng thí nghiệm rồi mới sửa |
| Q-6: gốc tính tiền hoàn khi khách chỉ trả cọc (hiện BE tính trên `FinalTotal`, B-1)                                                                                    | SV1                            | ⏳ Phase 6–7         | Chốt khi tích hợp TADSS, ghi vào `DEPOSIT_APPROVAL_API.md`; 5.7b chỉ đo rủi ro, không sửa BE                                                                    |
| B-2: nghi lệch +7 giờ trong `CalculateRefundPercentAsync` của BE                                                                                                       | SV1                            | ⏳ trace sau         | Người dùng báo vận hành đang đúng; chưa sửa, mô phỏng tính đúng múi giờ                                                                                         |
| U-3: xuất CSV SmashCourt vào `data/private/`                                                                                                                           | Người dùng (SV1)               | ⏳                   | Bắt buộc trước lượt đầy đủ (R-04)                                                                                                                               |
| U-4: commit, chạy lượt đầy đủ, gửi `logs --tail 60 evaluator` + `ps -a`                                                                                                | Người dùng (SV1)               | ⏳                   | T5.21                                                                                                                                                           |
| Task 10 plan 5.7: phân tích lượt đầy đủ, chốt Evidence Pack (M-10), báo cáo Gate M5                                                                                    | SV1                            | ⏳ sau U-4           | Có tiêu chí không đạt thì sửa gốc rồi chạy lại cả lượt đầy đủ                                                                                                   |
| NG-4: thử qua tunnel ngrok thật với Neon thật                                                                                                                          | Người dùng (SV1)               | ⏳                   | BACKLOG T5.30                                                                                                                                                   |
| T5.24 review contract v2; T5.29 review API BE ↔ FE                                                                                                                     | SV2                            | ⏳                   | Góp ý đòi đổi wire ⇒ INTEGRATION_SPEC Mục 6                                                                                                                     |
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
| Chạy đánh giá, đọc kết quả, sự cố                     | [EVALUATION_RUN_GUIDE](guides/EVALUATION_RUN_GUIDE.md)                                                                                                                                                                               |
| Khóa cấu hình `Tadss:*`                               | [CONFIGURATION_REFERENCE](guides/CONFIGURATION_REFERENCE.md)                                                                                                                                                                         |
| Lịch, gate, E-STAGING                                 | [MASTER_EXECUTION_PLAN](planning/MASTER_EXECUTION_PLAN.md)                                                                                                                                                                           |
| Trạng thái task T5.x                                  | [BACKLOG_SPRINTS](planning/BACKLOG_SPRINTS.md)                                                                                                                                                                                       |
| Lý do từng quyết định D-xx, P-xx, R-xx                | [PHASE5_DESIGN_SPEC](../spec/PHASE5_DESIGN_SPEC.md)                                                                                                                                                                                  |
| Hợp đồng BE ↔ FE (Phase 6)                            | [`SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md`](../../SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md) (link sang repo khác chỉ mở được khi xem trong workspace chứa cả `SmashCourt-BE/`; trên GitHub thì mở repo `SmashCourt-BE`) |
