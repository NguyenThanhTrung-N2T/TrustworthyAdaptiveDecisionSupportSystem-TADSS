# SỔ TAY TRIỂN KHAI ĐỒ ÁN & KHÓA LUẬN (THESIS DEVELOPMENT PLAYBOOK)

Tài liệu này là **Cẩm nang Điều hướng Trung tâm (Master Navigation Hub)** cho toàn bộ dự án Đồ án 2 và Khóa luận tốt nghiệp. Mọi quy chuẩn kỹ thuật, thuật ngữ, bản đồ tài liệu, lộ trình 16 tuần và cơ chế quản trị rủi ro đều được quy chiếu tại đây.

---

## 0. BẢNG THUẬT NGỮ CHÍNH THỨC (TERMINOLOGY LOCK)

> **⚠️ BẮT BUỘC:** Không được sử dụng sai lệch hoặc hoán đổi các thuật ngữ dưới đây trong bất kỳ tài liệu hay báo cáo nào:

| Viết tắt | Tên đầy đủ | Định nghĩa | Phạm vi áp dụng |
|:---|:---|:---|:---|
| **TADSS** | Trustworthy Adaptive Decision Support System | **Nền tảng runtime engine độc lập** hỗ trợ thực thi, định tuyến, kiểm soát an toàn (resilience, fallback, idempotency, optimistic locking) và lưu vết quyết định (audit trail). Trong Đồ án 2, *"Trustworthy"* được hiểu theo khía cạnh **operational trustworthiness** (độ tin cậy vận hành hạ tầng phần mềm). | Đồ án 2 (Hệ thống) |
| **TADS** | Trustworthy Adaptive Decision Strategy | **Chiến lược quyết định thích ứng thông minh** được nghiên cứu và cài đặt trên interface của SDK. Bao gồm: Adaptive Trust Score với Time Decay ($e^{-\lambda \Delta t}$), CatBoost dự báo No-show, và Decision Trace API. | Khóa luận (Giải thuật / ML) |
| **ATDA** | Adaptive Trust Decision Architecture | **Khung kiến trúc nghiên cứu đề xuất (Proposed Architecture / Research Framework)** mô tả quan hệ phân tầng giữa Runtime Engine (TADSS) và Chiến lược thích ứng (TADS). ATDA là framework tổ chức, không phải hypothesis. | Khóa luận (Framework) |

### 🔬 Giả thuyết Nghiên cứu & Ràng buộc (Research Hypothesis & Constraints)
*   **H1 (Primary Hypothesis):** Chiến lược TADS khi thực thi qua nền tảng TADSS cải thiện Expected Business Utility (Hiệu ích kinh doanh kỳ vọng) so với chính sách cọc tĩnh mặc định (Static Policy).
*   **Ràng buộc đánh giá (Constraints):**
    *   *C1 (Fairness Constraint):* Đảm bảo tính công bằng cơ hội (Equal Opportunity): $\left| TPR_{\text{group A}} - TPR_{\text{group B}} \right| \le \epsilon$.
    *   *C2 (Resilience Constraint):* Đảm bảo kiểm soát độ trễ SLA và duy trì hoạt động liên tục khi có sự cố qua Polly Resilience.

### 🏛️ Mối quan hệ Phân cấp Kiến trúc
```
ATDA (Proposed Architecture / Research Framework — Khóa luận)
 ├── TADSS (Runtime Platform — Đồ án 2)
 │    ├── Decision Contract SDK (Canonical Reservation Model, Value Objects, Enums)
 │    ├── Strategy Factory (DI-based Resolution, O(1) Lookup)
 │    │    ├── DefaultDecisionStrategy (Thanh toán tĩnh 100% - Fallback)
 │    │    └── MockAlternativeDecisionStrategy (Cọc động 30% giờ thường / 50% giờ cao điểm)
 │    ├── Polly v8 Resilience Pipeline (Outer Timeout 500ms, Retry 2 lần, Inner Timeout 150ms, CB, Fallback)
 │    ├── Append-only Audit Trail (Synchronous MediatR events trong DB Transaction)
 │    ├── Human-in-the-loop Override API (IdempotencyKey, ExpectedVersion, No Auto-Approve)
 │    ├── Request-Hash Cache (Redis / IMemoryCache fallback, Sort key JSON, SHA256)
 │    └── Feature Flag động (EnableTadssDecision reload qua IOptionsMonitor < 2s)
 └── TADS (Chiến lược nghiên cứu thích ứng — Khóa luận)
      ├── Adaptive Trust Score + Time Decay (e^-λΔt)
      ├── CatBoost Classifier No-show Predictor
      ├── Decision Trace Explainability (GET /decisions/{id}/trace)
      └── Agent-based Simulator (1,000 agents) & Ablation Studies (20,000+ synthetic records)
```

### 🛡️ 3 Quy tắc Ranh giới Kiến trúc Cốt lõi (Architectural Boundaries)
1. **Logical Database Boundary:** TADSS sử dụng chung PostgreSQL server vật lý (Neon Cloud) với SmashCourt nhưng **cô lập 100% ở tầng Logical Schema có tên là `tadss`** (bảng nghiệp vụ SmashCourt ở `public`, bảng của TADSS duy nhất là `tadss.decision_executions`).
2. **Domain Portability Boundary:** TADSS đạt tính di động nhờ giao thức trừu tượng `Reservation` SDK. Khi mở rộng sang Coworking Space hay Phòng khám, chỉ cần viết Adapter mapping ở client, mã nguồn TADSS Core giữ nguyên.
3. **Telemetry Ownership Boundary:** Bảng `tadss.decision_executions` do Đồ án 2 thiết kế và ghi nhận telemetry kỹ thuật (`execution_time_ms`, `fallback_used`, `failure_reason`, `cache_hit`, `execution_type`). Khóa luận sẽ tái sử dụng tập dữ liệu này làm dataset chạy thực nghiệm Ablation Study.

### ⚠️ Bảng Chuẩn hóa Thuật ngữ Kỹ thuật (Technical Clarification)

| Thuật ngữ SAI (Tuyệt đối tránh) | Thuật ngữ ĐÚNG (Chuẩn hóa) | Giải thích kỹ thuật |
|:---|:---|:---|
| Semantic Cache | **Request-Hash Cache** | Cơ chế băm SHA256 payload là exact-match, không có khả năng hiểu ngữ nghĩa (semantic). |
| Immutable event chain | **Append-only Audit Trail** | Không có cơ chế hash chaining/blockchain ở DB, chỉ là cơ chế insert-only log. |
| MediatR đảm bảo transaction | **In-process events trong PostgreSQL transaction boundary** | MediatR chỉ là in-memory mediator, transaction do EF Core DbContext quản lý. |
| Hot-swap strategy lúc runtime | **Thay thế Strategy ở mức Cấu hình / DI Registration** | Đăng ký Strategy qua DI Container $O(1)$, không nạp DLL động lúc đang chạy. |
| Circuit Breaker đếm 5 lỗi | **Circuit Breaker mở khi tỷ lệ lỗi vượt ngưỡng trong cửa sổ mẫu** | Polly v8 sử dụng `FailureRatio` (ví dụ 0.5) trong `SamplingDuration` (30s), min 10 requests. |
| Retry 3 lần (4 attempts) | **Retry 2 lần (3 attempts tổng cộng)** | $(3 \times 150\text{ms}) + (2 \times 30\text{ms}) = 510\text{ms} \approx 500\text{ms}$ SLA Budget. |
| Schema `tadss_*` | **Schema `tadss`** | PostgreSQL không dùng ký tự đại diện `*` làm tên schema. |

---

## I. TỔNG QUAN ĐỀ TÀI & ĐỊNH VỊ 3 HỌC PHẦN

### 1.1. Khóa Định vị — 3 Câu Hỏi Cốt Lõi

| Học phần | Tên chính thức đề tài | Định vị cốt lõi |
|:---|:---|:---|
| **Đồ án 1** | Xây dựng hệ thống quản lý và đặt sân cầu lông trực tuyến SmashCourt | **HOW TO BOOK A COURT** (Nghiệp vụ thương mại, CRUD, Chatbot FAQ) |
| **Đồ án 2** | Thiết kế và xây dựng nền tảng Runtime Engine ra quyết định chịu lỗi cho hệ thống đặt chỗ dịch vụ | **HOW TO EXECUTE A DECISION SAFELY** (Kiến trúc phần mềm chịu lỗi, SDK, API, Audit, Fallback) |
| **Khóa luận** | Nghiên cứu và đánh giá chiến lược quyết định thích ứng tin cậy cho nền tảng đặt chỗ dịch vụ | **HOW TO MAKE THE DECISION INTELLIGENTLY** (Nghiên cứu khoa học, Trust Score, ML, Simulator) |

### 1.2. Ma trận So sánh Phạm vi (Boundary Matrix)

| Tiêu chí | Đồ án 1 (Case Study) | Đồ án 2 (Runtime Platform) | Khóa luận (Research Strategy) |
|:---|:---|:---|:---|
| **Sản phẩm cốt lõi** | Web SmashCourt (Next.js + .NET) | TADSS Core Engine (Web API + SDK) | Chiến lược TADS trên TADSS |
| **Trọng tâm kỹ thuật** | CRUD Booking, VNPay, SignalR | Chịu lỗi (Polly), Audit Trail, Override API | Giải thuật thích ứng, Học máy, Fairness |
| **Thành phần AI** | Gemini Chatbot FAQ | Không phụ thuộc AI (Dùng Mock Strategy) | Adaptive Trust Score + CatBoost Classifier |
| **Khả năng chịu lỗi** | Chưa có (Single Point of Failure) | **Trọng tâm số 1 (Polly v8 Nested Pipeline)** | Đánh giá độ ổn định của giải thuật |
| **Thư viện SDK** | Không có | **Xây dựng `Shared.DecisionContractSDK`** | Tái sử dụng SDK và mở rộng Model |
| **Chiến lược quyết định** | Cọc tĩnh 100% (Hardcoded) | `Default` (100%) & `Mock` (30%/50%) | `AdaptiveTrustDecisionStrategy` |
| **Phương pháp kiểm thử** | Manual & Postman Tests | Unit Tests, Integration Tests, Failure Injection | Ablation Studies & Multi-agent Simulation |

---

## II. BẢN ĐỒ HỆ THỐNG TÀI LIỆU DỰ ÁN (PROJECT DOCUMENTATION MAP)

Tất cả các tài liệu trong workspace được tổ chức theo vai trò và phạm vi rõ ràng:

```
SmashCourt Workspace/
├── 🏆 MASTER_EXECUTION_PLAN.md                <-- [MASTER] Kế hoạch thực thi 16 tuần, Owner/Reviewer, DoD (Kim chỉ nam)
├── 📘 THESIS_DEVELOPMENT_PLAYBOOK.md          <-- [HUB] Cẩm nang điều hướng trung tâm (Tài liệu này)
├── 📄 PROPOSAL_DO_AN_2.md                     <-- [PROPOSAL] Đề xuất chính thức Đồ án 2 nộp GVHD
├── 🗺️ PROJECT_WBS_MAPPING.md                  <-- [WBS] Phân rã 16 tuần, 6 Milestones, Traffic Light, Risk
├── 📋 BACKLOG_SPRINTS.md                      <-- [BACKLOG] User Stories & Tasks chi tiết 16 tuần cho SV1 & SV2
├── 📐 SOFTWARE_ARCHITECTURE_SPECIFICATION.md  <-- [SAS] Đặc tả bản vẽ thiết kế kỹ thuật phần mềm
├── 🔌 INTEGRATION_SPEC.md                     <-- [SPEC] Hợp đồng API, Schema tadss, Rollback, k6 Test
├── 🏛️ ARCHITECTURE_DECISION_RECORDS.md        <-- [ADR] Tổng hợp 5 quyết định kiến trúc cốt lõi
│   └── docs/adr/ (ADR-001 -> ADR-005)        <-- [ADR Detail] Chi tiết từng quyết định kiến trúc
├── 🔬 RESEARCH_PROBLEM_BACKLOG.md             <-- [RESEARCH] 12 bài toán nghiên cứu Enterprise AI OS
├── 🎓 RESEARCH_THESIS_DEFENSE.md              <-- [DEFENSE] Đề cương bảo vệ Khóa luận & Phản biện
└── 📊 docs/3.1-kien-truc-he-thong.md          <-- [DOC] Kiến trúc SmashCourt Trước/Sau khi có TADSS
```

### Chi tiết Vai trò Từng Tài liệu:

| Tài liệu | Phạm vi | Mục đích sử dụng | Độc giả chính |
|:---|:---:|:---|:---|
| [MASTER_EXECUTION_PLAN.md](file:///d:/SINH%20VIEN/HK6/DA1/SmashCourt/MASTER_EXECUTION_PLAN.md) | **Kim chỉ nam** | Kế hoạch thực thi chi tiết 16 tuần, ma trận Owner/Reviewer, Definition of Done và Ma trận Failure Test. | Toàn nhóm phát triển |
| [THESIS_DEVELOPMENT_PLAYBOOK.md](file:///d:/SINH%20VIEN/HK6/DA1/SmashCourt/THESIS_DEVELOPMENT_PLAYBOOK.md) | Toàn diện | Quy chuẩn thuật ngữ, điều hướng dự án, phân tầng scope và quy tắc ranh giới. | Toàn nhóm & GVHD |
| [PROPOSAL_DO_AN_2.md](file:///d:/SINH%20VIEN/HK6/DA1/SmashCourt/PROPOSAL_DO_AN_2.md) | Đồ án 2 | Đề xuất nghiên cứu chính thức trình Giảng viên hướng dẫn và Hội đồng duyệt đề tài. | GVHD & Hội đồng |
| [PROJECT_WBS_MAPPING.md](file:///d:/SINH%20VIEN/HK6/DA1/SmashCourt/PROJECT_WBS_MAPPING.md) | Đồ án 2 | Phân rã công việc 16 tuần, 6 Milestones, cơ chế Traffic Light, Lối thoát khẩn cấp. | Quản lý tiến độ |
| [BACKLOG_SPRINTS.md](file:///d:/SINH%20VIEN/HK6/DA1/SmashCourt/BACKLOG_SPRINTS.md) | Thực thi | Chi tiết Epics ➔ User Stories ➔ Tasks kèm phân công cụ thể Sinh viên 1 & 2. | Sinh viên code hàng ngày |
| [SOFTWARE_ARCHITECTURE_SPECIFICATION.md](file:///d:/SINH%20VIEN/HK6/DA1/SmashCourt/SOFTWARE_ARCHITECTURE_SPECIFICATION.md) | Kỹ thuật | Bản vẽ chi tiết Class Diagram, Sequence Diagram, Component Design mở khi lập trình. | Sinh viên phát triển |
| [INTEGRATION_SPEC.md](file:///d:/SINH%20VIEN/HK6/DA1/SmashCourt/INTEGRATION_SPEC.md) | Tích hợp | Đặc tả JSON API Contracts, DDL Schema `tadss`, Migration, Rollback và Failure Injection. | Tích hợp hệ thống |
| [ARCHITECTURE_DECISION_RECORDS.md](file:///d:/SINH%20VIEN/HK6/DA1/SmashCourt/ARCHITECTURE_DECISION_RECORDS.md) | Kiến trúc | Lưu trữ 5 quyết định kiến trúc: Strategy Pattern, DI Lookup, Polly v8, MediatR, Cache. | Chương 2-3 Báo cáo |
| [RESEARCH_PROBLEM_BACKLOG.md](file:///d:/SINH%20VIEN/HK6/DA1/SmashCourt/RESEARCH_PROBLEM_BACKLOG.md) | Khóa luận | 12 bài toán Enterprise AI OS làm cơ sở phát triển nghiên cứu học thuật. | Chương 1 Luận văn |
| [RESEARCH_THESIS_DEFENSE.md](file:///d:/SINH%20VIEN/HK6/DA1/SmashCourt/RESEARCH_THESIS_DEFENSE.md) | Khóa luận | Tổng hợp Research Questions (RQs), Scientific Contributions, Kịch bản phản biện. | Bảo vệ tốt nghiệp |

---

## III. PHÂN TẦNG PHẠM VI & QUẢN TRỊ RỦI RO (SCOPE TIERING & RISK SAFEGUARDS)

### 3.1. Ma trận Phân tầng Phạm vi (Scope Tiering Matrix)

```
┌────────────────────────────────────────────────────────────────────────┐
│ 🔴 CORE TIER (Bắt buộc 100% — Tuyệt đối không cắt)                     │
│  - Shared Decision Contract SDK (Domain Model, DTOs, Invariants)       │
│  - Decision Runtime Web API (POST /api/v1/decisions, StrategyFactory)  │
│  - Executable Strategies (Default 100% Fallback, Mock 30%/50%)         │
│  - Polly v8 Nested Resilience Pipeline (500ms Outer, 2 Retries, CB)    │
│  - Append-only Audit Trail (MediatR Events trong DB Transaction)       │
│  - Human Override API (IdempotencyKey, ExpectedVersion Concurrency)    │
│  - Feature Flag động (EnableTadssDecision reload qua IOptionsMonitor)  │
│  - SmashCourt Integration & Integration Tests                          │
├────────────────────────────────────────────────────────────────────────┤
│ 🟡 IMPORTANT TIER (Cố gắng hoàn thành — Có phương án hạ cấp tự động)   │
│  - Redis Request-Hash Cache ──[Hạ cấp]--> IMemoryCache In-Process      │
│  - Manager Dashboard trên smashcourt-fe ──[Hạ cấp]--> Giảm độ phức tạp│
│  - Structured Logging (Serilog Console & File)                         │
│  - Retry & Circuit Breaker hoàn chỉnh                                  │
├────────────────────────────────────────────────────────────────────────┤
│ 🟢 OPTIONAL TIER (Cắt giảm ngay khi rơi vào màu Vàng/Đỏ)                │
│  - Advanced Dashboard UI Polish (Biểu đồ, Animation phức tạp)          │
│  - External Provider Cost Guardrail                                    │
│  - Additional Benchmark Scenarios                                      │
│  - OpenTelemetry Distributed Tracing                                  │
└────────────────────────────────────────────────────────────────────────┘
```

### 3.2. Sáu Mốc Kiểm Soát Tiến Độ Cốt Lõi (6 Milestones Checkpoints)
*   **[Mốc 1: Cuối Tuần 2]** ───► **Foundation Ready:** Solution skeleton, Database Schema `tadss` và Contract v1 được phê duyệt.
*   **[Mốc 2: Cuối Tuần 4]** ───► **Decision Contract Ready:** DLL `Shared.DecisionContractSDK` biên dịch thành công, Unit Tests > 80% coverage.
*   **[Mốc 3: Cuối Tuần 6]** ───► **Runtime Engine Standalone:** Web API và DI-based Strategy Resolution chạy độc lập hoàn chỉnh.
*   **[Mốc 4: Cuối Tuần 9]** ───► **Resilient & Auditable Core:** Polly v8 Pipeline, MediatR Audit Trail và Redis Request-Hash Cache hoàn tất.
*   **[Mốc 5: Cuối Tuần 12]** ───► **End-to-End Integration Ready:** SmashCourt ↔ TADSS ↔ VNPay và Manager Dashboard UI tích hợp thành công.
*   **[Mốc 6: Cuối Tuần 14]** ───► **Technical Evaluation Complete:** Vượt qua 11 kịch bản kiểm thử sự cố, đo lường đầy đủ SLA p95 < 150ms.
*   **[Final Milestone: Cuối Tuần 16]** ───► **Defense Ready:** Hoàn thành cuốn Báo cáo 60 trang, Video Demo và Slide thuyết trình.

### 3.3. Hệ thống Đèn Giao Thông Đánh giá Tiến độ (Traffic Light System)
*   🟢 **GREEN (Đúng tiến độ):** Hoàn thành 100% các task theo kế hoạch mốc $\rightarrow$ Tiếp tục triển khai toàn bộ Scope.
*   🟡 **YELLOW (Trễ 1-2 ngày):** Kích hoạt cắt giảm tầng **Optional (Cắt Dashboard Polish, Cost Guardrail)** để tập trung tài nguyên.
*   🔴 **RED (Trễ $\ge 3$ ngày):** Kích hoạt chuyển tầng **Important/Supporting (Chuyển Redis $\rightarrow$ `IMemoryCache`, tối giản UI)**, tập trung 100% cho Core Features.

### 3.4. Lối thoát Khẩn cấp Phase 4 (Phase 4 Emergency Escape Hatch)
> Nếu đến **cuối Tuần 8 (Day 5)** mà Polly v8 hoặc MediatR Events vẫn bị tắc nghẽn chưa chạy ổn định:
> $\rightarrow$ **KÍCH HOẠT CHẾ ĐỘ MVP TOÀN DIỆN (Minimal Viable Product Mode):**
> 1. **Polly:** Chỉ giữ `Outer Timeout (500ms)` + `Fallback` (Tạm ngắt Retry & Circuit Breaker).
> 2. **Audit Trail:** Ghi trực tiếp log audit vào DB sau khi execute (bỏ qua in-transaction MediatR events).
> 3. **Cache:** Sử dụng duy nhất `IMemoryCache` cục bộ, bỏ qua Redis.
>
> *Triết lý:* *"Tốt hơn là bàn giao 80% hệ thống chạy hoàn hảo đúng hạn thay vì bàn giao 0% do trễ deadline!"*

### 3.5. Quy tắc Tích hợp Dashboard trên `smashcourt-fe` (Task 5.4)
> Tích hợp trực tiếp các trang quản lý duyệt cọc vào thư mục `smashcourt-fe` (Next.js) hiện có để tận dụng giao diện, Tailwind và auth component sẵn có. Nếu sau **Tuần 10 Day 2** gặp trục trặc kỹ thuật lớn, chuyển ngay sang C# Razor Page / HTML Table trong vòng 1 ngày.

### 3.6. Chiến lược Viết Báo cáo Cuốn chiếu từ Tuần 10 (Task 5.3)
> *   **Tuần 10 (Phase 5):** SV1 + SV2 pair-writing Chương 1 (10-12 trang); SV1 viết Chương 2.1-2.2 (8-10 trang); SV2 viết Chương 2.3-2.4 (8-10 trang) đạt 26-32 trang (>40% khối lượng).
> *   **Tuần 15 (Phase 7):** Điền số liệu thực nghiệm vào Chương 3 & 4, vẽ sơ đồ kiến trúc và chuẩn hóa trích dẫn.
> *   **Tuần 16 (Phase 7):** Quay video demo, làm Slide thuyết trình và diễn tập bảo vệ.

### 3.7. Quy trình Họp Rà soát Hàng tuần (Weekly Checkpoint Protocol)
*   **Thời gian cố định:** Tối Chủ Nhật hàng tuần từ 20:00 - 20:30 (30 phút).
*   **Cấu trúc 10-10-10:**
    1. *Rà soát công việc (10 phút):* Đối chiếu tiến độ thực tế, xác định màu tiến độ (🟢/🟡/🔴).
    2. *Đánh giá rủi ro (10 phút):* Ra quyết định kích hoạt Scope Tiering nếu rơi vào màu Vàng hoặc Đỏ.
    3. *Kế hoạch tuần tới (10 phút):* Chốt Top 3 ưu tiên của từng thành viên và deadline cam kết.
*   **Lưu trữ:** Ghi biên bản họp ngắn gọn vào `docs/weekly-logs/week-{N}.md` và commit lên Git.

---

## IV. LỘ TRÌNH TRIỂN KHAI 16 TUẦN & MA TRẬN PHÂN CÔNG (16-WEEK WBS & RACI)

```mermaid
gantt
    title Lộ trình Triển khai Đồ án 2 (16 tuần) & Khóa luận (12 tuần)
    dateFormat  YYYY-MM-DD
    section Đồ án 2 (16 tuần)
    Phase 1: Foundation & Architecture (W1-W2)       :p1, 2026-09-01, 14d
    Phase 2: Decision Contract SDK (W3-W4)           :p2, after p1, 14d
    Phase 3: Decision Runtime API (W5-W6)            :p3, after p2, 14d
    Phase 4: Resilience, Cache & Audit (W7-W9)       :p4, after p3, 21d
    Phase 5: HITL & SmashCourt Integration (W10-W12) :p5, after p4, 21d
    Phase 6: Testing & Technical Evaluation (W13-W14):p6, after p5, 14d
    Phase 7: Stabilization, Documentation & Defense (W15-W16):p7, after p6, 14d
    section Khóa luận (12 tuần)
    Phase 8: ML Models & Trust Decay (W1-W8)         :p8, after p7, 56d
    Phase 9: Multi-Agent Simulator & Thesis (W9-W12) :p9, after p8, 28d
```

### Ma trận Trách nhiệm Chi tiết (Owner + Reviewer Model):

| Phase | Tuần | Deliverables Chính | Sinh viên 1 (Backend/Runtime Owner) | Sinh viên 2 (Contract/UI Owner) |
|:---|:---:|:---|:---|:---|
| **Phase 1** | **W1 — W2** | JSON API Spec, SQL Schema `tadss`, Solution Setup | Thiết kế DB Schema `tadss`, dựng Clean Architecture Solution .NET 8 | Đặc tả JSON Schema Request/Response, viết Integration Spec v1 |
| **Phase 2** | **W3 — W4** | DLL `Shared.DecisionContractSDK`, Unit Tests >80% | Lập trình Rich Domain Model `Reservation`, Invariants, Behavior Rules | Định nghĩa DTOs, Value Objects, Enums, viết bộ Unit Tests SDK |
| **Phase 3** | **W5 — W6** | Web API `DecisionRuntime.TADSS`, Swagger UI | Xây dựng Web API Core, Strategy Factory DI Lookup $O(1)$, Routing | Lập trình `DefaultStrategy` (100%) và `MockAlternativeStrategy` (30%/50%) |
| **Phase 4** | **W7 — W9** | Polly v8 Pipeline, MediatR Audit Trail, Cache | POC Spike Polly v8 (2 ngày đầu), MediatR in-process events bọc DB Transaction | Tích hợp Redis, chuẩn hóa JSON payload & băm SHA256 Request-Hash |
| **Phase 5** | **W10 — W12**| Override API, Dashboard UI, SmashCourt Integration | Override API (Idempotency, Concurrency), Feature Flag reload, SmashCourt Backend | Dựng Dashboard UI trên `smashcourt-fe`, cùng SV1 viết Chương 1 & 2 Báo cáo |
| **Phase 6** | **W13 — W14**| Integration Test Matrix (11 test cases), SLA Benchmark | Kịch bản Failure Injection, đo lường KPI (p95<150ms), Portability Mock | Kiểm thử tích hợp VNPay, đối soát bảng Test Matrix |
| **Phase 7** | **W15 — W16**| Báo cáo hoàn chỉnh (60 trang), Video Demo & Slide | Hoàn thiện Chương 3 & 4 Báo cáo, quay Video Demo, diễn tập thuyết trình | Hoàn thiện sơ đồ kiến trúc, làm Slide thuyết trình 20-25 slides |

---

## V. CÁC QUYẾT ĐỊNH KIẾN TRÚC KỸ THUẬT ĐÃ CHỐT (CORE ADR SUMMARY)

### 5.1. Luồng Quyết định & Phê duyệt (NeedApproval ➔ Block Payment)
```
DecisionResult.Status:
  ├── Approved      ──► Tiếp tục luồng đặt sân ──► Tạo giao dịch VNPay
  ├── NeedApproval  ──► Booking.Status = PendingDecisionApproval (TẠM CHẶN VNPAY)
  │                      └── Chờ Manager duyệt trên Dashboard:
  │                            ├── [Approve]  ──► Duyệt mức cọc đề xuất ──► Tiếp tục sang VNPay
  │                            └── [Override] ──► Sửa mức cọc (kèm Reason) ──► Tiếp tục sang VNPay
  └── Rejected      ──► Hủy Booking ──► Thông báo lý do từ chối
```

### 5.2. Chuỗi Chịu lỗi Polly v8 (Resilience SLA Budget)
```
┌── Outer Timeout: 500ms (Overall Request SLA Budget) ──────────┐
│                                                                │
│  Retry Policy (Tối đa 2 retries = 3 attempts, base delay 30ms) │
│   │                                                            │
│   └── Inner Timeout: 150ms per attempt                        │
│        └── Strategy.EvaluateAsync()                           │
│                                                                │
│  Circuit Breaker (Mở mạch khi tỷ lệ lỗi vượt ngưỡng trong    │
│   cửa sổ sampling 30 giây, tối thiểu 10 requests)              │
│                                                                │
│  Fallback: DefaultDecisionStrategy (thanh toán tĩnh 100%)      │
│   └── Overhead < 10ms sau khi failure được xác định           │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

### 5.3. Chuẩn hóa Payload Băm SHA-256 (Request-Hash Caching)
*   **Công thức băm:**
    $$\text{CacheKey} = \text{SHA256}(\text{strategyName} + \text{strategyVersion} + \text{normalizedDecisionRequest})$$
    Namespace Redis: `decision:{strategyName}:{strategyVersion}:{requestHash}`
*   **Quy trình 4 bước chuẩn hóa (Payload Normalization):**
    1. Sắp xếp các key trong JSON Payload theo thứ tự bảng chữ cái (Alphabetical sort).
    2. Chuyển đổi toàn bộ chuỗi định danh (UUID, CustomerId, ResourceId) về dạng chữ thường (lowercase).
    3. Làm tròn mốc thời gian đặt chỗ (`ReservationSchedule`) đến đơn vị giờ chẵn.
    4. Xóa bỏ toàn bộ khoảng trắng thừa (whitespace) và các trường có giá trị `null`.
*   **Chính sách TTL:**
    *   `DefaultDecisionStrategy` (Tĩnh): TTL = 15 phút.
    *   `AdaptiveDecisionStrategy` (Khóa luận): Vô hiệu hóa hoặc đặt TTL = 1 phút để tránh stale decisions.

### 5.4. Append-Only Audit Trail & Idempotency / Concurrency
*   **Bảng dữ liệu:** `tadss.decision_executions` lưu toàn bộ thông số thực thi.
*   **No Auto-Approve:** Quá hạn thời gian duyệt, booking giữ nguyên `PendingDecisionApproval` hoặc tự động hết hạn, tuyệt đối không tự động duyệt.
*   **Idempotency Key:** `UNIQUE(decision_id, idempotency_key)` ngăn chặn click đúp hoặc gửi lệnh duyệt trùng lặp.
*   **Optimistic Concurrency:** Cột `version` (kiểm tra `ExpectedVersion`) ngăn chặn xung đột khi 2 Manager cùng duyệt 1 đơn.

### 5.5. Phân luồng Nghiệp vụ tại Client (SmashCourt Business Routing)
*   **VIP/Loyal Fast-Track:** Khách hàng Gold/Platinum được SmashCourt chuyển thẳng sang VNPay, không gọi TADSS.
*   **Last-minute booking (< 24h):** Hạn chế trạng thái `NeedApproval` để tránh trễ giờ thi đấu.
*   **Peak/Off-Peak Routing:** Gọi `MockAlternativeDecisionStrategy` (30% giờ thường / 50% giờ cao điểm) để kiểm chứng Strategy Pattern.
*   **Emergency Valve:** Khi tắt cờ `EnableTadssDecision = false`, SmashCourt lập tức tự động áp dụng mức cọc tĩnh mặc định 100%, cô lập sự cố hoàn toàn.

---

## VI. TIÊU CHÍ THÀNH CÔNG & CHỈ SỐ THIẾT KẾ (DESIGN TARGETS)

> **Lưu ý:** Các chỉ số dưới đây là **Design Targets (Mục tiêu thiết kế kỹ thuật phần mềm)** được kiểm chứng qua các kịch bản kiểm thử, không phải cam kết môi trường production:

| Mã | Chỉ số Thiết kế | Mục tiêu Kiểm chứng | Phương pháp Đo lường |
|:---|:---|:---|:---|
| **SC1** | Thời gian xử lý thông thường | Latency p95 < 150ms | Đo lường các request thành công không qua cache và không retry. |
| **SC2** | Thời gian xử lý khi gặp sự cố | Overall Request SLA <= 500ms | Cưỡng chế bởi Polly Outer Timeout khi xảy ra retry/timeout. |
| **SC3** | Tính nhất quán của Audit Log | 100% kịch bản kiểm thử ghi nhận đúng log | Kiểm tra bảng `tadss.decision_executions` sau mỗi kịch bản test. |
| **SC4** | Cơ chế chống ghi trùng | Chặn 100% duplicate commands | Bắn liên tiếp 2 Override command cùng `IdempotencyKey`. |
| **SC5** | Tốc độ Reload Feature Flag | Hot reload dynamic dưới 2 giây | Thay đổi cấu hình trong `appsettings.json` qua `IOptionsMonitor`. |
| **SC6** | Tính độc lập tên miền (Portability) | Xử lý thành công request từ Coworking Space | Gửi mock request phòng họp qua Adapter mà không sửa code TADSS Core. |

---

## VII. ĐỊNH HƯỚNG PHÁT TRIỂN KHÓA LUẬN (THESIS EXTENSION)

Nền tảng TADSS sau khi hoàn thành ở Đồ án 2 sẽ được chuyển giao nguyên vẹn làm hạ tầng thực nghiệm cho Khóa luận tốt nghiệp (12 tuần):
1. **Mô hình Trọng số Thích ứng (Adaptive Trust Model):** Cài đặt hàm suy hao thời gian $e^{-\lambda \Delta t}$ tính điểm uy tín khách hàng dựa trên lịch sử bùng lịch/đúng hẹn.
2. **Mô hình Học máy CatBoost:** Huấn luyện trên tập dữ liệu tổng hợp (Synthetic Dataset 20,000+ records) dự đoán xác suất no-show. Mục tiêu thiết kế: AUC-ROC > 0.75, Precision > 0.70.
3. **Thực nghiệm Mô phỏng Đa tác tử (Multi-Agent Simulation):** Giả lập 1,000 khách hàng (Loyal, Regular, Risky, New) tương tác trong môi trường kinh doanh giả lập để chạy các nghiên cứu cắt giảm (Ablation Studies), kiểm chứng giả thuyết H1 và đánh giá độ công bằng Equal Opportunity.

---

*Tài liệu đã được khóa cứng đồng bộ 100% thông số kỹ thuật — Đóng vai trò làm Navigation Hub chính thức cho toàn bộ dự án.*
