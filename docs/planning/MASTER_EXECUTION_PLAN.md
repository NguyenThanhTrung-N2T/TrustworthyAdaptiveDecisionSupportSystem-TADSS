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
| **Decision Runtime Core API** | SV1 | SV2 | Web API, DI-based Strategy Resolution (expected $O(1)$), Routing Controller |
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
        F3 --> M1["Milestone 1: Foundation Ready (✅ COMPLETED)"]
    end

    subgraph P2 [Phase 2: Contract SDK]
        M1 --> SDK[Shared.DecisionContractSDK & Unit Tests]
        SDK --> M2["Milestone 2: Decision Contract Ready (✅ COMPLETED)"]
    end

    subgraph P3 [Phase 3: Runtime Core]
        M2 --> RT[Runtime API & DI Strategy Resolution]
        M2 --> ST[Default & Mock Strategies]
        RT --> M3["Milestone 3: Runtime Engine Độc lập (✅ COMPLETED)"]
        ST --> M3
    end

    subgraph P4 [Phase 4: Resilience, Benchmark & Dataset]
        M3 --> POL[Week 7: Polly v8, Sync Audit, L1/L2 Cache & F1-F7 Matrix]
        POL --> BMK[Week 8: 5-Layer Benchmark - 19 Scenarios / 76 Runs]
        BMK --> DSN[Week 9: In-Vivo Audit Harvester & 3 Normalized Datasets]
        DSN --> M4["Milestone 4: Engine Chịu Lỗi & Thực Nghiệm Chuẩn Hóa (✅ COMPLETED)"]
    end

    subgraph P5 [Phase 5: HITL & SmashCourt Integration]
        M4 --> OVR[Override API & Feature Flag]
        M4 --> DASH[Manager UI trong smashcourt-fe]
        OVR --> INT[SmashCourt Backend Integration]
        DASH --> INT
        INT --> M5[Milestone 5: End-to-End Integrated]
    end

    subgraph P6 [Phase 6: Testing & Evaluation]
        M5 --> TEST[Integration & Failure Injection Testing]
        TEST --> PERF[Performance & SLA Benchmark]
        PERF --> PORT[Coworking Portability Mock]
        PORT --> M6[Milestone 6: Technical Evaluation Complete]
    end

    subgraph P7 [Phase 7: Final Defense Prep]
        M6 --> DOC[Hoàn thiện Báo cáo, Video Demo & Slide]
        DOC --> MFinal[Final Milestone: Sẵn sàng Bảo vệ]
    end
```

---

## III. CHI TIẾT KẾ HOẠCH 16 TUẦN (WEEK-BY-WEEK SPRINT PLAN)

### 🟢 PHASE 1: FOUNDATION & ARCHITECTURE (Tuần 1 — Tuần 2)
*Mục tiêu:* Khóa hợp đồng dữ liệu, thiết lập hạ tầng DB Schema `tadss` và tạo khung Happy-path prototype.
* **Chiến lược Kiểm thử & Đảm bảo Chất lượng (Quality & Evidence Strategy):**
  * *Mục tiêu khử bỏ rủi ro:* Ngăn chặn sai lệch cấu trúc giải pháp, vi phạm phân tầng Clean Architecture và lỗi thiết kế DDL Schema PostgreSQL (sai kiểu dữ liệu, thiếu ràng buộc toàn vẹn).
  * *Phương pháp & Công cụ:* Kiểm thử tuân thủ kiến trúc (Architecture Compliance), kiểm định DDL Schema `schema_smashcourt.sql`, kiểm tra cơ chế di chuyển lược đồ tự động qua EF Core Migrations, biên dịch nghiêm ngặt với cờ `--warnaserror`.
  * *Hiện vật Bằng chứng Đầu ra (Evidence Artifacts):* Migration Model Snapshot `TadssDbContextModelSnapshot.cs`, Script DDL `schema_smashcourt.sql`, Build log 0 warnings / 0 errors.

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
*   🎯 **Milestone 1 (Cuối Tuần 2 — ✅ COMPLETED):** *Foundation Ready — Solution build thành công, Database Migration sẵn sàng, Contract v1 được phê duyệt.*

---

### 🟢 PHASE 2: DECISION CONTRACT SDK (Tuần 3 — Tuần 4)
*Mục tiêu:* Xây dựng thư viện dùng chung độc lập, cô lập nghiệp vụ và đóng băng Contract.
* **Chiến lược Kiểm thử & Đảm bảo Chất lượng (Quality & Evidence Strategy):**
  * *Mục tiêu khử bỏ rủi ro:* Thay đổi ngầm (breaking changes) làm vỡ hợp đồng giao tiếp giữa các bên; dữ liệu đặt sân không hợp lệ (sai ngày giờ, sai mức tiền cọc) lọt sâu vào runtime core.
  * *Phương pháp & Công cụ:* Kiểm thử đơn vị 25 bất biến nghiệp vụ miền (Domain Invariants) trong `ReservationTests` bằng `xUnit 2.x` + `FluentAssertions`; kiểm thử đóng băng bề mặt API công khai (Public API Surface) qua `PublicApiGenerator`; kiểm thử tương thích JSON Schema nghiêm ngặt (`additionalProperties: false`).
  * *Hiện vật Bằng chứng Đầu ra (Evidence Artifacts):* Hồ sơ đóng băng [`baseline_public_api.txt`](../contracts/baseline_public_api.txt) (32 kiểu dữ liệu), tài liệu [`CONTRACT_V1_FREEZE.md`](../contracts/CONTRACT_V1_FREEZE.md), báo cáo Unit Tests đạt 100% pass với độ bao phủ (Coverage) > 80%.

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
*   🎯 **Milestone 2 (Cuối Tuần 4 — ✅ COMPLETED):** *Decision Contract Ready — Thư viện dùng chung hoàn chỉnh và đóng gói an toàn.*

---

### 🟢 PHASE 3: DECISION RUNTIME API (Tuần 5 — Tuần 6)
*Mục tiêu:* Xây dựng lõi Web API, cơ chế phân giải chiến lược qua Dependency Injection (DI-based Strategy Resolution) và các chiến lược mẫu.
* **Chiến lược Kiểm thử & Đảm bảo Chất lượng (Quality & Evidence Strategy):**
  * *Mục tiêu khử bỏ rủi ro:* Định tuyến chiến lược sai lệch hoặc thất bại; khách hàng bấm đúp tạo giao dịch trùng (Race condition); xung đột đồng thời khi 2 quản lý cùng thao tác trên 1 đơn.
  * *Phương pháp & Công cụ:* Kiểm thử tích hợp component in-memory toàn bộ HTTP Pipeline qua `WebApplicationFactory<Program>`; kiểm thử bảo vệ 2 tầng Idempotency (Bộ nhớ đệm ứng dụng + Ràng buộc DB Unique Index `uq_decision_idempotency_key` bắt mã lỗi `SqlState 23505`); kiểm thử khóa đồng thời lạc quan Optimistic Concurrency Control (`Version` token 409 Conflict).
  * *Hiện vật Bằng chứng Đầu ra (Evidence Artifacts):* Bộ kiểm thử tích hợp `TADSS.IntegrationTests`, báo cáo test cases `IdempotencyIntegrationTests.cs`, `DecisionsControllerTests.cs`.

#### **Tuần 5: Runtime API Core & Strategy Resolution**
*   **Sinh viên 1:**
    *   Xây dựng `DecisionController` tiếp nhận `POST /api/v1/decisions`.
    *   Xây dựng `DecisionService` và `StrategyResolver`.
    *   Lập trình pipeline: `Validate Request ➔ Resolve Strategy (DI Lookup expected O(1)) ➔ Execute EvaluateAsync ➔ Return Recommendation`.
*   **Sinh viên 2:**
    *   Implement `DefaultDecisionStrategy` (**Conservative Baseline Business Policy**): Áp dụng chính sách nghiệp vụ cơ sở bảo thủ của nền tảng (tách biệt với cơ chế cứu cánh hạ tầng `FallbackDecisionStrategy`).
    *   Implement `MockAlternativeDecisionStrategy` (**Demonstration Policy**): Phân tích `ReservationSchedule` để áp dụng mức cọc 30% cho giờ thường (Approved) và cọc 50% cho giờ cao điểm (NeedApproval) nhằm kiểm chứng khả năng phân giải chiến lược động của hệ thống.
*   **Hai thành viên:**
    *   Viết Unit/Integration Tests cho các kịch bản: Default Selected, MockAlternative Selected, Unknown Strategy Exception, Invalid Request.

#### **Tuần 6: Hoàn thiện Runtime API Độc Lập**
*   **Sinh viên 1:**
    *   Hoàn thiện cơ chế DI Registration với Service Provider Lookup độ phức tạp thuật toán kỳ vọng expected $O(1)$ (DI-based Strategy Resolution).
    *   Xử lý Strategy Version Resolution, Global Error Handling Middleware, và sinh CorrelationId cho mỗi request.
*   **Sinh viên 2:**
    *   Chuẩn hóa cấu trúc `Recommendation`, `ReasonCode`, `Priority` trả về.
    *   Cập nhật Swagger UI với đầy đủ tài liệu schema và mock payloads.
*   **Hai thành viên (End-to-End Test):**
    *   Chạy test end-to-end kiểm chứng: `Request ➔ DI-based Strategy Resolution ➔ Execution ➔ DecisionResult`.
*   🎯 **Milestone 3 (Cuối Tuần 6 — ✅ COMPLETED):** *TADSS Decision Runtime Standalone — Nền tảng ra quyết định chạy độc lập hoàn chỉnh.*

---

### 🟢 PHASE 4: RESILIENCE, PERFORMANCE BENCHMARK & DATASET NORMALIZATION (Tuần 7 — Tuần 9)
*Mục tiêu:* Hoàn thiện chuỗi chịu lỗi Polly v8 cùng ma trận tiêm lỗi F1–F7, thực hiện đo đạc benchmark hiệu năng bộ nhớ đệm ($G_0..G_2$) & độ chịu lỗi ($R_0..R_3$), và chuẩn hóa tập dữ liệu nghiên cứu (`DatasetNormalizer`).
* **Chiến lược Kiểm thử & Đảm bảo Chất lượng (Quality & Evidence Strategy):**
  * *Mục tiêu khử bỏ rủi ro:* Chiến lược xử lý quá hạn làm sập toàn bộ ứng dụng; đứt kết nối bộ nhớ đệm Redis L2 làm nghẽn luồng xử lý; dữ liệu kiểm toán bị thất lạc hoặc sai lệch tính toàn vẹn toán học.
  * *Phương pháp & Công cụ:* Giả lập và tiêm lỗi có kiểm soát ma trận `F1`–`F7` qua Custom Decorator Pattern (`FaultInjectedStrategyDecorator`); khung đo đạc thực nghiệm 76 lượt chạy (`TADSS.BenchmarkRunner`) bằng Hardware Timer `Stopwatch` và tính toán phân vị độ trễ; xác thực tự động 8 bất biến nguồn gốc dữ liệu (`INV-P1` $\rightarrow$ `INV-P8`) bằng `DatasetProvenanceVerifier` và mã băm SHA-256.
  * *Hiện vật Bằng chứng Đầu ra (Evidence Artifacts):* Báo cáo thực nghiệm [`WEEK8_BENCHMARK_REPORT.md`](../research/benchmarks/WEEK8_BENCHMARK_REPORT.md), báo cáo chuẩn hóa [`WEEK9_DATASET_NORMALIZATION_REPORT.md`](../research/datasets/WEEK9_DATASET_NORMALIZATION_REPORT.md), 3 bộ dữ liệu nghiên cứu chuẩn hóa CSV/JSONL (23, 19, 31 cột), biên bản mật mã [`dataset_provenance_manifest.json`](../research/datasets/dataset_provenance_manifest.json).

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
        *   Tách biệt Request Identity (`RequestHash = SHA256(Canonical(DecisionRequest))`) và Strategy Identity trong Cache Key: `decision:hash:{requestHash}:{strategyName}:v{strategyVersion}` (kèm cấu hình baseline mặc định `TtlHours = 24` trên Redis 7.x/8.x L2 — hỗ trợ cả **Redis Cloud (`ap-southeast-1`)** lẫn Local/Docker — và giáng cấp tự động Redis L2 $\rightarrow$ `IMemoryCache` L1 khi vượt quá `RedisTimeoutMs = 150ms`).
    *   *Fault Injection Matrix (F1–F7):* Kiểm chứng tự động toàn bộ 7 kịch bản lỗi (`F1` Recoverable Timeout, `F2` Retry Exhaustion, `F3` Strategy Exception, `F4` Circuit Breaker Open, `F5` Redis Unavailable L2 $\rightarrow$ L1, `F6` Persistence Fail-Closed, `F7` Caller Cancellation).

#### **Tuần 8: Thực nghiệm Đo đạc Hiệu năng Cache, Độ chịu lỗi & Tải Hỗn hợp Thực tế ($G_0..G_2$, $R_0..R_3$, $C_0..C_3$, $D_0..D_2$, $E_0..E_4$ — 19 Kịch bản / 76 Lượt chạy)**
*   **Sinh viên 1 (Owner — Experimental Protocol, Resilience $R_0..R_3$, Strategy/DB $D_0..D_2$ & Recovery $E_0..E_4$):**
    *   Thiết lập **Controlled Experimental Protocol** (`TADSS.BenchmarkRunner` độc lập tại `tests/TADSS.BenchmarkRunner`, không xâm nhập mã nguồn production, hỗ trợ `--suite Core7` 28 runs và `--suite Full19` 76 runs) kèm ghi nhận Traceable Run Metadata (`run_id`, `scenario`, `concurrency`, `warmup_requests`, `cache_state`, `redis_state`, `database_state`, resilience parameters).
    *   Thực thi quy trình đo 3 pha (*Warm-up $\rightarrow$ Measurement $\rightarrow$ Statistical Aggregation*) trên ma trận tải đồng thời $VUs \in \{1, 10, 50, 100\}$ cho nhóm kịch bản chịu lỗi cốt lõi có cô lập trạng thái ($R_2 \neq R_3$): $R_0..R_3$, hiệu năng Strategy/DB ($D_0$ Heavy Multi-Rule Strategy, $D_1$ DB Audit Write Contention, $D_2$ Bimodal Jitter) và tải hỗn hợp/tự phục hồi ($E_0..E_4$, bao gồm $E_2$ Cascading Triple Stress, $E_3$ Intermittent Transient Faults và $E_4$ Half-Open $\rightarrow$ Closed Self-Healing).
    *   Đo lường thực nghiệm phân phối độ trễ ($P_{50}, P_{90}, P_{95}, P_{99}, \text{Max}$), thông lượng (RPS), tỷ lệ Fallback và chỉ số **Decision Continuity Rate (DCR)** thực tế để đánh giá đối chiếu với ngân sách cấu hình lý thuyết (`420ms` < `500ms` Outer Timeout) và tiêu chí chấp nhận SC1/SC2.
*   **Sinh viên 2 (Owner — Caching Benchmark $G_0..G_2$, Cache Pathologies $C_0..C_3$ & Concurrency Matrix):**
    *   Thực thi thí nghiệm trực giao đo đạc 3 điều kiện bộ nhớ đệm cơ sở ($G_0$ No Cache, $G_1$ Cache Miss Path, $G_2$ Pre-warmed Cache Hit Path) và 4 kịch bản bệnh lý bộ nhớ đệm thực tế ($C_0$ Partial 50% Hit, $C_1$ Slow WAN Cache RTT, $C_2$ Cache Thundering Herd Stampede, $C_3$ Bounded LRU Eviction Thrashing) tại các mức $VUs \in \{1, 10, 50, 100\}$.
    *   Đo lường thực nghiệm **Cache Miss Overhead** ($\text{Latency}(G_1) - \text{Latency}(G_0)$), **Cache Benefit** ($\text{Latency}(G_0) - \text{Latency}(G_2)$) và WAN Cache Overhead, xuất dữ liệu JSON/CSV/Markdown có khả năng truy vết đầy đủ phục vụ Chương 3 & Chương 4.

#### **Tuần 9: Truy xuất Telemetry Kiểm toán & Chuẩn hóa Tập dữ liệu Thực nghiệm (Dataset Normalizer)**
*   **Sinh viên 2 (Owner — Dataset Normalizer & In-Vivo Audit Harvester):**
    *   Đóng băng đặc tả [`DATASET_SCHEMA_SPECIFICATION_V1.md`](../research/DATASET_SCHEMA_SPECIFICATION_V1.md) và xây dựng phân hệ `Normalization/` trong `TADSS.BenchmarkRunner` (`AuditLedgerHarvester`, `DatasetNormalizerEngine`, `GovernanceLifecycleHarness`, `DatasetArtifactExporter`) thu hoạch trực tiếp dữ liệu từ bảng `tadss.decision_executions` (Immutable Machine Snapshot $\oplus$ Mutable Governance Metadata) khi host còn sống và kết nối song ánh 1:1 với `RawMeasurementRecord` qua khóa định danh logic `DecisionId` mà hoàn toàn không làm ô nhiễm schema production (Zero Production Schema Pollution).
    *   Trích xuất 3 tập dữ liệu chuẩn hóa định dạng kép CSV & JSONL (UTF-8 No-BOM): (1) *`resilience_evaluation_dataset`* (`23` cột, `7.600` dòng — RQ1), (2) *`latency_caching_dataset`* (`19` cột, `8.800` dòng — RQ2), và (3) *`governance_audit_dataset`* (`31` cột, `50` ca kiểm định chuyển trạng thái $t_0 \rightarrow t_1$ — RQ3).
*   **Sinh viên 1 (Supporting — Provenance Invariants Verification & Report):**
    *   Xây dựng bộ kiểm định tự động `DatasetProvenanceVerifier` kiểm chứng 100% PASS cho 8 bất biến nguồn gốc (`INV-P1` $\rightarrow$ `INV-P8`), xuất biên bản `dataset_provenance_manifest.json` và báo cáo [`WEEK9_DATASET_NORMALIZATION_REPORT.md`](../research/datasets/WEEK9_DATASET_NORMALIZATION_REPORT.md) phục vụ Chương 4 Báo cáo Đồ án 2.
*   🎯 **Milestone 4 (Cuối Tuần 9 — ✅ COMPLETED):** *Resilient, Auditable & Benchmarked Core Engine — Động cơ chịu lỗi, lưu vết đồng bộ, bộ nhớ đệm L1/L2, thực nghiệm 76 lượt chạy (19 kịch bản $\times$ 4 VUs) và 3 bộ dữ liệu nghiên cứu chuẩn hóa hoàn tất (510/510 automated tests pass đóng vai trò quality gate kiểm chứng chống hồi quy mã nguồn).*

---

### 🟢 PHASE 5: HUMAN-IN-THE-LOOP & TÍCH HỢP SMASHCOURT (Tuần 10 — Tuần 12 — Kế hoạch Triển khai)
*Mục tiêu:* Cung cấp quyền kiểm duyệt cho Manager, khóa nguyên tắc No Auto-Approve, tích hợp UI và kết nối đầu-cuối với luồng đặt sân SmashCourt.
* **Kế hoạch & Thiết kế Đánh giá Chất lượng Tích hợp (Quality & Evidence Strategy — IQ1 đến IQ5 cho Phase 5):**
  * *Mục tiêu khử bỏ rủi ro:* TADSS gặp sự cố làm chết luồng đặt sân SmashCourt; lễ tân duyệt đơn bị chậm làm nghẽn sân; khách hàng bị trừ tiền trùng lặp; dữ liệu không thể chẩn đoán khi gặp lỗi thực tế.
  * *Phương pháp & Công cụ:* Đánh giá chất lượng tích hợp 5 chiều (IQ1 Độ đúng đắn chức năng, IQ2 Độ trễ tích hợp đầu-cuối, IQ3 Cô lập sự cố & Zero Booking Corruption, IQ4 Khả năng chẩn đoán qua `X-Correlation-ID`, IQ5 Chất lượng trải nghiệm người dùng / UX); kiểm thử tự động Playwright E2E UI Test Runner thu thập DOM trace và Video thực nghiệm; kiểm thử đo độ trễ truy vấn Partial Index (`need_approval = true AND is_overridden = false`).
  * *Hiện vật Bằng chứng Đầu ra (Evidence Artifacts):* Bộ kiểm thử E2E tích hợp SmashCourt Backend $\rightarrow$ TADSS, Playwright Trace & Video Artifacts, đo lường kịch bản truy vấn pending-approvals với partial index (Design Target $< 10\text{ms}$ trên môi trường thử nghiệm), biên bản kiểm thử Feature Flag Reload $< 2\text{s}$.

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
| **1. Normal Decision** | Gửi request hợp lệ giờ thường | Trả về `Approved` (cọc 30%), latency p95 < 150ms trong môi trường test chuẩn (hoặc cộng độ trễ mạng nền WAN RTT ~35–60ms/hop khi chạy qua Cloud Managed `ap-southeast-1`), ghi log `AUTOMATED`. |
| **2. Peak Hour Decision** | Gửi request rơi vào khung giờ cao điểm | Trả về `NeedApproval` (cọc 50%), tạm dừng thanh toán VNPay. |
| **3. Strategy Timeout** | Giả lập Strategy xử lý chậm > 120ms | Inner Timeout ngắt attempt, Retry 2 lần, kích hoạt `FallbackDecisionStrategy` (`NEED_APPROVAL`). |
| **4. Strategy Exception** | Giả lập Strategy ném lỗi 500 | Retry 2 lần, kích hoạt `FallbackDecisionStrategy` (`NEED_APPROVAL`), ghi log `FALLBACK`. |
| **5. Cascading Failures** | Bắn liên tiếp 10 requests lỗi | Circuit Breaker mở mạch, chuyển thẳng sang Fallback trong < 10ms trong kịch bản benchmark. |
| **6. Cache Hit** | Gửi lại 2 request giống hệt nhau | Lần 2 trả về ngay mà không gọi lại Strategy (Lookup < 15ms trên Local/In-Memory hoặc ~40–70ms WAN RTT trên Redis Cloud `ap-southeast-1`), ghi log `CACHE_HIT`. |
| **7. Key Sorting Cache** | Đảo thứ tự các thuộc tính JSON trong request | Vẫn Hit Cache bình thường (chứng minh hàm chuẩn hóa representation hoạt động). |
| **8. Redis Unavailable** | Ngắt kết nối Redis Cloud / Tắt Service Redis | Tự động chuyển sang `IMemoryCache` L1, hệ thống không sập và không kích hoạt Strategy Fallback. |
| **9. Duplicate Override** | Gửi liên tiếp 2 lệnh Override cùng `IdempotencyKey` | Lệnh thứ 2 bị chặn, database không bị ghi đè trùng lặp. |
| **10. Concurrency Conflict** | 2 Manager cùng gửi Override với `version = 1` | 1 lệnh thành công (version lên 2), 1 lệnh báo lỗi Conflict 409. |
| **11. Emergency Valve OFF** | Đặt `EnableTadssDecision = false` | SmashCourt bỏ qua TADSS, tự động áp dụng cọc tĩnh 100%. |

#### **Tuần 14: Đo lường Chỉ số SLA & Proof of Portability**
*   **Đo lường Performance SLA (SC1 & SC2):**
    *   *Design Target:* Normal Latency $p95 < 150\text{ms}$ được kiểm chứng trong môi trường test chuẩn (local/in-network; khi chạy thực nghiệm trên hạ tầng Cloud Managed liên vùng VN $\leftrightarrow$ AWS `ap-southeast-1` Singapore gồm Neon PostgreSQL + Redis Cloud, số liệu báo cáo tách bạch rõ thời gian xử lý nội tại `execution_time_ms` và độ trễ mạng quốc tế WAN RTT ~35–60ms/hop).
    *   *Design Target:* Overall Failure SLA Budget $\le 500\text{ms}$ đối với chuỗi xử lý chiến lược (`ExecuteStrategyAsync`) khi xảy ra timeout/retry/fallback trong kịch bản benchmark.
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
*   Đóng gói mã nguồn sạch sẽ trên GitHub Repository kèm tài liệu `README.md` hướng dẫn cài đặt từ Cloud Managed (Neon PostgreSQL + Redis Cloud) hoặc Docker/Local.
*   🎯 **Final Milestone (Cuối Tuần 16):** *Ready for Defense — Sẵn sàng 100% bảo vệ xuất sắc Đồ án 2.*

---

## IV. HỆ THỐNG CỔNG KIỂM SOÁT CHẤT LƯỢNG & TIÊU CHÍ HOÀN THÀNH (QUALITY GATES & DEFINITION OF DONE)

Hệ thống TADSS áp dụng quy trình kiểm soát chất lượng qua **8 Cổng Kiểm Soát (Quality Gates)** kết hợp với tiêu chí hoàn thành (Definition of Done) nghiêm ngặt để đảm bảo mọi cấu phần bàn giao đều có minh chứng định lượng:

```text
[MÃ NGUỒN C# & SQL]
   │
   ▼
[GATE 1: BUILD GATE] ────────► 0 Errors, 0 Warnings với cờ --warnaserror
   │
   ▼
[GATE 2: UNIT & INVARIANT GATE] ──► 100% Invariant Tests PASS (25 Domain Invariants)
   │
   ▼
[GATE 3: CONTRACT GATE] ─────► 32 Public Types frozen (Milestone 2 Contract V1 Freeze); JSON Schema additionalProperties: false
   │
   ▼
[GATE 4: RESILIENCE GATE] ───► Ma trận F1-F7 PASS; Fallback luôn trả NEED_APPROVAL khi quá hạn
   │
   ▼
[GATE 5: PERFORMANCE GATE] ──► P99 < 500ms xuyên suốt 76 runs (19 kịch bản x 4 VUs, 5 tầng A-E); DCR = 100%
   │
   ▼
[GATE 6: AUDIT PROVENANCE GATE] ─► 100% thỏa mãn 8 bất biến toán học INV-P1 .. INV-P8
   │
   ▼
[GATE 7: INTEGRATION GATE] ──► SmashCourt E2E workflow hoàn tất; Zero Transaction Corruption
   │
   ▼
[GATE 8: OPERATIONAL GATE] ──► CorrelationId phủ kín 100%; Audit Ledger bất biến 100%
   │
   ▼
[SẴN SÀNG BẢO VỆ ĐỒ ÁN / TRIỂN KHAI THỰC TẾ]
```

### 4.1. Bảng Tiêu chí Chi tiết 8 Cổng Kiểm Soát Chất lượng (Quality Gates)

| Cổng Kiểm Soát | Phạm vi Áp dụng | Tiêu chí Vượt Cổng (Gate Criteria) | Hiện vật Minh chứng (Verifiable Evidence) |
| :--- | :--- | :--- | :--- |
| **Gate 1: Build Gate** | Toàn bộ Solution .NET 8 & Migrations | • 0 Compiler Errors, 0 Compiler Warnings với `TreatWarningsAsErrors = true`<br>• 0 model changes pending trong EF Core Migrations | • Output log của `dotnet build --warnaserror`<br>• [`TadssDbContextModelSnapshot.cs`](../../src/TADSS.Runtime.Api/Infrastructure/Data/Migrations/TadssDbContextModelSnapshot.cs) |
| **Gate 2: Unit & Invariant Gate** | Domain Model & Invariant Rules | • 100% Unit Tests PASS (483/483 tests đóng vai trò chốt chặn chống hồi quy)<br>• 25 Domain Invariants của thực thể `Reservation` được kiểm chứng toàn diện | • Test run report của `TADSS.UnitTests` (483 tests pass)<br>• [`ReservationTests.cs`](../../tests/TADSS.UnitTests/Domain/ReservationTests.cs) |
| **Gate 3: Contract Gate** | Shared.DecisionContractSDK & OpenAPI | • Bề mặt Public API Surface khớp 100% với baseline (32 public types tại thời điểm Milestone 2 Contract V1 Freeze)<br>• JSON Schema tuân thủ nghiêm ngặt (`additionalProperties: false`) | • [`baseline_public_api.txt`](../contracts/baseline_public_api.txt)<br>• [`CONTRACT_V1_FREEZE.md`](../contracts/CONTRACT_V1_FREEZE.md) |
| **Gate 4: Resilience Gate** | Polly v8 Pipeline & Fault Decorators | • 100% các kịch bản tiêm lỗi trong ma trận `F1`–`F7` vượt qua<br>• Fallback kích hoạt chính xác sang `NEED_APPROVAL` khi timeout/lỗi | • Test suite `TADSS.IntegrationTests` (27 tests pass)<br>• Test cases `F1`–`F7` trong `TADSS.UnitTests` |
| **Gate 5: Performance Gate** | Benchmark Runner (76 Lượt chạy) | • $P_{99} < 500\text{ms}$ xuyên suốt 76 lượt chạy thuộc 5 tầng A–E (kể cả kịch bản Retry Exhaustion $R_3$)<br>• Decision Continuity Rate đạt $\text{DCR} = 100\%$ | • [`WEEK8_BENCHMARK_REPORT.md`](../research/benchmarks/WEEK8_BENCHMARK_REPORT.md)<br>• `benchmark_summary.csv` |
| **Gate 6: Audit Provenance Gate** | Dataset Normalizer & In-Vivo Harvester | • 100% thỏa mãn 8 bất biến toán học nguồn gốc (`INV-P1` $\rightarrow$ `INV-P8`)<br>• Mã băm SHA-256 của 6 file dataset khớp bit-by-bit với manifest | • [`WEEK9_DATASET_NORMALIZATION_REPORT.md`](../research/datasets/WEEK9_DATASET_NORMALIZATION_REPORT.md)<br>• [`dataset_provenance_manifest.json`](../research/datasets/dataset_provenance_manifest.json) |
| **Gate 7: Integration Gate (Kế hoạch Phase 5)** | Tích hợp SmashCourt Backend & E2E | • Luồng đặt sân SmashCourt $\leftrightarrow$ TADSS hoàn tất thông suốt<br>• Khi TADSS offline hoặc bypass, không có giao dịch đặt sân nào bị hỏng (Zero Transaction Corruption) | • Bộ kiểm thử E2E tích hợp SmashCourt Backend<br>• Playwright Trace Viewer & Video Artifacts |
| **Gate 8: Operational Gate** | Khả năng Chẩn đoán & Quản trị HITL | • `X-Correlation-ID` phủ kín 100% từ request headers, logs đến database<br>• Bảng kiểm toán bảo toàn 100% tính bất biến (9 trường Machine Snapshot) | • Nhật ký Structured Logs Serilog<br>• `governance_audit_dataset.csv` (`INV-P6`, `INV-P7`) |

### 4.2. Bảng Kiểm định Hoàn thành Cấu phần (Definition of Done Checklist)

```text
[x] Polly Resilience Pipeline (COMPLETED — Phase 4 / Week 7-8):
    ├── Strategy timeout (InnerTimeout 120ms) được phát hiện chính xác
    ├── Retry 2 lần với đúng base delay (30ms)
    ├── Circuit Breaker mở mạch khi vượt ngưỡng tỷ lệ lỗi (50% trong 30s)
    ├── FallbackDecisionStrategy (NEED_APPROVAL) hoạt động với overhead < 10ms trong benchmark
    ├── Outer SLA Budget 500ms được cưỡng chế tuyệt đối
    └── 100% critical test scenarios (F1-F7) pass không còn bug Blocker/High

[x] Request-Hash Cache (COMPLETED — Phase 3-4 / Week 6-8):
    ├── Key normalization sắp xếp alphabet và lowercase đúng chuẩn (RFC 8785)
    ├── Giữ nguyên 100% semantic values của business timestamps (chỉ cắt bỏ sub-second ticks)
    ├── 2 JSON khác thứ tự thuộc tính sinh ra cùng mã SHA-256
    ├── Cache Hit bỏ qua thực thi Strategy và trả về kết quả nhanh (< 15ms Local/In-Memory)
    ├── Tự động fallback sang IMemoryCache khi Redis mất kết nối (F5)
    └── Cấu hình baseline TTL (TtlHours = 24) và Strategy Version isolation hoạt động chính xác

[x] Append-only Audit Trail & Dataset Normalization (COMPLETED — Phase 3-4 / Week 6-9):
    ├── Ghi nhận đầy đủ thông số thực thi vào tadss.decision_executions (20 cột)
    ├── Phân biệt rõ AUTOMATED, CACHE_HIT, FALLBACK
    ├── Ghi đồng bộ trực tiếp (Mandatory Synchronous Fail-Closed) trong PostgreSQL Transaction
    ├── Lưu vết ghi đè Override kèm lý do và định danh Manager (bảo toàn 9 trường Machine Snapshot)
    └── Trích xuất 3 bộ dữ liệu chuẩn hóa CSV/JSONL (23/19/31 cột) đạt 100% PASS INV-P1..INV-P8

[x] Human-in-the-loop & Concurrency Core (TADSS Runtime COMPLETED — SmashCourt UI/Integration ở Phase 5):
    ├── Chặn 100% các request gửi trùng lặp qua IdempotencyKey (uq_decision_idempotency_key)
    ├── Chặn ghi đè trùng lặp (IsOverridden guard) và xử lý tranh chấp đồng thời (Version token)
    └── Tuyệt đối không tự động duyệt đơn NeedApproval trong TADSS Core (No Auto-Approve)
```

### 4.3. Cách Trả Lời Chuẩn Mực Học Thuật khi Hội Đồng Hỏi: *"100% của cái gì?"*
> *"Thưa Thầy/Cô, con số 100% của chúng em không có nghĩa là phần mềm không còn lỗi tiềm ẩn hay đạt chất lượng tuyệt đối. Con số 100% ở đây mang ý nghĩa kỹ thuật phần mềm cụ thể và có thể kiểm chứng độc lập theo từng lớp bằng chứng:
> 1. *100% các Unit Test và Domain Invariants đã định nghĩa đều PASS (483/483 tests) — đóng vai trò chốt chặn tự động chống hồi quy (automated regression gate) cho các quy tắc nghiệp vụ đã cam kết.*
> 2. *100% các kịch bản tiêm lỗi trong ma trận F1-F7 kích hoạt đúng các tầng bảo vệ của Polly v8 (27/27 integration tests).*
> 3. *100% các mẫu đo đạc trong 76 lượt chạy đạt tỷ lệ duy trì quyết định (DCR = 100%), không có quyết định nào bị thất lạc.*
> 4. *100% các bản ghi kiểm toán thỏa mãn 8 bất biến toán học về tính toàn vẹn và bất biến nguồn gốc (INV-P1..INV-P8).*
> 5. *Toàn bộ các tiêu chí chấp nhận (Acceptance Criteria) đã khóa trong tài liệu đặc tả đều được đo lường và kiểm chứng bằng dữ liệu thực nghiệm, tách bạch rõ ràng giữa kết quả đã đo (Phase 1–4) và mục tiêu thiết kế khi tích hợp (Phase 5)."*


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
