# KẾ HOẠCH THỰC THI CHI TIẾT ĐỒ ÁN 2 (MASTER EXECUTION PLAN v1.0 — FROZEN)
## NỀN TẢNG RUNTIME ENGINE RA QUYẾT ĐỊNH CHỊU LỖI (TADSS)

*   **Dự án:** TADSS (Trustworthy Adaptive Decision Support System)
*   **Hệ thống đối chứng (Case Study):** SmashCourt — Hệ thống đặt sân cầu lông trực tuyến
*   **Thời lượng:** 16 tuần (14 tuần Kỹ thuật thực thi + 2 tuần Ổn định, Báo cáo & Bảo vệ)
*   **Nhân sự:** 2 thành viên (Sinh viên 1 — Backend/Runtime Owner; Sinh viên 2 — Contract/Supporting/UI Owner)
*   **Trạng thái Kế hoạch:** **FROZEN v1.0** (Đã khóa cứng ranh giới và thông số kỹ thuật)

---

## I. MA TRẬN TRÁCH NHIỆM & CƠ CHẾ PHỐI HỢP (OWNERSHIP & REVIEWER MODEL)

Thay vì phân chia cô lập theo kiểu "code của ai người nấy làm", dự án áp dụng mô hình **Owner (Chịu trách nhiệm chính) + Reviewer (Kiểm tra chéo & Test)** để đảm bảo tính nhất quán 100% của hệ thống:

| Cấu phần (Module) | Owner (Chính) | Reviewer (Phụ/Review) | Deliverable cốt lõi |
|:---|:---:|:---:|:---|
| **Solution & DB Architecture** | SV1 | SV2 | Solution .NET 8, PostgreSQL Schema `tadss`, bảng `tadss.decision_executions` |
| **Decision Contract SDK** | SV2 | SV1 | DLL `Shared.DecisionContractSDK`, Rich Domain Model `Reservation`, Unit Tests |
| **Decision Runtime Core API** | SV1 | SV2 | Web API, DI-based Strategy Resolution $O(1)$, Routing Controller |
| **Executable Strategies** | SV2 | SV1 | `DefaultDecisionStrategy` (Baseline Business Policy), `MockAlternativeDecisionStrategy` (30%/50%) |
| **Polly v8 Resilience Pipeline** | SV1 | SV2 | Nested Pipeline (Outer 500ms, Retry 2, Inner 120ms, CB, `FallbackDecisionStrategy`) |
| **Append-only Audit Trail** | SV1 | SV2 | Mandatory Synchronous Audit qua MediatR in-process events trong EF Core DbContext Transaction boundary |
| **Request-Hash Cache** | SV2 | SV1 | Canonicalization (Representation-only), SHA256 Key, Redis L2 & IMemoryCache L1 fallback |
| **Override & Concurrency API** | SV1 | SV2 | `POST /decisions/{id}/override`, `IsOverridden` guard & Optimistic Concurrency (`Version`) |
| **Manager Dashboard UI** | SV2 | SV1 | Tích hợp trực tiếp vào `smashcourt-fe` (Next.js) hiện có |
| **SmashCourt Integration** | SV1 | SV2 | Sửa backend SmashCourt, Feature Flag `EnableTadssDecision` (<2s reload) |
| **Testing & Evaluation** | Cả hai | Cả hai | 11 kịch bản Failure Injection, Đo lường p95 < 150ms, Portability Mock |
| **Báo cáo & Slide bảo vệ** | Cả hai | Cả hai | Báo cáo Đồ án 2, Video Demo thực tế, Slide thuyết trình |

---

## II. ĐỒ THỊ PHỤ THUỘC THỰC TẾ (REALISTIC DEPENDENCY GRAPH)

```mermaid
flowchart TD
    subgraph P1 [Phase 1: Foundation]
        F1[Thiết kế DB Schema tadss] --> F3[Solution Skeleton]
        F2[API & Domain Contracts] --> F3
    end

    subgraph P2 [Phase 2: Contract SDK]
        F3 --> SDK[Shared.DecisionContractSDK & Unit Tests]
    end

    subgraph P3 [Phase 3: Runtime Core]
        SDK --> RT[Runtime API & DI Strategy Resolution]
        SDK --> ST[Default & Mock Strategies]
        RT --> M2[Milestone 2: Runtime Engine Độc lập]
        ST --> M2
    end

    subgraph P4 [Phase 4: Resilience, Benchmark & Dataset]
        M2 --> POL[Week 7: Polly v8, Sync Audit, L1/L2 Cache & F1-F7 Matrix]
        POL --> BMK[Week 8: Cache G0-G2 & Resilience R0-R3 Benchmark]
        BMK --> DSN[Week 9: Audit Telemetry & Dataset Normalizer]
        DSN --> M3[Milestone 3: Engine Chịu Lỗi & Thực Nghiệm Chuẩn Hóa]
    end

    subgraph P5 [Phase 5: HITL & SmashCourt Integration]
        M3 --> OVR[Override API & Feature Flag]
        M3 --> DASH[Manager UI trong smashcourt-fe]
        OVR --> INT[SmashCourt Backend Integration]
        DASH --> INT
        INT --> M4[Milestone 4: End-to-End Integrated]
    end

    subgraph P6 [Phase 6: Testing & Evaluation]
        M4 --> TEST[Integration & Failure Injection Testing]
        TEST --> PERF[Performance & SLA Benchmark]
        PERF --> PORT[Coworking Portability Mock]
        PORT --> M5[Milestone 5: Technical Evaluation Complete]
    end

    subgraph P7 [Phase 7: Final Defense Prep]
        M5 --> DOC[Hoàn thiện Báo cáo, Video Demo & Slide]
        DOC --> M6[Milestone Final: Sẵn sàng Bảo vệ]
    end
```

---

## III. CHI TIẾT KẾ HOẠCH 16 TUẦN (WEEK-BY-WEEK SPRINT PLAN)

### 🟢 PHASE 1: FOUNDATION & ARCHITECTURE (Tuần 1 — Tuần 2)
*Mục tiêu:* Khóa hợp đồng dữ liệu, thiết lập hạ tầng DB Schema `tadss` và tạo khung Happy-path prototype.

#### **Tuần 1: Khóa Yêu cầu & Thiết kế Hợp đồng (Contract Lock)**
*   **Sinh viên 1 (Backend Lead):**
    *   Khởi tạo Git Repository, cấu trúc Solution .NET Core 8 theo Clean Architecture.
    *   Cấu hình ASP.NET Core Web API `DecisionRuntime.TADSS`.
    *   Cấu hình Entity Framework Core 8 kết nối PostgreSQL (Neon Cloud).
    *   Cấu hình Serilog Structured Logging cơ bản.
*   **Sinh viên 2 (Contract Lead):**
    *   Đặc tả chi tiết JSON Schema cho `DecisionRequest`, `DecisionResult`, `DecisionRecommendation`.
    *   Đặc tả Domain Model `Reservation` và các Enums (`DecisionStatus`, `BookingPriority`, `ReasonCode`).
    *   Soạn thảo tài liệu API Contract v1.
*   **Hai thành viên (Gate Tuần 1):**
    *   Họp chốt cứng 5 interface/model cốt lõi: `Reservation`, `DecisionRequest`, `DecisionResult`, `DecisionRecommendation`, `IReservationDecisionStrategy`.
    *   *Điều kiện Gate:* Không được sang Tuần 2 nếu chưa thống nhất 100% Contract.

#### **Tuần 2: Database Schema + Skeleton + Happy-Path Prototype**
*   **Sinh viên 1:**
    *   Thiết kế Migration sinh Logical Schema `tadss`.
    *   Tạo bảng duy nhất `tadss.decision_executions` (chứa toàn bộ telemetry thực thi và các trường ghi vết override của Manager: `is_overridden`, `overridden_by`, `override_reason`, `idempotency_key`, `version`).
    *   Thiết lập EF Core DbContext và Data Access Repository cơ bản.
*   **Sinh viên 2:**
    *   Thiết lập cấu trúc thư viện `Shared.DecisionContractSDK`.
    *   Viết logic JSON Serialization, Data Annotations Validation.
    *   Cấu hình Swagger API Documentation với các ví dụ request mẫu.
*   **Hai thành viên (Prototype Happy-Path):**
    *   Dựng luồng chạy thử nghiệm tối giản: `SmashCourt Request ➔ TADSS Web API ➔ Mock Static Result ➔ Return Response` (chưa cần Polly/Cache).
*   🎯 **Milestone 1 (Cuối Tuần 2):** *Foundation Ready — Solution build thành công, Database Migration sẵn sàng, Contract v1 được phê duyệt.*

---

### 🟢 PHASE 2: DECISION CONTRACT SDK (Tuần 3 — Tuần 4)
*Mục tiêu:* Xây dựng thư viện dùng chung độc lập, cô lập nghiệp vụ và đóng băng Contract.

#### **Tuần 3: Lập trình Domain Model & SDK Unit Tests**
*   **Sinh viên 1:**
    *   Implement thực thể miền `Reservation` (Aggregate Root).
    *   Implement các Value Objects: `CustomerInfo`, `ResourceInfo`, `ReservationSchedule`, `PaymentPolicy`, `ReservationMetadata`.
    *   Cài đặt Invariant Validation Rules nội tại của thực thể (`EnsureValid`, `CanBeCancelledWithoutPenalty`).
*   **Sinh viên 2:**
    *   Implement Enums: `DecisionStatus` (Approved, NeedApproval, Rejected), `BookingPriority`, `ReasonCode`.
    *   Implement DTOs: `DecisionRequest`, `DecisionResult`, `DecisionRecommendation`.
    *   Định nghĩa interface `IReservationDecisionStrategy` (`EvaluateAsync`).
    *   Viết bộ Unit Tests (xUnit + FluentAssertions) kiểm tra: Valid/Invalid Reservation, Invalid Schedule, Enum Mapping, JSON round-trip serialization.

#### **Tuần 4: Ổn định hóa SDK (SDK Stabilization & Contract Freeze)**
*   **Sinh viên 1:**
    *   Refactor Domain Model, kiểm tra triệt tiêu mọi Circular Dependency.
    *   Đảm bảo `Shared.DecisionContractSDK` hoàn toàn độc lập, không phụ thuộc vào codebase của SmashCourt.
    *   Viết XML Documentation chi tiết cho toàn bộ public classes/methods.
*   **Sinh viên 2:**
    *   Viết bộ kiểm tra tương thích DTO Validation và Contract Compatibility Test.
    *   Kiểm chứng SDK được đóng gói thành DLL độc lập.
*   **Hai thành viên (Gate Tuần 4):**
    *   Thực hiện reference thử nghiệm SDK vào cả `DecisionRuntime.TADSS` và `SmashCourt-BE`.
    *   *Tiêu chí Gate:* SDK Build SUCCESS, 100% Unit Tests PASS, Code Coverage > 80%, Contract đóng băng (Frozen v1).
*   🎯 **Milestone 2 (Cuối Tuần 4):** *Decision Contract Ready — Thư viện dùng chung hoàn chỉnh và đóng gói an toàn.*

---

### 🟢 PHASE 3: DECISION RUNTIME API (Tuần 5 — Tuần 6)
*Mục tiêu:* Xây dựng lõi Web API, cơ chế phân giải chiến lược qua Dependency Injection (DI-based Strategy Resolution) và các chiến lược mẫu.

#### **Tuần 5: Runtime API Core & Strategy Resolution**
*   **Sinh viên 1:**
    *   Xây dựng `DecisionController` tiếp nhận `POST /api/v1/decisions`.
    *   Xây dựng `DecisionService` và `StrategyResolver`.
    *   Lập trình pipeline: `Validate Request ➔ Resolve Strategy (DI Lookup O(1)) ➔ Execute EvaluateAsync ➔ Return Recommendation`.
*   **Sinh viên 2:**
    *   Implement `DefaultDecisionStrategy` (**Conservative Baseline Business Policy**): Áp dụng chính sách nghiệp vụ cơ sở bảo thủ của nền tảng (tách biệt với cơ chế cứu cánh hạ tầng `FallbackDecisionStrategy`).
    *   Implement `MockAlternativeDecisionStrategy` (**Demonstration Policy**): Phân tích `ReservationSchedule` để áp dụng mức cọc 30% cho giờ thường (Approved) và cọc 50% cho giờ cao điểm (NeedApproval) nhằm kiểm chứng khả năng phân giải chiến lược động của hệ thống.
*   **Hai thành viên:**
    *   Viết Unit/Integration Tests cho các kịch bản: Default Selected, MockAlternative Selected, Unknown Strategy Exception, Invalid Request.

#### **Tuần 6: Hoàn thiện Runtime API Độc Lập**
*   **Sinh viên 1:**
    *   Hoàn thiện cơ chế DI Registration với Service Provider Lookup $O(1)$ (DI-based Strategy Resolution).
    *   Xử lý Strategy Version Resolution, Global Error Handling Middleware, và sinh CorrelationId cho mỗi request.
*   **Sinh viên 2:**
    *   Chuẩn hóa cấu trúc `Recommendation`, `ReasonCode`, `Priority` trả về.
    *   Cập nhật Swagger UI với đầy đủ tài liệu schema và mock payloads.
*   **Hai thành viên (End-to-End Test):**
    *   Chạy test end-to-end kiểm chứng: `Request ➔ DI-based Strategy Resolution ➔ Execution ➔ DecisionResult`.
*   🎯 **Milestone 3 (Cuối Tuần 6):** *TADSS Decision Runtime Standalone — Nền tảng ra quyết định chạy độc lập hoàn chỉnh.*

---

### 🟢 PHASE 4: RESILIENCE, PERFORMANCE BENCHMARK & DATASET NORMALIZATION (Tuần 7 — Tuần 9)
*Mục tiêu:* Hoàn thiện chuỗi chịu lỗi Polly v8 cùng ma trận tiêm lỗi F1–F7, thực hiện đo đạc benchmark hiệu năng bộ nhớ đệm ($G_0..G_2$) & độ chịu lỗi ($R_0..R_3$), và chuẩn hóa tập dữ liệu nghiên cứu (`DatasetNormalizer`).

#### **Tuần 7: Chuỗi Chịu lỗi Polly v8, Resilient L1/L2 Cache, Synchronous Audit & Fault Matrix (F1–F7)**
*   **Sinh viên 1 (Owner — Resilience & Synchronous Audit):**
    *   *Polly v8 5-Layer Pipeline:*
        *   Outer Timeout: `500ms` (SLA Budget toàn cục).
        *   Fallback Policy: Tự động chuyển về `FallbackDecisionStrategy` (`Status = NEED_APPROVAL`, `FallbackUsed = true`, `ExecutionTimeMs = 0`) với mục tiêu đo lường fallback overhead < 10ms trong kịch bản benchmark.
            > **[ADR-W7-01 — Separation of Business Policy vs Safety Fallback]:** `FallbackDecisionStrategy` tách biệt hoàn toàn với `DefaultDecisionStrategy` (chính sách nghiệp vụ thông thường). TADSS fallback chỉ chịu trách nhiệm duy trì decision-path continuity và chuyển sang trạng thái `NEED_APPROVAL`. Transactional/business fallback policy, nếu có, thuộc trách nhiệm của reservation platform và nằm ngoài decision authority của TADSS.
        *   Retry Policy: Tối đa 2 lần retry (tổng 3 attempts, base delay `30ms`).
        *   Circuit Breaker: Mở mạch khi tỷ lệ lỗi $\ge 50\%$ trong sampling window `30s` (min 10 requests, `BreakDuration = 15s`).
        *   Inner Timeout: `120ms` per attempt (ngân sách $3 \times 120\text{ms} + 2 \times 30\text{ms} = 420\text{ms} < 500\text{ms}$ SLA).
    *   *Mandatory Synchronous Audit Persistence qua MediatR In-Process Events (F6/F7):* Thiết lập hệ thống ghi vết quyết định đồng bộ (fail-closed) thông qua MediatR in-process events (`DecisionEvaluatedAuditNotification` / `DecisionOverriddenAuditNotification`) nằm trong transaction boundary của PostgreSQL (`TadssDbContext`), phân biệt rõ các loại thực thi (`'AUTOMATED' | 'CACHE_HIT' | 'FALLBACK'`, `execution_time_ms`, `fallback_used`, `failure_reason`, `cache_hit`); bảo đảm hủy sớm không tạo rác kiểm toán khi Caller Cancellation (F7).
*   **Sinh viên 2 (Owner — Request-Hash Caching & Fault Injection Suite):**
    *   *RFC 8785 Canonicalization & Request-Hash L1/L2 Cache (F5):*
        1. Sắp xếp keys trong JSON Payload theo thứ tự alphabet (Key Sorting).
        2. Lowercase chuỗi định danh UUID.
        3. Chuẩn hóa format biểu diễn timestamp sang ISO 8601 UTC string (`yyyy-MM-ddTHH:mm:ssZ`, chỉ cắt bỏ sub-second ticks hệ thống; **TUYỆT ĐỐI giữ nguyên giá trị nghiệp vụ giờ/phút, không làm tròn timestamp nghiệp vụ**).
        4. Xóa khoảng trắng thừa (whitespace) và chuẩn hóa null fields.
        *   Tách biệt Request Identity (`RequestHash = SHA256(Canonical(DecisionRequest))`) và Strategy Identity trong Cache Key: `decision:hash:{requestHash}:{strategyName}:v{strategyVersion}` (kèm cấu hình baseline mặc định `TtlHours = 24` và giáng cấp tự động Redis L2 $\rightarrow$ `IMemoryCache` L1 khi vượt quá `RedisTimeoutMs = 150ms`).
    *   *Fault Injection Matrix (F1–F7):* Kiểm chứng tự động toàn bộ 7 kịch bản lỗi (`F1` Recoverable Timeout, `F2` Retry Exhaustion, `F3` Strategy Exception, `F4` Circuit Breaker Open, `F5` Redis Unavailable L2 $\rightarrow$ L1, `F6` Persistence Fail-Closed, `F7` Caller Cancellation).

#### **Tuần 8: Thực nghiệm Đo đạc Hiệu năng Cache ($G_0..G_2$) & Độ chịu lỗi ($R_0..R_3$) (Performance Benchmark)**
*   **Sinh viên 1 (Owner — Resilience Benchmark $R_0..R_3$):**
    *   Thiết lập môi trường kiểm soát thực nghiệm (Benchmark Environment Lock) và thực thi quy trình đo 3 pha (*Warm-up $\rightarrow$ Measurement $\rightarrow$ Statistical Aggregation*) tại các mức tải đồng thời $VUs \in \{1, 10, 50, 100\}$.
    *   Đo đạc đối chứng 4 kịch bản chịu lỗi: $R_0$ (*Normal Strategy*), $R_1$ (*Recoverable Timeout*), $R_2$ (*Circuit Breaker OPEN Fast-Fail*), $R_3$ (*Retry Exhaustion $\rightarrow$ Fallback*).
    *   Tính toán chỉ số **Decision Continuity Rate (DCR)** và kiểm chứng phong bì độ trễ $P_{50}, P_{90}, P_{95}, P_{99} \le 500\text{ms}$.
*   **Sinh viên 2 (Owner — Caching Benchmark $G_0..G_2$):**
    *   Thực thi thí nghiệm trực giao đo đạc 3 điều kiện bộ nhớ đệm: $G_0$ (*Direct Execution — No Cache*), $G_1$ (*Cache Miss Path*), $G_2$ (*Cache Hit Path*) với cấu hình baseline `Default TTL = 24h`.
    *   Định lượng **Cache Miss Overhead** ($\text{Latency}(G_1) - \text{Latency}(G_0)$) và **Cache Benefit** ($\text{Latency}(G_0) - \text{Latency}(G_2)$).

#### **Tuần 9: Truy xuất Telemetry Kiểm toán & Chuẩn hóa Tập dữ liệu Nghiên cứu (Dataset Normalizer)**
*   **Sinh viên 2 (Owner — Dataset Normalizer):**
    *   Xây dựng công cụ `DatasetNormalizer` kết hợp dữ liệu từ bảng `tadss.decision_executions` (Immutable Machine Snapshot $\oplus$ Mutable Governance Metadata) với Metadata thực nghiệm (`experiment_id`, `scenario_id`, `concurrency_level`, `injected_fault_type`, `cache_condition`) mà không làm ô nhiễm schema production.
    *   Trích xuất 3 tập dữ liệu chuẩn hóa (CSV / JSONL): (1) *Resilience Evaluation Dataset*, (2) *Latency & Caching Dataset*, (3) *Ablation & Governance Dataset*.
*   **Sinh viên 1 (Supporting — Telemetry Verification & Analysis):**
    *   Kiểm증 tính toàn vẹn provenance của dữ liệu trích xuất và tổng hợp bảng số liệu thực nghiệm phục vụ Chương 4 Báo cáo / Khóa luận.
*   🎯 **Milestone 4 (Cuối Tuần 9):** *Resilient, Auditable & Benchmarked Core Engine — Động cơ chịu lỗi, lưu vết đồng bộ, bộ nhớ đệm L1/L2 và bộ dữ liệu benchmark chuẩn hóa hoàn tất.*

---

### 🟢 PHASE 5: HUMAN-IN-THE-LOOP & TÍCH HỢP SMASHCOURT (Tuần 10 — Tuần 12)
*Mục tiêu:* Cung cấp quyền kiểm duyệt cho Manager, khóa nguyên tắc No Auto-Approve, tích hợp UI và kết nối đầu-cuối với luồng đặt sân SmashCourt.

#### **Tuần 10: Override API & Feature Flag Bật/Tắt Nóng**
*   **Sinh viên 1:**
    *   Hoàn thiện & kiểm thử bảo vệ API `POST /api/v1/decisions/{id}/override` với cơ chế chặn ghi đè trùng lặp (`IsOverridden` guard) và EF Core Optimistic Concurrency Control (`Version` token).
    *   Cấu hình Feature Flag `EnableTadssDecision` bên trong SmashCourt Backend sử dụng `IOptionsMonitor` để reload nóng cấu hình dưới 2 giây.
    *   **Khóa nguyên tắc No Auto-Approve:** Đơn ở trạng thái `NeedApproval` nếu quá hạn duyệt sẽ chuyển sang `Expired` hoặc `Cancelled`, tuyệt đối không bao giờ tự động chuyển thành `Approved`.
*   **Sinh viên 2:**
    *   Dựng khung trang Manager Approval UI trực tiếp bên trong thư mục `smashcourt-fe` (Next.js) hiện có: Danh sách đơn `PendingDecisionApproval`, bảng chi tiết quyết định, form duyệt/sửa cọc kèm bắt buộc nhập lý do.
*   **Hai thành viên (Khởi tạo Báo cáo sớm - Task 5.3):**
    *   SV1 + SV2 pair-writing: Hoàn thành bản thảo **Chương 1 (Tổng quan & Bối cảnh: 10-12 trang)** và **Chương 2 (Thiết kế Kiến trúc: 16-20 trang)** đạt >40% tổng dung lượng cuốn báo cáo.

#### **Tuần 11: Hoàn thiện Manager Dashboard trên `smashcourt-fe`**
*   **Sinh viên 2:**
    *   Hoàn thiện toàn bộ giao diện duyệt cọc trên `smashcourt-fe`: Hiển thị rõ Strategy Name, Version, Execution Type, Decision Status, và nút hành động Duyệt/Ghi đè.
    *   *(Nếu gặp chậm tiến độ, giảm thiểu độ phức tạp styling/animation, giữ nguyên các tính năng duyệt cốt lõi, không đổi công nghệ sang Razor).*
    *   Kết nối API từ Next.js sang endpoint `POST /api/v1/decisions/{id}/override`.
*   **Sinh viên 1:**
    *   Kiểm tra bảo mật API, Validation, xử lý xung đột đồng thời khi 2 Manager cùng thao tác trên 1 đơn đặt chỗ (1 thành công, 1 nhận mã 409 Conflict).
*   **Hai thành viên (UAT Luồng Quản lý):**
    *   Thực hiện kiểm thử chấp nhận người dùng (UAT): `Booking ➔ NeedApproval (50% cọc) ➔ Block VNPay ➔ Manager Override về 30% ➔ Audit ghi vết ➔ Tiếp tục VNPay`.

#### **Tuần 12: Tích hợp Toàn diện Đầu-Cuối với SmashCourt Backend**
*   **Sinh viên 1:**
    *   Sửa đổi Controller đặt sân tại `SmashCourt-BE`: Gửi request sang TADSS Core qua HTTP Client, nhận kết quả Recommendation để tính tiền cọc và chuyển hướng VNPay.
*   **Sinh viên 2:**
    *   Đồng bộ giao diện khách hàng trên `smashcourt-fe`: Hiển thị thông báo cọc tương ứng theo kết quả trả về từ TADSS.
*   **Hai thành viên (Tích hợp & Fallback Test):**
    *   Test kịch bản bình thường: `SmashCourt ➔ TADSS (Mock 30%/50%) ➔ VNPay`.
    *   Test kịch bản sự cố: `TADSS Sập / Tắt Feature Flag ➔ SmashCourt tự động chuyển về cọc tĩnh 100% ➔ VNPay hoạt động thông suốt`.
*   🎯 **Milestone 5 (Cuối Tuần 12):** *End-to-End Integration Ready — Hệ thống thương mại SmashCourt tích hợp mượt mà với TADSS.*

---

### 🟢 PHASE 6: KIỂM THỬ TỔNG THỂ & ĐÁNH GIÁ THIẾT KẾ (Tuần 13 — Tuần 14)
*Mục tiêu:* Thực hiện toàn bộ ma trận kiểm thử sự cố, đo lường các chỉ số SLA kỹ thuật và chứng minh tính độc lập tên miền.

#### **Tuần 13: Thực hiện Ma trận Kiểm thử Sự cố (Failure Injection Test Matrix)**
*   **Hai thành viên cùng phối hợp chạy và đối soát bảng Test Matrix:**

| Kịch bản Kiểm thử | Thao tác / Giả lập sự cố | Tiêu chí Chấp nhận (Acceptance Criteria) |
|:---|:---|:---|
| **1. Normal Decision** | Gửi request hợp lệ giờ thường | Trả về `Approved` (cọc 30%), latency p95 < 150ms trong môi trường test chuẩn, ghi log `AUTOMATED`. |
| **2. Peak Hour Decision** | Gửi request rơi vào khung giờ cao điểm | Trả về `NeedApproval` (cọc 50%), tạm dừng thanh toán VNPay. |
| **3. Strategy Timeout** | Giả lập Strategy xử lý chậm > 120ms | Inner Timeout ngắt attempt, Retry 2 lần, kích hoạt `FallbackDecisionStrategy` (`NEED_APPROVAL`). |
| **4. Strategy Exception** | Giả lập Strategy ném lỗi 500 | Retry 2 lần, kích hoạt `FallbackDecisionStrategy` (`NEED_APPROVAL`), ghi log `FALLBACK`. |
| **5. Cascading Failures** | Bắn liên tiếp 10 requests lỗi | Circuit Breaker mở mạch, chuyển thẳng sang Fallback trong < 10ms trong kịch bản benchmark. |
| **6. Cache Hit** | Gửi lại 2 request giống hệt nhau | Lần 2 trả về ngay trong < 15ms, không gọi strategy, ghi log `CACHE_HIT`. |
| **7. Key Sorting Cache** | Đảo thứ tự các thuộc tính JSON trong request | Vẫn Hit Cache bình thường (chứng minh hàm chuẩn hóa representation hoạt động). |
| **8. Redis Unavailable** | Tắt Docker / Service Redis | Tự động chuyển sang `IMemoryCache`, hệ thống không sập. |
| **9. Duplicate Override** | Gửi liên tiếp 2 lệnh Override cùng `IdempotencyKey` | Lệnh thứ 2 bị chặn, database không bị ghi đè trùng lặp. |
| **10. Concurrency Conflict** | 2 Manager cùng gửi Override với `version = 1` | 1 lệnh thành công (version lên 2), 1 lệnh báo lỗi Conflict 409. |
| **11. Emergency Valve OFF** | Đặt `EnableTadssDecision = false` | SmashCourt bỏ qua TADSS, tự động áp dụng cọc tĩnh 100%. |

#### **Tuần 14: Đo lường Chỉ số SLA & Proof of Portability**
*   **Đo lường Performance SLA (SC1 & SC2):**
    *   *Design Target:* Normal Latency $p95 < 150\text{ms}$ được kiểm chứng trong môi trường test chuẩn (local dev/staging).
    *   *Design Target:* Overall Failure SLA Budget $\le 500\text{ms}$ khi xảy ra timeout/retry/fallback trong kịch bản benchmark.
*   **Chứng minh Tính Độc lập Tên miền (Portability Mock - SC6):**
    *   Xây dựng một mock client đại diện cho **Coworking Space (Đặt phòng họp)** gửi request về TADSS Core thông qua `Reservation` SDK.
    *   Chứng minh TADSS xử lý và ra quyết định chính xác mà **không cần sửa đổi bất kỳ dòng code nào trong TADSS Core**.
*   🎯 **Milestone 6 (Cuối Tuần 14):** *Technical Evaluation Complete — 100% critical test scenarios đạt pass (không còn lỗi Blocker/High severity) và đo lường đầy đủ số liệu KPI.*

---

### 🟢 PHASE 7: ỔN ĐỊNH HÓA, BÁO CÁO & CHUẨN BỊ BẢO VỆ (Tuần 15 — Tuần 16)
*Mục tiêu:* Bảo hiểm tiến độ, hoàn thiện cuốn báo cáo, quay video demo và diễn tập thuyết trình.

#### **Tuần 15: Hoàn thiện Tài liệu Báo cáo & Đóng gói Source Code**
*   **Tuyệt đối không thêm tính năng mới (Feature Freeze).**
*   Tập trung hoàn thiện cuốn Báo cáo Đồ án 2:
    *   Điền số liệu thực nghiệm, biểu đồ đo lường p95 và bảng so sánh latency vào Chương 3 & 4.
    *   Hoàn thiện các sơ đồ kiến trúc Class Diagram, Sequence Diagram, Database Schema Diagram.
    *   Soát lỗi chính tả, chuẩn hóa tài liệu tham khảo theo chuẩn IEEE.

#### **Tuần 16: Diễn tập Bảo vệ & Hoàn thiện Sản phẩm Bàn giao**
*   Quay Video Demo kịch bản hoạt động tích hợp đầu cuối (SmashCourt ↔ TADSS ↔ VNPay ↔ Override Dashboard ➔ Failure Injection).
*   Soạn Slide thuyết trình bảo vệ trước Hội đồng (20-25 slides).
*   Diễn tập thuyết trình thử và chuẩn bị các câu trả lời phản biện.
*   Đóng gói mã nguồn sạch sẽ trên GitHub Repository kèm tài liệu `README.md` hướng dẫn cài đặt từ Docker/Local.
*   🎯 **Final Milestone (Cuối Tuần 16):** *Ready for Defense — Sẵn sàng 100% bảo vệ xuất sắc Đồ án 2.*

---

## IV. TIÊU CHÍ HOÀN THÀNH TỪNG CẤU PHẦN (DEFINITION OF DONE - DOD)

Hệ thống chỉ được công nhận hoàn thành một cấu phần khi vượt qua bảng kiểm định kỹ thuật sau:

```
[ ] Polly Resilience Pipeline:
    ├── Strategy timeout (InnerTimeout 120ms) được phát hiện chính xác
    ├── Retry 2 lần với đúng base delay (30ms)
    ├── Circuit Breaker mở mạch khi vượt ngưỡng tỷ lệ lỗi
    ├── FallbackDecisionStrategy (NEED_APPROVAL) hoạt động với overhead < 10ms trong kịch bản benchmark
    ├── Outer SLA Budget 500ms được cưỡng chế tuyệt đối
    └── 100% critical test scenarios (F1-F7) pass không còn bug Blocker/High

[ ] Request-Hash Cache:
    ├── Key normalization sắp xếp alphabet và lowercase đúng chuẩn (RFC 8785)
    ├── Giữ nguyên 100% semantic values của business timestamps (chỉ cắt bỏ sub-second ticks)
    ├── 2 JSON khác thứ tự thuộc tính sinh ra cùng mã SHA-256
    ├── Cache Hit trả về kết quả trong < 15ms
    ├── Tự động fallback sang IMemoryCache khi Redis mất kết nối (F5)
    └── Cấu hình baseline TTL (TtlHours = 24) và Strategy Version isolation (decision:hash:{requestHash}:{strategyName}:v{strategyVersion}) hoạt động chính xác

[ ] Append-only Audit Trail:
    ├── Ghi nhận đầy đủ thông số thực thi vào tadss.decision_executions
    ├── Phân biệt rõ AUTOMATED, CACHE_HIT, FALLBACK
    ├── Ghi đồng bộ trực tiếp (Mandatory Synchronous Fail-Closed) trong PostgreSQL Transaction
    └── Lưu vết ghi đè Override kèm lý do và định danh Manager

[ ] Human-in-the-loop & Concurrency:
    ├── Chặn 100% các request gửi trùng lặp qua IdempotencyKey (uq_decision_idempotency_key)
    ├── Xử lý tranh chấp dữ liệu đồng thời qua ExpectedVersion (Optimistic Concurrency)
    └── Tuyệt đối không tự động duyệt khi quá hạn (No Auto-Approve -> Expired/Cancelled)
```

---

## V. QUY TẮC TIẾT CHẾ THUẬT NGỮ HỌC THUẬT (ACADEMIC TONE LOCK)

Để bản báo cáo và slide bảo vệ mang tính học thuật vững chắc, không bị hội đồng bắt bẻ những thuật ngữ thương mại quá đà:

| Tránh dùng (Nghe thương mại/thổi phồng) | Thay bằng (Chuẩn kỹ thuật & Học thuật) |
|:---|:---|
| *"Gia cố khả năng chịu lỗi Enterprise"* | *"Triển khai các cơ chế chịu lỗi cho Decision Runtime nhằm duy trì khả năng xử lý khi strategy gặp timeout hoặc lỗi"* |
| *"Hệ thống an toàn mức ngân hàng (Banking-grade)"* | *"Cơ chế kiểm soát an toàn giao dịch với Idempotency và Optimistic Concurrency Control"* |
| *"Kiến trúc Event Sourcing siêu việt"* | *"Hệ thống lưu vết nhật ký thực thi dạng Append-only Audit Trail"* |
| *"Dynamic Hot-loading / Nạp chiến lược động"* | *"Cơ chế định tuyến và phân giải chiến lược thông qua Dependency Injection (DI-based Strategy Resolution)"* |
| *"Hệ thống AI tự động hóa hoàn toàn"* | *"Hệ thống hỗ trợ ra quyết định kết hợp sự phê duyệt của con người (Human-in-the-loop Decision Support System)"* |

---

*Bản Kế hoạch Thực thi Chi tiết này đã được đóng băng chính thức (FROZEN v1.0) — Là kim chỉ nam duy nhất điều hành toàn bộ quá trình phát triển Đồ án 2.*
