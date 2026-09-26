# 🏛️ TỔNG QUAN KIẾN TRÚC & BẢN ĐỒ QUẢN TRỊ DỰ ÁN TADSS
## (TADSS System Blueprint, Architectural Workflows & Executive Documentation Map)

> **Dự án:** TADSS (Trustworthy Adaptive Decision Support System)  
> **Hệ thống đối chứng (Host App):** SmashCourt — Hệ thống Đặt Sân Cầu Lông Trực Tuyến  
> **Loại hình:** Hệ thống lai (Decoupled Decision Engine Subsystem + Web Application)  
> **Thời điểm cập nhật:** Hết Tuần 7 (Phase 4 — Milestone Resilience Complete)  
> **Trạng thái kiểm thử:** 🟢 **417/417 Automated Tests Passed (100% PASS)**

---

## 📑 MỤC LỤC
1. [Bối Cảnh & Động Lực Đề Tài (The Core Storyline)](#1-bối-cảnh--động-lực-đề-tài-the-core-storyline)
2. [Mô Hình Kiến Trúc Tổng Thể (System Architecture)](#2-mô-hình-kiến-trúc-tổng-thể-system-architecture)
3. [So Sánh Luồng Nghiệp Vụ Đặt Sân (Trước vs Sau khi có TADSS)](#3-so-sánh-luồng-nghiệp-vụ-đặt-sân-trước-vs-sau-khi-có-tadss)
4. [Vòng Đời Quyết Định & Cơ Chế Human-In-The-Loop](#4-vòng-đời-quyết-định--cơ-chế-human-in-the-loop)
5. [Sơ Đồ Pipeline Dự Án Dành Cho PM (5-Stage Delivery Pipeline)](#5-sơ-đồ-pipeline-dự-án-dành-cho-pm-5-stage-delivery-pipeline)
6. [Bảng Tổng Kết Quản Trị Dự Án (Executive Summary Dashboard)](#6-bảng-tổng-kết-quản-trị-dự-án-executive-summary-dashboard)
7. [Bản Đồ Mạng Lưới Toàn Bộ 23 File Tài Liệu Quyết Định](#7-bản-đồ-mạng-lưới-toàn-bộ-23-file-tài-liệu-quyết-định)
8. [Bảng Rà Soát & Chú Giải Chi Tiết Toàn Bộ Tài Liệu Trong Repo](#8-bảng-rà-soát--chú-giải-chi-tiết-toàn-bộ-tài-liệu-trong-repo)

---

## 1. BỐI CẢNH & ĐỘNG LỰC ĐỀ TÀI (THE CORE STORYLINE)

```text
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│ [ĐỒ ÁN 1: SMASHCOURT WEB APPLICATION]                                                   │
│ • Hoàn thiện luồng đặt sân trực tuyến và tích hợp cổng thanh toán VNPay.                │
│ • HẠN CHẾ CỐT LÕI: Chính sách cọc 100% bị HARDCODE cố định trong code CRUD nghiệp vụ.   │
│ • RỦI RO KỸ THUẬT: Khi muốn đổi chính sách hoặc gọi AI tính cọc động:                   │
│   1. Phải sửa code và deploy lại toàn bộ ứng dụng lớn (Tight Coupling).                │
│   2. Dịch vụ AI chậm/sập sẽ làm SẬP TOÀN BỘ luồng đặt sân (Single Point of Failure).   │
│   3. Thiếu vết lưu giải trình (Audit Trail) và cổng cho Quản lý can thiệp (No Override).│
└───────────────────────────────────────────┬─────────────────────────────────────────────┘
                                            │
                                            ▼
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│ [ĐỒ ÁN 2: NỀN TẢNG TADSS DECISION RUNTIME ENGINE]                                       │
│ • Tách riêng toàn bộ logic thẩm định thành ĐỘNG CƠ RA QUYẾT ĐỊNH ĐỘC LẬP.              │
│ • Cung cấp cơ chế "CẮM RÚT CHIẾN LƯỢC" (Pluggable Strategies qua IReservationDecisionStrategy).│
│ • Tích hợp CHUỖI CHỊU LỖI ĐA TẦNG POLLY v8 (Outer 500ms -> Retry -> CB -> Fallback 0ms)│
│   ==> Đảm bảo luồng đặt sân không bao giờ bị chết dù Strategy AI gặp sự cố.             │
│ • Ghi nhật ký kiểm toán bất biến (Append-Only Audit) & Cổng duyệt thủ công cho Quản lý. │
└─────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. MÔ HÌNH KIẾN TRÚC TỔNG THỂ (SYSTEM ARCHITECTURE)

Hệ thống sử dụng mô hình **Decoupled Decision Engine as a Subsystem** kết hợp kiến trúc **Ports & Adapters (Hexagonal Architecture)** và **Strategy Pattern**:

```mermaid
flowchart TD
    subgraph Layer1 ["🌐 LỚP 1: SMASHCOURT (Host / Consumer Web App)"]
        FE["🖥️ Frontend (Next.js)<br/>Khách đặt sân & Quản lý duyệt cọc"]
        BE["⚙️ Backend (.NET Core)<br/>Nghiệp vụ Đặt Sân & Thanh Toán"]
        VNPay["💳 Cổng VNPay<br/>Thanh toán trực tuyến"]
        FE <--> BE
        BE <--> VNPay
    end

    subgraph Boundary ["📦 RANH GIỚI HỢP ĐỒNG (Shared.DecisionContractSDK v1.0.0)"]
        SDK["📋 Contract SDK<br/>• Reservation Domain Model<br/>• DecisionRequestDto / ResultDto<br/>• IReservationDecisionStrategy"]
    end

    subgraph Layer2 ["⚡ LỚP 2: TADSS DECISION RUNTIME ENGINE (Trọng tâm Đồ án 2)"]
        API["🚪 TADSS Controller<br/>POST /api/v1/decisions & /override"]
        Resolver["🧭 Strategy Resolver<br/>DI-based O(1) Lookup"]
        Polly["🛡️ Polly v8 Pipeline<br/>Outer 500ms | Retry 2x | Inner 120ms | CB | Fallback"]
        Cache["💾 Resilient Cache<br/>L1 Memory ➔ L2 Redis"]
        Audit["📑 Append-Only Audit<br/>PostgreSQL (schema: tadss)"]
        
        API --> Resolver
        Resolver --> Polly
        API --> Cache
        API --> Audit
    end

    subgraph Layer3 ["🎯 LỚP 3: PLUGGABLE STRATEGIES (Các Chiến Lược Thẩm Định)"]
        S1["🔒 Default Strategy<br/>Cọc tĩnh 100%"]
        S2["⚡ Mock Strategy<br/>Cọc động 30% / 50%"]
        S3["🧠 AI / ML Models<br/>Dự đoán rủi ro hủy"]
    end

    BE == "1. Gửi Đơn Đặt Sân (HTTP REST)" ==> API
    API -. "Tuân thủ Contract" .-> SDK
    Polly ==> S1
    Polly ==> S2
    Polly -.-> S3
    API == "2. Trả Đề Xuất (Recommendation)" ==> BE

    classDef hostStyle fill:#e0f2fe,stroke:#0284c7,stroke-width:2px,color:#0f172a;
    classDef boundaryStyle fill:#fef3c7,stroke:#d97706,stroke-width:2px,color:#0f172a;
    classDef engineStyle fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#0f172a;
    classDef stratStyle fill:#f3e8ff,stroke:#9333ea,stroke-width:2px,color:#0f172a;

    class FE,BE,VNPay hostStyle;
    class SDK boundaryStyle;
    class API,Resolver,Polly,Cache,Audit engineStyle;
    class S1,S2,S3 stratStyle;
```

---

## 3. SO SÁNH LUỒNG NGHIỆP VỤ ĐẶT SÂN (TRƯỚC VS SAU KHI CÓ TADSS)

### 3.1. Luồng Trước Kia (Đồ Án 1 - Bị Hardcode Tĩnh)

```mermaid
flowchart LR
    A([Khách chọn sân]) --> B[SmashCourt Backend]
    B --> C[Hardcode cọc = 100%]
    C --> D[Chuyển sang VNPay]
    D --> E([Thanh toán hoàn tất])

    classDef legacyStyle fill:#f1f5f9,stroke:#64748b,stroke-width:2px,color:#0f172a;
    class A,B,C,D,E legacyStyle;
```

---

### 3.2. Luồng Hoạt Động Hiện Tại Khi Có TADSS Engine (Activity Diagram)

```mermaid
flowchart TD
    Start([1. Khách gửi yêu cầu đặt sân]) --> SC_Pending[SmashCourt tạo đơn PENDING]
    SC_Pending --> SendTADSS["2. SmashCourt gọi TADSS API<br/>POST /api/v1/decisions"]
    
    subgraph TADSS_Core ["BÊN TRONG TADSS DECISION RUNTIME ENGINE"]
        SendTADSS --> CheckCache{Kiểm tra Cache?}
        CheckCache -- HIT --> GetCached["Lấy kết quả từ Cache O(1)"]
        CheckCache -- MISS --> RunPolly["Thực thi qua Polly v8 Pipeline"]
        
        subgraph Polly_Engine ["Chuỗi Chịu Lỗi Polly v8"]
            RunPolly --> RunStrategy["Gọi Strategy.EvaluateAsync() (120ms)"]
            RunStrategy --> StratResult{Strategy xử lý?}
            StratResult -- Thành công --> StratOK["Tính cọc động thành công (30% / 50%)"]
            StratResult -- Lỗi / Treo --> RetryCheck{Còn lượt Retry?}
            RetryCheck -- Còn (tối đa 2 lần) --> RunStrategy
            RetryCheck -- Hết lượt / Circuit OPEN --> RunFallback["Kích hoạt Fallback 0ms<br/>Chuyển sang cọc an toàn 100%"]
        end
        
        StratOK --> LogAudit
        RunFallback --> LogAudit
        GetCached --> LogAudit[Ghi vết Audit bất biến vào PostgreSQL]
    end
    
    LogAudit --> ReturnResult["3. TADSS trả kết quả Recommendation"]
    ReturnResult --> CheckStatus{Trạng thái Quyết định?}
    
    CheckStatus -- APPROVED (Cọc 30%) --> GoVNPay["4A. Hiển thị mức cọc 30%<br/>Chuyển khách sang VNPay"]
    CheckStatus -- NEED_APPROVAL (Cọc 50-100%) --> HoldBooking["4B. Đơn ở trạng thái PENDING_APPROVAL<br/>Chờ Quản lý duyệt"]
    
    HoldBooking --> ManagerReview{"5. Quản lý duyệt đơn trên Dashboard?"}
    ManagerReview -- Đồng ý / Sửa cọc --> CallOverride["Gọi POST /decisions/{id}/override<br/>Ghi vết lý do duyệt"]
    CallOverride --> GoVNPay
    ManagerReview -- Từ chối / Hết hạn --> CancelBooking([Hủy đơn & giải phóng sân])
    
    GoVNPay --> PaySuccess([6. Khách thanh toán VNPay thành công ➔ Hoàn tất])

    classDef normalNode fill:#f8fafc,stroke:#475569,stroke-width:1.5px,color:#0f172a;
    classDef decisionNode fill:#fef3c7,stroke:#d97706,stroke-width:2px,color:#0f172a;
    classDef successNode fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#0f172a;
    classDef fallbackNode fill:#fee2e2,stroke:#dc2626,stroke-width:2px,color:#0f172a;

    class Start,SC_Pending,SendTADSS,GetCached,RunPolly,RunStrategy,LogAudit,ReturnResult,GoVNPay,HoldBooking,CallOverride normalNode;
    class CheckCache,StratResult,RetryCheck,CheckStatus,ManagerReview decisionNode;
    class StratOK,PaySuccess successNode;
    class RunFallback,CancelBooking fallbackNode;
```

---

### 3.3. Sơ Đồ Tuần Tự Xử Lý Sự Cố & Tự Động Fallback (Sequence Diagram)

```mermaid
sequenceDiagram
    autonumber
    actor User as Khách hàng
    participant SC as SmashCourt Backend
    participant TADSS as TADSS Decision Engine
    participant Strat as AI/Dynamic Strategy
    participant Fallback as Fallback Decision Strategy
    participant DB as PostgreSQL (tadss schema)
    actor Manager as Quản lý (Dashboard)

    User->>SC: Đặt sân (Khung giờ 19:00 - 21:00)
    SC->>TADSS: POST /api/v1/decisions (Dữ liệu đặt sân)
    
    activate TADSS
    TADSS->>Strat: EvaluateAsync() (Hạn 120ms)
    Note over Strat: Strategy bị treo / Exception 500!
    TADSS->>TADSS: Retry 2 lần (Tổng 420ms < 500ms SLA)
    TADSS->>Fallback: Kích hoạt Fallback an toàn (0ms)
    Fallback-->>TADSS: Trả về: NEED_APPROVAL (Cọc an toàn 100%)
    
    TADSS->>DB: Ghi nhật ký (Type: FALLBACK, Reason: STRATEGY_TIMEOUT)
    TADSS-->>SC: 200 OK (Status: NEED_APPROVAL, Deposit: 100%)
    deactivate TADSS
    
    SC-->>User: Thông báo: "Đơn đặt sân đang chờ Quản lý duyệt"
    
    Manager->>SC: Xem Dashboard -> Duyệt giảm cọc còn 50%
    SC->>TADSS: POST /api/v1/decisions/{id}/override (Ghi nhận lý do duyệt)
    TADSS->>DB: Cập nhật Audit (is_overridden = true, override_reason = "Khách quen")
    
    SC-->>User: Gửi link VNPay thanh toán 50% tiền cọc
    User->>SC: Thanh toán hoàn tất
```

---

## 4. VÒNG ĐỜI QUYẾT ĐỊNH & CƠ CHẾ HUMAN-IN-THE-LOOP

```mermaid
stateDiagram-v2
    [*] --> Evaluating: Nhận Request từ SmashCourt
    
    Evaluating --> Approved: Strategy chấp thuận tự động (Cọc 30%)
    Evaluating --> NeedApproval: Rủi ro / Giờ cao điểm / Fallback kích hoạt (Cọc 50-100%)
    Evaluating --> Rejected: Vi phạm chính sách / Gian lận
    
    NeedApproval --> Overridden: Quản lý duyệt / chỉnh cọc trên Dashboard
    NeedApproval --> Expired: Quá hạn duyệt / Không ai xử lý
    
    Approved --> Completed: Khách thanh toán VNPay thành công
    Overridden --> Completed: Khách thanh toán VNPay theo mức duyệt mới
    
    Completed --> [*]
    Rejected --> [*]
    Expired --> [*]
```

---

## 5. SƠ ĐỒ PIPELINE DỰ ÁN DÀNH CHO PM (5-STAGE DELIVERY PIPELINE)

```mermaid
flowchart TD
    D1["📄 GIAI ĐOẠN 1: PROPOSAL_DO_AN_2.md<br/>• Bối cảnh bài toán cọc động & Phân định 3 ranh giới kiến trúc"]
    D2["📄 GIAI ĐOẠN 2: API_CONTRACT_V1.md & CONTRACT_V1_FREEZE.md<br/>• Đặc tả Endpoints, Headers & Đóng băng DLL SDK v1.0.0"]
    D3["📄 GIAI ĐOẠN 3: RESILIENCE_AND_FAULT_TAXONOMY.md & ADRs<br/>• Polly v8 5 lớp, SLA 420ms/500ms & Ma trận tiêm lỗi F1-F7"]
    D4["📄 GIAI ĐOẠN 4: MASTER_EXECUTION_PLAN.md & WBS_MAPPING.md<br/>• Lộ trình 16 tuần chi tiết SV1/SV2 & Quản trị rủi ro"]
    D5["📄 GIAI ĐOẠN 5: EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md<br/>• Đo hiệu năng Cache (G0-G2) & Đo tỷ lệ chịu lỗi DCR (R0-R3)"]

    D1 ==>|1. Định hình hợp đồng| D2
    D2 ==>|2. Hiện thực hóa kiến trúc| D3
    D3 ==>|3. Phân rã kế hoạch thực thi| D4
    D4 ==>|4. Đo lường & Thực nghiệm khoa học| D5

    classDef pDone fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#0f172a;
    classDef pInProg fill:#fef3c7,stroke:#d97706,stroke-width:2px,color:#0f172a;
    classDef pNext fill:#f3e8ff,stroke:#9333ea,stroke-width:2px,color:#0f172a;

    class D1,D2 pDone;
    class D3,D4 pInProg;
    class D5 pNext;
```

---

## 6. BẢNG TỔNG KẾT QUẢN TRỊ DỰ ÁN (EXECUTIVE SUMMARY DASHBOARD)

| Giai đoạn (Phase) | Câu hỏi PM quan tâm | File tài liệu Single Source of Truth | Sản phẩm đầu ra cụ thể (Deliverables) | Trạng thái |
|:---|:---|:---|:---|:---:|
| **1. Đề xuất & Phạm vi** *(Why & Scope)* | Tại sao cần làm? Ranh giới hệ thống ở đâu? | `PROPOSAL_DO_AN_2.md` | Phân định ranh giới 3 lớp: SmashCourt (Host) $\leftrightarrow$ TADSS (Engine) $\leftrightarrow$ Strategies. | 🟢 **100% DONE** |
| **2. Giao diện & Hợp đồng** *(What & Contract)* | Hai bên gửi nhận dữ liệu gì? Làm sao không vỡ code nhau? | `API_CONTRACT_V1.md`<br/>`CONTRACT_V1_FREEZE.md` | DLL Class Library `Shared.DecisionContractSDK` v1.0.0 độc lập, đóng băng bất biến. | 🟢 **100% DONE** |
| **3. Kiến trúc & Chịu lỗi** *(How & Resilience)* | Chiến lược sập thì sao? SLA có bị lố 500ms không? | `RESILIENCE_AND_FAULT_TAXONOMY.md`<br/>`ARCHITECTURE_DECISION_RECORDS.md` | Polly v8 5 lớp, Ma trận tiêm lỗi 7 kịch bản (F1 ➔ F7), **417/417 tests passed**. | 🟢 **100% DONE (W7)** |
| **4. Kế hoạch Tác chiến** *(When & Execution)* | Chia việc thế nào? Kế hoạch ứng phó rủi ro là gì? | `MASTER_EXECUTION_PLAN.md`<br/>`PROJECT_WBS_MAPPING.md` | Kế hoạch 16 tuần chi tiết cho SV1 (Backend/Resilience) & SV2 (Cache/UI/Report) kèm 6 Milestones. | 🟡 **Đang chạy (W7 ➔ W8)** |
| **5. Đánh giá & Thực nghiệm** *(Metrics & Evidence)* | Lấy số liệu gì để viết báo cáo tốt nghiệp? | `EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md` | Kịch bản đo Cache ($G_0, G_1, G_2$), đo Chịu lỗi ($R_0 \rightarrow R_3$), Dataset Normalizer xuất CSV/JSONL. | 🔵 **Chuẩn bị (W8)** |

---

## 7. BẢN ĐỒ MẠNG LƯỚI TOÀN BỘ 23 FILE TÀI LIỆU QUYẾT ĐỊNH

```mermaid
flowchart TD
    subgraph G1 ["1. ĐỀ XUẤT, BỐI CẢNH & TỔNG QUAN (Vision & Foundation)"]
        F1["📄 README.md (Root Overview)"]
        F2["📄 PROPOSAL_DO_AN_2.md (Tuyên ngôn đề tài)"]
        F3["📄 THESIS_DEVELOPMENT_PLAYBOOK.md (Sổ tay Khóa luận)"]
    end

    subgraph G2 ["2. GIAO ƯỚC HỢP ĐỒNG & TÍCH HỢP (Contracts & SDK)"]
        F7["📄 API_CONTRACT_V1.md (REST API Spec)"]
        F8["🔒 CONTRACT_V1_FREEZE.md (Đóng băng SDK v1.0.0)"]
        F9["📄 INTEGRATION_SPEC.md (Đặc tả tích hợp SmashCourt)"]
        F10["📄 baseline_metrics.md & baseline_public_api.txt"]
        F11["📄 openapi-v1.yaml & schemas/ (Swagger Schemas)"]
        F12["📄 Shared.DecisionContractSDK/README.md"]
    end

    subgraph G3 ["3. THIẾT KẾ KIẾN TRÚC & QUYẾT ĐỊNH (Architecture & ADRs)"]
        F13["📄 SOFTWARE_ARCHITECTURE_SPECIFICATION.md"]
        F14["📄 ARCHITECTURE_DECISION_RECORDS.md (ADR-001..005)"]
    end

    subgraph G4 ["4. CHỊU LỖI, LƯU TRỮ & DỮ LIỆU (Resilience, Audit & DB)"]
        F15["📄 RESILIENCE_AND_FAULT_TAXONOMY.md (Ma trận F1-F7)"]
        F16["📄 DECISION_PERSISTENCE_INVARIANTS.md (Audit Invariants)"]
        F17["📄 schema_smashcourt.sql (DDL Database)"]
    end

    subgraph G5 ["5. KẾ HOẠCH TÁC CHIẾN & BACKLOG (Planning & Roadmap)"]
        F4["📄 MASTER_EXECUTION_PLAN.md (Kế hoạch 16 Tuần)"]
        F5["📄 PROJECT_WBS_MAPPING.md (WBS & Quản trị rủi ro)"]
        F6["📄 BACKLOG_SPRINTS.md (Danh mục 6 Sprints)"]
    end

    subgraph G6 ["6. THỰC NGHIỆM KHOA HỌC & BẢO VỆ (Research & Evidence)"]
        F18["📄 EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md"]
        F19["📄 RESEARCH_PROBLEM_BACKLOG.md (RQ1, RQ2, RQ3)"]
        F20["📄 RESEARCH_THESIS_DEFENSE.md (Luận điểm bảo vệ)"]
    end

    F2 -->|1. Sinh yêu cầu hợp đồng| F7
    F7 -->|2. Đóng băng bất biến| F8
    F2 -->|3. Định hướng kiến trúc| F13
    F13 -->|4. Bảo chứng bằng 5 ADRs| F14
    F13 -->|5. Thiết lập chuỗi chịu lỗi| F15
    F15 -->|6. Ràng buộc lưu trữ| F16
    F7 & F13 -->|7. Lập kế hoạch thực thi| F4
    F4 -->|8. Phân rã rủi ro| F5
    F15 & F16 -->|9. Cung cấp kịch bản đo đạc| F18
    F18 -->|10. Luận điểm bảo vệ khóa luận| F20

    classDef c1 fill:#e0f2fe,stroke:#0284c7,stroke-width:2px,color:#0f172a;
    classDef c2 fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#0f172a;
    classDef c3 fill:#fef3c7,stroke:#d97706,stroke-width:2px,color:#0f172a;
    classDef c4 fill:#ffe4e6,stroke:#e11d48,stroke-width:2px,color:#0f172a;
    classDef c5 fill:#f3e8ff,stroke:#9333ea,stroke-width:2px,color:#0f172a;
    classDef c6 fill:#f1f5f9,stroke:#475569,stroke-width:2px,color:#0f172a;

    class F1,F2,F3 c1;
    class F7,F8,F9,F10,F11,F12 c2;
    class F13,F14 c3;
    class F15,F16,F17 c4;
    class F4,F5,F6 c5;
    class F18,F19,F20 c6;
```

---

## 8. BẢNG RÀ SOÁT & CHÚ GIẢI CHI TIẾT TOÀN BỘ TÀI LIỆU TRONG REPO

Dưới đây là bảng rà soát đầy đủ **100% tài liệu** hiện có trong repository, giải thích rõ **nội dung file nói cái gì** và **mối quan hệ ràng buộc/kết nối với các tài liệu khác**:

| STT | Tên File Tài Liệu | Nội dung cốt lõi (File này nói cái gì?) | Mối quan hệ liên kết với các file khác |
|:---:|:---|:---|:---|
| **1** | `README.md` (Root) | Tổng quan toàn bộ Solution TADSS-Runtime: Cấu trúc các project C#, hướng dẫn cài đặt, build và chạy bộ 417+ automated tests. | **Cổng vào của toàn bộ repository.** Giới thiệu tổng quát và dẫn dắt lập trình viên tới các tài liệu chi tiết. |
| **2** | `PROPOSAL_DO_AN_2.md` | Tuyên ngôn đề tài: Xác lập bối cảnh, 3 thách thức kỹ thuật (Coupling, SPOF, Lack of Audit), ranh giới 3 lớp kiến trúc và mục tiêu đồ án. | **Gốc rễ của toàn dự án.** Sinh ra các yêu cầu cho Contract (`API_CONTRACT_V1.md`) và Kiến trúc (`SOFTWARE_ARCHITECTURE_SPECIFICATION.md`). |
| **3** | `THESIS_DEVELOPMENT_PLAYBOOK.md` | Sổ tay định hướng phát triển Khóa luận tốt nghiệp: Quy định cách tái sử dụng kết quả kỹ thuật của Đồ án 2 vào cấu trúc các chương luận văn. | Kết nối trực tiếp giữa phần mềm thực tế (`TADSS-Runtime`) và các câu hỏi nghiên cứu (`RESEARCH_PROBLEM_BACKLOG.md`). |
| **4** | `MASTER_EXECUTION_PLAN.md` | Kế hoạch tác chiến 16 tuần: Phân rã chi tiết công việc từng ngày cho Sinh viên 1 (Backend/Resilience) và Sinh viên 2 (Strategy/Cache/UI/Report). | Định hướng lịch trình thực thi dựa trên các yêu cầu từ `PROPOSAL_DO_AN_2.md` và `API_CONTRACT_V1.md`. |
| **5** | `PROJECT_WBS_MAPPING.md` | Bảng ma trận phân rã công việc (WBS), ánh xạ sản phẩm bàn giao (Deliverables) qua 6 Milestones và 6 kịch bản rủi ro kèm giải pháp dự phòng. | Đi kèm `MASTER_EXECUTION_PLAN.md` để quản trị rủi ro tiến độ và kích hoạt kịch bản dự phòng khi tắc nghẽn. |
| **6** | `BACKLOG_SPRINTS.md` | Danh mục Product Backlog chia thành 6 Sprints: Định nghĩa chi tiết các User Stories, Tasks và tiêu chí nghiệm thu (Acceptance Criteria). | Cụ thể hóa `MASTER_EXECUTION_PLAN.md` thành các backlog task để quản lý tiến độ kiểu Agile/Scrum. |
| **7** | `API_CONTRACT_V1.md` | Đặc tả chi tiết chuẩn giao tiếp HTTP REST v1.0.0: Định nghĩa URLs, HTTP Headers (`X-Idempotency-Key`), JSON DTOs, Error Codes và Reason Codes. | Hiện thực hóa yêu cầu từ `PROPOSAL_DO_AN_2.md` thành chuẩn kỹ thuật bắt buộc cho cả SmashCourt và TADSS. |
| **8** | `CONTRACT_V1_FREEZE.md` | Tuyên bố đóng băng DLL SDK `Shared.DecisionContractSDK` v1.0.0 theo nguyên tắc Zero-Mutation (khóa cứng checksum, không bên nào được tự ý sửa). | Bảo vệ tính ổn định của `API_CONTRACT_V1.md` để 2 bên phát triển song song mà không sợ bị vỡ code nhau. |
| **9** | `INTEGRATION_SPEC.md` | Hướng dẫn kỹ thuật tích hợp đầu-cuối: Mô tả chi tiết cách SmashCourt Backend gửi đơn sang TADSS (`POST /api/v1/decisions`) và duyệt cọc (`POST /override`). | Cầu nối thực hành giữa `SmashCourt-BE` và `API_CONTRACT_V1.md`. |
| **10** | `baseline_metrics.md` | Báo cáo đo lường độ ổn định của hợp đồng SDK: Ghi nhận 0 breaking changes, độ bao phủ kiểm thử 96.8% và tính thuần khiết kiến trúc. | Minh chứng kiểm chứng chất lượng cho cam kết đóng băng trong `CONTRACT_V1_FREEZE.md`. |
| **11** | `baseline_public_api.txt` | Danh sách chữ ký kỹ thuật của toàn bộ 24 Public Types xuất bản từ `Shared.DecisionContractSDK.dll`. | File tham chiếu cho unit test `PublicApiSurfaceTests.cs` để tự động báo lỗi nếu có ai vô tình sửa interface SDK. |
| **12** | `openapi-v1.yaml` & `schemas/` | Đặc tả máy đọc chuẩn OpenAPI 3.0 và 7 file JSON Schema chi tiết (`decision-request`, `decision-result`, `override...`). | Tương thích 100% với `API_CONTRACT_V1.md`, dùng tạo Swagger UI và chạy kiểm thử Serialization tự động. |
| **13** | `src/Shared.DecisionContractSDK/README.md` | Hướng dẫn lập trình và cài đặt thư viện SDK: Dành cho lập trình viên SmashCourt (phía gọi) và lập trình viên Strategy (phía viết thuật toán). | Tài liệu hướng dẫn sử dụng trực tiếp cho mã nguồn `Shared.DecisionContractSDK`. |
| **14** | `SOFTWARE_ARCHITECTURE_SPECIFICATION.md` | Bản thiết kế kiến trúc chi tiết: Cấu trúc Clean/Hexagonal, cơ chế Dependency Injection Strategy Resolver $O(1)$ và luồng Middleware. | Hiện thực hóa các quyết định từ nhóm ADR thành kiến trúc mã nguồn C# thực tế. |
| **15** | `ARCHITECTURE_DECISION_RECORDS.md` | Tổng hợp 5 bản ghi quyết định kiến trúc then chốt (ADR-001 ➔ ADR-005) kèm bối cảnh, phương án so sánh và hệ quả. | Trả lời câu hỏi *"Tại sao chọn công nghệ này?"* để phục vụ giải trình trước Hội đồng phản biện. |
| **16** | `ADR-001` đến `ADR-005` (5 files) | Chi tiết từng quyết định: ADR-001 (Strategy Pattern), ADR-002 (SDK-first), ADR-003 (Polly v8), ADR-004 (Rich Domain), ADR-005 (CatBoost ML). | Các nhánh con chi tiết cấu thành nên `ARCHITECTURE_DECISION_RECORDS.md`. |
| **17** | `RESILIENCE_AND_FAULT_TAXONOMY.md` | **Sổ tay chịu lỗi cốt lõi:** Phân loại 3 miền lỗi, cấu hình chuỗi Polly v8 5 lớp, ngân sách SLA (420ms/500ms) và ma trận sự cố F1 ➔ F7. | **Kim chỉ nam cho tầng Resilience.** Làm cơ sở xây dựng bộ test `FaultInjectionTests.cs` (PASS 100%). |
| **18** | `DECISION_PERSISTENCE_INVARIANTS.md` | Các quy tắc bất biến khi lưu trữ Audit Ledger: Dữ liệu quyết định do máy sinh là bất biến; Quản lý chỉ ghi đè metadata kèm Optimistic Locking. | Bảo vệ tính toàn vẹn dữ liệu cho bảng `tadss.decision_executions` trong cơ sở dữ liệu. |
| **19** | `schema_smashcourt.sql` | Toàn bộ script DDL khởi tạo cơ sở dữ liệu PostgreSQL gồm các bảng nghiệp vụ SmashCourt (schema `public`) và bảng audit (schema `tadss`). | Hiện thực hóa vật lý các quy định lưu trữ trong `DECISION_PERSISTENCE_INVARIANTS.md`. |
| **20** | `EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md` | **Phương pháp thực nghiệm khoa học:** Quy định nhóm đo Cache ($G_0..G_2$), nhóm đo Chịu lỗi ($R_0..R_3$), công thức $DCR$ và chuẩn hóa Research Dataset. | Dẫn xuất từ `RESILIENCE_AND_FAULT_TAXONOMY.md` để phục vụ đo đạc hiệu năng trong Tuần 8-9 và Chương 4 Khóa luận. |
| **21** | `RESEARCH_PROBLEM_BACKLOG.md` | Danh mục 3 bài toán nghiên cứu cốt lõi: RQ1 (Decision Continuity), RQ2 (Tail Latency Envelope), RQ3 (Audit Provenance) kèm các giả thuyết. | Định hướng học thuật cho các thí nghiệm trong `EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`. |
| **22** | `RESEARCH_THESIS_DEFENSE.md` | Tổng hợp các luận điểm bảo vệ khóa luận: Chuyển đổi các kết quả phần mềm thành các đóng góp khoa học để phản biện trước Hội đồng. | Kết tinh từ toàn bộ kết quả của `PROPOSAL_DO_AN_2.md`, `RESILIENCE_AND_FAULT_TAXONOMY.md` và thực nghiệm. |
| **23** | `PROJECT_BLUEPRINT_AND_SYSTEM_OVERVIEW.md` | **Bản đồ tổng thể toàn dự án:** Tích hợp toàn bộ mô hình kiến trúc, luồng hoạt động, state machine, pipeline PM và bản đồ tài liệu. | **Tài liệu Single Source of Truth phục vụ báo cáo và tổng quan dự án.** |

---

*Tài liệu này được lưu trữ tập trung tại: `docs/PROJECT_BLUEPRINT_AND_SYSTEM_OVERVIEW.md`.*
