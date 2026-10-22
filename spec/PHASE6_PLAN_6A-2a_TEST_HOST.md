# Gói 6A-2a — Test host HTTP cho BE-IT

**Mục tiêu:** chạy được BE-IT ở mức HTTP như danh mục yêu cầu: gọi endpoint thật của BE (auth, policy, middleware, envelope), DB là PostgreSQL schema thật, TADSS là WireMock trả response mẫu đã qua JSON schema v2 (CT-04). Hiện project test **chưa có** host này (chỉ có fixture DB ở 6A-1a). Tổng quan: [6A-2](PHASE6_PLAN_6A-2_ONLINE_BOOKING.md).

**Spec:** 9.2 (WireMock theo mẫu CT-04); methodology mục BE-IT (môi trường E-TC). **Package test mới:** `Microsoft.AspNetCore.Mvc.Testing` 8.x, `WireMock.Net`.

---

### Task 1 — Host app thật trên DB test (kèm khóa tắt Hangfire `Hangfire:Enabled`, D4) ✅ xong 23/10

**File:** `Program.cs` (thêm khai báo partial cho `Program` để test tham chiếu được), `Extensions/HangfireExtensions.cs`, `Program.cs` (khóa `Hangfire:Enabled`, mặc định `true`: `false` ⇒ không gọi `AddHangfire`/`AddHangfireServer`/dashboard/`RecurringJob.AddOrUpdate`), `appsettings.json` (khóa mặc định); test: `SmashCourt-BE.Tests/Integration/Http/` (factory, base class, test khói).

**Giải pháp:**
- Factory dựa trên `WebApplicationFactory<Program>`, môi trường `Testing`, **một factory dùng chung cho cả collection** (B6). Dùng lại **container** PostgreSQL của fixture hiện có nhưng **một database riêng** cho bộ HTTP (B5, chốt 22/10): tạo một lần từ `schema_smashcourt.sql`, xóa khi xong, nên test cũ (rollback, quét toàn bảng như Phase 5 equivalence) không bao giờ thấy dữ liệu của bộ HTTP.
- **Cấu hình nạp trước khi host dựng (B2, chốt 22/10):** `Program.cs` đọc chuỗi kết nối (`NpgsqlDataSourceBuilder`, dòng ~40) và JWT (dòng ~358) ngay khi khởi động, trước lúc `WebApplicationFactory` áp `ConfigureAppConfiguration`; vì vậy chuỗi kết nối, JWT và mọi khóa đọc sớm được đặt bằng `UseSetting` hoặc biến môi trường trước khi tạo host. Host **từ chối khởi động** nếu máy chủ trong chuỗi kết nối không phải container test (chặn nối nhầm DB thật). Không đổi `Program.cs` chỉ để phục vụ test ngoài các khóa đã chốt (`Hangfire:Enabled`, rate limit, `partial class Program`).
- **Bộ cấu hình giả đủ (B3, chốt 22/10):** môi trường `Testing` không nạp `appsettings.Development.json`, nên factory tự cấp JWT (khóa, issuer, audience), VNPay (`TmnCode`, `HashSecret`, `CallbackUrl`, đọc bằng `!` nên thiếu là hỏng), SMTP, Cloudinary, AI, TADSS (`BaseUrl` = WireMock), toàn giá trị giả, không khóa thật. `IEmailService` được thay bằng bản giả ghi lại thư đã gửi (thực tế BE không có interface này: xem "Kết quả thực hiện", dùng `RecordingEmailService` kế thừa `EmailService`) (kiểm email D8 kèm link hủy, email duyệt/từ chối ở các gói sau); Cloudinary/AI trỏ vào địa chỉ cục bộ vô hại. Hangfire tắt bằng `Hangfire:Enabled=false` (D4).
- Dữ liệu tạo qua HTTP commit thật (không rollback được): mỗi test seed chi nhánh/sân/khách bằng định danh mới (GUID), không đếm cả bảng, không phụ thuộc thứ tự test; cô lập chính nhờ database riêng ở trên.
- Test khói: app khởi động, Swagger không cần; một GET công khai trả 200; tạo được scope DI để đọc DB; **`SELECT current_database()` qua `SmashCourtContext` của host bằng đúng tên database riêng của bộ HTTP** (chứng minh không nối nhầm DB, B2); thử khởi động với chuỗi kết nối trỏ máy chủ lạ ⇒ host từ chối.

**Test:** khởi động host < 30 s khi image đã có; host không đăng ký job Hangfire nào khi tắt; app thật (không phải test) vẫn đăng ký đủ 9 job khi khóa vắng mặt.

### Task 2 — WireMock giả TADSS và đăng nhập giả ✅ xong 23/10

**File:** `SmashCourt-BE.Tests/Integration/Http/` (stub TADSS, bộ đọc request đã nhận, helper token).

**Giải pháp:**
- Một WireMock server cho cả collection; `TadssConfig:BaseUrl` trỏ vào đó; mỗi test đặt kịch bản: trả mẫu `APPROVED`/`NEED_APPROVAL`/`REJECTED`/fallback từ `TestData/Tadss/v2/samples` (đã kiểm schema ở CT-04), lỗi 5xx/401/413/429, trễ quá `TimeoutMs`. Đọc lại request đã nhận để kiểm header (`X-Idempotency-Key`), số lần gọi, nội dung hợp lệ `decision-request.schema.json`.
- `reservationId`/`decisionId` của response mẫu được thay bằng giá trị của booking đang test (tránh lệch INV, giữ đúng hình dạng mẫu).
- Đăng nhập: phát JWT thật bằng cấu hình JWT của host test cho user seed sẵn (`CUSTOMER`, `STAFF`, `BRANCH_MANAGER`), gắn qua header `Authorization: Bearer` (BE chỉ đọc JWT từ header; query `access_token` chỉ dành cho `/hubs`, B4); không thêm đường tắt xác thực vào mã app.
- Đổi cấu hình lúc chạy (cho BE-IT-19): nguồn cấu hình trong bộ nhớ có thể đặt lại và báo reload để `IOptionsMonitor` thấy.

**Test:** gọi endpoint cần `CUSTOMER` với token khách ⇒ qua policy; không token ⇒ 401; WireMock nhận đúng request khi cờ bật (dùng tạm một endpoint có sẵn hoặc kiểm trực tiếp `ITadssClient` từ DI của host); đổi `EnableTadssDecision` trong test ⇒ `IOptionsMonitor` thấy giá trị mới < 2 s.

### Task 3 — Rate limit đếm theo từng người, cấu hình được (B1/D9, chốt 22/10) ✅ xong 23/10

**Vì sao:** bộ BE-IT HTTP gửi nhiều request đặt sân hơn 20 lượt/phút nên nhận 429 vô cớ; đồng thời đây là lỗi production có từ trước: cả 6 policy ở `Program.cs` (~253) là **một bộ đếm chung toàn hệ thống** (xem spec 9.2). Nặng nhất là `login` 5 lượt/phút chung (người thứ 6 trong phút đăng nhập không được; kẻ gửi liên tục chặn được mọi người đăng nhập) và `sensitive` 10 lượt/phút chung cho 16 endpoint.

**File:** `Program.cs` (thứ tự middleware), `Configurations/RateLimitSettings.cs` + `RateLimitSettingsValidator.cs` (mới, theo mẫu `BookingPaymentSettings`), `Extensions/RateLimitExtensions.cs` (mới, gom phần `AddRateLimiter` khỏi `Program.cs`), `appsettings.json` (giá trị mặc định = giá trị hiện tại); test `SmashCourt-BE.Tests/Configurations/RateLimitSettingsValidatorTests.cs`, `Helpers` hoặc `Extensions` test cho hàm chọn khóa, test HTTP trên host 6A-2a.

**Giải pháp:**
- Hàm thuần chọn khóa phân vùng: `user:{claim NameIdentifier}` nếu `User.Identity.IsAuthenticated`, ngược lại `ip:{RemoteIpAddress}`, thiếu địa chỉ ⇒ `unknown` (một bộ đếm chung cho các request không xác định được nguồn, cố ý chặt thay vì bỏ qua). Mỗi policy dùng `PartitionedRateLimiter` fixed window với khóa này; `QueueLimit = 0` như cũ.
- `app.UseRateLimiter()` chuyển xuống **sau** `app.UseAuthentication()` để biết người dùng; kiểm bằng test HTTP rằng policy đặt bằng attribute vẫn áp đúng (endpoint metadata).
- Giới hạn, cửa sổ đọc từ `RateLimiting:{policy}`; mặc định: login 5, booking 20, sensitive 10, ai-public 100, ai-user 20, ai-management 10 lượt/phút. Giá trị < 1 ⇒ lỗi lúc khởi động (`ValidateOnStart`, như `BookingPaymentSettings`). Host test đặt giá trị cao.
- Không đổi thân phản hồi 429 và không đụng `AspNetCoreRateLimit` của `/api/ai/*` (cơ chế riêng).

**Test:** khóa: đã đăng nhập ⇒ `user:{id}`, hai người khác nhau ⇒ hai khóa, chưa đăng nhập ⇒ `ip:…`, hai IP khác nhau ⇒ hai khóa, không có IP ⇒ `unknown`, claim thiếu dù `IsAuthenticated` ⇒ rơi về IP; cấu hình: mặc định đúng 6 giá trị cũ, 0 và âm bị từ chối, giá trị lạ không làm đổi policy khác; HTTP (host 6A-2a): cùng một người lượt thứ N+1 trong phút ⇒ 429 với thân `ApiResponse` cũ; hai người khác nhau không ảnh hưởng nhau (người B vẫn qua khi A đã hết lượt); `login` 5 lượt từ IP này không chặn IP khác; sang cửa sổ mới thì qua lại (dùng cửa sổ 1 giây trong host test); endpoint không gắn policy không bị giới hạn; thứ tự middleware: yêu cầu có token hợp lệ được đếm theo `user:`, không phải `ip:`.

**Xong khi:** một test HTTP mẫu chạy xanh cùng bộ integration hiện có; thời gian cả bộ HTTP **được đo và ghi lại** vào báo cáo gói (B6, chốt 22/10, thay mục tiêu cứng ~20 s); các ca timeout của TADSS đặt `TimeoutMs` thấp trong host test để không chờ lâu (thực tế 400 ms, xem "Kết quả thực hiện").

---

## Kết quả thực hiện gói 6A-2a (23/10/2026)

**Code production (4 thay đổi nhỏ, mặc định không đổi hành vi trừ rate limit):**
- `Hangfire:Enabled` (`HangfireExtensions.IsEnabled`, mặc định `true`, giá trị không phải bool ⇒ lỗi lúc khởi động): `false` ⇒ không `AddHangfire`/`AddHangfireServer`/dashboard/`RecurringJob`; lớp job vẫn đăng ký để gọi trực tiếp.
- `public partial class Program;` cuối `Program.cs`.
- Rate limit: `Configurations/RateLimitSettings` + `RateLimitSettingsValidator`, `Extensions/RateLimitExtensions` (`RateLimitPartitionKey.Resolve`, `AddSmashCourtRateLimiting`), `RateLimiting` trong `appsettings.json`; `UseRateLimiter` chuyển xuống sau `UseAuthentication`; thân 429 giữ nguyên. Giới hạn đọc qua `IOptionsMonitor` lúc tạo bộ đếm của từng người (đổi cấu hình áp cho bộ đếm tạo sau đó).
- **Lệch so với plan:** plan ghi "thay `IEmailService`" nhưng BE **không có** interface đó (`BookingService`, `PaymentService`... nhận thẳng lớp `EmailService`). Cách ít đụng nhất: `EmailService.SendAsync` (hàm gửi chung, mọi loại email đều đi qua) đổi `private` ⇒ `protected virtual`; host test thay bằng `RecordingEmailService` kế thừa và ghi lại thư. Không đổi hành vi production.

**Hạ tầng test (`SmashCourt-BE.Tests/Integration/Http/`):** `SmashCourtWebApplicationFactory` (cấu hình đọc sớm đặt bằng biến môi trường, khôi phục khi dispose; từ chối chuỗi kết nối không phải máy cục bộ và database không có tiền tố `smashcourt_http_`), `HttpTestHostFixture` (host dựng một lần, lazy, database riêng tạo từ `schema_smashcourt.sql` trong cùng container và xóa khi xong, WireMock TADSS, làm nóng client), `HttpTestBase` (seed user, JWT thật từ `ITokenService`, client có/không đăng nhập, giả IP bằng `X-Forwarded-For`), `TadssStub`, `RecordingEmailService`, `MutableConfigurationSource` (đổi cấu hình lúc chạy có reload).

**Phát hiện khi làm (đã xử lý):**
1. xUnit không tiêm fixture của collection vào fixture khác ⇒ `HttpTestHostFixture` nhận `PostgreSqlIntegrationFixture` qua `HttpTestBase` lúc gọi `GetAsync`.
2. Bản dump schema **không có dòng phiên bản** trong `hangfire.schema` nên Hangfire.PostgreSql coi như chưa cài và chờ khóa tới hết thời gian (test treo > 10 phút). Test đăng ký job dùng database riêng đã `DROP SCHEMA hangfire` để Hangfire tự cài như database mới.
3. `TimeoutMs` của host test là **400 ms**, không phải 100 ms như plan: 100 ms cắt oan lần gọi HTTP đầu (JIT, kết nối nguội). Có làm nóng client một lần.
4. WireMock ghi log **sau** khi trả lời và không ghi request bị cắt giữa chừng ⇒ test đếm request dùng `WaitForEvaluationsAsync` (chờ tới khi đủ, lọc theo booking id); ca timeout kiểm bằng thời gian (hai lần chờ `TimeoutMs`).
5. Endpoint upload chỉ nhận multipart; JSON bị bộ chọn endpoint trả 415 trước cả middleware rate limit.
6. Test khói Hangfire không thể khẳng định "không có schema hangfire" (dump có sẵn) ⇒ khẳng định không có dòng nào trong `server`, `hash`, `set`, `job`.

**Test mới: +147** (1009 tổng): Hangfire 13 + 3 (storage thật, đủ 9 job, chạy hai lần vẫn 9) ; host khói 14; xác thực/phân quyền 16 (token hợp lệ, thiếu, sai role, hết hạn, sai khóa/issuer/audience, hỏng, LOCKED, user không tồn tại, `access_token` trên query bị bỏ qua ngoài `/hubs`, khóa user giữa chừng); TADSS qua WireMock 27 (header, schema request, 4 mẫu response, retry theo mã lỗi, `Retry-After`, timeout, body hỏng, bật/tắt cờ lúc chạy < 2 s, `DecideAsync` đủ nhánh trên DB thật kể cả F1/F2); rate limit 58 đơn vị (cấu hình, validator, khóa) + 16 HTTP (6 policy nối đúng controller, mỗi người một bộ đếm, hai user cùng IP độc lập, cùng user đổi IP vẫn một bộ đếm, nguồn không xác định dùng chung khóa `unknown`, login theo IP, cửa sổ reset, endpoint không policy không bị giới hạn, từ chối ngay không xếp hàng).
**Đột biến: 12/12 bị bắt** (khóa user/IP thành chung, bỏ chuẩn hóa IPv4-mapped, sai đơn vị cửa sổ, xếp hàng thay vì từ chối, tin claim chưa xác thực, đổi mặc định, validator nới lỏng, Hangfire mặc định tắt, bỏ qua khóa tắt, bỏ `UseRateLimiter`).
**Đo (B6):** bộ test đầy đủ 1009 test, 43–48 s, ba lần chạy liên tiếp đều xanh (trước 6A-2a khoảng 20 s ⇒ thêm khoảng 25 s cho dựng database riêng, khởi động host, WireMock và các ca chờ timeout/cửa sổ rate limit).
**Giới hạn đã biết (E1):** `UseForwardedHeaders` tin `X-Forwarded-For` từ mọi nguồn nên khóa `ip:` lách được bằng IP giả khi truy cập thẳng cổng BE (không sửa ở 6A-2).
