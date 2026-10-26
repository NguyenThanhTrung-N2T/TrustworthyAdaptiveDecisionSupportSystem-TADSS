# SƠ ĐỒ HOẠT ĐỘNG VÀ SƠ ĐỒ TRẠNG THÁI: QUY TRÌNH ĐẶT CỌC

| | |
| :--- | :--- |
| **Mục đích** | Sơ đồ hoạt động (góc nhìn quy trình nghiệp vụ, có làn theo bên tham gia) và sơ đồ trạng thái để đưa vào báo cáo Đồ án 2 |
| **Trạng thái** | Mục 1, 2, 4: **thiết kế Phase 6**. Mục 3, 5: **hiện trạng** (TADSS) |
| **Nguồn sự thật** | [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md), [`DEPOSIT_APPROVAL_API.md`](../../../SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md), [`RESILIENCE_AND_FAULT_TAXONOMY.md`](../architecture/RESILIENCE_AND_FAULT_TAXONOMY.md), [`DECISION_PERSISTENCE_INVARIANTS.md`](../architecture/DECISION_PERSISTENCE_INVARIANTS.md). Khi khác nhau, các tài liệu đó đúng |
| **Bộ sơ đồ** | [`ARCHITECTURE_DIAGRAMS.md`](ARCHITECTURE_DIAGRAMS.md) · [`USE_CASES.md`](USE_CASES.md) · [`SEQUENCE_DIAGRAMS.md`](SEQUENCE_DIAGRAMS.md) · tài liệu này |
| **Cập nhật** | 18/10/2026 (đồng bộ thiết kế Phase 6: quyết định P6, DEPOSIT_APPROVAL_API Mục 11) |

---

## 1. Quy trình đặt sân có cọc, từ đầu đến cuối

*Thiết kế Phase 6.* Mỗi làn là một bên tham gia. Quy trình kết thúc khi khách đã cọc (chờ ngày chơi) hoặc đơn bị hủy.

```mermaid
flowchart TB
    subgraph L1["Khách hàng"]
        k1([Bắt đầu]) --> k2[Chọn sân, khung giờ,<br/>khuyến mãi]
        k2 --> k3[Xác nhận đặt sân]
        k7[Xem trang 'Chờ duyệt cọc']
        k6[Xem tóm tắt cọc,<br/>bấm 'Thanh toán cọc']
        k8[Thanh toán cọc trên VNPay]
        k10{Thanh toán<br/>thành công?}
        k11{Thử lại trong<br/>ân hạn 5 phút?}
        k9[Nhận thông báo<br/>đơn bị hủy]
        k12([Kết thúc: thanh toán<br/>tại quầy khi đến chơi])
    end

    subgraph L2["SmashCourt-BE"]
        b1[Kiểm tra slot,<br/>tạo booking PENDING, giữ slot]
        b2{Cờ quyết định cọc bật?}
        b3[Gửi yêu cầu quyết định]
        b4{TADSS phản hồi<br/>trong 800 ms?}
        b5{Kết quả?}
        b6[Lưu mức cọc,<br/>tạo link VNPay = tiền cọc]
        b14{Tiền cọc ≥ mức<br/>tối thiểu 5.000 đ?}
        b15[CONFIRMED,<br/>không qua VNPay]
        b7[Cọc 100 %,<br/>tạo link VNPay = tổng tiền]
        b8[PENDING_APPROVAL,<br/>giữ slot tới hạn duyệt,<br/>báo chi nhánh]
        b9[Hủy booking, nhả slot]
        b10{Quản lý xử lý<br/>trước hạn?}
        b11[Áp mức cọc của Quản lý,<br/>hạn thanh toán 15 phút]
        b12[Ghi nhận thanh toán<br/>PAID_ONLINE]
        b13([Kết thúc: đã cọc,<br/>chờ ngày chơi])
    end

    subgraph L3["TADSS"]
        t1[Chọn strategy,<br/>chạy trong pipeline chịu lỗi]
        t2[Ghi audit,<br/>trả quyết định]
    end

    subgraph L4["Quản lý chi nhánh"]
        m1[Xem đơn trong hàng chờ]
        m2{Duyệt?}
        m3[Nhập mức cọc và lý do]
        m4[Nhập lý do từ chối]
    end

    subgraph L5["VNPay"]
        v1[Thu tiền, gửi IPN]
    end

    k3 --> b1 --> b2
    b2 -- "tắt" --> b7
    b2 -- "bật" --> b3 --> t1 --> t2 --> b4
    b4 -- "không: timeout, lỗi mạng,<br/>5xx, 401, 413, 429,<br/>400/409 bất thường" --> b7
    b4 -- "có" --> b5
    b5 -- "APPROVED" --> b14
    b14 -- "có" --> b6
    b14 -- "không" --> b15 --> k12
    b5 -- "NEED_APPROVAL" --> b8
    b5 -- "NEED_APPROVAL nhưng hạn duyệt<br/>đã qua lúc nhận hoặc sau giờ chơi" --> b7
    b5 -- "REJECTED" --> b9
    b8 --> k7
    b8 --> m1 --> m2
    m2 -- "duyệt" --> m3 --> b10
    m2 -- "từ chối" --> m4 --> b10
    b10 -- "duyệt kịp, cọc > 0" --> b11 --> k6
    b10 -- "duyệt cọc 0 %" --> b15
    b10 -- "từ chối, hoặc<br/>hết hạn (job hủy)" --> b9
    b6 --> k6
    b7 --> k6
    k6 --> k8 --> v1 --> k10
    k10 -- "có" --> b12 --> b13
    k10 -- "không" --> k11
    k11 -- "thanh toán lại" --> k6
    k11 -- "hủy đơn, hoặc<br/>hết ân hạn (job hủy)" --> b9
    b9 --> k9
```

Ghi chú: nhánh "hết hạn" do job chạy mỗi phút thực hiện, **không bao giờ tự duyệt** (UC-DEPOSIT-09). Thanh toán không kịp trước hạn thì job hủy booking với lý do `PAYMENT_EXPIRED`. Thanh toán thất bại không hủy ngay: khách có tối đa 5 phút để thanh toán lại hoặc tự hủy; mọi lần thanh toán lại không kéo dài quá hạn giữ chỗ tuyệt đối (15 phút từ lúc đặt hoặc lúc duyệt).

## 2. Quy trình Quản lý duyệt mức cọc

*Thiết kế Phase 6.* Chi tiết hơn bước "Quản lý xử lý" ở Mục 1: các điểm kiểm tra và cách xử lý khi TADSS không phản hồi.

```mermaid
flowchart TB
    s([Quản lý bấm 'Duyệt']) --> a1[Nhập phần trăm cọc và lý do]
    a1 --> d1{Dữ liệu hợp lệ?<br/>0–100 %, lý do 5–500 ký tự}
    d1 -- "không" --> e1[Báo lỗi dưới ô nhập] --> a1
    d1 -- "có" --> d2{Đơn thuộc chi nhánh<br/>của Quản lý?}
    d2 -- "không" --> e2([403: không có quyền])
    d2 -- "có" --> d3{Theo DB SmashCourt:<br/>còn PENDING_APPROVAL<br/>và còn hạn?}
    d3 -- "không" --> e3([409: đã xử lý / đã hết hạn /<br/>không còn chờ duyệt])
    d3 -- "có" --> d7{Số tiền cọc = 0<br/>hoặc ≥ 5.000 đ?}
    d7 -- "không" --> e1
    d7 -- "có" --> a2[Gửi ghi đè APPROVE tới TADSS<br/>người duyệt lấy từ phiên đăng nhập]
    a2 --> d4{TADSS trả lời?}
    d4 -- "timeout, 5xx,<br/>429, 401" --> e4[503: báo thử lại,<br/>đơn vẫn chờ duyệt]
    e4 --> d5{Quản lý thử lại<br/>với đúng nội dung cũ?}
    d5 -- "có" --> d3
    d5 -- "không" --> e5([Đơn chờ tới khi<br/>có người duyệt hoặc hết hạn])
    d4 -- "409 đã có ghi đè" --> q9[Đồng bộ: đọc chi tiết quyết định,<br/>áp ghi đè đã lưu nếu đơn còn chờ]
    q9 --> e3
    d4 -- "409 hết hạn" --> e3
    d4 -- "200" --> a3["Cập nhật có điều kiện:<br/>PENDING_APPROVAL → PENDING<br/>(cọc 0 % ⇒ CONFIRMED),<br/>mức cọc do TADSS quy đổi"]
    a3 --> d6{Cập nhật thành công?}
    d6 -- "không: job/khách/<br/>người khác chen vào" --> e3
    d6 -- "có" --> a4[Đặt hạn thanh toán 15 phút,<br/>báo khách qua SignalR và email<br/>khách tự lấy link VNPay]
    a4 --> f([Đơn rời hàng chờ])
```

## 3. Pipeline quyết định bên trong TADSS

*Hiện trạng.* Một lời gọi strategy đi qua các lớp Polly (tham số mặc định của `Tadss:Resilience`). Mọi lỗi của strategy kết thúc ở **cọc 100 %**, không ở hàng chờ của Quản lý (D-13).

```mermaid
flowchart TB
    s([Yêu cầu hợp lệ,<br/>chưa có trong idempotency]) --> c1{Có trong cache?}
    c1 -- "có" --> r1[Kết quả CACHE_HIT]
    c1 -- "không" --> cb{Circuit breaker<br/>của strategy mở?}
    cb -- "mở" --> fb[Fallback: APPROVED cọc 100 %<br/>FALLBACK_CIRCUIT_OPEN]
    cb -- "đóng / nửa mở" --> run[Chạy strategy trên luồng riêng,<br/>tối đa 120 ms]
    run --> ok{Kết quả?}
    ok -- "trả kịp" --> r2[Kết quả AUTOMATED<br/>ghi cache]
    ok -- "quá 120 ms<br/>hoặc lỗi" --> rt{Còn lượt thử lại?<br/>tối đa 2}
    rt -- "còn" --> cb
    rt -- "hết" --> fb2[Fallback: APPROVED cọc 100 %<br/>FALLBACK_STRATEGY_TIMEOUT /<br/>FALLBACK_STRATEGY_EXCEPTION]
    r1 --> w{Cần duyệt nhưng<br/>hết thời gian duyệt?}
    r2 --> w
    w -- "có" --> fb3[APPROVED cọc 100 %<br/>APPROVAL_WINDOW_UNAVAILABLE]
    w -- "không" --> comp[Quy đổi tiền cọc,<br/>tính hạn duyệt]
    fb --> comp
    fb2 --> comp
    fb3 --> comp
    comp --> au{Ghi audit được?}
    au -- "có" --> e1([200: trả quyết định])
    au -- "không" --> e2([503 PERSISTENCE_UNAVAILABLE:<br/>không trả quyết định])

    g[/"Toàn bộ khối từ 'Circuit breaker' tới fallback<br/>chịu hạn cứng 500 ms (Outer timeout):<br/>vượt ⇒ 504 GATEWAY_TIMEOUT"/]
```

## 4. Vòng đời booking sau khi có TADSS

*Thiết kế Phase 6.* Trạng thái và chuyển trạng thái của Đồ án 1 lấy theo `Helpers/BookingStatusTransition.cs` của SmashCourt-BE; phần **mới** do TADSS là `PENDING_APPROVAL` và các chuyển trạng thái có nhãn "(mới)".

```mermaid
stateDiagram-v2
    direction TB
    [*] --> PENDING: Đặt online (booking được tạo,<br/>giữ slot)
    [*] --> CONFIRMED: Đặt tại quầy (không qua TADSS)

    PENDING --> PENDING_APPROVAL: (mới) TADSS NEED_APPROVAL
    PENDING --> CANCELLED: hết hạn thanh toán<br/>(mới) TADSS REJECTED<br/>(mới) khách, nhân viên hủy
    PENDING --> CONFIRMED: (mới) cọc dưới mức tối thiểu
    PENDING --> PAID_ONLINE: VNPay IPN thành công<br/>(trả số tiền cọc)

    PENDING_APPROVAL --> PENDING: (mới) Quản lý duyệt kèm mức cọc
    PENDING_APPROVAL --> CANCELLED: (mới) Quản lý từ chối<br/>hết hạn duyệt (job)<br/>khách, nhân viên hủy (Q8)
    PENDING_APPROVAL --> CONFIRMED: (mới) Duyệt cọc 0 % (Q4)

    CONFIRMED --> IN_PROGRESS: Check-in
    CONFIRMED --> CANCELLED: Hủy (khách chỉ trước giờ bắt đầu)
    CONFIRMED --> NO_SHOW: Không check-in sau giờ chơi + 15 phút (job)

    PAID_ONLINE --> IN_PROGRESS: Check-in
    PAID_ONLINE --> CANCELLED_PENDING_REFUND: Hủy (hoàn theo chính sách,<br/>khách chỉ trước giờ bắt đầu)
    PAID_ONLINE --> CANCELLED: Hủy, chính sách hoàn 0 %
    PAID_ONLINE --> NO_SHOW: Không check-in sau giờ chơi + 15 phút (job)
    PAID_ONLINE --> COMPLETED: có trong bảng chuyển trạng thái,<br/>không mã nào dùng (N-35)

    IN_PROGRESS --> PENDING_PAYMENT: Hết giờ chơi
    IN_PROGRESS --> COMPLETED: Check-out, hoặc hết giờ chơi<br/>khi hóa đơn đã trả đủ
    PENDING_PAYMENT --> COMPLETED: Check-out, thu phần còn lại

    CANCELLED_PENDING_REFUND --> CANCELLED_REFUNDED: Xác nhận hoàn tiền
    CANCELLED --> CANCELLED_PENDING_REFUND: (mới) tiền VNPay về<br/>sau khi đơn đã hủy, hoàn 100 %
    CANCELLED_REFUNDED --> CANCELLED_PENDING_REFUND: (mới) thêm một khoản về<br/>sau khi đã hoàn xong, hoàn 100 % khoản đó

    CANCELLED --> [*]
    CANCELLED_REFUNDED --> [*]
    COMPLETED --> [*]
    NO_SHOW --> [*]

    note right of PENDING_APPROVAL
        Giữ slot (trạng thái "đang hoạt động")
        Hạn duyệt = min(tạo + 30 phút, giờ chơi − 2 giờ)
        Không bao giờ tự duyệt khi hết hạn
    end note
```

Ảnh hưởng lên mã của BE khi hiện thực trạng thái này:

| Chỗ trong BE | Thay đổi |
| :--- | :--- |
| `Models/Enums/BookingStatus.cs`, enum `booking_status` của PostgreSQL | Thêm `PENDING_APPROVAL` |
| `BookingStatusTransition.CanTransition` | Thêm các chuyển trạng thái có nhãn "(mới)" (chuyển `PENDING → CANCELLED` đã có; `PAID_ONLINE → CANCELLED` khi hoàn 0 % đã thêm vào `CanTransition` ở 6A-1) |
| `BookingStatusTransition.GetActiveStatuses` | Thêm `PENDING_APPROVAL` để time grid, kiểm tra trùng giờ và lịch theo ngày coi slot là đã có người giữ |
| Job hủy `PENDING` quá hạn (Job-01) | Không đụng `PENDING_APPROVAL` (đơn chờ duyệt có job riêng, UC-DEPOSIT-09) |
| Job kết thúc giờ chơi (Job-02), job đánh `NO_SHOW` (Job-04) | Cập nhật có điều kiện từng đơn (P6-25): không ghi đè check-in, check-out hay lệnh hủy vừa xảy ra |
| Hủy và hoàn tiền từ `PAID_ONLINE` | Số tiền hoàn tính trên số đã thanh toán (tiền cọc), không còn là `finalTotal`; % hoàn tính đúng múi giờ (Q12, chốt 18/10) |
| Mọi đường hủy | Ghi lý do hủy (`cancel_reason`); lịch sử gửi TADSS và kết quả báo về TADSS đọc lý do này |

## 5. Vòng đời một quyết định trong TADSS

*Hiện trạng.* Bản ghi quyết định **bất biến**: không bao giờ bị sửa hay xóa (trigger chặn `UPDATE`/`DELETE`/`TRUNCATE`). Ghi đè và kết quả là bản ghi riêng, mỗi loại tối đa một bản cho một quyết định.

```mermaid
stateDiagram-v2
    direction LR
    state "Đã quyết định" as Decided
    state "Đã ghi đè" as Overridden
    state "Có kết quả thật" as HasOutcome
    [*] --> Decided: POST /decisions<br/>ghi decision_executions

    state Decided {
        [*] --> APPROVED
        [*] --> NEED_APPROVAL
        [*] --> REJECTED
    }

    NEED_APPROVAL --> Overridden: POST /override (APPROVE kèm mức cọc,<br/>hoặc REJECT), trước hạn duyệt<br/>ghi decision_overrides
    Decided --> HasOutcome: POST /outcome<br/>ghi decision_outcomes
    Overridden --> HasOutcome: POST /outcome

    HasOutcome --> [*]

    note right of NEED_APPROVAL
        Nằm trong hàng chờ đối soát khi:
        chưa ghi đè, chưa có kết quả,
        hạn duyệt chưa qua
    end note
```

| Bảng (schema `tadss`) | Nội dung | Ràng buộc |
| :--- | :--- | :--- |
| `decision_executions` | Quyết định: request, strategy và phiên bản, kết quả, lý do, thời gian xử lý, có dùng fallback/cache, bản `PRIMARY` hay `SHADOW` | Bất biến; idempotency key duy nhất cho bản `PRIMARY` |
| `decision_overrides` | Ghi đè của người duyệt: hành động, mức cọc, người duyệt, lý do, thời điểm | Bất biến; tối đa một bản mỗi quyết định |
| `decision_outcomes` | Kết quả thật: `COMPLETED`, `NO_SHOW`, `CANCELLED_BY_CUSTOMER`, `CANCELLED_BY_STAFF`, `PAYMENT_EXPIRED`, `APPROVAL_EXPIRED`, `REJECTED` | Bất biến; tối đa một bản mỗi quyết định |

