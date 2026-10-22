# Gói 6A-2c — Luồng đặt sân online hai transaction

**Mục tiêu:** nối 6A-2b vào `CreateOnlineAsync`, đổi DTO và sự kiện, rồi chứng minh 5 nhánh bằng BE-IT HTTP trên test host 6A-2a. Tổng quan: [6A-2](PHASE6_PLAN_6A-2_ONLINE_BOOKING.md).

**Spec:** 7.6, 7.12, 7.13; DEPOSIT_APPROVAL_API 2.2, 3.1, 6; P6-01, P6-06, P6-11. **Test:** BE-UT-07; BE-IT-02, 03, 04, 16, 17, 19, 24 (phần của 6A-2).

---

### Task 1 — `CreateOnlineAsync` hai transaction và P6-01

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

### Task 2 — DTO (BE-UT-07)

**File:** `DTOs/Booking/OnlineBookingResponse.cs`, `DTOs/Booking/BookingDto.cs`, phần map trong `BookingService` (`MapToDto`, `MapToDetailDto`) và `PaymentService.RetryPaymentAsync` (chỉ chỗ dựng response); test `SmashCourt-BE.Tests/DTOs/` hoặc `Services/` theo hàng xóm.

**Giải pháp:**
- `OnlineBookingResponse`: thêm `bookingCode`, `status`, 4 trường cọc, `approvalExpiresAt`; `paymentUrl`, `expiresAt` nullable. `retry-payment` dùng cùng DTO (luôn `PENDING`; số tiền của nó đổi ở 6A-4).
- `BookingDto`: 4 trường cọc, `approvalExpiresAt`, `approvalReasonCode`, `approvalReasons`, `cancelReason`. Lý do cần duyệt **chỉ** điền ở endpoint của nhân viên; endpoint của khách luôn `null` (Q6): một tham số của hàm map, không nhân đôi hàm.
- `remainingAmount = finalTotal hiện tại − deposit_amount` (không âm); `PENDING_APPROVAL` ⇒ 4 trường cọc `null`; đơn cũ / `WALK_IN` ⇒ `null`. Khóa `null` vẫn serialize (như envelope hiện có).
- Không DTO nào có `decisionId`, `strategyName`, `executionType`, `fallbackUsed`.

**Test:** đúng bảng BE-UT-07 (từng trạng thái, khách và nhân viên, đơn cũ, a′); JSON không có khóa cấm.

### Task 3 — Sự kiện SignalR sau transaction 2 (R-7)

**File:** `Common/Constants/SignalREvents.cs` (3 hằng của DEPOSIT_APPROVAL_API 6), DTO payload mới ở thư mục DTO thông báo hiện có, `Services/BroadcastService.cs` + interface (hàm phát cho payload mới, theo mẫu hàm hiện có); phần phát trong `CreateOnlineAsync`.

**Giải pháp:** `BookingCreated` phát **sau** transaction 2 với trạng thái cuối và câu `message` theo trạng thái; nhánh b thêm `BookingPendingApproval` (nhóm chi nhánh + `role_OWNER`) và `PendingApprovalQueueChanged` (`ADDED`/`NEW`); nhánh c phát `BookingCancelled` kèm time grid **cho nhân viên/chi nhánh, không vào nhóm `user_{id}` của chính khách** (C2: khách đã nhận kết quả ngay ở response 400); payload không có số tiền, lý do duyệt hay trường TADSS. Phát lỗi chỉ log (best-effort như hiện tại).

**Test:** unit với `IBroadcastService` giả: đúng sự kiện, đúng nhóm, đúng thứ tự cho từng nhánh; không phát gì trước khi có quyết định; nhánh c không gửi vào nhóm của khách.

### Task 4 — BE-IT HTTP (trên host 6A-2a)

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
