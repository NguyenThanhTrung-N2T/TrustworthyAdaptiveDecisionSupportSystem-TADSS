# ADR-005: Lựa chọn CatBoost làm mô hình phân loại ML Baseline Classifier

## Trạng thái
Đã duyệt (Accepted)

## Bối cảnh
Bài toán dự đoán bùng hẹn (No-show Prediction) yêu cầu một mô hình máy học gọn nhẹ, có khả năng xử lý tốt dữ liệu dạng bảng (tabular data) chứa nhiều thuộc tính phân loại (categorical features như Loyalty Tier, DayType, Chi nhánh) và hoạt động với độ trễ thấp để nhúng vào Strategy quyết định.

## Quyết định
Lựa chọn thuật toán **CatBoost (Categorical Boosting)** làm baseline classifier duy nhất để huấn luyện mô hình dự đoán xác suất bùng lịch của khách hàng.

## Hệ quả
*   **Tích cực (Positive):**
    *   CatBoost tự động xử lý các biến phân loại chất lượng cao mà không cần qua bước One-Hot Encoding thủ công, hạn chế rủi ro phình to không gian đặc trưng.
    *   Khả năng dự đoán có độ chính xác cao và không bị overfit trên tập dữ liệu dạng bảng mất cân bằng.
    *   Tải trực tiếp model dạng C# Wrapper vào runtime giúp việc chấm điểm (scoring) diễn ra cực kỳ nhanh (< 5ms).
*   **Tiêu cực (Negative):**
    *   Yêu cầu tài nguyên RAM tương đối lớn trong quá trình huấn luyện ban đầu ngoại tuyến (Offline Training).

## Phương án thay thế đã cân nhắc
1.  **Sử dụng XGBoost / LightGBM:** Loại bỏ vì yêu cầu cấu hình tiền xử lý biến phân loại thủ công phức tạp hơn CatBoost, tăng khả năng lỗi khi mock dữ liệu.
2.  **Sử dụng mạng Neural Network (Deep Learning):** Loại bỏ vì không tối ưu đối với dữ liệu dạng bảng kích thước vừa phải và tăng chi phí tài nguyên tính toán không cần thiết.
