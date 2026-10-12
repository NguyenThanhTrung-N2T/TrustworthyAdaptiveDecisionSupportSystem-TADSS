| ĐẠI HỌC QUỐC GIA TP. HỒ CHÍ MINH<br>TRƯỜNG ĐẠI HỌC<br>CÔNG NGHỆ THÔNG TIN | CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM<br>Độc Lập - Tự Do - Hạnh Phúc |
| :---: | :---: |

# ĐỀ CƯƠNG CHI TIẾT

**TÊN TIẾNG VIỆT:** Thiết kế và xây dựng nền tảng runtime engine ra quyết định chịu lỗi cho hệ thống đặt chỗ dịch vụ.

**TÊN TIẾNG ANH:** Design and Development of a Fault-Tolerant Decision Runtime Engine for Service Reservation Systems.

**Cán bộ hướng dẫn:** ThS. Trần Anh Dũng

**Thời gian thực hiện:** Từ 07/09/2026 đến 26/12/2026 (16 tuần)

**Sinh viên thực hiện:**

- Nguyễn Thành Trung – 23521683
- Đoàn Quốc Tuấn – 23521709

**Nội dung đề tài:**

## 1. Lý do chọn đề tài

- **Tầm quan trọng của quyết định nghiệp vụ:** Trong các hệ thống đặt chỗ dịch vụ theo thời gian thực (như đặt sân thể thao, phòng họp, phòng khám hay khách sạn), các quyết định về chính sách vận hành như tỷ lệ đặt cọc hay giới hạn hủy lịch đóng vai trò sống còn đối với doanh thu và hiệu quả kinh doanh.
- **Hạn chế của hệ thống SmashCourt cũ (Đồ án 1):** Ở giai đoạn Đồ án 1, hệ thống SmashCourt đã hoàn thiện luồng nghiệp vụ đặt sân trực tuyến tích hợp thanh toán VNPay. Tuy nhiên, luồng nghiệp vụ này hiện đang áp đặt một chính sách tĩnh duy nhất: yêu cầu đặt cọc cố định 100% đối với mọi khách hàng và khung giờ. Hệ thống thiếu một nền tảng thực thi quyết định tổng quát một cách linh hoạt.
- **Yêu cầu thực tế và rủi ro kỹ thuật phần mềm:** Vận hành thực tế đòi hỏi các chính sách cọc phải thích ứng linh hoạt (tăng cọc với khách hay bùng lịch, giảm cọc cho khách thân thiết giờ thấp điểm, cần người quản lý xem xét các đơn giá trị lớn). Việc nhúng trực tiếp các logic thay đổi liên tục này vào hệ thống CRUD của SmashCourt gây ra 3 thách thức lớn: Tight Coupling (ràng buộc chặt nghiệp vụ và logic quyết định), Single Point of Failure (sự cố từ dịch vụ quyết định gây sập toàn bộ luồng đặt chỗ chính), và Lack of Auditability & Human Control (thiếu vết lưu quyết định gốc và cổng can thiệp thủ công của con người).
- **Ý nghĩa giải pháp:** Việc thiết kế và xây dựng một nền tảng runtime engine độc lập, chịu lỗi tốt mang tên TADSS (Trustworthy Adaptive Decision Support System) làm môi trường trung gian an toàn chạy các chiến lược quyết định nghiệp vụ là cần thiết và cấp thiết.

## 2. Mục tiêu

- **Mục tiêu tổng quát:** Thiết kế và xây dựng nền tảng runtime engine độc lập TADSS hỗ trợ thực thi và kiểm soát các chiến lược ra quyết định. TADSS hoàn toàn tách biệt khỏi các thuật toán cụ thể; các chiến lược được trừu tượng hóa qua giao diện hợp đồng dùng chung và có thể thay đổi dễ dàng ở mức cấu hình/triển khai mà không cần chỉnh sửa lõi runtime core. Đồng thời kiểm chứng TADSS trên hạ tầng thật và tích hợp vào luồng đặt sân của SmashCourt để chứng minh khả năng vận hành thực tế.
- **Mục tiêu cụ thể:**
  - **Decision Contract SDK:** Thiết kế thư viện Class Library dùng chung chứa Domain Model chuẩn hóa, các interfaces và DTOs. Bản v2 bổ sung số tiền cọc, đặt nhiều sân trong một đơn, khách vãng lai, lịch sử khách và múi giờ chi nhánh.
  - **Decision Runtime API:** Xây dựng Web API độc lập nhận yêu cầu, định tuyến strategy theo từng yêu cầu dựa trên quy tắc cấu hình (giờ cao điểm, chi nhánh, hạng khách…), trả về trạng thái kèm số tiền cọc; hỗ trợ chế độ shadow để chạy thử strategy mới song song mà không ảnh hưởng kết quả.
  - **Polly v8 Resilience Pipeline:** Tích hợp đường ống chịu lỗi lồng nhau (Outer Timeout 500ms bọc ngoài, Retry tối đa 2 lần cách nhau 30ms, Circuit Breaker, Inner Timeout 120ms mỗi lần thử). Khi strategy lỗi, quá hạn hoặc circuit breaker mở, Fallback trả quyết định an toàn `APPROVED` với cọc 100% (cùng quy tắc với mọi sự cố kỹ thuật khác), không đẩy đơn sang Manager; strategy được chạy cách ly để timeout cắt được cả strategy không hợp tác.
  - **Append-only Audit Trail:** Lưu vết quyết định đồng bộ trong cùng giao dịch PostgreSQL, gồm ba bảng chỉ ghi thêm (quyết định kèm snapshot đầu vào, ghi đè của Manager, kết quả thực tế của đơn đặt); trigger cơ sở dữ liệu chặn mọi thao tác sửa/xóa.
  - **Human-in-the-loop Override API & Dashboard:** Xây dựng cổng API phê duyệt và ghi đè cho Manager (duyệt kèm mức cọc hoặc từ chối), tích hợp cơ chế chống ghi trùng (Idempotency Key) và chống ghi đè đồng thời (mỗi quyết định chỉ có một ghi đè); không bao giờ tự động duyệt.
  - **Feature Flag Integration:** Triển khai Feature Flag giúp bật/tắt nóng luồng gọi TADSS từ SmashCourt tức thời dưới 2 giây.
  - **SmashCourt Integration:** Thanh toán cọc qua VNPay theo số tiền TADSS trả về; màn hình chờ duyệt cho khách; trang duyệt cọc cho Manager; job hủy đơn hết hạn duyệt; job báo kết quả thực tế về TADSS — chứng minh khả năng vận hành chịu lỗi trong kịch bản thực tế.
  - **Bảo mật và triển khai:** Xác thực API key giữa SmashCourt và TADSS, giới hạn tốc độ theo client; đóng gói bằng Docker Compose, đưa ra Internet qua ngrok và giám sát bằng Grafana.
  - **Hệ thống kiểm chứng:** Kiểm thử trên PostgreSQL/Redis thật, tiêm lỗi, kiểm thử tải, mutation testing; đánh giá chất lượng quyết định bằng cách import dữ liệu đặt sân thật đã ẩn danh và dataset công khai; phần kiểm chứng TADSS chạy tự động bằng một lệnh và sinh báo cáo đạt/không đạt để trình bày trước hội đồng.

## 3. Phạm vi

- **Nền tảng hỗ trợ:**
  - **TADSS API Core Engine:** Web API Core viết bằng ASP.NET Core chịu trách nhiệm xử lý logic chịu lỗi, định tuyến chiến lược, cache và lưu vết quyết định.
  - **Manager Dashboard:** Trang duyệt cọc nằm trong ứng dụng web SmashCourt (smashcourt-fe, Next.js), hiển thị danh sách các yêu cầu cọc đang chờ duyệt để Manager can thiệp phê duyệt thủ công; gọi TADSS thông qua SmashCourt-BE.
- **Chức năng chính của hệ thống quyết định TADSS Core (Backend API):**
  - **Thẩm định và phân giải chiến lược:** Tiếp nhận yêu cầu quyết định đặt chỗ qua API `POST /api/v2/decisions`, chọn strategy theo quy tắc định tuyến, trả trạng thái (`APPROVED`, `NEED_APPROVAL`, `REJECTED`) kèm số tiền cọc.
  - **Thực thi chịu lỗi chủ động (Polly):** Áp dụng cơ chế cô lập sự cố, tự động kích hoạt Fallback trả `APPROVED` cọc 100% trong vòng ≤ 500ms khi strategy lỗi, timeout hoặc circuit breaker mở.
  - **Lưu vết Audit Trail đồng bộ:** Ghi lại toàn bộ lịch sử ra quyết định, snapshot đầu vào cùng các thông số kỹ thuật (thời gian thực thi, trạng thái cache, lý do fallback nếu có).
  - **Request-Hash Caching:** Chuẩn hóa và băm SHA-256 toàn bộ thuộc tính request để làm khóa cache trên Redis kết hợp bộ nhớ trong; strategy tự khai báo có cho phép cache và thời gian lưu.
  - **Tra cứu và phê duyệt:** `GET /api/v2/decisions/{id}` (chi tiết quyết định), `GET /api/v2/decisions/pending-approvals` (hàng chờ duyệt theo chi nhánh), `POST /api/v2/decisions/{id}/override` (duyệt kèm mức cọc hoặc từ chối, có Idempotency Key, bắt buộc lý do và mã người duyệt), `POST /api/v2/decisions/{id}/outcome` (ghi nhận kết quả thực tế của đơn).
  - **Bảo mật và vận hành:** API key, giới hạn tốc độ, giới hạn kích thước request; kiểm tra sức khỏe hệ thống (`/health/live`, `/health/ready`).
- **Chức năng chính của trang quản trị quyết định (Manager Dashboard):**
  - **Giám sát hàng đợi duyệt:** Hiển thị thời gian thực các booking của chi nhánh rơi vào trạng thái chờ phê duyệt thủ công.
  - **Phê duyệt và Ghi đè (Human-in-the-loop):** Manager có quyền duyệt nhanh theo đề xuất, nhập mức cọc mong muốn hoặc từ chối (bắt buộc nhập lý do can thiệp). Hệ thống tuyệt đối không tự động duyệt khi quá hạn (No Auto-Approve): hết hạn duyệt thì hủy đơn và giải phóng sân để đảm bảo an toàn dòng tiền.
- **Chức năng tích hợp phía ứng dụng nghiệp vụ (SmashCourt):**
  - **Đặt sân theo cọc:** SmashCourt gọi TADSS khi khách đặt sân online; thanh toán VNPay theo số tiền cọc TADSS trả về; phần còn lại thu khi checkout.
  - **Cửa sổ phê duyệt:** Đơn chỉ được chuyển sang chờ duyệt khi còn đủ thời gian để Manager xử lý trước giờ chơi; nếu không đủ, TADSS trả `APPROVED` với cọc 100% để không làm trễ giờ chơi.
  - **Mức cọc theo hạng khách:** Hạng thành viên được xử lý trong strategy của TADSS (khách hạng cao có mức cọc thấp hơn); TADSS là nơi duy nhất quyết định mức cọc.
  - **Van an toàn khẩn cấp (Emergency Valve):** Cho phép tắt cờ `EnableTadssDecision` trên SmashCourt để bypass toàn bộ luồng TADSS, tự động chạy bằng chính sách cọc tĩnh mặc định (100%) khi phát hiện TADSS gặp sự cố nghiêm trọng hoặc không phản hồi.

## 4. Đối tượng sử dụng

- **Khách hàng (Customer):** Người đặt sân cầu lông trên SmashCourt, trải nghiệm luồng thanh toán đặt sân linh hoạt (được áp dụng mức cọc theo hạng thành viên, khung giờ và lịch sử đặt sân) một cách trong suốt, mượt mà; theo dõi trạng thái chờ duyệt nếu đơn cần Manager xem xét.
- **Trưởng chi nhánh (Branch Manager):** Sử dụng trang duyệt cọc trên SmashCourt (chỉ thấy đơn của chi nhánh mình) để theo dõi vết quyết định và thực hiện các hành động phê duyệt/thay đổi mức cọc/từ chối đơn đặt chỗ.
- **Chủ hệ thống (System Owner):** Giám sát trạng thái hoạt động của TADSS qua dashboard Grafana, quản trị cờ Feature Flag, cấu hình strategy và quy tắc định tuyến.

## 5. Phương pháp thực hiện

- **Tìm hiểu công nghệ:**
  - Backend: ASP.NET Core Web API.
  - SDK và Domain: .NET Class Library.
  - Database: PostgreSQL (Neon).
  - Chịu lỗi: Polly v8.
  - Caching: Redis kết hợp In-Memory Cache.
  - Logging và Giám sát: Serilog, OpenTelemetry, Grafana.
  - Frontend Dashboard: Next.js.
  - Triển khai: Docker Compose, ngrok.
- **Tìm hiểu công cụ hỗ trợ:**
  - Quản lý công việc: Google Meet (trao đổi ý kiến online), Notion (theo dõi tiến độ công việc).
  - Quản lý mã nguồn: GitHub (version control).
  - CI/CD: GitHub Actions.
  - Kiểm thử: Postman, Swagger (kiểm thử API); xUnit (kiểm thử đơn vị, tích hợp và giả lập sự cố); k6 (kiểm thử tải); Playwright (kiểm thử giao diện). Chi tiết ở Kế hoạch kiểm thử (Mục 8).
- **Tìm hiểu và áp dụng Hệ thống hỗ trợ Quyết định chịu lỗi (TADSS):**
  - **Nền tảng Runtime Engine ra quyết định (TADSS Core):**
    - **Mục tiêu:** Xây dựng lõi trung gian ra quyết định thẩm định cọc độc lập, đảm bảo an toàn vận hành và không làm gián đoạn luồng giao dịch đặt chỗ.
    - **Phạm vi áp dụng:**
      - Thẩm định mức cọc cho các đơn đặt chỗ (giờ thường, giờ cao điểm, theo hạng khách).
      - Tự động áp dụng cọc 100% khi strategy gặp sự cố (Fallback `APPROVED` cọc 100%); chỉ chuyển đơn sang chờ Manager duyệt khi nghiệp vụ cần người xem xét (`NEED_APPROVAL`).
      - Cung cấp cơ chế cho Quản lý phê duyệt hoặc ghi đè quyết định (Human-in-the-loop).
    - **Cách tiếp cận:**
      - Trừu tượng hóa chiến lược ra quyết định qua giao diện chung, phân giải qua Dependency Injection; định tuyến strategy theo từng yêu cầu và chạy shadow để so sánh strategy mới.
      - Thiết lập chuỗi chịu lỗi đa tầng (Timeout → Retry → Circuit Breaker → Fallback về quyết định an toàn cọc 100%).
      - Lưu vết nhật ký thực thi bất biến (Append-only Audit Trail) đồng bộ trong giao dịch cơ sở dữ liệu.
      - Ghi log có cấu trúc kèm Correlation ID và OpenTelemetry để đo lường độ trễ và truy vết xuyên suốt luồng gọi API.
      - Áp dụng bộ nhớ đệm Request-Hash (chuẩn hóa dữ liệu và băm SHA-256) để tăng tốc độ phản hồi.
      - Kiểm soát tranh chấp dữ liệu đồng thời và chống gửi lệnh trùng lặp khi Quản lý thao tác ghi đè.
      - Sử dụng Feature Flag để bật/tắt hoặc bỏ qua hệ thống ra quyết định khi khẩn cấp.
      - Kiểm chứng trên hạ tầng thật, tiêm lỗi có kiểm soát và tự động hóa đánh giá bằng một lệnh.
- **Thu thập yêu cầu:**
  - **Kế thừa nghiệp vụ:** Kế thừa toàn bộ quy trình đặt sân, thanh toán và dữ liệu từ hệ thống SmashCourt (Đồ án 1).
  - **Đặc tả giao tiếp API:** Xây dựng tài liệu chuẩn hóa cấu trúc dữ liệu gửi và nhận giữa SmashCourt và TADSS (OpenAPI, JSON Schema).
  - **Phân định ranh giới dữ liệu:** Tách biệt bảng dữ liệu của TADSS sang schema riêng `tadss` để không ảnh hưởng đến cơ sở dữ liệu chính.
  - **Dữ liệu kiểm chứng:** 215 đơn đặt sân thật của SmashCourt (đã ẩn danh) và dataset công khai Hotel Booking Demand.
- **Quy trình phát triển (Agile):**
  - Phân tích yêu cầu và đặc tả hợp đồng dữ liệu (API Contract).
  - Xây dựng thư viện dùng chung (SDK) và thiết kế cơ sở dữ liệu.
  - Cài đặt Decision Runtime API và các chiến lược quyết định mẫu.
  - Xây dựng giao diện phê duyệt (Dashboard) và tích hợp vào hệ thống đặt sân.
  - Kiểm thử, đánh giá bằng chế độ đánh giá một lệnh và đánh giá chất lượng quyết định trên dữ liệu import.

## 6. Nền tảng công nghệ

- **Ngôn ngữ lập trình:**
  - Manager Dashboard: TypeScript (Next.js).
  - Backend và SDK: C# (ASP.NET Core 8).
  - Báo cáo minh chứng và xử lý dữ liệu: Python.
- **Xây dựng ứng dụng web (Manager Dashboard):**
  - Framework: Next.js (React).
  - Thư viện: TailwindCSS (giao diện), Axios (gọi API), React Hook Form (xử lý form duyệt cọc).
- **Cơ sở dữ liệu và Caching:**
  - Cơ sở dữ liệu: PostgreSQL (Neon), schema riêng `tadss`; trigger PostgreSQL chặn sửa/xóa bảng lưu vết.
  - ORM: Entity Framework Core (ASP.NET Core).
  - Bộ nhớ đệm: Redis kết hợp In-Memory Cache (lưu trữ kết quả Request-Hash Cache).
- **Backend và Runtime Engine:**
  - **Decision Runtime API (ASP.NET Core Web API, Clean Architecture nhiều project):**
    - Định tuyến và phân giải chiến lược quyết định theo từng yêu cầu qua Dependency Injection; chế độ shadow.
    - Tích hợp chuỗi chịu lỗi đa tầng Polly v8 (Timeout, Retry, Circuit Breaker, Fallback về `APPROVED` cọc 100%).
    - Lưu vết kiểm toán bất biến (Audit Trail) ghi đồng bộ trong cùng giao dịch CSDL.
    - Cung cấp API cho Quản lý duyệt, ghi đè quyết định và ghi nhận kết quả thực tế (Human-in-the-loop).
  - **Decision Contract SDK (.NET Class Library):**
    - Đóng gói Domain Model `Reservation` và giao diện `IReservationDecisionStrategy`.
  - **Các chiến lược quyết định mẫu (mỗi chiến lược là một project riêng, chỉ phụ thuộc SDK):**
    - `DefaultDecisionStrategy`: cọc theo hạng thành viên (Standard 50%, Silver 40%, Gold 30%, Premium 20%).
    - `MockAlternativeDecisionStrategy`: cọc theo khung giờ (cao điểm 50%, thấp điểm 30%).
    - Quyết định dự phòng `FallbackDecisionPolicy` (thuộc runtime, không phải strategy): trả `APPROVED` cọc 100% khi strategy gặp sự cố.
- **Giao tiếp hệ thống:**
  - RESTful API v2 giữa ứng dụng đặt sân SmashCourt và TADSS Core, đặc tả bằng OpenAPI và JSON Schema.
  - Bảo mật: API key giữa SmashCourt và TADSS, giới hạn tốc độ theo client (ASP.NET Core Rate Limiting).
  - Feature Flag hỗ trợ bật/tắt hoặc bỏ qua hệ thống ra quyết định khi khẩn cấp.
- **Giám sát hệ thống:**
  - Serilog (log có cấu trúc kèm Correlation ID).
  - OpenTelemetry (thu thập metric, trace, log) và Grafana (dashboard độ trễ, fallback, cache, circuit breaker).
- **Công cụ khác:**
  - CI/CD: GitHub Actions (build, kiểm thử tự động, quét lỗ hổng thư viện), Dependabot.
  - Container và triển khai: Docker, Docker Compose (chạy TADSS cùng ngrok và Grafana bằng một lệnh), ngrok (đưa TADSS ra Internet).
  - Kiểm thử & giám sát: Swagger, Postman (kiểm thử API); các công cụ kiểm thử tự động ở Kế hoạch kiểm thử (Mục 8).
  - Quản lý nhiệm vụ và tiến độ: Google Meet, Notion.

## 7. Kết quả mong đợi

- **Thời gian xử lý thành công (Latency p95):** p95 độ trễ của TADSS < 150ms ở tải 50 request/giây trên PostgreSQL/Redis thật; p95 thời gian tạo đơn đặt sân đầu–cuối qua SmashCourt < 1000ms.
- **Thời gian xử lý kịch bản lỗi (Overall SLA Budget):** 100% yêu cầu có thời gian xử lý pipeline ≤ 500ms khi xảy ra lỗi, timeout hoặc phải thử lại và kích hoạt fallback nhờ Polly Outer Timeout; p99 thời gian phản hồi ≤ 700ms.
- **Tính toàn vẹn của Audit Trail:** Mỗi phản hồi thành công có đúng một bản ghi lưu vết; 0 lần sửa/xóa lưu vết thành công.
- **Cơ chế chống ghi trùng & Tranh chấp:** 0 quyết định trùng khi gửi lặp cùng Idempotency Key; 0 ghi đè kép khi nhiều Manager thao tác đồng thời.
- **Khả năng tự reload Feature Flag:** Nhận diện sự thay đổi trạng thái bật/tắt của cờ `EnableTadssDecision` tại Backend SmashCourt thông qua `IOptionsMonitor` trong vòng < 2 giây không cần restart máy chủ; khi tắt, 100% đơn đặt áp cọc 100%.
- **Không hỏng đơn đặt sân:** 0 đơn đặt sân bị hỏng sau mọi kịch bản sự cố.
- **Tính độc lập tên miền (Domain Portability):** Xử lý được ≥ 10.000 đơn đặt phòng khách sạn từ dataset công khai Hotel Booking Demand gửi về TADSS mà không cần sửa một dòng mã nguồn lõi nào của TADSS.
- **Chất lượng kiểm thử:** 100% test case đạt; mutation score ≥ 70%.
- **Đánh giá trên dữ liệu thật:** Có bảng so sánh chính sách cọc của TADSS với chính sách cũ (cọc 100% cho mọi đơn) trên 215 đơn đặt sân thật của SmashCourt (đã ẩn danh).
- **Minh chứng:** Báo cáo kết quả kiểm thử sinh tự động bằng một lệnh, kèm dữ liệu gốc có thể kiểm tra lại.

## 8. Kế hoạch thực hiện

| Giai đoạn | Thời gian | Công việc | Kết quả |
| :--- | :--- | :--- | :--- |
| **Phase 1: Khởi tạo nền tảng & Thiết kế kiến trúc** (đã hoàn thành) | Tuần 1<br>(07/09 – 13/09/2026) | - Xác định phạm vi và ranh giới kiến trúc hệ thống TADSS.<br>- Thiết kế tài liệu đặc tả API Contract (JSON Request/Response).<br>- Thiết kế CSDL schema `tadss` và bảng lưu vết quyết định.<br>- Khởi tạo Solution .NET 8, cấu hình Central Package Management, CI build và Docker. | - Tài liệu kiến trúc & API Contract nháp.<br>- DDL CSDL schema `tadss`.<br>- Solution .NET 8 build thành công với đầy đủ cấu trúc các project. |
| **Phase 2: Xây dựng Domain Kernel & SDK dùng chung** (đã hoàn thành) | Tuần 2<br>(14/09 – 20/09/2026) | - Xây dựng Rich Domain Model `Reservation` kèm các quy tắc Invariants.<br>- Định nghĩa các Enums và DTO.<br>- Định nghĩa Interface `IReservationDecisionStrategy`.<br>- Đóng băng hợp đồng SDK v1. | - Thư viện `Shared.DecisionContractSDK` hoàn chỉnh.<br>- Unit Tests kiểm tra Invariants đạt 100%, độ phủ SDK 99,49%. |
| **Phase 3: Xây dựng Runtime Engine & Phân giải Chiến lược** (đã hoàn thành) | Tuần 3<br>(21/09 – 27/09/2026) | - Xây dựng Web API tiếp nhận yêu cầu quyết định.<br>- Phân giải chiến lược theo tên qua DI.<br>- Xây dựng 2 chiến lược mẫu `DefaultDecisionStrategy` và `MockAlternativeDecisionStrategy`.<br>- Idempotency, Correlation ID, lỗi chuẩn RFC 7807. | - Decision Runtime API hoạt động độc lập.<br>- Phân giải và thực thi chính xác 2 chiến lược qua API endpoint. |
| **Phase 4: Tích hợp Chịu lỗi, Lưu vết và Bộ nhớ đệm** (đã hoàn thành) | Tuần 4<br>(28/09 – 04/10/2026) | - Thiết lập chuỗi chịu lỗi đa tầng Polly v8 (Timeout → Retry → Circuit Breaker → Fallback).<br>- Lưu vết kiểm toán bất biến đồng bộ trong DB Transaction.<br>- Triển khai Request-Hash Cache (Redis 7 kết hợp In-Memory Fallback).<br>- Kiểm thử tiêm lỗi chiến lược. | - Hệ thống tự động kích hoạt Fallback khi chiến lược lỗi/quá tải.<br>- Cache hit giảm độ trễ phản hồi.<br>- 510 test tự động đạt. |
| **Phase 5: Hoàn thiện TADSS & Hạ tầng kiểm chứng** | Tuần 5 – 8<br>(05/10 – 01/11/2026) | - Rà soát, điều chỉnh kế hoạch.<br>- Contract v2 (trả số tiền cọc), tái cấu trúc Clean Architecture nhiều project.<br>- Chuyển kiểm thử sang PostgreSQL/Redis thật (Testcontainers).<br>- API phê duyệt, ghi đè, ghi nhận kết quả thực tế; bảo mật API key, giới hạn tốc độ.<br>- Docker Compose, ngrok.<br>- Định tuyến strategy theo từng yêu cầu, chế độ shadow.<br>- OpenTelemetry và dashboard Grafana.<br>- Chế độ đánh giá một lệnh (tiêm lỗi, kiểm thử tải, kiểm thử contract, mutation testing, sinh báo cáo).<br>- Script import dữ liệu đánh giá: 215 đơn đặt sân thật và dataset khách sạn. | - TADSS hoàn chỉnh, contract v2 đóng băng.<br>- Toàn bộ test case của TADSS đạt.<br>- Lượt đánh giá đầy đủ đầu tiên sinh báo cáo minh chứng.<br>- API contract giữa SmashCourt-BE và smashcourt-fe đã chốt. |
| **Phase 6: Tích hợp TADSS vào SmashCourt (BE ∥ FE)** | Tuần 9 – 12<br>(02/11 – 29/11/2026) | - SV1 (SmashCourt-BE): đặt sân theo cọc, thanh toán VNPay theo số tiền cọc, Feature Flag bật/tắt nóng, API cho Manager, job hết hạn duyệt, job báo kết quả về TADSS.<br>- SV2 (smashcourt-fe): thiết kế UI/UX, màn hình đặt sân của khách, trang duyệt cọc cho Manager. | - SmashCourt-BE và smashcourt-fe hoàn thành tính năng.<br>- Test BE và FE đạt. |
| **Phase 7: Tích hợp hệ thống** | Tuần 13<br>(30/11 – 06/12/2026) | - Ghép FE ↔ BE ↔ TADSS chạy bằng Docker Compose.<br>- Kiểm thử nhanh các luồng đầu–cuối (smoke test).<br>- Đóng băng tính năng (Feature Freeze). | - Các luồng chính chạy thông: duyệt tự động, chờ duyệt → duyệt, từ chối, hết hạn, tắt Feature Flag, TADSS sự cố. |
| **Phase 8: Kiểm thử & Đánh giá** | Tuần 14 – 15<br>(07/12 – 20/12/2026) | - Chạy đầy đủ kế hoạch kiểm thử (bảng bên dưới) bằng chế độ đánh giá một lệnh; quan sát trực tiếp trên Grafana.<br>- Kiểm thử đầu–cuối trên giao diện SmashCourt.<br>- Import dữ liệu đặt sân thật và dataset khách sạn để đánh giá chất lượng quyết định.<br>- Chỉ sửa lỗi; mỗi lần sửa chạy lại test liên quan. | - 100% test case đạt.<br>- Đo đủ các chỉ số ở Mục 7 (Kết quả mong đợi).<br>- Báo cáo HTML kết quả kiểm thử kèm dữ liệu minh chứng. |
| **Phase 9: Đóng gói, Báo cáo & Bảo vệ** | Tuần 16<br>(21/12 – 26/12/2026) | - Viết báo cáo tổng kết Đồ án 2 (số liệu lấy từ báo cáo kiểm thử).<br>- Slide, video demo (đặt sân → TADSS → VNPay → duyệt → tiêm lỗi quan sát trên Grafana).<br>- Diễn tập bảo vệ. | - Mã nguồn hoàn thiện trên GitHub.<br>- Báo cáo Đồ án 2 chi tiết, slide, video demo. |

**Kế hoạch kiểm thử (Phase 8):**

Toàn bộ kiểm thử do nhóm tự thực hiện ở mức phát triển, không có giai đoạn thử nghiệm với người dùng thật. Phần kiểm thử TADSS chạy tự động bằng chế độ đánh giá một lệnh trên môi trường Docker riêng (PostgreSQL, Redis riêng, không đụng cơ sở dữ liệu thật), quan sát trực tiếp trên Grafana và sinh báo cáo HTML đạt/không đạt kèm dữ liệu gốc; chất lượng quyết định được đánh giá bằng dữ liệu import.

| Tên | Nội dung kiểm thử | Công cụ |
| :--- | :--- | :--- |
| Kiểm thử đơn vị | Kiểm tra từng hàm, từng lớp nhỏ khi chạy riêng lẻ: tính tiền cọc theo hạng khách và khung giờ (ví dụ khách Gold đặt sân 200.000đ → cọc 30% = 60.000đ), cửa sổ phê duyệt (đặt sát giờ chơi → cọc 100%), chọn strategy, băm request làm khóa cache, chuyển lỗi thành mã lỗi. Đạt khi 100% test case đúng. | xUnit |
| Đánh giá chất lượng bộ test (mutation testing) | Công cụ tự cài lỗi giả vào mã nguồn (đổi mức cọc 30% thành 0%, đổi `>` thành `>=`…) rồi chạy lại bộ test; test phải báo sai thì mới tính là bắt được lỗi. Đạt khi ≥ 70% lỗi giả bị phát hiện (mutation score ≥ 70%). | Stryker.NET |
| Kiểm thử tích hợp | Gọi API của TADSS với PostgreSQL và Redis thật dựng tự động trong container: gửi cùng một request hai lần → chỉ tạo một quyết định; hai Manager cùng duyệt một đơn → chỉ một người thành công; mỗi phản hồi có đúng một bản ghi lưu vết; lệnh sửa/xóa lưu vết bị cơ sở dữ liệu chặn. | xUnit, Testcontainers |
| Kiểm thử hợp đồng API | Kiểm tra TADSS luôn trả đúng định dạng đã thống nhất với SmashCourt: so từng response với JSON Schema; tự sinh hàng nghìn request (thiếu trường, số âm, ngày sai, chuỗi rất dài, ký tự lạ) từ đặc tả OpenAPI rồi gửi vào. Đạt khi không có lỗi 500, không có response sai định dạng, dữ liệu sai trả 400 kèm mã lỗi rõ ràng. | Schemathesis |
| Kiểm thử chịu lỗi (tiêm lỗi) | Cố ý gây sự cố và kiểm tra phản ứng: strategy chậm, treo hoặc ném lỗi → trả `APPROVED` cọc 100% trong ≤ 500ms, không đưa vào hàng chờ duyệt; mất PostgreSQL → trả 503, không để lại bản ghi dở dang; mất Redis → vẫn phục vụ bình thường; client ngắt request giữa chừng → không hỏng dữ liệu; hết sự cố → tự hồi phục. | xUnit, Toxiproxy |
| Kiểm thử tải | Giả lập nhiều yêu cầu cùng lúc: 50 request/giây trong 10 phút; tăng dần 10 → 400 request/giây để tìm ngưỡng chịu tải; 50 request/giây liên tục 60 phút; vừa tải vừa gây sự cố; theo dõi trực tiếp trên Grafana. Đạt khi p95 < 150ms, p99 ≤ 700ms khi có sự cố, bộ nhớ và số kết nối không tăng dần theo thời gian. | k6, Grafana |
| Kiểm thử bảo mật | Kiểm tra chỉ SmashCourt gọi được TADSS và TADSS không bị lạm dụng: thiếu hoặc sai API key → 401; gửi dồn dập vượt giới hạn tốc độ → 429; request quá lớn → 413; rà log không chứa khóa, mật khẩu. | xUnit, k6 |
| Feature Flag & toàn vẹn đơn đặt sân | Tắt cờ `EnableTadssDecision` khi đang có người đặt sân: cờ có hiệu lực < 2 giây không cần khởi động lại, mọi đơn chuyển sang cọc 100%; sau mỗi kịch bản sự cố, kiểm tra đơn đặt, thanh toán, khung giờ trong cơ sở dữ liệu SmashCourt: 0 đơn bị hỏng. | xUnit, SQL |
| Kiểm thử phía SmashCourt (BE và FE) | Kiểm tra phần SmashCourt gọi TADSS: chuyển đơn đặt sân thành request gửi TADSS; xử lý đủ các kết quả (duyệt ngay, cần Manager duyệt, từ chối, cọc 100% khi TADSS gặp sự cố); chức năng duyệt của Manager (chỉ đơn của chi nhánh mình, báo thử lại khi TADSS không phản hồi mà không chuyển sang cọc 100%, bấm hai lần không duyệt hai lần); tự hủy đơn hết hạn duyệt (không bao giờ tự duyệt); báo kết quả thực tế cho TADSS đúng một lần; thông báo realtime. Chạy trên cơ sở dữ liệu thật dựng trong container với TADSS giả lập; giao diện FE chạy trên dữ liệu giả lập theo hợp đồng API. Đạt khi 100% test case đúng. | xUnit, Testcontainers, WireMock.Net, Playwright, MSW |
| Kiểm thử đầu–cuối trên giao diện | Tự động thao tác trên trình duyệt qua cả FE → BE → TADSS: khách đặt sân → thấy mức cọc → thanh toán VNPay (sandbox) → Manager duyệt / từ chối / để hết hạn; thêm các tình huống tắt cờ, TADSS dừng khi đặt sân và khi duyệt, hai Manager cùng duyệt một đơn, bấm duyệt lần hai, phạm vi chi nhánh, cập nhật hàng chờ realtime, đặt sân tại quầy không qua TADSS. Đạt khi mọi kịch bản chạy đúng, không đơn đặt sân nào bị hỏng sau mỗi kịch bản và p95 thời gian tạo đơn < 1000ms. | Playwright, k6 |
| Đánh giá trên dữ liệu đặt sân thật | Import 215 đơn đặt sân thật của SmashCourt (đã ẩn danh), gửi lần lượt vào TADSS và so sánh với chính sách cũ (cọc 100% cho mọi đơn): tỷ lệ đơn duyệt ngay / cần Manager xem xét, mức cọc trung bình theo hạng khách và khung giờ. Kết quả là bảng so sánh hai chính sách. | Script Python |
| Đánh giá độc lập nghiệp vụ | Import ≥ 10.000 đơn đặt phòng khách sạn từ dataset công khai Hotel Booking Demand; script chuyển từng đơn sang định dạng request của TADSS (khách sạn → chi nhánh, ngày nhận – trả phòng → khung thời gian, giá × số đêm → tổng tiền) rồi gửi vào. Đạt khi 100% đơn nhận được quyết định hợp lệ và mã nguồn TADSS không phải sửa dòng nào. | Script Python |
| Kiểm tra tài liệu | Kiểm tự động bộ tài liệu của dự án: mỗi tài liệu được đánh dấu hiện hành hay lịch sử; tài liệu hiện hành không mô tả những thứ đã gỡ như đang có; mọi liên kết nội bộ trỏ đúng; bảng cấu hình khớp mã nguồn. Đạt khi cả bốn kiểm tra đạt. | Python, xUnit |

## 9. Hướng phát triển đề tài

- **Tích hợp Chiến lược Trí tuệ nhân tạo (AI/ML Decision Strategy):** Thay thế các chiến lược quy tắc bằng mô hình Machine Learning thực thụ (như CatBoost / Reinforcement Learning) cắm trực tiếp vào giao diện `IReservationDecisionStrategy`, nhằm tối ưu hóa tỷ lệ cọc và chính sách hủy động theo thời gian thực dựa trên rủi ro của từng khách hàng.
- **Điểm tin cậy và công bằng:** Điểm tin cậy khách hàng có suy giảm theo thời gian; ràng buộc công bằng giữa các nhóm khách hàng; đánh giá chính sách mới trên dữ liệu lưu vết trước khi áp dụng và chạy shadow trên lưu lượng thật.
- **Minh bạch hóa Quyết định (Explainability AI — XAI):** Triển khai cơ chế giải thích quyết định (sử dụng SHAP / Feature Attribution) để tự động sinh lý do, giúp khách hàng và người quản trị hiểu rõ căn cứ tại sao hệ thống đưa ra mức cọc tương ứng.
- **Môi trường Giả lập & Học thích ứng liên tục (Simulator & Continuous Learning):** Xây dựng hệ thống Simulator mô phỏng hành vi đặt/hủy sân để kiểm thử và tinh chỉnh mô hình an toàn trước khi triển khai thực tế; đồng thời tự động tái huấn luyện mô hình dựa trên các thao tác ghi đè (Override) của người quản lý và kết quả thực tế đã lưu vết trong schema `tadss`.
- **Thương mại hóa & Đa nền tảng (Cross-Domain Portability):** Đóng gói lõi TADSS Runtime thành dịch vụ độc lập có thể cắm ghép và tái sử dụng cho nhiều bài toán đặt chỗ khác nhau như đặt phòng khám y tế, đặt phòng khách sạn, co-working space... mà không phải sửa đổi mã nguồn cốt lõi.

---

| Xác nhận của CBHD<br>(Ký tên và ghi rõ họ tên) | TP. HCM, ngày….tháng …..năm…..<br>Nhóm Sinh viên<br>(Ký tên và ghi rõ họ tên) |
| :---: | :---: |
| | Sinh viên 1 &nbsp;&nbsp;&nbsp;&nbsp; Sinh viên 2 |
| ThS. Trần Anh Dũng | Nguyễn Thành Trung &nbsp;&nbsp;&nbsp;&nbsp; Đoàn Quốc Tuấn |
