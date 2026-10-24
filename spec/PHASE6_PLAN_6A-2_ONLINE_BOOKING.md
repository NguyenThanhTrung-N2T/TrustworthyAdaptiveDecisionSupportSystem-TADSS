# Gói 6A-2 — Đặt sân online có quyết định cọc: tổng quan

**Mục tiêu:** `POST /api/bookings/online` chạy hai transaction quanh lời gọi TADSS và rẽ đúng 5 nhánh (a, a′, b, c, d) của spec 7.6; bắt đăng nhập `CUSTOMER` (P6-01); response và `BookingDto` có trường cọc. **Cờ `EnableTadssDecision` vẫn tắt** sau gói này: luồng thật đi nhánh (d) (cọc 100 %), như Đồ án 1 trừ các sửa lỗi đã chốt.

**Spec:** 5.1 (6A-2), 7.5 (dòng "đặt sân"), 7.6, 7.12, 7.13, 7.14, 9.2; DEPOSIT_APPROVAL_API 2.1, 2.2, 3.1, 6. **Test:** BE-UT-07; BE-IT-02, 03, 04, 16, 17, 19, 24 (phần thuộc 6A-2, bảng dưới).

**Nhánh:** `SmashCourt-BE` `feat/phase-6-deposit`. Nền: 6A-1 (commit `6590876`).

## Ba plan con, theo thứ tự

| Plan | Nội dung | Phụ thuộc |
| :--- | :--- | :--- |
| [6A-2a](PHASE6_PLAN_6A-2a_TEST_HOST.md) | Test host HTTP cho BE-IT: app thật + PostgreSQL schema thật + WireMock giả TADSS + đăng nhập giả | — (làm song song 2b được) |
| [6A-2b](PHASE6_PLAN_6A-2b_DEPOSIT_POLICY.md) | Hạn giữ chỗ (7.5), chính sách chọn nhánh (thuần), các cập nhật có điều kiện của transaction 2 | 6A-1 |
| [6A-2c](PHASE6_PLAN_6A-2c_BOOKING_FLOW.md) | `CreateOnlineAsync` hai transaction, DTO, sự kiện SignalR, bộ BE-IT | 2a, 2b |

## Quyết định của plan (✅ = đã chốt; mọi quyết định đã chốt 22/10)

| # | Quyết định | Lý do |
| :---: | :--- | :--- |
| D1 ✅ (chốt 21/10) | Chỉ thêm **lõi hủy dùng chung** `BookingRepository.CancelWithStatusCheckAsync(bookingId, expectedStatuses, newStatus, source, reason, cancelledBy, cancelledAtUtc)`: một lệnh `ExecuteUpdateAsync` có điều kiện trạng thái ghi đủ `status`, `cancelled_at`, `cancel_source`, `cancel_reason`, `cancelled_by`, `updated_at` (P6-14), trả số dòng. Hàm **không** biết TADSS, hóa đơn, Payment, khuyến mãi, sân. Phần riêng do từng đường hủy làm; nhánh (c) trong transaction 2: lõi hủy (cổng) ⇒ ghi `tadss_decision_id` (lệnh riêng) ⇒ nhả sân bằng 2 hàm có sẵn (`UpdateCourtActiveStatusAsync`, `SlotLockRepository.DeleteByBookingIdAsync`) ⇒ hóa đơn `UNPAID → EXPIRED`. Chưa tạo `BookingCancellationService`; 6A-3, 6A-4, 6A-5 gọi lại lõi này. **Không raw SQL, không concurrency token** (chỉ LINQ `ExecuteUpdateAsync`/`ExecuteDeleteAsync` như 46 chỗ có sẵn) | Một hàm gói cả hóa đơn sai với đơn đã trả tiền (6A-4 phải tách lại); dịch vụ hủy chung (7.9: hoàn tiền, khuyến mãi, email, Payment `FAILED`) làm sớm là làm trước 6A-4 mà nhánh (c) không cần thứ nào (chưa có Payment, chưa tăng lượt khuyến mãi, chưa đổi `Court.Status`). Raw SQL không được compiler kiểm tên cột/enum; concurrency token trên `Booking` đổi hành vi của ~20 chỗ đang lưu booking |
| D2 ✅ (chốt 22/10) | `IBookingDepositService` thêm `DecideAsync`: kiểm cờ, gọi `EvaluateAsync`, chuyển kết quả thành **quyết định nhánh** (không ghi DB). `BookingService` chỉ áp quyết định (transaction 2, Payment, VNPay) | Giữ `BookingService` (3.000 dòng) không phình thêm logic chọn nhánh; chọn nhánh test được bằng unit test thuần |
| D3 ✅ (chốt 21/10) | **Quy tắc chung:** đơn online chuyển sang `CONFIRMED` mà không qua VNPay ⇒ trong **cùng transaction** của lần chuyển đó, hóa đơn `payment_timing = POSTPAID` và tăng `UsedCount` nếu có khuyến mãi (`IncrementUsageCountAsync` có sẵn). Áp cho nhánh a′ (6A-2, transaction 2) **và** duyệt 0 % (6A-3). Ghi vào spec 7.6, 7.7 và DEPOSIT_APPROVAL_API 4.2 | `UsedCount` chỉ tăng khi đơn chắc chắn (tại quầy lúc tạo, online ở IPN). Đơn a′/duyệt 0 % không bao giờ có IPN ⇒ giới hạn tổng của khuyến mãi bị vượt (VD mã giảm 100 % ⇒ `finalTotal = 0` ⇒ a′, xảy ra **cả khi cờ tắt**). `POSTPAID` đúng nghĩa "thu tại quầy", giống đơn tại quầy trả sau, và là dấu hiệu 7.9 bước 5 (6A-4) dùng để giảm lượt khi hủy ⇒ mọi vòng đời tăng/giảm khớp. `payment_timing` chỉ được đọc lúc tạo đơn và trong log Job-04 (check-out, báo cáo, FE không đọc) nên đổi giá trị an toàn |
| D4 ✅ (chốt 22/10) | Test host tắt Hangfire bằng khóa `Hangfire:Enabled` (mặc định `true`, production không đổi). `false` ⇒ bỏ đăng ký Hangfire (storage, server, dashboard) và 9 job định kỳ; không nơi nào dùng `IBackgroundJobClient`/`BackgroundJob` nên tắt hẳn được, đồng thời không còn tạo schema `hangfire` trong DB test (B3) | Job thật (hủy đơn quá hạn…) chạy trên DB test sẽ làm test chập chờn |
| D5 ✅ (chốt 21/10) | **Luôn tôn trọng kết quả TADSS; a′ xét theo số tiền cần thu online.** Thứ tự (spec 7.6): cờ tắt / TADSS lỗi ⇒ `A_online = finalTotal`; `REJECTED` ⇒ c, `NEED_APPROVAL` ⇒ b (không xét tiền); `APPROVED` ⇒ `A_online = deposit.amount`; rồi `A_online < MinOnlinePaymentAmount` ⇒ a′, ngược lại a/d. `depositSource` = nguồn quyết định chính sách, không phải trạng thái tiền. Ngoại lệ có chủ đích: TADSS lỗi + đơn < 5.000 đ ⇒ a′ (không 503). Quyết định mồ côi (cả hai lần gọi lỗi) là giới hạn chấp nhận, đối soát ở 6A-6 | Plan cũ "`finalTotal = 0` ⇒ a′ mọi trường hợp" làm BE tự duyệt đơn 0 đ mà TADSS trả `NEED_APPROVAL`/`REJECTED` (strategy xét lịch sử khách, không chỉ giá) và lệch dữ liệu đánh giá. Nhánh d cũ tạo link VNPay cho đơn `0 < finalTotal < 5.000 đ` không thanh toán được (cả khi cờ tắt). Thêm giá trị enum `deposit_source` phải đổi DB đã áp `staging` |
| D6 ✅ (chốt 21/10) | Transaction 1 đã commit thì `DecideAsync` và transaction 2 **luôn chạy tới cùng**: `CreateOnlineAsync` truyền `CancellationToken.None`, controller không nhận token (như hiện tại). `DecideAsync`/`EvaluateAsync` vẫn giữ tham số token cho test và caller khác. Test K-01…K-06 | Truyền `RequestAborted` làm khách đóng tab (ca hằng ngày) để lại booking `PENDING` không Payment/`deposit_*` (trông như đơn cũ), giữ slot 10' vô ích, thêm quyết định mồ côi, chỉ log Debug (`ExceptionMiddleware` nuốt `OperationCanceledException`), và cho khách lách `REJECTED`/`NEED_APPROVAL` bằng `retry-payment`. Thời gian chờ đã do `TimeoutMs` giới hạn (mặc định ~1,6 s, tối đa 10 s), tắt BE có kiểm soát chờ request xong |
| D7 ✅ (chốt 21/10) | Câu cổng của transaction 2 thua ⇒ log Error, không Payment, không tăng lượt khuyến mãi, không phát sự kiện, trả **409 `CONFLICT`** `data = null` (mọi nhánh, kể cả c). Spec 7.9 ghi thêm cho 6A-4: gọi lõi hủy với `expectedStatuses` = **đúng trạng thái đã đọc** | Trong khoảng giữa hai transaction chỉ nhân viên hủy chạm được đơn `PENDING` chưa có Payment (đã grep mọi chỗ gán `Status`), nên đơn lúc đó luôn `CANCELLED`: 201 "đặt thành công" kèm `CANCELLED` tự mâu thuẫn; 409 là mã có sẵn, cùng mẫu khách hủy thua tranh chấp. `CancelByStaffAsync` không điều kiện nên ghi đè ngược được (đơn a/d bị hủy khi khách đang trả tiền); lớp lỗi N-11/P6-07, sửa ở 6A-4 |

## Tiến độ và việc còn lại (cập nhật 23/10/2026)

**Đã xong và đã commit** (nhánh `SmashCourt-BE` `feat/phase-6-deposit`: 2b Task 2, 3 ở `1f9861d`; 2b Task 1, 4 + F1/F2 ở `de54140`; 6A-2a ở `84e624b`; build 0 cảnh báo, 1009 test xanh):

| Gói | Task | Nội dung |
| :--- | :--- | :--- |
| 6A-2b | Task 3 ✅ | Lõi hủy `CancelWithStatusCheckAsync` + 4 hàm nhánh `ApplyPaymentRequiredAsync` / `ConfirmWithoutOnlinePaymentAsync` / `HoldForApprovalAsync` / `RejectByPolicyAsync`; chốt chặn bắt buộc có transaction |
| 6A-2a | Task 1, 2, 3 ✅ | Host HTTP thật trên database riêng + `Hangfire:Enabled`; WireMock TADSS + JWT thật + cấu hình đổi lúc chạy; rate limit đếm theo từng người, cấu hình được (xem "Kết quả thực hiện" của 6A-2a) |
| 6A-2b | Task 1 ✅ | `PaymentHoldHelper.ForNewBooking` + `PaymentHoldWindow` (chỉ dòng "đặt sân", A7) |
| 6A-2b | Task 4 ✅ | `CancelTokenHelper` dùng chung cho IPN, đơn tại quầy, a′; sửa T-2 (lệch 7 giờ), T-3 (`Unspecified`); a′ ghi token trong câu cổng (D8) |
| 6A-2b | Task 2 ✅ | `DepositDecisionResolver` + `DepositDecision` + `DepositBranch` + `IBookingDepositService.DecideAsync` |
| 6A-2c | Task 1 ✅ (chưa commit) | `CreateOnlineAsync` hai transaction + A9 + P6-01 + email a′ (xem "Kết quả thực hiện" của 2c) |
| 6A-2c | Task 2 ✅ (chưa commit) | DTO cọc (BE-UT-07): `DepositFieldsHelper`, `BookingDto`/`OnlineBookingResponse`, `message` theo trạng thái |
| 6A-2c | Task 3 ✅ (chưa commit) | Sự kiện sau transaction 2: `BookingCreated` theo trạng thái, nhánh b báo hàng chờ, nhánh c chỉ báo nhân viên (C2) |
| 6A-2c | Task 4 ✅ (chưa commit) | 66 BE-IT HTTP cho đặt sân online; sửa N-26 (giới hạn lượt khuyến mãi), N-27 (IPN không tăng lượt); N-28 (schema snapshot khuyến mãi `FIXED`) có script chờ áp `tadss-dev`; rà soát lần 2: sửa N-29 (đặt đồng thời lỗi 500), làm sớm P6-18; kiểm `reservationId` (24/10); 1276 test xanh |

**Làm tiếp được ngay (6A-2a, 6A-2b, 6A-2c xong hết; N-28 đã áp `tadss-dev` 24/10):** FE chưa sửa C1 nên chưa đưa BE Phase 6 lên môi trường dùng chung có FE thật; rồi kiểm tra cuối gói (BE chạy thật cờ tắt; VNPay sandbox 5.000/4.999 đ cần người dùng; BE với TADSS local, cờ bật chỉ trong lúc kiểm).

**Chốt đã ghi vào plan, spec, DEPOSIT_APPROVAL_API:** A1 → D1 (lõi hủy), A2 → D3 (`POSTPAID` + khuyến mãi), A3 → D5 (thứ tự nhánh, ngưỡng theo `A_online`), A4 → D6 (không dừng theo request), A5 → D7 (thua cổng ⇒ 409), A6 (transaction 2 giữ nguyên `hold_deadline`, test H-01…H-03), A7 (`PaymentHoldHelper` ở 6A-2 chỉ làm dòng "đặt sân"; ba dòng còn lại làm ở 6A-3, 6A-4 theo đề xuất ghi ở 2b Task 1), A8 → D8 (mọi đơn online thành `CONFIRMED` không qua VNPay, gồm a′ và duyệt 0 %, cũng có token hủy và email xác nhận kèm link hủy như đơn qua IPN, để các luồng đồng bộ; hàm token và hạn link hủy dùng chung kéo từ 6A-4 về 2b Task 4, kèm sửa T-2 và T-3 ở cả ba đường phát token để không còn hai công thức hạn), D4 (khóa `Hangfire:Enabled`), B2–B6 (host test: cấu hình nạp trước khi dựng host và host từ chối DB lạ; bộ cấu hình giả đủ và `IEmailService` giả; JWT chỉ qua header; database riêng cho bộ HTTP; đo thời gian thay mục tiêu cứng; xem 6A-2a Task 1), A9 (chặn đặt khung giờ đã qua, làm ở 2c Task 1), C1–C6 (xem bảng bên dưới), B1/D9 (rate limit đếm theo từng người và cấu hình được cho cả 6 policy; làm ở 6A-2a Task 3, phát hiện `login` 5 lượt/phút cũng là bộ đếm chung toàn hệ thống).

### Vấn đề đã chốt 22/10 (không còn vấn đề treo của 6A-2)

| # | Chốt | Ghi vào |
| :---: | :--- | :--- |
| D2 | Giữ `DecideAsync` ở `BookingDepositService` như đã code | — |
| A9 | **Làm trong 6A-2:** `CreateOnlineAsync` bước 1 từ chối 400 `BAD_REQUEST` ("Khung giờ đã bắt đầu hoặc đã qua, vui lòng chọn khung giờ khác") khi `ToUtcFromVietnam(BookingDate, StartTime) ≤ now`; đồng hồ là `TimeProvider` (tiêm vào `BookingService`, đăng ký `TimeProvider.System` nếu chưa có); các test `CreateOnlineAsync` dùng ngày cố định 2026-06-15 (đã là quá khứ) chuyển sang `FakeTimeProvider`; bảng 7.5 không đổi. FE đổi `<` thành `<=` ở `CourtTrack.tsx` (không bắt buộc, ghi ở spec 8.2) | spec 7.6, API doc dòng 2b, 2c Task 1 |
| C1 | **FE tự xử lý ở 6B-2**, chỉ ghi vào spec 8.2: khi `paymentUrl` null thì chuyển tới lịch sử/màn kết quả thay vì `window.location.href = null` (đơn a′ xảy ra cả khi cờ tắt, VD đơn giảm 100 %); kiểu `paymentUrl` thành `string \| null`. BE không đổi gì thêm | spec 8.2 |
| C2 | Đơn bị TADSS từ chối hiện trong lịch sử khách với nhãn "Không được nhận" (theo `cancel_reason = POLICY_REJECTED`), không ẩn. BE **không** đẩy `BookingCancelled` vào nhóm `user_{id}` của chính khách (đã nhận kết quả ngay ở response 400); vẫn cập nhật lưới giờ và phía nhân viên | 2c Task 3; spec 8.2 (nhãn, FE) |
| C3 | Đóng, không làm gì: P6-18 ở 6A-4 bỏ booking đã hủy khỏi `GetUserUsageCountAsync` (hiện đếm cả đơn hủy, lỗi cũ N-22); đơn bị từ chối cũng không tăng `UsedCount` vì lượt chỉ tăng khi `CONFIRMED` | 6A-4 |
| C4 | Kiểm tay "chạy BE thật" dùng **PostgreSQL cục bộ** (Docker, nạp từ `schema_smashcourt.sql`), không đụng `tadss-dev` | "Kiểm tra cuối gói" |
| C5 | Xác minh mức tối thiểu VNPay **sớm, ở cuối 6A-2** trên sandbox: thử đúng 5.000 đ và 4.999 đ (cần người dùng thao tác thanh toán sandbox, khoảng 10 phút) | "Kiểm tra cuối gói" |
| E1 | `Program.cs` tin `X-Forwarded-For` từ mọi nguồn (`KnownNetworks`/`KnownProxies` bị xóa) nên rate limit theo IP của người chưa đăng nhập (VD `login`) có thể bị lách bằng IP giả khi truy cập thẳng cổng BE. **Ghi nhận là giới hạn đã biết, không sửa ở 6A-2** (BE đứng sau ngrok, cổng không mở ra ngoài, `ForwardLimit` = 1; sửa cần danh sách proxy tin cậy theo mạng Docker, đụng triển khai) | spec 9.2 |
| E2 | Email D8 của đơn a′ **dùng lại mẫu có sẵn** `BookingConfirmation.html` qua `BookingEmailFactory.Build` + `SendBookingConfirmationAsync` như đường đơn tại quầy (mẫu đã hiện "Chưa thanh toán" và có link hủy); không tạo mẫu mới; dòng "đã cọc / còn lại" (N-13) vẫn ở 6A-3 | 2c Task 1, spec 7.13 |
| C6 | Chỉ là ràng buộc: **không bật `EnableTadssDecision` trước khi xong 6A-5** (đơn `PENDING_APPROVAL` chưa hết hạn, Job-01 chưa ghi `cancel_reason`) | Bật cờ |

### Ghi nhận từ rà soát chất lượng (22/10)

- Test đồng thời hai kết nối thật đã được thử một lần (đạt: kết nối thứ hai bị chặn bởi khóa dòng, thua cổng khi bên kia commit, thắng khi bên kia rollback); bản chính thức nằm ở 2c Task 4 (G-01…G-04) vì cần dữ liệu commit thật và dọn.
- ~~`DecideAsync` chưa có test integration trên DB thật~~: đã có 3 test trên DB thật (rà soát 22/10) và 8 test qua host HTTP + WireMock (6A-2a, 23/10).
- Mã `Catalog` trên vài test unit mượn mã `BE-IT-xx` theo nhánh nghiệp vụ (VD K-06 gắn `BE-IT-04`): chốt lại khi viết bảng truy vết ở 6A-6.
- Test Z-08 dùng vòng `foreach` trong một ca: tách thành `InlineData` nếu cần xem ca fail rõ hơn.
- Nhánh a/d: câu cổng không đổi trạng thái nên gọi lần hai vẫn thắng và ghi đè mức cọc; đã ghi comment ở `IBookingRepository`, caller không được gọi lại để "thử lại".

### Rà soát toàn bộ phần đã làm của Phase 6 (22/10, tối)

Phạm vi: 6A-1a, 6A-1b, 6A-1c, 6A-2b (Task 1–4). Đo độ phủ bằng coverlet và chạy đột biến.
- Độ phủ dòng 100 % cho mọi file Phase 6 (validator cấu hình, ánh xạ enum, đăng ký TADSS, `CustomerHistoryCalculator`, `TadssDecisionRequestMapper`, `DepositDecisionResolver`, `PaymentHoldHelper`, `CancelTokenHelper`, `TadssCallResult`, `TadssJson`, `CustomerHistoryRepository`, `BookingDepositService`, các hàm mới của `BookingRepository`). Sau khi bổ sung test: `TadssClient` phủ nốt nhánh body lỗi rỗng, `Retry-After` dạng ngày (tương lai, quá khứ), header thắng body, 2xx không có `Content-Type`. Nhánh còn lại không phủ: `Reasons ?? []` ở resolver (không thể xảy ra vì `Reasons` là `required`).
- Test thêm: 6 `TadssClientTests`, 4 `CustomerHistoryCalculatorTests` (`cancelled_at` dạng Local/Unspecified so đúng thời điểm), 122 `BookingStatusTransitionTests` (khóa cả bảng 11 × 11 cặp trạng thái và trạng thái kết thúc không đi đâu được). Tổng **842 test xanh**, build 0 cảnh báo.
- Đột biến: 24 ca trên 6A-1 và resolver, 23 bị bắt, 1 tương đương (hạng thứ 5 vốn là DIAMOND). Cộng 31 ca của 2b Task 2–3 và 14 ca của Task 1, 4: **68/69 bị bắt, 1 tương đương**.
- **Hai lỗ hổng F1, F2 — đã sửa 22/10 (người dùng chốt sửa luôn):**
  - **F1 — mức cọc TADSS ngoài phạm vi.** Client chỉ kiểm INV-7 (có `deposit`/`approvalExpiresAt`), không kiểm `rate ∈ [0, 1]`, `amount ≥ 0` (đã có trong `decision-result.schema.json`), `amount ≤ finalTotal`, `currency = VND`. TADSS trả sai thì: `rate > 1` hoặc `amount < 0` ⇒ CHECK của `invoices` chặn ở transaction 2 ⇒ 500 và đơn `PENDING` không cọc (đúng loại "đơn lửng" D6 muốn tránh); `amount > finalTotal` ⇒ DB không chặn ⇒ VNPay thu nhiều hơn tổng đơn. Đề xuất: client coi vi phạm schema là `InvalidResponse`; resolver coi `amount > finalTotal` hoặc `currency ≠ VND` là quyết định không dùng được ⇒ dự phòng 100 % kèm log Error.
  - **F2 — ngoại lệ trong `DecideAsync`.** Lỗi khi dựng request (snapshot null, lỗi DB khi đọc lịch sử, `Kind` sai) ném ra ngoài `DecideAsync`; sau khi transaction 1 đã commit thì caller nhận 500 và đơn `PENDING` chờ Job-01 dọn. Đề xuất: `DecideAsync` bắt mọi ngoại lệ trừ `OperationCanceledException`, chuyển thành dự phòng 100 % kèm log Error (đúng nguyên tắc "mọi sự cố đều cọc 100 %"); hoặc để 2c Task 1 xử lý ở caller.
  - **Đã làm:** F1 — `TadssClient.CheckDecisionResult` thêm `rate` ∉ [0, 1], `amount` < 0 ⇒ `InvalidResponse`; `DepositDecisionResolver` thêm `amount > finalTotal` hoặc `currency ≠ VND` ⇒ dự phòng, log Error, không giữ decision id. F2 — `DecideAsync` bắt mọi ngoại lệ trừ `OperationCanceledException` khi dựng request/gọi TADSS ⇒ dự phòng 100 %, log Error kèm exception (đơn dưới mức tối thiểu vẫn a′ nhưng giữ Error). Test: +6 client (3 vi phạm, 3 biên hợp lệ 0/1/0.0001), +6 resolver, +5 service (thay test "snapshot thiếu thì ném"), +3 integration `DecideAsync` trên DB thật (đóng ghi chú "chưa có integration"). 7/7 đột biến của phần sửa bị bắt. **862/862 test xanh**, build 0 cảnh báo.

## Phần test của danh mục không thuộc 6A-2

| Mã | Phần làm ở gói khác |
| :--- | :--- |
| BE-IT-02 | IPN thành công ⇒ `PAID_ONLINE` (hành vi có sẵn, chạy lại ở 6A-4); `retry-payment` dùng `depositAmount` (6A-4) |
| BE-IT-04 | "booking hợp lệ theo bộ truy vấn SC6" (BE-IT-20, 6A-6) |
| BE-IT-16 | Duyệt/từ chối đơn `WALK_IN` ⇒ 409 (6A-3); job outcome bỏ qua đơn `WALK_IN` (6A-5) |
| BE-IT-19 | Đơn `PENDING_APPROVAL` vẫn duyệt được khi tắt cờ (6A-3) |

## Ràng buộc chung

- Như 6A-1: kiến trúc theo skill `smashcourt-be-architecture`, thời gian theo 7.14, mã cũ chỉ sửa đúng chỗ plan nêu, test gắn `[Trait("Catalog", ...)]`.
- **Không bật cờ trên `tadss-dev`** cho tới khi xong 6A-5: trước đó chưa có job hết hạn duyệt (đơn `PENDING_APPROVAL` giữ slot mãi) và Job-01 chưa ghi `cancel_reason`.
- Wire của lỗi giữ nguyên: lỗi TADSS không bao giờ lộ ra endpoint đặt sân.

## Kiểm tra cuối gói

- [ ] Build 0 lỗi 0 cảnh báo; toàn bộ test xanh (unit + integration + BE-IT HTTP).
- [ ] Chạy BE thật với **PostgreSQL cục bộ** (Docker, schema từ `schema_smashcourt.sql`; không dùng `tadss-dev`, C4), cờ tắt: đặt sân online ⇒ `PENDING`, `depositSource = FULL_PAYMENT_FALLBACK`, thanh toán sandbox VNPay thành công như trước.
- [ ] Sandbox VNPay (C5): thanh toán đúng 5.000 đ và 4.999 đ, ghi lại VNPay chấp nhận hay từ chối, đối chiếu `MinOnlinePaymentAmount`.
- [ ] Chạy BE thật với TADSS cục bộ (PostgreSQL riêng, không chạm Neon), cờ bật **chỉ trong lượt kiểm**: ra được nhánh a, b, c theo hạng/giá trị đơn; tắt cờ lại sau khi kiểm.
- [ ] Tài liệu: spec 7.6 (D1, D3, A9), 7.13 (D8), 9.2 (test host, rate limit); DEPOSIT_APPROVAL_API (3.1, 4.2, bảng lỗi dòng 2a, 2b); `SmashCourt-BE/CLAUDE.md` (cách chạy BE-IT HTTP).

## Rủi ro

| Rủi ro | Ứng phó |
| :--- | :--- |
| Dựng app thật trong test vướng phần khởi động (Hangfire, VNPay, SMTP, Cloudinary) | **Đã xử lý ở 6A-2a** (host chạy được, 14 test khói, ổn định qua 3 lần chạy) |
| Đặt sân thật qua HTTP cần nhiều dữ liệu (giá, khung giờ, khuyến mãi, quyền chi nhánh) và ngày tương lai theo đồng hồ thật (A9) | Helper seed đặt riêng ở đầu 2c Task 4; mỗi test sân/khung giờ riêng |
| Test HTTP nhạy thời gian (timeout TADSS, khách ngắt kết nối, thua cổng) | Chờ theo điều kiện, không `sleep` cố định; nâng `TimeoutMs` tạm thời cho nhóm K, G; chạy cả bộ ít nhất 3 lần trước khi báo xong |
| Tách Payment ra transaction 2 làm hỏng luồng thanh toán hiện có | BE-IT nhánh (d) so response và số tiền VNPay với hành vi cũ; chạy tay sandbox ở bước kiểm cuối |
| Đơn kẹt giữa hai transaction (BE dừng) | Đơn `PENDING` không Payment, hóa đơn có hạn nên Job-01 vẫn dọn; ghi `SYSTEM_CLEANUP` là việc của 6A-5 |

**Commit gợi ý (một commit cho cả gói):** `feat(booking): Phase 6A-2 — online booking with TADSS deposit decision (two transactions, five branches)`
