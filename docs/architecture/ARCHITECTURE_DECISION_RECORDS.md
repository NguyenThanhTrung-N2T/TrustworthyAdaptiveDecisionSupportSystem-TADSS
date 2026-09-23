# ARCHITECTURE DECISION RECORDS (ADR)

Tài liệu này ghi nhận các quyết định kiến trúc cốt lõi của hệ thống **Decision Runtime Platform (TADSS)**, giải thích lý do lựa chọn giải pháp kỹ thuật, ưu nhược điểm và các phương án thay thế.

---

## ADR-001: Sử dụng Strategy Pattern cho việc Decoupling Quyết định

- **Trạng thái:** ĐÃ DUYỆT (APPROVED)
- **Bối cảnh:** Luồng đặt sân trong hệ thống SmashCourt cần tích hợp các chính sách cọc động dựa trên AI. Nếu nhúng trực tiếp mã nguồn AI hoặc các rules động vào SmashCourt backend, hệ thống sẽ bị coupling chặt, khó thay đổi thuật toán, khó viết test và gây rủi ro nghẽn luồng thanh toán khi AI Service sập.
- **Quyết định:** Sử dụng mẫu thiết kế **Strategy Pattern** bằng cách định nghĩa interface `IReservationDecisionStrategy` trong thư viện dùng chung. Runtime Engine chỉ giao tiếp thông qua giao diện này và phân giải qua Dependency Injection (DI-based Strategy Resolution) các cài đặt cụ thể (`DefaultDecisionStrategy`, `TrustworthyAdaptiveDecisionStrategy`).
- **Hệ quả:**
  - _Ưu điểm:_ Decoupling hoàn toàn hệ thống thực thi khỏi thuật toán cụ thể. Dễ dàng chuyển đổi hoặc chạy song song nhiều chiến lược quyết định.
  - _Nhược điểm:_ Cần quản lý cấu hình đăng ký Strategy một cách chặt chẽ qua DI container.

---

## ADR-002: Sử dụng Decision Contract SDK (Shared Kernel)

- **Trạng thái:** ĐÃ DUYỆT (APPROVED)
- **Bối cảnh:** Cả 3 lớp (Case Study App, Runtime Platform, và Research Strategy) đều cần hiểu cấu trúc thông tin đặt chỗ và kết quả quyết định. Nếu định nghĩa riêng lẻ ở từng dự án, nguy cơ bất đồng bộ dữ liệu khi thay đổi schema là cực kỳ lớn.
- **Quyết định:** Tạo thư viện dùng chung **Decision Contract SDK** (Shared Kernel Library) chứa tất cả các định nghĩa về Domain Entities (`Reservation`), Value Objects, Enums và DTOs.
- **Hệ quả:**
  - _Ưu điểm:_ Thống nhất "ngôn ngữ chung" (Ubiquitous Language) giữa các phân hệ. Đảm bảo tính di động (Portability), cho phép tái sử dụng SDK ở các domain khác (như Coworking).
  - _Nhược điểm:_ Bất kỳ thay đổi mang tính phá vỡ (Breaking changes) nào trong SDK đều yêu cầu biên dịch lại cả 3 project.

---

## ADR-003: Sử dụng Polly v8 Resilience Pipeline cho khả năng chịu lỗi

- **Trạng thái:** ĐÃ DUYỆT (APPROVED)
- **Bối cảnh:** Mô hình AI (CatBoost/Gemini) chạy trên FastAPI service độc lập có khả năng bị quá tải hoặc sập mạng, gây trễ luồng đặt sân trực tuyến của khách hàng.
- **Quyết định:** Triển khai **Polly v8 Resilience Pipeline** liên kết chặt chẽ các chiến lược: `Outer Timeout` (Giới hạn tổng SLA tối đa 500ms) ➔ `Retry` (Thử lại tối đa 2 lần = 3 attempts tổng cộng) ➔ `Inner Timeout` (Khống chế thời gian xử lý từng lượt tối đa 150ms) ➔ `Circuit Breaker` (Mở mạch khi tỷ lệ lỗi vượt ngưỡng trong cửa sổ đánh giá 30 giây, tối thiểu 10 requests) ➔ `Fallback` (Chuyển sang dùng chiến lược cọc tĩnh mặc định với tỷ lệ cọc 100%).
- **Hệ quả:**
  - _Ưu điểm:_ Bảo vệ tối đa tính sẵn sàng của luồng đặt chỗ. Hệ thống tự động phục hồi khi dịch vụ AI ổn định trở lại.
  - _Nhược điểm:_ Khách hàng có thể nhận kết quả cọc mặc định (Fallback) trong thời gian AI bị cô lập.

---

## ADR-004: Sử dụng Rich Domain Model cho thực thể Reservation

- **Trạng thái:** ĐÃ DUYỆT (APPROVED)
- **Bối cảnh:** Reservation Entity ban đầu được thiết kế chỉ chứa các thuộc tính dữ liệu thuần túy (Anemic Domain Model), dẫn đến việc các logic xác thực hợp lệ (Invariants) bị phân tán rải rác ở nhiều Services bên ngoài, gây khó khăn cho việc bảo trì.
- **Quyết định:** Chuyển đổi sang mô hình **Rich Domain Model** bằng cách đưa các business invariants/behaviors trực tiếp vào thực thể `Reservation` (như `EnsureValid`, `GetDuration`, `CanBeCancelledWithoutPenalty`).
- **Hệ quả:**
  - _Ưu điểm:_ Tập trung hóa logic nghiệp vụ tại chính thực thể sở hữu dữ liệu. Bảo vệ tính toàn vẹn dữ liệu từ lớp lõi.
  - _Nhược điểm:_ Tăng độ phức tạp khi khởi tạo thực thể qua Entity Framework Core (cần cấu hình Mapper cẩn thận).

---

## ADR-005: Lựa chọn CatBoost làm mô hình ML Baseline Classifier

- **Trạng thái:** ĐÃ DUYỆT (APPROVED)
- **Bối cảnh:** Bài toán dự báo no-show yêu cầu một mô hình máy học gọn nhẹ, có khả năng xử lý tốt dữ liệu dạng bảng (tabular data) chứa nhiều thuộc tính phân loại (categorical features như Loyalty Tier, DayType, Chi nhánh).
- **Quyết định:** Lựa chọn thuật toán **CatBoost (Categorical Boosting)** làm baseline classifier.
- **Hệ quả:**
  - _Ưu điểm:_ CatBoost tự động xử lý các biến phân loại mà không cần qua bước One-Hot Encoding phức tạp. Độ chính xác cao và không bị overfit trên tập dữ liệu dạng bảng.
  - _Nhược điểm:_ Yêu cầu tài nguyên RAM tương đối lớn trong quá trình huấn luyện ban đầu.
