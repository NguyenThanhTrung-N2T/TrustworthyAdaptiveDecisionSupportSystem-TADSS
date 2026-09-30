# ĐỀ XUẤT ĐỒ ÁN 2 (PROJECT PROPOSAL)

## THIẾT KẾ VÀ XÂY DỰNG NỀN TẢNG RUNTIME ENGINE RA QUYẾT ĐỊNH CHỊU LỖI CHO HỆ THỐNG ĐẶT CHỖ DỊCH VỤ

**Tên tiếng Anh:** *Design and Development of a Fault-Tolerant Decision Runtime Engine for Service Reservation Systems*

---

|  | Thông tin |
|--|----------|
| **Sinh viên thực hiện** | *(Điền tên)* |
| **MSSV** | *(Điền MSSV)* |
| **Giảng viên hướng dẫn** | *(Điền tên GVHD)* |
| **Khoa / Ngành** | Công nghệ Thông tin / Kỹ thuật Phần mềm |
| **Hệ thống đối chứng** | SmashCourt — Hệ thống đặt sân cầu lông trực tuyến (Đồ án 1) |
| **Sản phẩm chính** | **TADSS** (Trustworthy Adaptive Decision Support System) — Nền tảng Runtime Engine |

> [!NOTE]
> **Định danh hệ thống:** Trong phạm vi đề tài, **TADSS (Trustworthy Adaptive Decision Support System)** được sử dụng làm tên định danh chính thức của nền tảng runtime thực thi và kiểm soát các chiến lược quyết định (Decision Strategies). Trong phạm vi Đồ án 2, **"Trustworthy"** được hiểu theo khía cạnh operational trustworthiness của runtime (bao gồm khả năng chịu lỗi, fallback, auditability, kiểm soát phê duyệt thủ công, tính nhất quán khi thực thi và chống ghi đè/gọi trùng); các khía cạnh algorithmic fairness và trustworthy AI thuộc phạm vi Khóa luận tốt nghiệp.
>
> **📌 Ghi chú Quản trị Lịch sử (Historical Proposal Snapshot):** Văn bản này là bản Đề cương Đề xuất ban đầu (Initial Approved Proposal) nộp đầu kỳ. Trong quá trình hiện thực hóa thực tế ở Phase 3–4 (xem [`RESILIENCE_AND_FAULT_TAXONOMY.md`](../architecture/RESILIENCE_AND_FAULT_TAXONOMY.md), [`DECISION_PERSISTENCE_INVARIANTS.md`](../architecture/DECISION_PERSISTENCE_INVARIANTS.md) và [`MASTER_EXECUTION_PLAN.md`](./MASTER_EXECUTION_PLAN.md)), kiến trúc đã được chuẩn hóa ở 4 điểm: (1) `InnerTimeoutMs` được chuẩn hóa ở mức `120ms` ($3 \times 120\text{ms} + 2 \times 30\text{ms} = 420\text{ms} < 500\text{ms}$ `OuterTimeoutMs`); (2) Tách bạch `DefaultDecisionStrategy` (Conservative Baseline Business Policy: phân hạng khách hàng kết hợp mức thanh toán 100% mặc định khi không đạt ngưỡng ưu đãi) với `FallbackDecisionStrategy` (cơ chế an toàn hạ tầng trả về `NEED_APPROVAL` kèm khuyến nghị thanh toán 100%); (3) Ghi nhận Append-Only Audit Trail đồng bộ (Fail-Closed) qua `MediatR` in-process events (`INotification` / `INotificationHandler`) nằm trong cùng PostgreSQL transaction boundary; và (4) Định dạng Cache Key chuẩn hóa thành `decision:hash:{requestHash}:{strategyName}:v{strategyVersion}` với cấu hình baseline `TtlHours = 24`.

---

## 1. GIỚI THIỆU & BỐI CẢNH (INTRODUCTION & BACKGROUND)

Trong các hệ thống đặt chỗ dịch vụ theo thời gian thực (Service Reservation Systems) như đặt sân thể thao, phòng họp, phòng khám hay khách sạn, các quyết định về chính sách vận hành đóng vai trò sống còn đối với hiệu quả kinh doanh. Các quyết định này bao gồm: xác định tỷ lệ đặt cọc, giới hạn số lần hủy lịch, hay ưu tiên phân bổ tài nguyên giờ cao điểm. Trong phạm vi Đồ án 2, **TADSS (Trustworthy Adaptive Decision Support System)** đóng vai trò là một cơ sở hạ tầng runtime thực thi quyết định (Decision Runtime Infrastructure) độc lập; các chiến lược như chính sách đặt cọc động hay phân phối tài nguyên chỉ là các chiến lược cụ thể (Strategies) được cấu hình và chạy trên nền tảng này.

Trong giai đoạn xây dựng hệ thống **SmashCourt** (Đồ án 1), chúng ta đã hoàn thiện luồng đặt sân trực tuyến tích hợp thanh toán VNPay và hệ thống chatbot FAQ hỗ trợ khách hàng. Tuy nhiên, luồng nghiệp vụ này hiện đang áp dụng một chính sách tĩnh duy nhất: yêu cầu thanh toán cố định 100% đối với toàn bộ khách hàng và toàn bộ khung giờ. Đây chỉ là một chiến lược quyết định cụ thể của case study; về mặt hệ thống, SmashCourt thiếu một nền tảng thực thi quyết định tổng quát. 

Thực tế vận hành doanh nghiệp đòi hỏi các chính sách quyết định phải linh hoạt và thích ứng (adaptive). Ví dụ, cần áp dụng mức cọc cao hơn với khách hàng có tỷ lệ bùng hẹn cao, hoặc giảm cọc đối với khách hàng thân thiết đặt sân giờ thấp điểm. Tuy nhiên, việc tích hợp trực tiếp các thuật toán quyết định thay đổi liên tục này vào hệ thống nghiệp vụ SmashCourt sẽ dẫn đến các rủi ro lớn về mặt kỹ thuật phần mềm: nghẽn hệ thống do thuật toán xử lý chậm, sập toàn bộ luồng thanh toán nếu dịch vụ quyết định gặp sự cố, và thiếu khả năng kiểm tra vết (audit trail) hay ghi đè khẩn cấp (override) từ nhà quản lý.

Do đó, việc nghiên cứu thiết kế và xây dựng một **nền tảng runtime engine độc lập, chịu lỗi tốt (fault-tolerant)** là bắt buộc để làm môi trường trung gian an toàn chạy các chiến lược quyết định nghiệp vụ.

---

## 2. PHÁT BIỂU VẤN ĐỀ (PROBLEM STATEMENT)

Các hệ thống đặt chỗ dịch vụ hiện tại khi cố gắng áp dụng các cơ chế ra quyết định động thường đối mặt với 3 thách thức kỹ thuật phần mềm cốt lõi (Technical Challenges):

1. **Tight Coupling (Ràng buộc chặt nghiệp vụ và logic quyết định):** Logic tính toán các quyết định nghiệp vụ (như chính sách cọc và hủy lịch) bị nhúng trực tiếp vào mã nguồn nghiệp vụ CRUD. Mỗi khi thay đổi chiến lược ra quyết định hoặc thay đổi phiên bản thuật toán, lập trình viên buộc phải sửa code và deploy lại toàn bộ ứng dụng lớn.
2. **Single Point of Failure (Sự cố lan truyền):** Các mô hình dự đoán rủi ro hoặc API tính toán bên ngoài có độ trễ lớn (p95 > 500ms) hoặc dễ gặp sự cố gián đoạn. Nếu không có cơ chế chịu lỗi cô lập, khi dịch vụ quyết định bị chậm hoặc sập, toàn bộ luồng thanh toán và đặt chỗ của khách hàng sẽ bị sập theo.
3. **Lack of Auditability & Human Control (Thiếu vết lưu và kiểm soát):** Không có cơ chế lưu vết đồng bộ (synchronous audit log) mô tả tại sao hệ thống lại đưa ra kết quả quyết định đó, và không có cổng API cho phép con người (Manager) duyệt thủ công hoặc ghi đè (Override) thông số quyết định một cách an toàn mà vẫn giữ nguyên vết quyết định gốc.

---

## 3. MỤC TIÊU ĐỒ ÁN 2 (OBJECTIVES)

### 3.1. Mục tiêu tổng quát
Thiết kế và xây dựng **TADSS (Trustworthy Adaptive Decision Support System)** — một nền tảng runtime engine độc lập hỗ trợ thực thi và kiểm soát các chiến lược ra quyết định. TADSS hoàn toàn độc lập với các thuật toán quyết định cụ thể; các chiến lược được trừu tượng hóa thông qua giao diện hợp đồng dùng chung (`IReservationDecisionStrategy`) và có thể thay thế ở mức cấu hình/triển khai mà không thay đổi lõi runtime core.

### 3.2. Mục tiêu cụ thể
Để đạt được mục tiêu tổng quát, Đồ án 2 tập trung hoàn thành các mục tiêu kỹ thuật cụ thể sau:
* **O1 — Decision Contract SDK:** Thiết kế thư viện hợp đồng dùng chung chứa Domain Model chuẩn hóa (`Reservation` Canonical Model), interfaces chiến lược, các enums và DTOs đầu ra được cấu trúc chặt chẽ.
* **O2 — Decision Runtime API:** Xây dựng Web API độc lập chịu trách nhiệm nhận yêu cầu thẩm định, định tuyến đến chiến lược tương ứng thông qua Strategy Factory sử dụng cơ chế DI-based Strategy Resolution (O(1) DI registration lookup) và trả kết quả chuẩn hóa dưới dạng Recommendation.
* **O3 — Polly v8 Resilience Pipeline:** Tích hợp chuỗi chịu lỗi lồng nhau (Nested Policies) bao gồm: Outer Timeout kiểm soát SLA toàn cục ➔ Retry ➔ Inner Timeout từng lần thử ➔ Circuit Breaker ➔ Fallback sang kết quả quyết định mặc định (như thanh toán tĩnh 100% trong case study SmashCourt).
* **O4 — Append-only Audit Trail:** Thiết lập hệ thống ghi vết quyết định đồng bộ thông qua MediatR in-process events nằm trong transaction boundary của PostgreSQL, phân biệt rõ các loại thực thi.
* **O5 — Human-in-the-loop Override API:** Xây dựng API phê duyệt và ghi đè kết quả quyết định dành cho Manager, tích hợp cơ chế chống ghi trùng (`IdempotencyKey`) và chống ghi đè đồng thời (`ExpectedVersion`).
* **O6 — Feature Flag Integration:** Triển khai Feature Flag (`EnableTadssDecision`) tích hợp cổng reload động (`IOptionsMonitor`) giúp SmashCourt bật/tắt luồng gọi TADSS tức thời khi cần thiết.
* **O7 — SmashCourt Case Study Integration:** Thực hiện tích hợp đầu cuối giữa ứng dụng SmashCourt và TADSS Core để chứng minh khả năng vận hành thực tế.

---

## 4. RANH GIỚI KIẾN TRÚC & PHÂN LỚP DỰ ÁN

Để đảm bảo tính độc lập tuyệt đối giữa các học phần, ranh giới dự án được phân chia thành 3 lớp riêng biệt như sau:

```text
Lớp 3: Chiến lược nghiên cứu (TADS)    ───► KHÓA LUẬN (ML, Trust Model, Trace API)
       │ (implements IReservationDecisionStrategy)
       ▼
Lớp 2: Decision Runtime Engine (TADSS) ───► ĐỒ ÁN 2 [IN-SCOPE] (SDK, Polly, Audit, Override, Cache)
       │ (Giao tiếp qua HTTP REST Contract)
       ▼
Lớp 1: Case Study Product (SmashCourt) ───► ĐỒ ÁN 1 (CRUD Booking, Payments, Chatbot FAQ)
```

### 💡 Ghi chú 4 ranh giới kỹ thuật cốt lõi (Architectural Boundaries)

1. **TADSS ↔ VNPay Boundary:** TADSS **không** tích hợp trực tiếp và không biết cổng thanh toán VNPay là gì. TADSS chỉ nhận yêu cầu ra quyết định, trả về kết quả `Approved`, `NeedApproval` hoặc `Rejected` kèm các thông số đề xuất cấu trúc (Recommendation). Hệ thống SmashCourt (Lớp 1) mới là phía tiếp nhận kết quả, quyết định chuyển tiếp người dùng sang luồng thanh toán hoặc hành động nghiệp vụ tương ứng của SmashCourt.
2. **Database Boundary — Logical Schema Separation:** TADSS độc lập ở mức mã nguồn ứng dụng và hợp đồng nghiệp vụ. Trong phiên bản Đồ án 2, TADSS sử dụng chung máy chủ PostgreSQL vật lý với SmashCourt nhưng cô lập hoàn toàn ở tầng **Logical Schema** (bảng nghiệp vụ SmashCourt nằm trong schema mặc định, các bảng audit quyết định của TADSS nằm trong schema `tadss`). Điều này giúp đơn giản hóa hạ tầng triển khai nhưng vẫn đảm bảo tính độc lập dữ liệu (Logical Boundary).
3. **Domain Portability Boundary — Proof of Concept:** TADSS đạt tính di động nhờ giao thức trừu tượng `Reservation` SDK. Để chứng minh tính di động độc lập tên miền, chúng ta chỉ cần viết lớp Mapping Adapter ở phía client (ví dụ: client Coworking Space ánh xạ thuộc tính của Phòng họp về `Reservation` Contract) và gửi đến TADSS Core. Runtime Core hoàn toàn không phải thay đổi mã nguồn.
4. **Telemetry Ownership Boundary:** Bảng `decision_executions` do Đồ án 2 tạo và ghi nhận telemetry kỹ thuật (`execution_time_ms`, `fallback_used`, `failure_reason`, `cache_hit`, `execution_type`). Khóa luận sẽ tái sử dụng tập dữ liệu telemetry này làm dataset chạy thực nghiệm Ablation Study & Simulator.

---

## 5. PHẠM VI TRIỂN KHAI (PROJECT SCOPE)

### 5.1. Phân nhóm các cấu phần (Tiering)

Để đảm bảo dự án hoàn thành đúng tiến độ mà không bị phình phạm vi (scope creep), các cấu phần phát triển được phân chia nghiêm ngặt:

#### 🔴 Core — Bắt buộc hoàn thành (Đồ án 2)
* **Decision Contract SDK:** Thư viện Class Library dùng chung chứa Domain Model, DTOs, Enums.
* **Decision Runtime API:** Endpoint `POST /api/v1/decisions`, định tuyến qua Strategy Factory (DI-based Strategy Resolution).
* **Default & Mock Strategies:** Thiết kế `DefaultDecisionStrategy` (Conservative Baseline Business Policy: phân hạng khách hàng kết hợp mức thanh toán 100% mặc định) và `MockAlternativeDecisionStrategy` (cọc 30% giờ thường / 50% giờ cao điểm) nhằm chứng minh Strategy Pattern được kiểm chứng bằng executable code thực tế.
* **Polly Resilience Pipeline:** Khung chịu lỗi lồng nhau (Timeout, Retry, CB, Fallback).
* **Append-only Audit Trail:** Ghi vết transaction audit trail thông qua in-process application events của MediatR đồng bộ.
* **Override API:** Endpoint `POST /api/v1/decisions/{id}/override` có cơ chế idempotency và optimistic locking.
* **Feature Flag:** Cờ bật tắt động gọi TADSS từ SmashCourt sử dụng `IOptionsMonitor` reload.
* **SmashCourt Integration:** Kết nối và kiểm soát luồng thanh toán tại SmashCourt dựa trên kết quả TADSS.
* **Integration Tests:** Bộ test cases tự động mô phỏng sập/timeout và kiểm tra fallback.

#### 🟡 Supporting — Nên có (Đồ án 2)
* **Manager Dashboard UI:** Giao diện Next.js đơn giản hiển thị danh sách Pending Approval để duyệt/ghi đè.
* **Request-Hash Cache:** Tích hợp Redis Cache băm SHA256 payload để tránh thực thi lặp lại.
* **Structured Logging:** Cấu hình Serilog đẩy log cấu trúc phục vụ debug.

#### 🟢 Optional — Có thể cắt giảm nếu thiếu thời gian
* **External Provider Cost Guardrail:** Cơ chế giám sát giới hạn chi phí external provider độc lập (`IExternalProviderMetrics`).
* **k6 Load Test:** Script kiểm thử bắn tải 200 RPS đo lường Circuit Breaker.
* **OpenTelemetry:** Tích hợp phân tán log trace.

### 5.2. Các cấu phần loại trừ hoàn toàn khỏi Đồ án 2 (Out-of-Scope)
* Không nghiên cứu lý thuyết hay cài đặt thuật toán tính điểm tin cậy dynamic (**Khóa luận**).
* Không huấn luyện hay triển khai mô hình học máy CatBoost dự báo bùng lịch (**Khóa luận**).
* Không xây dựng API giải thích vết quyết định Decision Trace (**Khóa luận**).
* Không chạy simulator giả lập hay Ablation Studies (**Khóa luận**).
* Không tích hợp Event Sourcing hay Outbox Pattern phân tán (**Future Work**).

---

## 6. THIẾT KẾ KIẾN TRÚC HỆ THỐNG (SYSTEM ARCHITECTURE)

### 6.1. Quy trình xử lý phê duyệt & Ghi đè (Human-in-the-loop Flow)
Khi TADSS trả về trạng thái quyết định, SmashCourt và Manager phối hợp thực thi như sau:

```text
                  ┌────────────────────────┐
                  │    DecisionRequest     │
                  └───────────┬────────────┘
                              ▼
                  ┌────────────────────────┐
                  │ TADSS Runtime Execute  │
                  └───────────┬────────────┘
                              │
                    Phân tích Status?
                              │
         ┌────────────────────┼────────────────────┐
         ▼ (Approved)         ▼ (NeedApproval)     ▼ (Rejected)
  ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
  │ Booking      │     │ Booking      │     │ Hủy Booking  │
  │ checkout     │     │ Pending      │     │ Trả về Lỗi   │
  │ sang VNPay   │     │ (Block VNPay)│     └──────────────┘
  └──────────────┘     └──────┬───────┘
                              │
                    Manager Command duyệt
                              │
                     ┌────────┴────────┐
                     ▼                 ▼
                 [Approve]        [Override]
                     │                 │
              Đồng ý mức cọc     Thay đổi cọc
              đề xuất gốc        (bắt buộc reason)
                     │                 │
                     └────────┬────────┘
                              ▼
                       Ghi nhận Event
                    (Append-only Audit)
                              ▼
                     Tiếp tục thanh toán
```

### 6.2. Cấu hình Polly v8 Resilience Pipeline
Để bảo vệ hệ thống khỏi các sự cố quá tải hoặc nghẽn mạng từ strategy nghiệp vụ, Polly v8 được thiết lập lồng nhau:

```text
┌── Outer Timeout: 500ms (Overall Request SLA Budget) ───────────┐
│                                                                │
│  Fallback: FallbackDecisionStrategy (NEED_APPROVAL, 100%)      │
│   └── Overhead < 10ms sau khi failure được xác định            │
│                                                                │
│  Retry Policy (Tối đa 2 retries = 3 attempts, base delay 30ms) │
│   │                                                            │
│   └── Circuit Breaker (Sampling 30s, tối thiểu 10 requests)    │
│        └── Inner Timeout: 120ms per attempt                    │
│             └── Strategy.EvaluateAsync()                       │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```
> [!IMPORTANT]
> **Giải trình Budget trễ:** Outer Timeout 500ms là overall request budget (SLA) bọc ngoài toàn bộ retry attempts và retry delays. Inner Timeout 120ms là giới hạn cho từng strategy execution attempt riêng lẻ ($3 \times 120\text{ms} + 2 \times 30\text{ms} = 420\text{ms} < 500\text{ms}$).

### 6.3. Thiết kế Request-Hash Caching
* **Thuật toán:** Cache Key được tính toán bằng cách tách biệt định danh yêu cầu (`RequestHash` theo chuẩn RFC 8785 JCS) và định danh phiên bản chiến lược:
  $$\text{RequestHash} = \text{SHA256}(\text{Canonical}(\text{DecisionRequestDto}))$$
  Namespace Cache Key: `decision:hash:{requestHash}:{strategyName}:v{strategyVersion}`
* **Stale Behavior Policy:**
  * **Default Strategy (Baseline):** Cho phép cache bình thường với cấu hình baseline TTL = 24 giờ (`TtlHours = 24`).
  * **Adaptive Strategy (Khóa luận):** Mặc định bị vô hiệu hóa (disabled by default) hoặc đặt TTL = 1 phút (dưới dạng cấu hình thực nghiệm) để tránh stale decisions khi điểm tin cậy khách hàng thay đổi đột ngột.

### 6.4. Ràng buộc xử lý quá hạn phê duyệt (No Auto-Approve)
Đối với luồng duyệt thủ công (Human-in-the-loop): Nếu Manager không phản hồi trong khoảng thời gian cấu hình, booking sẽ giữ nguyên trạng thái `PendingDecisionApproval` hoặc tự động hết hạn (booking expired), hệ thống **tuyệt đối không tự động phê duyệt (No Auto-Approve)** để đảm bảo an toàn tuyệt đối cho dòng tiền của hệ thống đặt chỗ.

### 6.5. Cơ chế phân luồng quyết định nghiệp vụ tại hệ thống đặt sân (SmashCourt)
Để tối ưu hóa trải nghiệm khách hàng và giảm tải cho hệ thống, ứng dụng nghiệp vụ SmashCourt triển khai một cổng phân luồng quyết định (Business Routing Layer) trước khi gửi yêu cầu đến TADSS Core:
1.  **Phân luồng ưu tiên (VIP/Loyal Fast-Track):** Đối với nhóm khách hàng thân thiết (Gold, Platinum) có điểm uy tín cao, hệ thống SmashCourt tự động áp dụng chính sách phê duyệt nhanh (Fast-Track) và chuyển thẳng sang luồng thanh toán, không cần gửi yêu cầu thẩm định qua TADSS để tối ưu tốc độ giao dịch.
2.  **Phân loại theo thời gian đặt (Booking Lead Time Separation):** 
    *   **Đặt sát giờ (Last-minute booking - dưới 24h):** Ưu tiên ra quyết định tự động hoàn toàn (chỉ nhận kết quả `Approved` hoặc `Rejected` từ TADSS). Tránh tối đa trạng thái `NeedApproval` để không làm nghẽn luồng thanh toán của khách hàng khi thời gian bắt đầu cận kề.
    *   **Đặt trước dài ngày (Advance booking):** Cho phép kích hoạt luồng phê duyệt thủ công (`NeedApproval`), đưa yêu cầu lên Dashboard chờ Manager xử lý vì thời gian chuẩn bị còn dài.
3.  **Chính sách theo thời điểm (Peak/Off-Peak Rules):** Tách biệt luồng gọi chiến lược mặc định (`DefaultDecisionStrategy` - Baseline Business Policy với mức 100% mặc định khi không đạt ngưỡng ưu đãi) và chiến lược thử nghiệm (`MockAlternativeDecisionStrategy`) áp dụng mức cọc 30% cho giờ thường (Approved) và cọc 50% cho giờ cao điểm (NeedApproval) nhằm kiểm chứng khả năng phân giải chiến lược linh hoạt của hệ thống.
4.  **Cơ chế vận hành khẩn cấp (Emergency Valve):** Khi hệ thống TADSS quá tải hoặc gặp sự cố nghiêm trọng, Manager có thể tắt Feature Flag (`EnableTadssDecision = false`) trên SmashCourt. Hệ thống đặt sân sẽ lập tức chuyển sang chế độ tự hoạt động bằng chính sách thanh toán tĩnh mặc định (100%), cô lập hoàn toàn sự cố để bảo vệ luồng giao dịch cốt lõi.

*Lưu ý: Các cơ chế phân luồng trên thuộc về logic nghiệp vụ của ứng dụng SmashCourt, không thuộc phạm vi xử lý lõi của TADSS Runtime Platform.*

---

## 7. CÔNG NGHỆ SỬ DỤNG (TECHNOLOGY STACK)

Technology stack của Đồ án 2 được gom cụm rõ ràng theo phân nhóm scope phát triển:

### 7.1. Core Stack (Bắt buộc)
* **Ngôn ngữ & Framework:** C# 12, .NET Core 8 (ASP.NET Core Web API).
* **Database Access:** Entity Framework Core 8, PostgreSQL 16 (Logical schema isolation — hỗ trợ triển khai Cloud Managed trên Neon PostgreSQL hoặc Local/Docker).
* **Resilience Framework:** Polly v8 (Timeout, Retry, Circuit Breaker, Fallback).
* **In-process Dispatcher:** MediatR (Synchronous event handling trong transaction boundary).
* **Testing:** xUnit, FluentAssertions (Unit & Integration tests).

### 7.2. Supporting Stack (Nên có)
* **Cache Provider:** Redis 7.x / 8.x (Request-Hash Distributed Caching trên Redis Cloud Managed hoặc Local/Docker, tích hợp L1 `IMemoryCache` Fallback).
* **Logging Framework:** Serilog (Structured Logging ghi file/console).
* **Frontend Framework:** Next.js (Manager Dashboard duyệt/ghi đè đơn giản).

---

## 8. KẾ HOẠCH THỰC HIỆN THEO SPRINT (ROADMAP)

> [!NOTE]
> **Cập nhật Lộ trình Thực thi:** Chi tiết lộ trình triển khai kỹ thuật được phân rã thành 16 tuần (bao gồm 14 tuần phát triển kỹ thuật và 2 tuần ổn định/bảo vệ) trong tài liệu [MASTER_EXECUTION_PLAN.md](MASTER_EXECUTION_PLAN.md) nhằm tối ưu hóa tính khả thi, phân tách rõ ràng vai trò Owner/Reviewer và tăng cường bộ đệm an toàn cho đồ án.

Dự án được phân chia thành các giai đoạn phát triển logic kế thừa phụ thuộc:

* **Phase 1 — Phân tích & Thiết kế (2 tuần):** Phân tích vấn đề, xác định yêu cầu nghiệp vụ, hoàn thiện chuẩn giao tiếp Decision Contract, thiết kế kiến trúc tổng thể và DB schema.
* **Phase 2 — Xây dựng Decision Contract SDK (2 tuần):** Phát triển thư viện dùng chung `Shared.DecisionContractSDK` định nghĩa Reservation Aggregate, DTOs, Enums, giao diện `IReservationDecisionStrategy` và các quy tắc kiểm tra hợp lệ.
* **Phase 3 — Xây dựng Decision Runtime API (2 tuần):** Xây dựng Web API `POST /decisions`, bộ nạp chiến lược `StrategyFactory` (DI-based resolution), chiến lược mặc định `DefaultDecisionStrategy` (Baseline Business Policy: phân hạng khách hàng kết hợp mức thanh toán 100% mặc định) và chiến lược thử nghiệm `MockAlternativeDecisionStrategy` (cọc 50% giờ cao điểm).
* **Phase 4 — Resilience, Cache & Audit (3 tuần):** Tích hợp chuỗi chịu lỗi lồng nhau Polly v8 (Outer Timeout 500ms, Retry 2 lần, Inner Timeout 120ms, Circuit Breaker, Fallback), bộ nhớ đệm Redis Request-Hash Cache, ghi vết transaction audit log qua MediatR xuống cơ sở dữ liệu PostgreSQL, thực thi bộ Benchmark 5 tầng (76 lượt chạy) và chuẩn hóa 3 bộ dữ liệu nghiên cứu (`INV-P1..INV-P8`).
* **Phase 5 — Human-in-the-loop & Integration (3 tuần):** Xây dựng Override API cho phép duyệt thủ công (idempotency, optimistic locking), màn hình Manager Dashboard trên Next.js, cơ chế vận hành khẩn cấp Feature Flag và tích hợp hoàn chỉnh với luồng đặt sân của hệ thống SmashCourt.
* **Phase 6 — Testing, Evaluation & Hoàn thiện (4 tuần: W13–W16):** Thực hiện Unit Test, Integration Test, kiểm thử giả lập sự cố (Failure Injection Test), kiểm thử bắn tải (Load Test), đánh giá các chỉ số thiết kế (SC1-SC6), khắc phục lỗi, quay video demo và hoàn thiện báo cáo đồ án.

### Ràng buộc phụ thuộc giữa các Giai đoạn (Dependencies)
* **Phase 3** yêu cầu **Phase 2** hoàn thành 100% (để có SDK và Interface dùng chung).
* **Phase 4** yêu cầu **Phase 3** hoàn thành tối thiểu 80% (để có Runtime API nạp Middleware Polly).
* **Phase 5** yêu cầu **Phase 4** hoàn thành 100% (để hệ thống ghi vết Audit Log được cấy vào API trước khi tích hợp vào SmashCourt).
* **Phase 6** yêu cầu **Phase 5** hoàn thành tối thiểu 80% (để bắt đầu viết các kịch bản kiểm thử tích hợp thực tế).

---

## 9. TIÊU CHÍ THÀNH CÔNG (SUCCESS CRITERIA & KPIs)

Các chỉ số dưới đây đóng vai trò là **Design Targets** (Mục tiêu thiết kế kỹ thuật phần mềm) của Đồ án 2, được kiểm chứng qua các kịch bản test tích hợp:

| Mã | Chỉ số | Design Target (Kiểm chứng) |
|---|---|---|
| **SC1** | Thời gian xử lý thành công (Normal) | Latency p95 < 150ms đối với các request thành công thông thường trên môi trường chuẩn Local/In-Network (hoặc tách bạch độ trễ xử lý nội tại `execution_time_ms` với độ trễ mạng WAN RTT ~35–60ms/hop khi kết nối qua Cloud Managed tại region `ap-southeast-1`). |
| **SC2** | Thời gian xử lý kịch bản lỗi (Failure) | Overall strategy resilience budget <= 500ms (được cưỡng chế bởi Polly Outer Timeout khi retry/timeout xảy ra). |
| **SC3** | Tính nhất quán của Audit Trail | 100% các kịch bản kiểm thử ghi nhận đúng loại execution log tương ứng. |
| **SC4** | Cơ chế chống ghi trùng (Override) | Chặn hoàn toàn các command gửi trùng lặp thông qua `IdempotencyKey`. |
| **SC5** | Khả năng tự reload Feature Flag | Reload cấu hình Feature Flag động qua `IOptionsMonitor` dưới 2 giây. |
| **SC6** | Tính độc lập tên miền (Portability) | TADSS Core nhận và xử lý chính xác mock request từ Coworking Space mà không sửa code. |

---

## 10. ĐỊNH HƯỚNG PHÁT TRIỂN KHÓA LUẬN (THESIS DIRECTION) `[OUT-OF-SCOPE ĐỒ ÁN 2 — DÀNH CHO KHÓA LUẬN]`

Sau khi hoàn thành Đồ án 2, nền tảng chịu lỗi TADSS sẽ được sử dụng làm môi trường thực nghiệm cốt lõi để phát triển Khóa luận tốt nghiệp với đề tài **"Nghiên cứu và đánh giá kiến trúc quyết định tin cậy thích ứng cho các nền tảng đặt chỗ dịch vụ"**:

1. **Khảo sát chiến lược ra quyết định TADS:**
   * Cài đặt thuật toán **Adaptive Trust Score** tích hợp hàm mũ suy hao thời gian ($e^{-\lambda \Delta t}$) để giảm dần trọng số sai lầm trong quá khứ của người dùng.
   * Huấn luyện mô hình **CatBoost Classifier** dự đoán xác suất bùng lịch (No-show Probability) dựa trên lịch sử đặt, thời tiết và loại ngày.
2. **Khảo sát giả thuyết nghiên cứu (Research Hypothesis H1) & Ràng buộc:**
   * **Giả thuyết chính (Primary Hypothesis - H1):** Chiến lược quyết định thích ứng (TADS) được triển khai trên nền tảng runtime TADSS giúp cải thiện hiệu ích kinh doanh kỳ vọng (Expected Business Utility) so với baseline static policy.
   * **Ràng buộc công bằng (Fairness Constraint):** TADS nghiên cứu áp dụng ràng buộc công bằng cơ hội **Equal Opportunity** nhằm đảm bảo tỷ lệ hệ thống đánh giá sai (False Positive Rate) hoặc từ chối cơ hội là công bằng giữa các nhóm đối tượng khách hàng:
     $$\text{Fairness constraint} = \left| TPR_{\text{group A}} - TPR_{\text{group B}} \right| \le \epsilon$$
   * **Ràng buộc bền vững (Resilience Constraint):** Hệ thống duy trì được SLA phản hồi và tính liên tục trong các kịch bản sự cố dịch vụ thông qua Polly Resilience Pipeline của TADSS.
3. **Thực nghiệm đối chứng qua mô phỏng (Controlled Simulation):**
   * Phát triển **Agent-based Simulator** giả lập hành vi đặt/hủy lịch của 1,000 khách hàng (Risk, Loyal, New, Regular) tương tác trên tệp dữ liệu giả lập chất lượng cao (**Synthetic Dataset** đạt 20,000+ bản ghi) nhằm chạy các nghiên cứu cắt giảm (**System Ablation Study**) đối chứng các chiến lược.

### Lộ trình thực hiện Khóa luận tốt nghiệp (12 tuần)
Sau khi hoàn thành và bảo vệ thành công Đồ án 2, phần nghiên cứu khoa học phục vụ Khóa luận tốt nghiệp sẽ được thực hiện độc lập trong thời gian 12 tuần:
* **Tuần 1 — Tuần 4:** Nghiên cứu sâu lý thuyết mô hình suy hao thời gian của điểm tin cậy (Adaptive Trust Score) và cấu trúc mô hình CatBoost Classifier.
* **Tuần 5 — Tuần 8:** Thực thi thuật toán quyết định động thích ứng (TADS), biên soạn bộ dữ liệu tổng hợp quy mô lớn (Synthetic Dataset) phục vụ thực nghiệm.
* **Tuần 9 — Tuần 12:** Xây dựng phần mềm giả lập Agent-based Simulator, đánh giá thực nghiệm (Ablation Study) kiểm chứng giả thuyết H1, phân tích kết quả công bằng Equal Opportunity và hoàn thiện báo cáo tốt nghiệp.

---

*Proposal Đồ án 2 — Sẵn sàng trình nộp Giảng viên hướng dẫn.*
