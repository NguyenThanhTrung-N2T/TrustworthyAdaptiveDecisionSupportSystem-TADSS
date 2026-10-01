# ADR-005: Định hướng Lựa chọn CatBoost cho Chiến lược Mở rộng ở Khóa luận `[OUT-OF-SCOPE ĐỒ ÁN 2]`

> **📌 Ghi chú Phạm vi (Scope Lock):** Tài liệu này ghi nhận quyết định định hướng cho **Giai đoạn Mở rộng ở Khóa luận Tốt nghiệp (Lớp 3 — Chiến lược `TADS`)** và **nằm ngoài phạm vi triển khai của Đồ án 2** (xem mục 5.2 của [`PROPOSAL_DO_AN_2.md`](../../planning/PROPOSAL_DO_AN_2.md)). Trong phạm vi Đồ án 2, `TADSS Runtime Engine` chỉ triển khai các chiến lược quy tắc nghiệp vụ tất định (`DefaultDecisionStrategy` và `MockAlternativeDecisionStrategy`) và hoàn toàn không huấn luyện hay tích hợp mô hình học máy.

## Trạng thái
Định hướng mở rộng cho Khóa luận (Future Extension — Out-of-Scope Đồ án 2)

## Bối cảnh (Dành cho Giai đoạn Khóa luận)
Khi mở rộng từ nền tảng hạ tầng `TADSS Runtime Engine` (Đồ án 2) sang nghiên cứu giải thuật quyết định thích ứng (`TADS` — Khóa luận), bài toán dự đoán bùng hẹn (No-show Prediction) sẽ yêu cầu một mô hình máy học gọn nhẹ, xử lý tốt dữ liệu dạng bảng (tabular data) chứa các thuộc tính phân loại (như `CustomerTier`, `BookingType`, `Priority`) đã được chuẩn hóa sẵn trong `Shared.DecisionContractSDK`.

## Quyết định Định hướng
Định hướng lựa chọn thuật toán **CatBoost (Categorical Boosting)** làm baseline classifier cho gói chiến lược mở rộng trong Khóa luận tốt nghiệp, kết nối vào `TADSS Runtime Engine` thông qua giao diện `IReservationDecisionStrategy` mà không làm thay đổi mã nguồn lõi của Runtime.

## Hệ quả
*   **Tích cực (Positive):**
    *   Contract v2 của Đồ án 2 đã bổ sung lịch sử khách (`history`) và snapshot request được lưu trong audit, nên Khóa luận có sẵn đặc trưng và nhãn kết quả (`decision_outcomes`) mà không phải đổi contract.
    *   Giữ cho phạm vi Đồ án 2 tập trung vào độ tin cậy vận hành của Runtime Engine.
*   **Tiêu cực (Negative):**
    *   Không hiện thực trong Đồ án 2. Lựa chọn mô hình cuối cùng phụ thuộc dữ liệu thực tế thu được (xem [`RESEARCH_THESIS_DEFENSE.md`](../../research/RESEARCH_THESIS_DEFENSE.md)); CatBoost là baseline định hướng, không phải cam kết.
    *   Strategy học máy có thể nặng CPU, nên cần cơ chế cách ly timeout của runtime v2 (ADR-003).

## Phương án thay thế đã cân nhắc (Cho Khóa luận)
1.  **Sử dụng XGBoost / LightGBM:** Yêu cầu tiền xử lý biến phân loại thủ công phức tạp hơn CatBoost.
2.  **Sử dụng mạng Neural Network (Deep Learning):** Không tối ưu đối với dữ liệu dạng bảng kích thước vừa phải và tăng chi phí tài nguyên tính toán.
