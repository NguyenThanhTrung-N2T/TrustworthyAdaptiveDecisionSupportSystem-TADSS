# ADR-004: Sử dụng mô hình Rich Domain Model cho Thực thể Reservation

## Trạng thái
Đã duyệt (Accepted)

## Bối cảnh
Thực thể `Reservation` ban đầu được thiết kế chỉ chứa các thuộc tính dữ liệu và getter/setter (Anemic Domain Model). Điều này dẫn đến việc các logic xác thực tính hợp lệ (Business Invariants) và hành vi miền (như tính toán thời lượng đặt sân, kiểm tra thời gian hủy sân hợp lệ) bị phân tán rải rác ở tầng Service bên ngoài, vi phạm tính đóng gói (Encapsulation) của OOP.

## Quyết định
Áp dụng mẫu thiết kế **Rich Domain Model** của DDD. Đưa các business invariants và behaviors trực tiếp vào bên trong thực thể `Reservation` và các value object của nó. Ở v2 (bản duy nhất hiện nay), constructor của `Reservation`, `ReservationItem`, `CustomerProfile`, `CustomerHistory`, `PeakWindow` tự kiểm bất biến và ném `DomainInvariantException` kèm mã bất biến; hành vi miền gồm `Reservation.EarliestStart`, `Reservation.ToBranchLocal()`, `ReservationItem.Duration`, `BranchCalendar.IsPeak()` và `PeakWindow.Contains()`. Domain v2 mở rộng cho booking nhiều tài nguyên, khách vãng lai, lịch sử khách và thời gian có múi giờ chi nhánh. Các phương thức của bản v1 (kiểm hợp lệ, điều kiện hủy không phạt, tỷ lệ cọc trong domain) đã gỡ ở Phase 5 cùng contract v1: mức cọc do strategy quyết định, không nằm trong domain.

## Hệ quả
*   **Tích cực (Positive):**
    *   Tập trung hóa logic nghiệp vụ tại chính thực thể sở hữu dữ liệu, tránh phân tán logic miền.
    *   Đảm bảo thực thể luôn ở trạng thái hợp lệ ngay từ thời điểm khởi tạo (Encapsulated Invariants).
    *   Dễ dàng viết unit test cho các hành vi nghiệp vụ cốt lõi.
*   **Tiêu cực (Negative):**
    *   Tăng độ phức tạp khi cấu hình ánh xạ dữ liệu (Data Mapping) giữa thực thể miền và cơ sở dữ liệu thông qua Entity Framework Core.

## Phương án thay thế đã cân nhắc
1.  **Duy trì Anemic Domain Model:** Loại bỏ vì làm tăng nguy cơ phát sinh trạng thái dữ liệu không hợp lệ ở các tầng trên và làm giảm tính học thuật của đồ án.
