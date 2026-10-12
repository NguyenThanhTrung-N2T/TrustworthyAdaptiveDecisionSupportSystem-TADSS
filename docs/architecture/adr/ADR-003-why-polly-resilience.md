# ADR-003: Sử dụng Polly v8 Resilience Pipeline cho Khả năng chịu lỗi

## Trạng thái
Đã duyệt (Accepted — Đồ án 2)

## Bối cảnh
Khi thực thi chiến lược ra quyết định (`IReservationDecisionStrategy`), các chiến lược phức tạp hoặc phụ thuộc tính toán có khả năng bị quá tải, phản hồi chậm (`> 120ms`) hoặc ném ngoại lệ nội bộ. Nếu không có cơ chế chịu lỗi cô lập, luồng đặt sân trực tuyến chính của khách hàng sẽ bị nghẽn hoặc báo lỗi 500, gây gián đoạn giao dịch của hệ thống đặt chỗ.

## Quyết định
Sử dụng thư viện **Polly v8** để thiết lập một Resilience Pipeline 5 tầng tập trung bảo vệ luồng thực thi quyết định. Chuỗi pipeline bao gồm các chiến lược độc lập liên kết chặt chẽ: `Outer Timeout` (Giới hạn tổng SLA tối đa 500ms) ➔ `Fallback` (Chuyển sang `FallbackDecisionStrategy` trả về trạng thái an toàn `NEED_APPROVAL` để duy trì decision-path continuity) ➔ `Retry` (Thử lại tối đa 2 lần = 3 attempts tổng cộng, base delay 30ms) ➔ `Circuit Breaker` (Mở mạch khi tỷ lệ lỗi vượt ngưỡng trong cửa sổ đánh giá 30 giây, tối thiểu 10 requests) ➔ `Inner Timeout` (Khống chế thời gian xử lý từng lượt tối đa 120ms; ngân sách cấu hình lý thuyết $3 \times 120\text{ms} + 2 \times 30\text{ms} = 420\text{ms} < 500\text{ms}$).

## Hệ quả
*   **Tích cực (Positive):**
    *   Bảo vệ tối đa tính sẵn sàng và tính liên tục của luồng ra quyết định (Decision Continuity).
    *   Hệ thống tự động cô lập chiến lược đang gặp sự cố (fast-fail khi Circuit Breaker `OPEN`) và tự phục hồi (Self-healing) khi chiến lược ổn định trở lại.
*   **Tiêu cực (Negative):**
    *   Các yêu cầu rơi vào trạng thái sự cố sẽ chuyển sang `NEED_APPROVAL` (kèm khuyến nghị thanh toán an toàn 100%) chờ Quản lý phê duyệt trong thời gian chiến lược chính bị cô lập.

## Điều chỉnh khi re-baseline (01/10/2026)
*   Outer timeout chỉ bao phần thực thi strategy; đọc/ghi cache và ghi audit nằm ngoài pipeline.
*   Inner timeout của Polly v8 chỉ có hiệu lực khi strategy tôn trọng `CancellationToken`; v2 bổ sung cơ chế cách ly để cắt được cả strategy không hợp tác (điều kiện cần cho strategy học máy ở Khóa luận).
*   Bổ sung circuit breaker cho Redis để lỗi cache không làm mọi request chờ hết timeout.
*   Chi tiết: [`RESILIENCE_AND_FAULT_TAXONOMY.md`](../RESILIENCE_AND_FAULT_TAXONOMY.md).

## Điều chỉnh 04/10/2026 (D-13) — thay thế phần nói về `NEED_APPROVAL` ở trên
*   Kết quả của tầng `Fallback` (nay là `FallbackDecisionPolicy` trong `TADSS.Infrastructure/Resilience`) **không còn là `NEED_APPROVAL`**: fallback trả `APPROVED` với cọc 100% (`executionType = FALLBACK`, `fallbackUsed = true`, `reasonCode` `FALLBACK_*` theo nguyên nhân).
*   Hệ quả tiêu cực ở trên ("chuyển sang `NEED_APPROVAL` chờ Quản lý phê duyệt") không còn đúng: khi strategy bị cô lập, khách thanh toán cọc 100% ngay, không có đơn nào dồn sang Manager; quyết định fallback không vào hàng chờ duyệt và không override được.
*   Lý do: mọi sự cố kỹ thuật dùng chung một quy tắc cọc 100% (như khi SmashCourt tắt flag hoặc TADSS không phản hồi); `NEED_APPROVAL` chỉ dành cho bất định nghiệp vụ do strategy quyết định. Chi tiết: D-13 trong `spec/PHASE5_DESIGN_SPEC.md`.

## Phương án thay thế đã cân nhắc
1.  **Tự viết logic Retry/Timeout thủ công bằng vòng lặp `try/catch` và `CancellationTokenSource`:** Loại bỏ vì khó quản lý trạng thái trượt (sliding window) của Circuit Breaker dưới tải đồng thời cao và không tận dụng được cơ chế pipeline chuẩn hóa của Polly v8.
