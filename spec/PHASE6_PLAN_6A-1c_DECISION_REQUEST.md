# Gói 6A-1c — Lịch sử khách và dựng request quyết định (T6.3)

**Mục tiêu:** từ một `bookingId` vừa ghi ở transaction 1, BE dựng được request `POST /api/v2/decisions` đúng nghĩa (giá, hạng, lịch sử theo `cancel_reason`, thời gian có offset) và gọi client. **Không** rẽ nhánh kết quả (đó là 6A-2). Tổng quan: [6A-1](PHASE6_PLAN_6A-1_FOUNDATION.md). Cần 6A-1a (entity, fixture) và 6A-1b (DTO, client).

**Spec:** 4.4.1 (lý do hủy ⇒ lịch sử), 7.4, 7.14; INTEGRATION_SPEC §4.1–4.2; quy tắc Phase 5 trong `TADSS-Runtime/tools/evidence/sql/export_smashcourt_bookings.sql`. **Test:** BE-UT-02, BE-UT-09 (phần lịch sử), BE-UT-12 (phần request).

**Cách chia:** đọc DB (repository) tách khỏi tính toán (hàm thuần) ⇒ quy tắc nghiệp vụ test bằng unit test nhanh, câu truy vấn test bằng integration trên schema thật.

**Ngoài phạm vi (cố ý):** khối `demand` (tùy chọn trong contract; strategy 2.1.0 không dùng) ⇒ không gửi. Phần outcome của BE-UT-09 làm ở 6A-5.

---

### Task 1 — Lịch sử khách (`history`)

**File:** `Repositories/IRepository/ICustomerHistoryRepository.cs`, `Repositories/CustomerHistoryRepository.cs`, `Helpers/CustomerHistoryCalculator.cs`, `Models/ViewModels/CustomerHistoryEntry.cs` (dữ liệu thô) và `CustomerHistorySummary.cs` (kết quả đếm, để mapper ở Task 3 đổi sang DTO) (mới); test unit `SmashCourt-BE.Tests/Helpers/CustomerHistoryCalculatorTests.cs`, integration `SmashCourt-BE.Tests/Integration/CustomerHistoryRepositoryTests.cs`; mở rộng `Integration/TestDataSeeder.cs` (booking_courts, time_slots + booking_price_items, invoices, payments, refunds, loyalty_tiers, customer_loyalty: schema thật không có dữ liệu mẫu và có nhiều khóa ngoại).

**Giải pháp:**
- Repository trả **dữ liệu thô** của các booking cùng `customer_id`, trừ booking đang xét: trạng thái, `cancel_reason`, ngày + giờ kết thúc muộn nhất, `cancelled_at`, có Payment `SUCCESS`/`REFUNDED` hay không, `refund_percent` lớn nhất. Một truy vấn `AsNoTracking`, chỉ lấy cột cần.
- Calculator thuần đếm theo bảng 4.4.1:
  - `completedCount` + `lastCompletedAt` (`COMPLETED`, mốc = giờ kết thúc chơi muộn nhất, giờ tường VN);
  - `noShowCount` (`NO_SHOW`);
  - `CUSTOMER_CANCELLED` ⇒ `lateCancelledCount` nếu đã thanh toán và `refund_percent` lớn nhất < 100, ngược lại `cancelledCount` (đúng quy tắc Phase 5);
  - `paymentExpiredCount` chỉ `PAYMENT_EXPIRED` (P6-05);
  - `APPROVAL_EXPIRED`, `APPROVAL_REJECTED`, `POLICY_REJECTED`, `STAFF_CANCELLED`, `SYSTEM_CLEANUP` không tính.
- "Kết thúc **trước** booking đang xét" (so **nghiêm ngặt** `<`, đúng `export_smashcourt_bookings.sql` để số liệu Phase 5 dựng lại không đổi): đơn hoàn tất/không đến xét `ToUtcFromVietnam(ngày, giờ kết thúc) < requestedAt`; đơn hủy xét `cancelled_at < requestedAt` (`cancelled_at` NULL ⇒ không tính). Đơn đang mở không tính. Mọi sân của đơn tính, kể cả `is_active = false` (như bản export).
- Đơn hủy có `cancel_reason` NULL (hủy bằng mã chưa ghi lý do, 6A-4 mới ghi ở mọi đường hủy) **không tính** vào ô nào. Hợp lệ vì lịch sử chỉ được dùng khi bật cờ, sau 6A-4; trước khi bật cờ phải kiểm không còn đơn hủy nào có `cancel_reason` NULL (thêm vào spec 10.2).
- Bất biến: request là hàm của trạng thái tại `created_at` của booking, không phụ thuộc "bây giờ" (idempotency của TADSS so `request_hash` gồm cả `history`; gọi lại cùng `bookingId` với nội dung khác ⇒ 409 `IDEMPOTENCY_KEY_REUSE`).

**Test:** BE-UT-09: mỗi `cancel_reason` vào đúng ô hoặc bị bỏ (kể cả NULL); ranh giới late/không late (99 % / 100 %, chưa thanh toán); đơn kết thúc **đúng bằng** `requestedAt` hoặc sau đó không tính; qua nửa đêm: đơn kết thúc 23:59 giờ VN ngày D được tính cho đơn tạo 00:05 ngày D+1 (cùng ngày UTC), đơn kết thúc 00:15 ngày D+1 thì không. Integration: dữ liệu mẫu nhiều trạng thái trên schema thật ⇒ repository trả đúng hàng (gồm đơn backfill có `cancel_reason`).

### Task 2 — Hạng thành viên và dữ liệu booking cho request

**File:** `Repositories/IRepository/IBookingRepository.cs`, `Repositories/BookingRepository.cs` (thêm `GetDecisionSnapshotAsync`), `Models/ViewModels/BookingDecisionSnapshot.cs` và `BookingDecisionCourt.cs` (mới); test integration `Integration/BookingDecisionSnapshotTests.cs` và `BookingDecisionSnapshotPhase5EquivalenceTests.cs`.

**Giải pháp:**
- Một hàm đọc `AsNoTracking` gom mọi thứ request cần: booking (`created_at`, chi nhánh, khách), từng `booking_court` đang hoạt động (sân, loại sân, ngày, giờ) với giá trước giảm = tổng `booking_price_items.unit_price` của dòng đó, hóa đơn (`final_total`; thiếu hóa đơn ⇒ lỗi lập trình), mã khuyến mãi lấy từ `PromotionCodeSnapshot` (có thể NULL), `users.created_at`, `MinPoints` hạng hiện tại của khách (`customer_loyalty` duy nhất theo user, `loyalty_tiers.min_points` duy nhất nên thứ hạng không bị hòa).
- Repository trả `TierRank` (số nguyên 1…n hoặc NULL) = số hạng có `min_points` ≤ hạng của khách, đúng cột `tier_rank` của bản export; việc đổi thứ hạng sang `STANDARD`…`DIAMOND` do mapper (Task 3). Trả NULL cả snapshot khi booking không tồn tại, không có khách hoặc không có hóa đơn (service ném lỗi lập trình). Dòng sân sắp theo (ngày, giờ bắt đầu, sân) để request tất định; không còn dòng hoạt động nào thì lấy tất cả như bản export.
- Hạng ⇒ thứ hạng theo `MinPoints` tăng dần của toàn bộ `loyalty_tiers` (bảng nhỏ): hạng 1…5 ⇒ `STANDARD`…`DIAMOND`, từ hạng 6 gộp `DIAMOND`, chưa có hạng ⇒ `STANDARD`. Không dựa vào tên hạng.

**Test:** gộp vào integration của Task 1 (cùng dữ liệu mẫu): booking 2 sân đọc đủ 2 item, giá đúng tổng từng sân; khách có/không có hạng.

### Task 3 — Hàm dựng request (thuần)

**File:** `Helpers/TadssDecisionRequestMapper.cs` (mới; `Map(snapshot, history, settings)` và `MapTier(rank)`); test `SmashCourt-BE.Tests/Helpers/TadssDecisionRequestMapperTests.cs`.

**Giải pháp:** nhận snapshot (Task 2) + history (Task 1) + settings ⇒ DTO request (6A-1b):
- Mỗi `booking_court` một item: `resourceId = courtId`, `resourceTypeId = courtTypeId`, `price` trước giảm; `startTime`/`endTime`: giờ tường lưu trong DB luôn là giờ VN, nên thời điểm tuyệt đối tính bằng `DateTimeHelper.ToUtcFromVietnam` rồi mới biểu diễn bằng offset của `BranchTimeZone` tại ngày đó (`+07:00` với Asia/Ho_Chi_Minh). Không tự ghép offset từ chuỗi giờ. Dựng `DateTimeOffset` từ mốc UTC (`new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(...)`), **không** dùng `new DateTimeOffset(DateTime)` với `Kind` khác Utc (lấy offset của múi giờ máy).
- `totalPrice = finalTotal`, `currency = VND`, `channel = ONLINE`, `branchTimeZone` từ settings, `requestedAt = bookings.created_at` (UTC, offset `+00:00`).
- `customer`: `isGuest = false`, `customerId`, `tier`, `memberSince = users.created_at`; `history` từ Task 1 (`lastCompletedAt` offset `+07:00`); `metadata.promotionCode` khi có.

**Test:** BE-UT-02: nhiều sân, giá trước giảm, hạng theo thứ hạng (gồm hạng thứ 6 và chưa có hạng), `isGuest = false`; request hợp lệ `decision-request.schema.json` (bộ kiểm schema của 6A-1b). BE-UT-12 (phần request): mọi trường thời gian có offset; booking tạo lúc 00:30, 06:59, 23:45, 23:55 giờ VN ⇒ `requestedAt` đúng thời điểm UTC (23:55 VN là 16:55Z cùng ngày); giờ chơi 00:15 ngày hôm sau ⇒ item `…T00:15:00+07:00` đúng ngày VN. Tính tất định: dựng hai lần từ cùng đầu vào cho đúng cùng một chuỗi JSON.

### Task 4 — `BookingDepositService.EvaluateAsync`

**File:** `Services/IService/IBookingDepositService.cs`, `Services/BookingDepositService.cs` (mới), `Program.cs`; test `SmashCourt-BE.Tests/Services/BookingDepositServiceTests.cs`.

**Giải pháp:** `EvaluateAsync(Guid bookingId, CancellationToken)`: đọc snapshot + history ⇒ mapper ⇒ `ITadssClient.EvaluateAsync` với idempotency key = `bookingId`; trả nguyên `TadssCallResult` cho caller (6A-2 quyết nhánh). Đọc DB xong mới gọi HTTP (không giữ kết nối DB trong lúc chờ TADSS). Caller **không** gọi lại cho cùng booking sau khi đã có kết quả hoặc đã lỗi (hạng khách và dữ liệu có thể đã đổi ⇒ 409); 6A-2 xử lý 409 như lỗi TADSS (cọc 100 %). Không ghi DB, không đọc cờ (caller kiểm cờ). Booking không tồn tại hoặc không có khách ⇒ lỗi lập trình (exception), vì caller luôn gọi ngay sau transaction 1 của đơn online đã đăng nhập.

**Test:** repository và client giả: gọi client đúng một lần với request từ mapper và key = `bookingId`; kết quả thất bại của client trả nguyên, không nuốt.

**Xong khi:** BE khởi động được (DI đủ); cả bốn task xanh.

---

**Tiến độ: 6A-1c XONG 21/10/2026** (4/4 task, 501 test xanh, 0 cảnh báo; BE khởi động với `tadss-dev` không lỗi DI). Task 4: `BookingDepositService.EvaluateAsync(bookingId, ct)` có unit test (Moq) và integration test trên repository thật; đọc DB xong mới gọi TADSS, kết quả lỗi của client trả nguyên. Task 3: request do mapper dựng đã gửi tới TADSS thật (APPROVED, hạng 3 ⇒ GOLD ⇒ cọc 30 %), đọc lại bản TADSS lưu thì `requestedAt`, giờ chơi (cả 00:15 sáng hôm sau), `memberSince`, `lastCompletedAt`, tổng tiền, metadata đều đúng thời điểm và offset; bộ unit test chạy trên Linux với TZ UTC, America/Los_Angeles, Asia/Tokyo, Asia/Ho_Chi_Minh, Pacific/Auckland đều xanh; đột biến (coi giờ VN là UTC, lệch hạng, bỏ đổi offset) đều bị bắt. Task 2 cũng đã đối chiếu với `export_smashcourt_bookings.sql` (items, giá, `final_total`, `tier_rank`, `created_at`, `member_since`) và qua kiểm đột biến. Phần thời gian của Task 1 đã đối chiếu với `export_smashcourt_bookings.sql` bằng test `CustomerHistoryPhase5EquivalenceTests` (60 điểm kiểm, có đột biến chứng minh test bắt được lỗi `<=` và lỗi lệch 7 giờ) và chạy bộ unit test trên Linux với TZ=UTC, America/Los_Angeles, Asia/Tokyo, Asia/Ho_Chi_Minh.
