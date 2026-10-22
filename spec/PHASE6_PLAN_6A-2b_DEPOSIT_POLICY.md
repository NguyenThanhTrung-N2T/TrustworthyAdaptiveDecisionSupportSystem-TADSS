# Gói 6A-2b — Hạn giữ chỗ, chọn nhánh cọc, cập nhật của transaction 2

**Mục tiêu:** ba khối nền cho luồng đặt sân, mỗi khối test độc lập: hàm thuần tính hạn giữ chỗ (7.5), hàm thuần chọn nhánh từ kết quả TADSS (7.6), và các cập nhật có điều kiện trạng thái mà transaction 2 dùng. Chưa nối vào `CreateOnlineAsync` (6A-2c). Tổng quan: [6A-2](PHASE6_PLAN_6A-2_ONLINE_BOOKING.md).

**Spec:** 4.3, 7.5, 7.6 (bảng 5 nhánh), 7.14, P6-02, P6-06, P6-11, P6-14. **Test:** BE-UT mới cho hai hàm thuần; integration trên schema thật cho repository.

---

### Task 1 — `PaymentHoldHelper` (7.5) ✅ xong 22/10 (A7: ở 6A-2 chỉ dòng "đặt sân")

**File:** `Helpers/PaymentHoldHelper.cs` (mới); test `SmashCourt-BE.Tests/Helpers/PaymentHoldHelperTests.cs`.

**Giải pháp:** hàm thuần nhận `now` (UTC) + settings làm tham số. **A7 (chốt 22/10): ở 6A-2 chỉ làm dòng "đặt sân"** (`ForNewBooking(now, BookingPaymentSettings)` trả `PaymentHoldWindow(ExpiresAt = now + InitialHoldMinutes, HoldDeadline = now + PaymentHoldMaxMinutes)`, `Kind = Utc`, đầu vào khác Utc ⇒ ném lỗi). Mỗi dòng là một hàm tĩnh riêng nên thêm dòng sau không đổi chữ ký dòng trước. Ba dòng còn lại làm ở gói dùng chúng, **đề xuất (chưa chốt, xem lại khi làm):** dòng duyệt (6A-3) `TryForApproval(now, playStartUtc, int approvedWindowMinutes, out window)` nhận một `playStartUtc` (caller lấy booking_court đầu theo ngày rồi giờ như Job-04, `ToUtcFromVietnam`; đơn online luôn một ngày và một giờ chung) và nhận số phút nguyên thay vì `TadssSettings`; dòng thanh toán lại (6A-4) `TryForRetry(now, currentExpiresAt, holdDeadline, settings, out window)`; cả hai trả `false` khi quá hạn tuyệt đối để caller buộc phải trả 400 `PAYMENT_HOLD_EXPIRED` (không trả mốc đã qua, tránh ghi hạn quá khứ rồi Job-01 hủy `PAYMENT_EXPIRED` và cộng oan vào lịch sử khách); dòng IPN thất bại (6A-4) `AfterPaymentFailure(currentExpiresAt, now, settings)` luôn thành công; `hold_deadline = NULL` (đơn trước deploy) đổi bằng hàm nhỏ `created_at + PaymentHoldMaxMinutes` ở 6A-4. Phần dưới đây mô tả đủ 4 dòng để giữ thiết kế chung.

**Test (6A-2 chỉ dòng "đặt sân": giá trị đúng, biên cấu hình tối thiểu, `Kind = Utc`, đầu vào Local/Unspecified ném lỗi; các ý còn lại làm cùng dòng tương ứng ở 6A-3, 6A-4):** từng dòng của bảng; "không vượt giờ chơi" khi đơn sắp tới giờ; IPN thất bại chỉ rút ngắn; thanh toán lại không vượt hạn tuyệt đối, quá hạn tuyệt đối báo được cho caller (để 6A-4 trả `PAYMENT_HOLD_EXPIRED`); đầu vào `Kind` khác Utc ⇒ ném lỗi.

### Task 2 — Chọn nhánh cọc (thuần) và `DecideAsync` (D2) ✅ xong 22/10

**File:** `Helpers/DepositDecisionResolver.cs`, `Models/ViewModels/DepositDecision.cs` (mới); `Services/IService/IBookingDepositService.cs`, `Services/BookingDepositService.cs` (thêm `DecideAsync`); test `SmashCourt-BE.Tests/Helpers/DepositDecisionResolverTests.cs`, bổ sung `Services/BookingDepositServiceTests.cs`.

**Giải pháp:**
- Hàm thuần nhận: cờ, kết quả gọi TADSS (hoặc "không gọi"), `finalTotal`, `MinOnlinePaymentAmount` ⇒ quyết định: nhánh (a/a′/b/c/d), `deposit_rate`, `deposit_amount`, `deposit_source`, `tadss_decision_id`, thông tin duyệt (`approval_expires_at` đã đổi `.UtcDateTime`, mã và lý do cần duyệt), và **mức log** (Warning cho a′, Error cho 400/409 bất thường, Warning cho lỗi TADSS khác).
- Thứ tự đúng spec 7.6 (D5): (1) cờ tắt và (2) mọi lỗi (`Timeout`, `Network`, `Http`, `InvalidResponse`) ⇒ chính sách `FULL_PAYMENT_FALLBACK`, `A_online = finalTotal`; (3) `REJECTED` ⇒ c, `NEED_APPROVAL` ⇒ b, **không xét tiền**; (4) `APPROVED` ⇒ chính sách `TADSS`, `A_online = deposit.amount`; (5) `A_online < MinOnlinePaymentAmount` ⇒ a′ (cọc áp dụng 0, nguồn giữ theo chính sách), ngược lại a (`TADSS`) hoặc d (`FULL_PAYMENT_FALLBACK`) với số tiền VNPay = `A_online`. Không có nhánh riêng cho `finalTotal = 0`. Fallback của runtime TADSS (`fallbackUsed = true`) và `APPROVAL_WINDOW_UNAVAILABLE` là `TADSS`, không phải d. Lỗi kỹ thuật không bao giờ mang `decisionId`.
- Kết quả trả về mang `A_online` để log Warning của a′ ghi đúng số đã bỏ qua.
- `DecideAsync(bookingId, finalTotal, ct)`: đọc cờ qua `IOptionsMonitor` **mỗi lần gọi**; cờ tắt ⇒ không gọi TADSS (WireMock nhận 0 request); cờ bật ⇒ `EvaluateAsync` rồi hàm thuần. Không ghi DB.

**Test:** mỗi dòng bảng 7.6 và mỗi `TadssFailureKind`; 409 `IDEMPOTENCY_KEY_REUSE` và 400 ⇒ d với log Error; `approval_expires_at` giữ đúng thời điểm (offset +07:00 vào ⇒ UTC ra); service không gọi client khi cờ tắt; **K-06**: token đã hủy truyền thẳng vào `DecideAsync` ⇒ ném `OperationCanceledException` (giữ hợp đồng 6A-1 cho caller khác; `CreateOnlineAsync` truyền `None` theo D6); cùng bộ ca Z dưới đây. Ranh giới 4.999/5.000 kiểm cho **cả** tiền cọc lẫn tổng tiền fallback (cấu hình `MinOnlinePaymentAmount = 5000`; thêm một ca với giá trị cấu hình khác để chứng minh không hằng cứng).

| # | Ca | Kỳ vọng |
| :---: | :--- | :--- |
| Z-01 | `APPROVED`, `finalTotal` 200.000, cọc 4.999 | a′; cọc áp dụng 0; nguồn `TADSS`; `A_online = 4.999` trong kết quả để log |
| Z-02 | `APPROVED`, cọc 5.000 | a, VNPay = 5.000 |
| Z-03 | `APPROVED`, đơn 0 đ (cọc 0) | a′, nguồn `TADSS` |
| Z-04 | Cờ tắt, `finalTotal` 0 / 4.999 / 5.000 | a′ / a′ / d (VNPay = 5.000), nguồn `FULL_PAYMENT_FALLBACK`; client không được gọi |
| Z-05 | Từng `TadssFailureKind`, `finalTotal` 4.999 / 5.000 | a′ / d, nguồn `FULL_PAYMENT_FALLBACK`, không `decisionId` |
| Z-06 | `NEED_APPROVAL`, đơn 0 đ và 4.999 | b; không xét tiền |
| Z-07 | `REJECTED`, đơn 0 đ và 4.999 | c; không xét tiền |
| Z-08 | Fallback runtime TADSS (`fallbackUsed`, cọc 100 %) và `APPROVAL_WINDOW_UNAVAILABLE`, `finalTotal` 4.999 / 5.000 | a′ / a, nguồn `TADSS` |
| Z-09 | Mọi tổ hợp {`REJECTED`, `NEED_APPROVAL`} × `finalTotal` {0; 4.999; 5.000; 2.000.000} | Không tổ hợp nào ra a′ hay `CONFIRMED` |

**Kết quả thực hiện Task 2 (22/10/2026):** `Helpers/DepositDecisionResolver` (hàm thuần, 5 bước của spec 7.6), `Models/ViewModels/DepositDecision`, `Models/Enums/DepositBranch` (nội bộ, không lưu DB); `IBookingDepositService.DecideAsync(bookingId, finalTotal, ct)` đọc `EnableTadssDecision` và `MinOnlinePaymentAmount` qua `IOptionsMonitor` mỗi lần gọi, ghi log theo mức quyết định đề xuất, không ghi DB. Constructor `BookingDepositService` nhận thêm `IOptionsMonitor<BookingPaymentSettings>` và `ILogger`. Test: `DepositDecisionResolverTests` (62 ca gồm Z-01…Z-09), `BookingDepositServiceTests` (+15 ca `DecideAsync` gồm K-06); 14 đột biến đều bị bắt.

### Task 3 — Cập nhật có điều kiện của transaction 2 ✅ xong 22/10

**File:** `Repositories/IRepository/IBookingRepository.cs`, `Repositories/BookingRepository.cs` (và repo hóa đơn/slot lock nếu cập nhật nằm ở đó, theo hàng xóm); test integration `SmashCourt-BE.Tests/Integration/BookingDepositUpdateTests.cs`.

**Giải pháp:** mỗi nhánh mở đầu bằng **một lệnh cổng** có điều kiện `status = PENDING` trên dòng booking (P6-14; khóa dòng tới khi commit), trả số dòng; 0 ⇒ caller dừng, không ghi gì thêm. Các lệnh còn lại của nhánh chỉ chạy khi cổng thắng, cùng transaction. Chỉ dùng LINQ `ExecuteUpdateAsync`/`ExecuteDeleteAsync` (không raw SQL, không concurrency token — D1):
- a, d: cổng ghi `tadss_decision_id` (d: không có, cổng chỉ ghi `updated_at`); hóa đơn ghi `deposit_*`;
- a′ (D3; D8 thêm token hủy vào cùng câu cổng ở Task 4): cổng booking `CONFIRMED`; hóa đơn cọc 0, `expires_at = NULL`, `payment_timing = POSTPAID`; xóa slot lock; có khuyến mãi ⇒ `IncrementUsageCountAsync` trong cùng transaction;
- b: cổng booking `PENDING_APPROVAL` + 4 trường duyệt; hóa đơn `expires_at = NULL`, `deposit_* = NULL`; slot lock gia hạn tới `approval_expires_at`;
- c (D1): cổng là **lõi hủy dùng chung** `CancelWithStatusCheckAsync([PENDING] → CANCELLED, SYSTEM, POLICY_REJECTED, cancelledBy = null, now)`; rồi ghi `tadss_decision_id` (lệnh riêng); `UpdateCourtActiveStatusAsync(false)` + `SlotLockRepository.DeleteByBookingIdAsync` (có sẵn); hóa đơn `UNPAID → EXPIRED`. Lõi hủy không chứa logic hóa đơn, Payment, khuyến mãi, sân để 6A-3/6A-4/6A-5 dùng lại.
- `ExecuteUpdateAsync` bỏ qua change tracker: sau transaction 2 phải đọc lại booking/hóa đơn (`AsNoTracking`) hoặc `ClearChangeTracker()` trước khi dựng response; `updated_at` tự gán.
- Ràng buộc DB (`chk_bookings_pending_approval`, `chk_invoices_deposit_fields`, `chk_bookings_cancel_reason`, `chk_bookings_cancelled_at`…) là lưới an toàn cuối: test chứng minh mỗi hàm thỏa chúng.

**Test (schema thật):** mỗi nhánh ghi đúng cột; lõi hủy ghi đủ cột trong một lệnh (thỏa `chk_bookings_cancelled_at`, `chk_bookings_cancel_reason`), nhận nhiều trạng thái nguồn; booking không còn `PENDING` ⇒ 0 dòng, không đổi gì; nhánh c giải phóng slot (đơn khác đặt lại cùng giờ được); b giữ slot tới `approval_expires_at`; dữ liệu đọc lại sau transaction 2 đúng trạng thái mới.

**Hạn giữ chỗ qua transaction 2 (A6, chốt 21/10):** transaction 2 **không** đụng `hold_deadline` ở nhánh nào (giữ giá trị transaction 1; `NULL` vốn được hiểu là `created_at + PaymentHoldMaxMinutes` nên đặt `NULL` cho cùng hành vi mà làm nhầm với "đơn trước deploy"). 6A-3 bắt buộc ghi đè khi duyệt > 0 % (spec 7.7, test H-03).

| # | Ca | Kỳ vọng |
| :---: | :--- | :--- |
| H-01 | Mỗi nhánh a/a′/b/c/d: `hold_deadline` trước và sau transaction 2 | Không đổi; a/d: `expires_at = created + 10'`, `hold_deadline = created + 15'`, slot lock hết hạn cùng `expires_at` |
| H-02 | Nhánh b | `expires_at = NULL`, slot lock hết hạn đúng `approval_expires_at`, `hold_deadline` giữ nguyên |

**Test lượt khuyến mãi của a′ (D3):** hàm repository của a′ chỉ lo booking, hóa đơn, slot lock; **tăng `UsedCount` do `BookingService` gọi `PromotionEngineService.IncrementUsageCountAsync`** sau khi hàm trả `true` (khuyến mãi thuộc `PromotionRepository`, không để `BookingRepository` chứa nghiệp vụ). Vì vậy:

| # | Ca | Mức, gói |
| :---: | :--- | :--- |
| P-03 | Cổng thua ⇒ hàm trả `false`, hóa đơn/slot lock/sân giữ nguyên | Integration, **2b Task 3 (xong)** |
| P-01 | a′ có khuyến mãi ⇒ `IncrementUsageCountAsync` gọi đúng một lần với khuyến mãi của đơn | Unit Moq, 2c Task 1 |
| P-02 | a′ không khuyến mãi ⇒ không gọi | Unit Moq, 2c Task 1 |
| P-04 | Lỗi sau khi tăng lượt ⇒ rollback cả lượt khuyến mãi, booking vẫn `PENDING`, hóa đơn vẫn `PREPAID` | Integration qua HTTP, 2c Task 4 (cần ép lỗi giữa transaction) |
| P-05 | `UsedCount = 0`, `UsageLimit = null` ⇒ +1, không lỗi | HTTP, 2c Task 4 (gộp P-10…P-12) |

**Kết quả thực hiện Task 3 (22/10/2026):** `IBookingRepository`/`BookingRepository` có lõi hủy `CancelWithStatusCheckAsync` và 4 hàm theo nhánh `ApplyPaymentRequiredAsync` (a, d), `ConfirmWithoutOnlinePaymentAsync` (a′), `HoldForApprovalAsync` (b), `RejectByPolicyAsync` (c). Mỗi hàm: câu cổng `status = PENDING` rồi các lệnh phụ của chính đơn (hóa đơn, slot lock, sân), trả `bool`; thắng cổng mà hóa đơn không cập nhật được đúng một dòng ⇒ `InvalidOperationException` (dữ liệu hỏng, caller rollback). Hàm phải chạy trong transaction của caller và **tự kiểm**: không có transaction nào (không `BeginTransaction` của EF, không `TransactionScope`) ⇒ `InvalidOperationException` trước khi ghi gì. Nhánh a/d gọi lần hai vẫn thắng cổng (trạng thái không đổi), caller không được gọi lại để thử lại. Test: `BookingCancelWithStatusCheckTests` (26), `BookingDepositUpdateTests` (42, gồm H-01, H-02 và 8 ca chốt chặn transaction); 17 đột biến đều bị bắt. Đã thử một lần với hai kết nối thật chạy đồng thời (đạt).


### Task 4 — `CancelTokenHelper`: token hủy cho mọi đường phát token, sửa T-2, T-3 (D8) ✅ xong 22/10

**Vì sao:** đơn online `CONFIRMED` nào cũng phải có email xác nhận kèm link hủy như đơn qua IPN (D8). Đơn a′ và duyệt 0 % không có IPN nên token phải sinh ngay trong transaction ghi `CONFIRMED`.

**File:** `Helpers/CancelTokenHelper.cs` (mới); sửa `Repositories/IRepository/IBookingRepository.cs`, `Repositories/BookingRepository.cs` (`ConfirmWithoutOnlinePaymentAsync` thêm `cancelTokenHash`, `cancelTokenExpiresAtUtc`); test `SmashCourt-BE.Tests/Helpers/CancelTokenHelperTests.cs`, cập nhật các ca a′ trong `Integration/BookingDepositUpdateTests.cs`.

**Giải pháp:**
- `CancelTokenHelper.Generate(DateTime playStartUtc, DateTime nowUtc)` hàm thuần, trả `(RawToken, TokenHash, ExpiresAtUtc)`: token ngẫu nhiên và băm **cùng thuật toán** với `GenerateCancelToken`/`HashToken` hiện có (hai bản sao private ở `PaymentService`, `BookingService`; lookup `GetByCancelTokenAsync` phải khớp), `ExpiresAtUtc = min(playStartUtc, nowUtc + 24h)`, `Kind = Utc`, đầu vào khác Utc ⇒ ném lỗi. Đây chính là "hàm chung" mà spec 7.9 đã định cho 6A-4 (T-2, T-3), kéo về làm trước.
- `ConfirmWithoutOnlinePaymentAsync` ghi `cancel_token_hash`, `cancel_token_expires_at` **trong chính câu cổng** (cùng lệnh với `CONFIRMED`); thua cổng ⇒ không ghi gì. Hash rỗng ⇒ ném lỗi trước khi ghi.
- **Cả ba đường phát token dùng cùng hàm ngay ở 6A-2** (bạn chốt 22/10, để không còn hai công thức hạn): (1) IPN thành công `PaymentService.GenerateCancelTokenData` (sửa T-2: hiện `SpecifyKind(giờ VN, Utc)` ⇒ link sống tới 7 giờ sau giờ chơi); (2) đơn tại quầy `BookingService.SendConfirmationEmailAsync` (sửa T-3: `min` giữa giá trị `Unspecified` và `UtcNow + 24h` bỏ qua `Kind`, Npgsql ném lỗi, `catch` nuốt nên email không gửi); (3) a′ (và duyệt 0 % ở 6A-3). Hai bản `GenerateCancelToken`/`HashToken` private (giống hệt nhau) ở `PaymentService` và `BookingService` bị xóa; mọi nơi sinh **và tra cứu** (`GetCancelInfoAsync`, `CancelByTokenAsync`) gọi một hàm băm duy nhất để không lệch.
- Giờ chơi đưa vào helper luôn là `playStartUtc` do caller tính bằng `DateTimeHelper.ToUtcFromVietnam(ngày, giờ bắt đầu)`; helper không tự suy ra múi giờ. Đồng hồ của đường cũ giữ nguyên (`DateTimeHelper.GetUtcNow()`), chỉ truyền `now` vào helper.
- Token đã phát trước thay đổi giữ hạn cũ trong DB (không backfill); từ 6A-4, P6-23 chặn hủy sau giờ bắt đầu là lớp thứ hai. % hoàn tiền (T-1) không thuộc task này.
- Duyệt 0 % (6A-3) dùng cùng helper và cùng cách ghi trong câu cổng của hàm duyệt.
- Raw token chỉ nằm trong bộ nhớ của request để đưa vào email sau commit; email lỗi thì không phục hồi được token (như đường IPN hiện tại), khách vẫn hủy được trong app.

**Test:** hash khác raw, băm khớp thuật toán của `GetByCancelTokenAsync` (tra ngược ra đúng đơn); hạn = `min(giờ bắt đầu, now + 24h)` ở cả hai phía biên (đơn trong 24 giờ tới, ngoài 24 giờ, đúng 24 giờ), qua nửa đêm giờ VN; `Kind = Utc`; Local/Unspecified ném lỗi; token duy nhất qua nhiều lần gọi; repository a′ ghi đúng hash và hạn, thua cổng thì hai cột còn `NULL`, hash rỗng ném lỗi và không ghi gì; các nhánh a, b, c, d **không** ghi token.

**Test bổ sung cho T-2, T-3 (kiểm kỹ giờ):** bảng biên của `ComputeExpiry` bằng giờ cố định, mỗi dòng so với giá trị tính tay: đặt 10:00 VN cho 19:00 VN cùng ngày (hạn = 19:00 VN = 12:00 UTC); đặt 10:00 VN cho 19:00 VN ngày kia (hạn = now + 24h); đúng 24 giờ trước giờ bắt đầu; 23:59 và 00:00 VN; đơn lúc 06:59 VN cho giờ chơi 06:00 hôm sau; hạn không bao giờ sau giờ bắt đầu và không bao giờ quá now + 24h (kiểm bằng thuộc tính trên nhiều cặp ngẫu nhiên có hạt cố định). Đường IPN (Moq): `AtomicUpdatePaymentSuccessAsync` nhận hạn đúng bằng helper, `Kind = Utc`, không quá giờ bắt đầu theo UTC (ca đang sai 7 giờ trước khi sửa: viết test đỏ trước). Đường đơn tại quầy trên schema thật (BE-IT-25): tạo đơn trong ngày cho khách có email ⇒ `cancel_token_expires_at` ghi được, `Kind = Utc`, bằng giờ bắt đầu theo UTC, email được gửi (trước khi sửa: không gửi). Khứ hồi: token sinh ở từng đường (IPN, tại quầy, a′) đều tra ra đúng đơn bằng `GetCancelInfoAsync` và hủy được bằng `CancelByTokenAsync`; sau giờ bắt đầu thì báo hết hạn.

**Kết quả thực hiện Task 1 và Task 4 (22/10/2026):**
- Task 1: `Helpers/PaymentHoldHelper.ForNewBooking(nowUtc, BookingPaymentSettings)` trả `Models/ViewModels/PaymentHoldWindow`; tự kiểm `Kind = Utc`, `InitialHoldMinutes ≥ 1`, `PaymentHoldMaxMinutes ≥ InitialHoldMinutes`. Chưa có nơi gọi (2c Task 1 thay hằng 10 phút). 17 test (`PaymentHoldHelperTests`).
- Task 4: `Helpers/CancelTokenHelper` (`Generate`, `ComputeExpiry`, `Hash`) + `Models/ViewModels/CancelTokenData`. Ba đường dùng chung: IPN (`PaymentService.GenerateCancelTokenData`, slot sớm nhất theo ngày rồi giờ), đơn tại quầy (`BookingService.SendConfirmationEmailAsync`), a′ (`ConfirmWithoutOnlinePaymentAsync` thêm `cancelTokenHash`, `cancelTokenExpiresAtUtc`, ghi trong câu cổng). Tra cứu (`GetCancelInfoAsync`, `CancelByTokenAsync`) băm bằng `CancelTokenHelper.Hash`. Xóa hai bản `GenerateCancelToken`/`HashToken` trùng nhau.
- T-2 xác nhận bằng test đỏ trước khi sửa: hạn IPN muộn đúng 7 giờ (mong đợi 17:51Z, thực tế 00:51Z hôm sau). T-3 xác nhận trên schema thật: ghi hạn `Kind = Unspecified` bị Npgsql từ chối (`CancelTokenPersistenceTests`, BE-IT-25 phần lưu token); test đỏ trước khi sửa: hạn đơn tại quầy `Unspecified`.
- Test mới: 21 `CancelTokenHelperTests` (bảng biên tính tay, 2.000 cặp ngẫu nhiên hạt cố định, vector SHA-256 chuẩn, 1.000 token khác nhau), 3 IPN (`PaymentServiceTests`), 4 `BookingServiceTests` (đơn tại quầy trong/ngoài 24 giờ, tra cứu và tiêu token bằng hash chung), 13 `BookingDepositUpdateTests` (a′ ghi token, thua cổng không ghi, hash rỗng/hạn khác Utc ném và không đổi gì, khứ hồi tra cứu/tiêu một lần, nhánh a/d, b, c không ghi token; `CaptureAsync` chụp thêm hai cột token), 3 `CancelTokenPersistenceTests`.
- Kết quả: build 0 cảnh báo; **710/710 test xanh** (649 + 61); **14/14 đột biến bị bắt** (min/max, 24 → 25 giờ, bỏ chữ thường hash, bỏ kiểm Utc ở helper/repository, IPN và tại quầy quay về công thức lệch 7 giờ, tra cứu không băm, a′ không ghi hash/hạn, bỏ kiểm hash rỗng, `PaymentHoldHelper` dùng nhầm Max, bỏ kiểm Max ≥ Initial, bỏ kiểm Utc).
- Thay đổi hành vi production: link hủy của đơn thanh toán VNPay hết hạn đúng giờ chơi (trước đây sống thêm 7 giờ); đơn tại quầy trong ngày nay ghi được token và gửi được email xác nhận. Token đã phát trước giữ hạn cũ.
