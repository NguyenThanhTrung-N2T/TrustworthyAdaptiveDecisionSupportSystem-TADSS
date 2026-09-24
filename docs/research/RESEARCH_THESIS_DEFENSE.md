# ĐỀ CƯƠNG KHÓA LUẬN TIN GỌN: KIẾN TRÚC QUYẾT ĐỊNH TIN CẬY THÍCH ỨNG (ATDA)

*   **Tên Đề tài (Thesis Title)**: Thiết kế và đánh giá kiến trúc quyết định tin cậy thích ứng cho các nền tảng đặt chỗ dịch vụ (*Design and Evaluation of an Adaptive Trust Decision Architecture for Service Reservation Platforms*)
*   **Mô hình hiện thực hóa đối chứng (Reference Implementation / Artifact)**: **TADSS (Trustworthy Adaptive Decision Support System)**.
*   **Quy mô thực hiện**: Nhóm 1 - 2 sinh viên | Thời gian: 6 tháng (26 tuần).

---

## I. BA CÂU HỎI NGHIÊN CỨU TIN GỌN (RESEARCH QUESTIONS)

Để đảm bảo tính tập trung cao độ, khóa luận chỉ xoay quanh 1 câu hỏi chính và 2 câu hỏi phụ:

*   **Primary RQ (Câu hỏi chính)**: *Làm thế nào để thiết kế một Kiến trúc quyết định tin cậy thích ứng (ATDA) nhằm tối đa hóa hiệu ích kinh doanh kỳ vọng chuẩn hóa của nền tảng đặt chỗ dịch vụ mà vẫn đảm bảo tính công bằng toán học (Fairness) và tính chống chịu lỗi (Resilience) của hệ thống?*
*   **Sub-RQ 1 (Trust & Fairness)**: *Làm thế nào để mô hình hóa điểm tin cậy thích ứng (Adaptive Trust Score) tích hợp hàm suy hao thời gian (Time Decay) và ràng buộc công bằng (Equalized Odds) để đưa ra đề xuất chính sách cá nhân hóa tối ưu?*
*   **Sub-RQ 2 (Software Resilience)**: *Làm thế nào để thiết kế Tầng phục hồi (Resilience Layer) với cơ chế tự khôi phục (Circuit Breaker, Fallback) để hệ thống hoạt động liên tục khi thành phần strategy gặp sự cố?*

---

## II. BA ĐÓNG GÓP KHOA HỌC CHÍNH (RESEARCH CONTRIBUTIONS)

Đề tài tập trung chứng minh và thực hiện đúng **3 đóng góp khoa học**:

*   **Đóng góp 1**: Đề xuất kiến trúc quyết định tin cậy thích ứng (ATDA) tích hợp cấu phần đánh giá độ tin cậy (Trust Modeling) và cấu phần đề xuất chính sách (Decision Policy) cho các nền tảng đặt chỗ.
*   **Đóng góp 2**: Thiết lập mô hình điểm tin cậy thích ứng (Adaptive Trust Score) có tích hợp suy hao thời gian (Time Decay) kết hợp ràng buộc công bằng toán học (Equalized Odds).
*   **Đóng góp 3**: Đề xuất cơ chế phục hồi hệ thống (Resilience Layer) có khả năng tự động phát hiện sự cố strategy và fallback tức thời sang chính sách tĩnh để đảm bảo tính liên tục của hệ thống.

---

## III. KHUNG KIẾN TRÚC THU HẸP (ATDA ARCHITECTURE LAYERS)

Kiến trúc ATDA được thu hẹp tối đa vào 3 tầng cốt lõi:

```
                            ATDA
                             │
            ┌────────────────┴────────────────┐
            ▼                                 ▼
     Trust Modeling                    Decision Policy
     - Trust Score Calculation         - Deposit/Limit Proposals
     - Time Decay Integration          - Human-in-the-loop Feedback
     - Fairness Constraints (E.Odds)
            │                                 │
            └────────────────┬────────────────┘
                             │
                             ▼
                      Resilience Layer
                      - Circuit Breaker (Polly)
                      - Fallback Policy Engine
                      - Event Logging (Outbox Pattern)
                      - Offline Drift Monitoring
```

### 1. Phân hệ Trust Modeling (Mô hình hóa độ tin cậy)
*   **Trust Score Calculation**: Tính điểm tin cậy dựa trên hành vi thanh toán, bùng hẹn và hủy lịch.
*   **Time Decay**: Sử dụng hàm mũ suy hao thời gian $e^{-\lambda \Delta t}$ để giảm dần trọng số lỗi lầm cũ của khách hàng.
*   **Fairness Constraint**: Sử dụng ràng buộc công bằng **Equalized Odds** để đảm bảo tỷ lệ AI đánh giá sai (False Positive Rate) là công bằng giữa khách hàng VIP và khách hàng mới.

### 2. Phân hệ Decision Policy (Quyết định chính sách)
*   **Policy Engine**: Đề xuất các thay đổi chính sách vận hành (yêu cầu đặt cọc động, hạn mức hủy lịch, thứ tự ưu tiên booking giờ vàng) nhằm tối đa hóa hiệu ích kinh doanh kỳ vọng chuẩn hóa ($Utility$):
    $$\max \mathbb{E}[Utility] = \alpha \cdot \frac{Revenue}{Rev_{max}} + \beta \cdot Retention - \gamma \cdot Risk - \delta \cdot \frac{Complaint}{Complaints_{max}}$$
*   **Human-in-the-loop Feedback**: Cho phép Manager phê duyệt hoặc ghi đè (Override) quyết định của hệ thống. Quyết định ghi đè được lưu lại dưới dạng append-only event trail làm feedback loop cho tập dữ liệu.

### 3. Phân hệ Resilience Layer (Chống chịu lỗi & Bền vững hệ thống)
*   **Circuit Breaker (thư viện Polly)**: Phát hiện sự cố strategy service (timeout, lỗi 5xx). Nếu strategy gặp sự cố hoặc phản hồi chậm quá 150ms, tự động chuyển luồng giao dịch sang Fallback Policy Engine (sử dụng chính sách thanh toán tĩnh 100% mặc định).
*   **Append-only Audit Trail**: Đảm bảo ghi nhận an toàn các sự kiện giao dịch và phản hồi của con người xuống Database.
*   **Offline Drift Monitoring**: Thay vì giám sát thời gian thực phức tạp, hệ thống chạy tiến trình kiểm tra ngoại tuyến (offline batch job) hàng tuần để so sánh phân phối dữ liệu thực tế với dữ liệu huấn luyện, đưa ra cảnh báo huấn luyện lại mô hình (retraining).

---

## IV. PHƯƠNG PHÁP THỰC NGHIỆM TIN GỌN (EVALUATION PLAN)

Để đảm bảo tính khả thi trong 26 tuần thực hiện, phương án thực nghiệm được cắt giảm tối đa các cấu phần không cốt lõi:

1.  **Kiểm chứng tính độc lập của Domain (DDD Canonical Model)**:
    *   Sử dụng **Domain-Driven Design (DDD)** định nghĩa Aggregate Root là `Reservation` (chứa Customer, Resource, Schedule, Payment, Policy, DecisionContext).
    *   Chứng minh tính độc lập bằng cách mô tả việc mapping cấu trúc dữ liệu JSON từ **SmashCourt** (sân cầu lông), **Hotel Reservation** (Khách sạn) và **Clinic Booking** (Phòng khám) về Canonical Model này, không cần xây dựng toàn bộ app của khách sạn và phòng khám.
2.  **Đo lường Thuật toán dự đoán bùng lịch**: So sánh F1-Score của các mô hình cơ bản (CatBoost, XGBoost, Random Forest) trên tập dữ liệu bùng lịch giả lập của SmashCourt.
3.  **Nghiên cứu cắt giảm cấu phần hệ thống (System Ablation Study)**: Chạy đối chứng thực nghiệm trong môi trường giả lập hành vi người dùng (Agent-based Simulation) để đo lường các chỉ số $Utility$, tỷ lệ no-show, tỷ lệ VIP churn dưới 5 cấu hình hệ thống từ đơn giản đến phức tạp (Baseline ➔ Chỉ có ML ➔ Có Fairness ➔ Có Human Loop ➔ Có Fallback).

---
*Bản đề cương tin gọn này đã được phê duyệt làm lộ trình thực hiện khóa luận chính thức.*
