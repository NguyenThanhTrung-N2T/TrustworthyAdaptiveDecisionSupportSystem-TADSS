# ADR-003: Sử dụng Polly v8 Resilience Pipeline cho Khả năng chịu lỗi

## Trạng thái
Đã duyệt (Accepted)

## Bối cảnh
Mô hình AI (CatBoost/Gemini) chạy trên FastAPI service độc lập có khả năng bị quá tải, phản hồi chậm (>120ms) hoặc sập kết nối giữa chừng. Nếu không xử lý chịu lỗi, luồng đặt sân trực tuyến chính của khách hàng sẽ bị nghẽn hoặc báo lỗi 500, gây tổn thất doanh thu cho doanh nghiệp.

## Quyết định
Sử dụng thư viện **Polly v8** để thiết lập một Resilience Pipeline tập trung bảo vệ luồng gọi API quyết định. Chuỗi pipeline bao gồm các chiến lược độc lập liên kết chặt chẽ: `Outer Timeout` (Giới hạn tổng SLA tối đa 500ms) ➔ `Fallback` (Chuyển sang `FallbackDecisionStrategy` trả về trạng thái an toàn `NEED_APPROVAL` để duy trì decision-path continuity) ➔ `Retry` (Thử lại tối đa 2 lần = 3 attempts tổng cộng, base delay 30ms) ➔ `Circuit Breaker` (Mở mạch khi tỷ lệ lỗi vượt ngưỡng trong cửa sổ đánh giá 30 giây, tối thiểu 10 requests) ➔ `Inner Timeout` (Khống chế thời gian xử lý từng lượt tối đa 120ms; ngân sách $3 \times 120\text{ms} + 2 \times 30\text{ms} = 420\text{ms} < 500\text{ms}$).

## Hệ quả
*   **Tích cực (Positive):**
    *   Bảo vệ tối đa tính sẵn sàng và tính tin cậy (Resilience) của luồng đặt chỗ.
    *   Hệ thống tự động cô lập cấu phần AI bị sập và tự phục hồi (Self-healing) khi dịch vụ AI ổn định trở lại.
*   **Tiêu cực (Negative):**
    *   Khách hàng có thể phải chấp nhận mức cọc mặc định (Fallback) trong thời gian AI bị sập, làm giảm hiệu năng tối ưu của mô hình trong ngắn hạn.

## Phương án thay thế đã cân nhắc
1.  **Tự viết logic Retry/Timeout bằng HttpClient handler:** Loại bỏ vì khó quản lý trạng thái Circuit Breaker phức tạp và không tận dụng được sức mạnh của thư viện chuẩn đã được kiểm chứng.
