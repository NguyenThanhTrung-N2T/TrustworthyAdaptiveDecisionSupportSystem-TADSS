# PHASE 6 — HƯỚNG DẪN CHO NGƯỜI LÀM FE (EPIC 6B)

| | |
| :--- | :--- |
| **Dành cho** | Người làm `smashcourt-fe` ở Epic 6B (Phase 6, W09–W12, 02/11 – 29/11/2026) |
| **Trạng thái** | BE (Epic 6A, kèm P6-31: `cancelSource`, `cancelledByName`, `cancelledAt`) xong 31/10/2026 trên nhánh `feat/phase-6-deposit` của `SmashCourt-BE`; FE chưa bắt đầu |
| **Vai trò file** | Điểm vào: thứ tự đọc, cách chạy, danh sách việc theo từng file FE lệch BE (Mục 4), checklist từng gói, bẫy đã gặp. Nội dung chi tiết **không chép lại** ở đây; nguồn chuẩn là hai tài liệu ở Mục 1. Hai nơi lệch nhau thì theo nguồn chuẩn và báo lại để sửa file này |

---

## 1. Đọc gì, theo thứ tự nào

| # | Tài liệu | Đọc phần nào | Để làm gì |
| :---: | :--- | :--- | :--- |
| 1 | File này | Toàn bộ | Nắm phạm vi và các bẫy |
| 2 | [`PHASE6_DESIGN_SPEC.md`](PHASE6_DESIGN_SPEC.md) | Mục 5.2 (5 gói, tuần, tiêu chí xong); **Mục 8.1–8.5** (từng màn: file sửa, hành vi); Mục 9.1 dòng FE-11, FE-12 | Việc phải làm. Các dòng "Ghi chú FE từ BE 6A-x" và "Tác động của 6A-6 lên FE" trong 8.2 là những điều BE phát hiện khi làm, phải đọc hết |
| 3 | [`SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md`](../../SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md) | Mục 2 (trạng thái, trường mới), 3 (endpoint đổi response), 4 (endpoint hàng chờ, duyệt, từ chối), **5 (mã lỗi)**, 6 (sự kiện SignalR), 7 (ca biên) | Hợp đồng API. Mock MSW chép từ ví dụ JSON trong file này |
| 4 | [`docs/research/E2E_TEST_SPECIFICATION.md`](../docs/research/E2E_TEST_SPECIFICATION.md) | Mục 2.1 (chạy trên MSW), Mục 5 (E2E-01…14) | Kịch bản Playwright của 6B-5 |
| 5 | [`docs/report/USE_CASES.md`](../docs/report/USE_CASES.md) Mục 5, [`docs/report/ACTIVITY_AND_STATE_DIAGRAMS.md`](../docs/report/ACTIVITY_AND_STATE_DIAGRAMS.md) | UC-DEPOSIT-01…07, sơ đồ hoạt động 1–2, sơ đồ trạng thái booking | Hiểu luồng trước khi dựng màn |
| 6 | `smashcourt-fe/CLAUDE.md` | Toàn bộ | Quy ước đặt file, tên, cách gọi API của repo FE |

Không cần đọc: các plan `PHASE6_PLAN_6A-*.md` (việc của BE), hợp đồng BE ↔ TADSS (FE không gọi TADSS, không thấy `decisionId`).

## 2. Nghiệp vụ trong 5 dòng

1. Khách đặt online ⇒ BE hỏi TADSS ⇒ đơn ra một trong ba trạng thái: `PENDING` (phải trả cọc qua VNPay trong hạn `expiresAt`), `CONFIRMED` (cọc quá nhỏ hoặc 0 đ, thu tại quầy, không có link), `PENDING_APPROVAL` (chờ Manager duyệt trước `approvalExpiresAt`). TADSS từ chối ⇒ lỗi `BOOKING_REJECTED`, không tạo đơn.
2. Manager/Owner duyệt (nhập % cọc) hoặc từ chối đơn chờ duyệt. Duyệt xong đơn thành `PENDING`; khách lấy link bằng `retry-payment` (email không chứa link VNPay).
3. Thanh toán thất bại không hủy đơn: hạn rút còn tối đa 5 phút, khách thanh toán lại hoặc hủy. Quá hạn ⇒ job hủy (`cancelReason = PAYMENT_EXPIRED`).
4. Check-out: nhân viên thu `remainingAmount` (tổng trừ số đã trả online).
5. Mọi sự cố phía TADSS ⇒ cọc 100 %; FE không cần biết TADSS, chỉ rẽ nhánh theo `status` và `code` mà BE trả.

## 3. Cách chạy

### 3.1. Trên MSW (mặc định cho cả 6B, không cần BE)

Spec chốt FE làm trên Mock Service Worker (FE-02, FE-03): handler trả đúng ví dụ của DEPOSIT_APPROVAL_API, mỗi ca biên một response (spec 8.1, 8.2 liệt kê các ca MSW bắt buộc: đơn bị job hủy, đơn đã hủy với mỗi `cancelSource` (`CUSTOMER`, `LINK`, `STAFF` có và không có `cancelledByName`, `SYSTEM`), đơn hủy cũ `cancelReason = null`, cùng một đơn `STAFF` trả cho endpoint nhân viên (có tên) và endpoint khách (tên `null`), `confirm` thành công mà đơn `CANCELLED_PENDING_REFUND`, `PENDING` vài giây rồi `PAID_ONLINE`, thất bại mã `24` và `79`, 503 `TADSS_UNAVAILABLE` rồi 200…).

Repo FE hiện **chưa có** MSW, Playwright hay bộ chạy unit test (`package.json` chỉ có `dev`, `build`, `lint`; CI chạy `lint` + `build`). Cài và cấu hình chúng là một phần của 6B-1 (MSW, unit test cho hàm giờ FE-12) và 6B-5 (Playwright, thêm `tsc --noEmit` vào `.github/workflows/ci.yml`). Phiên bản do người làm FE chọn, ghi vào plan 6B-1.

### 3.2. Với BE thật (khi cần thử nối, sau khi xong màn trên MSW)

Cần người giữ BE gửi riêng (không qua repo, không qua chat nhóm công khai) hai file bí mật: `SmashCourt-BE/appsettings.Development.json` và `SmashCourt-BE/.env` (khóa VNPay sandbox, `NGROK_AUTHTOKEN`).

```bash
# Trong SmashCourt-BE (cần Docker Desktop)
docker compose -f docker-compose.yml -f docker-compose.mailpit.yml up -d --build
#   BE:       http://localhost:8080   (Swagger /swagger)
#   Mailpit:  http://localhost:8025   (mọi email BE gửi: OTP đăng ký, đã duyệt cọc, hết hạn...)
#   ngrok:    http://localhost:4040   (URL công khai cho VNPay gọi về)
docker compose -f docker-compose.yml -f docker-compose.mailpit.yml down   # tắt
```

| Việc | Đặt thế nào |
| :--- | :--- |
| FE trỏ tới BE | `.env` của FE: `NEXT_PUBLIC_API_BASE_URL=http://localhost:8080` (BE chạy Docker) hoặc `http://localhost:5179` (`dotnet run`). Một biến duy nhất cho cả REST lẫn SignalR hub; `NEXT_PUBLIC_API_URL` đã bỏ (31/10) |
| Cổng FE | Phải là `3000`: CORS của BE chỉ cho `http://localhost:3000` và tên miền Vercel |
| VNPay quay về FE local | `FrontendBaseUrl` của BE = `http://localhost:3000` (trong `appsettings.Development.json` hoặc biến môi trường `FrontendBaseUrl`). VNPay gọi URL ngrok của BE; lần đầu ngrok hiện trang cảnh báo, bấm "Visit Site"; BE chuyển tiếp tới `FrontendBaseUrl/payment/result?vnp_...` |
| Thẻ thử VNPay sandbox | Lấy ở trang hướng dẫn sandbox của VNPay (NCB); không ghi số thẻ hay OTP vào repo |
| Rate limit của BE | Đăng nhập 5 lần/phút/IP, đăng ký và xác thực 10 lần/phút/IP, đặt sân 20 lần/phút/người. Chạy Playwright trên BE thật thì dùng lại phiên đăng nhập, không đăng nhập lại ở mỗi test |
| Cờ TADSS | `TadssConfig:EnableTadssDecision` đang **tắt**: BE luôn cọc 100 %, không có đơn chờ duyệt. Đừng tự bật; muốn thử nhánh chờ duyệt với BE thật thì hỏi người giữ BE. Ca chờ duyệt thử trên MSW |

## 4. FE hiện tại lệch BE ở đâu (rà soát mã `develop` ngày 31/10)

FE lệch BE là bình thường: BE đã đổi nhiều qua 6A-1…6A-6 và P6-31, FE chưa làm gì cho Phase 6. Bảng dưới là **danh sách việc theo từng file**, rút từ mã FE thật (số dòng tại thời điểm rà soát, có thể xê dịch). Quy tắc chi tiết của từng màn vẫn ở spec 8.1–8.4; bảng này cho biết **chỗ nào trong mã** phải sửa.

### 4.1. Kiểu dữ liệu, hàm API, sự kiện (6B-1)

| # | File FE | Hiện tại | Phải sửa thành (theo BE) | Nguồn |
| :---: | :--- | :--- | :--- | :--- |
| A1 | `src/features/booking/shared/types/booking.types.ts` — `BookingStatus` | 10 giá trị `PENDING: 0` … `NO_SHOW: 9` | Thêm `PENDING_APPROVAL: 10` (giá trị cuối, P6-15). Mọi `switch`/bảng nhãn theo trạng thái phải có nhánh cho giá trị này (A13, A14) | API 2.1 |
| A2 | cùng file — `BookingDto` | Chưa có trường nào của Phase 6 | Thêm 11 trường, đều `\| null`: `depositRate`, `depositAmount`, `remainingAmount`, `depositSource`, `approvalExpiresAt`, `approvalReasonCode`, `approvalReasons` (`string[]`), `cancelReason`, `cancelSource`, `cancelledByName`, `cancelledAt`. Thêm kiểu chuỗi `DepositSource`, `CancelReason` (7 giá trị), `CancelSource` (4 giá trị) | API 2.2; spec 8.1 |
| A3 | cùng file — `OnlineBookingResponse` | 4 khóa; `paymentUrl: string`, `expiresAt: string` | Đúng 12 khóa của API 3.1; `paymentUrl`, `expiresAt`, `paymentLinkExpiresAt`, 4 trường cọc, `approvalExpiresAt` đều `\| null`; có `status`, `bookingCode` | API 3.1 |
| A4 | `src/features/booking/shared/types/payment.types.ts` — `RetryPaymentResponseDto` | 4 khóa, `paymentUrl: string`, chú thích `expiresAt` "ISO 8601" | BE trả **cùng kiểu** `OnlineBookingResponse` (A3): dùng chung type, bỏ type riêng. Mọi trường giờ BE trả là giờ VN `dd/MM/yyyy HH:mm:ss`, không phải ISO 8601 (chỉ thời gian trong sự kiện SignalR là ISO UTC) | API 1.3, 3.3 |
| A5 | cùng file — `PaymentConfirmationDto` | `isSuccess`, `bookingId`, `bookingCode`, `amount`, `message`, đều không null | Thêm `transactionRef`, `responseCode`, `bookingStatus`; `bookingId`, `bookingCode`, `amount`, `transactionRef`, `responseCode`, `bookingStatus` đều `\| null` (chữ ký sai, không tìm thấy giao dịch) | API 3.6 |
| A6 | (chưa có) | Không có type, hàm API nào cho hàng chờ duyệt | Tạo `src/features/booking-approval/shared/types/booking-approval.types.ts` và `src/api/booking-approval.api.ts` (`fetchPendingApprovals`, `approveBooking`, `rejectBooking`) | API 4.1–4.3; spec 8.1 |
| A7 | `src/lib/signalr-events.ts`, `src/features/notifications/NotificationDispatcher.tsx` | Không có 3 sự kiện duyệt cọc | Thêm `BOOKING_PENDING_APPROVAL`, `BOOKING_APPROVAL_RESOLVED`, `PENDING_APPROVAL_QUEUE_CHANGED` (giá trị trùng tên, khớp `SignalREvents.cs`); đăng ký toast và tải lại. `BookingCancelled`, `BookingExpired` không mang dữ liệu hủy: nhận thì tải lại đơn | API Mục 6; spec 8.1 |
| A8 | `src/shared/utils/date.ts`, `dateFormatter.ts` và 17 file dùng `toISOString().split("T")[0]` | "Hôm nay" theo UTC; `dateFormatter.ts` tự đọc chuỗi | Thêm `getTodayVN()`, `parseBackendDateVN()`; thay ở: `AnalyticsSummaryPanel`, `PricingSuggestionsPanel`, `PromotionSuggestionsPanel`, `StrategicInsightsPanel`, `CourtAndTimeSelectionStep`, `useBookingForm`, `BookingCalendarView`, `BookingFilterToolbar`, `WalkInBookingWorkspace`, `useBookingSchedule`, `BookingManagementBase`, `filter.types.ts`, `walkinBooking.types.ts`, `CourtManagementBase`, `CourtFilters`, `useCourtManagement`, `dateFormatter.ts`. `dateFormatter.ts` chuyển sang gọi `parseBackendDate` | Spec 8.1 (T-7, T-8) |
| A9 | `.env.example`, `src/lib/signalr-client.ts` | **Đã làm 31/10.** Trước đó REST đọc `NEXT_PUBLIC_API_BASE_URL`, SignalR đọc `NEXT_PUBLIC_API_URL` (mặc định `localhost:5179`) | Gộp về `NEXT_PUBLIC_API_BASE_URL`: hub dựng bằng `toUrl("/hubs/notifications")` như REST; file mẫu, README sửa theo | — |
| A10 | `package.json`, `.github/workflows/ci.yml` | Không có MSW, Playwright, bộ chạy unit test; CI chỉ `lint` + `build` | Cài MSW (6B-1), bộ chạy unit test cho FE-12 (6B-1), Playwright (6B-5); thêm bước `npx tsc --noEmit -p .` vào CI (FE-01) | Spec 8.5 |

### 4.2. Logic và màn hình (6B-2…6B-4)

| # | File FE | Hiện tại | Phải sửa thành (theo BE) | Nguồn |
| :---: | :--- | :--- | :--- | :--- |
| A11 | `useBookingSubmit.ts` (khoảng dòng 67), `BookingDetailModal.tsx` (69), `BookingHistoryList.tsx` (96), `BookingForm.tsx` (29, 100) | `window.location.href = paymentUrl` vô điều kiện; `onSuccess(paymentUrl: string)` | **C1:** rẽ nhánh theo `status`; `paymentUrl` null (đơn `CONFIRMED`, `PENDING_APPROVAL`) ⇒ tới trang kết quả đặt sân; chỉ mở link khi `PENDING` và chưa quá `paymentLinkExpiresAt`, quá thì gọi `retry-payment` | Spec 8.2 (C1, P6-27) |
| A12 | `src/features/booking/customer/components/shared/PaymentResult.tsx` | Tiêu đề "Thanh toán thành công!" theo `isSuccess` (dòng 117–158); nút "Đặt lại" | Rẽ nhánh theo `bookingStatus`; `PENDING` + `isSuccess` ⇒ hỏi lại chi tiết đơn 2–3 giây/lần tới 60 giây; thất bại chọn câu theo `responseCode` (`24`, `79`/`13`, `15`) với hai nút "Thanh toán lại" / "Hủy đơn"; `CANCELLED_PENDING_REFUND` ⇒ câu hoàn tiền; khôi phục phiên trước khi `RoleGuard` chuyển trang | Spec 8.2 (N-36, P6-28); API 3.6 |
| A13 | `src/features/booking/customer/utils/bookingStatus.ts` | Nhãn không có `PENDING_APPROVAL`; `canCancelBooking` chỉ `CONFIRMED`, `PAID_ONLINE` | Nhãn `PENDING_APPROVAL` ("Chờ chi nhánh xác nhận"), `CONFIRMED` online ("Thanh toán tại quầy"); khách hủy được `PENDING`, `PENDING_APPROVAL`, `CONFIRMED`, `PAID_ONLINE` **trước giờ bắt đầu** (tính bằng `+07:00`, P6-23). Link hủy trong email chỉ nhận `CONFIRMED`, `PAID_ONLINE` | BE `BookingCancellationService` (`CustomerCancellable`, `LinkCancellable`); spec 8.2 |
| A14 | `src/features/booking/shared/utils/bookingStatus.ts`, `bookingRules.ts` (`canCancel`, dòng 94) | Nhãn không có `PENDING_APPROVAL`; nhân viên hủy được `CONFIRMED`, `PAID_ONLINE`, `PENDING` | Thêm nhãn "Chờ duyệt cọc"; `canCancel` thêm `PENDING_APPROVAL` (BE `StaffCancellable` = `PENDING`, `PENDING_APPROVAL`, `CONFIRMED`, `PAID_ONLINE`; nhân viên hủy được cả sau giờ bắt đầu) | Spec 8.4 |
| A15 | `calculateRefundInfo` (`customer/utils/bookingStatus.ts` dòng 96), gọi ở `BookingDetailModal.tsx` (khoảng 442–452) và `CancelBookingModal.tsx` (38–49) | Truyền `finalTotal`; số giờ còn lại tính theo giờ máy | Truyền **số đã trả**: `PAID_ONLINE` ⇒ `depositAmount` (đơn online cũ trước Phase 6 có `depositAmount = null` ⇒ `finalTotal`); `PENDING`, `PENDING_APPROVAL`, `CONFIRMED` online ⇒ 0. Số giờ tới giờ chơi tính bằng mốc `+07:00` (P6-22) | Spec 8.2; API 3.4 |
| A16 | `BookingCard`, `BookingDetailModal` (khách) | Không hiện cọc, hạn, lý do hủy | Cọc / còn lại; nút "Thanh toán cọc" khi `PENDING` và chưa quá `expiresAt`; đơn hủy: nhãn lý do theo `cancelReason`, dòng ai hủy theo `cancelSource` và thời điểm `cancelledAt`, **không** tên nhân viên | Spec 8.2 (P6-31) |
| A17 | `src/features/booking/shared/components/management/BookingDetailDrawer.tsx` | Chỉ có `finalTotal` (dòng 266–312) | Thêm: mức cọc (`depositRate`, `depositAmount`, `depositSource`); **"cọc phải trả" khi `paymentStatus = UNPAID`, "đã cọc" khi đã thu** (`remainingAmount` luôn bằng `finalTotal − depositAmount`, kể cả khi chưa trả); số thu khi check-out = `remainingAmount` (BE tính lại trên tổng Payment thành công, có thể thấp hơn nếu khách trả hai link); lý do cần duyệt (`approvalReasons`); mục hủy: lý do, người hủy (`cancelledByName`, `cancelSource`), thời điểm `cancelledAt` | Spec 8.4; API 2.2, 3.5 |
| A18 | `BookingFilterToolbar.tsx` | Không có lọc "Chờ duyệt cọc" | Thêm `status=PENDING_APPROVAL` | Spec 8.4 |
| A19 | `src/layouts/manager/navigation/*`, `src/layouts/owner/navigation/*` | Không có mục hàng chờ duyệt | Thêm `/manager/booking-approvals`, `/owner/booking-approvals`, badge = `totalItems` | Spec 8.3 |
| A20 | `src/features/booking/shared/components/new/CourtTrack.tsx` | Khóa slot đã qua tính một lần theo giờ máy | Khóa khi giờ bắt đầu `<=` bây giờ (giờ VN), tính lại khi trang mở lâu | Spec 8.2 (A9) |
| A21 | `BookingConfirmationStep.tsx` | Câu "sẽ được chuyển đến trang thanh toán" | "Tổng tiền đơn; mức cọc được xác định sau khi đặt, phần còn lại thu tại quầy" | Spec 8.2 |
| A22 | (chưa có) trang kết quả đặt sân | `useBookingSubmit` chuyển thẳng sang VNPay | Trang `app/(customer)/bookings/result` → `BookingResultPage`, rẽ nhánh `PENDING` / `CONFIRMED` / `PENDING_APPROVAL` | Spec 8.2 |

Trạng thái `CONFIRMED` dùng cho cả đơn tại quầy lẫn đơn online cọc 0 đ; phân biệt bằng `source` (`WALK_IN` / `ONLINE`).

## 5. Checklist theo gói

Mỗi gói nên có plan riêng `spec/PHASE6_PLAN_6B-x_*.md` như các gói 6A (mục kết quả ở cuối plan).

| Gói | Tuần | Việc (chi tiết ở spec) | Xong khi |
| :--- | :---: | :--- | :--- |
| **6B-1** Nền | W09 | Spec 8.1: type — **type FE hiện chưa có trường nào của Phase 6**, danh sách đủ ở spec 8.1 (`PENDING_APPROVAL = 10`, `DepositSource`, trường cọc, `cancelReason` 7 giá trị, `cancelSource`, `cancelledByName`, `cancelledAt`, `paymentLinkExpiresAt`, `PaymentConfirmationDto` thêm `responseCode` + `bookingStatus`); `booking-approval.api.ts`; 3 sự kiện SignalR vào `signalr-events.ts` + `NotificationDispatcher`; `getTodayVN()`, `parseBackendDateVN()` và thay khoảng 20 chỗ `toISOString().split("T")[0]` (T-7), đọc ngày `dd/MM` đúng (T-8); MSW; thiết kế UI Việc theo file: A1–A10 (Mục 4.1) | Mock khớp ví dụ DEPOSIT_APPROVAL_API; FE-12 (phần hàm giờ) đạt; thiết kế được duyệt |
| **6B-2** Khách | W10 | Spec 8.2: màn xác nhận, trang kết quả đặt sân (mới), trang kết quả thanh toán, lịch sử / chi tiết, xem trước hoàn tiền, ẩn nút hủy từ giờ bắt đầu, nhãn lý do hủy, sửa C1. Việc theo file: A11–A13, A15, A16, A20–A22 | Rẽ nhánh đúng theo `status`, `bookingStatus`, `code`; FE-11, FE-12 đạt |
| **6B-3** Manager/Owner | W11 | Spec 8.3: trang hàng chờ (hai vai trò, dùng chung `shared/`), hộp thoại duyệt/từ chối, badge menu, realtime, đồng hồ hạn duyệt. Việc theo file: A6, A19 | Mọi mã lỗi ở DEPOSIT_APPROVAL_API Mục 5 có thông điệp |
| **6B-4** Staff | W11 | Spec 8.4: nhãn trạng thái mới ở danh sách/lịch/time grid, lọc "Chờ duyệt cọc", hủy đơn chờ duyệt, check-out hiện cọc / đã trả / `remainingAmount`, người hủy theo `cancelSource` + `cancelledByName`, thời điểm `cancelledAt`. Việc theo file: A14, A17, A18 | — |
| **6B-5** Kiểm thử | W12 | Playwright trên MSW cho E2E-01…14; `tsc --noEmit` vào CI | **Gate M6** |

6B-2…6B-4 không phụ thuộc nhau, làm song song được sau 6B-1.

## 6. Bẫy đã gặp (đọc trước khi code)

| # | Bẫy | Làm đúng | Nguồn |
| :---: | :--- | :--- | :--- |
| 1 | **C1:** FE hiện gán `window.location.href = paymentUrl` vô điều kiện (`useBookingSubmit.ts`, `BookingDetailModal.tsx`, `BookingHistoryList.tsx`, `BookingForm.tsx`) trong khi `paymentUrl` có thể `null` (đơn `CONFIRMED`, đơn chờ duyệt). Xảy ra **ngay cả khi cờ TADSS tắt** (đơn dưới 5.000 đ, đơn giảm 100 %) | Rẽ nhánh theo `status`; chuyển tới trang kết quả đặt sân. **Ràng buộc triển khai:** BE Phase 6 chưa được lên môi trường dùng chung có FE thật trước khi bản sửa C1 lên trước | Spec 8.2 (ghi chú 6A-2, P6-17) |
| 2 | Trang kết quả thanh toán tin `isSuccess` | `isSuccess` chỉ là kết quả cổng. Rẽ nhánh theo `bookingStatus` của response `confirm`; `PENDING` + `isSuccess = true` thì hỏi lại chi tiết đơn mỗi 2–3 giây tới 60 giây rồi báo "chưa nhận được xác nhận", **không** báo thất bại; thất bại thì chọn câu theo `responseCode` | Spec 8.2 (N-36); API 3.6 |
| 3 | Quay về từ VNPay là tải trang mới: access token (giữ trong bộ nhớ) mất, `RoleGuard` đá sang đăng nhập | `/payment/result` khôi phục phiên bằng refresh cookie trước khi guard quyết định; nếu vẫn phải đăng nhập thì giữ nguyên query `vnp_*` để quay lại đúng trang | Spec 8.2 (ghi chú sandbox 28/10) |
| 4 | Mở lại link VNPay cũ | BE không lưu URL; link hết hạn trước `expiresAt` 2 phút (`paymentLinkExpiresAt`), quá hạn VNPay hiện trang lỗi riêng và **không quay về FE**. Quá `paymentLinkExpiresAt` thì gọi `retry-payment` lấy link mới; tải lại đơn khi tab FE được focus lại (`visibilitychange`) | Spec 8.2 (6A-6, P6-27); API 3.3 |
| 5 | Đồng hồ đếm ngược tự tính từ giờ tạo đơn | Luôn đọc `expiresAt` / `approvalExpiresAt` từ BE (hạn bị rút còn 5 phút sau thanh toán thất bại, được gia hạn khi `retry-payment`) | Spec 8.2 |
| 6 | "Hôm nay" theo UTC (`new Date().toISOString()`): 00:00–06:59 giờ VN ra ngày hôm trước | `getTodayVN()` theo `Asia/Ho_Chi_Minh`; cộng/trừ ngày trên chuỗi `yyyy-MM-dd`; gửi ngày lên BE dạng `yyyy-MM-dd`, không gửi lại chuỗi `dd/MM/yyyy` | Spec 8.1 (T-7, T-13) |
| 7 | `new Date("05/10/2026 07:00:00")` hiểu là 10/05 (kiểu Mỹ) | Chuỗi giờ BE trả chỉ đọc qua `parseBackendDate` / `formatDateTime` trong `src/shared/utils/date.ts`; đồng hồ đếm ngược dùng `+07:00`, không phụ thuộc múi giờ máy | Spec 8.1 (T-8, R-6) |
| 8 | Xử lý lỗi bằng cách dò chuỗi `message` | Rẽ nhánh theo `code` (envelope `{ success, code, message, data, errors }`). `PAYMENT_HOLD_EXPIRED` có hai câu (hết hạn giữ chỗ / còn dưới 3 phút), cùng một mã | API Mục 5; spec 8.2 (6A-6) |
| 9 | Tự suy "ai hủy" từ `cancelReason`, hoặc hiện tên nhân viên cho khách | Đọc `cancelSource` (`CUSTOMER`, `LINK`, `STAFF`, `SYSTEM`); màn nhân viên hiện tên ở `cancelledByName` (`null` mà `STAFF` thì "Nhân viên"); màn khách chỉ hiện "Bạn đã hủy" / "Chi nhánh đã hủy" / "Hệ thống tự hủy" (BE luôn trả tên `null` cho khách). Đơn hủy trước Phase 6 có `cancelSource` nhưng `cancelReason` là `null`. `cancelReason` chỉ dùng cho nhãn lý do. Thời điểm hủy đọc từ `cancelledAt` (giờ VN, qua `parseBackendDate`), cả khách và nhân viên. SignalR `BookingCancelled` không mang các trường này: tải lại đơn | Spec 8.2, 8.4 (P6-31); API 2.2 |
| 10 | Đồng hồ chờ duyệt về 0 thì tự hiện "đã hủy" | Job hủy trong vòng 1 phút sau hạn: hiện "Đang chờ chi nhánh phản hồi…", tải lại khi nhận `BookingExpired` hoặc sau 60 giây | Spec 8.2 (6A-5) |
| 11 | Giả định `NEED_APPROVAL` luôn vào hàng chờ | Hạn duyệt đã qua lúc BE nhận ⇒ BE trả `PENDING` cọc 100 % (`FULL_PAYMENT_FALLBACK`). Luôn rẽ nhánh theo `status` | Spec 8.2 (P6-24) |
| 12 | Hộp thoại duyệt gặp 503 rồi người dùng sửa nội dung trước khi thử lại | Giữ hộp thoại, nút "Thử lại" gửi lại **đúng** nội dung cũ; 409 `APPROVAL_EXPIRED` ⇒ đóng hộp thoại, tải lại hàng chờ | Spec 8.3 |
| 13 | Nút hủy của khách vẫn hiện khi đã tới giờ chơi | Ẩn từ giờ bắt đầu (tính bằng `+07:00`); BE trả `BOOKING_ALREADY_STARTED` nếu vẫn gọi | Spec 8.2 (P6-23) |
| 14 | Xem trước tiền hoàn tính trên `finalTotal` | Tính trên số đã trả (`depositAmount` khi `PAID_ONLINE`, đơn online cũ `depositAmount = null` thì `finalTotal`; 0 khi chưa trả) | Spec 8.2; A15 |
| 15 | Hiện `depositAmount` là "đã cọc" cho đơn chưa trả | `remainingAmount = finalTotal − depositAmount` kể cả khi chưa trả; "đã cọc" chỉ khi `paymentStatus` khác `UNPAID` | API 2.2; A17 |
| 16 | Sửa type FE theo trí nhớ hoặc theo response một lần gọi | Theo danh sách khóa ở API 2.2 / 3.1 / 3.6 (khóa `null` vẫn có mặt); test `HttpDepositDtoJsonTests` của BE khóa đúng danh sách này | API 2.2, 3.1, 3.6; FE-03 |

## 7. CI/CD của FE (Epic 6C, từ 31/10/2026)

- `.github/workflows/ci.yml`: `npm ci` ⇒ `lint` ⇒ **`npx tsc --noEmit -p .`** (mới, FE-01) ⇒ `build` cho mọi push/pull request vào `master`, `develop`.
- **Chỉ push `master`** (CI đạt) mới triển khai lên Vercel production (job `deploy-production`); `develop` chỉ chạy CI, không triển khai.
- Việc của chủ repo FE (một lần): tạo Environment `production` trong Settings › Environments, thêm secrets `VERCEL_TOKEN` (Vercel › Account Settings › Tokens), `VERCEL_ORG_ID`, `VERCEL_PROJECT_ID` (Vercel › Project Settings › General); trong Vercel tắt tự triển khai theo Git (Project Settings › Git) để chỉ còn đường qua CI. Chưa đặt secrets thì push `master` vẫn chạy CI, riêng bước triển khai báo lỗi.
- Biến `NEXT_PUBLIC_API_BASE_URL` (địa chỉ gốc BE, không có `/api`) của bản production khai trong Vercel (Project Settings › Environment Variables, loại Config vì giá trị nằm trong bundle trình duyệt), không lấy từ file `.env` trên máy.
- Khi FE trên Vercel gọi BE qua ngrok gói miễn phí: mọi request phải có header `ngrok-skip-browser-warning: true`, nếu không ngrok trả trang cảnh báo HTML thay cho JSON.
- Chạy local: một file `.env` duy nhất, khối BE local đang bật, khối ngrok để dạng comment, tự đổi tay.

## 8. Hỏi ai, sửa ở đâu

- Hợp đồng API thiếu hoặc mơ hồ: hỏi người giữ BE; nếu phải đổi, sửa DEPOSIT_APPROVAL_API và code BE trước rồi FE theo sau (không tự đoán trường).
- Quyết định nghiệp vụ mới trong lúc làm FE: ghi vào spec Phase 6 (mã P6-xx kế tiếp) để hai bên cùng thấy.
- Lỗi BE phát hiện khi làm FE: báo kèm request/response (không kèm token, mật khẩu, khóa VNPay).
