# TADSS Runtime

**Trustworthy-Adaptive-Decision-Support-System (TADSS)**

TADSS Runtime is a decision-support runtime designed to provide **adaptive, explainable, resilient, and trustworthy decision-making** for service reservation platforms.

The project is developed as the technical foundation for **Đồ án 2 / Khóa luận tốt nghiệp**, with the goal of evolving into a reusable decision-support architecture that can be integrated with different reservation domains.

---

## 📚 Project Documentation Hub (Sơ Đồ Tài Liệu)

Toàn bộ tài liệu kỹ thuật, kiến trúc, hợp đồng API và kế hoạch nghiên cứu được quản lý tập trung theo 4 phân hệ trong thư mục [`docs/`](docs/):

```text
TADSS-Runtime/docs/
├── 🏛️ architecture/                                    <-- KIẾN TRÚC & QUYẾT ĐỊNH KỸ THUẬT
│   ├── ARCHITECTURE_DECISION_RECORDS.md                (Tổng hợp 5 ADR cốt lõi)
│   ├── SOFTWARE_ARCHITECTURE_SPECIFICATION.md          (Bản vẽ thiết kế hệ thống C4 Model)
│   ├── DECISION_PERSISTENCE_INVARIANTS.md              (Invariants tầng lưu trữ DB & Audit Ledger)
│   ├── RESILIENCE_AND_FAULT_TAXONOMY.md                (Ma trận lỗi F1-F7 & Resilience Invariants)
│   └── adr/                                            (Chi tiết các bản ghi kiến trúc ADR-001 -> ADR-005)
│       ├── ADR-001-why-strategy-pattern.md
│       ├── ADR-002-why-sdk-first-approach.md
│       ├── ADR-003-why-polly-resilience.md
│       ├── ADR-004-why-rich-domain-model.md
│       └── ADR-005-why-catboost-ml.md
│
├── 📜 contracts/                                       <-- HỢP ĐỒNG API & WIRE SCHEMAS (FROZEN v1.0.0)
│   ├── API_CONTRACT_V1.md                              (Đặc tả API Contract v1 Single Source of Truth)
│   ├── CONTRACT_V1_FREEZE.md                           (Tuyên ngôn đóng băng SDK v1.0.0 - Milestone 2)
│   ├── INTEGRATION_SPEC.md                             (Đặc tả tích hợp hệ thống SmashCourt ↔ TADSS)
│   ├── baseline_public_api.txt                         (Snapshot 32 Public Types của SDK)
│   ├── baseline_metrics.md                             (Báo cáo Quality Gates Milestone 2)
│   ├── openapi-v1.yaml                                 (OpenAPI 3.0 Specification)
│   └── schemas/                                        (7 JSON Schemas Draft 2020-12)
│
├── 📋 planning/                                        <-- QUẢN TRỊ ĐỀ ÁN & LỘ TRÌNH 16 TUẦN
│   ├── MASTER_EXECUTION_PLAN.md                        (Kế hoạch thực thi 16 tuần - Kim chỉ nam đã khóa)
│   ├── THESIS_DEVELOPMENT_PLAYBOOK.md                  (Cẩm nang điều hướng trung tâm & Khóa thuật ngữ)
│   ├── BACKLOG_SPRINTS.md                              (Phân rã User Stories & Tasks chi tiết cho SV1 & SV2)
│   ├── PROJECT_WBS_MAPPING.md                          (WBS Mapping, 6 Milestones, Quản trị rủi ro)
│   └── PROPOSAL_DO_AN_2.md                             (Đề cương đề xuất Đồ án 2 chính thức)
│
└── 🔬 research/                                        <-- NGHIÊN CỨU KHOA HỌC & LUẬN VĂN
    ├── EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md(Phương pháp thực nghiệm, DCR, Dataset Normalizer)
    ├── RESEARCH_PROBLEM_BACKLOG.md                     (Khung 12 bài toán nghiên cứu Enterprise AI OS)
    └── RESEARCH_THESIS_DEFENSE.md                      (Đề cương bảo vệ Khóa luận & Kịch bản phản biện)
```

| Danh mục | Tài liệu cốt lõi | Mô tả & Mục đích sử dụng |
| :--- | :--- | :--- |
| **Kiến trúc & Invariants** | [RESILIENCE_AND_FAULT_TAXONOMY.md](docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md) | Đặc tả 3 miền lỗi, Ma trận F1 ➔ F7, cơ chế Polly Circuit Breaker, và chính sách Mandatory Audit vs Degraded Cache. |
| **Kiến trúc DB** | [DECISION_PERSISTENCE_INVARIANTS.md](docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md) | Ràng buộc bất biến của bảng `tadss.decision_executions`, Snapshot máy bất biến $\oplus$ Metadata quản trị có thể thay đổi. |
| **Hợp đồng API** | [API_CONTRACT_V1.md](docs/contracts/API_CONTRACT_V1.md) | Single Source of Truth cho 4 endpoints (`/decisions`, `/decisions/{id}`, `/pending-approvals`, `/override`). |
| **Đóng băng SDK** | [CONTRACT_V1_FREEZE.md](docs/contracts/CONTRACT_V1_FREEZE.md) | Tuyên ngôn đóng băng wire format, 7 JSON schemas và 32 public types của `Shared.DecisionContractSDK`. |
| **Kế hoạch 16 tuần** | [MASTER_EXECUTION_PLAN.md](docs/planning/MASTER_EXECUTION_PLAN.md) | Kế hoạch thực thi chi tiết 16 tuần, ma trận Owner/Reviewer, Definition of Done và Ma trận Failure Test. |
| **Cẩm nang Đề án** | [THESIS_DEVELOPMENT_PLAYBOOK.md](docs/planning/THESIS_DEVELOPMENT_PLAYBOOK.md) | Quy chuẩn thuật ngữ (`TADSS` vs `TADS` vs `ATDA`), phân tầng phạm vi (Core/Important/Optional), và lối thoát khẩn cấp. |
| **Thực nghiệm Khoa học** | [EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md](docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) | Câu hỏi nghiên cứu (RQ1-RQ3), Thí nghiệm trực giao ($G_0..G_2, R_0..R_3$), chỉ số DCR, và đường ống Dataset Normalizer. |

---

## 🎯 Objectives

TADSS focuses on four main capabilities:

* **Adaptive Decision** — make decisions based on contextual and behavioral data.
* **Trust & Fairness** — incorporate trust scoring while considering fairness constraints.
* **Explainability** — record and expose decision traces for transparency.
* **Resilience** — maintain service availability when AI/decision components fail.

The architecture is designed to separate the **decision strategy** from the **runtime**, allowing different decision-making algorithms or policies to be plugged in without changing the core runtime.

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
    │   ├── Contracts
    │   ├── Domain
    │   ├── Services
    │   ├── Infrastructure
    │   └── TestData
    └── TADSS.IntegrationTests
```

### Core principle

```text
Client
   │
   ▼
Runtime API
   │
   ▼
Decision Service
   │
   ▼
Strategy
   │
   ├── Default Strategy (100% Deterministic Fallback)
   ├── Mock Alternative Strategy (30% / 50% Dynamic Deposit)
   └── Future AI/ML Strategy (TADS Adaptive Trust)
   │
   ▼
Decision Result
   │
   ├── Decision Trace
   ├── Audit Log (tadss.decision_executions)
   └── Resilience / Fallback
```

The runtime is responsible for **orchestration, resilience, persistence, and observability**.

The strategy is responsible for **decision-making logic**.

---

## 🛠️ Technology Stack

### Backend
* **.NET 8** (C# 12)
* ASP.NET Core Web API
* Entity Framework Core 8
* PostgreSQL 16 / Npgsql

### Decision & Runtime
* Polly v8 Resilience Pipelines
* StackExchange.Redis 7.x
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
| `TADSS.Strategies.Default` | `DefaultDecisionStrategy` (Deterministic 100% deposit fallback policy) |
| `TADSS.Strategies.MockAlternative` | `MockAlternativeDecisionStrategy` (30% off-peak / 50% peak demonstration policy) |
| `TADSS.UnitTests` | Unit tests for domain invariants, SDK serialization, resolvers, and policies (392 tests) |
| `TADSS.IntegrationTests` | End-to-end API, DB stateful persistence, idempotency replay, and governance tests (18 tests) |

---

## 🚀 Getting Started

### Requirements
* .NET 8 SDK
* PostgreSQL 16 (Schema `tadss`)
* Redis 7.x *(tùy chọn khi bật Distributed Caching)*

### Restore, Build & Test

```powershell
# 1. Restore dependencies
dotnet restore TADSS.slnx

# 2. Build Release with zero warnings
dotnet build TADSS.slnx --configuration Release --warnaserror

# 3. Run all 410 unit & integration tests
dotnet test TADSS.slnx --configuration Release --verbosity normal

# 4. Run API locally
dotnet run --project src/TADSS.Runtime.Api
```

Swagger UI is available at `http://localhost:5000/swagger` when running.

---

## 🗺️ 16-Week Development Roadmap (Đồ án 2 & Khóa luận)

### 🟢 Đồ án 2: Nền tảng Runtime Engine Ra Quyết định Chịu lỗi (16 Tuần)

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

* **Phase 4 — Resilience, Performance & Audit Experimentation (Tuần 7 — 9)** `[IN PROGRESS 🔄 - Milestone 4]`
  * [x] Failure Taxonomy & Failure Semantics Matrix ([RESILIENCE_AND_FAULT_TAXONOMY.md](docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md))
  * [x] Experimental Benchmark Methodology & Dataset Pipeline ([EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md](docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md))
  * [ ] Polly Budget Hardening & Fault Injection Test Suite (F1 ➔ F7)
  * [ ] Empirical Latency Envelope Benchmark ($G_0..G_2, R_0..R_3$) & Throughput measurement
  * [ ] Dataset Normalizer script & Ablation Study preparation

* **Phase 5 — Human-in-the-loop & SmashCourt Integration (Tuần 10 — 12)** `[UPCOMING ⏳ - Milestone 5]`
  * [ ] Human Override API hardening & Optimistic Concurrency (`ExpectedVersion`)
  * [ ] Dynamic Feature Flag (`EnableTadssDecision` reload < 2s)
  * [ ] Manager Dashboard UI integration on `smashcourt-fe` (Next.js)
  * [ ] End-to-end booking integration: SmashCourt ↔ TADSS ↔ VNPay

* **Phase 6 — Testing, Evaluation & Portability Mock (Tuần 13 — 14)** `[UPCOMING ⏳ - Milestone 6]`
  * [ ] Full 11-scenario Failure Injection Matrix & KPI measurement
  * [ ] Coworking Space Portability Mock demonstration

* **Phase 7 — Final Stabilization & Defense Preparation (Tuần 15 — 16)** `[UPCOMING ⏳ - Final Milestone]`
  * [ ] Feature Freeze & Final Thesis Report completion (60 pages)
  * [ ] Demo video recording & Defense presentation slides

---

### 🔬 Khóa luận Tốt nghiệp: Chiến lược Quyết định Thích ứng Tin cậy (TADS) (12 Tuần)

* **Phase 8 — Adaptive Trust Model & ML No-show Prediction (Tuần 1 — 8)**
  * [ ] Adaptive Trust Score algorithm with Exponential Time Decay ($e^{-\lambda \Delta t}$)
  * [ ] CatBoost Classifier training on 20,000+ synthetic reservation records
  * [ ] Decision Trace Explainability API (`GET /decisions/{id}/trace`)
* **Phase 9 — Multi-Agent Simulation & Ablation Studies (Tuần 9 — 12)**
  * [ ] Multi-Agent Simulator (1,000 agents across 4 behavioral groups)
  * [ ] Ablation studies on Expected Business Utility & Equal Opportunity Fairness
  * [ ] Thesis defense before the Graduation Committee

---

## 📄 License

This project is developed for academic research and educational purposes as part of the Graduation Thesis (Đồ án 2 & Khóa luận tốt nghiệp).
