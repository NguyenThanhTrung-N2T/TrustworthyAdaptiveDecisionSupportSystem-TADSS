# DANH SÁCH BẮC-LOG TRIỂN KHAI THEO PHASE (IMPLEMENTATION SPRINTS BACKLOG)

Tài liệu này phân rã toàn bộ dự án từ **Epics ➔ Features ➔ User Stories ➔ Tasks** cụ thể, phân chia trách nhiệm rõ ràng cho từng thành viên trong nhóm theo từng Phase phát triển (16 tuần cho Đồ án 2 và 12 tuần cho Khóa luận).

---

## PHẦN A: ĐỒ ÁN 2 — NỀN TẢNG RUNTIME ENGINE CHỊU LỖI (16 TUẦN)

### EPIC 1: DECISION RUNTIME PLATFORM (TADSS CORE)

---

### PHASE 1: FOUNDATION & ARCHITECTURE (Tuần 1 — Tuần 2)
*Mục tiêu: Thống nhất giao thức API Contract, thiết kế SQL Schema `tadss` và dựng khung dự án.*

*   **User Story 1: Đặc tả Contract & SQL Schema**
    *   *Task 1.1 (Sinh viên 2 - Owner):* Đặc tả chi tiết cấu trúc Request/Response Payload JSON Schema giữa SmashCourt và TADSS Core (`DecisionRequest`, `DecisionResult`, `DecisionRecommendation`, `Reservation`). Đóng gói tài liệu tích hợp API v1.
    *   *Task 1.2 (Sinh viên 1 - Owner):* Thiết kế sơ đồ quan hệ cơ sở dữ liệu cho schema `tadss` bao gồm duy nhất bảng nhật ký thực thi `decision_executions` (chứa toàn bộ telemetry và các trường phê duyệt override).
    *   *Task 1.3 (Sinh viên 1 - Owner):* Khởi tạo Solution .NET Core 8 trong Git Repository, thiết lập các thư mục dự án theo cấu trúc Clean Architecture chuẩn, kết nối EF Core PostgreSQL.
    *   *Task 1.4 (Cả hai):* Dựng luồng prototype tối giản `SmashCourt Request ➔ TADSS Web API ➔ Mock Static Result` (chưa cần Polly/Cache).
    *   *Deliverables (Mốc 1 - Cuối Tuần 2):* File đặc tả API JSON Schema; Script SQL khởi tạo schema `tadss`; Solution build thành công.

---

### PHASE 2: XÂY DỰNG DECISION CONTRACT SDK (Tuần 3 — Tuần 4)
*Mục tiêu: Xây dựng thư viện dùng chung `Shared.DecisionContractSDK`, đóng gói DLL độc lập và đóng băng Contract.*

*   **User Story 2: Lập trình SDK dùng chung**
    *   *Task 2.1 (Sinh viên 1 - Owner):* Khởi tạo dự án `Shared.DecisionContractSDK` độc lập (Class Library), cấu hình đóng gói DLL độc lập để đảm bảo tính di động sang tên miền khác.
    *   *Task 2.2 (Sinh viên 1 - Owner):* Hiện thực hóa thực thể `Reservation` (Aggregate Root) tích hợp sẵn các behavior kiểm tra tính hợp lệ (`EnsureValid`) và behavior nghiệp vụ (`CanBeCancelledWithoutPenalty`), cùng các Value Objects (`CustomerInfo`, `ResourceInfo`, `ReservationSchedule`, `PaymentPolicy`).
    *   *Task 2.3 (Sinh viên 2 - Owner):* Định nghĩa các Enums trạng thái (`DecisionStatus`, `BookingPriority`, `ReasonCode`), DTOs kết quả, và interface `IReservationDecisionStrategy` (`EvaluateAsync`).
    *   *Task 2.4 (Sinh viên 2 - Owner):* Viết bộ Unit Tests (xUnit + FluentAssertions) kiểm tra tính toàn vẹn của thực thể `Reservation`, DTO validation, JSON serialization (đạt Coverage > 80%).
    *   *Deliverables (Mốc 2 - Cuối Tuần 4):* DLL thư viện SDK biên dịch thành công; 100% Unit Tests PASS; Contract đóng băng (Frozen v1).

---

### PHASE 3: XÂY DỰNG DECISION RUNTIME API (Tuần 5 — Tuần 6)
*Mục tiêu: Xây dựng bộ não định tuyến quyết định và cài đặt các chiến lược mô phỏng.*

*   **User Story 3: Lập trình API & Strategy Loader**
    *   *Task 3.1 (Sinh viên 1 - Owner):* Khởi tạo dự án Web API `DecisionRuntime.TADSS`, endpoint `POST /api/v1/decisions`, xử lý validation và correlation ID.
    *   *Task 3.2 (Sinh viên 1 - Owner):* Lập trình lớp `StrategyResolver` tự động đăng ký và phân giải Strategy qua cơ chế Dependency Injection (DI-based Strategy Resolution) với độ phức tạp $O(1)$.
    *   *Task 3.3 (Sinh viên 2 - Owner):* Hiện thực hóa mã nguồn cho 2 chiến lược nghiệp vụ cụ thể:
        *   `DefaultDecisionStrategy`: Chính sách nghiệp vụ cơ sở bảo thủ của nền tảng (tách biệt với cơ chế cứu cánh hạ tầng `FallbackDecisionStrategy`).
        *   `MockAlternativeDecisionStrategy`: Tự động phân tích khung giờ cao điểm trong `ReservationSchedule` để nâng cọc lên 50%, giờ thường cọc 30%.
    *   *Task 3.4 (Sinh viên 1 & 2):* Hoàn thiện error handling middleware, correlation ID và Swagger UI với đầy đủ mock payloads.
    *   *Deliverables (Mốc 3 - Cuối Tuần 6):* Dự án Web API chạy local mượt mà; Tài liệu Swagger API hiển thị luồng phân giải Strategy thành công; Test end-to-end độc lập đạt 100%.

---

### PHASE 4: RESILIENCE, PERFORMANCE BENCHMARK & DATASET NORMALIZATION (Tuần 7 — Tuần 9)
*Mục tiêu: Triển khai chuỗi chịu lỗi Polly v8 cùng ma trận tiêm lỗi F1–F7, thực hiện đo đạc benchmark hiệu năng 5 tầng ($G_0..G_2$, $R_0..R_3$, $C_0..C_3$, $D_0..D_2$, $E_0..E_4$ — 19 kịch bản / 76 lượt chạy), và chuẩn hóa 3 tập dữ liệu nghiên cứu (`INV-P1..P8`).*

*   **User Story 4: Resilience, Benchmark & Dataset Normalization**
    *   *Task 4.1 (Sinh viên 1 & 2 - Tuần 7):*
        *   Cấu hình Polly v8 5-Layer Resilience Pipeline: Outer Timeout (`500ms` SLA) $\rightarrow$ Fallback sang `FallbackDecisionStrategy` (`Status = NEED_APPROVAL`, `FallbackUsed = true`, `ExecutionTimeMs = 0`) $\rightarrow$ Retry (tối đa 2 lần, base delay `30ms`) $\rightarrow$ Circuit Breaker (tỷ lệ lỗi $\ge 50\%$ trong cửa sổ `30s`, `BreakDuration = 15s`) $\rightarrow$ Inner Timeout (`120ms`/attempt).
        *   Triển khai Mandatory Synchronous Fail-Closed Append-only Audit Trail thông qua MediatR in-process events (`DecisionEvaluatedAuditNotification`) nằm trong PostgreSQL EF Core Transaction boundary để lưu nhật ký chi tiết (`execution_type` thuộc `'AUTOMATED' | 'CACHE_HIT' | 'FALLBACK'`) xuống bảng `tadss.decision_executions`.
        *   Tích hợp Redis 7.x / 8.x (Redis Cloud Managed `ap-southeast-1` hoặc Local) L2 + `IMemoryCache` L1 fallback (F5), chuẩn hóa JSON theo RFC 8785 (sắp xếp key alphabet, chuyển UUID về lowercase, chuẩn hóa format ISO 8601 UTC `yyyy-MM-ddTHH:mm:ssZ` giữ nguyên giá trị nghiệp vụ, xóa whitespace và null) tạo `RequestHash = SHA256(Canonical(DecisionRequest))` và Cache Key `decision:hash:{requestHash}:{strategyName}:v{strategyVersion}` với cấu hình baseline mặc định `TtlHours = 24`.
        *   Kiểm chứng tự động toàn bộ 7 kịch bản lỗi trong Fault Injection Matrix (`F1–F7`).
    *   *Task 4.2 (Sinh viên 1 & 2 - Tuần 8):* Thực thi đo đạc Performance Benchmark theo quy trình 3 pha (*Warm-up $\rightarrow$ Measurement $\rightarrow$ Statistical Aggregation*) tại các mức tải $VUs \in \{1, 10, 50, 100\}$ trên 19 kịch bản thực nghiệm (76 lượt chạy qua 5 tầng: Cache Baseline $G_0..G_2$, Core Resilience $R_0..R_3$, Cache Pathologies $C_0..C_3$, Strategy & DB Contention $D_0..D_2$, và Combined Stress & Self-Healing $E_0..E_4$).
    *   *Task 4.3 (Sinh viên 2 - Owner, Sinh viên 1 - Supporting, Tuần 9):* Đóng băng đặc tả `DATASET_SCHEMA_SPECIFICATION_V1.md` và xây dựng phân hệ `Normalization/` (`AuditLedgerHarvester`, `DatasetNormalizerEngine`, `GovernanceLifecycleHarness`, `DatasetProvenanceVerifier`, `DatasetArtifactExporter`) trích xuất 3 bộ dữ liệu chuẩn hóa CSV/JSONL (`resilience_evaluation_dataset` 23 cột, `latency_caching_dataset` 19 cột, `governance_audit_dataset` 31 cột) từ bảng `tadss.decision_executions` kết hợp song ánh 1:1 với `RawMeasurementRecord` qua khóa `DecisionId` kèm kiểm chứng 8 bất biến nguồn gốc (`INV-P1..P8`) phục vụ Chương 4 Báo cáo Đồ án 2.
    *   *Emergency Escape Hatch:* Nếu cuối Tuần 8 Day 5 hạ tầng Cache/Benchmark gặp nghẽn môi trường $\rightarrow$ Bật MVP Mode: Duy trì Outer Timeout + `FallbackDecisionStrategy`, dùng `IMemoryCache` L1, ghi audit đồng bộ.
    *   *Deliverables (Mốc 4 - Cuối Tuần 9):* Ma trận tiêm lỗi F1–F7 đạt 100% pass; Báo cáo số liệu Benchmark 76 lượt chạy ($P_{50}/P_{90}/P_{95}/P_{99}$, DCR); Đặc tả `DATASET_SCHEMA_SPECIFICATION_V1.md`, phân hệ `Normalization/`, 3 bộ dữ liệu thực nghiệm chuẩn hóa CSV/JSONL và `WEEK9_DATASET_NORMALIZATION_REPORT.md` (507/507 automated tests pass).

---

### PHASE 5: HUMAN-IN-THE-LOOP & TÍCH HỢP SMASHCOURT (Tuần 10 — Tuần 12)
*Mục tiêu: Cung cấp quyền kiểm duyệt tối cao cho Manager, khởi tạo báo cáo sớm và kết nối đầu-cuối với hệ thống SmashCourt (3 tuần).*

*   **User Story 5: Override & Tích hợp SmashCourt**
    *   *Task 5.1 (Sinh viên 1 - Owner, Tuần 10):* Hoàn thiện endpoint `POST /api/v1/decisions/{id}/override` tích hợp kiểm tra `IsOverridden` và Optimistic Concurrency (`Version` token) kèm ghi vết qua MediatR in-process event (`DecisionOverriddenAuditNotification`).
    *   *Task 5.2 (Sinh viên 1 - Owner, Tuần 10):* Cấu hình Feature Flag `EnableTadssDecision` bọc ngoài luồng đặt sân của SmashCourt sử dụng `IOptionsMonitor` để reload cấu hình appsettings.json động dưới 2 giây.
    *   *Task 5.3 (Cả hai, Tuần 10):* Viết phác thảo khung Báo cáo Đồ án 2 từ Tuần 10: Day 1-2 (Viết chung Chương 1: 10-12 trang), Day 3-4 (SV1 viết Chương 2.1-2.2: 8-10 trang), Day 5-6 (SV2 viết Chương 2.3-2.4: 8-10 trang) đạt 26-32 trang (>40% khối lượng).
    *   *Task 5.4 (Sinh viên 2 - Owner, Tuần 11):* Dựng giao diện Manager Dashboard hoàn chỉnh tích hợp trực tiếp vào thư mục `smashcourt-fe` (Next.js) hiện có. Hiển thị danh sách các booking ở trạng thái `PendingDecisionApproval` để Manager duyệt hoặc sửa đổi mức cọc kèm lý do. *(Quy tắc 2 ngày: Nếu sau Tuần 10 Day 2 chưa render được danh sách ➔ Chuyển ngay sang C# Razor Page / HTML Table).*
    *   *Task 5.5 (Sinh viên 1 - Owner, Tuần 12):* Sửa đổi API tạo đặt sân ở SmashCourt Backend để thực hiện gọi sang TADSS Core qua HTTP Client, nhận tỷ lệ cọc đề xuất để tính toán số tiền chuyển sang thanh toán VNPay. Đồng bộ giao diện frontend.
    *   *Deliverables (Mốc 5 - Cuối Tuần 12):* Giao diện UI phê duyệt cho Manager trên `smashcourt-fe`; Tích hợp thành công luồng đặt sân SmashCourt ↔ TADSS ↔ VNPay; Khung Báo cáo Đồ án 2 đạt > 40% khối lượng trang.

---

### PHASE 6: KIỂM THỬ TỔNG THỂ & ĐO LƯỜNG KPI (Tuần 13 — Tuần 14)
*Mục tiêu: Chứng minh các chỉ số KPI, đo lường khả năng phục hồi lỗi và kiểm chứng tính độc lập tên miền.*

*   **User Story 6: Testing & Technical Evaluation**
    *   *Task 6.1 (Cả hai, Tuần 13):* Thực hiện ma trận kiểm thử sự cố (Failure Injection Test Matrix: 11 kịch bản gồm Timeout, Exception, Circuit Breaker, Cache Hit/Miss, Key Sorting, Concurrency Conflict, Emergency Valve).
    *   *Task 6.2 (Cả hai, Tuần 14):* Thu thập số liệu chứng minh SLA phản hồi p95 < 150ms khi chạy bình thường, và SLA tổng thể <= 500ms khi xảy ra lỗi.
    *   *Task 6.3 (Cả hai, Tuần 14):* Chạy kịch bản giả lập (Mock data) gửi request đặt phòng họp Coworking Space để chứng minh TADSS xử lý hoàn hảo mà không cần đổi một dòng code lõi nào.
    *   *Deliverables (Mốc 6 - Cuối Tuần 14):* Source code hoàn chỉnh trên Github; Bộ Integration Test chạy tự động với 100% critical test scenarios đạt pass (không còn bug Blocker/High); Bảng số liệu đo lường KPI hoàn chỉnh.

---

### PHASE 7: ỔN ĐỊNH HÓA, BÁO CÁO & CHUẨN BỊ BẢO VỆ (Tuần 15 — Tuần 16)
*Mục tiêu: Đóng băng tính năng, hoàn thiện cuốn báo cáo 60 trang, quay video demo và diễn tập thuyết trình.*

*   **User Story 7: Finalization, Documentation & Defense Prep**
    *   *Task 7.1 (Cả hai, Tuần 15):* Áp dụng Feature Freeze. Hoàn thiện Chương 3 & 4 Báo cáo Đồ án 2, vẽ sơ đồ kiến trúc chuẩn (Class Diagram, Sequence Diagram, Database Diagram), soát lỗi chính tả và chuẩn hóa trích dẫn IEEE.
    *   *Task 7.2 (Cả hai, Tuần 16):* Quay Video Demo hoạt động tích hợp đầu cuối (SmashCourt ↔ TADSS ↔ VNPay ↔ Override Dashboard ➔ Failure Injection), soạn 20-25 slide thuyết trình, diễn tập bảo vệ thử trước hội đồng và đóng gói GitHub repository.
    *   *Deliverables (Final Milestone - Cuối Tuần 16):* File báo cáo đồ án bản mềm hoàn chỉnh (60 trang); Video demo thực tế hệ thống; Slide thuyết trình sẵn sàng bảo vệ.

---

## PHẦN B: `[OUT-OF-SCOPE ĐỒ ÁN 2]` KHÓA LUẬN TỐT NGHIỆP — CHIẾN LƯỢC QUYẾT ĐỊNH THÍCH ỨNG (TADS) (12 TUẦN)

> **⚠️ LƯU Ý PHẠM VI (SCOPE LOCK):** Toàn bộ Phần B (Phase 8 & Phase 9) thuộc phạm vi mở rộng của **Khóa luận tốt nghiệp** ở giai đoạn tiếp theo và được loại trừ hoàn toàn khỏi phạm vi triển khai của **Đồ án 2** theo mục 5.2 của [`PROPOSAL_DO_AN_2.md`](PROPOSAL_DO_AN_2.md).

### EPIC 2: ADAPTIVE DECISION STRATEGY (TADS RESEARCH — OUT-OF-SCOPE ĐỒ ÁN 2)

---

### PHASE 8: NGHIÊN CỨU THUẬT TOÁN & MODEL ML (Tuần 1 — Tuần 8)
*Mục tiêu: Triển khai giải thuật tính điểm uy tín, huấn luyện mô hình dự toán rủi ro và viết API giải thích.*

*   **User Story 8: Giải thuật thích ứng & Mô hình học máy**
    *   *Task 8.1:* Lập trình logic tính điểm uy tín động (Adaptive Trust Score) tích hợp hàm mũ suy hao thời gian ($e^{-\lambda \Delta t}$) áp dụng cho hành vi lịch sử của người dùng.
    *   *Task 8.2:* Xây dựng script giả lập dữ liệu lịch sử đặt sân (Synthetic Data Generator) đạt 20,000+ records. Huấn luyện mô hình phân loại CatBoost dự đoán xác suất bùng hẹn (No-show Probability) làm đầu vào cho Decision Engine.
    *   *Task 8.3:* Viết hàm serialize chuỗi suy luận quyết định thành định dạng JSON lưu vào trường `trace_payload`. Viết API giải thích quyết định độc lập `GET /decision/{id}/trace` và giao diện đồ thị cây Decision Trace UI.
    *   *Deliverables:* Lớp mã nguồn `AdaptiveTrustDecisionStrategy`; Tập dữ liệu Synthetic Dataset; API /trace và giao diện hiển thị vết quyết định.

---

### PHASE 9: GIẢ LẬP & ĐÁNH GIÁ THỰC NGHIỆM (Tuần 9 — Tuần 12)
*Mục tiêu: Đánh giá hiệu quả kinh doanh qua môi trường mô phỏng và hoàn thiện luận văn.*

*   **User Story 9: Thực nghiệm & Hoàn thiện luận văn**
    *   *Task 9.1:* Phát triển phần mềm giả lập Agent-based Simulator mô phỏng 1,000 khách hàng hoạt động theo 4 nhóm hành vi (Loyal, Regular, Risky, New) gửi tải lên hệ thống.
    *   *Task 9.2:* Chạy đối chứng thực nghiệm (Ablation Study) kiểm chứng giả thuyết H1, phân tích kết quả công bằng cơ hội (Equal Opportunity Fairness Metric) giữa các nhóm đối tượng khách hàng.
    *   *Task 9.3:* Tập hợp số liệu so sánh Doanh thu, tỷ lệ bùng lịch, viết tài liệu luận văn và chuẩn bị Slide bảo vệ trước Hội đồng tốt nghiệp.
    *   *Deliverables:* Mã nguồn bộ Simulator; Luận văn bản mềm; Slide thuyết trình.
