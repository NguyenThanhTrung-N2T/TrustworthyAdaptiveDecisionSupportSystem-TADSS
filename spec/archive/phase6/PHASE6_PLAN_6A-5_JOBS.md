# Gói 6A-5 — Job: hết hạn thanh toán, hết hạn duyệt, NO_SHOW, kết thúc giờ chơi, báo kết quả về TADSS

**Mục tiêu (đúng spec 5.1):** "Job-01 sửa (lý do, có điều kiện); job hết hạn duyệt; job outcome (7.11)". Hoàn thành khi: **không tự duyệt; outcome không gửi lặp, không tranh rate limit; nhãn gửi TADSS không mâu thuẫn với dữ liệu check-in.** Sau gói này, mọi điều kiện kỹ thuật để bật cờ `EnableTadssDecision` đã đủ; bật cờ là quyết định riêng của người dùng, không làm trong gói.

**Phạm vi:** ba job của spec, cộng (chốt 26–27/10): Job-04 và Job-02 ghi trạng thái có điều kiện (B6, B7 — P6-25), kiểm hạn duyệt lúc nhận quyết định (B8 — P6-24), outcome theo P6-26 (gồm timeout riêng 5 giây). **Không đổi schema, không đụng cổng thanh toán, không thêm khóa cấu hình, không đổi API.** Hạn link VNPay (E1, E2) làm ở gói 6A-6 (P6-27).

**Spec:** 5.1 (6A-5), 7.6, 7.11, 4.4.1, 7.12, 7.13, 7.14; P6-04, P6-07, P6-19, P6-22, **P6-24, P6-25, P6-26** (chốt 27/10); N-24, N-30…N-35; T-14. Contract: INTEGRATION_SPEC §2.3, §3.7, §3.8, §4.3. API: DEPOSIT_APPROVAL_API Mục 1 (quyết định 5), 2.1. Sơ đồ: tuần tự 4. Use case: UC-DEPOSIT-09, 10.

**Test (danh mục):** BE-UT-01 (phần kiểm response và timeout outcome), BE-UT-09 (ánh xạ outcome); BE-IT-04 (hạn duyệt không dùng được), BE-IT-08, BE-IT-09, BE-IT-26 (job có điều kiện, mới 27/10); phần Job-01 của BE-IT-21.

**Nhánh:** `SmashCourt-BE` `feat/phase-6-deposit`. Nền: 6A-4 đã commit (`f554fad`).

**Trạng thái:** **✅ đã code và kiểm (27/10), commit BE `fd29a51`** — kết quả ở mục "Kết quả thực hiện" cuối plan. Plan bản 5 (27/10). Bản 5 thêm B7 (Job-02), B8 (hạn duyệt lúc nhận), timeout outcome (C8) và chốt phần ngoài phạm vi (mục 0.6); spec, INTEGRATION_SPEC, use case, sơ đồ tuần tự 4 và methodology đã đồng bộ ở mức thiết kế ngày 27/10. Lịch sử: bản 3 (25/10) giữ phạm vi spec; bản 4 (26/10) sau một vòng review ngoài và rà lại với mã thật: chốt B1…B6, C1…C7 (mục 0.2); thêm kiểm tính nhất quán của nhãn (C1), sửa Job-04 (B6), giải thích nguồn gốc các cột dùng trong gói (mục 0.3), bảng trạng thái booking / hóa đơn / Payment (C7), bảo đảm slot lock (C5), so với cách các hệ thống khác vận hành (C3). B5 (API doc 2.1) đã sửa ngày 26/10.

**Cách đọc mỗi task:** *Vấn đề* (chuyện xảy ra ngoài đời, không chỉ tên hàm) → *Giải pháp* → *Vì sao* (kèm phương án đã cân nhắc) → *Ảnh hưởng* (thứ gì đổi với người dùng, dữ liệu, mã khác) → *Kiểm*.

---

## 0. Rà soát

### 0.1. Mã đã có, dùng lại được

| Thứ đã có | Dùng cho |
| :--- | :--- |
| `ITadssClient.ReportOutcomeAsync` (6A-1): `POST /decisions/{id}/outcome`, idempotency key `{decisionId}:OUTCOME`, không thử lại, lỗi trả trong `TadssCallResult` | Job báo kết quả |
| `TadssOutcome`, `TadssOutcomeRequestDto`, `TadssOutcomeResponseDto`; cột `tadss_outcome_reported_at`; chỉ mục `idx_bookings_outcome_pending` trên `updated_at` (spec Mục 6) | Đánh dấu đã báo, chọn đơn cần báo |
| `BookingCancellationService` (6A-4): lõi hủy có điều kiện `CancelWithStatusCheckAsync`, nhả sân `ReleaseCourtsAsync`, Payment chờ ⇒ `FAILED` | Hai job hủy |
| `IBookingRepository.LockAsync`, `IInvoiceRepository.SetPaymentStatusAsync`, `IBookingRepository.UpdateCourtActiveStatusAsync`, `ICourtRepository.BatchUpdateStatusAsync` | Kiểm "vẫn quá hạn" dưới khóa; hóa đơn `EXPIRED`; nhả sân của Job-04 |
| `BookingNotificationService`, `ISlotInterestNotificationService` (link trang chủ), `SignalREvents.BookingExpired`, `BookingNoShow`, `PendingApprovalQueueChanged` | Thông báo |
| `BookingEmailFactory`, `DepositApprovalEmailModel`, mẫu `ApprovalRejected.html` | Email hết hạn duyệt |
| Câu duyệt/từ chối của 6A-3 có điều kiện "`PENDING_APPROVAL` và `approval_expires_at > now`" | Loại trừ với job hết hạn duyệt |
| `TadssStub`, `RecordingEmailService`, `RecordingLoggerProvider`, `MoveApprovalExpiryAsync`, `RunPaymentExpiryJobAsync`, `TadssContractFiles` | BE-IT |

### 0.2. Mọi thay đổi của gói và trạng thái chốt

**A. Spec yêu cầu:** Job-01 qua dịch vụ hủy chung, ghi lý do, có điều kiện (Task 1, 3); job hết hạn duyệt, email hết hạn duyệt, rời hàng chờ (Task 1, 2, 4); job outcome tuần tự, 20 đơn/chu kỳ, dừng ở 429, `occurredAt` cố định, chạy cả khi cờ tắt, ánh xạ theo 4.4.1 (Task 6, 8, 9); đăng ký Hangfire, `TimeProvider`, BE-IT-08, BE-IT-09.

**B. Lỗi thật — sửa kèm test (chốt 26/10):**

| Mã | Lỗi | Nằm ở | Task |
| :---: | :--- | :--- | :---: |
| B1 | Định nghĩa `SYSTEM_CLEANUP` xếp nhầm đơn đã duyệt mà khách chưa bấm thanh toán thành "lỗi BE" | Spec 4.4.1 | 1 |
| B2 | Job-01 có thể hủy đơn khách vừa được gia hạn (đua quanh mốc hết hạn) | Mã Job-01 | 1, 12 |
| B3 | Email "slot đã trống" của Job-01 **và Job-04** dẫn tới `/booking?courtId=…`, FE không có trang này (404) | Mã `BookingJob` | 2, 3, 5 |
| B4 | Client không kiểm response của outcome: 200 sai nội dung vẫn bị coi là đã báo, đơn không bao giờ được báo lại | Mã `TadssClient` | 7 |
| B5 | API doc 2.1 ghi mọi đơn `CANCELLED` có `paymentStatus = UNPAID` | Tài liệu | **Đã sửa 26/10** |
| B6 | Job-04 ghi `NO_SHOW` không điều kiện, đè lên lần check-in hoặc lệnh hủy của nhân viên vừa commit (N-30) | Mã Job-04 | 5, 12 |
| B7 | Job-02 ghi `PENDING_PAYMENT` không điều kiện, đè lên lần check-out vừa commit; chọn trạng thái đích theo hóa đơn đọc trước khi khóa (N-31, chốt 27/10) | Mã Job-02 | 5, 12 |
| B8 | BE đưa đơn vào hàng chờ duyệt với hạn duyệt đã qua lúc nhận (hoặc sau giờ chơi) ⇒ không ai duyệt được, khách mất đơn (N-33, P6-24, chốt 27/10) | Mã `DepositDecisionResolver`; INTEGRATION_SPEC §4.3 | 14 |

**C. Chỗ spec chưa rõ hoặc plan khác spec — đã chốt 26/10:**

| Mã | Spec nói | Plan làm | Task |
| :---: | :--- | :--- | :---: |
| C1 | Không nói đơn `PENDING_PAYMENT` báo gì | Báo `COMPLETED`; **mọi nhãn phải khớp `checked_in_at`** (lệch ⇒ giữ lại, log Error) | 6 |
| C2 | Không nói thứ tự chọn đơn | `updated_at` mới nhất trước; ghi rõ giới hạn | 8 |
| C3 | Chỉ 429 dừng chu kỳ | Dừng chu kỳ ở mọi lỗi hệ thống; lỗi riêng một đơn thì làm tiếp | 6, 9 |
| C4 | 7.11: Job-01 hủy khi `expires_at < now` | `expires_at <= now`, thống nhất với "Thanh toán lại" (`PaymentService.cs:79`) | 1, 3 |
| C5 | Không nói | Job-01 bỏ xóa slot lock hết hạn toàn hệ thống (Job-03 làm); bảo đảm không có slot lock thừa gây hại | 3 |
| C6 | Không nói | Job-01 và job hết hạn duyệt xử lý tối đa 100 đơn mỗi lần chạy | 3, 4 |
| C7 | Không nói | Đơn hết hạn: Payment chờ ⇒ `FAILED`; bảng trạng thái booking / hóa đơn / Payment đầy đủ | 1 |
| C8 | 4.3 INTEGRATION_SPEC: timeout `TimeoutMs` (800 ms) cho mọi lệnh gọi | Riêng lệnh gửi outcome timeout 5 giây (N-34, P6-26, chốt 27/10); lệnh đánh giá lúc đặt giữ 800 ms | 7, 13 |

### 0.3. Dữ liệu dùng trong gói: cột nào, ai ghi, có từ bao giờ (C1, C2)

| Dữ liệu | Có từ | Ai ghi, khi nào | FE có thấy không | Gói này |
| :--- | :--- | :--- | :--- | :--- |
| `bookings.checked_in_at` | **Cũ**, Đồ án 1 (commit `c1a5c7a`, 01/05/2026) | Chỉ một chỗ: nhân viên bấm check-in (`POST /api/bookings/{id}/check-in`), cùng câu cập nhật có điều kiện chuyển `CONFIRMED`/`PAID_ONLINE ⇒ IN_PROGRESS` (`BookingRepository.CheckInAsync`). Check-in chỉ được trong khoảng giờ chơi ± 15'. Không bao giờ bị xóa hay ghi lại | Không (không có trong DTO) | Chỉ đọc: mapper outcome (C1), điều kiện của Job-04 (B6) |
| `bookings.updated_at` | **Cũ**, Đồ án 1 (`DEFAULT now()`, không có trigger) | Mã tự gán ở mỗi lần ghi. Mọi đường kết thúc đơn đều gán (đã kiểm): lõi hủy (hủy của khách, nhân viên, link, Quản lý từ chối, TADSS từ chối), check-out, Job-02, Job-04, Job-01 mới. Sau khi kết thúc vẫn có thể được gán lại: tiền về sau khi hủy (`MarkPendingRefundAsync`), nhân viên xác nhận đã hoàn | Không | Chỉ đọc: thứ tự chọn đơn báo outcome (C2). Chỉ mục `idx_bookings_outcome_pending` trên cột này là **mới** (Phase 6) |
| `bookings.cancelled_at` | **Cũ**, Đồ án 1 | Chỉ lõi hủy ghi (6A-4), một lần, cùng câu cập nhật đổi trạng thái | Có (chi tiết đơn) | Đọc làm `occurredAt` của các outcome hủy |
| `bookings.cancel_reason`, `tadss_decision_id`, `tadss_outcome_reported_at` | **Mới**, Phase 6 (`phase6_deposit.sql`) | Lõi hủy; transaction 2 của đặt online; job outcome | `cancelReason` có (6B hiển thị) | Đọc / ghi `tadss_outcome_reported_at` |
| `occurredAt` | **Không phải cột của BE.** Trường trong body gửi TADSS (contract Phase 5) | BE tính lúc gửi từ dữ liệu sẵn có: `COMPLETED`/`NO_SHOW` = giờ kết thúc chơi (giờ VN, `+07:00`); hủy = `cancelled_at`. TADSS lưu vào `tadss.decision_outcomes.occurred_at` | Không | Mới (mã 6A-5) |

Outcome **không hiển thị trên UI** nào (FE không có chỗ dùng). Nó là nhãn kết quả thật để đánh giá chất lượng quyết định cọc của TADSS (công cụ `tools/evidence`: xuất dữ liệu, tỷ lệ không đến, KPI cho Khóa luận). Vì thế yêu cầu là **đúng và không mất**, không phải đẹp.

### 0.4. Ma trận tương tác (đã đối chiếu mã thật)

| Đơn | Luồng chạy cùng lúc | Kết quả |
| :--- | :--- | :--- |
| `PENDING` (Job-01) | IPN thành công | Cả hai khóa dòng booking trước ⇒ không deadlock. Job thắng ⇒ IPN vào nhánh "tiền về sau khi hủy", hoàn 100 % (6A-4); Payment đã `FAILED` vẫn chuyển được sang `SUCCESS` (`TryMarkSuccessAsync` nhận mọi trạng thái khác `SUCCESS`) |
| | IPN thất bại | Cùng khóa booking trước; Payment không còn `PENDING` ⇒ bỏ qua |
| | Thanh toán lại | B2 (Task 1): khóa booking rồi đọc lại hạn |
| | Khách / nhân viên hủy | Một bên thắng; bên thua nhận 409 |
| | Transaction 2 của đặt online | Hạn 10' từ lúc tạo; transaction 2 xong trong khoảng 1 giây ⇒ không gặp nhau |
| `PENDING_APPROVAL` (job hết hạn duyệt) | Quản lý duyệt / từ chối | Câu duyệt cần `approval_expires_at > now`, job cần `≤ now`, cùng đồng hồ BE ⇒ không thể cùng thắng. TADSS đã ghi override rồi BE mới thua ⇒ BE là nguồn sự thật (INTEGRATION_SPEC §4.3) |
| | Khách / nhân viên hủy | Một bên thắng |
| `CONFIRMED` / `PAID_ONLINE` (Job-04) | Nhân viên check-in sát mốc giờ chơi + 15' | B6 (Task 5): câu cập nhật có điều kiện `checked_in_at IS NULL` và trạng thái chưa đổi ⇒ một bên thắng |
| | Nhân viên hủy (được hủy sau giờ chơi) | B6: cùng câu điều kiện ⇒ một bên thắng; đơn hủy giữ nguyên tiền hoàn |
| `IN_PROGRESS` hết giờ (Job-02) | Nhân viên check-out đúng giờ kết thúc | B7 (Task 5): Job-02 khóa booking rồi đọc lại; check-out cũng khóa booking trước ⇒ một bên thắng; job thua thì bỏ qua |
| Đơn đã kết thúc (job outcome) | Đơn đổi trạng thái trong lúc gửi (`PENDING_PAYMENT → COMPLETED`, tiền về sau khi hủy) | Outcome và `occurredAt` không đổi ⇒ cùng nội dung |
| | Mất phản hồi dù TADSS đã ghi | Chu kỳ sau gửi lại cùng nội dung ⇒ TADSS trả 200 bản đã lưu (chống trùng theo nội dung, §3.7; TADSS không lưu idempotency key của outcome nên không có 409 `IDEMPOTENCY_KEY_REUSE`) |
| | Hai instance BE | Khóa phân tán Hangfire; đánh dấu "đã báo" có điều kiện |

Thứ tự khóa: mọi luồng chạm đơn đều khóa dòng booking trước các bảng khác (6A-4 đã rà và sửa `40P01`; gói này chỉ thêm đường đi qua lõi chung hoặc câu điều kiện trên booking). Rate limit TADSS (spec 2.4): token bucket theo client, 100 request, nạp 100/giây ⇒ 20 request tuần tự mỗi 5 phút không tranh với lượt đặt sân.

### 0.5. Quyết định đã chốt

| # | Vấn đề | Chốt |
| :---: | :--- | :--- |
| **D1** | Đơn bị TADSS từ chối vĩnh viễn không được làm kẹt đơn mới | Chọn theo `updated_at` mới nhất trước, không đổi schema (C2, Task 8) |
| **D2** | Đơn `PENDING_PAYMENT` có báo không | Báo `COMPLETED`, kèm kiểm `checked_in_at` (C1, Task 6) |
| **D3** (26/10) | Lỗi hệ thống khi gửi outcome | Dừng chu kỳ (C3, Task 9) |
| **D4** (26/10) | Job-04 ghi đè | Sửa trong gói (B6, Task 5) |
| **D5** (27/10) | Job-02 ghi đè check-out (E5 cũ) | Sửa trong gói thành B7 (Task 5), P6-25 |
| **D6** (27/10) | Hạn duyệt không dùng được lúc nhận (E3 cũ) | Cọc 100 %, không lưu `decisionId` (B8, Task 14), P6-24; bổ sung INTEGRATION_SPEC §4.3; không đặt ngưỡng tối thiểu ở BE |
| **D7** (27/10) | Timeout gửi outcome (E4 cũ) | 5 giây riêng cho outcome (C8, Task 7); lệnh đánh giá giữ 800 ms |
| **D8** (27/10) | Link VNPay không có hạn, IPN trễ (E1, E2) | Gói 6A-6 (P6-27): `vnp_ExpireDate` sớm hơn hạn đơn một khoảng đệm đo trên sandbox; không ân hạn giữ sân |
| **D9** (27/10) | Cạnh `PAID_ONLINE → COMPLETED` (E6) | Giữ nguyên, ghi spec Mục 11 |

### 0.6. Ngoài phạm vi và các mục đã đưa vào gói (chốt 27/10)

| # | Vấn đề | Chốt |
| :---: | :--- | :--- |
| E1 | Link VNPay không có hạn (`VNPAY.NET` 2.1.0 không gửi `vnp_ExpireDate`, đã kiểm DLL): khách trả bằng tab cũ sau khi đơn bị hủy ⇒ mất sân, nhận hoàn tiền mặt (tiền an toàn nhờ P6-07) | **Gói 6A-6** (P6-27): `VnPayService` tự dựng và ký URL (dùng `HmacSha512` có sẵn); `IPaymentGateway.CreatePaymentLink` nhận hạn (UTC), mỗi cổng tự đổi định dạng (VNPay `yyyyMMddHHmmss` GMT+7 từ `TimeProvider`); còn quá ít thời gian ⇒ không tạo link, trả lỗi hết hạn giữ chỗ; IPN về đơn đã hủy chỉ đi nhánh hoàn tiền. Xác minh tài liệu VNPay và sandbox trước khi code |
| E2 | IPN về sau lúc Job-01 hủy (khách trả trong vài giây cuối) | **Gộp vào 6A-6**: hạn link sớm hơn `expires_at` một khoảng đệm (đề xuất 2 phút, chốt sau khi đo IPN thật); không kéo dài giữ sân; **không** phải bảo đảm tuyệt đối, lưới P6-07 giữ nguyên |
| E3 | Hạn duyệt không dùng được lúc nhận | **Đưa vào gói**: B8, Task 14 |
| E4 | Timeout 800 ms khi gửi outcome, DB Neon của TADSS ngủ | **Đưa vào gói**: C8, Task 7, Task 13 |
| E5 | Job-02 ghi đè check-out | **Đưa vào gói**: B7, Task 5 |
| E6 | Cạnh `PAID_ONLINE → COMPLETED` không mã nào dùng (chỉ check-in gọi `ValidateTransition`) | Giữ nguyên; ghi spec Mục 11 (N-35); nhãn outcome kiểm `checked_in_at` (C1) |

### 0.7. Việc người dùng làm

Không có việc chặn code (gói không đổi schema, không đổi cấu hình).

---

## Phần A — Dịch vụ hủy cho job

### Task 1 — Hủy do hết hạn trong `BookingCancellationService` (A, B1, B2, C4, C7)

**Vấn đề:**
- Job-01 hiện đọc cả lô đơn quá hạn rồi ghi một lần bằng `SaveChanges`: không điều kiện trạng thái (có thể ghi đè IPN vừa xác nhận), không thứ tự khóa, không ghi `cancel_reason`, để Payment chờ ở `PENDING`.
- Job hết hạn duyệt chưa có.
- **B1:** spec 4.4.1 định nghĩa `SYSTEM_CLEANUP` là "đơn `PENDING` không có Payment nào (BE dừng giữa hai transaction)". Nhưng đơn **đã được Quản lý duyệt cọc mà khách chưa bấm "Thanh toán cọc"** cũng chưa có Payment nào (Payment chỉ được tạo khi khách bấm, P6-10). Theo câu chữ hiện tại, đơn này bị xếp `SYSTEM_CLEANUP` ⇒ không tính vào `paymentExpiredCount` của khách và không báo outcome, trong khi đây đúng là "khách không trả tiền" (spec cũng ghi `PAYMENT_EXPIRED` áp "kể cả sau khi đã duyệt").
- **B2:** khách bấm "Thanh toán lại" lúc 9:59:59.8, request thấy đơn còn hạn rồi gia hạn tới 10:10; cùng lúc Job-01 đọc đơn lúc 10:00:00.1 thấy đã quá hạn rồi hủy ⇒ khách vừa nhận link thì đơn bị hủy (tiền về sau đó thành hoàn 100 %).
- **C4:** "Thanh toán lại" coi `expires_at <= now` là hết hạn (`PaymentService.cs:79`), Job-01 (và spec 7.11) dùng `expires_at < now` ⇒ hai định nghĩa cho cùng một mốc.

**Giải pháp:**
- Thêm vào `IBookingCancellationService`:
  ```csharp
  // Job-01: đơn PENDING quá hạn thanh toán ⇒ CANCELLED (SYSTEM, PAYMENT_EXPIRED; BE dừng giữa hai transaction ⇒ SYSTEM_CLEANUP); false khi đơn không còn quá hạn
  Task<bool> ExpireUnpaidAsync(Guid bookingId);

  // Job hết hạn duyệt: đơn PENDING_APPROVAL quá hạn duyệt ⇒ CANCELLED (SYSTEM, APPROVAL_EXPIRED); không bao giờ duyệt thay; false khi đơn đã đổi
  Task<bool> ExpireApprovalAsync(Guid bookingId);
  ```
- Thêm vào `IBookingRepository` (gọi trong transaction, sau `LockAsync`):
  ```csharp
  // Đơn còn PENDING, hóa đơn UNPAID và expires_at <= now (đọc dưới khóa booking để thua lần gia hạn vừa commit)
  Task<bool> IsPaymentWindowOverAsync(Guid bookingId, DateTime nowUtc);

  // Đơn còn PENDING_APPROVAL và approval_expires_at <= now
  Task<bool> IsApprovalWindowOverAsync(Guid bookingId, DateTime nowUtc);
  ```
- **Hàm riêng `ExpireAsync` dùng cho cả hai, không sửa `CancelAsync`.** `CancelAsync` hiện có (hủy của khách, nhân viên, link) khác đường hết hạn ở bốn chỗ: ném 409 khi thua (job cần `false`), không khóa và kiểm lại hạn, không đổi hóa đơn, luôn gọi `NotifyCancelledAsync`. Đổi chữ ký của nó dễ làm vỡ bốn đường hủy đang chạy, nên `ExpireAsync` gọi lại các phần dùng chung (`CancelWithStatusCheckAsync`, `ReleaseCourtsAsync`, `FailPendingByInvoiceAsync`), không chép lại logic. Các bước:
  1. Nạp đơn (ngoài transaction). Không thấy hoặc trạng thái khác trạng thái chờ ⇒ `false`.
  2. Có Payment `SUCCESS` ⇒ log Error, `false` (đơn chờ thanh toán / chờ duyệt không thể có tiền về, vì IPN đổi trạng thái trong cùng transaction).
  3. Transaction Read Committed: **khóa booking** ⇒ **kiểm lại "vẫn quá hạn"** (sai ⇒ `false`, không ghi gì) ⇒ lõi hủy có điều kiện (`SYSTEM`, lý do, `cancelledBy = null`; thua ⇒ `false`) ⇒ nhả sân và xóa slot lock của đơn ⇒ Payment `PENDING` ⇒ `FAILED` ⇒ hóa đơn `UNPAID ⇒ EXPIRED` ⇒ commit. Không trả lượt khuyến mãi: đơn `PENDING`/`PENDING_APPROVAL` chưa từng được tính lượt (chỉ tính ở IPN thành công, duyệt 0 % hoặc đơn trả tại quầy — đã kiểm `PaymentResultService.cs:150`, `BookingApprovalService.cs:245`, `BookingService.cs:604`).
  4. Sau commit: thông báo (Task 2). Trả `true`.
- **B1:** `SYSTEM_CLEANUP` khi hóa đơn **chưa có mức cọc** (`deposit_source IS NULL`) **và** chưa có Payment nào; còn lại `PAYMENT_EXPIRED`.

**Bảng trạng thái sau khi job thắng (C7, đã đối chiếu mã):**

| Đối tượng | Hết hạn thanh toán | Hết hạn duyệt |
| :--- | :--- | :--- |
| Booking | `PENDING ⇒ CANCELLED`; `cancel_source = SYSTEM`; `cancel_reason = PAYMENT_EXPIRED` hoặc `SYSTEM_CLEANUP`; `cancelled_at = updated_at = now`; `cancelled_by = null` | `PENDING_APPROVAL ⇒ CANCELLED`; `APPROVAL_EXPIRED`; còn lại như bên trái |
| Hóa đơn | `UNPAID ⇒ EXPIRED` (như Job-01 cũ) | `UNPAID ⇒ EXPIRED` |
| Payment | `PENDING ⇒ FAILED` (Job-01 cũ để `PENDING`); `FAILED` giữ nguyên; `SUCCESS`: không xảy ra (bước 2) | Không có Payment (Payment chỉ tạo cho đơn `PENDING`, `PaymentService.cs:63`) |
| Sân | `booking_courts.is_active = false`; slot lock của đơn bị xóa; sân về `AVAILABLE` nếu không đơn khác giữ | Như bên trái |
| Refund, lượt khuyến mãi | Không tạo, không đổi | Không tạo, không đổi |
| **Tiền về sau đó** (E1) | Payment `FAILED ⇒ SUCCESS`; booking `CANCELLED ⇒ CANCELLED_PENDING_REFUND`; Refund 100 % `PENDING`; hóa đơn giữ `EXPIRED`. Nhân viên xác nhận trả tiền mặt ⇒ `CANCELLED_REFUNDED`, hóa đơn `REFUNDED` (6A-4, P6-07) | Không thể (không có link thanh toán) |

**Vì sao:**
- Dùng các bước hủy chung: spec 7.11 yêu cầu; cùng thứ tự khóa với mọi đường hủy khác.
- B1: transaction 2 của đặt sân **luôn** ghi mức cọc (TADSS, dự phòng 100 %) cùng lúc với chuyển nhánh; Quản lý duyệt ghi `MANAGER`; nhánh chờ duyệt đặt `NULL` nhưng đơn đó là `PENDING_APPROVAL`, không phải `PENDING`. Mức cọc còn trống ở đơn `PENDING` chỉ khi BE dừng giữa hai transaction — đúng ý của spec. Phương án khác: thêm cột đánh dấu → không cần, dữ liệu sẵn có đã phân biệt được.
- B2: lần gia hạn của "Thanh toán lại" là câu cập nhật có điều kiện `status = PENDING` trên dòng booking (`ExtendPaymentWindowAsync`), tức khóa dòng trước khi đổi hạn. Job hoặc chờ nó commit rồi thấy hạn mới, hoặc chạy trước và làm "Thanh toán lại" nhận 409.
- C7: Payment chờ của đơn đã hủy không còn ý nghĩa; mọi đường hủy của 6A-4 đều đánh `FAILED`. Không chặn tiền về muộn vì IPN thành công nhận Payment ở mọi trạng thái khác `SUCCESS`.

**Ghi chú cho FE (6B) về "Thanh toán lại" và tải lại trang (B2):** BE **không lưu** URL thanh toán; mỗi lần "Thanh toán lại" tạo một Payment và link mới, đánh Payment chờ trước đó `FAILED`, và ghi hạn mới vào `invoices.expires_at` + slot lock **trong cùng transaction**. Khi khách tải lại trang, `GET /api/me/bookings/{id}` trả `expiresAt` là hạn mới nhất; muốn trả tiền thì bấm "Thanh toán lại" để lấy link mới (FE đang làm vậy: `BookingDetailModal`, `BookingHistoryList`). Link cũ nếu khách vẫn trả được thì tiền vẫn được ghi nhận (đơn còn `PENDING` ⇒ `PAID_ONLINE`; đơn đã trả bằng link khác ⇒ ghi nhận, không đổi đơn — test `SecondLinkPaidAfterTheFirst…` của 6A-4). Trả lại đúng link cũ khi tải trang (không tạo link mới) là tính năng mới, phải lưu URL và vướng E1 (link không có hạn) ⇒ không làm; ghi vào DEPOSIT_APPROVAL_API Mục 3.3.

**Ảnh hưởng:** đơn hết hạn có `cancel_reason`; Payment chờ của đơn hết hạn thành `FAILED`; đơn đã duyệt mà không trả tính vào `paymentExpiredCount`; đơn quá hạn đúng mốc bằng cũng bị hủy; không đổi API.

**Kiểm (`Unit/Services/BookingCancellationServiceExpiryTests`, `FakeTimeProvider`):**

| Ca | Mong đợi |
| :--- | :--- |
| **B1** `PENDING`, mức cọc `TADSS`, Payment `PENDING` (khách mở link rồi bỏ) | `PAYMENT_EXPIRED` |
| **B1** `PENDING`, mức cọc `FULL_PAYMENT_FALLBACK`, Payment `FAILED` (thẻ lỗi rồi bỏ) | `PAYMENT_EXPIRED` |
| **B1** `PENDING`, mức cọc `MANAGER`, chưa có Payment (đã duyệt, chưa bấm thanh toán) | `PAYMENT_EXPIRED` |
| **B1** `PENDING`, nhiều Payment `FAILED`/`PENDING` (thanh toán lại nhiều lần) | `PAYMENT_EXPIRED`; mọi Payment `PENDING` ⇒ `FAILED` |
| **B1** `PENDING`, chưa có mức cọc, chưa có Payment (BE dừng giữa hai transaction) | `SYSTEM_CLEANUP` |
| **B1** `PENDING` dữ liệu trước Phase 6 (chưa có mức cọc, có Payment) | `PAYMENT_EXPIRED` |
| **C7** đơn thắng | đúng từng dòng bảng trạng thái ở trên (booking, hóa đơn, Payment, `booking_courts`, slot lock, không Refund, không đổi lượt) |
| **C4** `expires_at` = `now − 1 µs` / `= now` / `now + 1 µs` | hủy / hủy / `false`, không ghi gì |
| **B2** kiểm lại dưới khóa thấy còn hạn (vừa được gia hạn) | `false`; không lõi hủy, không ghi, không thông báo |
| Lõi hủy thua (IPN vừa chuyển `PAID_ONLINE`) | `false`; không nhả sân, không đổi hóa đơn, không đổi Payment, không thông báo |
| Đọc ra `PAID_ONLINE` / `CANCELLED` / không tồn tại | `false`, không mở transaction |
| `PENDING` có Payment `SUCCESS` | `false`, log Error |
| `PENDING_APPROVAL` quá hạn duyệt (`= now` và quá) | `true`; `APPROVAL_EXPIRED`; hóa đơn `EXPIRED` |
| `ExpireApprovalAsync` gặp đơn đã `PENDING` (vừa được duyệt) | `false` |
| Thứ tự lệnh | khóa ⇒ kiểm hạn ⇒ lõi hủy ⇒ Payment ⇒ hóa đơn |
| Thông báo ném lỗi | Vẫn `true` |
| Bốn đường hủy cũ | Toàn bộ test hủy của 6A-4 giữ xanh (không đổi `CancelAsync`) |

Lịch sử khách (B1 tới TADSS): `Unit/Services/Deposit/CustomerHistoryCalculatorTests` thêm ca đơn `PAYMENT_EXPIRED` sau khi duyệt được đếm, `SYSTEM_CLEANUP` không đếm.

### Task 2 — Thông báo hết hạn và email hết hạn duyệt (A, B3)

**Vấn đề:** chưa có thông báo cho đơn bị job hủy. Job-01 cũ tự phát `BookingExpired` và tự gửi email "slot đã trống" bằng hàm riêng `NotifySlotInterestedUsersAsync` (`BookingJob.cs:80`) với link `{FrontendBaseUrl}/booking?courtId=…&date=…&start=…&end=…`.

**B3 — đã kiểm FE (26/10):**
- FE **không có trang `/booking`**: thư mục `app/booking/` chỉ có `cancel/page.tsx` (trang hủy bằng link email). Next.js trả 404 cho `/booking?...`. Không có `next.config` redirect hay middleware nào chuyển hướng.
- Trang đặt sân thật là `/bookings/new` (nhóm `(customer)`, cần đăng nhập CUSTOMER) và **không đọc tham số** `courtId`/`date` từ URL, nên không điền sẵn được slot.
- Dịch vụ dùng chung `SlotInterestNotificationService` (6A-3, dùng cho mọi đường hủy) gửi link **trang chủ** `{FrontendBaseUrl}`. Trang chủ (`HomeGateClient`) tự chuyển khách đã đăng nhập tới `/bookings/new`, khách chưa đăng nhập thấy trang giới thiệu. Đây là trang tồn tại và đúng hành vi mong muốn.
- Hàm riêng có link hỏng còn được **Job-04** gọi (`BookingJob.cs:583`) ⇒ sửa cả hai chỗ (Task 3, Task 5), rồi xóa hàm.

**Giải pháp:**
- `IBookingNotificationService.NotifyExpiredAsync(Booking booking, BookingStatus previousStatus)`:
  - Mọi đơn: `BookingExpired` kèm lưới giờ; báo người chờ slot qua `SlotInterestNotificationService` (link trang chủ — sửa B3).
  - Đơn chờ duyệt: thêm email hết hạn duyệt cho khách và `PendingApprovalQueueChanged` `REMOVED`/`EXPIRED`.
  - Hết hạn thanh toán: **không** email khách (như mã cũ; khách đã nhận hạn trong email/màn hình lúc đặt hoặc lúc được duyệt).
  - Câu sự kiện: hết hạn thanh toán giữ câu cũ "Booking #{code} của {tên} đã bị hủy tự động do hết hạn thanh toán."; hết hạn duyệt "… đã bị hủy tự động do chi nhánh chưa duyệt kịp."
- `SignalREvents.QueueReasonExpired = "EXPIRED"` (DTO hàng chờ đã ghi giá trị này; FE chỉ tải lại hàng chờ khi nhận sự kiện nên không phải sửa FE).
- `Templates/ApprovalExpired.html` theo khuôn `ApprovalRejected.html`: "Chi nhánh chưa kịp xác nhận đơn {{BookingCode}} trong thời hạn nên đơn đã tự hủy. Bạn chưa bị trừ tiền và sân đã được trả lại." kèm sân, ngày giờ, chi nhánh, nút tới lịch sử đặt sân. Câu "chưa bị trừ tiền" đúng vì đơn chờ duyệt không có link thanh toán (`PaymentService.cs:63`) và Task 1 bước 2 không hủy đơn có Payment `SUCCESS`. `EmailService.SendApprovalExpiredAsync(DepositApprovalEmailModel)`, tiêu đề "Đơn {code} đã hết hạn duyệt - SmashCourt".
- Mỗi bước bọc `RunSafelyAsync` như các hàm thông báo khác.

**Vì sao:** một nơi cho thông báo sau commit như các luồng khác (6A-3, 6A-4); dùng dịch vụ báo slot chung thay vì bản riêng có link hỏng. Muốn link điền sẵn slot thì FE phải đọc tham số ở `/bookings/new` — việc của 6B, ghi vào spec 8.2.

**Ảnh hưởng:** người chờ slot nhận link trang chủ thay vì link 404 (cả khi đơn hết hạn lẫn khi đơn bị đánh `NO_SHOW`); khách có đơn hết hạn duyệt nhận email mới.

**Kiểm:** `Unit/Services/BookingNotificationServiceExpiryTests` (hết hạn thanh toán: `BookingExpired`, người chờ slot nhận link đúng `FrontendBaseUrl`, không email khách, không sự kiện hàng chờ; hết hạn duyệt: đúng một email qua mẫu mới, `REMOVED`/`EXPIRED`; một bước lỗi không chặn bước sau); `DepositApprovalEmailTests` (mẫu mới có mã đơn, sân, giờ VN, không link VNPay, mã hóa HTML tên khách).

---

## Phần B — Job hủy, Job-04 và Job-02

### Task 3 — Job-01 viết lại (A, B3, C4, C5, C6)

**Vấn đề:** như Task 1; thêm: Job-01 cũ xóa slot lock hết hạn **của toàn hệ thống** (trùng việc của Job-03 `cleanup-slot-locks`, chạy mỗi phút) và dùng `DateTimeHelper.GetUtcNow()` thay vì `TimeProvider` (P6-22).

**Giải pháp:**
- `IBookingRepository.GetExpiredUnpaidIdsAsync(DateTime nowUtc, int take)`: id đơn `PENDING`, hóa đơn `UNPAID`, `expires_at <= now` (C4), hạn sớm nhất trước.
- `BookingJob.CancelExpiredPendingBookingsAsync`: lấy tối đa 100 id (C6); gọi `ExpireUnpaidAsync` từng đơn; một đơn lỗi (exception) ⇒ log Error kèm id rồi làm tiếp; log tổng: số ứng viên, đã hủy, bỏ qua, lỗi. Giữ `[DisableConcurrentExecution(10)]`; bọc `try/catch` ngoài cùng như job cũ.
- `BookingJob` nhận thêm `IBookingRepository`, `IBookingCancellationService`, `ISlotInterestNotificationService`, `TimeProvider`; bỏ phần cũ của Job-01 (cập nhật entity, xóa slot lock toàn hệ thống, phát sự kiện). Hàm `NotifySlotInterestedUsersAsync` xóa sau khi Task 5 chuyển Job-04 sang dịch vụ chung.

**C5 — bảo đảm không có slot lock thừa gây hại (đã đối chiếu mã):**

| Loại slot lock "thừa" | Có gây hại không | Vì sao |
| :--- | :--- | :--- |
| Hết hạn nhưng chưa bị xóa (tối đa ~1 phút tới lần chạy Job-03) | **Không** | Mọi chỗ đọc slot lock lọc `expires_at > now` (`SlotLockRepository.GetByCourtAndTimeAsync`, `GetByCourtAndDateAsync` — kiểm đặt sân và lưới giờ). Trước khi tạo slot lock mới, đặt online và tại quầy xóa slot lock hết hạn của chi nhánh (`DeleteExpiredByBranchAsync`). Ràng buộc `excl_slot_locks_no_overlap` không xét hạn, nên trong khe vài ms giữa lúc xóa và lúc chèn có thể nổ `23P01`; lỗi này đã được bắt thành "sân đang được giữ" (`BookingService.cs:1575`), không bao giờ đặt trùng |
| Còn hạn nhưng đơn đã rời `PENDING`/`PENDING_APPROVAL` | **Không xảy ra** | Mọi đường rời trạng thái xóa slot lock theo booking trong cùng transaction: lõi hủy (`ReleaseCourtsAsync`), IPN thành công, TADSS từ chối, xác nhận ngay (a′), Quản lý duyệt 0 %, Quản lý từ chối, và hai đường hết hạn mới (Task 1) |
| Hạn slot lock lệch hạn đơn | **Không xảy ra** | Slot lock luôn được đặt cùng hạn với đơn: tạo đơn, gia hạn (`ExtendPaymentWindowAsync`), rút ngắn sau thẻ lỗi, chờ duyệt (tới `approval_expires_at`), sau duyệt |
| Khoảng chết: đơn đã quá hạn nhưng Job-01 chưa chạy (≤ 60 s) | **Không đặt trùng** | Slot lock đã hết hạn nhưng đơn vẫn `PENDING` với `booking_courts` còn hiệu lực ⇒ `HasOverlapAsync` vẫn chặn, lưới giờ hiện `LOCKED`. Khách khác đặt vào lúc đó nhận "đã được đặt" và được ghi chờ slot, rồi nhận email khi job hủy. Giống hệt Job-01 cũ |

Bỏ việc xóa toàn hệ thống trong Job-01 không làm slot lock tồn lâu hơn: Job-03 vẫn xóa mỗi phút với cùng điều kiện `expires_at <= now`; slot lock của đơn bị hủy do dịch vụ hủy xóa ngay.

**Vì sao:** từng đơn một transaction nhỏ: một đơn lỗi không kéo cả lô; giới hạn 100 đơn để một lần chạy không kéo dài khi BE vừa bật lại sau thời gian tắt (lần sau làm tiếp, job chạy mỗi phút). Đơn bất thường (`PENDING` có Payment `SUCCESS`) bị bỏ qua và log Error mỗi phút; phải có từ 100 đơn như vậy mới chặn được Job-01 — chỉ ghi nhận.

**Ảnh hưởng:** không đổi hành vi nhìn thấy với khách và nhân viên, ngoài Payment chờ thành `FAILED`, có `cancel_reason`, link "slot đã trống" về trang chủ, và đơn quá hạn đúng mốc bằng được hủy.

**Kiểm:** `Unit/Jobs/BookingJobExpiryTests` (gọi dịch vụ đúng từng id, đúng thứ tự; một đơn lỗi không chặn đơn sau; không có đơn ⇒ không gọi; log tổng đủ bốn số); `Integration/BookingStateGateRepositoryTests` (`GetExpiredUnpaidIdsAsync` lấy đúng đơn ở ba mốc `<`, `=`, `>` now; bỏ đơn chờ duyệt / đã trả; `take`; `IsPaymentWindowOverAsync` đổi ngay khi hạn được gia hạn; `IsApprovalWindowOverAsync` ở biên `= now`); C5 trên DB thật (Task 11): sau khi job hủy không còn slot lock nào của đơn; slot lock hết hạn của đơn khác không chặn đơn mới; đặt vào khoảng chết bị từ chối và được ghi chờ slot, job hủy xong thì người đó nhận email.

### Task 4 — Job hết hạn duyệt (A, C6)

**Vấn đề:** đơn chờ duyệt quá `approval_expires_at` hiện giữ sân mãi: không ai hủy, khách không biết, khách khác không đặt được.

**Giải pháp:** `Jobs/Interfaces/IBookingApprovalJob.cs`, `Jobs/BookingApprovalJob.cs`:
```csharp
// Hủy đơn chờ duyệt quá hạn duyệt (APPROVAL_EXPIRED); không bao giờ tự duyệt
Task ExpireApprovalsAsync();
```
`IBookingRepository.GetExpiredApprovalIdsAsync(DateTime nowUtc, int take)` (đơn `PENDING_APPROVAL`, `approval_expires_at <= now`, sớm nhất trước, dùng chỉ mục `idx_bookings_pending_approval` có sẵn); tối đa 100 đơn; gọi `ExpireApprovalAsync` từng đơn; một đơn lỗi không chặn đơn khác. **Không** nhận `ITadssClient` (outcome do job báo kết quả gửi). `[DisableConcurrentExecution(10)]`. `HangfireExtensions`: đăng ký DI cạnh các job khác (trước kiểm `Hangfire:Enabled`, để host test gọi được) và lịch `expire-approvals`, `* * * * *`, múi giờ VN.

**Vì sao:** spec 7.11; "không bao giờ tự duyệt" được bảo đảm bằng cấu trúc: job không có đường nào gọi TADSS hay chuyển đơn sang `PENDING`.

**Ảnh hưởng:** đơn chờ duyệt quá hạn bị hủy trong vòng 1 phút sau hạn, sân trả lại, khách nhận email.

**Kiểm:** `Unit/Jobs/BookingApprovalJobTests` (như Task 3; constructor không có `ITadssClient`); repository ở biên `= now`, bỏ đơn còn hạn và đơn đã duyệt.

### Task 5 — Job-04 và Job-02 ghi trạng thái có điều kiện (B6, B7, B3)

**Bước 0 — test ghi lại hành vi hiện tại (làm trước khi viết lại, chốt 27/10):** Job-02 và Job-04 hiện **không có test nào** (chỉ Job-01 được gọi trong test: `ApprovalTestBase`, `OnlineBookingPromotionTests`). Trước khi sửa, viết `Integration/BookingFinishAndNoShowJobTests` chạy hai job **bằng mã cũ** trên DB thật (Testcontainers), phải xanh với mã cũ, để việc viết lại không làm lệch đường chạy bình thường:
- Job-04: đơn `CONFIRMED` và `PAID_ONLINE` chưa check-in quá giờ chơi + 15' ⇒ `NO_SHOW`, `booking_courts.is_active = false`, sân về `AVAILABLE`; đơn còn trong ân hạn 15' ⇒ giữ nguyên; đơn đã check-in (`IN_PROGRESS`) ⇒ giữ nguyên; sân `SUSPENDED` không bị trả về `AVAILABLE`; sân còn đơn khác đang giữ ⇒ không trả.
- Job-02: đơn `IN_PROGRESS` hết giờ, hóa đơn `PAID` ⇒ `COMPLETED`; hóa đơn chưa đủ (`UNPAID`, `PARTIALLY_PAID`) ⇒ `PENDING_PAYMENT`; hóa đơn chỉ đổi `updated_at`; chưa hết giờ ⇒ giữ nguyên; `booking_courts` tắt, sân về `AVAILABLE` theo cùng luật trên.
Sau khi viết lại (phần dưới), cùng bộ test này phải giữ xanh; chỉ được đổi assert ở chỗ hành vi đổi đúng plan (link "slot đã trống", log), ghi lý do trong test.

**Vấn đề (đã kiểm mã 26/10):**
- Job-04 (`DetectNoShowBookingsAsync`, `BookingJob.cs:447`) nạp mọi đơn `CONFIRMED`/`PAID_ONLINE` chưa check-in, lọc trong bộ nhớ những đơn quá giờ chơi + 15', đổi `Status = NO_SHOW` trên entity rồi `SaveChanges` một lần cho cả lô. Câu `UPDATE` không có điều kiện trạng thái và `Booking` không có concurrency token.
- Nhân viên được check-in tới đúng giờ chơi + 15' (`BookingService.cs:984`); Job-04 chạy `*/5` nên chạy **đúng phút :15 và :45** — trùng mốc hết hạn check-in của mọi khung giờ bắt đầu lúc :00 và :30. Khách đến muộn, nhân viên bấm check-in ở giây cuối; Job-04 đọc đơn trước khi lần check-in commit rồi ghi đè ⇒ đơn thành `NO_SHOW` mà vẫn có `checked_in_at`; `booking_courts` bị tắt và sân về `AVAILABLE` trong khi khách đang chơi ⇒ người khác đặt được sân đó; TADSS nhận nhãn `NO_SHOW` sai.
- Nhân viên hủy được đơn `CONFIRMED`/`PAID_ONLINE` cả sau giờ chơi (`StaffCancellable` không chặn giờ). Hủy commit trong lúc Job-04 đang chạy ⇒ đơn `CANCELLED_PENDING_REFUND` có Refund bị ghi đè thành `NO_SHOW` ⇒ không xác nhận hoàn tiền được (xác nhận cần `CANCELLED_PENDING_REFUND`), khách mất tiền hoàn.
- Tính "sân còn bận" (`GetBusyCourtIdsAsync`) loại trừ mọi đơn đang xử lý trong lô, nên một đơn vừa check-in cùng sân không được tính là bận.
- Job-04 cũng gửi email "slot đã trống" bằng hàm có link 404 (B3).

**Giải pháp:**
- `IBookingRepository`:
  ```csharp
  // CONFIRMED/PAID_ONLINE chưa check-in ⇒ NO_SHOW, chỉ khi trạng thái vẫn là expectedStatus và checked_in_at còn trống; gọi trong transaction
  Task<bool> MarkNoShowAsync(Guid bookingId, BookingStatus expectedStatus, DateTime nowUtc);
  ```
  Một `ExecuteUpdateAsync` với điều kiện `id`, `status = expectedStatus`, `checked_in_at IS NULL`; ghi `status`, `updated_at`.
- `DetectNoShowBookingsAsync` giữ cách chọn ứng viên và ân hạn 15' như cũ, lấy "bây giờ" từ `TimeProvider`. Mỗi đơn một transaction Read Committed: `MarkNoShowAsync` (câu điều kiện đầu tiên, khóa dòng booking) ⇒ thắng thì tính "sân còn bận" **sau khi thắng, chỉ loại trừ chính đơn đó**, rồi tắt `booking_courts` và trả sân về `AVAILABLE` theo luật cũ (không đụng sân `SUSPENDED`/`INACTIVE`) ⇒ commit ⇒ sau commit phát `BookingNoShow` và báo người chờ slot qua `ISlotInterestNotificationService` (sửa B3). Thua ⇒ log Information, không ghi gì, không thông báo. Một đơn lỗi không chặn đơn khác; log tổng: ứng viên, đã đánh, bỏ qua vì đổi trạng thái, lỗi.
- Xóa `NotifySlotInterestedUsersAsync` khỏi `BookingJob`.

**Vì sao:** cùng cách đã áp cho Job-01 (B2) và mọi luồng 6A-4: câu cập nhật có điều kiện chạy đầu tiên vừa khóa dòng vừa quyết định bên thắng. Phương án khác: thêm concurrency token cho `Booking` → đổi schema và làm mọi `SaveChanges` trên booking có thể ném lỗi ⇒ không chọn. Job-02 cùng kiểu lỗi, sửa cùng task (B7, chốt 27/10, phần dưới).

**Ảnh hưởng:** đơn vừa check-in hoặc vừa bị nhân viên hủy không còn bị đánh `NO_SHOW`; sân của khách đang chơi không bị nhả; email "slot đã trống" sau `NO_SHOW` dẫn về trang chủ. Lịch chạy, ân hạn 15', sự kiện `BookingNoShow` giữ nguyên.

**Job-02 (B7, chốt 27/10, đã kiểm mã):**
- *Vấn đề:* `ProcessExpiredActiveBookingsAsync` (`BookingJob.cs:272`, chạy mỗi phút) nạp mọi đơn đang chơi, lọc đơn hết giờ trong bộ nhớ rồi `SaveChanges` không điều kiện. Trạng thái nguồn: `IN_PROGRESS` (hai nhánh `CONFIRMED`/`PAID_ONLINE` có `checked_in_at` chỉ còn là mã cũ vì check-in luôn chuyển `IN_PROGRESS`). Job ghi: `status` + `updated_at` của booking; **chỉ** `updated_at` của hóa đơn; `booking_courts.is_active = false`; sân về `AVAILABLE` (trừ sân bận, `SUSPENDED`, `INACTIVE`); SignalR sau khi lưu. Khách hay ra về đúng giờ kết thúc, đúng phút job chạy; nhân viên check-out lúc đó (commit `COMPLETED`, hóa đơn `PAID`) ⇒ job ghi đè thành `PENDING_PAYMENT`: đơn đã trả đủ hiện "chờ thu tiền", báo cáo doanh thu (lọc `COMPLETED`) thiếu. Không thu trùng: check-out lại tính còn 0 (`BookingService.cs:1092`). Job còn chọn `COMPLETED` hay `PENDING_PAYMENT` theo hóa đơn đọc **trước khi khóa**.
- *Giải pháp:* mỗi đơn một transaction Read Committed: `LockAsync` ⇒ đọc lại trạng thái booking và trạng thái hóa đơn dưới khóa; trạng thái đã đổi ⇒ bỏ qua, không ghi gì ⇒ câu cập nhật có điều kiện sang `COMPLETED` (hóa đơn `PAID`) hoặc `PENDING_PAYMENT` ⇒ tắt `booking_courts`, trả sân theo luật cũ, "sân còn bận" chỉ loại trừ chính đơn này ⇒ commit ⇒ phát SignalR. Check-out và thêm dịch vụ đều khóa booking trước ⇒ thứ tự khóa thống nhất. **Giữ nguyên:** đơn trả cọc online luôn để hóa đơn `PARTIALLY_PAID` nên vẫn sang `PENDING_PAYMENT` (luật C1, không đổi trong B7); hóa đơn chỉ đổi `updated_at`; lịch chạy.
- *Ảnh hưởng:* lần check-out vừa xong không còn bị ghi đè; báo cáo doanh thu không thiếu đơn.

**Kiểm:** `Integration/NoShowRepositoryTests` (`MarkNoShowAsync`: đúng đơn ⇒ `true`; đã check-in, đã hủy, trạng thái khác ⇒ `false`, không đổi gì); `Unit/Jobs/BookingJobNoShowTests` (một đơn thua không chặn đơn sau; thua thì không thông báo; người chờ slot nhận link trang chủ; log tổng); `Unit/Jobs/BookingJobFinishTests` (Job-02: đọc lại dưới khóa, đơn đổi trạng thái thì bỏ qua, hóa đơn `PAID` ⇒ `COMPLETED`, còn lại `PENDING_PAYMENT`); đồng thời có điểm chặn trên DB thật ở Task 12 (BE-IT-26).

---

## Phần C — Báo kết quả về TADSS

### Task 6 — Ánh xạ outcome và phân loại phản hồi (A, C1, C3)

**Vấn đề:**
- Chưa có chỗ nào quyết định "đơn này báo outcome gì, `occurredAt` là gì".
- **C1:** IPN luôn để hóa đơn `PARTIALLY_PAID` kể cả khi khách trả đủ (`PaymentResultService.cs:146`), và Job-02 chỉ chuyển `COMPLETED` khi hóa đơn `PAID`. Nên gần như mọi đơn online có quyết định TADSS, sau giờ chơi, đều nằm ở `PENDING_PAYMENT` tới khi nhân viên bấm check-out. Nếu chỉ báo khi `COMPLETED`, đơn mà nhân viên quên check-out sẽ **không bao giờ** được báo ⇒ TADSS thiếu đúng nhóm "khách có đến" ⇒ tỷ lệ không đến bị thổi phồng khi đánh giá.
- **C1 — nhãn phải khớp dữ liệu:** nhãn `COMPLETED` khẳng định khách đã đến, `NO_SHOW` khẳng định khách không đến. Trạng thái đơn chỉ là gián tiếp; dữ liệu trực tiếp là `checked_in_at` (mục 0.3). Hai đường đã biết làm trạng thái lệch dữ liệu: lỗi B6 (đơn `NO_SHOW` có `checked_in_at`, đã sửa ở Task 5 nhưng dữ liệu cũ có thể còn) và cạnh thừa E6.
- **C3:** spec chỉ nói "gặp 429 thì dừng chu kỳ; lỗi khác ⇒ để chu kỳ sau". Nếu TADSS đang sập (timeout, 5xx) hoặc đường hầm ngrok rớt (trả trang lỗi HTML), bắn tiếp 19 request còn lại cũng chỉ lỗi y hệt.

**Giải pháp:**
- `Integrations/Tadss/TadssOutcomeMapper.cs` (static, cạnh `TadssDecisionRequestMapper`): `TadssOutcomePlan Plan(Booking booking, DateTime nowUtc)`; `record TadssOutcomePlan(TadssOutcomePlanKind Kind, TadssOutcomeRequestDto? Request, string? Problem)`; `enum TadssOutcomePlanKind { Send, NotDue, Skip, Invalid }`.

  | Booking | Điều kiện dữ liệu | Outcome | `occurredAt` |
  | :--- | :--- | :--- | :--- |
  | `COMPLETED`, `PENDING_PAYMENT` (C1) | **`checked_in_at` có** | `COMPLETED` | Giờ kết thúc muộn nhất của đơn (giờ VN), offset `+07:00`; chưa tới giờ đó ⇒ `NotDue` |
  | `NO_SHOW` | **`checked_in_at` trống** | `NO_SHOW` | Như trên |
  | Hủy (`CANCELLED`, `CANCELLED_PENDING_REFUND`, `CANCELLED_REFUNDED`) với `PAYMENT_EXPIRED` / `APPROVAL_EXPIRED` | — | Cùng tên | `cancelled_at`, offset `+00:00` |
  | Hủy với `APPROVAL_REJECTED` / `POLICY_REJECTED` | — | `REJECTED` | Như trên |
  | Hủy với `CUSTOMER_CANCELLED` / `STAFF_CANCELLED` | — | `CANCELLED_BY_CUSTOMER` / `CANCELLED_BY_STAFF` | Như trên |
  | Hủy với `SYSTEM_CLEANUP` | — | — | `Skip` (không gửi, theo spec) |
  | Lệch điều kiện dữ liệu ở hai dòng đầu; hủy thiếu `cancel_reason` / `cancelled_at`; đơn không có sân | — | — | `Invalid` (nêu rõ lệch gì) |
  | Trạng thái khác | — | — | `NotDue` |

  Giờ kết thúc dựng bằng `new DateTimeOffset(date.ToDateTime(end), TimeSpan.FromHours(7))`, không qua giờ máy chủ. Mỗi đơn chỉ có một ngày chơi và giờ kết thúc luôn sau giờ bắt đầu (validate của DTO đặt sân), nên không có khung qua nửa đêm.
- `Integrations/Tadss/TadssOutcomeDelivery.cs` (static): `TadssOutcomeDeliveryKind Classify(TadssCallResult<TadssOutcomeResponseDto> result)`:

  | Kết quả | Loại | Job làm gì |
  | :--- | :--- | :--- |
  | 200; 409 mã `OUTCOME_ALREADY_RECORDED` | `Recorded` | Đánh dấu đã báo |
  | 404 mã `DECISION_NOT_FOUND`; 400 mã `VALIDATION_ERROR` / `DOMAIN_INVARIANT_VIOLATION` | `Rejected` (lỗi của riêng đơn) | Log Warning, để chu kỳ sau, làm tiếp đơn khác |
  | 404/400 **không có** các mã trên (VD trang lỗi HTML của ngrok); 400 `MISSING_HEADER`; 401; 409 mã khác; 413; 415; 429; 5xx; `Timeout`; `Network`; `InvalidResponse` | `SystemFailure` (lỗi của cả hệ thống) | Dừng chu kỳ ngay, để chu kỳ sau |

**Vì sao:**
- C1: dữ liệu khách đến chốt từ lúc check-in và không bao giờ đổi; `occurredAt` là giờ kết thúc chơi nên gửi lúc nào cũng cùng nội dung ⇒ không gây 409 khi đơn sau đó được check-out. Đơn `COMPLETED` do check-out sớm vẫn chờ tới giờ kết thúc để `occurredAt` không nằm ở tương lai. Lệch dữ liệu thì **giữ lại, không đoán**: không gửi, không đánh dấu, log Error để người vận hành xem; sửa dữ liệu xong thì chu kỳ sau tự gửi.
- C3: chỉ khi TADSS thật sự trả lời "quyết định này không có / nội dung này sai" mới là lỗi riêng đơn; mọi thứ khác (kể cả 404 do ngrok) là hệ thống chưa sẵn sàng. So sánh với cách làm phổ biến ở Task 9.

**Ảnh hưởng:** chỉ mã mới, không đổi hành vi có sẵn.

**Kiểm:** `Unit/Integrations/Tadss/TadssOutcomeMapperTests` (BE-UT-09: mọi dòng bảng; `PENDING_PAYMENT` có check-in ⇒ `COMPLETED`; `COMPLETED`/`PENDING_PAYMENT` **thiếu** `checked_in_at` ⇒ `Invalid`; `NO_SHOW` **có** `checked_in_at` ⇒ `Invalid`; ba trạng thái hủy; check-out sớm `NotDue` rồi `Send` đúng giờ kết thúc; khung `23:00–23:59`; đơn hai sân lấy giờ muộn nhất; giữ nguyên µs của `cancelled_at`; gọi hai lần ra cùng request); `TadssOutcomeDeliveryTests` (mỗi dòng phân loại, gồm 404 HTML không mã ⇒ `SystemFailure`).

### Task 7 — Client kiểm response và timeout riêng của outcome (B4, C8)

**Vấn đề:** `TadssClient` kiểm response của đánh giá (đúng `reservationId`, đủ trường) và của override (đúng `decisionId`, đúng hành động), nhưng không kiểm response của outcome. Outcome không hiển thị ở UI; nó là **nhãn đánh giá** (mục 0.3). Khi BE nhận 200 thì đánh dấu `tadss_outcome_reported_at` và **không bao giờ gửi lại**. Một 200 sai nội dung (VD ngrok trỏ nhầm sang một TADSS khác, hoặc proxy trả trang mặc định có mã 200) sẽ làm nhãn **mất vĩnh viễn** mà không ai biết.

**Giải pháp:**
- `ReportOutcomeAsync` kiểm `decisionId` và `outcome` trùng bản đã gửi, `occurredAt` trùng sau khi cắt về µs; lệch ⇒ `InvalidResponse` (job coi là lỗi hệ thống, Task 6), không đánh dấu.
- **C8 (chốt 27/10):** lệnh gửi outcome dùng timeout riêng 5 giây (hằng số trong `TadssClient`, không thêm khóa cấu hình); lệnh đánh giá và override giữ `TimeoutMs` (800 ms). Lý do: healthcheck của TADSS chỉ gọi `/health/live` (không chạm DB), DB Neon của TADSS tự tạm dừng khi vắng và mất 1–3 giây để thức; TADSS hủy thao tác ghi khi BE ngắt kết nối; với 800 ms và C3, lần gửi đầu mỗi chu kỳ có thể timeout lặp lại ⇒ outcome bị trì hoãn kéo dài. Job chạy nền, không ai chờ; nhờ C3 mỗi chu kỳ chờ tối đa 5 giây. 5 giây là giá trị khởi đầu, xem lại sau khi chạy thật.
- An toàn khi timeout (đã kiểm phía TADSS): ghi outcome là một `INSERT` nguyên tử; đã ghi mà BE mất response thì lần gửi lại cùng nội dung nhận 200 bản đã lưu (`OutcomeService.ResolveExisting`); hai lần gửi đua nhau thì `UNIQUE(decision_id)` chọn bên thắng (test TADSS `Outcome_RecordReplayConflict`, `GovernanceStoreTests`). Timeout ⇒ không đánh dấu.

**Vì sao:** cùng nguyên tắc spec 7.3 đã áp cho hai hàm kia. Cắt µs là theo contract, không vì test dễ: TADSS lưu `timestamptz` (độ chính xác µs) và so trùng nội dung sau khi cắt µs (`OutcomeService`, INTEGRATION_SPEC §3.7); .NET giữ tới 100 ns nên không cắt thì response đúng vẫn bị coi là lệch.

**Ảnh hưởng:** không.

**Kiểm:** `Unit/Integrations/Tadss/TadssClientTests` (outcome dùng timeout 5 giây, đánh giá vẫn `TimeoutMs`; lệch `decisionId` / `outcome` / `occurredAt` ⇒ `InvalidResponse`; lệch chỉ dưới 1 µs ⇒ thành công; khớp ⇒ thành công; 200 với body không phải JSON ⇒ `InvalidResponse`; mẫu `outcome-response` vẫn qua schema, CT-04); BE-IT (Task 13): stub trả 200 với `decisionId` khác ⇒ đơn không được đánh dấu, chu kỳ dừng, chu kỳ sau (stub đúng) đánh dấu.

### Task 8 — Chọn đơn cần báo (A, C2)

**Vấn đề:** mỗi chu kỳ chỉ gửi 20 đơn (P6-19), nên **thứ tự chọn** quyết định đơn nào bị chờ. Spec không nói thứ tự. Tình huống thật: DB TADSS bị reset giữa đợt đánh giá (hoặc BE trỏ sang một TADSS khác) ⇒ mọi quyết định cũ trả 404 vĩnh viễn. Nếu chọn "cũ nhất trước", 20 đơn cũ luôn đứng đầu hàng, mỗi chu kỳ đều thử lại và đều lỗi ⇒ **đơn mới không bao giờ được báo**, dữ liệu đánh giá dừng âm thầm.

**Giải pháp (D1):** chọn theo `updated_at` **mới nhất trước**, dùng chỉ mục `idx_bookings_outcome_pending` (mục 0.3: cột cũ do mã gán ở mỗi lần ghi; chỉ mục mới của Phase 6).
```csharp
// Đơn có quyết định TADSS, chưa báo, đã kết thúc (ba trạng thái hủy, hoặc COMPLETED/NO_SHOW/PENDING_PAYMENT đã qua giờ kết thúc theo giờ VN);
// mới cập nhật trước; kèm BookingCourts
Task<IReadOnlyList<Booking>> GetOutcomeCandidatesAsync(DateOnly todayVn, TimeOnly nowTimeVn, int take);

// Đánh dấu đã báo; chỉ khi chưa đánh dấu (hai lần chạy chồng nhau không ghi đè mốc đầu)
Task<bool> MarkOutcomeReportedAsync(Guid bookingId, DateTime nowUtc);
```
"Đã qua giờ kết thúc": `booking_date < todayVn`, hoặc `= todayVn` và giờ kết thúc muộn nhất `≤ nowTimeVn` (mapper kiểm lại).

**Điều được bảo đảm và giới hạn (ghi đúng, không nói quá):**
- Bảo đảm: đơn vừa kết thúc luôn đứng đầu hàng nên được báo ở chu kỳ kế tiếp, kể cả khi có hàng trăm đơn cũ bị từ chối vĩnh viễn (kịch bản D1). Đơn đã báo không bao giờ gửi lại.
- Giới hạn: đơn bị từ chối (`Rejected`) và đơn lệch dữ liệu (`Invalid`) không đổi gì trong DB nên vẫn chiếm chỗ trong 20 suất. Nếu có **từ 20 đơn như vậy mới hơn** một đơn hợp lệ còn tồn (VD TADSS sập vài giờ để lại đơn tồn, sau đó lại có ≥ 20 đơn bị từ chối vĩnh viễn), đơn tồn đó không tới lượt cho tới khi các đơn chặn được xử lý. Cần đồng thời hai điều kiện hiếm; mỗi chu kỳ có log tổng (Task 9) cho thấy số đơn bị từ chối/lệch và tuổi của đơn chưa báo cũ nhất để phát hiện. Chữa tận gốc cần lưu trạng thái thử lại theo từng đơn (đổi schema) — ghi ở Task 9 "Hướng về sau".
- Thông lượng: tối đa 20 đơn mỗi 5 phút (≈ 4 đơn/phút) theo P6-19; đủ cho quy mô SmashCourt (vài chục đơn mỗi ngày).
- Thứ tự là gần đúng: `updated_at` là lần sửa cuối, không phải lúc kết thúc (VD tiền về sau khi hủy làm đơn đã hủy lên đầu hàng). Không ảnh hưởng tính đúng vì `occurredAt` cố định và không có yêu cầu thứ tự gửi.

**Vì sao:**
- Ví dụ với số liệu: 300 quyết định cũ lỗi 404, mỗi 5 phút có 3 đơn mới kết thúc. "Mới nhất trước": 3 đơn mới luôn nằm trong 20 suất đầu ⇒ được báo ngay. "Cũ nhất trước": 3 đơn mới đứng sau 300 đơn cũ ⇒ không bao giờ tới lượt.
- Phương án đã cân nhắc và bỏ (bản 2): thêm 2 cột đếm lượt thử và hạn thử lại. Chính xác hơn nhưng phải đổi schema, có script DB phải chạy trước khi deploy (quên chạy ⇒ mọi truy vấn booking lỗi).

**Ảnh hưởng:** không đổi schema; chỉ thêm truy vấn.

**Kiểm (`Integration/OutcomeCandidateRepositoryTests`, PostgreSQL thật):** lấy đơn chơi xong hôm qua, hôm nay đã qua giờ kết thúc, đơn ở cả ba trạng thái hủy; bỏ đơn hôm nay chưa tới giờ kết thúc, đơn đã báo, đơn không có quyết định, đơn chưa kết thúc; thứ tự `updated_at` giảm dần; `take`; đánh dấu lần hai trả `false` và giữ mốc đầu. Bảo đảm và giới hạn ở Task 13.

### Task 9 — Job báo kết quả (A, C3)

**Vấn đề:** chưa có job; TADSS không nhận được kết quả thật của đơn để đánh giá quyết định.

**Giải pháp:** `Jobs/Interfaces/ITadssOutcomeJob.cs`, `Jobs/TadssOutcomeJob.cs`:
```csharp
// Báo kết quả các đơn đã kết thúc về TADSS: tuần tự, tối đa 20 đơn mỗi chu kỳ; chạy cả khi tắt cờ đánh giá
Task ReportOutcomesAsync();
```
Nhận `IBookingRepository`, `ITadssClient`, `TimeProvider`, `ILogger`; `[DisableConcurrentExecution(10)]`; bọc `try/catch` ngoài cùng; không đọc `EnableTadssDecision` (Q7). Một chu kỳ: lấy tối đa 20 ứng viên, xử lý **tuần tự**:
- `Skip` ⇒ đánh dấu đã báo, log Warning; `NotDue` ⇒ bỏ qua; `Invalid` ⇒ log Error kèm `bookingId`, `decisionId` và lệch gì, để chu kỳ sau.
- `Send` ⇒ gửi rồi theo `Classify`: `Recorded` ⇒ đánh dấu (409 thêm Warning ghi outcome đã gửi); `Rejected` ⇒ log Warning kèm `bookingId`, `decisionId`, HTTP status, mã lỗi, làm tiếp đơn sau; `SystemFailure` ⇒ log Warning (429 kèm `Retry-After`), **dừng chu kỳ**.
- Log tổng mỗi chu kỳ (một dòng có cấu trúc): số ứng viên, đã báo, bỏ qua, chưa tới giờ, lệch dữ liệu, bị từ chối, lý do dừng (nếu có), tuổi của đơn chưa báo cũ nhất trong lô.
`HangfireExtensions`: DI và lịch `report-tadss-outcomes`, `*/5 * * * *`.

**Vì sao:** spec 7.11, P6-19; gửi tuần tự để không tạo đợt request dồn dập cạnh lượt đặt sân.

**C3 — so với cách các hệ thống khác vận hành giữa các service:**

| Cách làm phổ biến | Nguồn tham khảo | Gói này | Hướng về sau |
| :--- | :--- | :--- | :--- |
| Phân loại lỗi tạm thời (thử lại) và lỗi vĩnh viễn (không thử lại cùng nội dung) | Google API Design Guide / mã trạng thái gRPC (`UNAVAILABLE` thử lại, `INVALID_ARGUMENT`/`NOT_FOUND` không) | `SystemFailure` vs `Rejected`, theo mã lỗi của TADSS | — |
| Circuit breaker: lỗi liên tiếp thì ngắt, sau một thời gian thử một request rồi mới mở lại | Michael Nygard, *Release It!*; Martin Fowler, "CircuitBreaker" | Dừng chu kỳ ở lỗi hệ thống = ngắt trong 5 phút; chu kỳ sau là lần thử lại | Trạng thái "nửa mở": chỉ gửi 1 request thử, thành công mới gửi tiếp |
| Tôn trọng `Retry-After` | RFC 9110 §10.2.3 | Dừng chu kỳ; chu kỳ sau cách 5 phút (TADSS trả `Retry-After` vài giây) | Nhớ mốc `Retry-After` trong bộ nhớ, bỏ qua các chu kỳ trước mốc nếu mốc dài hơn 5 phút |
| Thử lại giãn cách theo cấp số nhân có jitter | AWS Architecture Blog, "Exponential Backoff And Jitter" | Chu kỳ cố định 5 phút, một job duy nhất nên không có đợt dồn | Giãn cách theo từng đơn (cần trạng thái mỗi đơn) |
| Idempotency: gửi lại an toàn | Stripe, "Idempotent requests" | TADSS chống trùng theo nội dung; `occurredAt` cố định; key `{decisionId}:OUTCOME` | — |
| Transactional outbox: ghi "cần gửi" cùng transaction với thay đổi nghiệp vụ, một worker gửi đi | microservices.io, "Transactional outbox" | Dòng booking chính là outbox: `tadss_decision_id` có và `tadss_outcome_reported_at` trống = chưa gửi | Bảng outbox riêng nếu có thêm loại sự kiện cần gửi |
| Dead-letter queue: sau N lần thất bại vĩnh viễn thì chuyển ra hàng riêng để người xem, không chặn hàng chính | Amazon SQS dead-letter queues | Chưa có; giới hạn ở Task 8 | Thêm `outcome_attempts`, `outcome_next_attempt_at`, `outcome_last_error` (đổi schema) ⇒ hết giới hạn của Task 8; đề xuất cho giai đoạn sau Phase 6 |

Kết luận: thiết kế hiện tại là dạng tối giản đúng của các mẫu trên cho quy mô vài chục đơn mỗi ngày và một instance BE. Khi số đơn tăng hoặc có thêm loại sự kiện cần gửi, bước tiếp theo là trạng thái thử lại theo từng đơn kèm dead-letter (cột ở bảng trên).

**Ảnh hưởng:** TADSS nhận outcome; không đổi gì phía khách.

**Kiểm (`Unit/Jobs/TadssOutcomeJobTests`, mock + `FakeTimeProvider`):**

| Ca | Mong đợi |
| :--- | :--- |
| 25 ứng viên đều 200 | Repository được gọi với `take = 20`; gửi tuần tự đúng thứ tự; 20 lần đánh dấu |
| 429 ở đơn thứ 3 | Đơn 1, 2 đánh dấu; đơn 3 không ghi; đơn 4…20 **không** được gửi |
| 503 / timeout / mạng / body hỏng / 401 / 404 HTML / 200 sai `decisionId` | Như 429 |
| 404 `DECISION_NOT_FOUND` ở đơn 1, 200 ở đơn 2 | Đơn 1 không đánh dấu, Warning có `bookingId` và mã lỗi; đơn 2 đánh dấu; chu kỳ chạy tiếp |
| 409 `OUTCOME_ALREADY_RECORDED` | Đánh dấu, Warning |
| `SYSTEM_CLEANUP` | Không gọi TADSS, đánh dấu, Warning |
| `Invalid` (lệch `checked_in_at`) | Không gọi TADSS, không đánh dấu, log Error |
| Cờ đánh giá tắt | Vẫn gửi |
| Đánh dấu trả `false` (lần chạy khác đã đánh dấu) | Không lỗi |
| Repository ném lỗi | Bắt, log Error, không lan ra Hangfire |
| Log tổng | Đúng các con số của chu kỳ |

---

## Phần D — Kiểm thử trên host thật và tài liệu

### Task 10 — `TadssStub`: endpoint outcome

`OutcomePath(Guid)`, `ReplyOutcome(Guid)` (200 trả lại đúng nội dung đã nhận, như TADSS thật khi ghi mới), `ReplyOutcomeForAny()`, `ReplyOutcomeProblem(Guid, int status, string? code, string? retryAfter = null)`, `ReplyOutcomeRaw(Guid, int status, string body, string contentType)` (trang lỗi HTML, 200 sai nội dung), `OutcomesFor(Guid)`, `AllOutcomes()`.

### Task 11 — BE-IT-08, Job-01, Job-04 và Job-02 trên host thật

**Kịch bản (`Integration/Http/ExpiryJobFlowTests`):**
- **Hết hạn duyệt:** đơn chờ duyệt có khuyến mãi; dời `approval_expires_at` về quá khứ; chạy job ⇒ `CANCELLED`, `SYSTEM`, `APPROVAL_EXPIRED`, `cancelled_at` là mốc UTC trong lúc chạy, slot lock bị xóa, `booking_courts` hết hiệu lực, hóa đơn `EXPIRED`, `UsedCount` không đổi; khách nhận đúng một email hết hạn duyệt; người chờ slot nhận email link trang chủ; đơn rời hàng chờ; TADSS giả nhận **0** request override; đơn còn hạn trong cùng lần chạy không đổi; Quản lý duyệt sau đó ⇒ 409 `APPROVAL_EXPIRED`, không gọi TADSS.
- **Hết hạn thanh toán (B1, C7):** đơn quá hạn có Payment `PENDING` ⇒ `PAYMENT_EXPIRED`, Payment `FAILED`, hóa đơn `EXPIRED`, slot nhả, khách **không** nhận email hủy; đơn chưa có mức cọc và chưa có Payment ⇒ `SYSTEM_CLEANUP`; đã duyệt cọc nhưng khách chưa bấm "Thanh toán cọc" ⇒ `PAYMENT_EXPIRED`, và lần đặt sau của khách đó gửi TADSS `paymentExpiredCount` tăng 1.
- **Tiền về sau khi job hủy (C7):** job hủy (Payment thành `FAILED`) rồi IPN thành công của link đó ⇒ Payment `SUCCESS`, booking `CANCELLED_PENDING_REFUND`, Refund 100 %; nhân viên xác nhận trả tiền mặt ⇒ `CANCELLED_REFUNDED`, hóa đơn `REFUNDED`.
- **Slot lock (C5):** sau khi job hủy không còn slot lock nào của đơn; đơn khác đặt cùng sân giờ đó thành công; đặt vào khoảng chết (đơn đã quá hạn, job chưa chạy) bị từ chối và được ghi chờ slot, chạy job xong thì người đó nhận email.
- **Job-04 (B6, B3):** đơn `PAID_ONLINE` không check-in quá giờ chơi + 15' ⇒ `NO_SHOW`, `booking_courts` tắt, người chờ slot nhận email link trang chủ; đơn đã check-in trong cùng lần chạy không đổi.
- **Job-02 (B7):** đơn `IN_PROGRESS` hết giờ, hóa đơn `PARTIALLY_PAID` ⇒ `PENDING_PAYMENT`; hóa đơn `PAID` ⇒ `COMPLETED`; nhân viên check-out sau đó thu đúng phần còn lại.
- Các test HTTP cũ có gọi `RunPaymentExpiryJobAsync` (6A-3, 6A-4; 12 chỗ) giữ xanh.

### Task 12 — Đồng thời có điểm chặn (DB thật, B2, B6, B7; BE-IT-26)

**Vì sao cần test kiểu này:** hai request bắn song song không ép được thứ tự xen kẽ mong muốn (chạy 100 lần có thể không lần nào gặp). Test mở transaction bằng tay để giữ khóa đúng lúc, nên kết quả ổn định.

**Mỗi ca khi job thua đều kiểm đủ trên DB** (không chỉ giá trị trả về): trạng thái và hạn của bên thắng; slot lock còn nguyên; `booking_courts` còn hiệu lực; hóa đơn không đổi; Payment không đổi; các cột `cancel_*` trống (hoặc giữ đúng giá trị của bên thắng); Refund không đổi; không có sự kiện SignalR hay email nào của job.

**Kịch bản (`Integration/Http/ExpiryConcurrencyTests`, hai scope DI của host):**
- **Job-01 thua lần gia hạn:** kết nối A mở transaction, gọi `ExtendPaymentWindowAsync` (giữ khóa booking), **chưa commit**; B chạy `ExpireUnpaidAsync` ⇒ sau 500 ms B vẫn đang chờ; A commit ⇒ B trả `false`; đơn còn `PENDING` với hạn mới; `GET /api/me/bookings/{id}` trả `expiresAt` mới. Bỏ bước kiểm lại dưới khóa (đột biến) ⇒ test đỏ.
- **Job-01 thắng trước:** B hủy xong; "Thanh toán lại" ⇒ 409, không tạo Payment.
- **Job-01 thua IPN:** A khóa booking và ghi `PENDING → PAID_ONLINE` chưa commit; B chạy `ExpireUnpaidAsync` ⇒ chờ; A commit ⇒ B `false`; đơn `PAID_ONLINE`, Payment `SUCCESS`; không có `40P01`.
- **Job hết hạn duyệt và Quản lý duyệt ở biên hạn** (dời hạn về `now + 1 giây`, chạy song song 5 lần): mỗi đơn hoặc bị hủy `APPROVAL_EXPIRED` và lần duyệt nhận 409, hoặc đã duyệt và job không hủy; không có 500.
- **Job-04 thua check-in (B6):** A gọi `CheckInAsync` của repository (giữ khóa) chưa commit; B chạy `MarkNoShowAsync` ⇒ chờ; A commit ⇒ B `false`; đơn `IN_PROGRESS`, `checked_in_at` giữ, sân `IN_USE`, `booking_courts` còn hiệu lực, không có `BookingNoShow`. Bỏ điều kiện `checked_in_at IS NULL` và trạng thái (đột biến) ⇒ test đỏ.
- **Job-04 thua nhân viên hủy (B6):** A hủy đơn `PAID_ONLINE` có hoàn tiền, chưa commit; B ⇒ chờ ⇒ `false`; đơn `CANCELLED_PENDING_REFUND`, Refund còn, nhân viên xác nhận hoàn được.
- **Job-04 thắng trước:** check-in sau đó ⇒ 409.
- **Hai đơn cùng sân (B6):** đơn X vừa check-in, đơn Y cùng sân khác giờ bị đánh `NO_SHOW` trong cùng lần chạy ⇒ sân vẫn `IN_USE`.
- **Job-02 thua check-out (B7):** A gọi check-out (giữ khóa booking) chưa commit; B chạy bước khóa của Job-02 ⇒ chờ; A commit ⇒ B đọc lại thấy `COMPLETED` ⇒ bỏ qua; đơn `COMPLETED`, hóa đơn `PAID`, số Payment không đổi, sân không bị đặt lại, đơn không nằm trong danh sách "chờ thu tiền" của dashboard. Bỏ bước đọc lại dưới khóa (đột biến) ⇒ test đỏ.
- **Job-02 thắng trước (B7):** đơn sang `PENDING_PAYMENT`; check-out sau đó chạy từ `PENDING_PAYMENT`, thu đúng phần còn lại.

### Task 13 — BE-IT-09 trên host thật

**Kịch bản (`Integration/Http/OutcomeJobFlowTests`):** đơn có quyết định dựng qua luồng thật (bật cờ trong test, TADSS giả trả `APPROVED`/`NEED_APPROVAL`/`REJECTED`).
- Mỗi loại đơn đã kết thúc gửi đúng một outcome đúng giá trị: `COMPLETED` (check-out), `COMPLETED` từ `PENDING_PAYMENT`, `NO_SHOW`, `CANCELLED_BY_CUSTOMER`, `CANCELLED_BY_STAFF`, `PAYMENT_EXPIRED` (cả đơn đã duyệt), `APPROVAL_EXPIRED`, `REJECTED` (Quản lý từ chối, và TADSS từ chối lúc đặt), đơn hủy rồi tiền về (`CANCELLED_PENDING_REFUND`); body qua `outcome-request.schema.json`; `occurredAt` đúng quy tắc.
- Chạy lần hai ⇒ không request mới. Check-out sớm ⇒ chưa gửi tới giờ kết thúc. Đơn không có quyết định ⇒ không gửi. Cờ tắt lúc chạy job ⇒ vẫn gửi. 25 đơn ⇒ chu kỳ đầu 20, chu kỳ sau 5.
- 429 ⇒ dừng chu kỳ, chu kỳ sau gửi tiếp. 404 HTML ⇒ dừng chu kỳ. 503 ⇒ chu kỳ sau gửi lại cùng nội dung. 200 sai `decisionId` (B4) ⇒ không đánh dấu, dừng chu kỳ.
- **C8:** stub trả chậm 2 giây ⇒ thành công (đột biến đổi về 800 ms ⇒ test đỏ); trả chậm 6 giây ⇒ timeout, không đánh dấu, chu kỳ dừng; chu kỳ sau stub trả 200 bản đã lưu ⇒ đánh dấu.
- **Bảo đảm của C2:** 25 đơn cũ bị 404 `DECISION_NOT_FOUND` vĩnh viễn và 3 đơn mới kết thúc ⇒ chu kỳ kế tiếp báo đủ 3 đơn mới.
- **Giới hạn của C2 (ghi lại hành vi, không phải lỗi):** đơn hợp lệ tồn từ trước, rồi 20 đơn mới hơn bị từ chối vĩnh viễn ⇒ đơn tồn chưa được gửi; log tổng cho thấy tuổi của đơn chưa báo cũ nhất tăng dần.
- **C1:** đơn `NO_SHOW` có `checked_in_at` (dựng thẳng trong DB như dữ liệu cũ của lỗi B6) ⇒ không gửi, không đánh dấu, có log Error.
- `SYSTEM_CLEANUP`: đơn loại này không bao giờ có `tadss_decision_id` (decision id chỉ ghi ở transaction 2) nên không là ứng viên; nhánh `Skip` chỉ kiểm ở unit test (Task 6, 9).

## Phần E — Kiểm hạn duyệt lúc nhận và tài liệu

### Task 14 — Kiểm hạn duyệt lúc nhận quyết định (B8, P6-24)

**Vấn đề (đã kiểm mã):** `DepositDecisionResolver` nhận `NEED_APPROVAL` chỉ kiểm có `approvalExpiresAt` (INV-7), không so với lúc nhận hay giờ chơi. Hạn duyệt = min(lúc đặt + 30', giờ chơi − 2 giờ) tính bằng đồng hồ TADSS. Khách đặt sát mốc giờ chơi − 2 giờ (VD chơi 18:00, đặt 15:59:59): TADSS thấy còn nửa giây nên trả `NEED_APPROVAL` với hạn 16:00:00; BE nhận lúc 16:00:00.3 (độ trễ mạng, lệch đồng hồ) vẫn đưa đơn vào hàng chờ. Quản lý bấm duyệt bị từ chối (câu duyệt cần hạn còn), job hết hạn duyệt hủy `APPROVAL_EXPIRED` trong vòng 1 phút ⇒ khách mất đơn, lỗi bị tính cho chi nhánh. Khách bấm chậm hơn 1 giây thì TADSS đã trả `APPROVAL_WINDOW_UNAVAILABLE` (cọc 100 %) và khách trả được ngay. Hạn duyệt sau giờ chơi chỉ xảy ra khi TADSS vi phạm công thức (`MinLeadMinutes ≥ 0`).

**Giải pháp:**
- `DepositDecisionResolver` nhận thêm `decidedAt` (mốc transaction 2 dùng để ghi đơn, lấy từ `TimeProvider` trước khi gọi resolver) và giờ bắt đầu chơi sớm nhất (UTC).
- `NEED_APPROVAL` có `approvalExpiresAt ≤ decidedAt` ⇒ dự phòng như mọi quyết định không dùng được: nguồn `FULL_PAYMENT_FALLBACK`, cọc 100 % (nhánh d, hoặc a′ nếu dưới mức tối thiểu), không mang `decisionId`, log Warning kèm `decisionId`, hạn duyệt, `decidedAt`.
- `approvalExpiresAt` sau giờ bắt đầu chơi sớm nhất ⇒ như trên, log Error.
- Hạn còn hiệu lực, dù ngắn ⇒ giữ nhánh (b). Không đặt ngưỡng tối thiểu phía BE (thuộc quy tắc approval window của TADSS, phiên bản sau).
- Ghi đơn ở transaction 2 dùng đúng `decidedAt` đó, nên quyết định "còn hạn" và mốc ghi đơn nhất quán; ca hạn trôi qua trong vài ms sau đó do job hết hạn duyệt dọn (`<= now`).

**Vì sao:** đúng điều TADSS tự làm khi hết thời gian duyệt (`APPROVAL_WINDOW_UNAVAILABLE`); căn cứ: INTEGRATION_SPEC §4.3 (bổ sung 27/10) và spec P6-24. Không lưu `decisionId` nên không báo outcome cho quyết định này, như mọi quyết định không dùng được.

**Ảnh hưởng:** khách đặt sát mốc không còn bị đưa vào hàng chờ vô ích; số đơn `APPROVAL_EXPIRED` không bị thổi phồng; không đổi API.

**Kiểm:** `Unit/Services/Deposit/DepositDecisionResolverTests` (hạn `decidedAt − 1 µs`, `= decidedAt`, `+ 1 µs`; hạn sau giờ chơi; hạn còn 1 phút vẫn chờ duyệt; dự phòng không mang `decisionId`; tổng tiền dưới mức tối thiểu ⇒ a′); BE-IT-04 trên host thật qua `POST /api/bookings/online` (WireMock trả `NEED_APPROVAL` với hạn đã qua: 201 `status = PENDING`, `depositSource = FULL_PAYMENT_FALLBACK`, Payment bằng 100 % tổng tiền, không có `tadss_decision_id`, không có đơn `PENDING_APPROVAL`, không có sự kiện hàng chờ). Đột biến bỏ bước kiểm ⇒ test đỏ.

### Task 15 — Tài liệu và kiểm tra cuối gói

**Đã đồng bộ ở mức thiết kế ngày 27/10** (trước khi code): spec (P6-24…P6-27, N-30…N-35, 4.4.1, 5.1, 7.6, 7.11, 8.2, Mục 11, 12, 13), INTEGRATION_SPEC §4.3, §4.4, `USE_CASES.md` (UC-01 luồng 6e, UC-09, UC-10), sơ đồ tuần tự 4 (đã render), methodology (BE-IT-04, 08, 09, 26), DEPOSIT_APPROVAL_API 2.1 và 3.3 (tải lại trang, link thanh toán). Sau khi code: đối chiếu lại các tài liệu trên với mã thật (sửa nếu lệch) và làm phần dưới.

**Cập nhật sau khi code:**
- Đối chiếu từng tài liệu đã đồng bộ ngày 27/10 với mã thật; lệch thì sửa tài liệu theo mã đã kiểm (hoặc sửa mã nếu mã sai thiết kế) và ghi lý do vào kết quả gói.
- Spec: dòng "Bật cờ TADSS trước khi xong 6A-4…" ở Mục 11 ghi đã đủ điều kiện kỹ thuật; cột "Gói" ở Mục 12 đánh dấu đã xong các dòng 6A-5.
- `SmashCourt-BE/CLAUDE.md`: hai job mới, Job-02/Job-04 có điều kiện, kiểm hạn duyệt lúc nhận. `CLAUDE.md` gốc, `TADSS-Runtime/CLAUDE.md`, README, MASTER_EXECUTION_PLAN: tiến độ 6A-5.
- Ghi kết quả vào cuối plan này.

**Kiểm cuối gói:** `check_docs`; build 0 cảnh báo; toàn suite 3 lần liên tiếp; đột biến: Job-02 (bỏ đọc lại dưới khóa), resolver (bỏ kiểm hạn duyệt lúc nhận, `<=` thành `<`), client (timeout outcome về 800 ms), mapper (đổi ánh xạ, bỏ `PENDING_PAYMENT`, bỏ kiểm `checked_in_at` từng nhánh, lệch offset), phân loại (404 không mã thành `Rejected`, 429 thành `Rejected`), thứ tự ứng viên (đổi sang cũ nhất trước), job outcome (bỏ dừng ở lỗi hệ thống, bỏ giới hạn 20), `ExpireUnpaidAsync` (bỏ kiểm lại dưới khóa, luật `SYSTEM_CLEANUP` chỉ theo Payment, `<=` thành `<`, bỏ Payment ⇒ `FAILED`), `MarkNoShowAsync` (bỏ điều kiện `checked_in_at`, bỏ điều kiện trạng thái), client (bỏ kiểm response outcome); script đột biến khôi phục đúng từng byte và build lại ở cuối.

---

## Kết quả thực hiện (27/10/2026) ✅

Toàn bộ suite **2232 xanh, 3 lần liên tiếp** sau vòng rà soát (cuối 6A-4 khoảng 1850), build 0 cảnh báo, chưa commit. Đột biến **28 lần, tất cả bị bắt** (danh sách cuối mục, cộng 2 ở vòng rà soát); script đột biến khôi phục đúng từng byte và build lại ở cuối. Không đổi schema, API, cấu hình; cờ `EnableTadssDecision` vẫn tắt.

| Task | Đã làm | Test |
| :---: | :--- | :--- |
| 1 | `IBookingCancellationService.ExpireUnpaidAsync` / `ExpireApprovalAsync`, hàm riêng `ExpireAsync` (không đổi `CancelAsync`): nạp ⇒ chặn đơn có Payment `SUCCESS` (log Error) ⇒ transaction: `LockAsync` ⇒ `IsPaymentWindowOverAsync` / `IsApprovalWindowOverAsync` ⇒ lõi hủy ⇒ nhả sân ⇒ Payment chờ `FAILED` ⇒ hóa đơn `EXPIRED`. Luật B1 ở `CancellationPolicy.PaymentExpiryReason` | `BookingCancellationServiceExpiryTests` (đủ bảng ca của Task 1), `ExpiryRepositoryTests` (biên µs `<`, `=`, `>`, gia hạn rồi kiểm lại), `CustomerHistoryCalculatorTests` (B1 tới `paymentExpiredCount`) |
| 2 | `NotifyExpiredAsync`, `Templates/ApprovalExpired.html`, `EmailService.SendApprovalExpiredAsync`, `SignalREvents.QueueReasonExpired` | `BookingNotificationServiceExpiryTests`, `DepositApprovalEmailTests` (mẫu mới, mã hóa HTML tên khách) |
| 3 | Job-01 viết lại: `GetExpiredUnpaidIdsAsync` (`<= now`, hạn sớm nhất trước, 100 đơn), từng đơn qua `ExpireUnpaidAsync`, log tổng; bỏ xóa slot lock toàn hệ thống | `Unit/Jobs/BookingJobExpiryTests`, `ExpiryRepositoryTests`; assert mới trong `ApprovalFlowTests`, `PaymentFailureFlowTests` (lý do, nguồn hủy, hóa đơn, Payment `FAILED`) |
| 4 | `Jobs/BookingApprovalJob` (`expire-approvals`, mỗi phút, giờ VN), `GetExpiredApprovalIdsAsync`; constructor không có TADSS client hay dịch vụ duyệt | `Unit/Jobs/BookingApprovalJobTests`, `HangfireRegistrationTests` (storage thật: cron, múi giờ, hàm) |
| 5 | **Bước 0** trước: `Integration/Http/BookingFinishAndNoShowJobTests` xanh với mã cũ (15 ca), vẫn xanh sau khi viết lại. Job-04: `MarkNoShowAsync` có điều kiện (trạng thái + `checked_in_at IS NULL`); Job-02: khóa ⇒ đọc lại trạng thái và hóa đơn dưới khóa ⇒ `EndPlayAsync` có điều kiện; "sân còn bận" sau khi thắng, chỉ loại chính đơn; trả sân `ReleaseToAvailableAsync` (điều kiện trong câu lệnh); người chờ slot qua dịch vụ chung (link trang chủ); xóa `NotifySlotInterestedUsersAsync` | `Unit/Jobs/BookingJobNoShowTests`, `BookingJobFinishTests`, `Integration/BookingPlayEndRepositoryTests` |
| 6 | `TadssOutcomeMapper` (+ `TadssOutcomePlan`, `TadssOutcomePlanKind`), `TadssOutcomeDelivery.Classify` (+ `TadssOutcomeDeliveryKind`) | `TadssOutcomeMapperTests` (BE-UT-09), `TadssOutcomeDeliveryTests` |
| 7 | `TadssClient.ReportOutcomeAsync` kiểm `decisionId`, `outcome`, `occurredAt` (cắt µs); timeout riêng 5 giây (`OutcomeTimeout`) | `TadssClientOutcomeTests` (đồng hồ giả: 800 ms và 4,999 s chưa hết, 5 s hết; đánh giá vẫn `TimeoutMs`) |
| 8 | `GetOutcomeCandidatesAsync` (mới nhất trước, kèm sân), `MarkOutcomeReportedAsync` (có điều kiện, không đổi `updated_at`) | `Integration/OutcomeCandidateRepositoryTests` |
| 9 | `Jobs/TadssOutcomeJob` (`report-tadss-outcomes`, `*/5`): 20 đơn, tuần tự, dừng chu kỳ ở lỗi hệ thống, log tổng một dòng (gồm tuổi đơn chưa báo cũ nhất) | `Unit/Jobs/TadssOutcomeJobTests` (đủ bảng ca của Task 9), `HangfireRegistrationTests` |
| 10 | `TadssStub`: `ReplyOutcome`, `ReplyOutcomeForAny`, `ReplyOutcomeProblem`, `ReplyOutcomeRaw`, `OutcomesFor`, `AllOutcomes`, `OutcomeResponseBody` | dùng ở Task 13 |
| 11 | BE-IT-08 và Job-01 trên host thật | `Integration/Http/ExpiryJobFlowTests` (9 ca) + 1 ca Job-02 ⇒ check-out trong bước 0 |
| 12 | BE-IT-26, điểm chặn bằng transaction mở tay | `Integration/Http/ExpiryConcurrencyTests` (10 ca) |
| 13 | BE-IT-09 trên host thật | `Integration/Http/OutcomeJobFlowTests` (25 ca) |
| 14 | `DepositDecisionResolver.Resolve(…, decidedAtUtc, playStartUtc)`: `NEED_APPROVAL` hạn ≤ lúc nhận ⇒ dự phòng (Warning), hạn sau giờ chơi ⇒ dự phòng (Error), không mang `decisionId`. `DecideAsync(bookingId, finalTotal, playStartUtc)` lấy mốc nhận từ `TimeProvider` sau khi TADSS trả lời; `DepositDecision.DecidedAtUtc`; transaction 2 ghi đơn bằng đúng mốc đó | `DepositDecisionResolverTests` (biên µs, sau giờ chơi, còn 1 phút, a′, mốc có ở mọi nhánh), `BookingServiceOnlineBookingTests` (transaction 2 dùng mốc nhận dù đồng hồ đã trôi), BE-IT-04 trên host (`OnlineBookingTadssFailureTests`: hạn đã qua, hạn sau giờ chơi) |
| 15 | Spec (đầu file, Mục 11, Mục 12), chương kiến trúc BE (bảng job), DEPOSIT_APPROVAL_API (trạng thái), CLAUDE.md, README, MASTER_EXECUTION_PLAN | `check_docs` đạt |

**Khác plan (đều là chi tiết nội bộ, không đổi hành vi ngoài):**
- Job-02 và Job-04 trước đây truy vấn DbContext ngay trong job nên không unit test được. Chuyển phần đọc/ghi xuống repository (`GetNoShowCandidatesAsync`, `GetPlayEndCandidatesAsync`, `GetCourtsHeldByOtherBookingsAsync`, `MarkNoShowAsync`, `EndPlayAsync`, `ICourtRepository.ReleaseToAvailableAsync`, `IInvoiceRepository.GetPaymentStatusByBookingIdAsync`); job chỉ còn lọc giờ (luật cũ) và điều phối. Ứng viên lọc thêm "có sân chơi không sau hôm nay" ở DB (mã cũ nạp mọi đơn tương lai rồi lọc trong bộ nhớ; kết quả như nhau).
- `ISlotInterestNotificationService` vào `BookingJob` ở Task 5 (Job-01 mới không cần), không ở Task 3.
- Tên file test: bước 0 ở `Integration/Http/` (cần host thật); `NoShowRepositoryTests` của plan thành `BookingPlayEndRepositoryTests` (gồm cả Job-02).
- Task 12, "Job-01 thắng trước": "Thanh toán lại" nhận **400** ở bước kiểm trạng thái có sẵn (đơn không còn `PENDING`); 409 chỉ khi hủy chen giữa lúc đọc và lúc gia hạn (spec 7.10). "Job-04 thắng trước": check-in qua API nhận 400 (đã quá khung check-in); ca 409 (nhân viên nạp đơn trước lúc job commit) kiểm ở mức repository (`CheckInAsync` trả `false`, dịch vụ đổi thành 409). Hai mã này là hành vi có sẵn, gói không đổi.
- Log Information "job thua, bỏ qua" và log tổng của job outcome không kiểm được trên host thật (bộ ghi log của host test chỉ giữ từ Warning). Trên host, "job không ghi gì" chứng minh bằng `updated_at` của booking / hóa đơn giữ đúng mốc của bên thắng; log tổng kiểm ở unit test.
- `TadssStub` trả outcome bằng callback C# thay vì templating: templating JSON của WireMock đọc chuỗi ngày giờ thành kiểu ngày và in lại khác định dạng, client (kiểm khớp µs) sẽ coi là lệch.

**Phát hiện trong lúc làm:**
- **Test có sẵn sẽ tự đỏ sau 18/11/2026:** `HttpTadssIntegrationTests.DecideAsync_NeedApproval_ReturnsDeadlineInUtc` (và unit test tương tự) dùng hạn duyệt cố định 18/11/2026; với B8, sau ngày đó hạn "đã qua lúc nhận" và đi dự phòng. Đổi sang hạn tương đối (BE-IT) và đồng hồ giả (unit).
- Test mẫu `outcome-response.json` mang `decisionId` khác quyết định của test, nên test outcome cũ "thành công" với phản hồi lệch — đúng kiểu lỗi B4. Đổi test cho phản hồi khớp request.
- Ràng buộc schema thật `chk_invoices_expires_at` (hạn sau `created_at`) và định dạng ngày giờ của API (`dd/MM/yyyy HH:mm:ss`, giờ VN) làm vài test mới đỏ lần đầu — lỗi dữ liệu test, đã sửa test, không đổi mã.
- Hook tự build của workspace chạy chồng lên script đột biến một lần (DLL bị `testhost` khóa); build lại sau khi script xong thì đạt.

**Đột biến (26/26 bị bắt):** Job-01: bỏ kiểm lại dưới khóa (unit, BE-IT-26), khóa sau kiểm hạn, luật `SYSTEM_CLEANUP` (hai dạng), `<=` thành `<` (kiểm hạn, chọn ứng viên), bỏ Payment ⇒ `FAILED`; Job-04: ân hạn 0 phút (bước 0), bỏ cả hai điều kiện, bỏ riêng `checked_in_at`, bỏ riêng trạng thái; Job-02: hóa đơn đọc trước khóa, bỏ đọc lại trạng thái; resolver: bỏ kiểm hạn lúc nhận, `<=` thành `<`; client: timeout outcome 800 ms, bỏ kiểm response; mapper: biên giờ kết thúc + nhãn `NO_SHOW`, bỏ `PENDING_PAYMENT`, bỏ kiểm `checked_in_at` của `COMPLETED`, lệch offset, đổi ánh xạ; phân loại: 404 không mã thành lỗi riêng đơn, 429 thành lỗi riêng đơn; thứ tự ứng viên cũ nhất trước; job outcome: bỏ dừng chu kỳ + đổi giới hạn 20.

**Rà soát sau khi code (27/10), đối chiếu mã, test với spec 7.6, 7.11, 4.4.1 và bảng ca của plan:**
- *Sửa cấu trúc:* các truy vấn và câu ghi của job tách sang file partial `Repositories/BookingRepository.Jobs.cs` (`BookingRepository.cs` 1534 ⇒ 1352 dòng), xếp theo hết hạn ⇒ kết thúc giờ chơi / NO_SHOW ⇒ báo kết quả; trong `IBookingRepository` các hàm này gom về một mục ở cuối thay vì chen giữa mục "Thanh toán và hoàn tiền". Vòng hủy theo lô của Job-01 và job hết hạn duyệt (trước đây chép hai lần) dùng chung `Jobs/ExpiryBatch`. Test repository của Job-01 và job hết hạn duyệt tách khỏi `BookingStateGateRepositoryTests` (của 6A-4) sang `Integration/ExpiryRepositoryTests`.
- *Sửa nhỏ:* XML doc của `CourtRepository.BatchUpdateStatusAsync` bị gắn nhầm vào hàm mới chèn giữa doc và hàm; `TadssOutcomeJob` gặp đơn không có `decisionId` nay log Error (trước chỉ đếm). Đổi các file đã sửa về CRLF như working copy của repo (index vẫn LF).
- *Test bổ sung (22):* luật `PaymentExpiryReason` dạng hàm thuần (6 ca); đơn chờ duyệt không có hóa đơn; mọi cột của đơn hết hạn ghi cùng một mốc dù đồng hồ trôi; Job-04 thua một đơn vẫn đánh đơn sau, đơn không có sân bị bỏ; Job-02 đơn không có sân, đơn nhiều sân chờ giờ kết thúc muộn nhất; mọi tác vụ của `BookingJob` có `[DisableConcurrentExecution]` (5 ca); job outcome gặp đơn không có quyết định; trên host thật: slot lock hết hạn còn sót không chặn đơn mới và Job-01 không còn xóa thay Job-03 (C5), sân vẫn `IN_USE` khi đơn khác cùng sân đang chơi (B6), job hết hạn duyệt thua lệnh hủy của nhân viên có điểm chặn (ma trận 0.4), 409 `OUTCOME_ALREADY_RECORDED` được đánh dấu kèm Warning. Đột biến cho `ExpiryBatch` (bỏ bắt lỗi từng đơn, đếm sai "bỏ qua") đều bị bắt.
- *Đã xét, không thêm:* hai instance BE chạy cùng job (khóa phân tán của Hangfire, đánh dấu outcome có điều kiện đã kiểm ở repository); log tổng và log "job thua" mức Information trên host thật (bộ ghi log của host test chỉ giữ từ Warning; đã kiểm ở unit test).

**Chạy thật với VNPay sandbox (28/10, chi tiết spec 10.1 bước 2):** BE chạy Docker từ mã gói này trên `tadss-dev`; Job-01 hủy đúng 5 đơn quá hạn (log "1 ứng viên: đã hủy 1, bỏ qua 0, lỗi 0"); khoản tiền về sau khi Job-01 hủy được ghi chờ hoàn 100 %; IPN thất bại rút hạn và job hủy khi hết ân hạn với `PAYMENT_EXPIRED`. Không thấy lỗi của gói này. Phát hiện cho gói sau: khe hở P6-27 có thật, N-36 (`vnpay/confirm` báo thành công theo mã VNPay).

**Còn lại:** gói 6A-6 (hạn link VNPay P6-27 kèm T-5, số liệu sandbox đã có; N-36; bộ test theo danh mục, k6). Bật cờ `EnableTadssDecision` là quyết định của người dùng. FE (6B): trang `/bookings/new` đọc tham số slot (spec 8.2), xử lý `PendingApprovalQueueChanged` `REMOVED`/`EXPIRED`.

## Ràng buộc chung

- Không SQL thô; câu cập nhật có điều kiện bằng `ExecuteUpdateAsync`. Không đổi schema.
- Mọi "bây giờ" của mã mới hoặc viết lại (Job-01, Job-04, hai job mới, dịch vụ) lấy từ `TimeProvider`; giờ chơi đổi bằng `DateTimeHelper.ToUtcFromVietnam`; `occurredAt` luôn có offset.
- Email, SignalR sau commit; job không ném exception ra Hangfire.
- Không đổi route, request, response của API; không mã lỗi mới; không khóa cấu hình mới.
- Trạng thái booking của BE là nguồn sự thật: không tự duyệt, không hồi sinh đơn vì phản hồi của TADSS; nhãn gửi TADSS không được mâu thuẫn với `checked_in_at`.
- Không bật `EnableTadssDecision`; không chạm `tadss-dev`.
- Comment theo skill `smashcourt-be-architecture`; test: luật thuần trong `Unit/`, DB thật trong `Integration/`, HTTP trong `Integration/Http/`; mỗi test nêu được đột biến làm nó đỏ. Chỉ sửa lỗi thật, kèm test; quyết định mới ghi vào spec.

## Rủi ro

| Rủi ro | Xử lý |
| :--- | :--- |
| Viết lại Job-01 / Job-04 làm vỡ test cũ | Chạy lại toàn bộ test có `RunPaymentExpiryJobAsync` và test NO_SHOW; chỉ sửa test khi hành vi đổi đúng spec, ghi lý do |
| Tách `ExpireAsync` làm lệch các đường hủy cũ | Không đổi `CancelAsync`; toàn bộ test hủy 6A-4 giữ xanh |
| Outcome tồn bị chặn bởi ≥ 20 đơn bị từ chối mới hơn (C2) | Hiếm (cần hai điều kiện cùng lúc); log tổng có tuổi đơn chưa báo cũ nhất; ghi spec Mục 11 kèm hướng chữa |
| Khách trả tiền sau hạn đơn vẫn mất sân (E1, E2) | Không đổi trong gói; P6-07 bảo đảm hoàn 100 % |
| Job hết hạn duyệt trùng lúc Quản lý duyệt | Loại trừ nhau theo điều kiện thời gian; BE là nguồn sự thật; Task 12 |
| Viết lại Job-02 làm lệch luồng kết thúc đơn | Giữ nguyên luật chọn `COMPLETED`/`PENDING_PAYMENT`, lịch chạy; test đường chạy bình thường và test NO_SHOW/check-out cũ giữ xanh |
| Kiểm hạn duyệt lúc nhận làm đơn hợp lệ đi cọc 100 % | Chỉ khi hạn ≤ `decidedAt` hoặc sau giờ chơi; hạn còn (dù ngắn) vẫn chờ duyệt; test biên µs |
| Kiểm hạn duyệt lúc nhận làm đổi hành vi test HTTP cũ | Đã rà 27/10: test hiện có dựng đơn quá hạn duyệt bằng cách sửa DB (`ApprovalExpiresAt` lùi về quá khứ, `MoveApprovalExpiryAsync`), không bằng TADSS giả trả hạn đã qua; TADSS giả trả `now + 20 phút` cho đơn chơi ở ngày tương lai. Chạy lại mọi test có `RequireApprovalForAnyBooking`; hạn giả lập phải trước giờ chơi của đơn |

## Thứ tự và ước lượng

| Phần | Task | Ước lượng |
| :--- | :--- | :---: |
| A | 1–2 | 1 ngày |
| B | 3–5 | 1,5 ngày |
| C | 6–9 | 1 ngày |
| D | 10–13 | 2 ngày |
| E | 14–15 | 1 ngày |

Tổng khoảng 6,5 ngày (ước lượng kỹ thuật, không cắt test DB thật để giữ mốc). Phụ thuộc: Task 2 xong trước khi Task 1 hoàn tất (thông báo); Task 1 trước 3, 4; Task 2 trước 5 (dịch vụ báo slot chung); Task 6, 7, 8 trước 9; Task 10 trước 11–13; Task 14 độc lập (làm bất kỳ lúc nào trước Task 15).
