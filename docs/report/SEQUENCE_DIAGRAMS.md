# SƠ ĐỒ TUẦN TỰ: LUỒNG ĐẶT CỌC QUA TADSS

| | |
| :--- | :--- |
| **Mục đích** | Sơ đồ tuần tự để đưa vào báo cáo Đồ án 2: thông điệp giữa FE, BE, TADSS, VNPay và database trong các luồng có quyết định cọc |
| **Trạng thái** | Mục 1, 2, 4: **thiết kế Phase 6** (BE/FE chưa hiện thực). Mục 3: **hiện trạng** (bên trong TADSS, đã có trong mã) |
| **Nguồn sự thật** | [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md) (BE ↔ TADSS), [`DEPOSIT_APPROVAL_API.md`](../../../SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md) (BE ↔ FE). Khi khác nhau, các tài liệu đó đúng |
| **Use case tương ứng** | [`USE_CASES.md`](USE_CASES.md) |
| **Bộ sơ đồ** | [`ARCHITECTURE_DIAGRAMS.md`](ARCHITECTURE_DIAGRAMS.md) · [`USE_CASES.md`](USE_CASES.md) · tài liệu này · [`ACTIVITY_AND_STATE_DIAGRAMS.md`](ACTIVITY_AND_STATE_DIAGRAMS.md) |
| **Cập nhật** | 17/10/2026 |

---

## 1. Đặt sân online có quyết định cọc (UC-DEPOSIT-01)

*Thiết kế Phase 6.* Điểm thiết kế quan trọng: BE **không** gọi TADSS bên trong transaction ghi booking. Booking và slot lock được ghi trước (transaction 1), gọi TADSS sau đó, rồi áp kết quả bằng cập nhật có điều kiện trạng thái (transaction 2). Nếu BE dừng giữa hai bước, booking `PENDING` chưa có thanh toán sẽ bị job hủy `PENDING` quá hạn hiện có dọn sau 10 phút.

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

    C->>FE: Xác nhận đặt sân
    FE->>BE: POST /api/bookings/online
    BE->>DB: Kiểm tra slot trống, slot lock
    BE->>DB: Transaction 1: booking PENDING, booking_courts,<br/>hóa đơn, slot lock (chưa tạo thanh toán)

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
        FE->>V: Chuyển khách tới VNPay (UC-DEPOSIT-02)
    else NEED_APPROVAL (đơn ≥ 2.000.000 VND, hoặc ≥ 2 lần không đến)
        T-->>BE: 200 status NEED_APPROVAL, approvalExpiresAt, reasonCode, decisionId
        BE->>DB: Transaction 2: PENDING → PENDING_APPROVAL,<br/>lưu hạn duyệt và lý do, gia hạn slot lock tới hạn duyệt
        BE-->>FE: 201 status PENDING_APPROVAL, approvalExpiresAt, paymentUrl = null
        BE-)H: BookingCreated, BookingPendingApproval,<br/>PendingApprovalQueueChanged (tới chi nhánh, Chủ hệ thống)
        FE->>C: Trang "Chờ duyệt cọc" (đếm ngược)
    else REJECTED (Q1, khách lặp lại việc giữ chỗ không thanh toán)
        T-->>BE: 200 status REJECTED
        BE->>DB: Transaction 2: PENDING → CANCELLED, xóa slot lock
        BE-->>FE: 400 BOOKING_REJECTED
        FE->>C: Thông báo không nhận đơn
    else Cờ tắt, hoặc TADSS timeout / lỗi mạng / 5xx / 401 / 413 / 429
        Note over BE: Ghi log kèm correlation id. Khách không thấy lỗi
        BE->>DB: Transaction 2: cọc 100 %, nguồn FULL_PAYMENT_FALLBACK,<br/>thanh toán VNPay = finalTotal
        BE->>BE: Ký URL thanh toán VNPay (100 %)
        BE-->>FE: 201 status PENDING, paymentUrl, depositRate = 1
        BE-)H: BookingCreated
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

    BE->>BE: Kiểm dữ liệu nhập, quyền chi nhánh
    BE->>DB: Đọc booking: còn PENDING_APPROVAL? còn hạn? có decisionId?
    alt Không còn chờ duyệt, đã quá hạn, hoặc thiếu decisionId
        BE-->>FE: 409 APPROVAL_ALREADY_RESOLVED / APPROVAL_EXPIRED /<br/>BOOKING_NOT_PENDING_APPROVAL / APPROVAL_DECISION_NOT_FOUND
    else Hợp lệ
        BE->>T: POST /api/v2/decisions/{decisionId}/override<br/>{ action: APPROVE, deposit.rate: 0.3, reason }<br/>X-Actor-Id = userId từ JWT, X-Idempotency-Key = {decisionId}:APPROVE
        alt 200 OK
            T-->>BE: deposit { rate 0.3, amount }, actorId, overriddenAt
            BE->>DB: Cập nhật có điều kiện: PENDING_APPROVAL → PENDING,<br/>mức cọc do TADSS trả, nguồn MANAGER, người duyệt,<br/>hạn thanh toán 30 phút (Q2), gia hạn slot lock, thanh toán VNPay
            alt Cập nhật thành công
                BE-->>FE: 200 approvalResult APPROVED, depositAmount, expiresAt
                BE-)H: BookingApprovalResolved, PendingApprovalQueueChanged
                H-)C: Đơn đã được duyệt (khách tải lại chi tiết để lấy link thanh toán)
            else Booking đã đổi trạng thái trong lúc gọi (job hết hạn, khách hủy, request khác)
                BE-->>FE: 409, không hồi sinh booking
            end
        else 409 ALREADY_OVERRIDDEN / IDEMPOTENCY_KEY_REUSE
            T-->>BE: 409
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

Từ chối (`POST /api/bookings/{bookingId}/reject`) đi cùng đường trên với `action: REJECT`, không gửi `deposit`, khóa `{decisionId}:REJECT`; nhánh 200 chuyển `PENDING_APPROVAL → CANCELLED`, xóa slot lock, báo người đăng ký chờ slot và phát thêm `BookingCancelled`.

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
        J1->>DB: Cập nhật có điều kiện: → CANCELLED (nguồn hủy SYSTEM),<br/>xóa slot lock
        J1-)H: BookingExpired, PendingApprovalQueueChanged (REMOVED, EXPIRED)
        Note over J1: Gửi email cho khách, báo người đăng ký chờ slot.<br/>Không bao giờ tự duyệt
    end

    J2->>DB: Booking đã kết thúc, có decisionId, chưa báo kết quả
    loop Từng booking
        J2->>T: POST /api/v2/decisions/{decisionId}/outcome<br/>{ outcome, occurredAt }
        alt 200 (kể cả gửi trùng cùng nội dung)
            T-->>J2: Đã ghi (hoặc trả bản đã lưu)
            J2->>DB: Đánh dấu đã báo
        else 409 OUTCOME_ALREADY_RECORDED
            T-->>J2: Đã có kết quả khác
            J2->>DB: Đánh dấu, ghi log để đối soát
        else Lỗi tạm thời
            Note over J2: Để lần chạy sau thử lại
        end
    end
```

Ánh xạ trạng thái booking sang `outcome`: [`USE_CASES.md`](USE_CASES.md) Mục 5.10.

