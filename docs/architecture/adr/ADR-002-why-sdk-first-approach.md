# ADR-002: Tiếp cận theo hướng SDK-First (Decision Contract SDK)

## Trạng thái
Đã duyệt (Accepted)

## Bối cảnh
Cả ba thành phần phần mềm (Case Study App, Runtime Platform và các Decision Strategies) đều cần trao đổi thông tin đặt chỗ và kết quả quyết định. Nếu định nghĩa riêng biệt cấu trúc dữ liệu ở từng dự án, nguy cơ bất đồng bộ dữ liệu (data model drift) khi thay đổi schema là rất lớn, làm tăng chi phí bảo trì và kiểm thử.

## Quyết định
Sử dụng tiếp cận **SDK-First** bằng cách tạo dự án thư viện dùng chung **Shared.DecisionContractSDK** làm nhân dùng chung (Shared Kernel). Dự án này chứa tất cả các định nghĩa về Domain Entities (`Reservation` Aggregate Root), Value Objects, Enums, DTOs (`DecisionRequestDto`, `DecisionResultDto`, `DecisionDetailDto`, DTO override/hàng chờ), mã lỗi (`ErrorCodes`) và Interface của Strategy. SDK chỉ phụ thuộc BCL (0 NuGet).

## Điều chỉnh khi re-baseline (01/10/2026)
SDK v1.0.0 đóng băng tại Milestone 2. Do v1 không biểu diễn được nghiệp vụ cọc, SDK được nâng lên v2.0.0 và đóng băng lại tại Milestone 5 — xem ADR-007 trong [`ARCHITECTURE_DECISION_RECORDS.md`](../ARCHITECTURE_DECISION_RECORDS.md) và [`INTEGRATION_SPEC.md`](../../contracts/INTEGRATION_SPEC.md).

## Hệ quả
*   **Tích cực (Positive):**
    *   Thống nhất một mô hình dữ liệu chuẩn hóa (Canonical Data Model) cho mọi phân hệ.
    *   Tăng tính di động (Portability), cho phép tái sử dụng trực tiếp SDK sang các ứng dụng đặt chỗ khác (ví dụ: Coworking, Phòng khám) mà không cần định nghĩa lại hợp đồng dữ liệu.
    *   Loại bỏ trùng lặp mã nguồn (DRY Principle).
*   **Tiêu cực (Negative):**
    *   Tạo ra một điểm phụ thuộc chung. Bất kỳ thay đổi mang tính phá vỡ (breaking changes) nào trong SDK đều yêu cầu biên dịch lại cả 3 project.

## Phương án thay thế đã cân nhắc
1.  **Sử dụng JSON thô (Dynamic JSON/Dictionary) cho mọi giao tiếp:** Loại bỏ vì làm mất tính an toàn kiểu (Type Safety), dễ phát sinh bug khi gõ sai tên trường trong runtime.
2.  **Copy-paste code DTO giữa các project:** Loại bỏ vì vi phạm nghiêm trọng nguyên lý thiết kế và làm giảm tính chuyên nghiệp của mã nguồn.
