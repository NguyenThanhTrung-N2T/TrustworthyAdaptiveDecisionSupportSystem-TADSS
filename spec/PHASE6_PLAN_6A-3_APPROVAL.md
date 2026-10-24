# Gói 6A-3 — Hàng chờ, duyệt và từ chối mức cọc

**Mục tiêu:** Quản lý chi nhánh (và Chủ hệ thống) có ba thao tác mới:
- **Xem hàng chờ:** đơn đang chờ duyệt cọc của chi nhánh mình.
- **Duyệt:** chọn mức cọc (kể cả 0 %).
- **Từ chối:** đơn bị hủy và sân được nhả.

Duyệt và từ chối đi qua TADSS (ghi đè quyết định, "override"). BE chỉ ghi kết quả khi đơn vẫn còn chờ, rồi báo khách bằng SignalR và email. Mọi lỗi của TADSS đều trả cho Quản lý một mã rõ ràng; **không bao giờ** tự đổi sang cọc 100 %.

Gói xong khi ma trận lỗi Mục 5 của DEPOSIT_APPROVAL_API có đủ test. **Cờ `EnableTadssDecision` vẫn tắt** sau gói này. Duyệt/từ chối không phụ thuộc cờ (Q7) nên test chạy được khi cờ tắt.

**Spec:** 5.1 (6A-3), 7.5 (dòng "duyệt"), 7.7, 7.13, 7.14; P6-06, P6-10; Q2, Q4, Q6…Q11. Use case: UC-DEPOSIT-05, 06, 07. Sơ đồ: tuần tự 2. API: DEPOSIT_APPROVAL_API 1.2, 3.3, 4.1–4.3, 5, 6, 7. TADSS: INTEGRATION_SPEC 3.4, 3.6, 3.8, 4.3.

**Test (danh mục):** BE-UT-03, 04, 05, 08 (dòng duyệt); BE-IT-05, 06, 07, 11…15, 18. Phần còn lại của BE-IT-16 (duyệt đơn tại quầy ⇒ 409) và BE-IT-19 (duyệt khi cờ tắt).

**Nhánh:** `SmashCourt-BE` `feat/phase-6-deposit`. Nền: 6A-2 (commit `beff795`).

**Trạng thái:** bản 2 (24/10), viết lại sau khi rà spec lần hai. Người dùng đã chốt 24/10: Task 15 (kéo phần "Thanh toán cọc" về 6A-3); email "đang chờ duyệt" (Task 14); từ chối dùng lõi hủy, chưa qua dịch vụ hủy chung (Task 7); ba cách trả lỗi mới (Task 5b); email từ chối không ghi lý do của Quản lý (Task 14); cọc TADSS trả dưới mức tối thiểu ⇒ `CONFIRMED` (Task 10); đồng bộ với người duyệt không có trong BE (Task 12). Không còn đề xuất chờ chốt.

---

## 0. Kết quả rà soát spec, mã và bản 1 của plan

### 0.1. Mã đã có, dùng lại được

| Thứ đã có | Dùng cho |
| :--- | :--- |
| `ITadssClient.OverrideAsync`, `GetDecisionAsync` (khóa chống trùng `{decisionId}:APPROVE/REJECT`, không tự thử lại) | Gọi TADSS khi duyệt/từ chối, đọc lại quyết định khi đồng bộ |
| Lõi hủy `CancelWithStatusCheckAsync` (một câu lệnh ghi đủ cột hủy, có điều kiện trạng thái) | Từ chối |
| `CancelTokenHelper` (token hủy dùng chung) | Duyệt 0 % (đơn thành `CONFIRMED` nên khách cần link hủy, như nhánh a′) |
| `TransactionScopeHelper.ReadCommitted()` | Transaction ghi kết quả duyệt (tránh lỗi 500 khi đồng thời, N-29) |
| `IBranchScopeResolver.ResolveOptionalBranchIdAsync` | Hàng chờ: Manager đúng chi nhánh, sai chi nhánh ⇒ 403, chi nhánh ngưng ⇒ 400 (đúng API doc 4.1) |
| `IUserBranchRepository.IsUserInBranchAsync` | Duyệt/từ chối: Manager chỉ xử lý đơn chi nhánh mình |
| `DepositFieldsHelper` | Trường cọc trong response duyệt |
| Mẫu email `BookingConfirmation.html` + `BookingEmailFactory` | Email đơn duyệt 0 % (dùng lại như nhánh a′) |
| Hằng sự kiện `BookingApprovalResolved`, `PendingApprovalQueueChanged`; bảng chuyển trạng thái `PENDING_APPROVAL → PENDING / CONFIRMED / CANCELLED` | Đã thêm ở 6A-1, 6A-2 |
| Mẫu WireMock `override-response.approve.json`, `override-response.reject.json`, `decision-detail.json` | Test HTTP |

### 0.2. Mã còn thiếu

| Thiếu | Hệ quả nếu không làm |
| :--- | :--- |
| Client không kiểm response của override (mã quyết định, hành động, mức cọc) | TADSS trả nhầm bản của đơn khác hoặc thiếu mức cọc ⇒ BE ghi sai vào đơn |
| Kết quả gọi TADSS không giữ **tên trường** bị lỗi | Không phân biệt được "TADSS chê tỷ lệ cọc" (phải trả 400 cho Manager sửa) với "TADSS chê header" (lỗi của BE, phải trả 500) |
| `AppException` không mang danh sách lỗi theo trường | Không trả được 400 kèm `errors.DepositRate` khi số tiền cọc dưới 5.000 đ |
| Không có kiểm độ dài theo Unicode code point | Lý do có emoji bị đếm sai so với TADSS ⇒ BE nhận, TADSS từ chối |
| `PaymentHoldHelper` chưa có dòng "duyệt" | Không tính được hạn thanh toán 15' sau duyệt |
| Hàm báo slot trống nằm riêng trong `BookingService` | Service duyệt không gọi được; 6A-5 (job hết hạn duyệt) cũng cần |
| 5 mã lỗi, 3 hàm ghi có điều kiện, truy vấn hàng chờ, 3 endpoint, 2–3 email | — |

### 0.3. Bản 1 của plan sai hoặc thiếu gì (sửa trong bản này)

| # | Vấn đề | Sửa |
| :---: | :--- | :--- |
| 1 | **Luồng duyệt không chạy trọn.** Duyệt xong, cách duy nhất để khách trả cọc là bấm "Thanh toán cọc" (`retry-payment`, P6-10). Nhưng `retry-payment` hiện vẫn thu **toàn bộ** `finalTotal` và đặt hạn `now + 10'` (có thể vượt hạn 15' sau duyệt). Spec 5.1 đặt BE-IT-07 (gồm cả "`retry-payment` trả URL đúng tiền cọc, không kéo dài hạn") vào 6A-3, trong khi spec 7.8 để phần sửa `retry-payment` ở 6A-4. Bản 1 đẩy cả phần test sang 6A-4, nên hết 6A-3 vẫn chưa demo được kịch bản E2E-02 (chờ duyệt ⇒ duyệt ⇒ thanh toán cọc) | **Task 15 (người dùng chốt 24/10):** kéo phần "số tiền + hạn" của `retry-payment` về 6A-3. Phần IPN thất bại và mức cô lập vẫn ở 6A-4 |
| 2 | **Email "chờ duyệt" không gói nào làm.** Spec 7.13 liệt kê mẫu email chờ duyệt, nhưng 6A-2 chỉ gửi email cho nhánh a′, còn 6A-3 bản 1 bỏ sót | **Task 14 (người dùng chốt 24/10):** gửi email "đơn đang chờ duyệt" ở nhánh b của đặt sân |
| 3 | **Trái spec về nơi đặt mẫu email.** Spec 7.13 ghi email mới nằm trong `Templates/`; bản 1 viết HTML inline trong `EmailService` và còn trích sai spec | Task 14 làm file trong `Templates/` như `BookingConfirmation.html` |
| 4 | **Một ca test không dựng được trên DB thật.** BE-IT-12 có ca "đơn chờ duyệt nhưng thiếu mã quyết định ⇒ 409 `APPROVAL_DECISION_NOT_FOUND`", nhưng ràng buộc `chk_bookings_pending_approval` của DB cấm đúng trạng thái đó | Kiểm ca này bằng unit test (Task 5, Task 10); ghi chú vào danh mục test |
| 5 | **Tài liệu lệch nhau.** INTEGRATION_SPEC 2.2 vẽ "duyệt xong ⇒ tạo URL thanh toán cọc, gửi link cho khách", trái P6-10 (duyệt không tạo URL). Sơ đồ tuần tự 2 kiểm số tiền cọc **trước** trạng thái đơn, ngược thứ tự API doc 4.2 | Task 19 sửa hai sơ đồ theo API doc (API doc là nơi sở hữu) |
| 6 | Câu cổng có thêm điều kiện `approval_resolved_at IS NULL` không cần thiết (đơn đã duyệt/từ chối thì luôn rời `PENDING_APPROVAL`) | Bỏ |
| 7 | **Task quá thô.** Task 4 cũ gộp 6 việc (hàng chờ, duyệt, từ chối, đồng bộ, sự kiện, email); Task 5 cũ gộp 11 nhóm test | Chia thành 19 task, 3 phần. **Bản 1 vẫn là cả gói 6A-3, không phải một phần**, chỉ là chia quá thô |

### 0.4. Giới hạn đã biết (không sửa trong 6A-3, đã có hướng)

- **Quản lý chưa hủy tay được đơn chờ duyệt** (cần cho ca "không tìm thấy quyết định", Q10): endpoint hủy của nhân viên chưa nhận `PENDING_APPROVAL`, sửa ở 6A-4 (R-2). Cờ tắt nên chưa có đơn như vậy.
- **Đơn chờ duyệt quá hạn không ai dọn** tới khi có job hết hạn duyệt (6A-5). 6A-3 chỉ bảo đảm không duyệt được đơn đã quá hạn.
- **Lượt khuyến mãi khi duyệt 0 % tăng không kiểm giới hạn tổng** (cùng cách với IPN và nhánh a′): người khác dùng hết lượt trong lúc đơn chờ duyệt thì vượt 1 lượt.
- **Tên sự kiện phía FE** (`signalr-events.ts`) thuộc 6B-1; BE-IT-18 chỉ kiểm phía BE.
- **Check-out đơn đã cọc** vẫn chỉ thu phí dịch vụ (R-3, 6A-4). Đây là một lý do nữa để không bật cờ trước khi xong 6A-4.

---

## Phần A — Nền (chưa có endpoint mới, không đổi hành vi đang chạy)

### Task 1 — Mã lỗi mới; lỗi nghiệp vụ mang được danh sách lỗi theo trường

**Vấn đề:**
- Chưa có 5 mã lỗi của luồng duyệt.
- `AppException` chỉ có message và code. Bước 4b (số tiền cọc quy đổi > 0 nhưng < 5.000 đ) phải trả 400 giống hệt lỗi validate của MVC: `code = VALIDATION_ERROR`, `errors.DepositRate = [...]`, để FE hiện lỗi dưới ô nhập. Hiện không có cách ném lỗi như vậy từ service.

**Làm gì:**
- `Common/ErrorCodes.cs`: tạo nhóm `// ─── Deposit Approval ───` gồm `BookingNotPendingApproval`, `ApprovalAlreadyResolved`, `ApprovalExpired`, `ApprovalDecisionNotFound`, `TadssUnavailable`, và chuyển `BookingRejected` vào nhóm này (spec 7.13). `PaymentHoldExpired` thêm ở Task 15 nếu chốt, còn `BookingAlreadyStarted` để 6A-4 (chỉ thêm khi có chỗ dùng).
- `AppException` thêm tham số tùy chọn `errors` (mặc định `null`); `ExceptionMiddleware` truyền vào `ApiResponse.Fail(message, code, errors)`. Mọi chỗ ném `AppException` hiện có không đổi.

**Kiểm:**
- Middleware: có `errors` thì envelope có đúng từ điển, khóa giữ nguyên tên C# (`DepositRate`); không có thì `errors = null` như trước.
- Một test so envelope 400 của bước 4b với envelope của lỗi validate MVC (cùng `code`, cùng `message`).

### Task 2 — Kiểm dữ liệu Quản lý nhập (BE-UT-03)

**Vấn đề:** BE phải chặn dữ liệu sai **trước** khi gọi TADSS, theo đúng luật của TADSS, để không phí một lần ghi đè (mỗi quyết định chỉ được ghi đè một lần). Hai bẫy:
- **Làm tròn âm thầm:** `0.12345` phải bị từ chối, không được làm tròn thành `0.1235`.
- **Đếm ký tự:** TADSS đếm lý do theo Unicode code point (emoji là 1), C# `string.Length` đếm theo UTF-16 (emoji là 2). Đếm lệch thì lý do 300 emoji qua BE nhưng TADSS từ chối.

**Làm gì:**
- `DTOs/Booking/ApproveBookingDto.cs` (`DepositRate`, `Reason`), `RejectBookingDto.cs` (`Reason`), kiểm bằng `IValidatableObject` như `CreateOnlineBookingDto`.
- `DepositRate` bắt buộc, trong [0, 1], tối đa 4 chữ số thập phân (đếm sau khi bỏ số 0 thừa nên `0.30000` hợp lệ).
- `Reason` bắt buộc, không chỉ gồm khoảng trắng, 5…500 code point (`EnumerateRunes`). Hàm đếm đặt ở `Helpers/StringHelper.cs` (file có sẵn).
- Message tiếng Việt như API doc 4.2.

**Kiểm (BE-UT-03, chạy qua bộ validate thật của MVC):**
- Tỷ lệ hợp lệ: 0, 1, 0.3, 0.125, 0.0001, 0.30000.
- Tỷ lệ bị từ chối: −0.0001, 1.0001, 0.12345, thiếu.
- Lý do: biên 4/5/500/501 ký tự; 5 emoji hợp lệ; 501 emoji bị từ chối; chỉ khoảng trắng bị từ chối; thiếu bị từ chối.
- Khóa lỗi đúng `DepositRate` / `Reason`.

### Task 3 — Client TADSS: kiểm response của ghi đè, giữ tên trường lỗi

**Vấn đề:**
- **Không kiểm response.** `OverrideAsync` hiện nhận mọi response 2xx parse được. Nếu TADSS trả bản ghi đè của quyết định khác (lỗi chống trùng, giống ca `reservationId` đã chặn ở 6A-2), hoặc trả `APPROVE` mà thiếu mức cọc, BE sẽ ghi sai vào đơn. Spec 7.3 đã ghi "6A-3 áp cùng nguyên tắc cho override: `decisionId` trả về phải khớp".
- **Mất tên trường lỗi.** Khi TADSS trả 400 `VALIDATION_ERROR`, client giữ `code` nhưng bỏ danh sách trường bị lỗi (`errors`). Ma trận lỗi Mục 5 cần nó: lỗi ở `deposit.rate` ⇒ 400 để Quản lý sửa (dòng 4); lỗi ở chỗ khác ⇒ 500 vì là lỗi của BE (dòng 5).

**Làm gì:**
- `TadssCallResult` thêm `ErrorFields` (danh sách khóa của `problem.errors`, rỗng nếu không có). `ReadFailure` điền từ body RFC 7807. Khóa của TADSS là đường dẫn JSON: `deposit.rate`, `deposit.amount`, `reason`.
- `OverrideAsync` kiểm response, sai thì trả `InvalidResponse` (log Error, không thử lại), cùng mẫu `CheckDecisionResult`:
  - `decisionId` khác quyết định đã gửi;
  - `action` khác hành động đã gửi;
  - `APPROVE` thiếu `deposit`, hoặc `rate` ngoài [0, 1], hoặc `amount` âm;
  - `REJECT` lại có `deposit`.
- `GetDecisionAsync` kiểm `decisionId` khớp.

**Kiểm:**
- Mỗi trường hợp sai ở trên ⇒ `InvalidResponse`, có log Error ghi cả hai mã, chỉ 1 request.
- `APPROVE`/`REJECT` đúng thì qua.
- `ErrorFields` đọc đúng khóa; rỗng khi body không phải problem+json hoặc không có `errors`.

### Task 4 — Hạn thanh toán sau duyệt (BE-UT-08, dòng "duyệt" của 7.5)

**Vấn đề:**
- Theo Q2, sau khi duyệt mức cọc > 0 khách có **15 phút** để trả cọc, nhưng không được quá giờ bắt đầu chơi; đó cũng là hạn giữ chỗ tuyệt đối mới của đơn.
- Transaction 2 của 6A-2 cố ý **không** đổi `hold_deadline` (vẫn là lúc đặt + 15'). Đơn duyệt ở phút 20 mà không ghi đè `hold_deadline` thì đã "quá hạn" ngay khi được duyệt (ca H-03).
- Hàm phải báo được "không còn thời gian" (giờ chơi đã tới) thay vì trả một mốc đã qua.

**Làm gì:**
- `PaymentHoldHelper.TryForApproval(nowUtc, playStartUtc, approvedWindowMinutes, out window)`: `expires_at = min(now + 15', giờ chơi)`, `hold_deadline = expires_at`.
- `expires_at ≤ now` ⇒ trả `false`.
- Đầu vào không phải UTC hoặc số phút < 1 ⇒ ném lỗi.
- Chốt mẫu `Try…(out window)` mà 6A-2b đã đề xuất; 6A-4 dùng cùng mẫu cho thanh toán lại.

**Kiểm:**
- Duyệt sớm ⇒ +15'.
- Giờ chơi còn 10' ⇒ hạn = giờ chơi.
- Giờ chơi = now + 1 s ⇒ `true`; = now ⇒ `false`; đã qua ⇒ `false`.
- `hold_deadline = expires_at`.
- Cấu hình 1 và 60 phút.
- Qua nửa đêm giờ VN.
- `Kind` sai ⇒ ném lỗi.

### Task 5 — Chọn mã 409 và ánh xạ lỗi TADSS (BE-UT-04, BE-UT-05)

**Vấn đề:** hai quy tắc dày đặc ca biên, nên tách thành hai hàm thuần, kiểm bằng bảng như `DepositDecisionResolver` của 6A-2.

**(a) Chọn mã 409 theo DB của BE** (API doc 4.2 bước 4). Dùng hai lần: trước khi gọi TADSS, và khi thua câu ghi có điều kiện (đọc lại đơn để báo đúng lý do). Xét theo thứ tự:
1. Đơn đã được duyệt/từ chối trước đó (kể cả đã duyệt rồi bị hủy vì hết hạn thanh toán) ⇒ `APPROVAL_ALREADY_RESOLVED`.
2. Đơn bị hủy vì hết hạn duyệt, hoặc còn chờ nhưng đã quá hạn duyệt ⇒ `APPROVAL_EXPIRED`.
3. Đơn không ở trạng thái chờ duyệt (đơn tại quầy, đơn chưa từng cần duyệt, khách đã hủy) ⇒ `BOOKING_NOT_PENDING_APPROVAL`.
4. Còn chờ nhưng không có mã quyết định TADSS ⇒ `APPROVAL_DECISION_NOT_FOUND`.

DB thật cấm trường hợp 4 nên chỉ kiểm bằng unit test.

**(b) Ánh xạ lỗi TADSS thành mã trả cho Quản lý:**

| TADSS trả | Quản lý nhận | Vì sao |
| :--- | :--- | :--- |
| Không phản hồi (timeout), lỗi mạng, 5xx, 429 | 503 `TADSS_UNAVAILABLE`, nút "Thử lại" | Lỗi tạm thời; đơn vẫn chờ |
| 401, 403; 404 **không** có mã `DECISION_NOT_FOUND` (VD trang lỗi ngrok, sai đường dẫn) | 503 `TADSS_UNAVAILABLE`, log **Error** | Lỗi cấu hình phía BE/hạ tầng (VD URL ngrok đổi sau khi khởi động lại). **Mới, chốt 24/10**: thêm vào Mục 5 |
| 404 `DECISION_NOT_FOUND` | 409 `APPROVAL_DECISION_NOT_FOUND` | Q10 |
| 409 `NOT_PENDING_APPROVAL` | 409 `BOOKING_NOT_PENDING_APPROVAL` | |
| 409 `APPROVAL_EXPIRED` | 409 `APPROVAL_EXPIRED` | Đồng hồ hai bên lệch |
| 409 `ALREADY_OVERRIDDEN`, `IDEMPOTENCY_KEY_REUSE` | Đồng bộ (Task 12) rồi 409 `APPROVAL_ALREADY_RESOLVED` | Q9 |
| 400 `VALIDATION_ERROR` tại trường `deposit.rate`/`deposit.amount` | 400 `VALIDATION_ERROR`, `errors.DepositRate` | Dòng 4 Mục 5 |
| Response 2xx hỏng (Task 3: sai mã quyết định, sai hành động, thiếu mức cọc, body không đọc được) | 503 `TADSS_UNAVAILABLE`, log **Error** | Spec 7.3: response hỏng xử lý như mọi lỗi TADSS. Hay gặp nhất là proxy (ngrok) trả trang HTML; khi đó TADSS chưa ghi gì nên thử lại là đúng. Bản 2 từng đề xuất 500, đổi và chốt 24/10 |
| 400 khác, 413, 415, 409 với mã lạ | 500 `INTERNAL_ERROR`, log Error | Lỗi do BE gửi sai hoặc lệch hợp đồng (dòng 5 Mục 5) |

**Kiểm:**
- (a): từng điều kiện ở hai phía biên (hạn duyệt = now ⇒ hết hạn; now + 1 tick ⇒ còn); thứ tự ưu tiên khi nhiều điều kiện cùng đúng; 3 loại đơn của điều kiện 3.
- (b): mỗi dòng bảng, mỗi loại lỗi; không dòng nào ra "cọc 100 %".

### Task 6 — Truy vấn hàng chờ

**Vấn đề:** hàng chờ đọc DB của BE, không gọi TADSS (quyết định 2 của API doc). Cần lọc đúng, sắp ổn định để phân trang không lặp hay sót đơn, và nạp đủ dữ liệu cho một dòng (khách, hạng, sân, giờ, tiền, lý do cần duyệt).

**Làm gì:** `BookingRepository.GetPendingApprovalsAsync(branchId?, nowUtc, page, pageSize)`.
- Lọc: `PENDING_APPROVAL`, hạn duyệt > now, chi nhánh nếu có.
- Sắp theo hạn duyệt, rồi giờ tạo, rồi id.
- Chỉ đọc (`AsNoTracking`); nạp kèm chi nhánh, khách + hạng thành viên, sân, hóa đơn.
- Phân trang theo mẫu `GetAllAsync`.

**Kiểm (DB thật):**
- Hạn = now không hiện.
- Lọc chi nhánh.
- Thứ tự ba khóa (kể cả hòa hạn duyệt).
- Phân trang đủ, không trùng.
- Đơn `PENDING`, đã hủy, tại quầy không hiện.

### Task 7 — Ba câu ghi có điều kiện: duyệt có cọc, duyệt 0 %, từ chối

**Vấn đề:** BE gọi TADSS **ngoài** transaction (giữ khóa DB trong lúc chờ mạng là tối kỵ). Trong lúc chờ, đơn có thể đã đổi: Quản lý khác vừa duyệt, đơn vừa hết hạn, hoặc (từ 6A-4) khách vừa hủy. Vì vậy mỗi lần ghi mở đầu bằng **một câu cập nhật có điều kiện** (câu cổng): chỉ ghi khi đơn **còn chờ duyệt và còn hạn duyệt**; 0 dòng nghĩa là thua, không ghi gì thêm. Điều kiện "còn hạn" bảo đảm không bao giờ duyệt đơn đã quá hạn, kể cả khi job dọn đơn quá hạn (6A-5) chưa chạy.

**Làm gì** (cùng quy ước với các hàm transaction 2 của 6A-2: bắt buộc có transaction, mốc giờ UTC, câu cổng trước, lệnh phụ sau, hóa đơn phải cập nhật đúng một dòng):

| Hàm | Câu cổng ghi | Lệnh phụ |
| :--- | :--- | :--- |
| `ApproveWithDepositAsync` | Đơn → `PENDING`; kết quả `APPROVED`, người duyệt, giờ duyệt, ghi chú | Hóa đơn: cọc theo TADSS, nguồn `MANAGER`, hạn thanh toán và **hạn giữ chỗ ghi đè** (Task 4). Slot lock gia hạn tới cùng mốc |
| `ApproveWithoutDepositAsync` (0 %) | Đơn → `CONFIRMED` + 4 cột duyệt + token hủy (cùng câu, như nhánh a′) | Hóa đơn: cọc 0, `MANAGER`, `POSTPAID`, bỏ hạn; xóa slot lock. Lượt khuyến mãi do service tăng |
| `RejectByManagerAsync` | 4 cột duyệt, kết quả `REJECTED` | Lõi hủy chung (`STAFF`, `APPROVAL_REJECTED`, người hủy = Quản lý); nhả sân; xóa slot lock; hóa đơn `EXPIRED` |

**Quyết định (người dùng chốt 24/10): từ chối chưa đi qua "dịch vụ hủy chung".** Spec 7.7 ghi "từ chối qua dịch vụ hủy chung (7.9)", nhưng dịch vụ đó là việc của 6A-4. Đơn chờ duyệt chưa có tiền, chưa tăng lượt khuyến mãi, chưa đổi trạng thái sân, nên lõi hủy và vài lệnh phụ là đủ (như nhánh từ chối của 6A-2). 6A-4 chuyển phần chung vào dịch vụ mà không đổi hành vi.

**Kiểm (DB thật):**
- Mỗi hàm ghi đúng cột và thỏa các ràng buộc CHECK liên quan.
- Thua cổng khi: đơn không còn chờ; hạn duyệt = now ⇒ trả `false`, không đổi dòng nào.
- Không có transaction ⇒ ném trước khi ghi.
- Duyệt có cọc ghi đè hạn giữ chỗ (H-03, phần DB).
- Token của duyệt 0 % tra ngược ra đúng đơn.
- Từ chối nhả sân (đơn khác đặt lại được cùng giờ).
- Hai kết nối thật cùng duyệt một đơn ⇒ đúng một bên thắng.

### Task 8 — Tách "báo người chờ slot" khỏi `BookingService` (không đổi hành vi)

**Vấn đề:** khi đơn bị từ chối, người đã đăng ký "báo khi trống" cho khung giờ đó phải nhận email (BE-IT-06). Hàm làm việc này là hàm private trong `BookingService` (3.257 dòng). Job hết hạn duyệt ở 6A-5 cũng cần nó.

**Làm gì:** chuyển nguyên thân hàm sang `Services/SlotInterestNotificationService.cs` (`ISlotInterestNotificationService.NotifyReleasedSlotsAsync(booking)`), đăng ký DI. `BookingService` gọi qua interface ở 3 đường hủy cũ.

**Kiểm:** toàn bộ test cũ của 3 đường hủy và đặt sân vẫn xanh; thêm unit test cho service mới (có người chờ ⇒ gửi và xóa đăng ký; lỗi gửi một người không chặn người khác).

---

### Kết quả thực hiện Phần A (24/10/2026) ✅

Mỗi task viết test đỏ trước, rồi chạy đột biến (sửa ngược code, test phải đỏ). Toàn bộ suite: **1454 xanh** (trước 6A-3: 1277), build 0 cảnh báo, chưa commit.

| Task | Đã làm | Test | Đột biến |
| :---: | :--- | :--- | :---: |
| 1 | 5 mã lỗi trong nhóm `Deposit Approval` (chuyển `BookingRejected` vào); `AppException.Errors` + middleware trả `errors` | `ExceptionMiddlewareTests` (3), `DepositApprovalErrorCodesTests` (7, gồm kiểm không trùng giá trị) | 1/1 |
| 2 | `ApproveBookingDto`, `RejectBookingDto` (`IValidatableObject`, báo lỗi cả hai trường cùng lúc); luật chung ở `Helpers/DepositApprovalRules`; `StringHelper.CountCodePoints` | `ApprovalRequestDtoTests` (34) | 3/3 |
| 3 | `TadssCallResult.ErrorFields`; client kiểm response ghi đè (mã quyết định, hành động, có/không mức cọc, phạm vi cọc) và chi tiết quyết định (mã quyết định) | `TadssClientOverrideTests` (19); sửa 1 test cũ dùng mẫu APPROVE cho lệnh REJECT | 8/8 |
| 4 | `PaymentHoldHelper.TryForApproval` | `PaymentHoldHelperApprovalTests` (16) | 5/5 |
| 5 | `ApprovalConflictResolver` (trả `AppException` 409 hoặc null), `OverrideFailureResolver` + `OverrideFailure` | `ApprovalConflictResolverTests` (20), `OverrideFailureResolverTests` (31) | 16/16 |
| 6–7 | `GetPendingApprovalsAsync`, `ApproveWithDepositAsync`, `ApproveWithoutDepositAsync`, `RejectByManagerAsync` (câu cổng chung "còn chờ và còn hạn") | `BookingApprovalRepositoryTests` (45, DB thật) | 18/18 |
| 8 | `SlotInterestNotificationService` (chuyển nguyên thân hàm), `BookingService` gọi qua interface ở 3 đường hủy, đăng ký DI | `SlotInterestNotificationServiceTests` (4); test cũ của 3 đường hủy vẫn xanh | — |

**Lệch nhỏ so với plan (đã cân nhắc):**
- Không tạo `ApprovalState`: hàm chọn mã 409 nhận thẳng entity `Booking` (cùng cách `DepositFieldsHelper` nhận `Invoice`), bớt một kiểu chỉ để chép cột.
- `OverrideFailure` không cần enum hành động: chỉ một cờ "đồng bộ ghi đè đã lưu" và lỗi trả về.
- Thêm điều kiện: đơn còn chờ duyệt nhưng **thiếu hạn duyệt** cũng trả `APPROVAL_DECISION_NOT_FOUND` (dữ liệu lệch, không tự coi là còn hạn).
- Ca "hai kết nối thật cùng duyệt" chuyển sang test HTTP ở Task 18 (cần dữ liệu commit thật; test repository chạy trong transaction rollback).
- **Một đột biến sống sót lúc đầu:** bỏ điều kiện trạng thái khỏi hàng chờ mà test vẫn xanh, vì đơn mẫu ở trạng thái khác không có hạn duyệt. Ngoài đời đơn đã duyệt vẫn **giữ** hạn duyệt để tra cứu, nên thiếu điều kiện đó thì đơn đã duyệt hiện lại trong hàng chờ. Đã sửa test (đơn đã duyệt và đơn khách hủy còn giữ hạn duyệt tương lai); đột biến bị bắt.
- Test double `RecordingEmailService` thêm `FailFor` (hỏng riêng một người nhận).

## Phần B — Luồng nghiệp vụ

Tất cả nằm trong service mới `Services/BookingApprovalService.cs` (`IBookingApprovalService`), nhận `TimeProvider` (7.14), để không làm `BookingService` dài thêm. Ba endpoint đặt trong `BookingController` (API doc 4), policy `OwnerOrManager`.

### Task 9 — Endpoint hàng chờ `GET /api/bookings/pending-approval`

**Vấn đề:** UC-DEPOSIT-05. Quản lý chỉ thấy chi nhánh được gán; Chủ hệ thống thấy tất cả hoặc lọc một chi nhánh.

**Làm gì:**
- Xác định chi nhánh bằng `ResolveOptionalBranchIdAsync`, rồi gọi truy vấn của Task 6 với `now` lấy từ `TimeProvider`.
- Map ra `PendingApprovalBookingDto` đúng bảng API doc 4.1: ngày dạng `yyyy-MM-dd`, giờ chung của các sân, tên hạng thành viên, lý do cần duyệt, ghi chú.
- Query `PendingApprovalListQuery : PaginationQuery`. Message "Lấy danh sách đơn chờ duyệt thành công".

**Kiểm (unit):** gọi phân quyền đúng tham số; map đủ trường; không gọi TADSS.

### Task 10 — Endpoint duyệt `POST /api/bookings/{bookingId}/approve`

**Vấn đề:** UC-DEPOSIT-06. Thứ tự kiểm cố định (API doc 4.2), **dừng ở bước đầu tiên không đạt và không gọi TADSS**: dữ liệu nhập ⇒ đơn tồn tại ⇒ quyền chi nhánh ⇒ trạng thái (Task 5a) ⇒ số tiền cọc tối thiểu.

**Làm gì:**
1. Đọc đơn (`GetByIdWithDetailsAsync`): không có ⇒ 404. Quản lý không thuộc chi nhánh ⇒ 403.
2. Chọn mã 409 với `now` hiện tại; có mã ⇒ 409.
3. Tính trước số tiền `round(finalTotal × tỷ lệ)` (làm tròn xa số 0, cùng công thức TADSS). `0 < số tiền < 5.000 đ` ⇒ 400 `errors.DepositRate` "Số tiền cọc tối thiểu 5.000 đ, hoặc chọn 0 %" (P6-06).
4. Gọi ghi đè TADSS với người duyệt lấy từ JWT, **không hủy theo request** (Quản lý đóng tab giữa chừng mà TADSS đã ghi thì BE vẫn phải ghi theo; cùng lý do D6 của 6A-2). Lỗi ⇒ ánh xạ ở Task 5b.
5. Thành công:
   - Kiểm thêm số tiền TADSS trả không lớn hơn tổng đơn và tiền tệ là VND; sai ⇒ 503 như response hỏng (Task 5b), log Error.
   - Đọc lại đồng hồ (giờ duyệt). Tính hạn ở Task 4; giờ chơi đã tới ⇒ 409 `APPROVAL_EXPIRED`, không ghi, log Error kèm mã quyết định để đối soát.
6. Rẽ nhánh theo **số tiền TADSS trả**:
   - Dưới 5.000 đ (gồm 0) ⇒ duyệt 0 %: `CONFIRMED`, sinh token hủy, tăng lượt khuyến mãi trong cùng transaction.
   - Ngược lại ⇒ duyệt có cọc: `PENDING`.
   - Bước 3 đã chặn `0 < tiền < 5.000`, nên trường hợp này chỉ xảy ra khi hai bên lệch công thức; khi đó vẫn ra `CONFIRMED` kèm log Error. Cùng quy tắc nhánh a′ của 6A-2 (chốt 24/10).
7. Ghi trong `ReadCommitted()`. Thua cổng ⇒ đọc lại đơn, 409 theo Task 5a, log Warning kèm mã quyết định (INTEGRATION_SPEC 4.3), không phát gì.
8. Sau commit: sự kiện (Task 13), email (Task 14). Lỗi ở bước này chỉ log.
9. Trả `BookingApprovalResultDto` (API doc 4.2). Message: "Duyệt đơn thành công, đã thông báo khách thanh toán cọc", hoặc với 0 %: "Duyệt đơn thành công, đơn đã xác nhận, thu tiền tại quầy".

**Kiểm (unit, Moq):**
- Mỗi bước không đạt ⇒ dừng, TADSS không bị gọi, không ghi.
- Đơn chờ duyệt thiếu mã quyết định ⇒ 409 (ca không dựng được trên DB thật).
- TADSS trả 0 / 4.999 / 5.000 đ ⇒ `CONFIRMED` / `CONFIRMED` + log Error / `PENDING`.
- Duyệt 0 % có khuyến mãi ⇒ tăng lượt đúng 1 lần; không có ⇒ 0 lần.
- Thua cổng ⇒ không tăng lượt, không phát, không email.
- Token gửi TADSS không hủy được.
- Lỗi gửi email hoặc phát sự kiện không đổi kết quả.

### Task 11 — Endpoint từ chối `POST /api/bookings/{bookingId}/reject`

**Vấn đề:** UC-DEPOSIT-07. Cùng thứ tự kiểm như duyệt (không có bước tiền). Ghi đè gửi `REJECT` **bỏ hẳn** khóa `deposit` (JSON schema của TADSS từ chối `"deposit": null`).

**Làm gì:** gọi `RejectByManagerAsync` (Task 7). Sau commit:
- báo người chờ slot (Task 8);
- sự kiện (Task 13);
- email từ chối (Task 14).

Không hoàn tiền, không đổi lượt khuyến mãi. Message "Đã từ chối đơn và giải phóng sân".

**Kiểm (unit):** body gửi TADSS không có khóa `deposit`; các bước dừng như Task 10; thua cổng ⇒ 409 và không báo người chờ slot.

### Task 12 — Đồng bộ khi TADSS đã có ghi đè (Q9)

**Vấn đề:** hai tình huống làm TADSS và BE lệch nhau:
- Quản lý bấm duyệt, TADSS ghi xong nhưng response tới chậm (BE báo 503); Quản lý thử lại với **mức cọc khác** ⇒ TADSS báo "khóa đã dùng với nội dung khác".
- Quản lý khác đã duyệt qua TADSS nhưng BE chưa ghi.

TADSS đã có kết quả mà đơn ở BE vẫn chờ. Nếu không đồng bộ thì đơn kẹt tới khi hết hạn rồi bị hủy, dù TADSS ghi là đã duyệt.

**Làm gì:**
1. Khi TADSS trả `ALREADY_OVERRIDDEN` hoặc `IDEMPOTENCY_KEY_REUSE`: đọc chi tiết quyết định.
2. Nếu có ghi đè khớp đúng quyết định và đúng đơn, áp **đúng ghi đè đã lưu** bằng cùng hàm và cùng câu cổng của Task 7, với điều kiện đơn vẫn chờ và còn hạn. Có ghi thì phát sự kiện và gửi email như duyệt thường, vì lần trước khách chưa được báo.
3. **Luôn trả 409 `APPROVAL_ALREADY_RESOLVED`**: người đang bấm không phải người quyết định.
4. Người duyệt ghi theo `actorId` của TADSS nếu đó là mã một user có thật (cột có khóa ngoại tới `users`); nếu không thì vẫn áp kết quả, để trống người duyệt, log Warning ghi nguyên `actorId` (chốt 24/10, phương án A: không đồng bộ thì TADSS "đã duyệt" mà BE "hết hạn duyệt", khách mất đơn; ghi người đang bấm là sai lưu vết; ghi thẳng mã lạ thì lỗi khóa ngoại).
5. Đọc chi tiết lỗi, không có ghi đè, hoặc thua cổng ⇒ chỉ 409, log Warning.

**Kiểm (unit):** đồng bộ thành công (ghi, phát, email, vẫn 409); từng ca không đồng bộ ⇒ chỉ 409, không ghi; `actorId` không phải GUID / user không tồn tại ⇒ người duyệt trống.

### Task 13 — Sự kiện SignalR sau duyệt / từ chối

**Vấn đề:** khách đang mở trang chờ duyệt cần biết ngay; hàng chờ của Quản lý khác cần tải lại (E2E-07, E2E-10). Payload không được có số tiền, link thanh toán, SĐT, email, lý do, mã quyết định (API doc 6).

**Làm gì:**
- DTO `BookingApprovalResolvedNotificationDto` và hàm `BroadcastBookingApprovalResolvedAsync`, gửi tới khách (`user_`), chi nhánh và `role_OWNER`.
- Hằng `QueueReasonApproved`, `QueueReasonRejected` (lý do "hết hạn", "khách hủy" thêm ở 6A-5, 6A-4).
- Duyệt ⇒ `BookingApprovalResolved` + `PendingApprovalQueueChanged` (REMOVED/APPROVED).
- Từ chối ⇒ hai sự kiện trên (REJECTED) + `BookingCancelled` kèm lưới giờ, để người khác thấy slot trống.

**Kiểm:** hub giả ghi lại nhóm và payload; không có khóa cấm; giờ UTC; hub lỗi không làm request thất bại.

### Task 14 — Email cho khách (theo spec 7.13: mẫu trong `Templates/`)

**Vấn đề:** khách không mở app vẫn phải biết kết quả (UC-03 luồng 3d). Email **không bao giờ** chứa link VNPay, vì link VNPay không đặt được hạn (N-15, P6-10).

**Làm gì:** `EmailService` thêm hàm, mẫu HTML mới trong `Templates/` theo cách nạp của `BookingConfirmation.html`. Giờ hiển thị đổi sang giờ VN, định dạng `HH:mm dd/MM/yyyy`.

| Email | Khi nào | Nội dung chính |
| :--- | :--- | :--- |
| Đã duyệt, cần trả cọc (`DepositApproved.html`) | Duyệt có cọc | Số tiền cọc, phần còn lại thu tại quầy (N-13), "thanh toán cọc trước HH:mm", nút tới `{FrontendBaseUrl}/bookings` |
| Xác nhận kèm link hủy (dùng lại `BookingConfirmation.html`) | Duyệt 0 % | Như email nhánh a′ (thu tại quầy, link hủy) |
| Không được nhận (`ApprovalRejected.html`) | Từ chối | Mã đơn, ngày giờ, gợi ý đặt khung khác. **Không** ghi lý do của Quản lý (ghi chú nội bộ; chốt 24/10), kèm số điện thoại chi nhánh để khách liên hệ |
| Đang chờ duyệt (`BookingPendingApproval.html`, chốt 24/10) | Nhánh b của đặt sân (6A-2), sau transaction 2 | Mã đơn, hạn duyệt, "sẽ báo kết quả qua email và app". Spec 7.13 có mẫu này nhưng chưa gói nào làm |

Dòng "đã cọc / còn lại" trong email xác nhận thanh toán (IPN) vẫn ở 6A-4 (spec 7.8).

**Kiểm:** thư ghi lại có đúng người nhận, số tiền, giờ VN; không chứa `vnpay`; thiếu email khách thì bỏ qua, không lỗi; lỗi gửi không đổi kết quả request.

### Task 15 — Thanh toán lại theo tiền cọc và hạn giữ chỗ (kéo từ 6A-4)

**Vấn đề:** xem mục 0.3 dòng 1. Không làm thì sau 6A-3, khách được duyệt cọc 30 % bấm "Thanh toán cọc" sẽ bị VNPay đòi **100 %**, và hạn bị đặt lại `now + 10'` (có thể vượt hạn 15' sau duyệt, đúng lỗi N-6/N-20).

**Làm gì (chỉ phần số tiền và hạn của spec 7.8 dòng 1, API doc 3.3):**
- Số tiền VNPay = tiền cọc của hóa đơn; đơn trước Phase 6 (chưa có cọc) giữ `finalTotal`.
- Hạn mới = `min(hạn giữ chỗ, max(hạn hiện tại, now + 10'))` bằng `PaymentHoldHelper.TryForRetry`. Đã tới hạn giữ chỗ ⇒ 400 `PAYMENT_HOLD_EXPIRED`. Đơn trước Phase 6 không có hạn giữ chỗ ⇒ coi là giờ tạo + 15'.
- Response như hiện tại (đã đổi ở 6A-2).

**Vẫn ở 6A-4:** IPN thất bại không hủy đơn, chuyển transaction sang Read Committed, dịch vụ hủy chung.

**Kiểm:** đơn duyệt 30 % ⇒ link VNPay đúng số tiền cọc; bấm lại liên tục dừng ở hạn giữ chỗ (400); duyệt ở phút 20 rồi thanh toán lại ⇒ thành công (H-03 đủ); đơn cọc 100 % và đơn cũ không đổi số tiền.

---

## Phần C — Kiểm thử HTTP trên host thật và tài liệu

Hạ tầng: dùng host HTTP của 6A-2 (app thật, PostgreSQL schema thật, WireMock giả TADSS, JWT thật).
- **Dựng đơn chờ duyệt bằng luồng thật:** bật cờ trong test, WireMock trả `NEED_APPROVAL`, gọi đặt sân online, đọc mã quyết định từ DB.
- **Đơn quá hạn duyệt:** cho TADSS trả hạn đã qua (BE nhận nguyên) hoặc lùi cột trong DB.
- `TadssStub` thêm: trả ghi đè, trả lỗi ghi đè (kèm `errors`), trả chi tiết quyết định, đếm request ghi đè theo quyết định.
- `SeedManagerAsync`.
- Timeout 2 s như 6A-2; chỉ ca kiểm timeout dùng 400 ms.

### Task 16 — BE-IT: hàng chờ và phân quyền (BE-IT-05, BE-IT-11)

| Ca | Kỳ vọng |
| :--- | :--- |
| Quản lý không truyền chi nhánh / truyền đúng / truyền sai | 200 / 200 / 403 `FORBIDDEN` |
| Chủ hệ thống: tất cả / một chi nhánh | 200 đúng phạm vi |
| Chi nhánh của Quản lý đang ngưng | 400 `BAD_REQUEST` |
| `pageSize` 0 hoặc 51 | 400 `VALIDATION_ERROR` |
| Đơn quá hạn duyệt, đơn đã duyệt | Không hiện |
| Thứ tự, đủ trường, ngày `yyyy-MM-dd` | Đúng API doc 4.1 |
| Ba endpoint: chưa đăng nhập | 401 không envelope |
| Ba endpoint: `STAFF`, `CUSTOMER` | 403 không envelope, WireMock 0 request |
| Quản lý duyệt/từ chối đơn chi nhánh khác, hoặc chưa được gán chi nhánh | 403 `FORBIDDEN`, đơn không đổi |
| Chủ hệ thống duyệt đơn bất kỳ chi nhánh | 200 |

### Task 17 — BE-IT: duyệt, từ chối, thứ tự kiểm, TADSS lỗi (BE-IT-06, 07, 12, 14, 16, 19)

| Ca | Kỳ vọng |
| :--- | :--- |
| Duyệt 30 % (BE-IT-06, 07) | Header gửi TADSS: người duyệt = user JWT, khóa `{decisionId}:APPROVE`. Đơn `PENDING`, cọc theo TADSS, nguồn `MANAGER`, hạn = min(duyệt + 15', giờ chơi) cho cả hạn thanh toán, hạn giữ chỗ và slot lock; **không** Payment; email không chứa link VNPay |
| Duyệt 0 % | `CONFIRMED`, `POSTPAID`, khuyến mãi +1 đúng một lần; link hủy trong email dùng được (xem trước + hủy) |
| Duyệt rồi thanh toán cọc (Task 15) | Link VNPay đúng số tiền cọc; IPN thành công ⇒ `PAID_ONLINE`, hóa đơn `PARTIALLY_PAID` (E2E-02 phía BE) |
| Từ chối | Body không có `deposit`; đơn `CANCELLED`, `APPROVAL_REJECTED`, nguồn `STAFF`; sân nhả (khách khác đặt lại được); người chờ slot nhận email; khách nhận email |
| Thứ tự kiểm (BE-IT-12) | Body sai ⇒ 400; không có đơn ⇒ 404; chi nhánh khác ⇒ 403; đã xử lý / quá hạn / không chờ ⇒ 409 đúng mã; số tiền dưới mức ⇒ 400 `errors.DepositRate`. Mọi ca: WireMock 0 request, đơn và slot lock không đổi, không sự kiện |
| TADSS lỗi (BE-IT-14) | Timeout, lỗi mạng, 500, 503, 504, 429, 401 ⇒ 503; đơn vẫn chờ, cọc trống, không Payment, slot lock không đổi, không sự kiện; mọi lần gửi cùng khóa chống trùng; 401 log Error. Thêm: 404 không mã ⇒ 503; 400 tại `deposit.rate` ⇒ 400; 400 `MISSING_HEADER` ⇒ 500; ghi đè trả về sai mã quyết định ⇒ 503, đơn không đổi |
| Đơn tại quầy (BE-IT-16) | Duyệt/từ chối ⇒ 409 `BOOKING_NOT_PENDING_APPROVAL`, WireMock 0 request |
| Cờ tắt (BE-IT-19) | Đơn đang chờ vẫn duyệt, từ chối được qua TADSS (Q7) |
| Duyệt 0 % rồi check-in, check-out | Thu đủ `finalTotal` tại quầy (spec 9.4 dòng "cọc 0") |
| Duyệt có cọc rồi để quá hạn thanh toán | Job hủy đơn quá hạn chạy trực tiếp ⇒ đơn bị hủy, sân nhả |

### Task 18 — BE-IT: đồng thời, lặp request, sự kiện, ca thực tế (BE-IT-13, 15, 18)

| Ca | Kỳ vọng |
| :--- | :--- |
| Bấm duyệt lần hai sau khi lần đầu thành công | 409 `APPROVAL_ALREADY_RESOLVED`, TADSS không nhận lần hai |
| Hai request song song cùng Quản lý (TADSS trả cùng ghi đè cho cả hai) | Một 200, một 409 |
| Hai Quản lý song song (người thua bị TADSS báo khóa đã dùng / đã ghi đè) | 200 và 409 |
| Thử lại sau 503 (lần 1 quá thời gian, lần 2 TADSS trả bản đã lưu) | 200, đơn `PENDING` |
| Thử lại sau 503 với mức cọc khác | 409, đơn được đồng bộ theo mức cọc đã lưu ở TADSS (Q9) |
| TADSS trả 200 nhưng đơn đổi trong lúc chờ (WireMock trễ 1 s; test ghi DB: hủy / hết hạn / một lần duyệt khác) (BE-IT-15) | 409 đúng lý do; không hồi sinh đơn; không Payment; log có mã quyết định |
| Duyệt sát hạn (hạn duyệt còn 2 s, TADSS trễ 3 s) | 409 `APPROVAL_EXPIRED` dù TADSS trả 200 |
| Đơn khuyến mãi 100 % (tổng 0 đ) duyệt 30 % | TADSS trả 0 ⇒ `CONFIRMED` |
| Duyệt 1 % đơn 200.000 đ | 400, TADSS không bị gọi |
| Từ chối đơn nhiều sân | Mọi sân nhả |
| Sự kiện (BE-IT-18) | Phát sau commit; đúng nhóm, đúng payload, không có trường cấm; lỗi phát không làm request thất bại |

### Task 19 — Tài liệu, sơ đồ, kiểm tra cuối gói

**Tài liệu:**
- **Spec:**
  - 7.5: chốt mẫu `TryForApproval` (và `TryForRetry` nếu Task 15).
  - 7.7: các quyết định đã chốt của plan này.
  - 7.13: mẫu email.
  - 5.1: ghi việc kéo Task 15, sửa danh sách việc của 6A-4.
  - Mục 11: rủi ro mới.
  - Mục 12: N-6, N-7, N-13, N-20.
- **API doc:**
  - Mục 5: thêm dòng 403 / 404 không mã ⇒ 503, response hỏng ⇒ 503.
  - Mục 6: `BookingApprovalResolved` đã phát.
  - Mục 7: câu cổng có điều kiện "còn hạn".
  - Dòng trạng thái đầu file.
- **Sơ đồ:**
  - INTEGRATION_SPEC 2.2 bỏ "tạo URL thanh toán cọc" (trái P6-10).
  - Tuần tự 2 đổi thứ tự: kiểm trạng thái trước số tiền.
  - Chạy lại `check_docs`.
- **Danh mục test:** ghi chú ca "thiếu mã quyết định" của BE-IT-12 kiểm bằng unit test. BE-IT-07 đủ nếu chốt Task 15.
- **Plan:** "Kết quả thực hiện" từng task; bảng tiến độ ở plan 6A-2 trỏ sang plan này.

**Kiểm tra cuối gói:**
- [ ] Build 0 lỗi 0 cảnh báo; toàn bộ test xanh 3 lần liên tiếp.
- [ ] Đột biến trên hai hàm thuần, ba câu cổng, bước số tiền tối thiểu: mỗi đột biến phải bị bắt.
- [ ] Mỗi dòng Mục 5 của API doc thuộc 6A-3 trỏ được tới ít nhất một test.
- [ ] Chạy BE thật với PostgreSQL cục bộ + TADSS cục bộ, bật cờ **chỉ trong lúc kiểm**:
  - đơn ≥ 2.000.000 đ ⇒ hàng chờ ⇒ duyệt 30 % ⇒ (nếu Task 15) thanh toán sandbox đúng tiền cọc;
  - đơn khác bị từ chối ⇒ slot trống;
  - tắt cờ sau khi kiểm.

---

### Kết quả thực hiện Phần B, Phần C và Task 19 (24/10/2026) ✅

Toàn bộ suite **1626 xanh, 3 lần liên tiếp** (khoảng 2 phút mỗi lần), build 0 cảnh báo, chưa commit.

| Task | Đã làm | Test | Đột biến |
| :---: | :--- | :--- | :---: |
| 9–12 | `BookingApprovalService` (hàng chờ, duyệt, từ chối, đồng bộ Q9), 4 DTO (`PendingApprovalListQuery`, `PendingApprovalBookingDto`, `PendingApprovalCourtDto`, `BookingApprovalResultDto`), 3 action trong `BookingController`, đăng ký DI | `BookingApprovalServiceTests` (63) | 24/25 bằng unit; con còn lại (bỏ bước đọc lại đơn sau khi ghi) và biến thể "không xóa change tracker" bị test HTTP bắt |
| 13 | `BookingApprovalResolvedNotificationDto`, `BroadcastBookingApprovalResolvedAsync` (`user_` nếu có, `branch_`, `role_OWNER`), hằng `QueueReasonApproved/Rejected` | `BroadcastServiceTests` (+9) | — |
| 14 | 3 mẫu trong `Templates/` (`BookingPendingApproval.html`, `DepositApproved.html`, `ApprovalRejected.html`), `DepositApprovalEmailModel`, `BookingEmailFactory.BuildApproval`; nhánh b của đặt sân gửi email "đang chờ duyệt" | `DepositApprovalEmailTests` (6), +3 ca nhánh b; sửa 2 test cũ khẳng định "nhánh b không có email" | — |
| 15 | `PaymentHoldHelper.TryForRetry`; `retry-payment` thu tiền cọc, hạn `min(hạn giữ chỗ, max(hạn, now + 10'))`, `PAYMENT_HOLD_EXPIRED`; `PaymentService` nhận `TimeProvider` và cấu hình | `PaymentHoldHelperRetryTests` (14), `PaymentServiceRetryDepositTests` (8) | 5/5 + 5/5 |
| 16 | HTTP: hàng chờ, phân quyền | `ApprovalQueueTests` (24) | — |
| 17 | HTTP: duyệt 30 % (header, body gửi TADSS, DB, email), trả cọc qua `retry-payment` + IPN (E2E-02 phía BE), hết hạn trả cọc ⇒ Job-01, duyệt 0 % (khuyến mãi +1, link hủy dùng được), đơn 0 đ, duyệt 0 % rồi check-in/check-out thu đủ, từ chối (đơn hai sân, người chờ slot nhận email, khách khác đặt lại được), thứ tự kiểm, đơn tại quầy, cờ tắt; TADSS lỗi theo từng dòng Mục 5 | `ApprovalFlowTests` (18), `ApprovalTadssFailureTests` (17) | — |
| 18 | HTTP: bấm lần hai, hai lần bấm song song (hai kết nối DB thật), hai Quản lý, thử lại sau 503 cùng/khác nội dung (Q9), đơn đổi trong lúc chờ TADSS (hủy, hết hạn, người khác duyệt), duyệt sát hạn | `ApprovalConcurrencyTests` (10) | — |
| 19 | Spec 5.1, 7.5, 7.7, 7.8, 7.13, Mục 11 (2 rủi ro), Mục 12 (N-6, N-20 về 6A-3); API doc trạng thái, 3.3, 4.1, 4.2, Mục 5 (dòng 22–24), Mục 6, 7; sơ đồ tuần tự 2 và INTEGRATION_SPEC 2.2 (render kiểm bằng mermaid-cli, đạt); danh mục test BE-IT-12 | `check_docs` đạt | — |

**Phát hiện và xử lý trong lúc làm:**
- **Hai test dựng sai, code đúng:** TADSS giả trả cọc 60.000 đ cho đơn 0 đ (code chặn đúng, trả 503); TADSS giả trả lý do khác với lý do đã gửi (ngoài đời TADSS trả lại đúng lý do nhận được). Đã sửa test cho sát thực tế.
- **Chi nhánh "không hoạt động":** `INACTIVE` là xóa mềm nên trả 404; "tạm ngưng" là `SUSPENDED`, trả 400 như API doc. Test kiểm cả hai; API doc 4.1 ghi rõ.
- **Thứ tự kiểm của `retry-payment`:** kiểm hạn giữ chỗ trước hạn thanh toán, nếu không `PAYMENT_HOLD_EXPIRED` không bao giờ xảy ra (đã báo người dùng khi làm; ghi spec 7.8, API doc 3.3).
- **Bỏ điều kiện thừa:** "giờ chơi ≤ lúc ghi" lặp với `TryForApproval`; bỏ để code gọn.
- **Lỗi có sẵn trong tài liệu:** sơ đồ INTEGRATION_SPEC 2.2 có dấu `;` trong nội dung tin nhắn nên mermaid không render được (có từ trước). Đã đổi thành dấu phẩy cùng lúc sửa nội dung sai so với P6-10.
- **Giới hạn đã biết (ghi spec Mục 11):** hai Quản lý duyệt cùng lúc mà response TADSS của người thắng về chậm hơn lần đồng bộ của người thua ⇒ cả hai nhận 409, dữ liệu vẫn đúng.
- **Kinh nghiệm chạy đột biến:** script trả file gốc nhưng không build lại thì lần chạy cả bộ kế tiếp dùng bản build của đột biến cuối (4 test "đỏ" giả). Script nay luôn build lại ở cuối.

**Còn lại của kiểm tra cuối gói:** chạy BE thật với PostgreSQL cục bộ và TADSS cục bộ, bật cờ trong lúc kiểm (đơn ≥ 2.000.000 đ ⇒ hàng chờ ⇒ duyệt 30 % ⇒ thanh toán sandbox đúng tiền cọc; đơn khác từ chối ⇒ slot trống). Cần chạy TADSS cục bộ và thao tác sandbox VNPay; gộp với kiểm tra cuối gói 6A-2 đang chờ.

### Dọn kiến trúc và comment BE sau gói (24/10/2026) ✅

Chỉ đổi cấu trúc, không đổi hành vi nhìn thấy từ ngoài (route, DTO, mã lỗi, nội dung email/SignalR giữ nguyên). Suite **1626 xanh, 3 lần liên tiếp**, build 0 cảnh báo, chưa commit.

| Phần | Trước | Sau |
| :--- | :--- | :--- |
| Luật thuần của luồng cọc | Rải trong `Helpers/` lẫn tiện ích kỹ thuật | `Services/Deposit/`: `DepositDecisionResolver`, `ApprovalConflictResolver`, `OverrideFailureResolver`, `PaymentHoldPolicy` (từ `PaymentHoldHelper`), `CustomerHistoryCalculator`, `DepositFieldsMapper` (từ `DepositFieldsHelper`) |
| Kiểu nội bộ của luồng cọc | `Models/ViewModels/` (lẫn với projection của repo) | `Models/Deposit/`: `DepositDecision`, `DepositFields`, `OverrideFailure`, `PaymentHoldWindow`, `CustomerHistorySummary` |
| Dựng request TADSS | `Helpers/TadssDecisionRequestMapper` | `Integrations/Tadss/TadssDecisionRequestMapper` (cạnh client và DTO của TADSS) |
| Thông báo sau commit | Nằm trong `BookingService` (3244 dòng) và `BookingApprovalService` (490 dòng) | `BookingNotificationService` (199 dòng; SignalR + email, mỗi bước tự nuốt lỗi và ghi log); `BookingService` 3096, `BookingApprovalService` 385 |
| Comment | 125 dòng comment code production mang mã quyết định, ngày chốt, số mục spec | 0; quy tắc ghi vào skill `smashcourt-be-architecture` (mục Comment) |

Giữ lại ở `Helpers/`: `CancelTokenHelper`, `TransactionScopeHelper` (tiện ích kỹ thuật), `DepositApprovalRules` (tầng DTO dùng, DTO không phụ thuộc `Services`). Thư mục test chia theo loại: `Unit/` phản chiếu cây nguồn (VD `Unit/Services/Deposit/`, `Unit/Integrations/Tadss/`), `Integration/` giữ nguyên, hạ tầng test (`AppExceptionAssertions`, `TestCategoryAttribute`) về `TestData/`; trước đó `Integration/` (loại test) và `Integrations/` (phản chiếu nguồn) nằm cạnh nhau, tên gần trùng. Test log của thông báo kiểm logger của `BookingNotificationService`.
Phát hiện khi chạy cả bộ: 4 test HTTP khớp chuỗi log "thua câu cổng" đỏ do log đổi thành "thua câu cập nhật có điều kiện"; đã sửa chuỗi kỳ vọng (log nội bộ, không phải hành vi). Plan các gói trước giữ tên cũ vì là biên bản lúc làm; spec 7.5, BE-UT-08 và methodology đã đổi sang `PaymentHoldPolicy`.

## Ràng buộc chung

- Kiến trúc theo skill `smashcourt-be-architecture`; thời gian theo 7.14 (`TimeProvider`, UTC, `ToUtcFromVietnam`); không raw SQL, không concurrency token.
- Test gắn `[Trait("Catalog", ...)]`. Chỉ sửa lỗi thật, kèm test, và ghi vào spec.
- Transaction không bao giờ bao quanh lời gọi TADSS. Lỗi TADSS không bao giờ dẫn tới cọc 100 % khi duyệt/từ chối.
- Không bật cờ trên `tadss-dev` trước khi xong 6A-5; không đụng `tadss-dev` để kiểm tay.

## Rủi ro

| Rủi ro | Ứng phó |
| :--- | :--- |
| Tách hàm báo slot trống làm vỡ 3 đường hủy cũ | Chuyển nguyên thân hàm; test cũ phải xanh trước khi làm tiếp |
| Ca đồng thời chập chờn | Chờ theo điều kiện, WireMock trễ có kiểm soát, chạy cả bộ 3 lần |
| TADSS trả hạn duyệt sai (lệch đồng hồ) | Câu cổng "còn hạn" + kiểm giờ chơi ở Task 10; job 6A-5 dọn |

## Thứ tự và ước lượng

| Phần | Task | Ghi chú | Ước lượng |
| :--- | :--- | :--- | :---: |
| A | 1 → 2, 3 → 4, 5 → 6, 7 → 8 | 2–3, 4–5, 6–7 làm song song được; 8 độc lập | ~1,5 ngày |
| B | 9 → 10 → 11 → 12 → 13, 14 → 15 | 15 tùy chốt | ~1,5–2 ngày |
| C | 16 → 17 → 18 → 19 | | ~1,5 ngày |

Tổng khoảng 4,5–5 ngày (W10). 6B-3 (màn Quản lý ở FE) chạy trên MSW theo API doc, không phải chờ gói này.

**Commit gợi ý (một commit cho cả gói):** `feat(booking): Phase 6A-3 — pending-approval queue, approve/reject via TADSS override, Q9 sync, approval events and emails`
