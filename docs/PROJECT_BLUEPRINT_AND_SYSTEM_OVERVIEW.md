# TỔNG QUAN HỆ THỐNG TADSS (SYSTEM BLUEPRINT) — v2.0

> **Vai trò của tài liệu:** bức tranh tổng thể và các sơ đồ dùng cho báo cáo/slide. Thông số chi tiết nằm ở các file sở hữu (xem bản đồ tài liệu tại Mục III của [`THESIS_DEVELOPMENT_PLAYBOOK.md`](planning/THESIS_DEVELOPMENT_PLAYBOOK.md)).
>
> **Trạng thái (12/10/2026):** Phase 1–4 hoàn thành; Phase 5 (W05–W08, 05/10 – 01/11; bắt đầu thực tế 02/10) đã qua bước 0 (rà soát và điều chỉnh kế hoạch, Gate MR 01/10) và xong các gói 5.1–5.5 và phần mã của 5.6 — contract v2 là phiên bản duy nhất (năm endpoint `/api/v2/decisions`: đánh giá, chi tiết, hàng chờ, override, outcome; audit PostgreSQL bất biến; API key, rate limit, health live/ready; Docker Compose + ngrok; kiểm thử trên Testcontainers; định tuyến strategy + shadow mode; OpenTelemetry + Grafana; chế độ đánh giá chạy bằng một lệnh `docker compose up -d --build`; harness replay và adapter dữ liệu khách sạn, bản nhanh `EV-20261011-EVAL-quick-r1` đạt mọi tiêu chí áp dụng được). Gói 5.7: phần mã và tài liệu đã xong (12/10/2026) — contract v2.0.0 đóng băng ngày 11/10/2026 trước lượt đánh giá đầy đủ, tài liệu có kiểm tự động DOC-1…DOC-4; còn chờ người dùng chạy lượt đánh giá đầy đủ, lưu Evidence Pack và báo cáo Gate M5. Kế hoạch: [`MASTER_EXECUTION_PLAN.md`](planning/MASTER_EXECUTION_PLAN.md).

---

## 1. CÂU CHUYỆN ĐỀ TÀI

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ ĐỒ ÁN 1 — SMASHCOURT                                                         │
│ Đặt sân online + VNPay. Chính sách duy nhất: thanh toán 100% cho mọi đơn.    │
│ Muốn chính sách linh hoạt thì phải sửa mã CRUD → coupling, lỗi lan truyền,   │
│ không có vết giải trình, quản lý không can thiệp được.                       │
└───────────────────────────────────────┬──────────────────────────────────────┘
                                        ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│ ĐỒ ÁN 2 — TADSS DECISION RUNTIME                                             │
│ Tách quyết định cọc ra runtime độc lập: định tuyến strategy theo request,    │
│ pipeline chịu lỗi, audit bất biến, Manager duyệt kèm mức cọc.                │
│ Kiểm chứng trên hạ tầng thật + đánh giá bằng dữ liệu import.                 │
└───────────────────────────────────────┬──────────────────────────────────────┘
                                        ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│ KHÓA LUẬN — STRATEGY TADS                                                    │
│ Điểm tin cậy + mô hình rủi ro chạy trên TADSS (shadow mode, off-policy).      │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. KIẾN TRÚC TỔNG THỂ

```mermaid
flowchart TD
    subgraph L1["Lớp 1 — SmashCourt"]
        FE["smashcourt-fe<br/>Khách đặt sân · Manager duyệt"]
        BE["SmashCourt-BE<br/>Booking, cọc, VNPay, job"]
        VN["VNPay"]
        FE <--> BE
        BE <--> VN
    end
    subgraph SDK["Hợp đồng — Decision Contract v2"]
        C["DTO v2 · Reservation · IReservationDecisionStrategy"]
    end
    subgraph L2["Lớp 2 — TADSS Runtime"]
        API["API /api/v2/decisions<br/>override · outcome · pending"]
        R["StrategyRouter + Shadow"]
        P["Polly v8 pipeline"]
        CA["Cache (Redis)"]
        AU["Audit bất biến (PostgreSQL schema tadss)"]
        API --> R --> P
        API --> CA
        API --> AU
    end
    subgraph L3["Strategies"]
        S1["DefaultDecisionStrategy<br/>cọc theo hạng"]
        S2["MockAlternativeDecisionStrategy<br/>cọc theo khung giờ"]
        S3["TADS (Khóa luận)"]
    end
    OT["OpenTelemetry → Grafana"]
    BE == "POST /api/v2/decisions" ==> API
    API -. tuân thủ .-> C
    P --> S1
    P --> S2
    P -.-> S3
    API -.-> OT
```

---

## 3. LUỒNG ĐẶT SÂN: TRƯỚC VÀ SAU

### 3.1. Đồ án 1

```mermaid
flowchart LR
    A([Khách chọn sân]) --> B[SmashCourt-BE] --> C[Thanh toán 100%] --> D[VNPay] --> E([Hoàn tất])
```

### 3.2. Đồ án 2

```mermaid
flowchart TD
    Start([Khách đặt sân]) --> Flag{EnableTadssDecision?}
    Flag -- tắt --> Full[Cọc 100%]
    Flag -- bật --> Call[Gọi TADSS]
    Call -- lỗi / timeout --> Full
    Call -- APPROVED --> Dep[Cọc theo số tiền TADSS trả]
    Call -- "APPROVED (fallback khi strategy lỗi)" --> Full
    Call -- NEED_APPROVAL --> Wait[Đơn chờ duyệt, giữ slot]
    Call -- REJECTED --> Cancel([Hủy đơn])
    Wait --> M{Manager}
    M -- duyệt + mức cọc --> Dep
    M -- từ chối --> Cancel
    M -- hết hạn --> Cancel
    Dep --> Pay[VNPay] --> Done([Booking xác nhận; thu phần còn lại khi checkout])
    Full --> Pay
```

---

## 4. VÒNG ĐỜI QUYẾT ĐỊNH

```mermaid
stateDiagram-v2
    [*] --> Evaluating: Nhận request
    Evaluating --> Approved: Strategy xác định được mức cọc
    Evaluating --> Approved: Fallback khi strategy lỗi (cọc 100%)
    Evaluating --> NeedApproval: Nghiệp vụ cần xem xét
    Evaluating --> Rejected: Chính sách từ chối
    NeedApproval --> Approved: Không còn cửa sổ duyệt (cọc 100%)
    NeedApproval --> ManagerApproved: Manager duyệt + mức cọc
    NeedApproval --> ManagerRejected: Manager từ chối
    NeedApproval --> Expired: Hết hạn duyệt (không tự duyệt)
    Approved --> Outcome: SmashCourt báo kết quả
    ManagerApproved --> Outcome
    ManagerRejected --> Outcome
    Expired --> Outcome
    Rejected --> Outcome
    Outcome --> [*]
```

Mọi chuyển trạng thái được ghi bằng bản ghi mới (quyết định, override, outcome); không bản ghi nào bị sửa.

---

## 5. TRẠNG THÁI DỰ ÁN

| Hạng mục | Trạng thái |
| :--- | :--- |
| Contract SDK v1, Runtime v1, pipeline chịu lỗi, audit đồng bộ, 510 test tại M4 (InMemory; còn 500 sau bước rà soát đầu Phase 5) | ✅ Phase 1–4 |
| Đồng bộ tài liệu, dọn repo | ✅ Phase 5, bước 0 (Gate MR 01/10) |
| Contract v2, audit bất biến, routing/shadow, toàn bộ hạ tầng & danh mục kiểm thử | ⏳ Phase 5 (W05–W08, 05/10 – 01/11; bắt đầu thực tế 02/10): xong các gói 5.1–5.5 và phần mã của 5.6; gói 5.7: contract v2.0.0 đã đóng băng ngày 11/10/2026 (trước lượt đánh giá đầy đủ), DOC-1…4 xong; lượt đánh giá đầy đủ và Gate M5 đang chờ |
| SmashCourt-BE ∥ smashcourt-fe | ⏳ Phase 6 (02/11 – 29/11) |
| Tích hợp, kiểm thử & sửa lỗi, đánh giá bằng dữ liệu import, Evidence Pack | ⏳ Phase 7–8 (30/11 – 20/12) |
| Báo cáo & bảo vệ | ⏳ Phase 9 (21/12 – 26/12/2026) |
