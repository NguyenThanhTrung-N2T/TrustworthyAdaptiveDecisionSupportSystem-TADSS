# ADR-001: Sử dụng mẫu thiết kế Strategy Pattern cho Định tuyến Chiến lược Quyết định

## Trạng thái
Đã duyệt (Accepted — Đồ án 2)

## Bối cảnh
Luồng đặt sân trong hệ thống SmashCourt cần tích hợp các chính sách quyết định động (như tỷ lệ đặt cọc theo khung giờ cao điểm/thấp điểm và phân hạng khách hàng). Nếu nhúng trực tiếp các quy tắc quyết định động vào SmashCourt backend, hệ thống sẽ bị coupling chặt, khó thay đổi chính sách, khó viết test độc lập và gây rủi ro nghẽn luồng thanh toán khi thành phần tính toán quyết định gặp sự cố.

## Quyết định
Sử dụng mẫu thiết kế **Strategy Pattern** bằng cách định nghĩa interface `IReservationDecisionStrategy` trong thư viện dùng chung `Shared.DecisionContractSDK`. Runtime Engine chỉ giao tiếp thông qua giao diện này và phân giải các cài đặt cụ thể từ DI container (DI-based Strategy Resolution $O(1)$: `DefaultDecisionStrategy` và `MockAlternativeDecisionStrategy` trong Đồ án 2).

## Hệ quả
*   **Tích cực (Positive):**
    *   Decoupling hoàn toàn hệ thống thực thi (Runtime) khỏi logic chính sách cụ thể (Decision Strategy).
    *   Dễ dàng chuyển đổi hoặc đăng ký nhiều chiến lược quyết định (`DefaultDecisionStrategy` ↔ `MockAlternativeDecisionStrategy`, và sẵn sàng mở rộng ở giai đoạn sau mà không sửa đổi Runtime Core).
    *   Kiểm thử độc lập (Testable) đối với từng chiến lược cụ thể mà không ảnh hưởng hệ thống lớn.
*   **Tiêu cực (Negative):**
    *   Cấu trúc code phân lớp chặt chẽ hơn so với việc viết mã lệnh trực tiếp (hard-code).
    *   Yêu cầu cấu hình `StrategyResolver` / DI container để quản lý vòng đời (lifecycle) của các strategy.

## Phương án thay thế đã cân nhắc
1.  **Hard-code trực tiếp logic vào SmashCourt:** Loại bỏ vì gây tight coupling và vi phạm nguyên lý Open-Closed Principle (OCP).
2.  **Sử dụng Rule Engine thương mại chuyên dụng (Drools/RulesEngine):** Loại bỏ vì quá phức tạp (overkill) và tạo phụ thuộc thư viện ngoài vào tầng hợp đồng.
