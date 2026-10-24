# Gói 6A-2c — Luồng đặt sân online hai transaction

**Mục tiêu:** nối 6A-2b vào `CreateOnlineAsync`, đổi DTO và sự kiện, rồi chứng minh 5 nhánh bằng BE-IT HTTP trên test host 6A-2a. Tổng quan: [6A-2](PHASE6_PLAN_6A-2_ONLINE_BOOKING.md).

**Spec:** 7.6, 7.12, 7.13; DEPOSIT_APPROVAL_API 2.2, 3.1, 6; P6-01, P6-06, P6-11. **Test:** BE-UT-07; BE-IT-02, 03, 04, 16, 17, 19, 24 (phần của 6A-2).

---

### Task 1 — `CreateOnlineAsync` hai transaction và P6-01 ✅ (23/10)

**File:** `Services/BookingService.cs`, `Controllers/BookingController.cs`, `Common/ErrorCodes.cs` (`BOOKING_REJECTED`); test `SmashCourt-BE.Tests/Services/BookingServiceTests.cs` (cập nhật ca hiện có).

**Giải pháp:**
- **A9 (chốt 22/10):** bước 1, trước mọi truy vấn: `ToUtcFromVietnam(BookingDate, StartTime) ≤ now` ⇒ 400 `BAD_REQUEST` "Khung giờ đã bắt đầu hoặc đã qua, vui lòng chọn khung giờ khác". "Bây giờ" lấy từ `TimeProvider` tiêm vào `BookingService` (đăng ký `TimeProvider.System` ở `Program.cs` nếu chưa có); các phần cũ của `BookingService` giữ `DateTime.UtcNow`. Test `CreateOnlineAsync` hiện dùng ngày cố định 2026-06-15 chuyển sang `FakeTimeProvider`.
- Endpoint thêm policy `CustomerOnly`; service coi `customerId` luôn có, bỏ qua `guest*` (giữ trường trong DTO). Bỏ nhánh validate khách vãng lai.
- **Transaction 1** giữ nguyên các bước kiểm tra, tính giá, khuyến mãi, bắt 23P01 như hiện tại; ghi booking `PENDING`, chi tiết, hóa đơn với `expires_at`/`hold_deadline` từ `PaymentHoldHelper` (thay hằng 10 phút), một slot lock mỗi sân. **Không** tạo Payment/URL.
- Sau commit: `DecideAsync(bookingId, finalTotal, CancellationToken.None)` (ngoài transaction; D6): transaction 1 đã commit thì quyết định và transaction 2 **luôn chạy tới cùng** dù khách ngắt kết nối, comment lý do tại chỗ gọi. Controller không nhận token (như hiện tại); thời gian chờ do `TimeoutMs` của client giới hạn.
- **Transaction 2** theo nhánh, dùng hàm của 6A-2b Task 3; nhánh a, d tạo Payment `PENDING` = `deposit_amount` + URL VNPay (cùng cách gọi `_vnPayService` hiện tại); nhánh a′ tăng lượt dùng khuyến mãi (D3) và ghi token hủy (D8: service gọi `CancelTokenHelper.Generate` **chỉ khi** nhánh là a′, truyền hash và hạn vào hàm repository của 2b Task 4). Thua câu cổng (D7) ⇒ log Error (`bookingId`, nhánh định áp, trạng thái đọc lại), không Payment (link VNPay đã sinh trước cổng bị bỏ), không tăng lượt, không phát sự kiện, ném 409 `CONFLICT` "Đơn đặt sân vừa được nhân viên xử lý, vui lòng xem lịch sử đặt sân" cho mọi nhánh kể cả c.
- Nhánh a′, sau commit (D8, E2): gửi email xác nhận kèm link hủy bằng mẫu có sẵn (`BookingEmailFactory.Build` + `EmailService.SendBookingConfirmationAsync`, như đơn tại quầy; BE không có `IEmailService`), trong try/catch như các email hiện có; lỗi hoặc thiếu địa chỉ email không làm hỏng response 201 (log). Các nhánh a, b, c, d không gửi email này.
- Nhánh c: sau transaction 2 ném 400 `BOOKING_REJECTED` (message của DEPOSIT_APPROVAL_API 3.1, `data = null`); **không** ghi `slot_interest`.
- Bỏ hằng `AddMinutes(10)` ở `CreateBookingDetailsAsync` chỉ cho đường online (đơn tại quầy giữ nguyên).
- **Phụ thuộc mới của `BookingService` (bổ sung 23/10):** `TimeProvider` (A9 và "bây giờ" của luồng mới), `IBookingDepositService`, `IOptionsMonitor<BookingPaymentSettings>` (`PaymentHoldHelper.ForNewBooking(now, settings)`); cập nhật `BookingServiceTestBuilder` (một chỗ dựng service cho mọi unit test). Cùng một `now` cho `created_at` của booking, hạn giữ chỗ và token a′.
- **Đã có sẵn từ 2b, không viết lại:** `DecideAsync` không bao giờ ném vì lỗi dựng request hay TADSS (F2) nên caller không cần try/catch quanh nó; quyết định `APPROVED` có cọc vượt tổng đơn hoặc sai tiền tệ đã bị đổi thành dự phòng (F1). Token a′: `CancelTokenHelper.Generate(ToUtcFromVietnam(ngày, giờ bắt đầu sớm nhất), now)`; tra cứu link hủy đã dùng chung `CancelTokenHelper.Hash`.

**Test A9:** giờ bắt đầu = now − 1 s, = now, = now + 1 s (chặn, chặn, qua); hôm nay giờ đã qua; hôm nay giờ sắp tới (qua); ngày mai giờ sớm (qua); 23:59 và 00:00 giờ VN; lúc UTC còn là hôm qua nhưng VN đã sang ngày mới; nhiều sân cùng giờ (chặn như nhau); bị chặn thì không tạo bản ghi nào và không gọi TADSS.

**Test (unit, Moq):** nhánh d khi cờ tắt giữ đúng số tiền VNPay = `finalTotal`; không tạo Payment trước khi có quyết định; nhánh c không gọi `CreateSlotUnavailableExceptionAsync`; **K-01**: `DecideAsync` nhận token có `CanBeCanceled == false` (khóa D6); **D8**: a′ gọi `CancelTokenHelper` đúng một lần và gửi đúng một email chứa raw token, các nhánh khác không sinh token, không gửi email; lỗi gửi email vẫn 201; thua cổng (G-05) ⇒ không gửi email; **G-05**: câu cổng trả 0 ở từng nhánh ⇒ không gọi tạo Payment, không tăng lượt, không phát sự kiện, log Error, ném 409.

**Kết quả thực hiện (23/10):**
- Đúng giải pháp trên; thêm `ErrorCodes.BookingRejected`. Chữ ký đổi thành `CreateOnlineAsync(dto, Guid customerId)` (không còn nullable); controller thêm `CustomerOnly`, đọc claim như các action khác.
- **Lệch nhỏ so với plan, đã chốt theo hệ quả của P6-01:**
  - Khi slot bận, `slot_interest` lấy email **tài khoản** (trước ưu tiên `guestEmail` của request). Tài khoản không có email thì trả `SLOT_UNAVAILABLE` thường, không đăng ký.
  - Đơn **online** không lưu `guest_name`/`guest_phone`/`guest_email`. Đơn tại quầy (`CreateWalkInAsync`) giữ nguyên: vẫn nhận khách vãng lai và lưu thông tin liên hệ (test `CreateWalkInAsync_StillStoresGuestContact`).
  - `OnlineBookingResponse.paymentUrl` và `expiresAt` đổi nullable ngay ở Task 1, vì nhánh a′/b không có hai giá trị này. Các trường mới và câu `message` theo trạng thái ("Đơn đặt sân đang chờ quản lý chi nhánh duyệt mức cọc" của nhánh b) để Task 2.
- **Link VNPay sinh sau câu cổng** (plan ghi "trước cổng, thua thì bỏ"): dựng URL không gọi mạng nên thứ tự không đổi kết quả; sinh sau thì thua cổng không phải bỏ gì.
- **Thời gian:** một `now` từ `TimeProvider` cho A9, `created_at`, hạn giữ chỗ, slot lock và token a′. `updated_at` của transaction 2 lấy đồng hồ **sau** quyết định (có thể trễ tới hai lần `TimeoutMs`).
- **Change tracker:** xóa sau commit transaction 1. Transaction 2 ghi bằng `ExecuteUpdate`, nên nếu không xóa, lần đọc lại cho email và sự kiện sẽ thấy `PENDING`/`PREPAID` cũ.
- **Phần còn ở Task 3:** `BookingCreated` vẫn phát (sau transaction 2) với câu cũ "(Chờ thanh toán)"; nhánh c chưa phát `BookingCancelled`.
- **Test:**
  - File mới `BookingServiceOnlineBookingTests.cs` có 49 ca (A9 đủ bảng, thứ tự bước, K-01, các nhánh a/a′/b/c/d, D8, G-05 cho cả 5 nhánh, khuyến mãi theo nhánh). `BookingServiceTestBuilder` thêm `FakeTimeProvider`, `IBookingDepositService` giả (mặc định như cờ tắt), `BookingPaymentSettings`, `RecordingEmailService`.
  - 4 ca cũ trong `BookingServiceTests` được cập nhật; ca "khách vãng lai thiếu thông tin" được thay bằng ca "guest* bị bỏ qua".
  - Mutation 14/14 bị bắt.
  - Toàn bộ suite: 1059 xanh (43 s), build 0 cảnh báo.

### Task 2 — DTO (BE-UT-07) ✅ (23/10)

**File:** `DTOs/Booking/OnlineBookingResponse.cs`, `DTOs/Booking/BookingDto.cs`, phần map trong `BookingService` (`MapToDto`, `MapToDetailDto`) và `PaymentService.RetryPaymentAsync` (chỉ chỗ dựng response); test `SmashCourt-BE.Tests/DTOs/` hoặc `Services/` theo hàng xóm.

**Giải pháp:**
- `OnlineBookingResponse`: thêm `bookingCode`, `status`, 4 trường cọc, `approvalExpiresAt`; `paymentUrl`, `expiresAt` nullable. `retry-payment` dùng cùng DTO (luôn `PENDING`; số tiền của nó đổi ở 6A-4).
- `BookingDto`: 4 trường cọc, `approvalExpiresAt`, `approvalReasonCode`, `approvalReasons`, `cancelReason`. Lý do cần duyệt **chỉ** điền ở endpoint của nhân viên; endpoint của khách luôn `null` (Q6): một tham số của hàm map, không nhân đôi hàm.
- `remainingAmount = finalTotal hiện tại − deposit_amount` (không âm); `PENDING_APPROVAL` ⇒ 4 trường cọc `null`; đơn cũ / `WALK_IN` ⇒ `null`. Khóa `null` vẫn serialize (như envelope hiện có).
- Không DTO nào có `decisionId`, `strategyName`, `executionType`, `fallbackUsed`.

**Test:** đúng bảng BE-UT-07 (từng trạng thái, khách và nhân viên, đơn cũ, a′); JSON không có khóa cấm.

**Kết quả thực hiện (23/10):**
- **Một nơi tính trường cọc:** `Helpers/DepositFieldsHelper` + `Models/ViewModels/DepositFields`, dùng chung cho `BookingDto`, `OnlineBookingResponse` và response `retry-payment`. Hàm đọc nguyên số đã lưu, không tự nhân `rate × total`. Trả null khi đơn đang `PENDING_APPROVAL`, khi không có hóa đơn, hoặc khi hóa đơn thiếu một trong ba cột cọc (đơn tại quầy, đơn trước Phase 6).
- **Hàm map:** `MapToDto`/`MapToDetailDto` nhận tham số `includeApprovalReasons`.
  - Endpoint của khách (`GET /api/me/bookings`, chi tiết với role `CUSTOMER`) truyền `false`.
  - Endpoint của nhân viên truyền `true`. `GetByIdAsync`/`GetAllAsync` tính theo role, nên role khách vẫn ra `null`.
- **Response đặt online:** `status` theo nhánh (a/d `PENDING`, a′ `CONFIRMED`, b `PENDING_APPROVAL`); số cọc lấy nguyên từ quyết định; `approvalExpiresAt` chỉ có ở nhánh b.
- **Controller:** câu `message` theo `status`. Nhánh b dùng câu của API doc; nhánh a′ "Đặt sân thành công, vui lòng thanh toán tại quầy" (đã thêm vào API doc 3.1); còn lại "Đặt sân online thành công". Hiện chưa có test ở mức controller, sẽ kiểm qua HTTP ở Task 4.
- **`retry-payment`:** dùng cùng DTO; trường cọc đọc từ hóa đơn. Số tiền VNPay vẫn là `finalTotal`, đổi ở 6A-4 theo plan.
- **Test:**
  - Mới: `DepositFieldsHelperTests` (hàm thuần, mọi trạng thái), `BookingServiceDepositDtoTests` (bảng BE-UT-07 qua đúng hàm service của từng endpoint, khách/nhân viên, 7 lý do hủy), `DepositDtoContractTests` (không DTO nào trong `SmashCourt_BE.DTOs` có trường nội bộ TADSS; các trường mới nullable), `HttpDepositDtoJsonTests` (serialize bằng `JsonOptions` thật của host: khóa null vẫn có, tên khóa đúng, giờ VN).
  - Thêm 6 ca response theo nhánh vào `BookingServiceOnlineBookingTests` và 1 ca retry có cọc vào `PaymentServiceTests`.
  - Mutation 14/14 bị bắt.
  - Toàn bộ suite: 1121 xanh (46 s), build 0 cảnh báo.

### Task 3 — Sự kiện SignalR sau transaction 2 (R-7) ✅ (23/10)

**File:** `Common/Constants/SignalREvents.cs` (3 hằng của DEPOSIT_APPROVAL_API 6), DTO payload mới ở thư mục DTO thông báo hiện có, `Services/BroadcastService.cs` + interface (hàm phát cho payload mới, theo mẫu hàm hiện có); phần phát trong `CreateOnlineAsync`.

**Giải pháp:** `BookingCreated` phát **sau** transaction 2 với trạng thái cuối và câu `message` theo trạng thái; nhánh b thêm `BookingPendingApproval` (nhóm chi nhánh + `role_OWNER`) và `PendingApprovalQueueChanged` (`ADDED`/`NEW`); nhánh c phát `BookingCancelled` kèm time grid **cho nhân viên/chi nhánh, không vào nhóm `user_{id}` của chính khách** (C2: khách đã nhận kết quả ngay ở response 400); payload không có số tiền, lý do duyệt hay trường TADSS. Phát lỗi chỉ log (best-effort như hiện tại).

**Test:** unit với `IBroadcastService` giả: đúng sự kiện, đúng nhóm, đúng thứ tự cho từng nhánh; không phát gì trước khi có quyết định; nhánh c không gửi vào nhóm của khách.

**Kết quả thực hiện (23/10):**
- **`SignalREvents`:**
  - Thêm 3 hằng của API doc mục 6. `BookingApprovalResolved` để sẵn cho 6A-3, chưa phát.
  - Thêm hằng `ADDED` / `REMOVED` / `NEW` cho `change` / `reason` của `PendingApprovalQueueChanged`.
- **Payload mới:** `BookingPendingApprovalNotificationDto` và `PendingApprovalQueueChangedNotificationDto` trong `DTOs/SignalR`. `BookingNotificationDto` không đổi.
- **`IBroadcastService` thêm 3 hàm:**
  - `BroadcastBookingEventToStaffAsync`: như hàm cũ nhưng **bỏ** nhóm `user_{id}`, dùng cho nhánh c (C2).
  - `BroadcastBookingPendingApprovalAsync` và `BroadcastPendingApprovalQueueChangedAsync`: gửi tới `branch_{id}` + `role_OWNER`. Lỗi chỉ log.
  - Lý do không thêm tham số tùy chọn vào hàm cũ: Moq không cho biểu thức có tham số tùy chọn bị bỏ trống (CS0854), mọi chỗ mock hàm cũ sẽ hỏng.
- **`CreateOnlineAsync`:**
  - Đọc lại đơn một lần sau transaction 2, rồi gửi email (a′) và phát sự kiện (`PublishOnlineBookingEventsAsync`).
  - Nhánh c phát sự kiện **trước** khi ném 400.
  - `status` của payload lấy từ DB đọc lại; câu `message` theo trạng thái: "Chờ thanh toán", "Chờ duyệt cọc", "Đã xác nhận, thanh toán tại quầy", "không được nhận theo chính sách, slot đã được giải phóng".
  - `timestamp` lấy từ `TimeProvider`.
- **Test:**
  - `BroadcastServiceTests` có hub giả ghi nhóm và payload: kiểm nhóm của từng hàm (hàm "chỉ nhân viên" không có `user_`), lỗi SignalR được nuốt và log, giá trị tên sự kiện, payload không có số tiền / lý do duyệt / trường TADSS.
  - `BookingServiceOnlineBookingTests` kiểm: chuỗi sự kiện của 5 nhánh và tất cả đều sau transaction 2; câu và trạng thái của `BookingCreated`; payload của nhánh b; nhánh c không vào nhóm khách nhưng có lưới giờ; SignalR lỗi không đổi kết quả đặt sân.
  - Mutation 14/14 bị bắt.
  - Toàn bộ suite: 1149 xanh (44 s), build 0 cảnh báo.
- **FE:** ghi ở spec 8.2 (hằng mới trong `signalr-events.ts`, đăng ký `NotificationDispatcher`); BE không sửa FE.

### Task 4 — BE-IT HTTP (trên host 6A-2a) ✅ (23/10)

**File:** `SmashCourt-BE.Tests/Integration/Http/OnlineBookingFlowTests.cs` (có thể tách theo nhánh nếu > ~300 dòng).

**Hạ tầng còn thiếu, làm đầu Task 4 (bổ sung 23/10 sau 6A-2a):**
- **Seed sân đặt được thật:** đặt sân qua HTTP chạy bộ tính giá thật (`BranchPriceService.CalculateBaseAsync`) nên cần chi nhánh có giờ mở cửa, loại sân, sân `AVAILABLE`, `time_slots` phủ khung giờ đặt, `system_prices` (hoặc giá chi nhánh) đúng loại ngày của ngày đặt; khuyến mãi theo từng ca (`PERCENTAGE` 100 % cho đơn 0 đ, `FIXED` cho đơn 3.000 đ, `UsageLimit`, `UsagePerUserLimit`). Một helper trong `HttpTestBase` hoặc file seed riêng; giá chọn sẵn để biết trước `finalTotal`.
- **Ngày đặt luôn ở tương lai** (A9 chặn giờ đã qua theo đồng hồ thật của host): lấy ngày VN hiện tại + N ngày, mỗi test một sân/khung giờ riêng (dữ liệu commit thật, không rollback).
- **Nhân viên có quyền chi nhánh** cho nhóm G và P-20 (hủy qua API kiểm `ValidateBranchAccessAsync`): seed `user_branches`.
- **TADSS giả theo chuỗi** cho Z-13 (lần 1 chậm, lần 2 trả quyết định): kịch bản trạng thái của WireMock (`InScenario`/`WillSetStateTo`) trong `TadssStub`. Đếm request bằng `WaitForEvaluationsAsync(bookingId, n)`; độ trễ "quá timeout" chỉ nhỉnh hơn `TimeoutMs` để WireMock kịp ghi log trong thời gian chờ.
- **`TimeoutMs` của host test là 400 ms** (không phải 100 ms). Nhóm K và G cần WireMock trễ ~1 s mà vẫn áp quyết định ⇒ test tạm nâng `TadssConfig:TimeoutMs` (VD 3000) bằng `SetSetting` rồi trả lại.
- **IPN giả cho P-14:** dựng query IPN có chữ ký bằng `HashSecret` giả của host (cùng thư viện VNPAY.NET mà BE dùng để kiểm); số tiền trong URL VNPay đọc từ `vnp_Amount` (= số tiền × 100).

**Ca bổ sung (23/10):**

| # | Ca | Kỳ vọng |
| :---: | :--- | :--- |
| A9-H | Đặt khung giờ đã bắt đầu trong hôm nay (qua HTTP) | 400 `BAD_REQUEST` "Khung giờ đã bắt đầu hoặc đã qua…", không booking, không slot lock, WireMock 0 request |
| E-01 | Đơn a′ (D8) | Đúng một thư tới email khách (`RecordingEmailService`), thân có link `/booking/cancel?token=…`; token trong link dùng được với `GET /api/bookings/cancel/{token}` (khứ hồi) và hết hạn đúng giờ bắt đầu |
| E-02 | Đơn a′ khi SMTP hỏng (`FailOnSend = true`) | Vẫn 201 `CONFIRMED`, token vẫn ghi trong DB |
| E-03 | Nhánh a, b, c, d | Không gửi thư xác nhận lúc đặt (a/d gửi khi IPN thành công như cũ) |


**Giải pháp và ca:**
- **BE-IT-02** (a): 201 `PENDING`, `depositSource = TADSS`, URL VNPay mang `deposit.amount`, hóa đơn và booking lưu đủ; mẫu fallback runtime và `APPROVAL_WINDOW_UNAVAILABLE` cũng đi nhánh a.
- **BE-IT-03** (b): 201 `PENDING_APPROVAL`, `paymentUrl`/`expiresAt`/cọc `null`, không Payment, slot lock tới `approvalExpiresAt`, time grid ô `LOCKED`; `retry-payment` ⇒ 400.
- **BE-IT-04** (d): cờ tắt ⇒ WireMock 0 request; 5xx và lỗi mạng (WireMock `WithFault`) ⇒ đúng 2 request cùng key; timeout ⇒ kiểm bằng thời gian (hai lần chờ `TimeoutMs`) vì WireMock không ghi log request bị cắt giữa chừng; 401, 413, 429 ⇒ 1 request; mọi ca 201 cọc 100 %, không lộ mã lỗi TADSS.
- **BE-IT-16** (phần 6A-2): đặt tại quầy khi cờ bật ⇒ WireMock 0 request, trường cọc `null`.
- **BE-IT-17** (c): 400 `BOOKING_REJECTED`, `data = null`, không lộ `reasons`; booking `POLICY_REJECTED`, slot trống lại (đặt lại cùng giờ được), không `slot_interest`, không Payment, `decisionId` được lưu.
- **BE-IT-19** (phần 6A-2): đổi cờ lúc chạy ⇒ đơn tạo sau đó < 2 s theo cờ mới, không khởi động lại.
- **BE-IT-24**: không đăng nhập ⇒ 401, `STAFF` ⇒ 403, WireMock 0 request; `APPROVED` với số tiền < mức tối thiểu, = 0, và đơn 0 đ được `APPROVED` ⇒ 201 `CONFIRMED`, không Payment, cọc 0 (đơn 0 đ mà TADSS trả `NEED_APPROVAL`/`REJECTED` đi b/c: Z-11, Z-12).
- Mọi request đã gửi WireMock hợp lệ `decision-request.schema.json` (ca 9.4 "Request TADSS").
- **Lượt khuyến mãi theo từng nhánh (D3), qua HTTP:**

| # | Ca | Kỳ vọng `UsedCount` và dữ liệu |
| :---: | :--- | :--- |
| P-10 | Cờ tắt, mã giảm 100 % ⇒ `finalTotal = 0` ⇒ a′ | +1; 201 `CONFIRMED`, `depositSource = FULL_PAYMENT_FALLBACK`, hóa đơn `POSTPAID`, không Payment |
| P-11 | Cờ bật, `APPROVED` cọc < mức tối thiểu, có khuyến mãi | +1; `depositSource = TADSS` |
| P-12 | Cờ bật, `APPROVED` cọc = 0, có khuyến mãi | +1 |
| P-13 | Nhánh a (cọc ≥ mức tối thiểu), có khuyến mãi | Không đổi lúc đặt; hóa đơn vẫn `PREPAID` |
| P-14 | Nhánh d (cờ tắt, `finalTotal > 0`), có khuyến mãi | Không đổi lúc đặt; giả IPN thành công cho Payment vừa tạo ⇒ +1 (hồi quy đường IPN) |
| P-15 | Nhánh b `NEED_APPROVAL`, có khuyến mãi | Không đổi; hóa đơn `PREPAID` |
| P-16 | Nhánh c `REJECTED`, có khuyến mãi | Không đổi (không tăng, không giảm) |
| P-17 | `UsageLimit = 1`: đơn a′ thứ nhất rồi đơn thứ hai cùng mã (khách khác) | Đơn 1: 201, `UsedCount = 1`; đơn 2: 400 "Mã khuyến mãi đã hết lượt sử dụng", không booking mới |
| P-18 | `UsageLimit = 2`, `UsedCount = 1` sẵn: một đơn a′ | 201, `UsedCount = 2`; đơn kế tiếp bị chặn |
| P-19 | `UsagePerUserLimit = 1`: cùng khách đặt a′ hai lần | Lần 2 bị chặn (hồi quy kiểm theo khách) |
| P-20 | Đơn a′ có khuyến mãi rồi nhân viên hủy (đường hủy hiện có, trước 6A-4) | `UsedCount` về giá trị ban đầu; không Refund; hóa đơn vẫn `UNPAID` |
| P-21 | Đơn a/d có khuyến mãi bỏ ngang, Job-01 hủy | `UsedCount` không đổi (Job-01 không giảm) |
| P-22 | Khuyến mãi chạm `UsageLimit` giữa transaction 1 và 2 (một đơn khác dùng lượt cuối) | Đơn a′ vẫn `CONFIRMED`, `UsedCount` vượt giới hạn 1: ghi nhận hạn chế có sẵn (kiểm lúc tạo, tăng sau), không đổi hành vi |

- **Thứ tự chọn nhánh và ngưỡng tiền (D5), qua HTTP:**

| # | Ca | Kỳ vọng |
| :---: | :--- | :--- |
| Z-10 | Cờ tắt, mã `FIXED` làm `finalTotal = 3.000` | 201 `CONFIRMED`, không Payment, `POSTPAID`, khuyến mãi +1, `remainingAmount = 3.000`, `depositSource = FULL_PAYMENT_FALLBACK` |
| Z-11 | Cờ bật, đơn 0 đ, WireMock `NEED_APPROVAL` | 201 `PENDING_APPROVAL`, không Payment, khuyến mãi không đổi |
| Z-12 | Cờ bật, đơn 0 đ, WireMock `REJECTED` | 400 `BOOKING_REJECTED`, slot được nhả, `decisionId` được lưu |
| Z-13 | Lần 1 WireMock trễ quá `TimeoutMs`, lần 2 trả quyết định | Đúng 2 request **cùng key, cùng body**; BE áp quyết định lần 2 (không rơi về d) |
| Z-14 | Cả hai lần timeout, `finalTotal ≥ 5.000` | d, không `decisionId`, khách không thấy lỗi |
| Z-15 | Cả hai lần timeout, `finalTotal < 5.000` (ngoại lệ có chủ đích) | a′, `FULL_PAYMENT_FALLBACK`, log Warning, 201 |
| Z-16 | Request đơn 0 đ gửi TADSS | Hợp lệ `decision-request.schema.json` (`totalPrice = 0`, `price` của item có thể 0) |
| Z-17 | `APPROVED` cọc 4.999 trên đơn 200.000 | 201 `CONFIRMED`, `depositAmount = 0`, `remainingAmount = 200.000`, không Payment |
| Z-18 | `APPROVED` cọc 5.000 | 201 `PENDING`, Payment và `vnp_Amount` = 5.000 |

Ca rollback khi lỗi giữa transaction 2 của a′ đã nằm ở P-04 (6A-2b).

- **Khách ngắt kết nối giữa chừng (D6), qua HTTP:** client HTTP hủy request sau ~200 ms trong khi WireMock trễ ~1 s; test chờ theo điều kiện (đọc lại DB tới trạng thái cuối, có giới hạn thời gian), không `sleep` cố định.

| # | WireMock trả | Kỳ vọng sau khi luồng chạy xong |
| :---: | :--- | :--- |
| K-02 | `APPROVED` (cọc ≥ mức tối thiểu) | Booking `PENDING`, hóa đơn có `deposit_*`, Payment `PENDING` = cọc; `retry-payment` dùng được |
| K-03 | `REJECTED` | `CANCELLED` / `POLICY_REJECTED`, slot được nhả, `decisionId` lưu; `retry-payment` ⇒ 400 |
| K-04 | `NEED_APPROVAL` | `PENDING_APPROVAL`; `retry-payment` ⇒ 400 (không lách duyệt) |
| K-05 | Timeout cả hai lần | Nhánh d hoàn chỉnh (hoặc a′ nếu `< 5.000 đ`); không còn booking `PENDING` thiếu Payment/`deposit_*` |

- **Thua câu cổng (D7), qua HTTP:** WireMock trễ ~1 s; trong lúc chờ, nhân viên gọi API hủy đơn bằng token `STAFF` (đọc `bookingId` từ DB theo khách và giờ đặt); chờ theo điều kiện như nhóm K.

| # | WireMock trả | Kỳ vọng |
| :---: | :--- | :--- |
| G-01 | `APPROVED` | Khách nhận 409 `CONFLICT`, `data = null`; không Payment; booking giữ `CANCELLED` / `STAFF` |
| G-02 | `APPROVED` cọc < mức tối thiểu (a′), có khuyến mãi | 409; không có lần tăng lượt nào (`UsedCount` chỉ phản ánh đường hủy cũ của nhân viên) |
| G-03 | `NEED_APPROVAL` | 409; booking không thành `PENDING_APPROVAL`; không phát `BookingPendingApproval`/`PendingApprovalQueueChanged` |
| G-04 | `REJECTED` | 409 (không phải `BOOKING_REJECTED`); `cancel_source` vẫn `STAFF`, `cancel_reason` không bị ghi đè |

Các ca của duyệt 0 % (6A-3) và giảm lượt khi hủy theo 7.9 bước 5 (6A-4) nằm ở plan của gói đó; spec 7.7, 7.9 đã ghi điểm cần kiểm.

**Xong khi:** đủ ca trên xanh; BE chạy thật cờ tắt như mục "Kiểm tra cuối gói" của tổng quan.

**Kết quả thực hiện (23/10):**
- **Hạ tầng** (`Integration/Http/`):
  - `OnlineBookingTestBase` seed sân đặt được thật. Khung giờ 1 tiếng phủ cả ngày cho cả hai loại ngày, dựng một lần. Mỗi test có chi nhánh, loại sân với giá hệ thống riêng và sân mới; ngày đặt ở tương lai.
  - Seed thêm: khuyến mãi, nhân viên kèm `user_branches`.
  - Đọc DB, chờ theo điều kiện, ký IPN thật bằng `HashSecret` của host.
  - `TadssStub` dùng templating của WireMock: `decisionId` sinh mới mỗi lần (cột unique), `reservationId` lấy lại từ request. Có kịch bản hai lượt cho Z-13.
  - `RecordingLoggerProvider` ghi log mức Warning trở lên của host: kiểm log, và lỗi 500 hiện stack trace trong thông báo của test.
  - Client gửi `X-Forwarded-For` vì thư viện VNPay lấy IP máy khách, mà `TestServer` không có IP.
- **Bốn file, 66 ca:**
  - `OnlineBookingBranchTests` (23): BE-IT-02/03/04 (cờ tắt)/16/17/19/24, Z-16, Z-17, A9-H, E-01…E-03, đơn nhiều sân.
  - `OnlineBookingTadssFailureTests` (16): 5xx, lỗi mạng, timeout, 401/413/429, 400/409, phản hồi hỏng, F1, Z-13…Z-15.
  - `OnlineBookingPromotionTests` (18): P-10…P-22, Z-10…Z-12, Z-18.
  - `OnlineBookingConcurrencyTests` (9): K-02…K-05, G-01…G-04.
  - Câu `message` theo trạng thái của Task 2 đã được kiểm qua HTTP.
- **Lệch so với plan:**
  - **Lỗi mạng:** fault `EMPTY_RESPONSE` của WireMock.Net trả 200 body rỗng (thành ca "phản hồi hỏng, không thử lại"). Cổng đóng thì Windows chờ ~2 s nên thành timeout. Ca lỗi mạng thật dùng một `TcpListener` nhận kết nối rồi cắt bằng RST, đếm đúng 2 kết nối.
  - **Đếm request:** WireMock **có** ghi log request quá timeout, nhưng ghi sau khi hết thời gian trễ, nên Z-13 đếm được 2 request. Ca "không gọi TADSS" chỉ đếm request mang id sân của chính test, vì request chậm của test trước có thể được ghi sau khi test này reset.
  - **G-04:** đường hủy hiện có của nhân viên chưa ghi `cancel_reason` (6A-4 đưa về lõi hủy chung, 7.9). Test kiểm transaction 2 không ghi đè thành `POLICY_REJECTED`.
  - **G-03:** việc không phát `BookingPendingApproval` khi thua cổng đã kiểm ở unit (G-05), không kiểm qua HTTP.
- **Lỗi có sẵn phát hiện nhờ BE-IT** (spec mục 3, N-26…N-28):
  - **N-26:** khuyến mãi không điều kiện bỏ qua giới hạn lượt. Đã sửa, có ca unit (đỏ trước khi sửa) và P-17…P-19.
  - **N-27:** IPN không tăng lượt khuyến mãi. Đã sửa, P-14 đỏ trước khi sửa.
  - **N-28:** cột snapshot `numeric(5,2)` làm hỏng mọi đơn dùng mã `FIXED` > 100 đ. **Chốt sửa 23/10:** script `phase6_fix_promotion_snapshot.sql` (đã thử trên PostgreSQL 17: chạy lại được, dữ liệu cũ hợp lệ, biên CHECK đúng), schema dump và `schema_snapshot.py` đã cập nhật, BE-IT dùng lại mã `FIXED` và thêm ca N-28 (online + tại quầy). Chờ người dùng áp lên `tadss-dev`.
- **Độ ổn định:** một lần đỏ của P-11/P-12 khi tiến trình còn nguội (lần gọi TADSS đầu bị cắt ở 400 ms ⇒ nhánh d). Sửa ở test: ca cần TADSS trả lời dùng `TimeoutMs` 2 s, ca timeout/lỗi gọi `UseTadssWithShortTimeoutAsync` (400 ms). Toàn bộ 1221 xanh 3 lần liên tiếp, mỗi lần một tiến trình mới.
- **Toàn bộ suite:** 1220 xanh, ổn định 3 lần liên tiếp (~1 phút mỗi lần); build 0 cảnh báo.

**Rà soát tình huống thực tế (23/10, lần 2):** danh mục của plan đã phủ đủ; rà thêm tình huống ngoài danh mục, kèm 4 mục người dùng đưa.
- **Thêm `OnlineBookingRealWorldTests` (16 ca):**
  - Hai khách / một khách bấm hai lần đặt cùng slot cùng lúc.
  - 8 vòng × 3 request đồng thời.
  - Thanh toán **cọc** qua IPN (nhánh a): `PAID_ONLINE`, `PARTIALLY_PAID`, email lúc tiền về, khách thấy `remainingAmount`. IPN bị sửa số tiền (cao hoặc thấp hơn) bị bỏ qua.
  - Đặt trúng slot đang giữ (a, b) ⇒ 400 và đăng ký báo slot trống bằng email tài khoản.
  - Đơn nhiều sân có một sân bị chiếm.
  - Trùng một phần thời gian (4 kiểu) và sát giờ (2 kiểu).
  - Tranh lượt khuyến mãi cuối (P-22 mở rộng: không mất lượt, sau đó mã bị chặn).
  - P6-18 qua HTTP.
- **Phát hiện và sửa N-29:** đặt đồng thời làm người thua nhận 500. Hai nguyên nhân: Serializable mặc định (`40001`), và deadlock của exclusion constraint (`40P01`) không được nhận diện. Bản sửa áp cho luồng đặt online; 12 transaction cũ chuyển sang 6A-4. Bỏ từng bản sửa ⇒ test đồng thời đỏ.
- **P6-18 làm sớm:** `GetUserUsageCountAsync` bỏ booking ở trạng thái hủy. `PromotionUsageRepositoryTests` chạy trên schema thật cho mọi trạng thái (đỏ trước khi sửa), kèm ca schema N-28.
- **Bốn mục người dùng đưa:**
  - **"DST":** VN không có giờ mùa hè từ 1975 và cả hai id múi giờ đều không có DST, nên không có ca nhảy giờ. Thay bằng `VietnamTimeZoneTests`: offset luôn +7, mọi ngày của 2026–2028 quy đổi đúng, không có giờ rơi vào khoảng trống hoặc lặp vào các ngày đổi giờ của châu Âu/Mỹ. Test này khóa trường hợp id múi giờ bị ánh xạ sai khi chạy Docker Linux.
  - **Deadlock:** có thật (N-29), đã tái hiện và sửa như trên.
  - **Trùng giờ nhiều sân:** đã thêm như trên.
  - **Tranh khuyến mãi:** đã mở rộng P-22 như trên.
- **Toàn bộ suite:** 1267 xanh, 3 lần liên tiếp (~1 phút 20 giây mỗi lần); build 0 cảnh báo.

**Bổ sung 24/10:**
- **N-28 đã áp lên `tadss-dev`:** trước khi áp, công cụ cho thấy `tadss-dev` chỉ lệch đúng bảng `booking_promotions`; áp xong báo "đã áp đủ", schema trùng bản trong repo, 29 dòng khuyến mãi cũ (phần trăm, ≤ 50) giữ nguyên.
- **Kiểm `reservationId` (chốt 24/10):** `TadssClient` coi quyết định có `reservationId` khác booking đã gửi là `InvalidResponse` (so GUID, không thử lại, log Error ghi cả hai id). Đơn đi dự phòng (d/a′), không lưu `decisionId`, không hủy oan.
  - Unit: 3 nhánh bị lệch, log đủ hai id, khớp nhưng viết hoa vẫn hợp lệ, so theo `request.ReservationId`. Đỏ trước khi sửa.
  - HTTP: quyết định duyệt, chờ duyệt, từ chối của booking khác ⇒ 201 nhánh d.
- **Timeout trong test:** chỉ 4 ca kiểm timeout (timeout hai lần, Z-13, Z-14, Z-15) và K-05 dùng 400 ms. Mọi ca khác (kể cả lỗi HTTP cần đếm đúng 1 request) dùng 2 s, để lần gọi đầu lúc tiến trình còn nguội không bị cắt oan thành thử lại.
- **Toàn bộ suite:** 1276 xanh, 3 lần liên tiếp (~2 phút 15 giây; ca HTTP đầu tiên gánh khởi động host, ca dồn tải đồng thời mất ~1 s mỗi lần PostgreSQL phát hiện deadlock).

**Rà soát toàn bộ 6A-2 (24/10, lần 3):**
- Đọc lại toàn bộ diff code từ `6590876` (trước 6A-2) và đối chiếu chéo spec, plan, API doc với code.
- **Không có lỗi mới trong code.** Thêm BE-IT khách tự hủy đơn a′ bằng link email: chạy đầu cuối, không hoàn tiền, bấm lại vẫn idempotent như thiết kế có sẵn, slot đặt lại được.
- **Tài liệu đã sửa:**
  - API doc 3.1 bổ sung 409, A9, `BOOKING_REJECTED`, đặt đồng thời, email đăng ký báo slot trống.
  - Spec 7.6 bước 2 ghi N-29.
  - Ghi chú link VNPay sinh sau câu cổng.
  - Spec mục 11 thêm 5 rủi ro liên gói: bật cờ trước 6A-4 (R-3, `retry-payment`), `approvalExpiresAt` lệch đồng hồ, lượt khuyến mãi của đơn a′ bị hủy trước 6A-4, lỗi sau câu cổng, 12 transaction còn Serializable.
- **Toàn bộ suite:** 1277 xanh.
