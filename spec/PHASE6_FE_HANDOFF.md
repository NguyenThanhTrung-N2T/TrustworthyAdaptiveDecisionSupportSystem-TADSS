# PHASE 6 — HƯỚNG DẪN CHO NGƯỜI LÀM FE (EPIC 6B)

| | |
| :--- | :--- |
| **Dành cho** | Người làm `smashcourt-fe` ở Epic 6B (Phase 6, W09–W12, 02/11 – 29/11/2026) |
| **Trạng thái** | BE (Epic 6A, kèm P6-31: `cancelSource`, `cancelledByName`, `cancelledAt`) xong 31/10/2026, đã gộp vào nhánh `develop` của `SmashCourt-BE` (đang chạy trên bản deploy). **Rà lại 08/11:** hợp đồng API không đổi từ 31/10, danh sách Mục 4 vẫn đúng (kết quả ở 4.3). FE chưa bắt đầu; chia việc logic/API và UI ở 4.4 |
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

1. Khách đặt online ⇒ BE hỏi TADSS ⇒ đơn ra một trong ba trạng thái: `PENDING` (phải trả cọc qua VNPay trong hạn `expiresAt`), `CONFIRMED` (cọc quá nhỏ hoặc 0 đ, thu tại quầy, không có link), `PENDING_APPROVAL` (chờ Manager duyệt trước `approvalExpiresAt`). TADSS từ chối ⇒ lỗi 400 `BOOKING_REJECTED`; đơn **vẫn có** trong lịch sử của khách ở trạng thái `CANCELLED`, `cancelReason = POLICY_REJECTED`, nhãn "Không được nhận" (C2), và BE không gửi `BookingCancelled` cho chính khách.
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
| A10 | `package.json`, `.github/workflows/ci.yml` | Không có MSW, Playwright, bộ chạy unit test (`scripts` chỉ `dev`, `build`, `start`, `lint`). **CI đã có `npx tsc --noEmit -p .` từ 31/10** (FE-01, Mục 7) | Cài MSW (6B-1), bộ chạy unit test cho FE-12 (6B-1), Playwright (6B-5) | Spec 8.5 |

### 4.2. Logic và màn hình (6B-2…6B-4)

| # | File FE | Hiện tại | Phải sửa thành (theo BE) | Nguồn |
| :---: | :--- | :--- | :--- | :--- |
| A11 | `useBookingSubmit.ts` (khoảng dòng 67), `BookingDetailModal.tsx` (69), `BookingHistoryList.tsx` (96), `BookingForm.tsx` (29, 100) | `window.location.href = paymentUrl` vô điều kiện; `onSuccess(paymentUrl: string)` | **C1:** rẽ nhánh theo `status`; `paymentUrl` null (đơn `CONFIRMED`, `PENDING_APPROVAL`) ⇒ tới trang kết quả đặt sân; chỉ mở link khi `PENDING` và chưa quá `paymentLinkExpiresAt`, quá thì gọi `retry-payment` | Spec 8.2 (C1, P6-27) |
| A12 | `src/features/booking/customer/components/shared/PaymentResult.tsx` | Tiêu đề "Thanh toán thành công!" theo `isSuccess` (dòng 117–158); nút "Đặt lại" | Rẽ nhánh theo `bookingStatus`; `PENDING` + `isSuccess` ⇒ hỏi lại chi tiết đơn 2–3 giây/lần tới 60 giây; thất bại chọn câu theo `responseCode` (`24`, `79`/`13`, `15`) với hai nút "Thanh toán lại" / "Hủy đơn"; `CANCELLED_PENDING_REFUND` ⇒ câu hoàn tiền; khôi phục phiên trước khi `RoleGuard` chuyển trang | Spec 8.2 (N-36, P6-28); API 3.6 |
| A13 | `src/features/booking/customer/utils/bookingStatus.ts` | Nhãn không có `PENDING_APPROVAL`; `canCancelBooking` chỉ `CONFIRMED`, `PAID_ONLINE` | Nhãn `PENDING_APPROVAL` ("Chờ chi nhánh xác nhận"), `CONFIRMED` online ("Thanh toán tại quầy"); khách hủy được `PENDING`, `PENDING_APPROVAL`, `CONFIRMED`, `PAID_ONLINE` **trước giờ bắt đầu** (tính bằng `+07:00`, P6-23). Link hủy trong email chỉ nhận `CONFIRMED`, `PAID_ONLINE` | BE `BookingCancellationService` (`CustomerCancellable`, `LinkCancellable`); spec 8.2 |
| A14 | `src/features/booking/shared/utils/bookingStatus.ts`, `bookingRules.ts` (`canCancel`, dòng 94) | Nhãn không có `PENDING_APPROVAL`; nhân viên hủy được `CONFIRMED`, `PAID_ONLINE`, `PENDING` | Thêm nhãn "Chờ duyệt cọc"; `canCancel` thêm `PENDING_APPROVAL` (BE `StaffCancellable` = `PENDING`, `PENDING_APPROVAL`, `CONFIRMED`, `PAID_ONLINE`; nhân viên hủy được cả sau giờ bắt đầu) | Spec 8.4 |
| A15 | `calculateRefundInfo` (`customer/utils/bookingStatus.ts` dòng 96), gọi ở `BookingDetailModal.tsx` (khoảng 442–452) và `CancelBookingModal.tsx` (38–49) | Truyền `finalTotal`; số giờ còn lại tính theo giờ máy; chọn mốc bằng `find` đầu tiên thỏa (chỉ đúng khi danh sách đã sắp giảm dần); không tải được chính sách thì **tự dùng bảng % viết cứng** (24 giờ 100 %, 12 giờ 50 %, 6 giờ 25 %) | Truyền **số đã trả**: `PAID_ONLINE` ⇒ `depositAmount` (đơn online cũ trước Phase 6 có `depositAmount = null` ⇒ `finalTotal`); `PENDING`, `PENDING_APPROVAL`, `CONFIRMED` online ⇒ 0. Số giờ tới giờ chơi tính bằng mốc `+07:00` (P6-22). Chọn mốc như BE (4.5): `hoursBefore` lớn nhất ≤ số giờ còn lại, đã qua giờ chơi ⇒ 0 %, không phụ thuộc thứ tự mảng; bỏ bảng viết cứng (không tải được chính sách thì không hiện số xem trước) | Spec 8.2; API 3.4; BE `CancellationPolicy` |
| A16 | `BookingCard`, `BookingDetailModal` (khách) | Không hiện cọc, hạn, lý do hủy | Cọc / còn lại; nút "Thanh toán cọc" khi `PENDING` và chưa quá `expiresAt` (bấm luôn gọi `retry-payment` lấy link mới rồi mở ngay: BE không lưu link, `BookingDto` không có `paymentUrl`); đơn hủy: nhãn lý do theo `cancelReason`, dòng ai hủy theo `cancelSource` và thời điểm `cancelledAt`, **không** tên nhân viên | Spec 8.2 (P6-31) |
| A17 | `src/features/booking/shared/components/management/BookingDetailDrawer.tsx` | Chỉ có `finalTotal` (dòng 266–312) | Thêm: mức cọc (`depositRate`, `depositAmount`, `depositSource`); **"cọc phải trả" khi `paymentStatus = UNPAID`, "đã cọc" khi đã thu** (`remainingAmount` luôn bằng `finalTotal − depositAmount`, kể cả khi chưa trả); số thu khi check-out = `remainingAmount` (BE tính lại trên tổng Payment thành công, có thể thấp hơn nếu khách trả hai link); lý do cần duyệt (`approvalReasons`); mục hủy: lý do, người hủy (`cancelledByName`, `cancelSource`), thời điểm `cancelledAt` | Spec 8.4; API 2.2, 3.5 |
| A18 | `BookingFilterToolbar.tsx` | Không có lọc "Chờ duyệt cọc" | Thêm `status=PENDING_APPROVAL` | Spec 8.4 |
| A19 | `src/layouts/manager/navigation/*`, `src/layouts/owner/navigation/*` | Không có mục hàng chờ duyệt | Thêm `/manager/booking-approvals`, `/owner/booking-approvals`, badge = `totalItems` | Spec 8.3 |
| A20 | `src/features/booking/shared/components/new/CourtTrack.tsx` | Khóa slot đã qua tính một lần theo giờ máy | Khóa khi giờ bắt đầu `<=` bây giờ (giờ VN), tính lại khi trang mở lâu | Spec 8.2 (A9) |
| A21 | `BookingConfirmationStep.tsx` | Câu "sẽ được chuyển đến trang thanh toán" | "Tổng tiền đơn; mức cọc được xác định sau khi đặt, phần còn lại thu tại quầy" | Spec 8.2 |
| A22 | (chưa có) trang kết quả đặt sân | `useBookingSubmit` chuyển thẳng sang VNPay | Trang `app/(customer)/bookings/result` → `BookingResultPage`, rẽ nhánh `PENDING` / `CONFIRMED` / `PENDING_APPROVAL` | Spec 8.2 |
| A23 | Đọc chuỗi giờ BE bằng `new Date(...)` (T-8, bổ sung 08/11): `OwnerPromotionsPage`, `StaffPromotionsPage`, `BranchManagerTab`, `SessionManagementTab`, `PromotionSelector`, `ChatPanel`, các panel AI (`AnalyticsSummaryPanel`, `PricingSuggestionsPanel`, `PromotionSuggestionsPanel`, `StrategicInsightsPanel`, `BookingSuggestionsPanel`, `OccupancyForecastChart`), `LiveCourtCard`, `BookingCalendarView`, `useTimeGrid`, `SignalRContext` | `new Date("05/10/2026 07:00:00")` bị hiểu là 10/05 kiểu Mỹ; có chỗ là ngày `yyyy-MM-dd` do FE tự tạo (không lỗi) | Soát từng chỗ: chuỗi BE (`dd/MM/yyyy HH:mm:ss`) chỉ đọc qua `parseBackendDate` / `formatDateTime`; thời gian trong payload SignalR là ISO UTC, đọc bằng `new Date` được. `useBookingSchedule`, `CourtFilters` cộng/trừ ngày trên chuỗi `yyyy-MM-dd`, không qua `toISOString` | Spec 8.1 (T-8, T-13) |
| A24 | `src/features/booking/customer/hooks/useRetryPayment.ts` | Xử lý lỗi theo `message` | Theo `code` (danh sách đủ ở 4.5, `retry-payment`): `PAYMENT_HOLD_EXPIRED` (hai câu cùng mã: hết hạn giữ chỗ / còn dưới 3 phút) ⇒ hiện `message`, tải lại đơn, gợi ý đặt lại; 400 `BAD_REQUEST` (đơn không còn `PENDING`, hoặc đã quá hạn thanh toán, kể cả khi job chưa kịp hủy) ⇒ "Đơn đã hết hạn, vui lòng đặt lại" + nút về `/bookings/new`, tải lại đơn; 409 `CONFLICT` (đơn vừa đổi trạng thái) ⇒ tải lại đơn; response dùng type A3 (có `paymentLinkExpiresAt`) | Spec 8.2 (6A-5, 6A-6); API 5 #19; BE `PaymentService.RetryPaymentAsync` |
| A25 | (chưa có) đồng hồ đếm ngược dùng chung | Mỗi màn tự tính, theo giờ máy | Một hook đọc `expiresAt` / `approvalExpiresAt` của BE qua `parseBackendDateVN` (không tự tính từ giờ tạo đơn); về 0 với đơn chờ duyệt ⇒ "Đang chờ chi nhánh phản hồi…", tải lại khi nhận `BookingExpired` hoặc sau 60 giây; tải lại đơn khi tab được focus lại (`visibilitychange`) | Spec 8.2; bẫy 4, 5, 10 |
| A26 | `app/booking/cancel/page.tsx` (trang hủy bằng link trong email; logic đang viết ngay trong page) | Chưa có P6-23 | Ẩn nút hủy từ giờ bắt đầu (`bookingDate` + `startTime` của `CancelTokenInfoDto`, tính bằng `+07:00`); link chỉ hủy được `CONFIRMED`, `PAID_ONLINE` (BE `LinkCancellable`), trạng thái khác thì không hiện nút. **Tiền hoàn lấy thẳng `refundAmount`, `refundPercent` BE trả** (BE đã tính trên số đã thu), không tự tính như A15. Lỗi theo `code`: `BOOKING_ALREADY_STARTED` 400, `NOT_FOUND` 404 (link sai), `BAD_REQUEST` 400 (link đã dùng / hết hạn / trạng thái không hủy được), `ACCOUNT_LOCKED` 403 | Spec 8.2 (P6-23); API 3.4; BE `GetCancelInfoAsync`, `CancelByTokenAsync` |
| A27 | `BookingScheduleView.tsx` (`getStatusColor`), `customer/components/booking-history/BookingFilters.tsx` | `switch` / danh sách lọc không có `PENDING_APPROVAL`; lọc của khách thiếu cả `CONFIRMED` | Màu cho `PENDING_APPROVAL` trên lịch nhân viên (API 2.1: lịch và time grid coi là đang giữ sân); thêm lựa chọn lọc "Chờ duyệt cọc", "Đã xác nhận" cho khách | API 2.1; spec 8.4 |
| A28 | `src/features/court/staff/components/BookingDetailModal.tsx` (chi tiết đơn ở màn sân của nhân viên, có nút check-out) | Chỉ hiện `finalTotal` | Như A17: đã cọc / còn lại, số thu khi check-out = `remainingAmount`, mục hủy theo `cancelSource` + `cancelledByName` | Spec 8.4; API 3.5 |
| A29 | `src/features/booking/shared/hooks/useBookingManagement.ts` (check-in, check-out), `court/staff/components/BookingDetailModal.tsx` | Lỗi check-in hiện nguyên câu BE | Check-in đúng lúc job vừa đánh `NO_SHOW`: 409 `CONFLICT` hoặc 400 ⇒ "Đơn đã bị đánh không đến, vui lòng tải lại" + tải lại danh sách | Spec 8.4 (6A-5) |
| A30 | (chưa có) logic hộp thoại duyệt / từ chối (6B-3) | — | Hàm thuần: % (0…100, tối đa 2 chữ số thập phân) ⇒ `depositRate = % / 100` (tối đa 4 chữ số thập phân như BE); "Số tiền cọc ≈" theo đúng công thức BE ở 4.5 (làm tròn tới đồng, nửa lên) và báo trước khi > 0 mà dưới 5.000 đ (mức tối thiểu là cấu hình BE: lỗi `errors.DepositRate` của BE là nguồn đúng cuối cùng); `reason` 5…500 code point, không chỉ khoảng trắng; đơn `finalTotal = 0` ⇒ duyệt là xác nhận không cọc. Hook gửi: khóa nút khi đang gửi; 503 `TADSS_UNAVAILABLE` giữ nội dung, "Thử lại" gửi **đúng** nội dung cũ; 409 (`APPROVAL_EXPIRED`, `APPROVAL_ALREADY_RESOLVED`, `BOOKING_NOT_PENDING_APPROVAL`) ⇒ đóng hộp thoại, tải lại hàng chờ; `APPROVAL_DECISION_NOT_FOUND` có câu riêng; `VALIDATION_ERROR` gắn lỗi theo khóa `errors` (`DepositRate`, `Reason`) | Spec 8.3; API 4.2, 4.3, 5 #3–#17 |
| A31 | `booking.types.ts` — `TimeGridSlotDto.status` | `"AVAILABLE" \| "LOCKED" \| "IN_USE"` | Thêm `"RESERVED"` (BE `TimeGridService` trả 4 giá trị; FE hiện coi mọi giá trị khác `AVAILABLE` là bận nên không sai hành vi, chỉ sai type) | BE `TimeGridService` |
| A32 | `src/api/core/apiClient.ts`, `src/api/upload.api.ts` (refresh riêng), `src/lib/signalr-client.ts` | **Không gửi** `ngrok-skip-browser-warning` ở đâu cả, trong khi Mục 7 ghi bắt buộc khi FE trên Vercel gọi BE qua ngrok gói miễn phí | Kiểm trước trên Vercel production (mở trang, xem request API trả JSON hay trang cảnh báo HTML). Nếu bị chặn: thêm header ở `apiClient`, lời gọi refresh, và `headers` của `withUrl` SignalR. CORS BE đã `AllowAnyHeader` | Mục 7; P6-36 |

Trạng thái `CONFIRMED` dùng cho cả đơn tại quầy lẫn đơn online cọc 0 đ; phân biệt bằng `source` (`WALK_IN` / `ONLINE`).

### 4.3. Rà soát lại ngày 08/11 (BE `develop`, FE `develop` `8e07911`)

| Hạng mục | Kết quả |
| :--- | :--- |
| Hợp đồng BE từ 31/10 | **Không đổi.** Các commit BE sau P6-31 chỉ thêm `/health/*`, telemetry, auto-prepare (tắt), CD, và sửa lỗi bên trong: N-38 (check-out thu cả dịch vụ nhân viên khác vừa thêm), N-39…N-42 (thêm/xóa cùng một dịch vụ cùng lúc không còn lỗi 500 hay mất lần thêm; phản hồi `POST /api/bookings/{id}/services` và payload `BookingUpdated` hiện đúng số lượng mới). FE không phải sửa gì cho các lỗi này; có thể dùng thẳng phản hồi thêm dịch vụ thay vì tải lại |
| TADSS | FE không gọi TADSS, không có việc mới. Lưu ý nghiệp vụ: với dữ liệu thử, strategy mặc định từ chối khá nhiều đơn (đo 08/11: khoảng 40 % lần đặt khi bật cờ) ⇒ `BOOKING_REJECTED` không phải ca hiếm, phải có thông điệp và đưa khách về bước chọn giờ |
| Trường DTO (so tự động BE ↔ type FE) | Lệch **đúng** như A2 (`BookingDto` thiếu 11 trường) và A3 (`OnlineBookingResponse` thiếu 8 khóa); `BookingCourtDto`, `BookingServiceDto` khớp |
| Route FE gọi ↔ route BE (129 lời gọi) | Mọi route có ở BE, trừ `fetchServiceById` (`GET /api/services/{id}`, `src/api/service.api.ts`): BE không có endpoint này, nhưng hàm không được dùng ở đâu ⇒ mã chết, xóa hoặc để nguyên, không ảnh hưởng chạy |
| Sự kiện SignalR | 12 sự kiện khớp tên; thiếu đúng 3 sự kiện duyệt cọc (A7) |
| Rẽ nhánh theo `code` | FE chỉ dò `code` ở hai file `error-handling.ts` (`features/customer/shared/utils`, `features/branch/owner/utils`); bảng mã ở đó có mã BE **không bao giờ trả** (`CUSTOMER_NOT_FOUND`, `BOOKING_NOT_FOUND`, `INSUFFICIENT_PERMISSIONS`…; BE trả `NOT_FOUND`, `FORBIDDEN`), hiện vẫn chạy vì rơi về `message` của BE. **Chưa chỗ nào xử lý mã Phase 6** (`BOOKING_REJECTED`, `PAYMENT_HOLD_EXPIRED`, `BOOKING_ALREADY_STARTED`, `APPROVAL_EXPIRED`, `APPROVAL_ALREADY_RESOLVED`, `BOOKING_NOT_PENDING_APPROVAL`, `TADSS_UNAVAILABLE`, `SLOT_UNAVAILABLE`, `SLOT_UNAVAILABLE_NOTIFY_REGISTERED`) |
| Số dòng ở Mục 4 | Vẫn đúng: chuyển hướng `paymentUrl` ở `useBookingSubmit.ts:67`, `BookingDetailModal.tsx:69`, `BookingHistoryList.tsx:96`; 17 file dùng `toISOString()` cho ngày (A8) |
| Đối chiếu lại spec 8.1–8.4 và API Mục 2, 5, 6, 7 | Bản 31/10 thiếu 10 việc, đã thêm thành **A23–A32**: đọc chuỗi giờ BE ở khoảng 17 file ngoài luồng đặt sân (T-8); lỗi theo `code` của `useRetryPayment`; đồng hồ đếm ngược dùng chung; trang hủy bằng link `app/booking/cancel`; màu lịch nhân viên và lọc của khách; chi tiết đơn ở màn sân của nhân viên (check-out); lỗi check-in khi job vừa đánh `NO_SHOW`; logic hộp thoại duyệt; type time grid thiếu `RESERVED`; header ngrok. Mục 2 dòng 1 sửa lại: đơn bị TADSS từ chối vẫn có trong lịch sử (C2) |
| Đối chiếu thẳng với mã BE (lượt 3) | Mọi enum, DTO, endpoint, mã lỗi, công thức ghi ở **4.5** lấy từ mã C#. Sửa theo mã: A15 (FE chọn mốc hoàn tiền dựa vào thứ tự mảng và có bảng % viết cứng), A24 (`retry-payment` có hai ca 400 `BAD_REQUEST` và 409 `CONFLICT`), A26 (trang hủy bằng link dùng `refundAmount` BE trả, không tự tính), A30 (công thức cọc, mức tối thiểu). Thêm bẫy 17–19: `bookingDate` ra `"dd/MM/yyyy 07:00:00"`, tỷ lệ cọc phải đúng 4 chữ số thập phân, lý do đếm theo code point |
| Rủi ro đang chạy | Bản deploy đã chạy BE Phase 6. Nếu FE production trên Vercel trỏ vào bản deploy thì **C1 xảy ra ngay** (đơn `CONFIRMED` không có `paymentUrl` ⇒ trình duyệt mở trang hỏng), kể cả khi cờ TADSS tắt ⇒ A11 làm đầu tiên |

### 4.4. Chia việc (08/11): logic/API và UI

Người giữ BE nhận phần **sửa lỗi cũ và map API mới, không đổi giao diện**; người làm FE nhận **mọi phần giao diện** (màn mới, bố cục, chữ hiển thị mới). Phần logic xuất ra hàm/hook có tên rõ để phía UI gọi; phía UI không tự rẽ nhánh theo `status` hay `code`.

| # | Logic / API (người giữ BE) | UI (người làm FE) |
| :---: | :--- | :--- |
| A1–A5 | Toàn bộ: enum, type, `OnlineBookingResponse` dùng chung cho `retry-payment`, `PaymentConfirmationDto` | — |
| A6 | Type + `src/api/booking-approval.api.ts` (`fetchPendingApprovals`, `approveBooking`, `rejectBooking`) | Trang hàng chờ, hộp thoại duyệt/từ chối (6B-3) |
| A7 | Hằng sự kiện + đăng ký trong `NotificationDispatcher` (toast dùng cơ chế có sẵn, tải lại dữ liệu) | Badge, hiển thị riêng của hàng chờ (6B-3) |
| A8 | `getTodayVN()`, `parseBackendDateVN()` + thay ở 17 file + unit test FE-12 | — |
| A10 | MSW (handler theo ví dụ DEPOSIT_APPROVAL_API), bộ chạy unit test | Playwright (6B-5) |
| A11 (C1) | Rẽ nhánh theo `status` ở 3 chỗ chuyển hướng + `BookingForm.onSuccess`; chỉ mở link khi `PENDING` và chưa quá `paymentLinkExpiresAt`, quá hạn thì gọi `retry-payment`. **Tạm thời** đơn `CONFIRMED` / `PENDING_APPROVAL` chuyển về trang lịch sử đặt sân có sẵn (`app/(customer)/bookings`) cho tới khi có A22 | — |
| A12 | Hook xử lý kết quả thanh toán: rẽ nhánh theo `bookingStatus`, hỏi lại đơn 2–3 giây/lần tới 60 giây, chọn loại thông điệp theo `responseCode`, khôi phục phiên bằng refresh cookie trước `RoleGuard` | Bố cục và chữ của từng ca trên `PaymentResult.tsx`, hai nút "Thanh toán lại" / "Hủy đơn" |
| A13, A14 | Quy tắc `canCancelBooking` / `canCancel` (mốc `+07:00`), nhãn trạng thái `PENDING_APPROVAL` trong bảng nhãn có sẵn | — |
| A15 | `calculateRefundInfo` tính trên số đã trả, mốc `+07:00` | — |
| A16, A17 | Hàm định dạng dùng chung (số đã cọc / còn lại theo `paymentStatus`, nhãn lý do theo `cancelReason`, người hủy theo `cancelSource` cho từng vai trò) | Hiện các trường đó trên `BookingCard`, `BookingDetailModal`, `BookingDetailDrawer` |
| A18, A19, A21, A22 | — | Lọc "Chờ duyệt cọc", mục menu hàng chờ, câu ở bước xác nhận, trang kết quả đặt sân mới (khi xong, A11 đổi đích từ trang lịch sử sang trang này) |
| A20 | Logic khóa slot đã qua theo giờ VN, tính lại khi trang mở lâu | — |
| A23 | Soát và sửa đọc chuỗi giờ BE ở các file liệt kê | — |
| A24 | Toàn bộ (`useRetryPayment` theo `code`) | — |
| A25 | Hook đồng hồ đếm ngược + tải lại khi focus / `BookingExpired` | Chỗ đặt đồng hồ trên từng màn |
| A26 | Quy tắc ẩn nút hủy, mã lỗi, tiền hoàn xem trước của trang hủy bằng link (có thể tách logic ra hook theo đúng quy ước "page mỏng") | Bố cục trang nếu đổi |
| A27 | Màu `PENDING_APPROVAL` trong `getStatusColor`, giá trị lọc mới | Thứ tự, chữ của lựa chọn lọc nếu muốn |
| A28 | Dùng hàm định dạng của A16/A17 | Hiện cọc / còn lại / người hủy trên modal |
| A29 | Toàn bộ (thông điệp + tải lại sau lỗi check-in) | — |
| A30 | Hàm thuần tính và kiểm hộp thoại duyệt + hook gửi (thử lại, xử lý 409, lỗi theo `errors`) | Hộp thoại, ô nhập, nút |
| A31 | Toàn bộ | — |
| A32 | Kiểm trên Vercel, thêm header nếu cần | — |
| Mã lỗi | Một bảng mã dùng chung + hàm trả loại xử lý (thông điệp, tải lại, đóng hộp thoại, về bước chọn giờ). Gồm mã Phase 6 (API Mục 5) **và** mã có sẵn mang nghĩa riêng trong luồng cọc: đặt sân `CONFLICT` 409 (đơn vừa bị nhân viên xử lý ⇒ mở lịch sử), `BAD_REQUEST` 400 (khung giờ đã bắt đầu), `SLOT_UNAVAILABLE`, `SLOT_UNAVAILABLE_NOTIFY_REGISTERED`; `FORBIDDEN` ở hàng chờ. Dọn mã không tồn tại ở hai `error-handling.ts` nếu muốn | Câu chữ hiển thị nếu muốn khác `message` của BE |

SignalR chỉ là best-effort (API Mục 6): mọi màn có dữ liệu cọc phải tự tải lại qua REST khi mở trang, khi kết nối lại và khi tab được focus, không chỉ dựa vào sự kiện (thuộc phần logic, A7 + A25).

Thứ tự đề xuất cho phần logic/API: A32 (kiểm nhanh, nếu Vercel đang hỏng thì mọi thứ khác vô nghĩa) ⇒ A11 (rủi ro đang chạy) ⇒ A1–A5, A31 ⇒ A8, A23 ⇒ A13–A15, A26 ⇒ mã lỗi, A24, A29 ⇒ A6, A7, A30 ⇒ A12, A25 ⇒ A20, A27 ⇒ MSW. Hai người đụng chung `booking.types.ts` (A1–A3): đẩy phần type lên `develop` sớm nhất để tránh xung đột.

### 4.5. Hợp đồng chính xác (đọc thẳng từ mã BE `develop` ngày 08/11)

Bảng dưới lấy từ mã C# (enum, DTO, controller, service), không chép từ tài liệu; lệch với DEPOSIT_APPROVAL_API thì theo bảng này và báo để sửa tài liệu. Viết type FE theo đúng tên khóa (camelCase), khóa `null` vẫn có mặt.

#### Quy ước JSON

| Thứ | Dạng trên dây | Ghi chú |
| :--- | :--- | :--- |
| Tên khóa | camelCase | `JsonNamingPolicy.CamelCase` |
| Enum trong DTO | **chuỗi tên** (`"PENDING_APPROVAL"`) | DTO khai trường `string`; BE **không** bật `JsonStringEnumConverter` toàn cục. Số (`10`) chỉ dùng khi FE gửi bộ lọc `status` lên query |
| `DateTime` (REST) | `"dd/MM/yyyy HH:mm:ss"` giờ VN, không offset | `VietnamDateTimeConverter`; đọc bằng `parseBackendDate` / `parseBackendDateVN` |
| Ngày đặt `bookingDate` | `BookingDto`, `CancelTokenInfoDto`: **`"dd/MM/yyyy 07:00:00"`**; `PendingApprovalBookingDto`: `"yyyy-MM-dd"` | BE dựng `DateOnly.ToDateTime(00:00)` (Kind không xác định), converter coi là UTC rồi cộng +7 ⇒ phần giờ luôn `07:00:00`, vô nghĩa. **Chỉ lấy phần ngày**, ghép với `startTime` của sân để ra mốc `+07:00`; không đưa cả chuỗi vào `parseBackendDateVN` để làm giờ chơi. `PendingApprovalBookingDto.bookingDate` là `DateOnly` nên ra `yyyy-MM-dd` |
| `TimeSpan` | `"HH:mm:ss"` | `startTime`, `endTime` |
| Thời gian trong SignalR | ISO 8601 UTC (`"…Z"`) | `AddSignalR()` dùng JSON mặc định, không qua converter giờ VN |
| Envelope | `{ success, code, message, data, errors }` | `errors`: từ điển khóa PascalCase (`DepositRate`, `Reason`) ⇒ mảng câu |

#### Giá trị enum (thứ tự số đúng như C#)

| Enum | Giá trị |
| :--- | :--- |
| `BookingStatus` | `PENDING`=0, `CONFIRMED`=1, `PAID_ONLINE`=2, `IN_PROGRESS`=3, `PENDING_PAYMENT`=4, `COMPLETED`=5, `CANCELLED`=6, `CANCELLED_PENDING_REFUND`=7, `CANCELLED_REFUNDED`=8, `NO_SHOW`=9, `PENDING_APPROVAL`=10 |
| `DepositSource` | `TADSS`, `MANAGER`, `FULL_PAYMENT_FALLBACK` |
| `CancelReason` (`BookingCancelReason`) | `PAYMENT_EXPIRED`, `APPROVAL_EXPIRED`, `APPROVAL_REJECTED`, `POLICY_REJECTED`, `CUSTOMER_CANCELLED`, `STAFF_CANCELLED`, `SYSTEM_CLEANUP` |
| `CancelSource` (`CancelSourceEnum`) | `LINK`, `STAFF`, `SYSTEM`, `CUSTOMER` |
| `paymentStatus` (`InvoicePaymentStatus`) | `UNPAID`, `PARTIALLY_PAID`, `PAID`, `REFUNDED`, `EXPIRED` |
| `source` | `ONLINE`, `WALK_IN` |
| Time grid `status` | `AVAILABLE`, `LOCKED`, `RESERVED`, `IN_USE` (`PENDING`, `PENDING_APPROVAL` ⇒ `LOCKED`; `CONFIRMED`, `PAID_ONLINE` ⇒ `RESERVED`) |

#### DTO (khóa đủ)

| DTO BE ⇒ type FE | Khóa |
| :--- | :--- |
| `BookingDto` (11 khóa mới) | `depositRate` number\|null, `depositAmount` number\|null, `remainingAmount` number\|null, `depositSource` string\|null, `approvalExpiresAt` string\|null, `approvalReasonCode` string\|null, `approvalReasons` string[]\|null, `cancelReason` string\|null, `cancelSource` string\|null, `cancelledByName` string\|null (chỉ endpoint nhân viên có giá trị), `cancelledAt` string\|null. Khóa cũ giữ nguyên (`expiresAt`, `refundAmount`, `paymentStatus`…) |
| `OnlineBookingResponse` (cả `POST /api/bookings/online` lẫn `retry-payment`) | `bookingId`, `bookingCode`, `status`, `paymentUrl`\|null, `expiresAt`\|null, `paymentLinkExpiresAt`\|null, `finalTotal`, `depositRate`\|null, `depositAmount`\|null, `remainingAmount`\|null, `depositSource`\|null, `approvalExpiresAt`\|null |
| `VnPayReturnResult` ⇒ `PaymentConfirmationDto` | `isSuccess`, `message`, `bookingId` **string**\|null, `bookingCode`\|null, `transactionRef`\|null, `amount`\|null, `responseCode`\|null, `bookingStatus`\|null |
| `CancelTokenInfoDto` | `bookingId`, `branchName`, `courtNames` string[], `bookingDate`, `startTime`, `endTime`, `refundAmount`, `refundPercent`, `status` (FE đang khớp) |
| `PagedResult<T>` | `items`, `page`, `pageSize`, `totalItems`, `totalPages`, `hasNext`, `hasPrev` (FE đã có `PagedResult<T>` ở `court/shared/types/court.types.ts`, nên chuyển lên `src/shared/types` khi dùng chung) |
| `PendingApprovalBookingDto` | `bookingId`, `bookingCode`, `branchId`, `branchName`, `customerId`\|null, `customerName`, `customerPhone`\|null, `isGuest`, `loyaltyTierName`\|null, `bookingDate` (`yyyy-MM-dd`), `startTime`, `endTime`, `courts` (`{ courtId, courtName }[]`), `finalTotal`, `approvalReasonCode`\|null, `approvalReasons` string[], `approvalExpiresAt`, `createdAt`, `note`\|null |
| `ApproveBookingDto` (gửi) | `depositRate` number (0…1, tối đa 4 chữ số thập phân), `reason` string |
| `RejectBookingDto` (gửi) | `reason` string |
| `BookingApprovalResultDto` | `bookingId`, `bookingCode`, `approvalResult` (`APPROVED` \| `REJECTED`), `status`, `finalTotal`, `depositRate`\|null, `depositAmount`\|null, `remainingAmount`\|null, `depositSource`\|null, `expiresAt`\|null, `resolvedBy`\|null, `resolvedAt`\|null |
| SignalR `BookingPendingApproval` | `bookingId`, `bookingCode`, `branchId`, `branchName`, `customerName`, `courtIds`, `status`, `approvalExpiresAt`\|null, `message`, `timestamp` |
| SignalR `BookingApprovalResolved` | `bookingId`, `bookingCode`, `branchId`, `approvalResult`, `status`, `message`, `timestamp` |
| SignalR `PendingApprovalQueueChanged` | `branchId`, `bookingId`, `change` (`ADDED` \| `REMOVED`), `reason` (`NEW` \| `APPROVED` \| `REJECTED` \| `EXPIRED` \| `CANCELLED`), `timestamp` |
| SignalR `BookingCreated` / `BookingCancelled` / `BookingExpired` (có sẵn, `BookingNotificationDto`) | `bookingId`, `customerId`, `customerName`, `branchId`, `branchName`, `courtIds`, `status` (nay là trạng thái cuối: `PENDING` / `PENDING_APPROVAL` / `CONFIRMED`), `message`, `timestamp`. Không mang trường cọc / hủy ⇒ tải lại đơn |

#### Endpoint của luồng cọc và mã lỗi thật

| Endpoint | Thành công | Lỗi FE phải rẽ nhánh (`HTTP code`) |
| :--- | :--- | :--- |
| `POST /api/bookings/online` (CUSTOMER, giới hạn 20 lần/phút) | **201**, `OnlineBookingResponse`; `message` theo `status` | 400 `BOOKING_REJECTED` (đơn vẫn có trong lịch sử, `POLICY_REJECTED`); 400 `SLOT_UNAVAILABLE`, 400 `SLOT_UNAVAILABLE_NOTIFY_REGISTERED` (đã tự đăng ký báo khi trống); 400 `BAD_REQUEST` (khung giờ đã bắt đầu, khuyến mãi không hợp lệ…, hiện `message`); 409 `CONFLICT` (đơn vừa bị nhân viên xử lý ⇒ mở lịch sử); 401/403 không envelope ⇒ đăng nhập; 429 ⇒ chờ rồi thử lại |
| `POST /api/me/bookings/{id}/retry-payment` | 200, `OnlineBookingResponse` | 400 `PAYMENT_HOLD_EXPIRED` (hai câu); 400 `BAD_REQUEST` (không còn `PENDING` / quá hạn thanh toán); 409 `CONFLICT`; 404 `NOT_FOUND`; 403 `FORBIDDEN` |
| `POST /api/payments/vnpay/confirm` | 200, `VnPayReturnResult` (kể cả khi `isSuccess = false`) | Rẽ nhánh theo `bookingStatus` + `responseCode`, không theo HTTP |
| `GET /api/me/bookings/{id}` | 200, `BookingDto` (`cancelledByName` luôn `null`) | 404 |
| `POST /api/me/bookings/{id}/cancel` | 200, không `data` | 400 `BOOKING_ALREADY_STARTED`; 400 `BAD_REQUEST` (trạng thái không hủy được); 403 `ACCOUNT_LOCKED` |
| `GET /api/bookings/cancel/{token}` (không cần đăng nhập) | 200, `CancelTokenInfoDto` | 404 `NOT_FOUND` (link sai hoặc hết hạn); 403 `ACCOUNT_LOCKED` |
| `POST /api/bookings/cancel/{token}` | 200, không `data` | 400 `BOOKING_ALREADY_STARTED`; 400 `BAD_REQUEST` (link đã dùng, hết hạn, trạng thái không hủy được); 404; 403 `ACCOUNT_LOCKED` |
| `GET /api/bookings/pending-approval?branchId=&page=&pageSize=` (OWNER, BRANCH_MANAGER) | 200, `PagedResult<PendingApprovalBookingDto>`; `pageSize` 1…50 | 403 `FORBIDDEN` (Manager truyền chi nhánh khác) |
| `POST /api/bookings/{bookingId}/approve` | 200, `BookingApprovalResultDto`; `message` theo kết quả | 400 `VALIDATION_ERROR` + `errors.DepositRate` / `errors.Reason`; 403 `FORBIDDEN`; 404 `NOT_FOUND`; 409 `BOOKING_NOT_PENDING_APPROVAL`, `APPROVAL_ALREADY_RESOLVED`, `APPROVAL_EXPIRED`, `APPROVAL_DECISION_NOT_FOUND`; 503 `TADSS_UNAVAILABLE` (thử lại đúng nội dung); 500 `INTERNAL_ERROR` |
| `POST /api/bookings/{bookingId}/reject` | 200, `BookingApprovalResultDto` | Như approve, `errors` chỉ có `Reason` |
| `POST /api/bookings/{id}/check-in` | 200 | 400 `BAD_REQUEST` ("Quá sớm để check-in", "Đã quá thời gian check-in", hoặc trạng thái không check-in được, gồm `NO_SHOW`); 409 `CONFLICT` ("Đơn đã được xử lý bởi người khác") |
| `POST /api/bookings/{id}/cancel` (nhân viên) | 200 | 400 `BAD_REQUEST` (trạng thái không hủy được) |

#### Công thức phải giống BE

| Việc | Công thức BE | Cách viết ở FE |
| :--- | :--- | :--- |
| Tỷ lệ cọc hợp lệ | `0 ≤ rate ≤ 1`, đúng tối đa 4 chữ số thập phân (`DepositApprovalRules.IsValidRate`) | Nhập % tối đa 2 chữ số thập phân; đổi bằng số nguyên: `basisPoints = Math.round(percent * 100)`, gửi `depositRate = basisPoints / 10000` |
| Lý do | 5…500 **code point**, không chỉ khoảng trắng (`IsValidReason`) | Đếm bằng `[...reason].length` (không dùng `reason.length`: emoji tính 2), `reason.trim() !== ""` |
| Số tiền cọc khi duyệt | `Math.Round(finalTotal × rate, 0, AwayFromZero)` (`BookingApprovalService`) | `Math.round(finalTotal * basisPoints / 10000)` (số dương nên làm tròn nửa lên trùng `AwayFromZero`; nhân số nguyên trước để tránh lỗi số thực) |
| Mức tối thiểu khi duyệt | Số tiền > 0 mà < `MinOnlinePaymentAmount` (cấu hình, hiện 5.000 đ) ⇒ 400, `errors.DepositRate` = "Số tiền cọc tối thiểu 5.000 đ, hoặc chọn 0 %" | Báo trước bằng hằng 5.000, vẫn hiện lỗi BE nếu cấu hình khác |
| % hoàn khi hủy | Mốc có `hoursBefore` **lớn nhất** ≤ số giờ còn tới giờ chơi (giờ VN); đã qua giờ chơi ⇒ 0 (`CancellationPolicy.RefundPercent`). `GET /api/cancel-policies` trả sắp giảm dần | Tự sắp hoặc lọc lấy max, không dựa vào thứ tự mảng |
| Tiền hoàn | Mỗi Payment `SUCCESS`: `round((amount − refundedAmount) × % / 100)`, cộng lại (`QuoteRefund`) | Xem trước ≈ `Math.round(sốĐãTrả × % / 100)` với số đã trả ở A15; có thể lệch 1 đ do BE làm tròn theo từng Payment; số thật là số BE ghi |
| Đã tới giờ chơi | `now ≥ bookingDate + startTime sớm nhất` theo giờ VN (`CancellationPolicy.HasStarted`) | Mốc `+07:00` từ `bookingDate` và `startTime` của sân đầu tiên |
| Còn phải thu khi check-out | `finalTotal hiện tại − tổng Payment SUCCESS` (không âm), bằng `remainingAmount` | Hiện `remainingAmount`, không tự trừ |

#### Ai được hủy (BE `BookingCancellationService`)

| Ai | Trạng thái hủy được | Giờ |
| :--- | :--- | :--- |
| Khách trong app | `PENDING`, `PENDING_APPROVAL`, `CONFIRMED`, `PAID_ONLINE` | Trước giờ bắt đầu |
| Link trong email | `CONFIRMED`, `PAID_ONLINE` | Trước giờ bắt đầu |
| Nhân viên / Manager / Owner | `PENDING`, `PENDING_APPROVAL`, `CONFIRMED`, `PAID_ONLINE` | Cả sau giờ bắt đầu |

## 5. Checklist theo gói

Mỗi gói nên có plan riêng `spec/PHASE6_PLAN_6B-x_*.md` như các gói 6A (mục kết quả ở cuối plan).

| Gói | Tuần | Việc (chi tiết ở spec) | Xong khi |
| :--- | :---: | :--- | :--- |
| **6B-1** Nền | W09 | Spec 8.1: type — **type FE hiện chưa có trường nào của Phase 6**, danh sách đủ ở spec 8.1 (`PENDING_APPROVAL = 10`, `DepositSource`, trường cọc, `cancelReason` 7 giá trị, `cancelSource`, `cancelledByName`, `cancelledAt`, `paymentLinkExpiresAt`, `PaymentConfirmationDto` thêm `responseCode` + `bookingStatus`); `booking-approval.api.ts`; 3 sự kiện SignalR vào `signalr-events.ts` + `NotificationDispatcher`; `getTodayVN()`, `parseBackendDateVN()` và thay khoảng 20 chỗ `toISOString().split("T")[0]` (T-7), đọc ngày `dd/MM` đúng (T-8); MSW; thiết kế UI Việc theo file: A1–A10 (Mục 4.1), A23, A31, A32 | Mock khớp ví dụ DEPOSIT_APPROVAL_API; FE-12 (phần hàm giờ) đạt; thiết kế được duyệt |
| **6B-2** Khách | W10 | Spec 8.2: màn xác nhận, trang kết quả đặt sân (mới), trang kết quả thanh toán, lịch sử / chi tiết, xem trước hoàn tiền, ẩn nút hủy từ giờ bắt đầu, nhãn lý do hủy, sửa C1. Việc theo file: A11–A13, A15, A16, A20–A22, A24–A26 | Rẽ nhánh đúng theo `status`, `bookingStatus`, `code`; FE-11, FE-12 đạt |
| **6B-3** Manager/Owner | W11 | Spec 8.3: trang hàng chờ (hai vai trò, dùng chung `shared/`), hộp thoại duyệt/từ chối, badge menu, realtime, đồng hồ hạn duyệt. Việc theo file: A6, A19, A30 | Mọi mã lỗi ở DEPOSIT_APPROVAL_API Mục 5 có thông điệp |
| **6B-4** Staff | W11 | Spec 8.4: nhãn trạng thái mới ở danh sách/lịch/time grid, lọc "Chờ duyệt cọc", hủy đơn chờ duyệt, check-out hiện cọc / đã trả / `remainingAmount`, người hủy theo `cancelSource` + `cancelledByName`, thời điểm `cancelledAt`. Việc theo file: A14, A17, A18, A27–A29 | — |
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
| 17 | Lấy cả chuỗi `bookingDate` (`"dd/MM/yyyy 07:00:00"`) làm thời điểm, hoặc so `bookingDate` với "hôm nay" theo giờ máy | Phần giờ `07:00:00` là sản phẩm phụ của converter, không phải giờ chơi: chỉ lấy `dd/MM/yyyy`, ghép `startTime` (`HH:mm:ss`) thành mốc `+07:00`; so ngày bằng `getTodayVN()` | 4.5 (quy ước JSON) |
| 18 | Gửi `depositRate = percent / 100` bằng số thực rồi tin BE làm tròn | BE **từ chối** (không làm tròn) tỷ lệ quá 4 chữ số thập phân: nhập 7,15 % thì `7.15 / 100` trong JS là `0.07150000000000001` (57,7 % ⇒ `0.5770000000000001`) ⇒ 400 `VALIDATION_ERROR`. Đổi qua số nguyên như 4.5: `Math.round(7.15 * 100) / 10000` = `0.0715` (đã thử bằng Node 08/11) | 4.5 (công thức) |
| 19 | Đếm độ dài lý do bằng `reason.length` | BE đếm code point (emoji tính 1, `reason.length` tính 2) ⇒ dùng `[...reason].length` | 4.5 (công thức) |

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
