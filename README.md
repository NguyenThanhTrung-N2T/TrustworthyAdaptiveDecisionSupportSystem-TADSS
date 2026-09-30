# TADSS Runtime

**Trustworthy Adaptive Decision Support System — Decision Runtime Engine (TADSS)**

TADSS Runtime là **nền tảng thực thi và kiểm soát quyết định chịu lỗi (Fault-Tolerant Decision Runtime Engine)** được thiết kế độc lập cho các hệ thống đặt chỗ dịch vụ theo thời gian thực (Service Reservation Systems).

> **📌 Phạm vi Đồ án 2 (Scope Lock):** Trong phạm vi **Đồ án 2 (16 tuần)**, dự án tập trung hoàn toàn vào **Kỹ thuật Phần mềm & Kiến trúc Hệ thống Phân tán Chịu lỗi (`TADSS Runtime Engine`)** — bao gồm hợp đồng dùng chung (`Shared.DecisionContractSDK`), định tuyến chiến lược $O(1)$ (`DefaultDecisionStrategy` & `MockAlternativeDecisionStrategy`), đường ống chịu lỗi 5 tầng Polly v8, bộ nhớ đệm chuẩn hóa RFC 8785 (`Request-Hash Cache` L1/L2), nhật ký kiểm toán đồng bộ bất biến (`Append-Only Audit Trail`), và cổng phê duyệt thủ công (`Human-in-the-loop Override API`). Khái niệm *"Trustworthy"* trong Đồ án 2 được định nghĩa theo **Operational Trustworthiness** (độ tin cậy vận hành hạ tầng). Các cấu phần giải thuật AI/ML (`Adaptive Trust Score`, `CatBoost`) thuộc phạm vi mở rộng của **Khóa luận tốt nghiệp** ở giai đoạn sau và nằm ngoài phạm vi Đồ án 2.

---

## 📚 Project Documentation Hub (Sơ Đồ Tài Liệu)

Toàn bộ tài liệu kỹ thuật, kiến trúc, hợp đồng API và kế hoạch thực nghiệm được quản lý tập trung theo 4 phân hệ trong thư mục [`docs/`](docs/):

```text
TADSS-Runtime/docs/
├── architecture/                                         <-- KIẾN TRÚC & QUYẾT ĐỊNH KỸ THUẬT
│   ├── ARCHITECTURE_DECISION_RECORDS.md                  (Tổng hợp các ADR cốt lõi)
│   ├── SOFTWARE_ARCHITECTURE_SPECIFICATION.md            (Bản vẽ thiết kế hệ thống C4 Model)
│   ├── DECISION_PERSISTENCE_INVARIANTS.md                (Invariants tầng lưu trữ DB & Audit Ledger)
│   ├── RESILIENCE_AND_FAULT_TAXONOMY.md                  (Ma trận lỗi F1-F7 & Resilience Invariants)
│   └── adr/                                              (Chi tiết các bản ghi kiến trúc ADR-001 -> ADR-005)
│       ├── ADR-001-why-strategy-pattern.md
│       ├── ADR-002-why-sdk-first-approach.md
│       ├── ADR-003-why-polly-resilience.md
│       ├── ADR-004-why-rich-domain-model.md
│       └── ADR-005-why-catboost-ml.md                    (Định hướng mở rộng cho Khóa luận - Out-of-Scope ĐA2)
│
├── contracts/                                            <-- HỢP ĐỒNG API & WIRE SCHEMAS (FROZEN v1.0.0)
│   ├── API_CONTRACT_V1.md                                (Đặc tả API Contract v1 Single Source of Truth)
│   ├── CONTRACT_V1_FREEZE.md                             (Tuyên ngôn đóng băng SDK v1.0.0 - Milestone 2)
│   ├── INTEGRATION_SPEC.md                               (Đặc tả tích hợp hệ thống SmashCourt ↔ TADSS)
│   ├── baseline_public_api.txt                           (Snapshot 32 Public Types của SDK)
│   ├── baseline_metrics.md                               (Báo cáo Quality Gates Milestone 2)
│   ├── openapi-v1.yaml                                   (OpenAPI 3.0 Specification)
│   └── schemas/                                          (7 JSON Schemas Draft 2020-12)
│
├── planning/                                             <-- QUẢN TRỊ ĐỀ ÁN & LỘ TRÌNH 16 TUẦN (ĐỒ ÁN 2)
│   ├── MASTER_EXECUTION_PLAN.md                          (Kế hoạch thực thi 16 tuần - Kim chỉ nam đã khóa)
│   ├── THESIS_DEVELOPMENT_PLAYBOOK.md                    (Cẩm nang điều hướng trung tâm & Khóa thuật ngữ)
│   ├── BACKLOG_SPRINTS.md                                (Phân rã User Stories & Tasks chi tiết cho SV1 & SV2)
│   ├── PROJECT_WBS_MAPPING.md                            (WBS Mapping, 7 Milestones, Quản trị rủi ro)
│   └── PROPOSAL_DO_AN_2.md                               (Đề cương đề xuất Đồ án 2 chính thức)
│
└── research/                                             <-- THỰC NGHIỆM HỆ THỐNG & CHUẨN HÓA DỮ LIỆU NGHIÊN CỨU
    ├── EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md  (Phương pháp thực nghiệm 5 tầng: G0..G2, R0..R3, C0..C3, D0..D2, E0..E4)
    ├── DATASET_SCHEMA_SPECIFICATION_V1.md                (Đặc tả đóng băng 3 Schema Dataset v1.0.0 & 8 Bất biến INV-P1..P8)
    ├── benchmarks/WEEK8_BENCHMARK_REPORT.md              (Báo cáo kết quả thực nghiệm 76 runs / 19 scenarios Tuần 8)
    ├── datasets/WEEK9_DATASET_NORMALIZATION_REPORT.md    (Báo cáo chuẩn hóa 3 Dataset CSV/JSONL & Provenance Manifest Tuần 9)
    ├── RESEARCH_PROBLEM_BACKLOG.md                       (Tài liệu khảo sát tiền đề lịch sử - Pre-scoping)
    └── RESEARCH_THESIS_DEFENSE.md                        (Định hướng mở rộng cho Khóa luận - Out-of-Scope ĐA2)
```

| Danh mục | Tài liệu cốt lõi | Mô tả & Mục đích sử dụng |
| :--- | :--- | :--- |
| **Đề cương Đồ án 2** | [PROPOSAL_DO_AN_2.md](docs/planning/PROPOSAL_DO_AN_2.md) | Tuyên ngôn phạm vi Đồ án 2, phân định ranh giới 3 lớp và danh sách cấu phần Core / Supporting / Out-of-Scope. |
| **Kiến trúc & Invariants** | [RESILIENCE_AND_FAULT_TAXONOMY.md](docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md) | Đặc tả 3 miền lỗi, Ma trận F1 ➔ F7, cơ chế Polly Circuit Breaker, và chính sách Mandatory Audit vs Degraded Cache. |
| **Kiến trúc DB** | [DECISION_PERSISTENCE_INVARIANTS.md](docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md) | Ràng buộc bất biến của bảng `tadss.decision_executions`, Snapshot máy bất biến $\oplus$ Metadata quản trị có thể thay đổi. |
| **Hợp đồng API** | [API_CONTRACT_V1.md](docs/contracts/API_CONTRACT_V1.md) | Single Source of Truth cho 4 endpoints (`/decisions`, `/decisions/{id}`, `/pending-approvals`, `/override`). |
| **Đóng băng SDK** | [CONTRACT_V1_FREEZE.md](docs/contracts/CONTRACT_V1_FREEZE.md) | Tuyên ngôn đóng băng wire format, 7 JSON schemas và 32 public types của `Shared.DecisionContractSDK`. |
| **Kế hoạch 16 tuần** | [MASTER_EXECUTION_PLAN.md](docs/planning/MASTER_EXECUTION_PLAN.md) | Kế hoạch thực thi chi tiết 16 tuần Đồ án 2, ma trận Owner/Reviewer, Definition of Done và Ma trận Failure Test. |
| **Cẩm nang Đề án** | [THESIS_DEVELOPMENT_PLAYBOOK.md](docs/planning/THESIS_DEVELOPMENT_PLAYBOOK.md) | Quy chuẩn thuật ngữ (`TADSS` trong Đồ án 2 vs `TADS` ở Khóa luận), phân tầng phạm vi và lối thoát khẩn cấp. |
| **Thực nghiệm Hiệu năng** | [EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md](docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) | Giao thức thực nghiệm 5 tầng (19 kịch bản $\times$ 4 mức VUs = 76 runs), chỉ số DCR, và đường ống chuẩn hóa dữ liệu kiểm toán. |
| **Đặc tả & Bộ Dữ liệu W9** | [DATASET_SCHEMA_SPECIFICATION_V1.md](docs/research/DATASET_SCHEMA_SPECIFICATION_V1.md) | Đặc tả đóng băng 3 bộ dữ liệu chuẩn hóa (`23 / 19 / 31` cột), hợp đồng kết nối `DecisionId` và 8 bất biến nguồn gốc `INV-P1..P8`. |

---

## 🎯 Core Capabilities (Trọng tâm Đồ án 2)

TADSS Runtime tập trung giải quyết 4 bài toán kỹ thuật phần mềm cốt lõi:

* **Decoupled Strategy Execution** — Tách biệt hoàn toàn logic chính sách quyết định khỏi ứng dụng nghiệp vụ thông qua `Shared.DecisionContractSDK` và cơ chế phân giải Dependency Injection $O(1)$ (`DefaultDecisionStrategy`, `MockAlternativeDecisionStrategy`).
* **Fault Tolerance & Decision Continuity** — Bảo vệ luồng giao dịch bằng chuỗi Polly v8 5 tầng (`OuterTimeout 500ms` ➔ `FallbackDecisionStrategy` ➔ `Retry 2x` ➔ `CircuitBreaker` ➔ `InnerTimeout 120ms`), tự động chuyển sang trạng thái an toàn `NEED_APPROVAL` khi chiến lược gặp sự cố.
* **Synchronous Auditability & Human-in-the-loop Governance** — Ghi nhận nhật ký kiểm toán bất biến đồng bộ (Fail-Closed) vào PostgreSQL (`tadss.decision_executions`) và cung cấp API phê duyệt/ghi đè thủ công có kiểm soát `IdempotencyKey` & `Optimistic Concurrency`.
* **Canonical Request-Hash Caching** — Tối ưu hóa thời gian phản hồi bằng chuẩn hóa JSON RFC 8785 + băm SHA-256 trên Redis L2 kèm cơ chế tự động giáng cấp về `IMemoryCache` L1 khi mất kết nối.

---

## 🏗️ Architecture

```text
TADSS Runtime
│
├── Shared.DecisionContractSDK
│   ├── Common
│   ├── Domain
│   ├── DTOs
│   │   ├── Decisions
│   │   ├── Governance
│   │   └── Mappings
│   ├── Enums
│   └── Interfaces
│
├── TADSS.Runtime.Api
│   ├── Controllers
│   ├── Services
│   ├── Middlewares
│   ├── Configuration
│   └── Infrastructure
│       ├── Data
│       ├── Resilience
│       ├── Caching
│       └── Events
│
├── TADSS.Strategies.Default
│
├── TADSS.Strategies.MockAlternative
│
└── Tests
    ├── TADSS.UnitTests
    │   ├── Benchmarks
    │   ├── Contracts
    │   ├── Domain
    │   ├── Services
    │   ├── Infrastructure
    │   └── TestData
    ├── TADSS.IntegrationTests
    └── TADSS.BenchmarkRunner
        ├── Protocol
        ├── Harness
        ├── Telemetry
        ├── Normalization
        └── Export
```

### Core principle

```text
Client (SmashCourt-BE)
   │
   ▼
Runtime API (POST /api/v1/decisions)
   │
   ▼
Decision Service + Request-Hash Cache (L2 Redis / L1 Memory)
   │
   ▼
Polly v8 Resilience Pipeline (500ms SLA Budget)
   │
   ├── DefaultDecisionStrategy (Conservative Baseline Business Policy)
   ├── MockAlternativeDecisionStrategy (30% Off-Peak / 50% Peak Dynamic Deposit)
   └── FallbackDecisionStrategy (Deterministic Safety Fallback -> NEED_APPROVAL)
   │
   ▼
Synchronous Audit Persistence & Decision Result
   │
   ├── Immutable Machine Snapshot (tadss.decision_executions)
   └── Human-in-the-loop Governance (GET /pending-approvals, POST /override)
```

The runtime is responsible for **orchestration, resilience, persistence, and observability**.

The strategy is responsible for **decision-making logic**.

---

## 🛠️ Technology Stack

### Backend
* **.NET 8** (C# 12)
* ASP.NET Core Web API
* Entity Framework Core 8
* PostgreSQL 16 / Npgsql *(Neon Cloud Managed `ap-southeast-1` hoặc Local/Docker)*

### Decision & Runtime
* Polly v8 Resilience Pipelines
* StackExchange.Redis *(Redis 7.x / 8.x trên Redis Cloud Managed `ap-southeast-1` hoặc Local, kèm L1 `IMemoryCache` Fallback)*
* Serilog Structured Logging & LogContext Enricher

### API & Contracts
* OpenAPI 3.0 / Swagger UI with XML Docstrings
* JSON Schema Draft 2020-12
* RFC 7807 ProblemDetails Error Responses

### Testing
* xUnit 2.9.2
* FluentAssertions 7.0.0
* Moq
* Microsoft.AspNetCore.Mvc.Testing

---

## 📦 Project Structure

| Project | Responsibility |
| :--- | :--- |
| `Shared.DecisionContractSDK` | Shared domain primitives, rich domain model `Reservation`, contracts, enums and interfaces |
| `TADSS.Runtime.Api` | Main runtime, DI-based Strategy Resolution, Polly resilience, and Web API controllers |
| `TADSS.Strategies.Default` | `DefaultDecisionStrategy` (Conservative baseline business policy) |
| `TADSS.Strategies.MockAlternative` | `MockAlternativeDecisionStrategy` (30% off-peak / 50% peak demonstration policy) |
| `TADSS.UnitTests` | Unit tests for domain invariants, SDK serialization, resolvers, resilience, caching, middlewares, policies, benchmark harness, and dataset provenance normalizer (483 tests) |
| `TADSS.IntegrationTests` | End-to-end API, DB stateful persistence, idempotency replay, fault injection, and governance tests (27 tests) |
| `TADSS.BenchmarkRunner` | Non-intrusive controlled experimental benchmark harness across 5 layers ($G_0..G_2$, $R_0..R_3$, $C_0..C_3$, $D_0..D_2$, $E_0..E_4$, Concurrency Matrix $1 \rightarrow 100\text{ VUs}$) & In-Vivo Audit Dataset Normalizer (`INV-P1..P8`) |

---

## 🚀 Getting Started

### Requirements
* .NET 8 SDK
* PostgreSQL 16 (Schema `tadss` — Neon Cloud `ap-southeast-1` hoặc Local)
* Redis 7.x / 8.x *(Redis Cloud `ap-southeast-1` hoặc Local — tự động suy thoái êm sang L1 `IMemoryCache` nếu không cấu hình)*

### Restore, Build & Test

```powershell
# 1. Restore dependencies
dotnet restore TADSS.slnx

# 2. Build Release with zero warnings
dotnet build TADSS.slnx --configuration Release --warnaserror

# 3. Run all 510 unit & integration tests (483 Unit + 27 Integration)
dotnet test TADSS.slnx --configuration Release --verbosity normal

# 4. Run Week 8-9 Controlled Experimental Benchmark & Dataset Normalization Suite (76 runs + 50 governance cases)
dotnet run --project tests/TADSS.BenchmarkRunner/TADSS.BenchmarkRunner.csproj --configuration Release

# 5. Run API locally
dotnet run --project src/TADSS.Runtime.Api
```

Swagger UI is available at `http://localhost:5296/swagger` (or `https://localhost:7074/swagger`) when running.

---

## 🗺️ 16-Week Development Roadmap (Đồ án 2 — TADSS Runtime Engine)

### 🟢 Đồ án 2: Nền tảng Runtime Engine Ra Quyết định Chịu lỗi (16 Tuần — Phạm vi hiện tại)

* **Phase 1 — Foundation & Architecture (Tuần 1 — 2)** `[COMPLETED ✅ - Milestone 1]`
  * [x] Solution skeleton .NET 8 & Clean Architecture
  * [x] Database Schema `tadss` & bảng `tadss.decision_executions`
  * [x] Draft API contract & Happy-path prototype

* **Phase 2 — Decision Contract SDK Stabilization & Contract Freeze (Tuần 3 — 4)** `[COMPLETED ✅ - Milestone 2]`
  * [x] Package purity (pure .NET 8 BCL, 0 external dependencies)
  * [x] Rich Domain Model `Reservation` & Invariants (`EnsureValid`, `CanBeCancelledWithoutPenalty`)
  * [x] Public API Surface snapshot (`baseline_public_api.txt`) & 7 Frozen JSON Schemas
  * [x] Contract Freeze governance manifest ([CONTRACT_V1_FREEZE.md](docs/contracts/CONTRACT_V1_FREEZE.md))

* **Phase 3 — Decision Runtime Engine & Governance Boundary (Tuần 5 — 6)** `[COMPLETED ✅ - Milestone 3]`
  * [x] DI-based Strategy Resolution ($O(1)$ Lookup)
  * [x] `DefaultDecisionStrategy` & `MockAlternativeDecisionStrategy`
  * [x] Cross-cutting infrastructure: Canonical Correlation ID, RFC 7807 ProblemDetails
  * [x] Governance E2E Tests (DB stateful assertions, partial index filtering, machine snapshot immutability)

* **Phase 4 — Resilience, Performance & Audit Experimentation (Tuần 7 — 9)** `[COMPLETED ✅ - Milestone 4]`
  * [x] Failure Taxonomy & Failure Semantics Matrix ([RESILIENCE_AND_FAULT_TAXONOMY.md](docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md))
  * [x] Experimental Benchmark Methodology & Dataset Pipeline ([EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md](docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md))
  * [x] Polly Budget Hardening (Inner 120ms / Outer 500ms) & Fault Injection Test Suite (F1 ➔ F7)
  * [x] Empirical Latency Envelope Benchmark (19 Scenarios / 76 Runs: $G_0..G_2, R_0..R_3, C_0..C_3, D_0..D_2, E_0..E_4$) & Throughput measurement ([WEEK8_BENCHMARK_REPORT.md](docs/research/benchmarks/WEEK8_BENCHMARK_REPORT.md))
  * [x] Audit Telemetry & Research Dataset Normalizer (`resilience_evaluation_dataset`, `latency_caching_dataset`, `governance_audit_dataset` CSV/JSONL & `INV-P1..P8` verification — [DATASET_SCHEMA_SPECIFICATION_V1.md](docs/research/DATASET_SCHEMA_SPECIFICATION_V1.md) & [WEEK9_DATASET_NORMALIZATION_REPORT.md](docs/research/datasets/WEEK9_DATASET_NORMALIZATION_REPORT.md))

* **Phase 5 — Human-in-the-loop & SmashCourt Integration (Tuần 10 — 12)** `[UPCOMING ⏳ - Milestone 5]`
  * [ ] Human Override API hardening & Optimistic Concurrency (`ExpectedVersion`)
  * [ ] Dynamic Feature Flag (`EnableTadssDecision` reload < 2s)
  * [ ] Manager Dashboard UI integration on `smashcourt-fe` (Next.js)
  * [ ] End-to-end booking integration: SmashCourt ↔ TADSS ↔ VNPay

* **Phase 6 — Testing, Evaluation & Portability Mock (Tuần 13 — 14)** `[UPCOMING ⏳ - Milestone 6]`
  * [ ] Full 11-scenario Failure Injection Matrix & KPI measurement
  * [ ] Coworking Space Portability Mock demonstration

* **Phase 7 — Final Stabilization & Defense Preparation (Tuần 15 — 16)** `[UPCOMING ⏳ - Final Milestone]`
  * [ ] Feature Freeze & Final Đồ án 2 Report completion (60 pages)
  * [ ] Demo video recording & Defense presentation slides

---

### 🔬 Hướng Phát triển Tiếp theo (Out-of-Scope Đồ án 2 — Khóa luận Tốt nghiệp: Chiến lược TADS)

> *Lưu ý: Các cấu phần giải thuật AI/ML dưới đây thuộc phạm vi mở rộng của **Khóa luận tốt nghiệp** ở giai đoạn tiếp theo (được loại trừ hoàn toàn khỏi phạm vi Đồ án 2 theo mục 5.2 của [`PROPOSAL_DO_AN_2.md`](docs/planning/PROPOSAL_DO_AN_2.md)).*

* **Phase 8 — Adaptive Trust Model & ML No-show Prediction (Tuần 1 — 8 Khóa luận)** `[FUTURE / OUT-OF-SCOPE ĐA2]`
  * [ ] Adaptive Trust Score algorithm with Exponential Time Decay ($e^{-\lambda \Delta t}$)
  * [ ] CatBoost Classifier training on 20,000+ synthetic reservation records
  * [ ] Decision Trace Explainability API (`GET /decisions/{id}/trace`)
* **Phase 9 — Multi-Agent Simulation & Ablation Studies (Tuần 9 — 12 Khóa luận)** `[FUTURE / OUT-OF-SCOPE ĐA2]`
  * [ ] Multi-Agent Simulator (1,000 agents across 4 behavioral groups)
  * [ ] Ablation studies on Expected Business Utility & Equal Opportunity Fairness
  * [ ] Thesis defense before the Graduation Committee

---

## 📄 License

This project is developed for academic and software engineering evaluation as part of **Đồ án 2** (with future extensibility for **Khóa luận tốt nghiệp**).
