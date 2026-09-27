# ARCHITECTURE DECISION RECORDS (ADR)

Tài liệu này ghi nhận các quyết định kiến trúc cốt lõi của hệ thống **Decision Runtime Platform (TADSS)**, giải thích lý do lựa chọn giải pháp kỹ thuật, ưu nhược điểm và các phương án thay thế.

> **📌 Ghi chú Phạm vi (Scope Lock):** Các quyết định **ADR-001 đến ADR-004** thuộc phạm vi hiện thực hóa trực tiếp của **Đồ án 2 (`TADSS Runtime Engine`)**. Quyết định **ADR-005** là định hướng mở rộng ở tầng Chiến lược Nghiên cứu (**Khóa luận tốt nghiệp — Out-of-Scope đối với Đồ án 2**).

---

## ADR-001: Sử dụng Strategy Pattern cho việc Decoupling Quyết định

- **Trạng thái:** ĐÃ DUYỆT (APPROVED — Đồ án 2)
- **Bối cảnh:** Luồng đặt sân trong hệ thống SmashCourt cần tích hợp các chính sách cọc động linh hoạt theo khung giờ và phân hạng khách hàng. Nếu nhúng trực tiếp các quy tắc quyết định động vào SmashCourt backend, hệ thống sẽ bị coupling chặt, khó thay đổi chiến lược, khó viết test độc lập và gây rủi ro nghẽn luồng thanh toán khi thành phần tính toán quyết định gặp sự cố.
- **Quyết định:** Sử dụng mẫu thiết kế **Strategy Pattern** bằng cách định nghĩa interface `IReservationDecisionStrategy` trong thư viện dùng chung `Shared.DecisionContractSDK`. Runtime Engine chỉ giao tiếp thông qua giao diện này và phân giải qua Dependency Injection (DI-based Strategy Resolution $O(1)$) các cài đặt cụ thể trong Đồ án 2 (`DefaultDecisionStrategy`, `MockAlternativeDecisionStrategy`), đồng thời sẵn sàng cho các chiến lược mở rộng trong tương lai.
- **Hệ quả:**
  - _Ưu điểm:_ Decoupling hoàn toàn hệ thống thực thi khỏi thuật toán/chính sách cụ thể. Dễ dàng chuyển đổi hoặc kiểm thử độc lập nhiều chiến lược quyết định.
  - _Nhược điểm:_ Cần quản lý cấu hình đăng ký Strategy một cách chặt chẽ qua DI container.

---

## ADR-002: Sử dụng Decision Contract SDK (Shared Kernel)

- **Trạng thái:** ĐÃ DUYỆT (APPROVED — Đồ án 2)
- **Bối cảnh:** Cả ứng dụng nghiệp vụ (SmashCourt), nền tảng thực thi (TADSS Runtime), và các gói chiến lược quyết định (Strategies) đều cần hiểu cấu trúc thông tin đặt chỗ và kết quả quyết định. Nếu định nghĩa riêng lẻ ở từng dự án, nguy cơ bất đồng bộ dữ liệu khi thay đổi schema là cực kỳ lớn.
- **Quyết định:** Tạo thư viện dùng chung **Decision Contract SDK** (`Shared.DecisionContractSDK` v1.0.0 — Shared Kernel Library) chứa tất cả các định nghĩa về Domain Entities (`Reservation`), Value Objects, Enums và DTOs.
- **Hệ quả:**
  - _Ưu điểm:_ Thống nhất "ngôn ngữ chung" (Ubiquitous Language) giữa các phân hệ. Đảm bảo tính di động (Portability), cho phép tái sử dụng SDK ở các domain khác (như Coworking Space).
  - _Nhược điểm:_ Bất kỳ thay đổi mang tính phá vỡ (Breaking changes) nào trong SDK đều yêu cầu biên dịch lại các project phụ thuộc (được kiểm soát bằng chính sách Contract Freeze Zero-Mutation).

---

## ADR-003: Sử dụng Polly v8 Resilience Pipeline cho khả năng chịu lỗi

- **Trạng thái:** ĐÃ DUYỆT (APPROVED — Đồ án 2)
- **Bối cảnh:** Khi thực thi các chiến lược ra quyết định phức tạp hoặc phụ thuộc tính toán có độ trễ biến thiên, một chiến lược bị treo (`> 120ms`) hoặc ném ngoại lệ nội bộ có thể làm nghẽn luồng đặt sân trực tuyến của khách hàng nếu không có cơ chế cô lập lỗi.
- **Quyết định:** Triển khai **Polly v8 Resilience Pipeline** 5 tầng liên kết chặt chẽ: `Outer Timeout` (Giới hạn tổng SLA tối đa 500ms) ➔ `Fallback` (Chuyển sang `FallbackDecisionStrategy` trả về trạng thái an toàn `NEED_APPROVAL` để duy trì decision-path continuity) ➔ `Retry` (Thử lại tối đa 2 lần = 3 attempts tổng cộng, base delay 30ms) ➔ `Circuit Breaker` (Mở mạch khi tỷ lệ lỗi vượt ngưỡng trong cửa sổ đánh giá 30 giây, tối thiểu 10 requests) ➔ `Inner Timeout` (Khống chế thời gian xử lý từng lượt tối đa 120ms; ngân sách cấu hình lý thuyết $3 \times 120\text{ms} + 2 \times 30\text{ms} = 420\text{ms} < 500\text{ms}$).
- **Hệ quả:**
  - _Ưu điểm:_ Bảo vệ tối đa tính sẵn sàng của luồng ra quyết định. Hệ thống tự động ngắt mạch nhanh (fast-fail) khi chiến lược lỗi liên tiếp và tự phục hồi khi chiến lược ổn định trở lại.
  - _Nhược điểm:_ Các yêu cầu rơi vào trạng thái sự cố sẽ chuyển sang `NEED_APPROVAL` chờ Quản lý duyệt trong thời gian chiến lược chính bị cô lập.

---

## ADR-004: Sử dụng Rich Domain Model cho thực thể Reservation

- **Trạng thái:** ĐÃ DUYỆT (APPROVED — Đồ án 2)
- **Bối cảnh:** Reservation Entity ban đầu được thiết kế chỉ chứa các thuộc tính dữ liệu thuần túy (Anemic Domain Model), dẫn đến việc các logic xác thực hợp lệ (Invariants) bị phân tán rải rác ở nhiều Services bên ngoài, gây khó khăn cho việc bảo trì.
- **Quyết định:** Chuyển đổi sang mô hình **Rich Domain Model** bằng cách đưa các business invariants/behaviors trực tiếp vào thực thể `Reservation` (như `EnsureValid`, `GetDuration`, `CanBeCancelledWithoutPenalty`).
- **Hệ quả:**
  - _Ưu điểm:_ Tập trung hóa logic nghiệp vụ tại chính thực thể sở hữu dữ liệu. Bảo vệ tính toàn vẹn dữ liệu từ lớp lõi.
  - _Nhược điểm:_ Đòi hỏi thiết lập lớp ánh xạ dữ liệu (Mapper) rõ ràng giữa DTO và Domain Entity.

---

## ADR-005: Định hướng lựa chọn CatBoost cho Chiến lược Mở rộng ở Khóa luận `[OUT-OF-SCOPE ĐỒ ÁN 2]`

- **Trạng thái:** ĐỊNH HƯỚNG MỞ RỘNG CHO KHÓA LUẬN (FUTURE EXTENSION — OUT-OF-SCOPE ĐỒ ÁN 2)
- **Bối cảnh:** Trong giai đoạn mở rộng tiếp theo sau Đồ án 2 (Khóa luận tốt nghiệp — Lớp 3 `TADS`), bài toán dự báo xác suất bùng hẹn trên dữ liệu dạng bảng sẽ cần một mô hình phân loại xử lý tốt các thuộc tính phân loại (categorical features) và có thể đóng gói vào một `IReservationDecisionStrategy` mới mà không phải sửa đổi `TADSS Runtime Engine`.
- **Quyết định (Dành cho Khóa luận):** Định hướng lựa chọn **CatBoost (Categorical Boosting)** làm mô hình phân loại khi triển khai chiến lược mở rộng ở giai đoạn Khóa luận. *(Trong phạm vi Đồ án 2 hiện tại, hệ thống hoàn toàn không huấn luyện hay nhúng mô hình ML/AI mà sử dụng `DefaultDecisionStrategy` và `MockAlternativeDecisionStrategy`).*
- **Hệ quả:**
  - _Ưu điểm:_ Định hình trước các thuộc tính ngữ cảnh trong `Shared.DecisionContractSDK` để đảm bảo khả năng tương thích ngược khi mở rộng lên Khóa luận.
  - _Nhược điểm:_ Không áp dụng trong phạm vi 16 tuần của Đồ án 2 để tránh phình phạm vi (scope creep).
