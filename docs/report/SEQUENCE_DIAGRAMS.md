# SƠ ĐỒ TUẦN TỰ: LUỒNG ĐẶT CỌC QUA TADSS

| | |
| :--- | :--- |
| **Mục đích** | Sơ đồ tuần tự để đưa vào báo cáo Đồ án 2: thông điệp giữa FE, BE, TADSS, VNPay và database trong các luồng có quyết định cọc |
| **Trạng thái** | Mục 1, 2, 4, 5, 6: **thiết kế Phase 6** (BE/FE chưa hiện thực). Mục 3: **hiện trạng** (bên trong TADSS, đã có trong mã) |
| **Nguồn sự thật** | [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md) (BE ↔ TADSS), [`DEPOSIT_APPROVAL_API.md`](../../../SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md) (BE ↔ FE). Khi khác nhau, các tài liệu đó đúng |
| **Use case tương ứng** | [`USE_CASES.md`](USE_CASES.md) |
| **Bộ sơ đồ** | [`ARCHITECTURE_DIAGRAMS.md`](ARCHITECTURE_DIAGRAMS.md) · [`USE_CASES.md`](USE_CASES.md) · tài liệu này · [`ACTIVITY_AND_STATE_DIAGRAMS.md`](ACTIVITY_AND_STATE_DIAGRAMS.md) |
| **Cập nhật** | 18/10/2026 (đồng bộ thiết kế Phase 6: quyết định P6, DEPOSIT_APPROVAL_API Mục 11; thêm Mục 5, 6) |

---

## 1. Đặt sân online có quyết định cọc (UC-DEPOSIT-01)

*Thiết kế Phase 6.* Điểm thiết kế quan trọng: BE **không** gọi TADSS bên trong transaction ghi booking. Booking và slot lock được ghi trước (transaction 1), gọi TADSS sau đó, rồi áp kết quả bằng cập nhật có điều kiện trạng thái (transaction 2). Nếu BE dừng giữa hai bước, booking `PENDING` chưa có thanh toán sẽ bị job hủy `PENDING` quá hạn dọn sau 10 phút, với lý do `SYSTEM_CLEANUP` (không tính vào lịch sử của khách, không báo kết quả cho TADSS). Đặt sân online bắt buộc đăng nhập.

```mermaid
sequenceDiagram
    autonumber
    actor C as Khách hàng
    participant FE as smashcourt-fe
    participant BE as SmashCourt-BE
    participant DB as DB SmashCourt
    participant T as TADSS
    participant V as VNPay
    participant H as SignalR hub

    C->>FE: Xác nhận đặt sân (đã đăng nhập)
    FE->>BE: POST /api/bookings/online (JWT CUSTOMER)
    BE->>DB: Kiểm tra slot trống, slot lock
    BE->>DB: Transaction 1: booking PENDING, booking_courts,<br/>hóa đơn (hạn 10 phút, hạn giữ chỗ tối đa 15 phút),<br/>một slot lock mỗi sân (chưa tạo thanh toán)

    alt Cờ EnableTadssDecision bật
        BE->>T: POST /api/v2/decisions<br/>X-Api-Key, X-Idempotency-Key = bookingId
        Note over BE,T: Timeout phía BE 800 ms. Lỗi tạm thời được thử lại<br/>tối đa 1 lần với cùng idempotency key
    end

    alt APPROVED (cọc theo hạng, hoặc 100 % do fallback bên trong TADSS)
        T-->>BE: 200 status APPROVED, deposit.rate, deposit.amount, decisionId
        BE->>DB: Transaction 2: lưu mức cọc, nguồn TADSS, decisionId,<br/>thanh toán VNPay = deposit.amount
        BE->>BE: Ký URL thanh toán VNPay (số tiền cọc)
        BE-->>FE: 201 status PENDING, paymentUrl, depositAmount, remainingAmount
        BE-)H: BookingCreated
        FE->>C: Trang kết quả: cọc, phần còn lại, hạn thanh toán
        C->>FE: Bấm Thanh toán cọc
        FE->>V: Chuyển khách tới VNPay (UC-DEPOSIT-02)
    else APPROVED nhưng tiền cọc dưới 5.000 đ (gồm 0)
        T-->>BE: 200 status APPROVED, deposit.amount nhỏ
        BE->>DB: Transaction 2: PENDING → CONFIRMED, cọc áp dụng 0,<br/>xóa slot lock (sân giữ qua booking_courts)
        BE-->>FE: 201 status CONFIRMED, paymentUrl = null
        BE-)H: BookingCreated
        FE->>C: Đặt sân thành công, thanh toán tại quầy
    else NEED_APPROVAL (đơn ≥ 2.000.000 VND, hoặc ≥ 2 lần không đến)
        T-->>BE: 200 status NEED_APPROVAL, approvalExpiresAt, reasonCode, decisionId
        BE->>DB: Transaction 2: PENDING → PENDING_APPROVAL,<br/>lưu hạn duyệt và lý do, gia hạn slot lock tới hạn duyệt
        BE-->>FE: 201 status PENDING_APPROVAL, approvalExpiresAt, paymentUrl = null
        BE-)H: BookingCreated, BookingPendingApproval,<br/>PendingApprovalQueueChanged (tới chi nhánh, Chủ hệ thống)
        FE->>C: Trang "Chờ duyệt cọc" (đếm ngược)
    else REJECTED (Q1, khách lặp lại việc giữ chỗ không thanh toán)
        T-->>BE: 200 status REJECTED
        BE->>DB: Transaction 2: PENDING → CANCELLED (lý do POLICY_REJECTED),<br/>xóa slot lock
        BE-->>FE: 400 BOOKING_REJECTED
        FE->>C: Thông báo không nhận đơn
    else Cờ tắt, hoặc TADSS timeout / lỗi mạng / 5xx / 401 / 413 / 429 / 400, 409 bất thường
        Note over BE: Ghi log kèm correlation id. Khách không thấy lỗi
        BE->>DB: Transaction 2: cọc 100 %, nguồn FULL_PAYMENT_FALLBACK,<br/>thanh toán VNPay = finalTotal
        BE->>BE: Ký URL thanh toán VNPay (100 %)
        BE-->>FE: 201 status PENDING, paymentUrl, depositRate = 1
        BE-)H: BookingCreated
        FE->>C: Trang kết quả (như nhánh APPROVED)
        C->>FE: Bấm Thanh toán cọc
        FE->>V: Chuyển khách tới VNPay (UC-DEPOSIT-02)
    end
```

## 2. Quản lý duyệt hoặc từ chối mức cọc (UC-DEPOSIT-05, 06, 07)

*Thiết kế Phase 6.* Khác với lúc đặt sân, khi duyệt **không có** đường lùi cọc 100 %: TADSS không phản hồi thì Quản lý được báo thử lại và đơn vẫn chờ.

```mermaid
sequenceDiagram
    autonumber
    actor M as Quản lý chi nhánh
    participant FE as smashcourt-fe
    participant BE as SmashCourt-BE
    participant DB as DB SmashCourt
    participant T as TADSS
    participant H as SignalR hub
    actor C as Khách hàng

    M->>FE: Mở "Hàng chờ duyệt cọc"
    FE->>BE: GET /api/bookings/pending-approval
    BE->>DB: Booking PENDING_APPROVAL còn hạn,<br/>trong chi nhánh của Quản lý (theo JWT)
    BE-->>FE: Danh sách, đơn sắp hết hạn lên đầu
    M->>FE: Duyệt, cọc 30 %, lý do
    FE->>BE: POST /api/bookings/{bookingId}/approve<br/>{ depositRate: 0.3, reason }

    BE->>BE: Kiểm dữ liệu nhập, quyền chi nhánh,<br/>số tiền cọc quy đổi = 0 hoặc ≥ 5.000 đ (sai ⇒ 400)
    BE->>DB: Đọc booking: còn PENDING_APPROVAL? còn hạn? có decisionId?
    alt Không còn chờ duyệt, đã quá hạn, hoặc thiếu decisionId
        BE-->>FE: 409 APPROVAL_ALREADY_RESOLVED / APPROVAL_EXPIRED /<br/>BOOKING_NOT_PENDING_APPROVAL / APPROVAL_DECISION_NOT_FOUND
    else Hợp lệ
        BE->>T: POST /api/v2/decisions/{decisionId}/override<br/>{ action: APPROVE, deposit.rate: 0.3, reason }<br/>X-Actor-Id = userId từ JWT, X-Idempotency-Key = {decisionId}:APPROVE
        alt 200 OK
            T-->>BE: deposit { rate 0.3, amount }, actorId, overriddenAt
            BE->>DB: Cập nhật có điều kiện: PENDING_APPROVAL → PENDING<br/>(cọc 0 % ⇒ CONFIRMED, xóa slot lock),<br/>mức cọc do TADSS trả, nguồn MANAGER, người duyệt,<br/>hạn thanh toán 15 phút (Q2), gia hạn slot lock<br/>(không tạo giao dịch VNPay)
            alt Cập nhật thành công
                BE-->>FE: 200 approvalResult APPROVED, depositAmount, expiresAt
                BE-)H: BookingApprovalResolved, PendingApprovalQueueChanged
                H-)C: Đơn đã được duyệt (khách bấm Thanh toán cọc để lấy link VNPay)
            else Booking đã đổi trạng thái trong lúc gọi (job hết hạn, khách hủy, request khác)
                BE-->>FE: 409, không hồi sinh booking
            end
        else 409 ALREADY_OVERRIDDEN / IDEMPOTENCY_KEY_REUSE
            T-->>BE: 409
            BE->>T: GET /api/v2/decisions/{decisionId} (Q9)
            BE->>DB: Đơn còn chờ và chưa hết hạn ⇒ áp ghi đè đã lưu
            BE-->>FE: 409 APPROVAL_ALREADY_RESOLVED (tải lại hàng chờ)
        else 409 APPROVAL_EXPIRED / NOT_PENDING_APPROVAL
            T-->>BE: 409
            BE-->>FE: 409 APPROVAL_EXPIRED / BOOKING_NOT_PENDING_APPROVAL
        else Timeout, lỗi mạng, 5xx, 429, 401
            BE-->>FE: 503 TADSS_UNAVAILABLE (booking vẫn PENDING_APPROVAL)
            FE->>M: Giữ hộp thoại, nút "Thử lại" gửi lại đúng nội dung cũ
        end
    end
```

Từ chối (`POST /api/bookings/{bookingId}/reject`) đi cùng đường trên với `action: REJECT`, không gửi `deposit`, khóa `{decisionId}:REJECT`; nhánh 200 chuyển `PENDING_APPROVAL → CANCELLED` (lý do `APPROVAL_REJECTED`), xóa slot lock, báo người đăng ký chờ slot và phát thêm `BookingCancelled`.

## 3. Bên trong TADSS khi xử lý một yêu cầu quyết định (UC-TADSS-01)

*Hiện trạng.* Thứ tự đúng theo `DecisionService` và `StrategyEvaluator` trong mã.

```mermaid
sequenceDiagram
    autonumber
    participant BE as SmashCourt-BE
    participant MW as Middleware
    participant DS as DecisionService
    participant ST as Audit store (PostgreSQL)
    participant R as StrategyRouter
    participant EV as StrategyEvaluator
    participant CA as Cache (Redis, dự phòng bộ nhớ)
    participant P as Pipeline Polly
    participant S as Strategy

    BE->>MW: POST /api/v2/decisions
    MW->>MW: Correlation id, giới hạn kích thước (413),<br/>xác thực X-Api-Key (401), rate limit (429)
    MW->>DS: EvaluateAsync(request, idempotencyKey)
    DS->>DS: Dựng domain Reservation (sai ⇒ 400), băm request

    DS->>ST: Tìm bản PRIMARY theo idempotency key
    alt Đã có
        ST-->>DS: Quyết định đã lưu
        DS-->>BE: 200 trả lại kết quả cũ (khác nội dung ⇒ 409 IDEMPOTENCY_KEY_REUSE)
    else Chưa có
        DS->>DS: Kiểm thời lượng từng sân (sai ⇒ 400 DOMAIN_INVARIANT_VIOLATION)
        DS->>R: Chọn strategy theo Tadss:Routing
        R-->>DS: Strategy + tên luật
        DS->>EV: Đánh giá
        EV->>CA: Đọc cache theo (strategy, phiên bản, cấu hình, hash request)
        alt Có trong cache
            CA-->>EV: StrategyDecision (CACHE_HIT)
        else Không có
            EV->>P: ExecuteAsync
            P->>S: EvaluateAsync (luồng riêng, timeout 120 ms mỗi lượt, tối đa 2 lần thử lại)
            alt Strategy trả kết quả kịp
                S-->>P: StrategyDecision (AUTOMATED)
                P-->>EV: Kết quả
                EV->>CA: Ghi cache (chỉ kết quả AUTOMATED)
            else Lỗi, quá hạn mọi lượt, hoặc circuit breaker mở
                P-->>EV: Fallback: APPROVED cọc 100 % (FALLBACK)
            else Quá Outer timeout 500 ms
                P-->>DS: Lỗi ⇒ 504 GATEWAY_TIMEOUT
            end
        end
        EV-->>DS: Kết quả strategy
        DS->>DS: Ghép response: quy đổi tiền cọc,<br/>hạn duyệt = min(tạo + 30', giờ chơi − 120')<br/>(hết thời gian duyệt ⇒ APPROVED 100 %)
        DS->>ST: Ghi audit PRIMARY (đồng bộ)
        alt Ghi được
            ST-->>DS: OK
            DS-)DS: Đếm metric, gửi shadow (nếu bật, không chờ)
            DS-->>BE: 200 kết quả quyết định
        else Request song song cùng key đã ghi trước
            ST-->>DS: Bản của request thắng
            DS-->>BE: 200 kết quả của request thắng
        else PostgreSQL lỗi
            DS-->>BE: 503 PERSISTENCE_UNAVAILABLE (không trả quyết định khi chưa ghi được audit)
        end
    end
```

## 4. Hết hạn duyệt và báo kết quả về TADSS (UC-DEPOSIT-09, 10)

*Thiết kế Phase 6.*

```mermaid
sequenceDiagram
    autonumber
    participant J1 as Job hủy đơn hết hạn duyệt<br/>(mỗi phút)
    participant J2 as Job báo kết quả
    participant DB as DB SmashCourt
    participant H as SignalR hub
    participant T as TADSS

    J1->>DB: Booking PENDING_APPROVAL có approvalExpiresAt ≤ now
    loop Từng booking
        J1->>DB: Cập nhật có điều kiện: → CANCELLED (nguồn SYSTEM,<br/>lý do APPROVAL_EXPIRED), xóa slot lock
        J1-)H: BookingExpired, PendingApprovalQueueChanged (REMOVED, EXPIRED)
        Note over J1: Gửi email cho khách, báo người đăng ký chờ slot.<br/>Không bao giờ tự duyệt
    end

    J2->>DB: Booking đã kết thúc, có decisionId, chưa báo kết quả<br/>(giới hạn số booking mỗi lần chạy)
    loop Từng booking, tuần tự (rate limit của TADSS dùng chung với lúc đặt sân)
        J2->>T: POST /api/v2/decisions/{decisionId}/outcome<br/>{ outcome, occurredAt }
        alt 200 (kể cả gửi trùng cùng nội dung)
            T-->>J2: Đã ghi (hoặc trả bản đã lưu)
            J2->>DB: Đánh dấu đã báo
        else 409 OUTCOME_ALREADY_RECORDED
            T-->>J2: Đã có kết quả khác
            J2->>DB: Đánh dấu, ghi log để đối soát
        else 429 RATE_LIMITED
            Note over J2: Dừng chu kỳ này, phần còn lại để lần sau
        else Lỗi tạm thời
            Note over J2: Để lần chạy sau thử lại
        end
    end
```

Ánh xạ trạng thái booking sang `outcome`: [`USE_CASES.md`](USE_CASES.md) Mục 5.10.

## 5. Thanh toán cọc, thanh toán thất bại và thanh toán lại (UC-DEPOSIT-02)

*Thiết kế Phase 6.* Thanh toán thất bại **không** hủy đơn: khách có tối đa 5 phút để thanh toán lại hoặc tự hủy. Mọi lần thanh toán lại không kéo dài quá hạn giữ chỗ tuyệt đối (15 phút từ lúc đặt, hoặc từ lúc duyệt với đơn cần duyệt). Sau khi duyệt, link VNPay chỉ sinh khi khách bấm "Thanh toán cọc".

```mermaid
sequenceDiagram
    autonumber
    actor C as Khách hàng
    participant FE as smashcourt-fe
    participant BE as SmashCourt-BE
    participant DB as DB SmashCourt
    participant V as VNPay
    participant H as SignalR hub

    C->>FE: Bấm Thanh toán cọc (sau khi đặt hoặc sau khi được duyệt)
    FE->>BE: POST /api/me/bookings/{id}/retry-payment
    alt Đã tới hạn giữ chỗ tuyệt đối
        BE-->>FE: 400 PAYMENT_HOLD_EXPIRED
        FE->>C: Hết thời gian giữ chỗ, mời đặt lại
    else Còn hạn
        BE->>DB: Payment cũ đang chờ ⇒ FAILED, tạo Payment mới = depositAmount,<br/>hạn = min(hạn tuyệt đối, max(hạn hiện tại, now + 10'))
        BE-->>FE: paymentUrl
        FE->>V: Chuyển khách tới VNPay
        V->>BE: GET /api/payments/vnpay/ipn
        alt Thành công, đơn còn PENDING
            BE->>DB: PENDING → PAID_ONLINE (có điều kiện), Payment SUCCESS,<br/>hóa đơn PARTIALLY_PAID, xóa slot lock
            BE-)H: PaymentSuccess
        else Thất bại (thẻ lỗi, khách hủy giao dịch)
            BE->>DB: Payment đang chờ ⇒ FAILED, đơn giữ PENDING,<br/>hạn rút về min(hạn, now + 5')
            BE-)H: PaymentFailed (kèm time grid)
        else Thành công nhưng đơn đã bị hủy (phiên VNPay còn sống sau khi hết hạn)
            BE->>DB: Payment SUCCESS, Refund 100 % PENDING,<br/>CANCELLED → CANCELLED_PENDING_REFUND
            BE-)H: BookingCancelled (nhân viên thấy đơn chờ hoàn)
        end
        V->>FE: Chuyển về /payment/result
        FE->>BE: POST /api/payments/vnpay/confirm (chỉ đọc)
        alt Thất bại
            FE->>C: Hai nút: Thanh toán lại (về bước 1) / Hủy đơn (Mục 6)
        else Thành công
            FE->>C: Đã cọc, phần còn lại thu tại quầy
        end
    end
    Note over BE,DB: Không thanh toán trước hạn ⇒ job hủy đơn, lý do PAYMENT_EXPIRED
```

## 6. Hủy đơn và hoàn tiền (UC-DEPOSIT-04, 12)

*Thiết kế Phase 6.* Mọi đường hủy đi qua một dịch vụ hủy chung trong BE: cập nhật có điều kiện ghi đủ trạng thái, thời điểm, nguồn hủy và **lý do hủy** trong một câu lệnh; tiền hoàn tính trên **số đã trả**, % hoàn theo chính sách hủy tính đúng múi giờ. Khách chỉ tự hủy được **trước giờ bắt đầu** (P6-23); từ giờ bắt đầu chỉ nhân viên hủy được, đơn khách không đến do job đánh `NO_SHOW`.

```mermaid
sequenceDiagram
    autonumber
    actor C as Khách hàng hoặc nhân viên
    participant FE as smashcourt-fe
    participant BE as SmashCourt-BE
    participant DB as DB SmashCourt
    participant H as SignalR hub

    C->>FE: Bấm Hủy đơn
    FE->>BE: POST /api/me/bookings/{id}/cancel (khách)<br/>hoặc POST /api/bookings/{id}/cancel (nhân viên)
    opt Khách hủy khi đã tới giờ bắt đầu (P6-23)
        BE-->>FE: 400 BOOKING_ALREADY_STARTED, đơn không đổi
        Note over BE: Khách không đến thì job đánh NO_SHOW
    end
    BE->>DB: Cập nhật có điều kiện: → CANCELLED, cancelled_at,<br/>nguồn hủy, lý do CUSTOMER_CANCELLED / STAFF_CANCELLED
    alt Người khác vừa đổi trạng thái (duyệt, từ chối, job)
        BE-->>FE: 409, FE tải lại
    else Thành công
        BE->>DB: Tắt booking_courts, xóa slot lock, Payment đang chờ ⇒ FAILED
        alt Đơn đã có thanh toán thành công
            BE->>BE: tiền hoàn = round(tổng đã trả × % hoàn)
            BE->>DB: Tiền hoàn > 0 ⇒ Refund PENDING, CANCELLED_PENDING_REFUND,<br/>trả lại lượt khuyến mãi
        else Chưa thanh toán (PENDING, PENDING_APPROVAL, CONFIRMED cọc 0 %)
            Note over BE,DB: Không hoàn tiền, không giảm lượt khuyến mãi,<br/>không tính là hết hạn thanh toán
        end
        BE-->>FE: 200
        BE-)H: BookingCancelled (time grid), đơn chờ duyệt thêm<br/>PendingApprovalQueueChanged (REMOVED, CANCELLED)
        Note over BE: Email cho khách, báo người đăng ký chờ slot
    end
```

Các đường hủy khác (Manager từ chối, TADSS từ chối, job hết hạn duyệt, job hết hạn thanh toán) dùng cùng dịch vụ với lý do `APPROVAL_REJECTED`, `POLICY_REJECTED`, `APPROVAL_EXPIRED`, `PAYMENT_EXPIRED` (hoặc `SYSTEM_CLEANUP` khi đơn chưa có giao dịch nào).
