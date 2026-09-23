# ADR-001: Sử dụng mẫu thiết kế Strategy Pattern cho Lập luận Quyết định

## Trạng thái
Đã duyệt (Accepted)

## Bối cảnh
Luồng đặt sân trong hệ thống SmashCourt cần tích hợp các chính sách cọc động dựa trên AI. Nếu nhúng trực tiếp mã nguồn AI hoặc các rules động vào SmashCourt backend, hệ thống sẽ bị coupling chặt, khó thay đổi thuật toán, khó viết test độc lập và gây rủi ro nghẽn luồng thanh toán khi AI Service gặp sự cố.

## Quyết định
Sử dụng mẫu thiết kế **Strategy Pattern** bằng cách định nghĩa interface `IReservationDecisionStrategy` trong thư viện dùng chung. Runtime Engine chỉ giao tiếp thông qua giao diện này và phân giải các cài đặt cụ thể từ DI container (DI-based Strategy Resolution).

## Hệ quả
*   **Tích cực (Positive):**
    *   Decoupling hoàn toàn hệ thống thực thi (Runtime) khỏi chiến lược nghiên cứu (Research Strategy).
    *   Dễ dàng chuyển đổi hoặc chạy song song nhiều chiến lược quyết định (Default ↔ Adaptive).
    *   Kiểm thử độc lập (Testable) đối với từng chiến lược cụ thể mà không ảnh hưởng hệ thống lớn.
*   **Tiêu cực (Negative):**
    *   Cấu trúc code phức tạp hơn so với việc viết mã lệnh trực tiếp (hard-code).
    *   Yêu cầu cấu hình Factory / DI container để quản lý vòng đời (lifecycle) của các strategy.

## Phương án thay thế đã cân nhắc
1.  **Hard-code trực tiếp logic vào SmashCourt:** Loại bỏ vì không đáp ứng tính linh hoạt và làm giảm chất lượng nghiên cứu của khóa luận.
2.  **Sử dụng Rule Engine chuyên dụng (Drools/RulesEngine):** Loại bỏ vì quá phức tạp (overkill) đối với phạm vi của đồ án tốt nghiệp.
