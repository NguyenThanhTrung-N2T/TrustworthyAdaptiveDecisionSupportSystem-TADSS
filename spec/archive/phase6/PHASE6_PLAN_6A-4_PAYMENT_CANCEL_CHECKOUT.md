# Gói 6A-4 — Thanh toán, hủy, check-out

**Mục tiêu:** đóng mọi đường mất tiền hoặc giữ sân sai đã biết, để sau 6A-5 bật được cờ `EnableTadssDecision`:
- **Thanh toán thất bại không hủy đơn:** khách có 5 phút để thanh toán lại hoặc tự hủy.
- **Tiền về sau khi đơn đã hủy:** có bản ghi và được hoàn 100 %.
- **Hủy:** một dịch vụ hủy dùng chung. Khách và nhân viên hủy được đơn chưa trả và đơn chờ duyệt. Tiền hoàn tính trên số đã trả, đúng múi giờ. Khách không tự hủy được sau giờ bắt đầu.
- **Check-out:** thu đúng phần còn lại (tiền sân chưa cọc + dịch vụ).
- **12 transaction cũ** chuyển sang Read Committed kèm câu cập nhật có điều kiện, để thao tác đồng thời trả 409/400 thay vì 500.
- **Nghiệp vụ thanh toán không gắn VNPay:** cổng thanh toán chỉ tạo URL và xác thực kết quả; mọi quy tắc (thất bại, thành công, tiền về sau khi hủy, trả hai lần) nằm ở một service dùng chung, để thêm cổng khác (MoMo đã có trong enum) không phải chép lại nghiệp vụ.

**Hoàn tiền luôn offline tại chi nhánh đặt sân** (người dùng chốt 24/10): khách nhận tiền mặt tại quầy, nhân viên chi nhánh của đơn bấm xác nhận hoàn. BE không bao giờ gọi API hoàn tiền của cổng thanh toán; Refund chỉ là khoản chi nhánh phải trả, không phụ thuộc khách đã trả qua cổng nào. Email hủy và email xác nhận hoàn hiện có đã ghi "nhận tiền mặt tại chi nhánh, không chuyển khoản".

**Cờ `EnableTadssDecision` vẫn tắt** sau gói này (chỉ bật sau 6A-5).

**Spec:** 5.1 (6A-4), 7.5 (dòng IPN thất bại), 7.8, 7.9, 7.10, 7.13, 7.14; P6-02, P6-03, P6-07, P6-08, P6-09, P6-12, P6-23; N-2, N-8, N-11, N-15, N-18, N-29; R-1, R-2, R-3; T-1, T-4. API: DEPOSIT_APPROVAL_API 3.4, 3.5, 3.6, Mục 5 dòng 21. Sơ đồ: tuần tự 5, 6. Use case: UC-DEPOSIT-02, 04, 08, 12.

**Test (danh mục):** BE-UT-08 (dòng IPN thất bại), BE-UT-10, BE-UT-12 (phần P6-23); BE-IT-10, 21, 22, 23; phần 6A-4 của BE-IT-25 (đơn tại quầy có email, mốc thời gian theo đồng hồ test).

**Nhánh:** `SmashCourt-BE` `feat/phase-6-deposit`. Nền: 6A-3 (chưa commit lúc lập plan).

**Trạng thái:** ✅ đã thực hiện (24/10) và commit (`f554fad`, 25/10, gồm bổ sung test và rà soát hồi quy); kết quả ở cuối file. Bản 2 (24/10). Người dùng đã chốt C1…C4 theo đề xuất, thêm hai yêu cầu: hoàn tiền chỉ offline tại chi nhánh; logic thanh toán dùng chung cho nhiều cổng (Task 8).

---

## 0. Kết quả rà soát spec và mã

### 0.1. Mã đã có, dùng lại được

| Thứ đã có | Dùng cho |
| :--- | :--- |
| Lõi hủy `CancelWithStatusCheckAsync` (một câu lệnh ghi đủ cột hủy, có điều kiện trạng thái) | Bước 1 của mọi đường hủy |
| `TryConsumeTokenAsync` (tiêu token có điều kiện) | Hủy bằng link |
| `PaymentHoldPolicy` (dòng đặt sân, duyệt, thanh toán lại) | Thêm dòng IPN thất bại |
| `TransactionScopeHelper.ReadCommitted()`, `IsOverlapConstraintViolation` (nhận 23P01 và 40P01) | Hạ mức cô lập 12 transaction cũ |
| `AtomicUpdatePaymentSuccessAsync` (`PENDING → PAID_ONLINE` có điều kiện) | IPN thành công |
| `GetCollectedAmountAsync` (Σ Payment `SUCCESS` trừ phần đã hoàn) | Check-out, tiền hoàn |
| `PromotionRepository.DecrementUsageCountAsync` (không xuống âm) | Trả lượt khuyến mãi |
| `BookingNotificationService` (SignalR + email sau commit, mỗi bước tự nuốt lỗi) | Thông báo hủy, tiền về sau khi hủy |
| `SlotInterestNotificationService` | Báo người chờ slot khi hủy |
| Bảng chuyển trạng thái đã có `CANCELLED → CANCELLED_PENDING_REFUND`, `PENDING_APPROVAL → CANCELLED` | P6-07, hủy đơn chờ duyệt |
| Hàng "Chờ hoàn tiền" và `ConfirmRefundAsync` | Hoàn tiền của P6-07 |

### 0.2. Mã hiện tại sai ở đâu (theo từng chức năng)

**Hủy: ba đường hủy là ba bản chép tay khác nhau.**

| | Nhân viên (`CancelByStaffAsync`) | Khách (`CancelByCustomerAsync`) | Link (`CancelByTokenAsync`) |
| :--- | :--- | :--- | :--- |
| Trạng thái nhận | `PENDING`, `CONFIRMED`, `PAID_ONLINE`; **thiếu `PENDING_APPROVAL`** (R-2) | `CONFIRMED`, `PAID_ONLINE`; **thiếu `PENDING`, `PENDING_APPROVAL`** (P6-09) | `CONFIRMED`, `PAID_ONLINE` (đúng) |
| Ghi có điều kiện | **Không**, cũng không có transaction: ghi đè được IPN vừa chuyển `PAID_ONLINE` ⇒ tiền vào đơn đã hủy | Có, nhưng sau đó còn ghi đè cả entity một lần nữa | **Không** |
| `cancel_reason` | Không ghi | Không ghi | Không ghi |
| Tiền hoàn | `FinalTotal × %` ⇒ **hoàn vượt số đã trả** khi chỉ cọc (R-1); % lệch 7 giờ (T-1) | Như trên | Như trên |
| Payment `PENDING` khi hủy | Giữ nguyên (N-11) | Giữ nguyên | Giữ nguyên |
| Lượt khuyến mãi | **Luôn trả**, kể cả đơn chưa từng được tính lượt (N-8) | Luôn trả | **Không bao giờ trả** |
| Sau giờ bắt đầu | Được hủy (đúng) | **Được hủy** ⇒ né `NO_SHOW` (T-4) | Hết hạn link (đúng) |
| Sân về `AVAILABLE` | Không kiểm sân còn đơn khác | Có kiểm | Có kiểm |

**Hủy bằng link: token bị tiêu trước khi kiểm.** Token được đánh "đã dùng" **trước** khi kiểm hạn và trạng thái, ngoài transaction hủy. Lỗi nào xảy ra sau bước đó (hết hạn, trạng thái không hủy được, ghi DB lỗi) thì đơn không bị hủy nhưng link đã chết; khách bấm lại chỉ thấy "Link hủy đã được sử dụng". Đây là **lỗi thật có sẵn**, sửa trong Task 7 kèm test.

**Thanh toán (IPN):**
- **IPN thất bại hủy đơn ngay** (N-2), không kiểm Payment có phải bản mới nhất. Đơn vừa được duyệt mất sau một lần thẻ lỗi; IPN thất bại của link cũ hủy nhầm đơn đang trả bằng link mới.
- **IPN thành công trên đơn đã hủy bị bỏ qua** (N-11): tiền bị trừ mà không có bản ghi.
- **IPN thành công thua cuộc đua với lệnh hủy**: khách hủy đúng lúc tiền về, lệnh hủy thắng, IPN ghi log rồi bỏ đi; cũng mất dấu tiền.
- **Chống lặp dựa trên trạng thái đơn**, không dựa trên Payment.
- **Nghiệp vụ gắn chặt VNPay:** `HandleVnPayIpnAsync` vừa kiểm chữ ký VNPay vừa xử lý nghiệp vụ; tạo giao dịch online có hai bản riêng (`BookingService.CreateDepositPaymentAsync` lúc đặt sân, `PaymentService.RetryPaymentAsync` lúc thanh toán lại), cả hai gọi thẳng `IVnPayService`. Thêm một cổng mới sẽ phải chép lại cả ba chỗ.

**Check-out:** hóa đơn `PARTIALLY_PAID` chỉ thu phí dịch vụ (R-3) ⇒ đơn cọc 30 % mất 70 % tiền sân.

**Transaction (N-29):** 12 transaction còn mức mặc định Serializable. Hai thao tác đồng thời trên cùng dữ liệu (hai nhân viên cùng hủy, hủy trùng IPN, hai đơn tại quầy cùng giờ) cho 500 thay vì 409/400.

| # | Hàm | Ghi gì |
| :---: | :--- | :--- |
| 1 | `BookingService.CreateWalkInAsync` | Booking, sân, hóa đơn (exclusion constraint chống trùng) |
| 2 | `CancelByCustomerAsync` | Hủy |
| 3 | `CancelByTokenAsync` | Hủy |
| 4 | `CheckInAsync` | Trạng thái, sân (ghi đè entity, không điều kiện) |
| 5 | `CheckoutAsync` | Trạng thái (có điều kiện), Payment, hóa đơn |
| 6 | `AddServiceAsync` | Dịch vụ, hóa đơn (đọc rồi ghi đè cả hóa đơn) |
| 7 | `RemoveServiceAsync` | Như trên |
| 8 | `ConfirmRefundAsync` | Refund, Payment, hóa đơn, trạng thái (không điều kiện) |
| 9 | `EarnLoyaltyPointsAsync` (lồng trong #5) | Điểm (cộng nguyên tử) |
| 10 | `DeductLoyaltyPointsAsync` (lồng trong #8) | Điểm (trừ nguyên tử) |
| 11 | `PaymentService.RetryPaymentAsync` | Payment, hạn |
| 12 | `PaymentService.HandleVnPayIpnAsync` | Thanh toán |

**Bẫy khi đổi:** #9 và #10 mở `TransactionScope` lồng trong #5 và #8. Scope lồng khác mức cô lập với scope ngoài thì .NET ném `ArgumentException` lúc chạy (build vẫn xanh). Phải đổi cặp ngoài–trong cùng lúc và có test chạy trên DB thật.

**Email:** email xác nhận thanh toán chưa ghi "đã cọc / còn lại" (7.13).

### 0.3. Spec có, mã chưa có (giữ đúng spec)

- Dòng "IPN thất bại" của 7.5: `expires_at = min(expires_at, now + 5')`, không bao giờ kéo dài.
- Mã lỗi `BOOKING_ALREADY_STARTED` (400).
- `Services/BookingCancellationService` (7.9) nhận `TimeProvider` (T-14).

### 0.4. Điểm đã chốt (24/10, theo đề xuất)

| # | Vấn đề | Đã chốt |
| :---: | :--- | :--- |
| **C1** | **Khách trả hai lần cho cùng một đơn.** `VNPAY.NET` không gửi hạn phiên (N-15) nên link cũ vẫn trả được sau khi khách bấm "Thanh toán lại". Nếu khách trả cả link cũ lẫn link mới: lần thứ hai đến khi đơn đã `PAID_ONLINE`. Spec chỉ xử lý "tiền về khi đơn **đã hủy**" (P6-07); ca "đơn **còn hiệu lực** đã được trả" không có trong spec, mã hiện tại bỏ qua (mất dấu tiền) | **Ghi Payment `SUCCESS`, không đổi đơn, log Warning.** Khoản trả thừa tự được trừ vào phần thu tại quầy lúc check-out (P6-12 tính `finalTotal − Σ SUCCESS`), và được hoàn theo chính sách nếu khách hủy. Trả thừa vượt cả tổng đơn (hai lần cọc 100 %) ⇒ check-out thu 0, log Error ghi số tiền thừa để nhân viên hoàn tiền mặt tại chi nhánh. Phương án khác: tạo Refund 100 % ngay cho khoản thừa, nhưng hàng "Chờ hoàn tiền" chỉ lọc theo trạng thái đơn nên nhân viên không thấy, phải sửa thêm màn hình FE |
| **C2** | **Một đơn có nhiều Payment `SUCCESS` thì hoàn thế nào.** Bảng `refunds` gắn với **một** Payment; `ConfirmRefundAsync` đặt `refunded_amount` của Payment đó bằng số hoàn. Hoàn `Σ SUCCESS × %` vào một Payment sẽ vượt `amount` của nó, vi phạm `chk_payments_refunded_range` ⇒ 500. Chỉ xảy ra khi có C1 hoặc P6-07 trên đơn đã hoàn một phần | **Một Refund cho mỗi Payment `SUCCESS`** (`round(amount × %)`), `ConfirmRefundAsync` xác nhận mọi Refund `PENDING` của đơn (khách nhận một lần tổng tiền mặt tại chi nhánh; tách theo Payment chỉ để sổ sách đúng ràng buộc). Đơn một Payment (gần như mọi đơn) cho đúng công thức spec; đơn nhiều Payment lệch tối đa 1 đ mỗi Payment do làm tròn từng phần. Ghi vào spec 7.9 |
| **C3** | **Email "tiền về sau khi hủy"** (spec 7.13 liệt kê mẫu riêng trong `Templates/`) | **Dùng lại email hủy có sẵn** (`SendCancelConfirmationAsync`: "đơn đã hủy, sẽ hoàn X đ") với X = số vừa về. Nội dung đã đúng ý, không cần mẫu mới. Nếu muốn mẫu riêng (giải thích "tiền về sau khi đơn hết hạn") thì thêm 1 file `Templates/` |
| **C4** | **Lệnh từ chối của 6A-3** (spec 7.7: "6A-4 chuyển phần chung vào dịch vụ hủy chung mà không đổi hành vi") | **Giữ nguyên.** Từ chối đã đi qua câu cập nhật có điều kiện riêng (cần thêm điều kiện "còn hạn duyệt"), đơn chờ duyệt chưa có tiền, chưa tính lượt khuyến mãi; chuyển sang dịch vụ chung chỉ đổi chỗ code mà thêm rủi ro. Ghi vào spec 7.7. Job hết hạn duyệt (6A-5) sẽ dùng dịch vụ chung |

Thay đổi chỉ về cấu trúc (không cần chốt, ghi lại sau): chuyển 4 hàm hủy (`CancelByStaffAsync`, `CancelByCustomerAsync`, `CancelByTokenAsync`, `GetCancelInfoAsync`) từ `BookingService` sang `BookingCancellationService`; hai controller gọi service mới; route và response không đổi.

### 0.5. Việc người dùng làm (không chặn code)

- **Kiểm mức tối thiểu của VNPay sandbox** (rủi ro Mục 11 của spec: "xác minh đầu 6A-4"): tạo một giao dịch 5.000 đ trên sandbox. VNPay từ chối thì báo lại mức thật để đổi `BookingPayment:MinOnlinePaymentAmount`.

---

## Phần A — Nền: luật thuần và câu ghi có điều kiện (chưa đổi hành vi)

### Task 1 — Mã lỗi và hạn sau thanh toán thất bại (BE-UT-08)

**Vấn đề:** chưa có mã `BOOKING_ALREADY_STARTED`; `PaymentHoldPolicy` thiếu dòng "IPN thất bại" của 7.5.

**Làm gì:**
- `Common/ErrorCodes.cs`: `BookingAlreadyStarted = "BOOKING_ALREADY_STARTED"` vào nhóm Deposit Approval.
- `Services/Deposit/PaymentHoldPolicy.ForPaymentFailure(DateTime nowUtc, DateTime expiresAtUtc, int graceMinutes)` trả `min(expiresAtUtc, nowUtc + grace)`; kiểm `Kind = Utc` như các hàm khác.

**Kiểm (`Unit/Services/Deposit/PaymentHoldPolicyFailureTests`):** hạn còn 8' ⇒ rút về 5'; còn 3' ⇒ giữ 3' (không bao giờ kéo dài); đúng biên 5'; đã quá hạn ⇒ giữ hạn cũ; `Kind` khác `Utc` ⇒ `ArgumentException`.

### Task 2 — Luật hủy thuần: tiền hoàn, đã tới giờ, trả lượt khuyến mãi (BE-UT-10, BE-UT-12)

**Vấn đề:**
- **Tiền hoàn sai hai chỗ.** (1) Tính trên `FinalTotal` thay vì số đã trả (R-1): cọc 30 % mà hủy sớm thì hoàn 100 % tổng. (2) Số giờ tới giờ chơi lấy giờ tường VN trừ giờ UTC (T-1): dư 7 giờ, khách hủy trước 18 giờ được tính như 25 giờ.
- **Khách hủy sau giờ bắt đầu** để né `NO_SHOW` (T-4).
- **Trả lượt khuyến mãi sai** (N-8): đơn online chỉ được tính lượt khi thanh toán thành công (hoặc khi xác nhận không qua VNPay), nên hủy đơn chưa trả không được trả lượt.

**Làm gì:** `Services/Cancellation/CancellationPolicy.cs` (static, không I/O), kiểu kết quả ở `Models/Cancellation/`:
- `HasStarted(DateOnly date, TimeOnly earliestStart, DateTime nowUtc)`: `nowUtc ≥ DateTimeHelper.ToUtcFromVietnam(date, earliestStart)`.
- `RefundPercent(IReadOnlyCollection<CancelPolicy> policies, DateTime playStartUtc, DateTime nowUtc)`: đã tới giờ ⇒ 0; ngược lại mốc `HoursBefore` lớn nhất ≤ số giờ còn lại.
- `QuoteRefund(policies, playStartUtc, nowUtc, IReadOnlyCollection<Payment> successfulPayments)` ⇒ `RefundQuote(decimal Percent, decimal Amount, IReadOnlyList<RefundShare> Shares)`; mỗi Payment một `RefundShare(PaymentId, Amount)` với `round(amount − refundedAmount) × % / 100` (theo C2), bỏ phần bằng 0.
- `ShouldReleasePromotionUse(Invoice invoice)`: có Payment `SUCCESS` hoặc `PaymentTiming = POSTPAID`.
- `BookingCancellationService` và `GetCancelInfoAsync` dùng chung các hàm này; xóa `CalculateRefundPercentAsync` cũ.

**Kiểm (`Unit/Services/Cancellation/CancellationPolicyTests`, giờ cố định bằng tham số):**
- % hoàn ở từng mốc của bảng `cancel_policies` mẫu: đúng mốc, trước/sau 1 phút, theo giờ VN (ca 00:30, 06:59, 23:45 giờ VN, đơn ngày hôm sau).
- Ca lệch 7 giờ của mã cũ: hủy trước 18 giờ ⇒ ra % của mốc 18 giờ, không phải 24 giờ.
- Tiền hoàn: cọc 30 % hủy sớm ⇒ 30 % tổng; trả 100 % ⇒ như cũ; làm tròn VND (0,5 đ); hai Payment ⇒ hai phần; Payment đã hoàn một phần chỉ tính phần còn lại; không Payment ⇒ 0.
- `HasStarted`: đúng giờ bắt đầu ⇒ true; trước 1 phút ⇒ false; đơn qua nửa đêm.
- `ShouldReleasePromotionUse`: đơn a/d đã trả ⇒ true; a/d chưa trả ⇒ false; a′ và duyệt 0 % (`POSTPAID`) ⇒ true; chờ duyệt ⇒ false; tại quầy trả sau / trả ngay ⇒ true.

### Task 3 — Câu ghi có điều kiện cho Payment, hạn và hoàn tiền (repository)

**Vấn đề:** các đường mới cần ghi chỉ khi dữ liệu còn đúng như lúc đọc. Mã cũ đọc entity rồi ghi đè cả dòng, nên thao tác đồng thời có thể ghi đè nhau.

**Làm gì** (LINQ `ExecuteUpdateAsync`, không SQL thô; mỗi hàm một dòng `//` trong interface):
- `IPaymentRepository`:
  - `TryMarkSuccessAsync(paymentId, nowUtc)`: chỉ khi `status <> SUCCESS`.
  - `TryFailPendingAsync(paymentId, nowUtc)`: chỉ khi `PENDING`.
  - `FailPendingByInvoiceAsync(invoiceId, exceptPaymentId?, nowUtc)`.
  - `GetSuccessfulByInvoiceAsync(invoiceId)`.
- `IBookingRepository`:
  - `ShortenPaymentWindowAsync(bookingId, expiresAtUtc, nowUtc)`: chỉ khi đơn `PENDING`; đặt hạn hóa đơn và slot lock bằng giá trị nhỏ hơn giữa hạn cũ và hạn mới.
  - `MarkLatePaymentRefundAsync(bookingId, nowUtc)`: `CANCELLED → CANCELLED_PENDING_REFUND`, giữ `cancel_reason`.
- `IRefundRepository.GetPendingByBookingIdAsync` (danh sách, thay bản lấy một).

**Kiểm (`Integration/PaymentGateRepositoryTests`, PostgreSQL thật):** mỗi hàm thắng một lần, thua khi trạng thái đã đổi; `ShortenPaymentWindowAsync` không kéo dài hạn và không đụng đơn khác `PENDING`; ràng buộc `chk_payments_paid_at` đạt khi đánh `SUCCESS`.

---

## Phần B — Hủy

### Task 4 — Dịch vụ hủy chung (`BookingCancellationService`)

**Vấn đề:** ba đường hủy là ba bản chép tay lệch nhau (bảng 0.2). Mỗi lần sửa một quy tắc phải sửa ba chỗ, và đã sót.

**Làm gì:** `Services/BookingCancellationService.cs` + `IBookingCancellationService` (nhận `TimeProvider`). Một hàm nội bộ chạy 6 bước của 7.9, trong `TransactionScopeHelper.ReadCommitted()`:
1. Tính tiền hoàn (Task 2) **trước** khi ghi, rồi gọi `CancelWithStatusCheckAsync` với **đúng trạng thái đã đọc** và trạng thái mới `CANCELLED_PENDING_REFUND` (có tiền hoàn) hoặc `CANCELLED`. Thua ⇒ 409 `CONFLICT`.
2. Nhả sân: `booking_courts.is_active = false`, xóa slot lock, sân về `AVAILABLE` nếu không còn đơn khác đang giữ. Một cách cho mọi đường (bản của nhân viên trước đây không kiểm sân bận).
3. `FailPendingByInvoiceAsync`.
4. Mỗi `RefundShare` một Refund `PENDING`.
5. `ShouldReleasePromotionUse` ⇒ `DecrementUsageCountAsync`.
6. Sau commit: `IBookingNotificationService.NotifyCancelledAsync(booking, previousStatus, refundAmount)`: email hủy (có số hoàn), báo người chờ slot, `BookingCancelled` kèm time grid, và nếu đơn đang chờ duyệt thì thêm `PendingApprovalQueueChanged` `REMOVED`/`CANCELLED`.

Không còn lệnh `UpdateAsync(booking)` ghi đè cả entity sau câu cập nhật có điều kiện.

**Kiểm (`Unit/Services/BookingCancellationServiceTests`):**
- Ma trận trạng thái × tiền:
  - `PENDING` chưa trả;
  - `PENDING_APPROVAL`;
  - `CONFIRMED` tại quầy chưa trả và đã trả;
  - a′ (`POSTPAID`, không Payment);
  - `PAID_ONLINE` cọc 30 %;
  - `PAID_ONLINE` trả 100 %.
- Với mỗi ca, kiểm: trạng thái mới, số hoàn, số Refund, Payment chờ ⇒ `FAILED`, lượt khuyến mãi ±0/−1.
- Thua câu cập nhật có điều kiện ⇒ 409, không ghi gì thêm.
- Email/SignalR lỗi không làm hỏng lệnh hủy.

### Task 5 — Nhân viên hủy (R-2, P6-14)

**Vấn đề:** chưa hủy được đơn chờ duyệt; ghi không điều kiện, không transaction; `cancel_reason` trống.

**Làm gì:** `CancelByStaffAsync` chuyển sang `BookingCancellationService`:
- Kiểm quyền chi nhánh như cũ.
- Nhận `PENDING`, `PENDING_APPROVAL`, `CONFIRMED`, `PAID_ONLINE`.
- `cancel_source = STAFF`, `cancel_reason = STAFF_CANCELLED`, `cancelled_by`.
- Không kiểm giờ bắt đầu (nhân viên hủy được).

`BookingController` gọi service mới; route và response giữ nguyên.

**Kiểm:**
- Unit: từng trạng thái nhận / từ chối (`IN_PROGRESS` ⇒ 400 như cũ).
- Sai chi nhánh ⇒ 403.
- Sau giờ bắt đầu vẫn hủy được.

### Task 6 — Khách hủy trong app (P6-09, P6-23)

**Vấn đề:** khách không tự hủy được đơn chưa trả hoặc đơn chờ duyệt, nhưng lại hủy được sau giờ bắt đầu.

**Làm gì:** `CancelByCustomerAsync` chuyển sang service mới:
- Thứ tự kiểm: chủ đơn ⇒ tài khoản bị khóa ⇒ đã hủy rồi (trả thành công như cũ) ⇒ **đã tới giờ bắt đầu ⇒ 400 `BOOKING_ALREADY_STARTED`** ⇒ trạng thái nhận được (`PENDING`, `PENDING_APPROVAL`, `CONFIRMED`, `PAID_ONLINE`).
- `CUSTOMER` / `CUSTOMER_CANCELLED`.
- Token hủy bị vô hiệu trong cùng transaction.

`CustomerBookingController` gọi service mới.

**Kiểm:**
- Đúng giờ bắt đầu ⇒ 400; trước 1 phút ⇒ hủy được; đơn đã trả 100 % sau giờ bắt đầu ⇒ 400, đơn không đổi.
- Đơn `PENDING` ⇒ `CANCELLED`, không hoàn, không trả lượt.
- Đơn của người khác ⇒ 403.

### Task 7 — Hủy bằng link và xem trước (sửa lỗi tiêu token sớm)

**Vấn đề:**
- Token bị tiêu **trước** khi kiểm hạn, trạng thái, và ngoài transaction hủy (mục 0.2). Lỗi sau bước đó làm link chết mà đơn không hủy.
- Xem trước dùng `FinalTotal` và giờ lệch 7 giờ.
- Hủy bằng link không trả lượt khuyến mãi.

**Làm gì:**
- `CancelByTokenAsync` kiểm theo thứ tự: token có tồn tại ⇒ tài khoản bị khóa ⇒ đã hủy rồi (trả thành công) ⇒ link hết hạn ⇒ đã tới giờ bắt đầu ⇒ trạng thái nhận được. Sau đó mới mở transaction: `TryConsumeTokenAsync` rồi tới bước 1 của dịch vụ hủy. Thua ở bất kỳ bước nào ⇒ rollback cả việc tiêu token.
- `GetCancelInfoAsync` dùng `QuoteRefund` (đơn a′, duyệt 0 %: hoàn 0).

**Kiểm:**
- Unit: link hết hạn ⇒ lỗi hết hạn, token **chưa** tiêu; trạng thái không hủy được ⇒ token chưa tiêu; thua câu cập nhật có điều kiện ⇒ 409 và token chưa tiêu (bấm lại được).
- Hai người bấm cùng link ⇒ một thành công, một nhận "đã được sử dụng".
- Xem trước và lệnh hủy ra cùng một số tiền.

---

## Phần C — Thanh toán

### Task 8 — Lõi thanh toán dùng chung cho mọi cổng

**Vấn đề:** nghiệp vụ thanh toán gắn chặt VNPay (mục 0.2). Người dùng định thêm phương thức thanh toán khác; nếu viết Task 9–12 thẳng vào `HandleVnPayIpnAsync` thì mỗi cổng mới phải chép lại toàn bộ quy tắc ân hạn, tiền về sau khi hủy, trả hai lần.

**Làm gì** (chỉ đổi cấu trúc; route `GET /api/payments/vnpay/ipn`, `/callback`, `POST /confirm` và response giữ nguyên):
- `Services/IService/IPaymentGateway.cs`: hợp đồng của một cổng.
  - `PaymentTxMethod Method`
  - `GatewayPaymentLink CreatePaymentLink(decimal amount, string orderInfo)`
  - `GatewayPaymentResult ReadCallback(IQueryCollection query)`: xác thực chữ ký, trả kết quả chuẩn hóa.
- `VnPayService` hiện thực `IPaymentGateway`. `IVnPayService` được giữ cho callback trình duyệt (`HandleVnPayReturnAsync`) nếu còn dùng; nếu không còn chỗ dùng thì gỡ.
- `Models/Payments/GatewayPaymentResult.cs`: `Provider`, `TransactionRef`, `IsSignatureValid`, `IsSuccess`, `Amount` (VND, đã quy đổi từ đơn vị của cổng), `ResponseCode`, `RawPayload`. `GatewayPaymentLink`: `Url`, `TransactionRef`.
- `Services/Payments/PaymentGatewayResolver.cs`: chọn cổng theo `PaymentTxMethod` từ các `IPaymentGateway` đã đăng ký DI. Hiện chỉ có `VNPAY`; phương thức chưa đăng ký ⇒ `InvalidOperationException` (lỗi cấu hình).
- `Services/PaymentResultService.cs` + `IPaymentResultService.ApplyAsync(GatewayPaymentResult)`: ghi IPN log, tra Payment theo `TransactionRef`, kiểm số tiền = `payment.Amount`, rồi rẽ nhánh Task 9–12. Không biết gì về VNPay.
- `PaymentService.HandleVnPayIpnAsync` còn: `gateway.ReadCallback(query)` ⇒ `paymentResults.ApplyAsync(result)`.
- `Services/OnlinePaymentStarter.cs` + `IOnlinePaymentStarter.StartAsync(Invoice invoice, decimal amount, string orderInfo, PaymentTxMethod method, DateTime nowUtc)`: tạo link qua cổng và ghi Payment `PENDING` đúng `Method`. Đặt sân online và thanh toán lại cùng gọi hàm này; xóa hai bản tạo URL riêng.

**Kiểm:**
- `Unit/Services/Payments/PaymentGatewayResolverTests`: chọn đúng cổng; phương thức chưa đăng ký ⇒ lỗi rõ ràng.
- `Unit/Services/VnPayServiceTests`: `ReadCallback` quy đổi `vnp_Amount` (×100) đúng; chữ ký sai ⇒ `IsSignatureValid = false`; mã `00` ⇒ thành công, mã khác ⇒ thất bại.
- `PaymentResultService` test bằng một cổng giả trong test (không phải VNPay), để chứng minh nghiệp vụ không phụ thuộc VNPay: chữ ký sai ⇒ chỉ ghi log; Payment không tồn tại ⇒ chỉ ghi log; số tiền lệch ⇒ chỉ ghi log, không đổi gì.
- Test IPN HTTP hiện có (6A-2, 6A-3) giữ xanh.

### Task 9 — Thanh toán thất bại không hủy đơn (P6-03)

**Vấn đề:** thẻ lỗi một lần là mất đơn, kể cả đơn vừa được duyệt; IPN thất bại của link cũ hủy nhầm đơn đang trả bằng link mới.

**Làm gì:** trong `PaymentResultService`, transaction Read Committed:
- `TryFailPendingAsync(payment)`. Thua (Payment không còn `PENDING`, tức link cũ hoặc IPN lặp) ⇒ chỉ log, kết thúc.
- Thắng ⇒ `ShortenPaymentWindowAsync(booking, ForPaymentFailure(...))`. Đơn giữ `PENDING`, không hủy, không nhả sân.
- Sau commit: `PaymentFailed` kèm time grid (người khác thấy số phút còn lại), message "Thanh toán thất bại, bạn còn N phút để thanh toán lại".

**Kiểm:**
- Unit: hạn 8' ⇒ 5'; hạn 3' ⇒ 3'; IPN thất bại của link cũ ⇒ không đổi đơn, không đổi hạn; IPN lặp ⇒ một lần.
- Đơn đã hủy ⇒ Payment `FAILED`, đơn không đổi.

### Task 10 — IPN thành công: chống lặp theo Payment, email ghi cọc

**Vấn đề:** chống lặp dựa trên trạng thái đơn; Payment chờ khác của hóa đơn vẫn `PENDING`; email xác nhận chưa ghi đã cọc / còn lại.

**Làm gì:**
- `TryMarkSuccessAsync(payment)` thua ⇒ IPN lặp, kết thúc.
- `AtomicUpdatePaymentSuccessAsync` (`PENDING → PAID_ONLINE`) thắng ⇒ luồng hiện có (hóa đơn `PARTIALLY_PAID`, xóa slot lock, tăng lượt khuyến mãi, token hủy, email), cộng thêm `FailPendingByInvoiceAsync(except = payment)`.
- Thua ⇒ đọc lại trạng thái đơn và rẽ nhánh sang Task 11 (đơn đã hủy) hoặc C1 (đơn còn hiệu lực).
- `BookingEmailFactory.Build` và `BookingConfirmation.html` thêm dòng "Đã cọc X đ, còn lại Y đ thu tại quầy" khi hóa đơn có cọc; đơn cũ (không cọc) giữ nguyên.

**Kiểm:**
- IPN lặp hai lần ⇒ một Payment `SUCCESS`, lượt khuyến mãi +1 một lần.
- Hai Payment chờ ⇒ cái còn lại `FAILED`.
- Link cũ (Payment đã `FAILED` vì thanh toán lại) vẫn được ghi nhận khi đơn còn `PENDING`.
- Email có / không có dòng cọc.

### Task 11 — Tiền về sau khi đơn đã hủy (P6-07)

**Vấn đề:** đơn hết hạn hoặc khách vừa hủy, rồi tiền mới về (phiên VNPay còn sống): BE bỏ qua, tiền bị trừ mà không ai biết. Ca đua: khách bấm hủy đúng lúc tiền về, lệnh hủy thắng.

**Làm gì:** đơn ở `CANCELLED` (đọc lại sau khi thua ở Task 10):
- `TryMarkSuccessAsync`, Refund 100 % `PENDING` cho Payment đó, `MarkLatePaymentRefundAsync` (giữ `cancel_reason`), log Warning.
- Sau commit: email hủy kèm số hoàn (C3), `BookingCancelled` để nhân viên thấy đơn trong hàng "Chờ hoàn tiền".
- Đơn đã `CANCELLED_PENDING_REFUND` hoặc `CANCELLED_REFUNDED` (đã hoàn phần trước đó) ⇒ thêm Refund 100 % cho Payment mới; trạng thái `CANCELLED_REFUNDED` thì đưa về `CANCELLED_PENDING_REFUND`. Cần thêm cạnh này vào `BookingStatusTransition` (ghi spec 7.12).

**Kiểm:**
- Đơn `CANCELLED` ⇒ `CANCELLED_PENDING_REFUND`, Refund = số vừa về, `cancel_reason` giữ nguyên.
- Nhân viên xác nhận hoàn ⇒ `CANCELLED_REFUNDED`.
- Ca đua (lệnh hủy thắng trước IPN) ⇒ vẫn có Refund.
- IPN lặp ⇒ một Refund.

### Task 12 — Trả hai lần cho đơn còn hiệu lực (theo C1)

**Vấn đề:** xem C1.

**Làm gì (theo đề xuất C1):**
- Đơn `PAID_ONLINE`, `CONFIRMED`, `IN_PROGRESS`, `PENDING_PAYMENT`: `TryMarkSuccessAsync`, log Warning ghi mã đơn và số tiền; đơn và hóa đơn không đổi.
- Đơn `COMPLETED`: thêm log Error "trả thừa sau khi đã check-out, cần hoàn tay".

**Kiểm:**
- Trả hai lần tiền cọc 30 % ⇒ check-out thu `finalTotal − 60 %`.
- Hủy sau đó ⇒ hai Refund theo %.
- Trả hai lần 100 % ⇒ check-out thu 0, có log Error.

### Task 13 — Thanh toán lại: Read Committed và câu cập nhật có điều kiện (phần còn lại của 7.8)

**Vấn đề:** 6A-3 đã sửa số tiền và hạn; còn ghi đè entity trong transaction Serializable. Thanh toán lại trùng IPN hoặc trùng lệnh hủy ⇒ 500, hoặc kéo hạn của đơn vừa bị hủy.

**Làm gì:**
- `FailPendingByInvoiceAsync`.
- Gia hạn bằng hàm có điều kiện "đơn còn `PENDING`" (hạn hóa đơn và slot lock). Thua ⇒ 409 `CONFLICT`, không tạo Payment.
- Tạo link và Payment `PENDING` qua `IOnlinePaymentStarter` (Task 8), cùng phương thức với Payment trước của đơn.
- `ReadCommitted()`.

**Kiểm:**
- Đơn bị hủy giữa lúc đọc và lúc ghi ⇒ 409, không có Payment mới.
- Các test 6A-3 của `PaymentServiceRetryDepositTests` giữ xanh.

---

## Phần D — Check-out và các transaction cũ

### Task 14 — Check-out thu phần còn lại (P6-12, R-3)

**Vấn đề:** đơn cọc chỉ thu dịch vụ, bỏ sót tiền sân chưa cọc.

**Làm gì:**
- `remaining = max(0, latestInvoice.FinalTotal − GetCollectedAmountAsync)`.
- `remaining > 0` ⇒ Payment `CASH SUCCESS` bằng `remaining`. Hóa đơn `PAID`.
- Thay hai nhánh `UNPAID` / `PARTIALLY_PAID`: đơn tại quầy và đơn trả 100 % cho cùng kết quả.
- `ReadCommitted()`. `EarnLoyaltyPointsAsync` đổi scope lồng sang `ReadCommitted()` cùng lúc (bẫy mục 0.2).

**Kiểm (BE-IT-10, trên DB thật):**
- Cọc 30 % + dịch vụ 20.000 đ ⇒ thu 70 % + 20.000.
- Trả 100 % + dịch vụ ⇒ thu dịch vụ.
- Tại quầy chưa trả ⇒ thu toàn bộ.
- a′ ⇒ thu toàn bộ.
- Trả thừa (C1) ⇒ thu 0, không tạo Payment.
- Khách có tài khoản ⇒ tích điểm chạy được (chứng minh scope lồng không ném lỗi).
- Hai check-out đồng thời ⇒ 200 và 409.

### Task 15 — Hạ mức cô lập các transaction còn lại (N-29)

**Vấn đề:** xem bảng 12 transaction (mục 0.2). Chỉ hạ mức cô lập khi mọi lệnh ghi trong transaction đã có điều kiện; hạ trước thì "đọc rồi ghi đè" sẽ thành mất cập nhật thay vì lỗi.

**Làm gì:**

| Hàm | Đổi gì để an toàn với Read Committed |
| :--- | :--- |
| `CreateWalkInAsync` | Giống transaction 1 của đặt online: trùng sân do exclusion constraint chặn (đã nhận 23P01/40P01); mã đơn từ sequence ⇒ chỉ đổi mức |
| `CheckInAsync` | Thay ghi đè entity bằng câu cập nhật có điều kiện `CONFIRMED/PAID_ONLINE → IN_PROGRESS` (đúng trạng thái đã đọc); thua ⇒ 409 |
| `AddServiceAsync`, `RemoveServiceAsync` | Ghi hóa đơn bằng câu cập nhật có điều kiện "hóa đơn chưa `PAID`, đơn còn sửa được", tính `service_fee` từ bảng dịch vụ ngay trong câu lệnh; thua ⇒ 400 hiện có ("hóa đơn đã thanh toán") |
| `ConfirmRefundAsync` | Refund `PENDING → COMPLETED` và đơn `CANCELLED_PENDING_REFUND → CANCELLED_REFUNDED` có điều kiện; xác nhận mọi Refund chờ của đơn (C2); `DeductLoyaltyPointsAsync` đổi scope lồng cùng lúc |
| Các đường hủy, IPN, thanh toán lại, check-out | Đã đổi ở Task 4–14 |

**Kiểm (`Integration/Http/ConcurrencyRegressionTests`, hai kết nối DB thật như `ApprovalConcurrencyTests`):**
- Hai đơn tại quầy cùng sân cùng giờ ⇒ 201 và 400 `SLOT_UNAVAILABLE` (không 500).
- Hai lần check-in ⇒ 200 và 409.
- Thêm dịch vụ trùng check-out ⇒ hoặc dịch vụ vào hóa đơn trước khi thu, hoặc 400; không bao giờ có dịch vụ chưa thu trên hóa đơn `PAID`.
- Hai lần xác nhận hoàn ⇒ 200 và 400.
- Nhân viên hủy trùng IPN thành công ⇒ đơn hủy kèm Refund, hoặc đơn `PAID_ONLINE`; không bao giờ tiền vào đơn hủy mà không có Refund.
- Nhân viên hủy trùng khách hủy ⇒ 200 và 409.

---

## Phần E — Kiểm thử HTTP trên host thật và tài liệu

### Task 16 — BE-IT-21: thanh toán thất bại

**Kịch bản:**
- IPN thất bại ⇒ Payment `FAILED`, đơn `PENDING`, hạn và slot lock ≤ 5'.
- `retry-payment` trong ân hạn ⇒ URL mới; IPN thành công ⇒ `PAID_ONLINE`.
- Bỏ đi ⇒ Job-01 hủy (lý do `PAYMENT_EXPIRED` do 6A-5 sửa Job-01; ở 6A-4 kiểm đơn bị hủy và slot nhả).
- Bấm `retry-payment` liên tục ⇒ dừng ở hạn giữ chỗ (400 `PAYMENT_HOLD_EXPIRED`).
- IPN thất bại của link cũ ⇒ không đổi đơn.
- Đơn đã duyệt, thẻ lỗi lúc phút 3 ⇒ vẫn trả được trong 5'.

### Task 17 — BE-IT-22, BE-IT-23, BE-IT-10: hủy và tiền

**BE-IT-22:**
- Đơn hết hạn rồi tiền về ⇒ `CANCELLED_PENDING_REFUND`, Refund 100 %, `cancel_reason` giữ nguyên.
- Nhân viên xác nhận hoàn qua endpoint có sẵn ⇒ `CANCELLED_REFUNDED`.

**BE-IT-23:**
- Khách hủy `PENDING` / `PENDING_APPROVAL`, nhân viên hủy `PENDING_APPROVAL` ⇒ đúng cột hủy, không hoàn, `UsedCount` không đổi, Payment chờ ⇒ `FAILED`, slot nhả.
- Có `PendingApprovalQueueChanged`.
- Khách đặt lại được khuyến mãi giới hạn 1 lượt/khách.

**BE-IT-10:**
- Cọc 30 %, hủy sớm ⇒ hoàn 30 %; hủy sát mốc ⇒ % theo giờ VN.
- Đơn khuyến mãi đã trả bị hủy ⇒ `UsedCount −1`; a′ bị hủy bằng link ⇒ `−1`.
- Khách hủy sau giờ bắt đầu ⇒ 400 `BOOKING_ALREADY_STARTED` qua cả app và link.
- Phần check-out ở Task 14.

### Task 18 — BE-IT-25 (phần 6A-4)

**Kịch bản:**
- Nhân viên tạo đơn tại quầy trong ngày cho khách có email ⇒ `cancel_token_expires_at` = giờ bắt đầu theo UTC, email được gửi.
- Mọi cột thời gian mới do BE ghi trong gói này (`cancelled_at`, `paid_at`, `processed_at` của Refund, hạn rút ngắn) nằm giữa hai mốc UTC lấy ngay trước và sau request. Host test HTTP dùng đồng hồ thật, không có `FakeTimeProvider`; ca giờ cố định (00:30, 06:59, 23:45 giờ VN) kiểm ở unit test bằng `FakeTimeProvider`.

Phần báo cáo, dashboard, lọc điểm (T-9…T-11) để 6A-6.

### Task 19 — Tài liệu và kiểm tra cuối gói

**Cập nhật tài liệu:**
- **Spec:**
  - 7.7: C4.
  - 7.8: C1, Task 8 (lõi thanh toán dùng chung), Task 10, 11.
  - 7.9: C2, thứ tự kiểm của link hủy.
  - 7.12: cạnh `CANCELLED_REFUNDED → CANCELLED_PENDING_REFUND`.
  - 7.13: C3.
  - Mục 11: rủi ro còn lại.
  - Mục 12: các N/R/T đã đóng.
- **API doc:**
  - 3.4: thứ tự lỗi khi hủy bằng link.
  - 3.6: trả hai lần (C1).
  - Mục 5: ghi chú dòng 21 cho cả link hủy.
- **Sơ đồ:** tuần tự 5 và 6.
- **Methodology:** BE-IT-10, 21, 22, 23 trỏ đúng test.
- Ghi kết quả vào cuối plan này.

**Kiểm cuối gói:**
- `check_docs`.
- Build 0 cảnh báo; toàn suite 3 lần liên tiếp.
- Chạy đột biến cho `CancellationPolicy`, `PaymentHoldPolicy.ForPaymentFailure` và công thức check-out; script luôn build lại ở cuối.

---

## Kết quả thực hiện (24/10/2026) ✅

Toàn bộ suite **1812 xanh, 3 lần liên tiếp** (trước gói: 1626), build 0 cảnh báo, chưa commit. Đột biến **18/18 bị bắt**: luật hủy 7, ân hạn 1, công thức check-out 2, lõi thanh toán 5, câu cập nhật có điều kiện của test đồng thời 3. Script đột biến luôn build lại ở cuối.

| Task | Đã làm | Test |
| :---: | :--- | :--- |
| 1 | `BOOKING_ALREADY_STARTED`; `PaymentHoldPolicy.ForPaymentFailure` | `PaymentHoldPolicyFailureTests` (9) |
| 2 | `Services/Cancellation/CancellationPolicy` (`HasStarted`, `RefundPercent`, `QuoteRefund` tách theo Payment, `ShouldReleasePromotionUse`); `Models/Cancellation/RefundQuote` | `CancellationPolicyTests` (33) |
| 3 | Payment: `TryMarkSuccessAsync`, `TryFailPendingAsync`, `FailPendingByInvoiceAsync`, `GetSuccessfulByInvoiceAsync`. Booking: `ShortenPaymentWindowAsync`, `MarkPendingRefundAsync`. Refund: `GetPendingByBookingIdAsync` (gỡ bản lấy một) | `PaymentGateRepositoryTests` (20, PostgreSQL thật) |
| 4–7 | `BookingCancellationService` nhận 4 hàm hủy (`BookingService` 3096 → 2388 dòng; `PaymentService` khoảng 600 → 211). Nhả sân một cách cho mọi đường. Link hủy kiểm hết rồi mới tiêu token trong cùng transaction (sửa lỗi có sẵn). Khách hủy trong app vô hiệu link email. `NotifyCancelledAsync` thêm `PendingApprovalQueueChanged` `REMOVED`/`CANCELLED` | `BookingCancellationServiceTests` (46) |
| 8 | `IPaymentGateway` (`VnPayService`), `PaymentGatewayResolver`, `OnlinePaymentStarter`, `PaymentResultService`; gỡ `IVnPayService`. `PaymentService` chỉ còn phần riêng của endpoint; sửa comment sai của `PaymentController` | `OnlinePaymentStarterTests` (5), `VnPayServiceTests` (6), `PaymentServiceTests` (10) |
| 9–12 | Thất bại giữ đơn và rút hạn. Thành công chống lặp theo Payment, email có "Đã cọc / Còn lại". Tiền về sau khi hủy hoàn 100 % (kể cả đơn đã `CANCELLED_REFUNDED`). Trả hai lần ghi nhận không đổi đơn. Khóa booking trước Payment | `PaymentResultServiceTests` (27) |
| 13 | Thanh toán lại: `ExtendPaymentWindowAsync` có điều kiện (409 khi đơn đổi), tạo link qua `OnlinePaymentStarter` cùng cổng | `PaymentServiceRetryDepositTests` (+2) |
| 14 | Check-out `max(0, finalTotal − số đã thu)`, trả thừa log Error, hóa đơn `PAID` bằng `SetPaymentStatusAsync` | `BookingServiceCheckoutTests` (6) |
| 15 | 12 transaction sang Read Committed. Check-in qua `CheckInAsync` có điều kiện. Thêm/bớt dịch vụ khóa booking bằng `TryLockForServiceChangeAsync`. Xác nhận hoàn: câu cập nhật có điều kiện, mọi Refund chờ, cộng dồn `refunded_amount`. Scope lồng tích/trừ điểm đổi cùng | `BookingServiceRefundTests` (4), `BookingStateGateRepositoryTests` (15), +2 ca thua trong `BookingServiceTests` |
| 16–18 | HTTP trên host thật: BE-IT-21, 22; BE-IT-23, 10; phần 6A-4 của BE-IT-25; test đồng thời; test thứ tự khóa | `PaymentFailureFlowTests` (8), `CancellationFlowTests` (10), `ConcurrencyRegressionTests` (8), `PaymentLockOrderTests` (1) |
| 19 | Spec 7.7, 7.8, 7.9, 7.10, 7.12, 7.13, Mục 11; API doc 3.4, 3.6, Mục 5 dòng 21; sơ đồ tuần tự 5, 6 (render bằng mermaid-cli, đạt); skill BE và `SmashCourt-BE/CLAUDE.md` | `check_docs` đạt |

**Phát hiện và xử lý trong lúc làm:**
- **Deadlock thật giữa kết quả thanh toán và lệnh hủy** (lộ ra khi chạy cả suite, không có trong plan):
  - IPN thành công khóa Payment trước rồi chờ booking. Lệnh hủy làm ngược lại: giữ booking rồi chờ Payment.
  - PostgreSQL hủy một bên (`40P01`): nhân viên nhận 500, hoặc lần ghi nhận tiền bị hủy và phải chờ VNPay gửi lại.
  - Sửa: `IBookingRepository.LockAsync` khóa booking đầu tiên, ở cả nhánh thành công lẫn thất bại, cùng thứ tự với mọi luồng khác.
  - Test: `PaymentLockOrderTests` tái hiện ổn định (đỏ trước khi sửa, xanh sau khi sửa); unit test khóa thứ tự "booking trước Payment".
- **Test cũ của 6A-3 đỏ sau 23:00 giờ VN:** `ApproveZero_ThenCheckInAndCheckout...` dời giờ chơi về `now − 5'` và cộng 1 giờ, nên giờ kết thúc vòng qua 00:xx và vi phạm `chk_booking_courts_valid_time`. Đây là lỗi dữ liệu test, không phải lỗi code. Đã sửa: giờ kết thúc không vượt 23:59.
- **Test 6A-2 ghi nhận hành vi sai cũ:** `StaffCancelsASmallDepositOrder_NoUseIsCounted` chờ `UsedCount` giảm 1 (comment ghi "sửa ở 6A-4"). Nay giữ nguyên 3. Test bên cạnh siết từ "khác `POLICY_REJECTED`" thành `STAFF_CANCELLED`.
- **Test dữ liệu sai:** test "bỏ đi sau thẻ lỗi" chỉ lùi hạn hóa đơn mà không lùi slot lock, trong khi ngoài đời hai hạn luôn bằng nhau. Đã sửa test.
- **Giữ nguyên mốc "đã qua giờ chơi" của mã cũ** (`< 0`): chính sách cho phép mốc 0 giờ. Test ghi lại hành vi này.

**Rà soát hồi quy Phase 6 (25/10)**, so với `8320f8a` (trước Phase 6). Logic cũ được giữ, có hai lỗi đã sửa kèm test:
- Hủy bằng link không trả lượt khuyến mãi (`GetByCancelTokenAsync` thiếu `Include` `BookingPromotion`). Mã cũ cũng không trả lượt ở đường này, nhưng spec 7.9 bước 5 đã chốt. BE-IT: Theory 3 đường + ca `POSTPAID` qua link, đỏ trước khi sửa.
- Email hủy hoàn 0 đ ghi "hủy quá gần giờ chơi" cả với đơn chưa trả (có từ trước). Nay phân biệt bằng `hasPaidAmount`. `CancelEmailTests` + 2 BE-IT.
- Đột biến 5/5 bị bắt (cờ luôn true/false, đếm cả Payment chờ, đảo nhánh, bỏ `Include`).

**Đối chiếu lại plan (25/10): code đủ 19 task, nhưng thiếu một số ca kiểm.** Bảng kết quả ở trên ghi ✅ trong khi 6 nhóm ca kiểm của plan chưa có test. Đã bổ sung 23 test, không đổi code production:

| Task | Ca còn thiếu | Test bổ sung |
| :---: | :--- | :--- |
| 8 | Đăng ký trùng cổng; `Program.cs` nối VNPay vào resolver | `OnlinePaymentStarterTests.Resolver_TwoGatewaysForOneMethod_…` (resolver là Scoped nên lỗi lộ ở request đầu tiên, không phải lúc khởi động); `HttpHostSmokeTests.Host_WiresVnPayAsTheOnlyPaymentGateway`. Hai ca chọn cổng và phương thức chưa đăng ký đã có sẵn |
| 12 (C1) | Trả hai lần ⇒ check-out | `CheckoutFlowTests`: trả hai lần 100 % ⇒ thu 0 + log Error; trả hai lần cọc ⇒ thu `finalTotal − 2 × cọc`. Test cũ `SecondLinkPaidAfterTheFirst_…CountedAtCheckout` không hề check-out, đổi tên thành `…_IsRecordedWithoutChangingTheBooking` |
| 14 | Tích điểm trong scope lồng; đơn tại quầy (HTTP) | Điểm = `floor(courtFee / 1000)` (biên 199.999 ⇒ 199), ba loại đơn, hai check-out đồng thời chỉ cộng một lần; tại quầy trả sau / trả ngay, `paid_at` của khoản thu là UTC trong request. `EarnLoyaltyPointsAsync` nuốt lỗi nên trước đây hỏng vẫn 200 |
| 16 | Đơn đã duyệt, thẻ lỗi | Hạn rút về ≤ 5' và ngắn hơn hạn trả cọc; trả lại đúng 60.000 đ cọc đã duyệt |
| 17 | Link sau giờ bắt đầu; % theo giờ VN (HTTP); trả hai lần rồi hủy | 400 `BOOKING_ALREADY_STARTED`, token chưa bị tiêu; Theory còn 30/20/5 giờ ⇒ 100/50/20 % (ca 20 giờ bắt lỗi lệch 7 giờ cũ); hai Refund theo Payment, xác nhận cộng dồn, email ghi tổng |
| 18 | Mốc thời gian | `paid_at` (IPN), `cancelled_at`, `created_at` và `processed_at` của Refund là UTC trong khoảng request |

Thêm ngoài plan (bất biến về tiền): tiền về sau khi đã hoàn xong (thêm Refund mới, Refund cũ giữ nguyên `COMPLETED`); xác nhận hoàn lần hai bị từ chối, không hoàn thêm; thanh toán lại sau khi khách hủy bị từ chối, không tạo link; giới hạn lượt mỗi khách với đơn **đã trả** bị hủy (`CANCELLED_PENDING_REFUND`).

**Đột biến cho các test mới: 16/16 bị bắt** (công thức check-out theo cọc, log trả thừa, bỏ 409 check-out, làm tròn điểm, scope lồng khai rõ mức cô lập khác, % hoàn theo giờ tường, bỏ chốt giờ bắt đầu ở link, xác nhận chỉ một Refund, thiếu `processed_at`, bỏ chốt trạng thái xác nhận hoàn, tiền về sau khi hoàn xong bị coi là trả thêm, P6-18 chỉ bỏ `CANCELLED`, thẻ lỗi không rút hạn, resolver chọn bừa khi trùng, quên đăng ký VNPay, thanh toán lại không chặn đơn hủy). Một đột biến tương đương bị loại: scope lồng tạo bằng `new TransactionScope(AsyncFlowOption)` không truyền `TransactionOptions` thì chỉ nhập vào transaction ngoài, không kiểm mức cô lập; .NET chỉ ném `ArgumentException` khi scope lồng **khai rõ** mức cô lập khác (bẫy thật của mục 0.2 là trường hợp này).

**Lỗi có sẵn ghi nhận, không sửa:** `DeductLoyaltyPointsAsync` ghi `loyalty_transactions.type = DEDUCT`, vi phạm `chk_loyalty_earn_booking_id` (chỉ cho `EARN`/`ADJUST`) ⇒ nếu chạy tới thì xác nhận hoàn trả 500. Luồng thật không chạm tới: điểm chỉ cộng lúc check-out, đơn đã check-out không hủy được. Ghi ở Mục 11 của spec.

**Còn lại cho gói sau:**
- Job-01 (6A-5) vẫn ghi bằng `SaveChanges` nhiều dòng, chưa theo thứ tự "khóa booking trước"; viết lại bằng dịch vụ hủy chung ở 6A-5.
- Lý do `PAYMENT_EXPIRED` / `SYSTEM_CLEANUP` của Job-01 làm ở 6A-5.
- Kiểm mức tối thiểu VNPay sandbox (mục 0.5) chờ người dùng.

## Ràng buộc chung

- Không SQL thô; câu cập nhật có điều kiện bằng `ExecuteUpdateAsync`.
- Mọi "bây giờ" của mã mới lấy từ `TimeProvider`; so sánh giờ chơi bằng `DateTimeHelper.ToUtcFromVietnam`.
- Email và SignalR luôn sau commit, lỗi không làm hỏng thao tác.
- Không đổi route, request, tên trường response; mã lỗi mới chỉ `BOOKING_ALREADY_STARTED`.
- Comment theo chuẩn của skill `smashcourt-be-architecture` (một dòng `//` cho method interface, không mã quyết định trong code).
- Test mới: luật thuần trong `Unit/Services/Cancellation/`, `Unit/Services/Deposit/`; DB thật trong `Integration/`; HTTP trong `Integration/Http/`.
- Không bật `EnableTadssDecision`; không chạm `tadss-dev`.
- Nghiệp vụ thanh toán chỉ ở `PaymentResultService` và `OnlinePaymentStarter`; code riêng của một cổng chỉ ở lớp hiện thực `IPaymentGateway`.
- Hoàn tiền chỉ tạo Refund cho chi nhánh trả tiền mặt; không gọi API hoàn của cổng; chỉ nhân viên chi nhánh của đơn (hoặc OWNER) xác nhận hoàn (đã có trong `ConfirmRefundAsync`).

## Rủi ro

| Rủi ro | Xử lý |
| :--- | :--- |
| Scope lồng khác mức cô lập ném lỗi lúc chạy (build vẫn xanh) | Đổi cặp ngoài–trong cùng task; test check-out và xác nhận hoàn chạy trên DB thật với khách có tài khoản |
| Chuyển 4 hàm hủy sang service mới làm vỡ khoảng 40 test cũ trong `BookingServiceTests` | Chuyển test theo cùng hàm sang `BookingCancellationServiceTests`; chỉ sửa chỗ khác hành vi đúng theo spec, ghi lý do |
| Mức tối thiểu VNPay khác 5.000 đ | Người dùng kiểm sandbox (mục 0.5); chỉ đổi cấu hình |
| Một số khách hủy sát mốc được hoàn ít hơn trước (sửa T-1) | Đúng chính sách đã công bố; API doc 3.4 đã ghi |

## Thứ tự và ước lượng

| Phần | Task | Ước lượng |
| :--- | :--- | :---: |
| A | 1–3 | 0,5 ngày |
| B | 4–7 | 1,5 ngày |
| C | 8–13 | 2 ngày |
| D | 14–15 | 1 ngày |
| E | 16–19 | 1,5 ngày |

Phụ thuộc: Phần A trước mọi phần; Task 4 trước 5–7; Task 8 trước 9–13; Task 10 trước 11–12; Task 14 trước 15.
