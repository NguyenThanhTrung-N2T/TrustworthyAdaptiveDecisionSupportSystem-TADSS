# PHASE 6 — HƯỚNG DẪN CHO NGƯỜI LÀM FE (EPIC 6B)

| | |
| :--- | :--- |
| **Dành cho** | Người làm `smashcourt-fe` ở Epic 6B (Phase 6, W09–W12, 02/11 – 29/11/2026) |
| **Trạng thái (23/11)** | BE xong và đang chạy trên bản deploy (hợp đồng API không đổi từ 31/10, trừ P6-61 và P6-62 (28/11, giá phải là số đồng nguyên) ở Mục 7). FE `develop` `345e89e`: phần lớn Phase 6 đã làm (Mục 5), CI xanh (lint, `tsc`, unit test 184 ca, build). **Còn 14 việc ở Mục 4**, soát thẳng trên mã `develop` ngày 23/11 |
| **Hạn** | **Gate M6: 29/11** (FE build + lint + Playwright trên mock đạt, không còn việc tính năng mở). Phase 7 (30/11 – 06/12) ghép FE ↔ BE ↔ TADSS thật, **feature freeze 06/12**: sau mốc này chỉ sửa lỗi |
| **Chia việc** | Từ 23/11 toàn bộ việc ở Mục 4 do người làm FE làm, cả logic lẫn giao diện. Người giữ BE trả lời câu hỏi hợp đồng và review |
| **Vai trò file** | Điểm vào duy nhất cho FE: việc còn lại (Mục 4), đã xong (Mục 5), cách chạy, hợp đồng đọc từ mã BE (Mục 7), bẫy (Mục 8). Chi tiết hành vi từng màn ở spec Phase 6 Mục 8; lệch nhau thì theo mã BE và báo để sửa file này |

---

## 1. Bắt đầu nhanh

1. Đọc Mục 3 (nghiệp vụ, 5 dòng) và Mục 8 (bẫy).
2. Làm Mục 4 theo thứ tự trong bảng. Mỗi việc có file, hiện trạng, cách làm và điều kiện "xong".
3. Trước mỗi lần push chạy đủ bốn lệnh ở Mục 4.3; CI chạy đúng các lệnh đó.
4. Không chắc một trường, mã lỗi hay công thức thì tra Mục 7 (đọc từ mã C#), đừng đoán theo một lần gọi API.

## 2. Đọc gì, theo thứ tự nào

| # | Tài liệu | Đọc phần nào | Để làm gì |
| :---: | :--- | :--- | :--- |
| 1 | File này | Toàn bộ | Việc còn lại, hợp đồng, bẫy |
| 2 | [`PHASE6_DESIGN_SPEC.md`](PHASE6_DESIGN_SPEC.md) | Mục 8.1–8.5 (hành vi từng màn); Mục 9.1 dòng FE-11, FE-12 | Khi cần biết một màn phải chạy thế nào |
| 3 | [`SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md`](../../SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md) | Mục 2–7, nhất là **Mục 5 (mã lỗi)** | Ví dụ JSON để chép vào handler MSW |
| 4 | [`docs/research/E2E_TEST_SPECIFICATION.md`](../docs/research/E2E_TEST_SPECIFICATION.md) | Mục 2.1 (chạy trên MSW), Mục 5 (E2E-01…14) | Kịch bản Playwright (việc V11) |
| 5 | [`docs/report/USE_CASES.md`](../docs/report/USE_CASES.md) Mục 5, [`docs/report/ACTIVITY_AND_STATE_DIAGRAMS.md`](../docs/report/ACTIVITY_AND_STATE_DIAGRAMS.md) | UC-DEPOSIT-01…07, sơ đồ trạng thái booking | Hiểu luồng |
| 6 | `smashcourt-fe/CLAUDE.md` | Toàn bộ | Quy ước đặt file, tên, gọi API, thư mục test |

Không cần đọc: các plan `archive/phase6/PHASE6_PLAN_6A-*.md` (việc của BE), hợp đồng BE ↔ TADSS (FE không gọi TADSS).

## 3. Nghiệp vụ trong 5 dòng

1. Khách đặt online ⇒ BE hỏi TADSS ⇒ đơn ra một trong ba trạng thái: `PENDING` (phải trả cọc qua VNPay trước `expiresAt`), `CONFIRMED` (cọc 0 đ hoặc quá nhỏ, thu tại quầy, không có link), `PENDING_APPROVAL` (chờ Manager duyệt trước `approvalExpiresAt`). TADSS từ chối ⇒ 400 `BOOKING_REJECTED`; đơn **vẫn có** trong lịch sử ở trạng thái `CANCELLED`, `cancelReason = POLICY_REJECTED`.
2. Manager/Owner duyệt (nhập % cọc) hoặc từ chối đơn chờ duyệt. Duyệt xong đơn thành `PENDING`; khách lấy link bằng `retry-payment` (email không chứa link VNPay).
3. Thanh toán thất bại không hủy đơn: hạn rút còn tối đa 5 phút, khách thanh toán lại hoặc hủy. Quá hạn ⇒ job hủy (`cancelReason = PAYMENT_EXPIRED`).
4. Check-out: nhân viên thu `remainingAmount` (tổng trừ số đã trả online).
5. Mọi sự cố phía TADSS ⇒ cọc 100 %; FE không cần biết TADSS, chỉ rẽ nhánh theo `status` và `code` BE trả.

## 4. Việc còn lại (soát mã `develop` `345e89e` ngày 23/11)

Số dòng là tại `345e89e`, có thể xê dịch. **P1** = người dùng thấy sai, làm trước; **P2** = điều kiện Gate M6; **P3** = nên làm nếu kịp trước 06/12.

### 4.1. Bảng việc

| # | Ưu tiên | Việc | File và hiện trạng | Làm thế nào | Xong khi |
| :---: | :---: | :--- | :--- | :--- | :--- |
| V1 | P1 | Header ngrok (A32) | `src/api/core/apiClient.ts`, lời gọi refresh ở `src/api/upload.api.ts`, `src/lib/signalr-client.ts`: không chỗ nào gửi `ngrok-skip-browser-warning` | Mở FE trên Vercel trỏ vào BE qua ngrok, xem tab Network: request API trả JSON hay trang cảnh báo HTML. Bị chặn thì thêm header `ngrok-skip-browser-warning: true` ở `apiClient`, lời gọi refresh và `headers` của `withUrl` SignalR (CORS BE đã `AllowAnyHeader`) | Mọi request từ Vercel nhận JSON, SignalR kết nối được |
| V2 | P1 | Nhãn lý do hủy, người hủy, nguồn cọc ở màn nhân viên dùng mã BE không có | `src/features/booking/shared/utils/bookingStatus.ts`: `getCancelReasonText` (dòng 230) dò `CUSTOMER_REQUEST`, `PAYMENT_TIMEOUT`, `APPROVAL_TIMEOUT`, `FORCE_MAJEURE`, `STAFF_CORRECTION`; `getCancelSourceText` (214) thiếu `LINK`, thừa `ADMIN`; `getDepositSourceText` (250) dò `MANUAL`, `POLICY`. Kết quả: ngăn chi tiết và modal sân của nhân viên hiện mã thô (`PAYMENT_EXPIRED`, `MANAGER`…) | Viết lại theo đúng enum BE ở Mục 7: `CancelReason` 7 giá trị, `CancelSource` 4 giá trị, `DepositSource` 3 giá trị (`TADSS`, `MANAGER`, `FULL_PAYMENT_FALLBACK`). Bảng nhãn lý do của khách ở `customer/utils/bookingStatus.ts` (dòng 66–100) đã đúng: chuyển phần dùng chung lên `shared`, chỉ khác câu người hủy theo vai trò (bẫy 9). Gõ tham số bằng type `CancelReason` / `CancelSource` / `DepositSource` để `tsc` bắt mã sai | Unit test: mọi giá trị enum có nhãn tiếng Việt, không giá trị nào rơi về mã thô |
| V3 | P1 | "Cọc phải trả" hay "đã cọc", tỷ lệ cọc hiện sai (bẫy 15, A17, A28) | `shared/components/management/BookingDetailDrawer.tsx` (312–333) và `court/staff/components/BookingDetailModal.tsx` (230–246): luôn ghi "Tiền cọc" + "Còn lại cần thu tại quầy" bất kể `paymentStatus`; tỷ lệ hiện bằng `Math.round(depositRate * 100)` nên `0.0715` thành "7%" | Một hàm thuần dùng chung trả các dòng tiền theo `paymentStatus`: `UNPAID` ⇒ "Cọc phải trả" (chưa thu đồng nào, không ghi "còn lại tại quầy" như đã cọc); khác `UNPAID` ⇒ "Đã cọc" + "Còn phải thu khi check-out" = `remainingAmount` (không tự trừ). Tỷ lệ hiện tối đa 2 chữ số thập phân (`7,15 %`). Hai màn gọi chung hàm | Unit test các ca `UNPAID` / `PARTIALLY_PAID` / `PAID`, `FULL_PAYMENT_FALLBACK` (cọc = tổng, còn 0), tỷ lệ `0.0715` |
| V4 | P1 | Xem trước tiền hoàn (A15, bẫy 14) | `customer/utils/bookingStatus.ts` `calculateRefundInfo` (164), gọi ở `booking-history/BookingDetailModal.tsx` (khoảng 503) và `CancelBookingModal.tsx`: chọn mốc bằng `find` đầu tiên thỏa (đúng chỉ khi mảng đã sắp giảm dần); không tải được chính sách thì dùng bảng 100/50/25 % viết cứng; tính trên `totalAmount` (tổng đơn) | Tính trên **số đã trả**: `PAID_ONLINE` ⇒ `depositAmount` (đơn online cũ có `depositAmount = null` ⇒ `finalTotal`); `PENDING`, `PENDING_APPROVAL`, `CONFIRMED` online ⇒ 0. Chọn mốc có `hoursBefore` lớn nhất ≤ số giờ còn lại, không phụ thuộc thứ tự mảng; đã tới giờ chơi ⇒ 0 %. Bỏ bảng viết cứng: không tải được chính sách thì không hiện số xem trước. Công thức ở Mục 7 | Unit test (thêm vào `src/tests/features/booking/customer/utils/bookingStatus.test.ts`): mảng xáo thứ tự, đơn chưa trả, đơn cũ `depositAmount = null`, đúng mốc giờ |
| V5 | P1 | `retry-payment` xử lý lỗi theo `code` (A24, bẫy 8) | `customer/hooks/useRetryPayment.ts` (12–47): đoán lỗi bằng chữ trong `message` ("expired", "đã hủy"…) | Theo `code` (Mục 7, `retry-payment`): 400 `PAYMENT_HOLD_EXPIRED` (hai câu cùng mã) ⇒ hiện `message`, tải lại đơn, gợi ý đặt lại; 400 `BAD_REQUEST` (đơn không còn `PENDING` hoặc đã quá hạn) ⇒ "Đơn đã hết hạn, vui lòng đặt lại" + nút về `/bookings/new`, tải lại đơn; 409 `CONFLICT` ⇒ tải lại đơn; 404, 403 ⇒ `message`. Bỏ hẳn phần dò chữ | Unit test hàm chọn cách xử lý theo `code` (hàm thuần, tách khỏi hook) |
| V6 | P1 | Giờ đặt quá 12 giờ, lỗi kiểm dữ liệu khi đặt sân (P6-61) | Kéo chọn giờ ở `shared/components/new/InteractiveTimeGrid.tsx` / `CourtTrack.tsx` (`onSlotsChange`, dùng ở `CourtAndTimeSelectionStep` của khách và `WalkInBookingWorkspace` tại quầy) không giới hạn độ dài; kiểm form tại quầy (`WalkInBookingWorkspace.tsx` 234–237) chỉ kiểm kết thúc sau bắt đầu; `customer/hooks/useBookingSubmit.ts` (85–117) gặp 400 `VALIDATION_ERROR` chỉ hiện `message` chung, bỏ qua `errors` | Giới hạn mỗi lượt kéo tối đa 12 giờ (24 ô 30 phút), báo ngay khi chạm giới hạn; form tại quầy thêm cùng điều kiện. Gặp `VALIDATION_ERROR` thì hiện câu trong `err.errors` (BE để câu ở khóa `Courts`). Ở cùng chỗ: `SLOT_UNAVAILABLE_NOTIFY_REGISTERED` báo "đã đăng ký nhận thông báo khi sân trống", 429 báo chờ rồi thử lại | Kéo quá 12 giờ không được; gửi tay một đơn 12 giờ 30 phút (sửa request) thấy câu của BE |
| V7 | P1 | Check-in đúng lúc job vừa đánh `NO_SHOW` (A29) | `shared/hooks/useBookingManagement.ts` (178): bắt 409 nhưng ca 400 (trạng thái `NO_SHOW` không check-in được) chỉ bắt khi `message` chứa chữ `NO_SHOW`; dò mã `BOOKING_ALREADY_NO_SHOW` BE không có | 409 `CONFLICT` và 400 `BAD_REQUEST` của check-in đều tải lại danh sách và chi tiết đơn; câu hiển thị lấy `message` của BE ("Quá sớm để check-in", "Đã quá thời gian check-in"…), riêng 409 thêm "vui lòng tải lại". Bỏ mã không tồn tại và phần dò chữ. Áp cùng cách cho nút check-in ở `court/staff/components/BookingDetailModal.tsx` | Thử trên MSW: 400 và 409 đều tải lại danh sách |
| V8 | P2 | Toast cho 3 sự kiện duyệt cọc (A7) | `features/notifications/NotificationDispatcher.tsx` (20–34): `BookingPendingApproval`, `BookingApprovalResolved`, `PendingApprovalQueueChanged` chưa đăng ký (mới chỉ được nghe riêng ở `BookingResultPage` và `usePendingApprovals`) | Đăng ký theo cơ chế toast có sẵn: Manager/Owner nhận `BookingPendingApproval` ("Có đơn chờ duyệt cọc"); khách nhận `BookingApprovalResolved` (đã duyệt ⇒ "Thanh toán cọc để giữ chỗ", từ chối ⇒ "Đơn không được nhận"); `PendingApprovalQueueChanged` chỉ làm mới, không toast. Payload ở Mục 7 | Mở hai tab (Manager, khách) trên MSW hoặc BE thật, duyệt một đơn: hai bên đều thấy toast và dữ liệu mới |
| V9 | P2 | Badge số đơn chờ duyệt trên menu (A19) | `src/layouts/manager/navigation/manager.navigation.ts` (35), `owner/navigation/owner.navigation.ts` (47): đã có mục `/…/booking-approvals`, chưa có badge | Badge = `totalItems` của `GET /api/bookings/pending-approval?pageSize=1`; tải lại khi nhận `PendingApprovalQueueChanged`, khi kết nối lại và khi tab được focus | Badge đổi số ngay khi duyệt / từ chối / hết hạn một đơn |
| V10 | P2 | MSW (A10) | `package.json` chưa có MSW | Cài MSW, handler chép từ ví dụ JSON của DEPOSIT_APPROVAL_API; đủ các ca bắt buộc ở Mục 6.1. Bật bằng một biến môi trường riêng, mặc định tắt (bản build production không được nạp MSW) | `npm run dev` với biến bật chạy được mọi màn Phase 6 không cần BE |
| V11 | P2 | Playwright E2E-01…14 (6B-5) | Chưa có | Kịch bản ở E2E_TEST_SPECIFICATION Mục 5, chạy trên MSW (V10). Thêm lệnh `npm run test:e2e` và bước CI chạy trên mock | **Gate M6**: 14 kịch bản đạt trên CI |
| V12 | P3 | Gộp đồng hồ đếm ngược (A25) | Ba bản tự viết, đều đọc đúng `parseBackendDateVN`: `booking-result/BookingResultPage.tsx` (184–222), `booking-history/BookingCard.tsx` (36–46), `booking-approval/shared/components/PendingApprovalTable.tsx` (34–60) | Một hook dùng chung: đọc `expiresAt` / `approvalExpiresAt`, về 0 với đơn chờ duyệt thì hiện "Đang chờ chi nhánh phản hồi…" và tải lại sau 60 giây hoặc khi nhận `BookingExpired` (bẫy 10); tải lại khi tab được focus (`visibilitychange`), kể cả ở trang lịch sử | Ba màn dùng chung hook, hành vi không đổi |
| V13 | P3 | Dọn mã chết dễ gây lỗi | `shared/utils/bookingStatus.ts` `formatDate` (196), `formatDateTime` (204) đọc chuỗi BE bằng `new Date()` kiểu Mỹ (đúng lỗi T-8), hiện không file nào gọi; `fetchServiceById` ở `src/api/service.api.ts` (41) gọi endpoint BE không có; hai `error-handling.ts` (`features/customer/shared/utils`, `features/branch/owner/utils`) có mã BE không bao giờ trả (`CUSTOMER_NOT_FOUND`, `BOOKING_NOT_FOUND`, `INSUFFICIENT_PERMISSIONS`…; BE trả `NOT_FOUND`, `FORBIDDEN`) | Xóa hai hàm định dạng và `fetchServiceById`; sửa bảng mã theo `SmashCourt-BE/Common/ErrorCodes.cs` | `tsc`, `npm run build` xanh |
| V14 | P1 | Phát hành lên Vercel | `master` FE vẫn là mã 31/10 (`426e680`): bản production **chưa có** bản sửa C1, lỗi giờ và màn Phase 6. Nếu production trỏ vào bản deploy (BE Phase 6), đơn `CONFIRMED` không có `paymentUrl` mở ra trang hỏng (bẫy 1) | Xong V1–V7 ⇒ người giữ repo tạo pull request `develop` → `master` (CI đạt mới triển khai, Mục 9) | Vercel chạy mã mới; đặt thử một đơn trên production ra đúng trang kết quả |

### 4.2. Thứ tự

V1 (kiểm nhanh, nếu Vercel bị ngrok chặn thì mọi thứ khác vô nghĩa) ⇒ V2, V3 (màn nhân viên đang hiện sai) ⇒ V4, V5, V6, V7 ⇒ V14 (phát hành) ⇒ V10, V11 (Gate M6, 29/11) ⇒ V8, V9 ⇒ V12, V13. V10 cần làm sớm song song vì V11 phụ thuộc nó.

### 4.3. Quy ước khi sửa

- Logic (chọn nhãn, tính tiền, chọn cách xử lý theo `code`) viết thành **hàm thuần** trong `utils/` của feature, component và hook chỉ gọi; mỗi hàm có unit test.
- Unit test đặt ở `src/tests/`, phản chiếu cây `src/` (ví dụ `src/features/booking/shared/utils/bookingStatus.ts` ⇒ `src/tests/features/booking/shared/utils/bookingStatus.test.ts`), viết bằng `node:test` + `node:assert/strict` như ba file có sẵn. Hàm có ngày giờ bọc trong `describeEachTimeZone` (chạy dưới 4 múi giờ máy) và cố định "bây giờ" bằng `atInstant` (`src/tests/clock.ts`). Bộ chạy `src/tests/run.mjs` tự tìm mọi `*.test.ts`.
- Mọi chuỗi giờ BE trả đọc qua `src/shared/utils/date.ts` (`parseBackendDate`, `parseBackendDateVN`, `formatDate`, `formatDateTime`); "hôm nay" và cộng trừ ngày dùng `getTodayVN`, `toDateKeyVN`, `addDaysToDateKey`. Không `new Date(chuỗiBE)`, không `toISOString().split("T")[0]`.
- Rẽ nhánh theo `code` (và `errors` khi `VALIDATION_ERROR`), không dò chữ trong `message`.
- Gói mới chỉ thêm khi cần (MSW, Playwright); ghim phiên bản, commit `package-lock.json` một lần cùng commit thêm gói, không chạy lại `npm install` làm lockfile đổi lung tung.
- Trước khi push, chạy đủ (CI chạy đúng bốn lệnh này):

```bash
npm run lint
npx tsc --noEmit -p .
npm test          # unit test node:test ở src/tests/
npm run build
```

## 5. Đã xong (đừng làm lại)

Mã A-xx là tên việc ở bản 31/10 của file này (tra lại trong lịch sử git nếu cần chi tiết); lỗi C-, N-, R-, T- là mã ở spec Phase 6.

| Việc | Nội dung | Ở đâu |
| :--- | :--- | :--- |
| A1–A5, A31 | Type Phase 6: `PENDING_APPROVAL = 10`, 11 trường cọc / hủy của `BookingDto`, `OnlineBookingResponse` 12 khóa dùng chung cho `retry-payment`, `PaymentConfirmationDto`, time grid `RESERVED` | `booking/shared/types/booking.types.ts`, `payment.types.ts` |
| A6, A30 | API và hàng chờ duyệt, hộp thoại duyệt / từ chối (tỷ lệ 4 chữ số thập phân, lý do theo code point, 503 thử lại đúng nội dung, 409 đóng hộp thoại) | `src/api/booking-approval.api.ts`, `features/booking-approval/` |
| A7 (một phần) | Hằng 3 sự kiện mới; hàng chờ và trang kết quả tự nghe | `src/lib/signalr-events.ts`; phần toast chung còn ở V8 |
| A8, A23, A20 | Lỗi giờ T-7, T-8: "hôm nay" theo giờ VN, thay 22 chỗ `toISOString().split("T")[0]`, đọc chuỗi BE ở panel AI, `ChatPanel`, ngày gán quản lý, phiên đăng nhập, ô chọn khuyến mãi, dashboard; `CourtTrack` khóa slot đã qua theo giờ VN, tính lại mỗi 30 giây | `src/shared/utils/date.ts`, `dateFormatter.ts`, `CourtTrack.tsx` |
| A11 (C1), A22, A21 | Không mở `paymentUrl` vô điều kiện; trang kết quả đặt sân rẽ nhánh `PENDING` / `CONFIRMED` / `PENDING_APPROVAL`; câu ở bước xác nhận | `useBookingSubmit.ts`, `booking-result/BookingResultPage.tsx` |
| A12 (N-36) | Trang kết quả thanh toán rẽ nhánh theo `bookingStatus`, hỏi lại đơn khi IPN chưa về, câu theo `responseCode` | `customer/components/shared/PaymentResult.tsx` |
| A13, A14 (T-4) | Ai hủy được, ẩn nút hủy từ giờ bắt đầu theo giờ VN, nhãn `PENDING_APPROVAL` | `customer/utils/bookingStatus.ts`, `shared/utils/bookingStatus.ts`, `bookingRules.ts` |
| A16, A26, A27 | Lịch sử / chi tiết của khách hiện cọc, lý do và người hủy đúng mã BE; trang hủy bằng link dùng `refundAmount` BE trả, bắt `BOOKING_ALREADY_STARTED`; màu và lọc `PENDING_APPROVAL` | `booking-history/`, `app/booking/cancel/page.tsx`, `BookingScheduleView.tsx`, `BookingFilters.tsx` |
| A17, A18, A28 (một phần) | Ngăn chi tiết nhân viên có mức cọc, lý do cần duyệt, mục hủy; lọc "Chờ duyệt cọc" | phần còn lại ở V2, V3 |
| A9 | Một biến `NEXT_PUBLIC_API_BASE_URL` cho REST và SignalR | `.env.example`, `signalr-client.ts` |
| Unit test (FE-12, phần hàm giờ) | 184 ca `node:test`, mỗi ca chạy dưới 4 múi giờ máy (UTC−5…UTC+14): `date.test.ts`, `dateFormatter.test.ts`, `bookingStatus.test.ts`; bước "Unit test" của CI. Phía BE `BookingDateTimeRoundTripTests` gửi request đúng dạng FE qua PostgreSQL rồi đọc lại, khóa định dạng ngày giờ hai bên | `src/tests/` |

## 6. Cách chạy

### 6.1. Trên MSW (cho V10, V11; không cần BE)

Handler trả đúng ví dụ của DEPOSIT_APPROVAL_API, mỗi ca biên một response. Ca bắt buộc: đơn `PENDING` có link, `CONFIRMED` cọc 0 đ, `PENDING_APPROVAL`; `BOOKING_REJECTED`; đơn bị job hủy; đơn đã hủy với mỗi `cancelSource` (`CUSTOMER`, `LINK`, `STAFF` có và không có `cancelledByName`, `SYSTEM`); đơn hủy cũ `cancelReason = null`; cùng một đơn `STAFF` trả cho endpoint nhân viên (có tên) và endpoint khách (tên `null`); `confirm` thành công mà đơn `CANCELLED_PENDING_REFUND`; `PENDING` vài giây rồi `PAID_ONLINE`; thất bại mã `24` và `79`; 503 `TADSS_UNAVAILABLE` rồi 200; một đơn `depositRate = 0.3` (P6-59: bản deploy giảm cọc giờ thấp điểm cho khách Bronze / Silver sạch lịch sử); một đơn `depositRate = 0.0715` (V3); `VALIDATION_ERROR` có `errors.Courts` (V6).

### 6.2. Với BE thật (khi cần thử nối)

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
| FE trỏ tới BE | `.env` của FE: `NEXT_PUBLIC_API_BASE_URL=http://localhost:8080` (BE chạy Docker) hoặc `http://localhost:5179` (`dotnet run`). Một biến cho cả REST lẫn SignalR hub |
| Cổng FE | Phải là `3000`: CORS của BE chỉ cho `http://localhost:3000` và tên miền Vercel |
| VNPay quay về FE local | `FrontendBaseUrl` của BE = `http://localhost:3000`. VNPay gọi URL ngrok của BE; lần đầu ngrok hiện trang cảnh báo, bấm "Visit Site"; BE chuyển tiếp tới `FrontendBaseUrl/payment/result?vnp_...` |
| Thẻ thử VNPay sandbox | Lấy ở trang hướng dẫn sandbox của VNPay (NCB); không ghi số thẻ hay OTP vào repo |
| Rate limit của BE | Đăng nhập 5 lần/phút/IP, đăng ký và xác thực 10 lần/phút/IP, đặt sân 20 lần/phút/người. Playwright trên BE thật dùng lại phiên đăng nhập |
| Cờ TADSS | BE chạy local: cờ `TadssConfig:EnableTadssDecision` **tắt** (mặc định), BE luôn cọc 100 %, không có đơn chờ duyệt; đừng tự bật. Bản deploy: cờ **bật** (P6-56), có đủ `PENDING` / `CONFIRMED` / `PENDING_APPROVAL` / `BOOKING_REJECTED` (với dữ liệu thử, khoảng 40 % lần đặt bị từ chối ⇒ không phải ca hiếm). Muốn thử trên bản deploy thì hỏi người giữ BE |

## 7. Hợp đồng chính xác (đọc thẳng từ mã BE `develop`, 08/11, thêm P6-61 ngày 20/11)

Bảng dưới lấy từ mã C# (enum, DTO, controller, service), không chép từ tài liệu; lệch với DEPOSIT_APPROVAL_API thì theo mục này và báo để sửa tài liệu. Viết type FE theo đúng tên khóa (camelCase), khóa `null` vẫn có mặt.

### Quy ước JSON

| Thứ | Dạng trên dây | Ghi chú |
| :--- | :--- | :--- |
| Tên khóa | camelCase | `JsonNamingPolicy.CamelCase` |
| Enum trong DTO | **chuỗi tên** (`"PENDING_APPROVAL"`) | DTO khai trường `string`; BE **không** bật `JsonStringEnumConverter` toàn cục. Số (`10`) chỉ dùng khi FE gửi bộ lọc `status` lên query |
| `DateTime` (REST) | `"dd/MM/yyyy HH:mm:ss"` giờ VN, không offset | `VietnamDateTimeConverter`; đọc bằng `parseBackendDate` / `parseBackendDateVN` |
| Ngày đặt `bookingDate` | `BookingDto`, `CancelTokenInfoDto`: **`"dd/MM/yyyy 07:00:00"`**; `PendingApprovalBookingDto`: `"yyyy-MM-dd"` | BE dựng `DateOnly.ToDateTime(00:00)` (Kind không xác định), converter coi là UTC rồi cộng +7 ⇒ phần giờ luôn `07:00:00`, vô nghĩa. **Chỉ lấy phần ngày**, ghép với `startTime` của sân để ra mốc `+07:00`; không đưa cả chuỗi vào `parseBackendDateVN` để làm giờ chơi. `PendingApprovalBookingDto.bookingDate` là `DateOnly` nên ra `yyyy-MM-dd` |
| `TimeSpan` | `"HH:mm:ss"` | `startTime`, `endTime` |
| Thời gian trong SignalR | ISO 8601 UTC (`"…Z"`) | `AddSignalR()` dùng JSON mặc định, không qua converter giờ VN |
| Envelope | `{ success, code, message, data, errors }` | `errors`: từ điển khóa PascalCase (`DepositRate`, `Reason`) ⇒ mảng câu |

### Giá trị enum (thứ tự số đúng như C#)

| Enum | Giá trị |
| :--- | :--- |
| `BookingStatus` | `PENDING`=0, `CONFIRMED`=1, `PAID_ONLINE`=2, `IN_PROGRESS`=3, `PENDING_PAYMENT`=4, `COMPLETED`=5, `CANCELLED`=6, `CANCELLED_PENDING_REFUND`=7, `CANCELLED_REFUNDED`=8, `NO_SHOW`=9, `PENDING_APPROVAL`=10 |
| `DepositSource` | `TADSS`, `MANAGER`, `FULL_PAYMENT_FALLBACK` |
| `CancelReason` (`BookingCancelReason`) | `PAYMENT_EXPIRED`, `APPROVAL_EXPIRED`, `APPROVAL_REJECTED`, `POLICY_REJECTED`, `CUSTOMER_CANCELLED`, `STAFF_CANCELLED`, `SYSTEM_CLEANUP` |
| `CancelSource` (`CancelSourceEnum`) | `LINK`, `STAFF`, `SYSTEM`, `CUSTOMER` |
| `paymentStatus` (`InvoicePaymentStatus`) | `UNPAID`, `PARTIALLY_PAID`, `PAID`, `REFUNDED`, `EXPIRED` |
| `source` | `ONLINE`, `WALK_IN` |
| Time grid `status` | `AVAILABLE`, `LOCKED`, `RESERVED`, `IN_USE` (`PENDING`, `PENDING_APPROVAL` ⇒ `LOCKED`; `CONFIRMED`, `PAID_ONLINE` ⇒ `RESERVED`) |

### DTO (khóa đủ)

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

### Endpoint của luồng cọc và mã lỗi thật

| Endpoint | Thành công | Lỗi FE phải rẽ nhánh (`HTTP code`) |
| :--- | :--- | :--- |
| `POST /api/bookings/online` (CUSTOMER, giới hạn 20 lần/phút) | **201**, `OnlineBookingResponse`; `message` theo `status` | 400 `BOOKING_REJECTED` (đơn vẫn có trong lịch sử, `POLICY_REJECTED`); 400 `SLOT_UNAVAILABLE`, 400 `SLOT_UNAVAILABLE_NOTIFY_REGISTERED` (đã tự đăng ký báo khi trống); 400 `BAD_REQUEST` (khung giờ đã bắt đầu, khuyến mãi không hợp lệ…, hiện `message`); 400 `VALIDATION_ERROR` (P6-61: mốc giờ không theo bước 30 phút hoặc một lượt dài quá 12 giờ, câu ở `errors.Courts`, `message` chung chung); 409 `CONFLICT` (đơn vừa bị nhân viên xử lý ⇒ mở lịch sử); 401/403 không envelope ⇒ đăng nhập; 429 ⇒ chờ rồi thử lại |
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

### Công thức phải giống BE

| Việc | Công thức BE | Cách viết ở FE |
| :--- | :--- | :--- |
| Tỷ lệ cọc hợp lệ | `0 ≤ rate ≤ 1`, đúng tối đa 4 chữ số thập phân (`DepositApprovalRules.IsValidRate`) | Nhập % tối đa 2 chữ số thập phân; đổi bằng số nguyên: `basisPoints = Math.round(percent * 100)`, gửi `depositRate = basisPoints / 10000` |
| Lý do | 5…500 **code point**, không chỉ khoảng trắng (`IsValidReason`) | Đếm bằng `[...reason].length` (không dùng `reason.length`: emoji tính 2), `reason.trim() !== ""` |
| Số tiền cọc khi duyệt | `Math.Round(finalTotal × rate, 0, AwayFromZero)` (`BookingApprovalService`) | `Math.round(finalTotal * basisPoints / 10000)` (số dương nên làm tròn nửa lên trùng `AwayFromZero`; nhân số nguyên trước để tránh lỗi số thực) |
| Giờ đặt hợp lệ (P6-61, cả online lẫn tại quầy) | `startTime`, `endTime` chia hết cho 30 phút; `endTime − startTime` ≤ 12 giờ (`CourtSlotDto.IsOnSlotStep`, `IsWithinMaxDuration`) | Lưới giờ chỉ cho chọn ô 30 phút (đã đúng); giới hạn 12 giờ mỗi lượt (V6) |
| Mức tối thiểu khi duyệt | Số tiền > 0 mà < `MinOnlinePaymentAmount` (cấu hình, hiện 5.000 đ) ⇒ 400, `errors.DepositRate` = "Số tiền cọc tối thiểu 5.000 đ, hoặc chọn 0 %" | Báo trước bằng hằng 5.000, vẫn hiện lỗi BE nếu cấu hình khác |
| % hoàn khi hủy | Mốc có `hoursBefore` **lớn nhất** ≤ số giờ còn tới giờ chơi (giờ VN); đã qua giờ chơi ⇒ 0 (`CancellationPolicy.RefundPercent`). `GET /api/cancel-policies` trả sắp giảm dần | Tự sắp hoặc lọc lấy max, không dựa vào thứ tự mảng |
| Tiền hoàn | Mỗi Payment `SUCCESS`: `round((amount − refundedAmount) × % / 100)`, nửa đồng làm tròn lên (P6-62, `MoneyHelper.RoundVnd`), cộng lại (`QuoteRefund`) | Xem trước ≈ `Math.round(sốĐãTrả × % / 100)` với số đã trả ở V4; có thể lệch 1 đ do BE làm tròn theo từng Payment; số thật là số BE ghi |
| Làm tròn tiền, giá nhập (P6-62) | Mọi số tiền là số đồng nguyên; mọi chỗ đổi phần trăm / tỷ lệ ra tiền (tiền sân theo tỷ lệ khung giờ, giảm giá hạng, khuyến mãi, cọc, hoàn) làm tròn về đồng, nửa đồng làm tròn lên (`MoneyHelper.RoundVnd`). Giá sân (`slots[i].weekdayPrice`, `weekendPrice`) và giá dịch vụ (`defaultPrice`, `price`) có phần lẻ ⇒ 400 `VALIDATION_ERROR`, câu "… phải là số đồng nguyên" ở khóa của trường (VD `errors["Slots[1].WeekdayPrice"]`) | Tính trước bằng `Math.round` (số dương nên trùng BE); số tiền hiển thị lấy từ BE (`finalTotal`, `depositAmount`, `remainingAmount`, `/api/prices/calculate`). Ô nhập giá chỉ nhận số nguyên (`step="1"`), lỗi BE hiện theo khóa `errors` |
| Đã tới giờ chơi | `now ≥ bookingDate + startTime sớm nhất` theo giờ VN (`CancellationPolicy.HasStarted`) | Mốc `+07:00` từ `bookingDate` và `startTime` của sân đầu tiên |
| Còn phải thu khi check-out | `finalTotal hiện tại − tổng Payment SUCCESS` (không âm), bằng `remainingAmount` | Hiện `remainingAmount`, không tự trừ |

### Ai được hủy (BE `BookingCancellationService`)

| Ai | Trạng thái hủy được | Giờ |
| :--- | :--- | :--- |
| Khách trong app | `PENDING`, `PENDING_APPROVAL`, `CONFIRMED`, `PAID_ONLINE` | Trước giờ bắt đầu |
| Link trong email | `CONFIRMED`, `PAID_ONLINE` | Trước giờ bắt đầu |
| Nhân viên / Manager / Owner | `PENDING`, `PENDING_APPROVAL`, `CONFIRMED`, `PAID_ONLINE` | Cả sau giờ bắt đầu |

## 8. Bẫy đã gặp (đọc trước khi code)

| # | Bẫy | Làm đúng | Nguồn |
| :---: | :--- | :--- | :--- |
| # | Bẫy | Làm đúng | Nguồn |
| :---: | :--- | :--- | :--- |
| 1 | **C1:** FE hiện gán `window.location.href = paymentUrl` vô điều kiện (đã sửa trên `develop`, chưa lên Vercel: V14) trong khi `paymentUrl` có thể `null` (đơn `CONFIRMED`, đơn chờ duyệt). Xảy ra **ngay cả khi cờ TADSS tắt** (đơn dưới 5.000 đ, đơn giảm 100 %) | Rẽ nhánh theo `status`; chuyển tới trang kết quả đặt sân. **Ràng buộc triển khai:** BE Phase 6 chưa được lên môi trường dùng chung có FE thật trước khi bản sửa C1 lên trước | Spec 8.2 (ghi chú 6A-2, P6-17) |
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
| 14 | Xem trước tiền hoàn tính trên `finalTotal` | Tính trên số đã trả (`depositAmount` khi `PAID_ONLINE`, đơn online cũ `depositAmount = null` thì `finalTotal`; 0 khi chưa trả) | Spec 8.2; V4 |
| 15 | Hiện `depositAmount` là "đã cọc" cho đơn chưa trả | `remainingAmount = finalTotal − depositAmount` kể cả khi chưa trả; "đã cọc" chỉ khi `paymentStatus` khác `UNPAID` | API 2.2; V3 |
| 16 | Sửa type FE theo trí nhớ hoặc theo response một lần gọi | Theo danh sách khóa ở API 2.2 / 3.1 / 3.6 (khóa `null` vẫn có mặt); test `HttpDepositDtoJsonTests` của BE khóa đúng danh sách này | API 2.2, 3.1, 3.6; FE-03 |
| 17 | Lấy cả chuỗi `bookingDate` (`"dd/MM/yyyy 07:00:00"`) làm thời điểm, hoặc so `bookingDate` với "hôm nay" theo giờ máy | Phần giờ `07:00:00` là sản phẩm phụ của converter, không phải giờ chơi: chỉ lấy `dd/MM/yyyy`, ghép `startTime` (`HH:mm:ss`) thành mốc `+07:00`; so ngày bằng `getTodayVN()` | Mục 7 (quy ước JSON) |
| 18 | Gửi `depositRate = percent / 100` bằng số thực rồi tin BE làm tròn | BE **từ chối** (không làm tròn) tỷ lệ quá 4 chữ số thập phân: nhập 7,15 % thì `7.15 / 100` trong JS là `0.07150000000000001` (57,7 % ⇒ `0.5770000000000001`) ⇒ 400 `VALIDATION_ERROR`. Đổi qua số nguyên như 4.5: `Math.round(7.15 * 100) / 10000` = `0.0715` (đã thử bằng Node 08/11) | Mục 7 (công thức) |
| 19 | Đếm độ dài lý do bằng `reason.length` | BE đếm code point (emoji tính 1, `reason.length` tính 2) ⇒ dùng `[...reason].length` | Mục 7 (công thức) |
| 20 | Cho kéo chọn giờ dài tùy ý, hoặc gửi mốc lẻ (VD 18:15) khi tự dựng request | BE từ chối 400 `VALIDATION_ERROR` (P6-61), câu ở `errors.Courts`. Giới hạn 12 giờ ở lưới giờ, mốc luôn lấy từ ô 30 phút; hiện câu trong `errors` | Mục 7; V6 |
| 21 | Hiện tỷ lệ cọc bằng `Math.round(depositRate * 100)` | Tỷ lệ có tới 4 chữ số thập phân (Manager nhập 7,15 % ⇒ `0.0715`): làm tròn tới số nguyên ra "7%", lệch với số tiền. Hiện tối đa 2 chữ số thập phân | Mục 7; V3 |
| 22 | Đọc mã lỗi hay giá trị enum theo trí nhớ (`PAYMENT_TIMEOUT`, `MANUAL`, `BOOKING_ALREADY_NO_SHOW`…) | Chỉ dùng mã có trong `SmashCourt-BE/Common/ErrorCodes.cs` và enum ở Mục 7; gõ tham số bằng type union để `tsc` bắt mã sai | V2, V7 |

## 9. CI/CD của FE

- `.github/workflows/ci.yml`: `npm ci` ⇒ `lint` ⇒ `npx tsc --noEmit -p .` ⇒ **`npm test`** (unit test `node:test`, từ 21/11) ⇒ `build` cho mọi push / pull request vào `master`, `develop`. V11 thêm bước Playwright trên mock.
- **Chỉ push `master`** (CI đạt) mới triển khai lên Vercel production (job `deploy-production`); `develop` chỉ chạy CI.
- Việc của chủ repo FE (một lần, kiểm lại trước V14): Environment `production` trong Settings › Environments có secrets `VERCEL_TOKEN`, `VERCEL_ORG_ID`, `VERCEL_PROJECT_ID`; trong Vercel tắt tự triển khai theo Git (Project Settings › Git) để chỉ còn đường qua CI. Chưa đặt secrets thì push `master` vẫn chạy CI, riêng bước triển khai báo lỗi.
- Biến `NEXT_PUBLIC_API_BASE_URL` (địa chỉ gốc BE, không có `/api`) của bản production khai trong Vercel (Project Settings › Environment Variables), không lấy từ file `.env` trên máy.
- FE trên Vercel gọi BE qua ngrok gói miễn phí: mọi request phải có header `ngrok-skip-browser-warning: true` (V1).
- Chạy local: một file `.env` duy nhất, khối BE local đang bật, khối ngrok để dạng comment, tự đổi tay.

## 10. Hỏi ai, sửa ở đâu

- Hợp đồng API thiếu hoặc mơ hồ: hỏi người giữ BE; nếu phải đổi, sửa DEPOSIT_APPROVAL_API và code BE trước rồi FE theo sau (không tự đoán trường).
- Quyết định nghiệp vụ mới trong lúc làm FE: ghi vào spec Phase 6 (mã P6-xx kế tiếp) để hai bên cùng thấy.
- Lỗi BE phát hiện khi làm FE: báo kèm request/response (không kèm token, mật khẩu, khóa VNPay).
- Xong một việc ở Mục 4: đánh dấu trong commit message (`fix(booking): ... (V4)`) để người giữ BE cập nhật bảng.
