# ĐẶC TẢ TÍCH HỢP SMASHCOURT ↔ TADSS & DECISION CONTRACT v2

| | |
| :--- | :--- |
| **Trạng thái** | v2.0.0 — **bản nháp**, đóng băng tại Milestone 5 (01/11/2026) |
| **Thay thế** | Contract v1.0.0 (Phase 1–4, chưa có consumer) — đã gỡ khỏi mã và tài liệu; tra lại qua tag git `phase4-baseline` |
| **Consumer** | `SmashCourt-BE` (Phase 6); adapter Hotel Booking Demand (kiểm chứng độc lập domain) |
| **Sở hữu** | Tài liệu này là nguồn duy nhất về contract v2 và luồng tích hợp. Bất biến lưu trữ: [`DECISION_PERSISTENCE_INVARIANTS.md`](../architecture/DECISION_PERSISTENCE_INVARIANTS.md). Pipeline chịu lỗi: [`RESILIENCE_AND_FAULT_TAXONOMY.md`](../architecture/RESILIENCE_AND_FAULT_TAXONOMY.md) |

---

## 1. NGHIỆP VỤ ĐẶT CỌC (ĐÃ CHỐT)

| Tình huống | Bên quyết định | Kết quả |
| :--- | :--- | :--- |
| Strategy xác định được mức cọc | TADSS | `APPROVED` + `deposit.amount` → SmashCourt tạo thanh toán VNPay đúng số tiền cọc |
| Strategy không xác định được (giá trị lớn, rủi ro, chính sách yêu cầu duyệt) | Manager | `NEED_APPROVAL` → booking chờ duyệt; Manager **APPROVE kèm mức cọc** hoặc **REJECT** |
| Strategy lỗi / timeout / circuit breaker mở (fallback của runtime) | TADSS | `APPROVED` với cọc **100%**, `executionType = FALLBACK`, `fallbackUsed = true`, `reasonCode` ∈ `FALLBACK_STRATEGY_TIMEOUT`, `FALLBACK_STRATEGY_EXCEPTION`, `FALLBACK_CIRCUIT_OPEN`. Không vào hàng chờ duyệt, không override được |
| Cần duyệt nhưng không còn đủ thời gian duyệt trước giờ chơi (quy tắc *approval window*, Mục 1.1) | TADSS | `APPROVED` với cọc 100%, `reasonCode = APPROVAL_WINDOW_UNAVAILABLE` |
| Strategy từ chối theo chính sách | TADSS | `REJECTED` → SmashCourt hủy booking |
| Feature flag tắt, hoặc TADSS không phản hồi / timeout / lỗi 5xx / lỗi xác thực | SmashCourt | Cọc **100%** (hành vi Đồ án 1), không chặn khách |
| Hết hạn duyệt mà Manager chưa xử lý | SmashCourt (job) | Hủy booking, giải phóng sân. **Không bao giờ tự động duyệt** |

Khách hàng **không** tự chọn mức cọc. Phần còn lại (tổng tiền − cọc) được thu khi checkout tại quầy.

**Nguyên tắc:** mọi sự cố kỹ thuật (strategy lỗi → fallback, TADSS lỗi/không phản hồi, tắt flag, không kịp duyệt) đều dẫn tới cọc 100%;
`NEED_APPROVAL` chỉ dành cho bất định **nghiệp vụ** do strategy quyết định (đơn giá trị lớn, lịch sử không đến…), nên circuit breaker mở
không đẩy hàng loạt đơn sang Manager (quyết định D-13, 04/10/2026 — trước đó fallback trả `NEED_APPROVAL`).

### 1.1. Quy tắc approval window (runtime TADSS)

- `approvalExpiresAt = min(createdAt + Tadss:Approval:WindowMinutes, earliestStart − Tadss:Approval:MinLeadMinutes)`; mặc định `WindowMinutes = 30`, `MinLeadMinutes = 120`.
- Nếu strategy trả `NEED_APPROVAL` nhưng `approvalExpiresAt ≤ createdAt` → runtime trả `APPROVED`, cọc 100%, `reasonCode = APPROVAL_WINDOW_UNAVAILABLE`. Kết quả gốc của strategy vẫn được lưu nguyên trong audit (`strategy_status`).

---

## 2. LUỒNG TÍCH HỢP

### 2.1. Đặt sân online

```mermaid
sequenceDiagram
    autonumber
    actor C as Khách
    participant FE as smashcourt-fe
    participant BE as SmashCourt-BE
    participant T as TADSS
    participant V as VNPay
    actor M as Manager

    C->>FE: Chọn sân, khung giờ
    FE->>BE: POST /api/bookings/online
    BE->>BE: Tạo booking PENDING, giữ slot, tính giá
    alt EnableTadssDecision = true
        BE->>T: POST /api/v2/decisions (X-Api-Key, X-Idempotency-Key = bookingId)
        alt APPROVED
            T-->>BE: deposit.amount
            BE->>V: Tạo URL thanh toán = deposit.amount
            BE-->>FE: paymentUrl + số tiền cọc + phần còn lại
        else NEED_APPROVAL
            T-->>BE: approvalExpiresAt
            BE->>BE: booking = PENDING_APPROVAL, giữ slot tới approvalExpiresAt
            BE-->>FE: trạng thái chờ duyệt
            BE-)M: SignalR: có đơn chờ duyệt
        else REJECTED
            BE->>BE: Hủy booking, giải phóng slot
            BE-->>FE: thông báo từ chối
        else Timeout / 5xx / 401
            BE->>V: Tạo URL thanh toán = 100%
            BE-->>FE: paymentUrl (100%)
        end
    else EnableTadssDecision = false
        BE->>V: Tạo URL thanh toán = 100%
    end
```

### 2.2. Manager duyệt

```mermaid
sequenceDiagram
    autonumber
    actor M as Manager
    participant FE as smashcourt-fe
    participant BE as SmashCourt-BE
    participant T as TADSS
    actor C as Khách
    M->>FE: Mở hàng chờ (chi nhánh của mình)
    FE->>BE: GET /api/decision-approvals?branchId=...
    BE->>T: GET /api/v2/decisions/pending-approvals?branchId=...
    M->>FE: Duyệt, cọc 30%, lý do
    FE->>BE: POST /api/decision-approvals/{decisionId}/approve
    BE->>T: POST /api/v2/decisions/{id}/override (X-Actor-Id = userId từ JWT)
    alt 200 OK
        BE->>BE: Cập nhật booking, tạo URL thanh toán cọc
        BE-)C: SignalR/email: link thanh toán
    else 409 ALREADY_OVERRIDDEN / APPROVAL_EXPIRED
        BE-->>FE: Thông báo xung đột / đã hết hạn
    end
```

Endpoint phía SmashCourt-BE (`/api/decision-approvals/...`) là bản đề xuất; API contract BE ↔ FE được chốt ở task T5.22 (W08).

### 2.3. Báo kết quả thực tế (outcome)

Khi booking kết thúc, job SmashCourt gửi `POST /api/v2/decisions/{decisionId}/outcome` với một trong các giá trị: `COMPLETED`, `NO_SHOW`, `CANCELLED_BY_CUSTOMER`, `CANCELLED_BY_STAFF`, `PAYMENT_EXPIRED`, `APPROVAL_EXPIRED`, `REJECTED`. Đây là nhãn kết quả phục vụ phân tích và Khóa luận.

---

## 3. DECISION CONTRACT v2

### 3.1. Quy ước chung

- Base path `/api/v2`. JSON `camelCase`; enum dạng `SCREAMING_SNAKE_CASE`; JSON schema đặt `additionalProperties: false` (lưu tại `docs/contracts/schemas/v2/`: `decision-request`, `decision-result`, `decision-detail`, `pending-approvals`, `override-request`, `override-response`, `outcome-request`, `outcome-response`). Tài liệu OpenAPI [`openapi-v2.yaml`](openapi-v2.yaml) được **sinh từ mã** và có test chống lệch (CT-01); khi hai bên khác nhau, JSON schema viết tay là nguồn chuẩn.
- **Phạm vi đã hiện thực:** năm endpoint — `POST /api/v2/decisions` (3.3), `GET /api/v2/decisions/{decisionId}` (3.4), `GET /api/v2/decisions/pending-approvals` (3.5), `POST /api/v2/decisions/{decisionId}/override` (3.6) và `POST /api/v2/decisions/{decisionId}/outcome` (3.7). Mọi request `/api/*` phải mang `X-Api-Key` hợp lệ (xem 3.2); `/health/live` và `/health/ready` được miễn.
- **Giới hạn của contract:** `X-Idempotency-Key` 1..128 ký tự; `X-Actor-Id` 1..100 ký tự; `reason` của override 5..500 ký tự và không chỉ gồm khoảng trắng; `page` ≥ 1 (mặc định 1); `pageSize` 1..100 (mặc định 20). Độ dài `X-Actor-Id` và `reason` đếm theo **Unicode code point** (khớp `char_length` của PostgreSQL, ký tự ngoài BMP như emoji tính là một); vi phạm → 400 `VALIDATION_ERROR`.
- Thời gian dùng ISO 8601 **kèm offset** (`2026-11-20T18:00:00+07:00`). Mọi quy tắc theo khung giờ được tính theo `branchTimeZone` (IANA, ví dụ `Asia/Ho_Chi_Minh`).
- Tiền là số thập phân, đơn vị theo `currency` (SmashCourt: `VND`).
- Thời gian thiếu offset (ví dụ `2026-11-20T18:00:00`) bị từ chối với 400 `VALIDATION_ERROR` — không suy múi giờ từ máy chủ.
- Enum chỉ nhận đúng tên đã định nghĩa, phân biệt hoa thường; không nhận số hay chuỗi số. Trường lạ hoặc thiếu trường bắt buộc → 400 `VALIDATION_ERROR`.

### 3.2. Header

| Header | Bắt buộc | Mô tả |
| :--- | :---: | :--- |
| `X-Api-Key` | Mọi endpoint | Khóa service-to-service do TADSS cấp cho từng client; server so SHA-256 của khóa với danh sách đã cấu hình (so sánh thời gian cố định). Thiếu hoặc sai → 401 `UNAUTHORIZED` kèm `WWW-Authenticate: ApiKey header="X-Api-Key"`, không gọi tới nghiệp vụ. Không bao giờ được ghi vào log |
| `X-Idempotency-Key` | `POST /decisions`, `POST /override`, `POST /outcome` | 1..128 ký tự; SmashCourt dùng `bookingId` (đánh giá) hoặc `{decisionId}:{action}` (override). Thiếu hoặc toàn khoảng trắng → 400 `MISSING_HEADER`; dài hơn 128 → 400 `VALIDATION_ERROR`. Ở `/outcome` key chỉ được kiểm tra, **không được lưu** (chống trùng theo nội dung, xem 3.7) |
| `X-Actor-Id` | `POST /override` | Định danh Manager do SmashCourt lấy từ JWT đã xác thực; 1..100 ký tự (code point). Thiếu hoặc toàn khoảng trắng → 400 `MISSING_HEADER`; dài hơn 100 → 400 `VALIDATION_ERROR` |
| `X-Correlation-ID` | Tùy chọn | Được echo lại trong response và gắn vào log, trace, audit. Giá trị rỗng/khoảng trắng hoặc dài hơn 128 ký tự bị bỏ qua: server cấp một GUID mới (không từ chối request) |

### 3.3. `POST /api/v2/decisions`

**Request**

```json
{
  "reservationId": "5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11",
  "branchId": "1f2e3d4c-0000-4000-8000-000000000001",
  "branchTimeZone": "Asia/Ho_Chi_Minh",
  "items": [
    {
      "resourceId": "c0a8012e-0000-4000-8000-0000000000aa",
      "resourceTypeId": "c0a8012e-0000-4000-8000-0000000000bb",
      "startTime": "2026-11-20T18:00:00+07:00",
      "endTime": "2026-11-20T20:00:00+07:00",
      "price": 240000
    }
  ],
  "customer": {
    "customerId": "9d3f1c2a-0000-4000-8000-000000000123",
    "isGuest": false,
    "tier": "GOLD",
    "memberSince": "2025-03-14T08:00:00+07:00",
    "history": {
      "completedCount": 12, "noShowCount": 1, "cancelledCount": 2,
      "lateCancelledCount": 1, "paymentExpiredCount": 0,
      "lastCompletedAt": "2026-11-02T20:00:00+07:00"
    }
  },
  "totalPrice": 228000,
  "currency": "VND",
  "channel": "ONLINE",
  "demand": { "occupancyRate": 0.75, "waitlistCount": 2 },
  "requestedAt": "2026-11-18T09:15:00+07:00",
  "metadata": { "promotionCode": "WINTER" }
}
```

| Trường | Ràng buộc |
| :--- | :--- |
| `items` | 1..20 phần tử; mỗi `endTime > startTime`; `price ≥ 0`. Thời lượng mỗi item là chính sách của runtime, cấu hình bằng `Tadss:Validation:MinItemMinutes` / `MaxItemMinutes` (mặc định 30 phút – 12 giờ); vượt giới hạn → 400 `DOMAIN_INVARIANT_VIOLATION` |
| `customer.customerId` | Bắt buộc khi `isGuest = false`; `null` khi `isGuest = true` |
| `customer.tier` | Một trong `STANDARD`, `SILVER`, `GOLD`, `PLATINUM`, `DIAMOND` (client tự ánh xạ hạng nội bộ) |
| `customer.memberSince` | Tùy chọn; thời điểm tạo tài khoản (có offset); `null`/bỏ trống với khách vãng lai |
| `customer.history` | Tùy chọn (gửi được cả với khách vãng lai); số đếm ≥ 0. `completedCount`: booking hoàn tất. `noShowCount`: không đến. `cancelledCount`: khách tự hủy và được hoàn đủ theo chính sách hủy. `lateCancelledCount` (tùy chọn): khách tự hủy nhưng không được hoàn đủ (hủy muộn). `paymentExpiredCount` (tùy chọn): booking bị hệ thống hủy do hết hạn thanh toán. Hủy do nhân viên không tính |
| `demand` | Tùy chọn; ảnh chụp nhu cầu tại khung giờ khi tạo booking: `occupancyRate` ∈ [0, 1] (tỷ lệ sân hoạt động của chi nhánh đã có booking giao với khung giờ, không tính booking này), `waitlistCount` ≥ 0 (số người đăng ký chờ slot còn hạn). **Không** đưa vào `request_hash` vì thay đổi giữa các lần retry |
| `totalPrice` | Tổng sau giảm giá, ≥ 0 |
| `currency` | Mã ISO 4217, 3 chữ cái in hoa |
| `branchTimeZone` | Mã múi giờ IANA (ví dụ `Asia/Ho_Chi_Minh`); mã Windows hoặc mã lạ → 400 `VALIDATION_ERROR` |
| `channel` | `ONLINE` \| `WALK_IN` (đặt tại quầy) — trùng tên với `BookingSource` của SmashCourt-BE |
| `metadata` | Tùy chọn; object chuỗi → chuỗi, không ảnh hưởng quyết định và không đưa vào `request_hash` |

**Hai tầng lỗi request.** Vi phạm ràng buộc **hình thức** diễn đạt được bằng JSON schema request (thiếu trường, sai kiểu/định dạng, trường lạ, giá trị ngoài miền, số phần tử `items`, mã múi giờ, mã tiền tệ…) → 400 `VALIDATION_ERROR`. Chỉ quy tắc **ngoài schema** (`endTime > startTime`, thời lượng item theo `Tadss:Validation`) → 400 `DOMAIN_INVARIANT_VIOLATION`. Cùng một vi phạm hình thức không bao giờ trả hai mã khác nhau.

**Response 200**

```json
{
  "decisionId": "0f9c7e5a-0000-4000-8000-00000000d001",
  "reservationId": "5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11",
  "status": "APPROVED",
  "deposit": { "rate": 0.5, "amount": 114000, "currency": "VND" },
  "strategyName": "MockAlternativeDecisionStrategy",
  "strategyVersion": "2.0.0",
  "executionType": "AUTOMATED",
  "fallbackUsed": false,
  "reasonCode": "PEAK_HOUR_DEPOSIT",
  "reasons": ["Khung giờ cao điểm (18:00 thứ Sáu, giờ chi nhánh) áp dụng cọc 50%."],
  "recommendations": [],
  "approvalExpiresAt": null,
  "evaluatedAt": "2026-11-18T02:15:00.041Z"
}
```

| Trường | Quy tắc |
| :--- | :--- |
| `status` | `APPROVED` \| `NEED_APPROVAL` \| `REJECTED` |
| `deposit` | **Bắt buộc** khi `APPROVED` (`0 ≤ rate ≤ 1`, `amount = round(totalPrice × rate)`); `null` trong các trường hợp còn lại |
| `executionType` | `AUTOMATED` \| `FALLBACK` \| `CACHE_HIT` |
| `approvalExpiresAt` | Bắt buộc khi `NEED_APPROVAL` |
| `recommendations[]` | `{ "type": ..., "description": "..." }`; `type` ∈ `REQUIRE_APPROVAL`, `ALTERNATIVE_SLOT`, `ALTERNATIVE_RESOURCE`, `PRIORITY_ESCALATION` |

### 3.4. `GET /api/v2/decisions/{decisionId}`

Trả chi tiết của một quyết định:

```json
{
  "decisionId": "...", "reservationId": "...",
  "request": { "...": "request v2 đã chuẩn hóa (cùng hình dạng Mục 3.3)" },
  "result": { "status": "APPROVED", "strategyStatus": "NEED_APPROVAL", "deposit": { "rate": 1, "amount": 228000, "currency": "VND" },
              "reasonCode": "APPROVAL_WINDOW_UNAVAILABLE",
              "reasons": ["Không còn đủ thời gian để Manager duyệt trước giờ bắt đầu, nên áp dụng mức cọc 100% thay cho bước duyệt."],
              "recommendations": [], "approvalExpiresAt": null },
  "execution": { "strategyName": "...", "strategyVersion": "2.0.0", "executionType": "AUTOMATED", "executionTimeMs": 3,
                 "fallbackUsed": false, "failureReason": null, "cacheHit": false },
  "override": null,
  "outcome": null,
  "createdAt": "2026-11-18T02:15:00.041+00:00",
  "correlationId": "..."
}
```

`override` là một object (hình dạng response Mục 3.6) hoặc `null` — mỗi quyết định có tối đa một override (INV-5). `outcome` có hình dạng response Mục 3.7 hoặc `null`.

### 3.5. `GET /api/v2/decisions/pending-approvals?branchId={id}&page=1&pageSize=20`

- Tham số: `branchId` (bắt buộc, GUID), `page` (mặc định 1), `pageSize` (mặc định 20, tối đa 100). Thiếu `branchId`, giá trị không đọc được (ví dụ `page=abc`) hoặc ngoài miền (`page < 1`, `pageSize` ngoài 1..100) → 400 `VALIDATION_ERROR`.
- Chỉ gồm **bản quyết định mới nhất** của mỗi `reservationId` (trong cùng chi nhánh, chỉ xét bản `PRIMARY`) thỏa: `status = NEED_APPROVAL`, chưa override, chưa có outcome, `approvalExpiresAt > now` (so nghiêm ngặt; `now` lấy từ đồng hồ của runtime). Bản `SHADOW` không bao giờ xuất hiện.
- Sắp theo `approvalExpiresAt` tăng dần, hòa thì theo `decisionId` nên phân trang tất định.
- Mỗi item: `decisionId`, `reservationId`, `branchId`, `totalPrice`, `currency`, `earliestStart`, `customer.isGuest`, `customer.tier`, `reasonCode`, `fallbackUsed`, `approvalExpiresAt`, `createdAt`.
- Bao ngoài: `{ "items": [...], "page": 1, "pageSize": 20, "totalItems": 42, "totalPages": 3, "hasNext": true, "hasPrev": false }`. Trang và `totalItems` đọc bằng hai truy vấn riêng, không chung transaction nên tổng chỉ là số đếm tại thời điểm đọc.
- Schema response: `pending-approvals.schema.json`.

### 3.6. `POST /api/v2/decisions/{decisionId}/override`

```json
{ "action": "APPROVE", "deposit": { "rate": 0.3 }, "reason": "Khách quen, chi nhánh bảo lãnh" }
```

- `action`: `APPROVE` (bắt buộc **đúng một** trong `deposit.rate` hoặc `deposit.amount`) \| `REJECT` (không gửi `deposit`). `reason` 5..500 ký tự (code point), không chỉ gồm khoảng trắng. Schema: `override-request.schema.json`, `override-response.schema.json`.
- **`REJECT` kèm `"deposit": null`:** runtime chấp nhận (DTO cho phép `null`, coi như không gửi `deposit`), nhưng JSON schema `override-request` từ chối vì chỉ sự có mặt của khóa `deposit` đã vi phạm nhánh `REJECT` (có test `reject-with-null-deposit`). Schema là nguồn chuẩn: client phải bỏ hẳn khóa `deposit` khi `REJECT`, không gửi `null`.
- **Mức cọc khi `APPROVE`** (một nơi quy đổi): gửi `rate` thì `amount = round(totalPrice × rate)` theo số chữ số thập phân của tiền tệ; gửi `amount` thì giữ nguyên `amount` và `rate = round(amount / totalPrice, 4)` (làm tròn xa số 0; `totalPrice = 0` thì `amount` phải là 0 và `rate = 0`). `totalPrice` và `currency` lấy từ request đã lưu của quyết định. Từ chối bằng 400 `VALIDATION_ERROR` (không làm tròn âm thầm) khi: `rate` ngoài `[0, 1]` hoặc quá 4 chữ số thập phân; `amount` âm, lớn hơn `totalPrice` hoặc nhiều chữ số thập phân hơn tiền tệ cho phép (VND: 0).
- Mỗi quyết định có **tối đa một** override (ràng buộc `UNIQUE(decision_id)` trên bảng `decision_overrides`). Bản ghi đánh giá bất biến nên chi tiết quyết định (3.4) vẫn hiện `result.status = NEED_APPROVAL` kèm `override`; quyết định đã override rời hàng chờ (3.5).
- **Thứ tự kiểm tra**, dừng ở bước đầu tiên không đạt:
  1. Request sai hình thức (header, body, `reason`, cấu trúc `deposit`, `rate`) → 400.
  2. Không có quyết định, hoặc là bản `SHADOW` (không bao giờ lộ cho client) → 404 `DECISION_NOT_FOUND`.
  3. `deposit.amount` sai so với đơn (lớn hơn `totalPrice` hoặc quá số chữ số thập phân của tiền tệ) → 400 `VALIDATION_ERROR` (trường `deposit.amount`). Bước này đứng **trước** bước 4: request như vậy gửi vào quyết định đã được override vẫn là 400, không phải 409 hay replay.
  4. Đã có override: cùng `X-Idempotency-Key` và cùng nội dung → 200 trả bản đã lưu (không đóng dấu lại `overriddenAt`); cùng key khác nội dung → 409 `IDEMPOTENCY_KEY_REUSE`; khác key → 409 `ALREADY_OVERRIDDEN`. "Cùng nội dung" là cùng `action`, `reason` (so sánh ordinal), `X-Actor-Id` và cùng mức cọc request gửi (`rate` so với `deposit_rate` đã lưu, `amount` so với `deposit_amount` đã lưu).
  5. `status` không phải `NEED_APPROVAL` hoặc đã có outcome → 409 `NOT_PENDING_APPROVAL`.
  6. `approvalExpiresAt ≤ now` → 409 `APPROVAL_EXPIRED`.
  7. Ghi. Thua tranh chấp `UNIQUE(decision_id)` thì xử lý như bước 4 với bản thắng (20 request song song cùng nội dung đều nhận 200 với cùng một override; 20 request khác key chỉ một nhận 200, 19 còn lại 409 `ALREADY_OVERRIDDEN`).
- **Response 200:** `decisionId`, `action`, `deposit` (đã quy đổi `rate`, `amount`, `currency`; `null` khi `REJECT`), `actorId`, `reason`, `overriddenAt` (UTC).

### 3.7. `POST /api/v2/decisions/{decisionId}/outcome`

```json
{ "outcome": "NO_SHOW", "occurredAt": "2026-11-20T18:30:00+07:00" }
```

Outcome áp cho mọi quyết định `PRIMARY` bất kể trạng thái (kể cả `NEED_APPROVAL` chưa được duyệt); bản `SHADOW` và quyết định không tồn tại → 404 `DECISION_NOT_FOUND`. Mỗi quyết định có tối đa một outcome (`UNIQUE(decision_id)`). Chống trùng theo **nội dung** (`outcome` và `occurredAt` sau khi cắt về microsecond): gửi trùng → 200 trả bản đã lưu (không ghi nhận lại, `reportedAt` giữ nguyên); khác → 409 `OUTCOME_ALREADY_RECORDED`. `X-Idempotency-Key` bắt buộc và được kiểm tra theo 3.2 nhưng **không được lưu** (bảng `decision_outcomes` không có cột này). Schema: `outcome-request.schema.json`, `outcome-response.schema.json`.

**Response 200:** `{ "decisionId": "...", "outcome": "NO_SHOW", "occurredAt": "2026-11-20T11:30:00+00:00", "reportedAt": "2026-11-20T19:00:02+00:00" }` — các thời điểm trong response trả theo UTC.

### 3.8. Mã lỗi (RFC 7807 `application/problem+json`, trường `code`)

| HTTP | `code` | Khi nào |
| :---: | :--- | :--- |
| 400 | `VALIDATION_ERROR`, `DOMAIN_INVARIANT_VIOLATION`, `MISSING_HEADER` | Payload/header/tham số sai (kể cả body không giải mã được; `X-Idempotency-Key` dài hơn 128 hoặc `X-Actor-Id` dài hơn 100 ký tự, `page`/`pageSize` ngoài miền, mức cọc của override không hợp lệ → `VALIDATION_ERROR`) |
| 415 | *(không có `code`)* | `Content-Type` không phải JSON. Do MVC trả mặc định, ngoài bộ mã của contract: body `application/problem+json` chuẩn không có trường `code`; `traceId` vẫn là correlation id |
| 401 | `UNAUTHORIZED` | Thiếu hoặc sai `X-Api-Key`; response kèm header `WWW-Authenticate: ApiKey header="X-Api-Key"`. Áp cho mọi đường dẫn `/api/*` (kể cả route không tồn tại) |
| 404 | `DECISION_NOT_FOUND` | Không có `decisionId` (với override/outcome: hoặc đó là bản `SHADOW`). `decisionId` không phải GUID thì route không khớp → 404 mặc định của framework (không có `code`) |
| 409 | `IDEMPOTENCY_KEY_REUSE` | Dùng lại key với payload khác (đánh giá, override) |
| 409 | `NOT_PENDING_APPROVAL`, `ALREADY_OVERRIDDEN`, `APPROVAL_EXPIRED`, `OUTCOME_ALREADY_RECORDED` | Vi phạm trạng thái của override/outcome (3.6, 3.7) |
| 503 | `PERSISTENCE_UNAVAILABLE` | Không ghi được audit (fail-closed); response kèm header `Retry-After` (giây) |
| 504 | `GATEWAY_TIMEOUT` | Vượt deadline toàn cục |
| 500 | `INTERNAL_ERROR` | Lỗi không mong đợi (ẩn chi tiết kỹ thuật) |
| 413 | `PAYLOAD_TOO_LARGE` | Body request lớn hơn giới hạn (mặc định 65536 byte, cấu hình `Tadss:Security:MaxRequestBodyBytes`). Kiểm tra trước xác thực nên request quá lớn không cần khóa vẫn nhận 413 |
| 429 | `RATE_LIMITED` | Client vượt giới hạn tần suất request; response kèm header `Retry-After` (giây) và `retryAfterSeconds` trong body |

Mọi response (kể cả lỗi) mang các header bảo mật `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`; response của `/api/*` thêm `Cache-Control: no-store`. Ngoài môi trường `Development`/`Testing`, response https còn có `Strict-Transport-Security`. Server không gửi header `Server`. Lỗi giải mã body (400 `VALIDATION_ERROR`) chỉ nêu tên trường (đường dẫn JSON do client gửi), không chứa thông điệp nội bộ của bộ giải mã JSON.

### 3.9. Idempotency

| Trạng thái key | Payload | Hành vi |
| :--- | :--- | :--- |
| Chưa tồn tại | Bất kỳ | Thực thi, lưu audit, 200 |
| Đã tồn tại | Giống (`request_hash` trùng) | Trả lại kết quả đã lưu, không chạy lại strategy, 200 |
| Đã tồn tại | Khác | 409 `IDEMPOTENCY_KEY_REUSE` |

Hai request đồng thời cùng key: ràng buộc unique của PostgreSQL bảo đảm chỉ một bản ghi; request thua đọc lại và trả kết quả của request thắng. Request sai hình thức luôn trả 400 `VALIDATION_ERROR` kể cả khi key đã từng được dùng (runtime dựng domain từ request trước khi tra idempotency, nên request sai không bao giờ được replay hay báo 409).

---

## 4. TRÁCH NHIỆM PHÍA SMASHCOURT-BE

### 4.1. Cấu hình (`TadssConfig`, đọc qua `IOptionsMonitor`, reload < 2 giây)

```json
{
  "TadssConfig": {
    "EnableTadssDecision": true,
    "BaseUrl": "http://localhost:5296",
    "TimeoutMs": 800,
    "TierMapping": { "Đồng": "STANDARD", "Bạc": "SILVER", "Vàng": "GOLD", "Bạch Kim": "PLATINUM", "Kim Cương": "DIAMOND" }
  }
}
```

`ApiKey` đặt trong user-secrets/biến môi trường, không ghi vào `appsettings.json`. Tên hạng trong `TierMapping` là ví dụ; giá trị thật lấy theo bảng `loyalty_tiers`.

**Cách SmashCourt-BE dùng contract:** BE **không** reference `Shared.DecisionContractSDK` (reference chéo repo từng làm vỡ CI độc lập và đã được gỡ ở commit `0b5d041` của SmashCourt-BE). BE tự định nghĩa DTO và `ITadssClient`/`TadssClient` trong `Integrations/Tadss/` theo mẫu `Integrations/AI/` hiện có; tính tương thích được bảo đảm bằng kiểm thử CT-04 (response mẫu kiểm tra theo JSON schema v2).

### 4.2. Ánh xạ booking → request v2

- **Chỉ gọi TADSS cho booking `ONLINE`.** Booking tại quầy (`WALK_IN`) được `CONFIRMED` ngay, mặc định trả sau, khách có mặt — không cần quyết định cọc. Giá trị `WALK_IN` được giữ trong enum để mở rộng sau.
- Mỗi `booking_court` → một phần tử `items` (`resourceId = courtId`, `resourceTypeId = courtTypeId`, `price` = phí sân trước giảm giá theo bảng giá `TimeSlot`). Mọi sân của một đơn dùng chung khung giờ (ràng buộc hiện có của BE).
- `totalPrice` = `finalTotal` của hóa đơn (phí sân − giảm theo hạng − khuyến mãi); mức cọc tính trên số này.
- `branchTimeZone`: bảng `branches` không có múi giờ, BE gửi hằng số cấu hình `Asia/Ho_Chi_Minh`.
- `tier`: ánh xạ `loyalty_tiers` theo **thứ hạng `MinPoints` tăng dần** → `STANDARD`, `SILVER`, `GOLD`, `PLATINUM`, `DIAMOND` (hạng vượt quá 5 gộp vào `DIAMOND`), không phụ thuộc tên hạng do Owner đặt. Khách chưa có hạng → `STANDARD`.
- Khách vãng lai → `isGuest = true`, `customerId = null`, `tier = STANDARD`, `memberSince = null`.
- `history` tính từ các booking đã kết thúc của khách; với khách vãng lai, ghép theo số điện thoại đã chuẩn hóa (chỉ gửi số đếm, không gửi số điện thoại).
- `demand` tính tại thời điểm tạo booking (Mục 3.3).
- `X-Idempotency-Key = bookingId`, nên đánh giá lại cùng booking (khi client retry) không sinh quyết định mới.

### 4.3. Xử lý kết quả

- Lưu `decisionId`, mức cọc và nguồn áp dụng (`TADSS`, `MANAGER`, `FULL_PAYMENT_FALLBACK`) trên booking/hóa đơn để đối soát. Quyết định fallback của runtime (`APPROVED` cọc 100%, `fallbackUsed = true`) là một quyết định `TADSS` hợp lệ: BE xử lý như mọi `APPROVED` (thanh toán `deposit.amount`), không cần nhánh riêng.
- Timeout `TimeoutMs`, lỗi 5xx, 401, 413, 429 hoặc lỗi mạng → áp dụng 100%, ghi log kèm correlation id (413 do body quá lớn, 429 do vượt tần suất: đều không retry ngay; 429 có thể ghi log `Retry-After`). Không retry POST quá một lần; lần retry dùng lại cùng idempotency key.
- Không bao giờ để lỗi của TADSS làm hỏng booking hoặc trả lỗi 500 cho khách.
- **Tranh chấp override ↔ outcome:** runtime kiểm tra "chưa có outcome" rồi mới ghi override, hai bước này không được khóa nguyên tử với việc ghi outcome (tranh chấp override ↔ override thì do `UNIQUE(decision_id)` bảo đảm). Cửa sổ tranh chấp chỉ có ở đúng thời điểm hết hạn duyệt, khi job báo outcome `APPROVAL_EXPIRED` chạy trùng lúc Manager bấm duyệt; khi đó có thể cả hai cùng thành công và `GET /api/v2/decisions/{decisionId}` hiện cả `override` lẫn `outcome`. BE coi trạng thái booking của mình là nguồn sự thật: không dùng một override 200 để hồi sinh booking đã hết hạn hoặc đã hủy, và đối soát theo chi tiết quyết định.

### 4.4. Thay đổi dữ liệu SmashCourt (chi tiết hóa ở Phase 6)

- Thêm giá trị `PENDING_APPROVAL` cho enum `booking_status`.
- Hóa đơn: số tiền cọc, tỷ lệ cọc, `tadss_decision_id`, nguồn áp dụng cọc; trạng thái `PARTIALLY_PAID` (đã có) dùng sau khi thanh toán cọc.
- Job: hết hạn duyệt (hủy + giải phóng sân), báo outcome.
- Script SQL cập nhật `TADSS-Runtime/docs/database/schema_smashcourt.sql` theo quy trình hiện có của SmashCourt-BE.

### 4.5. Ghi chú triển khai: kết nối tới TADSS qua ngrok (dev, demo)

TADSS chạy trong Docker có thể được expose qua ngrok (`docker-compose.yml`, file compose duy nhất: một lệnh `docker compose up -d --build` chạy `tadss-runtime`, `ngrok` và Grafana; cách chạy và biến môi trường: [`README.md`](../../README.md) mục "Chạy bằng Docker và expose qua ngrok"). Phía `SmashCourt-BE`:

- `TadssConfig.BaseUrl` (4.1) trỏ tới URL https của ngrok. URL của ngrok bản miễn phí đổi mỗi lần khởi động lại: phải cập nhật `BaseUrl` mỗi lần (muốn URL cố định thì thêm `--url=<domain>` của domain tĩnh miễn phí vào lệnh ngrok trong compose). Không qua ngrok: TADSS chạy `dotnet run` thì `http://localhost:5296` (BE trong container dùng `http://host.docker.internal:5296`); TADSS chạy compose thì `http://localhost:8081` từ BE `dotnet run` trên host, hoặc `http://tadss-runtime:8080` từ BE trong Docker (cả hai cùng mạng external `smashcourt-network`) và khi đó không cần ngrok.
- `TadssClient` gửi `X-Api-Key` (giá trị **khóa**, không phải mã băm) trên mọi request tới `/api/*`; khóa lấy từ user-secrets/biến môi trường của BE. Phía TADSS chỉ lưu SHA-256 của khóa và `ClientId` (`smashcourt-be`) trong `Tadss:Security:ApiKeys` của `appsettings.Development.json` (file này được copy vào image local như SmashCourt-BE, container chạy `Development`, nên `dotnet run` và Docker dùng chung một khóa; khi expose phải là khóa riêng thật, không dùng `tadss-local-dev-key`, vì Swagger công khai ở Development). Thiếu hoặc sai khóa → 401, BE áp dụng cọc 100% (4.3).
- `HttpClient` của `TadssClient` gửi thêm header `ngrok-skip-browser-warning: true` như `FastApiClient` đang làm, để ngrok không chèn trang cảnh báo vào response.
- Mỗi `ClientId` có một xô rate limit riêng, mặc định 100 request mỗi giây cho mỗi instance TADSS (429 `RATE_LIMITED` kèm `Retry-After`, xem 3.8 và 4.3).

---

## 5. CƠ SỞ DỮ LIỆU TADSS

TADSS dùng schema `tadss` trên cùng máy chủ PostgreSQL với SmashCourt. Cấu trúc bảng, trigger chặn sửa và index của v2: xem [`DECISION_PERSISTENCE_INVARIANTS.md`](../architecture/DECISION_PERSISTENCE_INVARIANTS.md).

**Rollback:** tắt `EnableTadssDecision` để SmashCourt quay về luồng 100%; schema `tadss` độc lập nên có thể gỡ mà không ảnh hưởng dữ liệu SmashCourt.

---

## 6. QUẢN LÝ PHIÊN BẢN

- Contract v2.0.0 là phiên bản duy nhất; đóng băng tại M5 với: `openapi-v2.yaml`, `schemas/v2/`, public API baseline v2 của SDK (`baseline_public_api.txt`).
- **Sau khi đóng băng** (mọi schema đều `additionalProperties: false`, nên thêm cả trường tùy chọn cũng làm client kiểm tra chặt từ chối):
  - **Được phép trong `2.x.y`:** cải tiến nội bộ SDK/runtime không đổi wire JSON (hiệu năng, sửa lỗi, XML doc); thêm `reasonCode` mới — consumer phải xử lý `reasonCode` như chuỗi và không lỗi khi gặp mã lạ.
  - **Bắt buộc lên phiên bản lớn mới:** thêm/xóa/đổi tên/đổi kiểu trường, đổi ràng buộc, đổi tập giá trị enum. Phiên bản mới có route riêng (`/api/v3`), thư mục schema riêng (`schemas/v3/`), baseline public API riêng; phiên bản cũ chạy song song trong thời gian chuyển đổi nếu đã có consumer.
- Contract v1.0.0 (Phase 1–4) không còn trong repo; tra lại qua tag git `phase4-baseline`.
