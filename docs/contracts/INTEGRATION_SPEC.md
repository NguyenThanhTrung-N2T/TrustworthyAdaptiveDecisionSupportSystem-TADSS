# ĐẶC TẢ TÍCH HỢP SMASHCOURT ↔ TADSS & DECISION CONTRACT v2

| | |
| :--- | :--- |
| **Trạng thái** | v2.0.0 — **bản nháp**, đóng băng tại Milestone 5 (08/11/2026) |
| **Thay thế** | Contract v1.0.0 ([`API_CONTRACT_V1.md`](API_CONTRACT_V1.md)) — v1 không có consumer thực tế nên được ngừng khi v2 đóng băng |
| **Consumer** | `SmashCourt-BE` (Phase 6); adapter Hotel Booking Demand (kiểm chứng độc lập domain) |
| **Sở hữu** | Tài liệu này là nguồn duy nhất về contract v2 và luồng tích hợp. Bất biến lưu trữ: [`DECISION_PERSISTENCE_INVARIANTS.md`](../architecture/DECISION_PERSISTENCE_INVARIANTS.md). Pipeline chịu lỗi: [`RESILIENCE_AND_FAULT_TAXONOMY.md`](../architecture/RESILIENCE_AND_FAULT_TAXONOMY.md) |

---

## 1. NGHIỆP VỤ ĐẶT CỌC (ĐÃ CHỐT)

| Tình huống | Bên quyết định | Kết quả |
| :--- | :--- | :--- |
| Strategy xác định được mức cọc | TADSS | `APPROVED` + `deposit.amount` → SmashCourt tạo thanh toán VNPay đúng số tiền cọc |
| Strategy không xác định được (giá trị lớn, rủi ro, chính sách yêu cầu duyệt) hoặc strategy lỗi (fallback) | Manager | `NEED_APPROVAL` → booking chờ duyệt; Manager **APPROVE kèm mức cọc** hoặc **REJECT** |
| Cần duyệt nhưng không còn đủ thời gian duyệt trước giờ chơi (quy tắc *approval window*, Mục 1.1) | TADSS | `APPROVED` với cọc 100%, `reasonCode = APPROVAL_WINDOW_UNAVAILABLE` |
| Strategy từ chối theo chính sách | TADSS | `REJECTED` → SmashCourt hủy booking |
| Feature flag tắt, hoặc TADSS không phản hồi / timeout / lỗi 5xx / lỗi xác thực | SmashCourt | Cọc **100%** (hành vi Đồ án 1), không chặn khách |
| Hết hạn duyệt mà Manager chưa xử lý | SmashCourt (job) | Hủy booking, giải phóng sân. **Không bao giờ tự động duyệt** |

Khách hàng **không** tự chọn mức cọc. Phần còn lại (tổng tiền − cọc) được thu khi checkout tại quầy.

### 1.1. Quy tắc approval window (runtime TADSS)

- `approvalExpiresAt = min(createdAt + Tadss:Approval:WindowMinutes, earliestStart − Tadss:Approval:MinLeadMinutes)`; mặc định `WindowMinutes = 30`, `MinLeadMinutes = 120`.
- Nếu strategy (hoặc fallback) trả `NEED_APPROVAL` nhưng `approvalExpiresAt ≤ createdAt` → runtime trả `APPROVED`, cọc 100%, `reasonCode = APPROVAL_WINDOW_UNAVAILABLE`. Kết quả gốc của strategy vẫn được lưu nguyên trong audit (`strategy_status`).

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

Endpoint phía SmashCourt-BE (`/api/decision-approvals/...`) là bản đề xuất; API contract BE ↔ FE được chốt ở task T5.22 (W10).

### 2.3. Báo kết quả thực tế (outcome)

Khi booking kết thúc, job SmashCourt gửi `POST /api/v2/decisions/{decisionId}/outcome` với một trong các giá trị: `COMPLETED`, `NO_SHOW`, `CANCELLED_BY_CUSTOMER`, `CANCELLED_BY_STAFF`, `PAYMENT_EXPIRED`, `APPROVAL_EXPIRED`, `REJECTED`. Đây là nhãn kết quả phục vụ phân tích và Khóa luận.

---

## 3. DECISION CONTRACT v2

### 3.1. Quy ước chung

- Base path `/api/v2`. JSON `camelCase`; enum dạng `SCREAMING_SNAKE_CASE`; JSON schema đặt `additionalProperties: false` (lưu tại `docs/contracts/schemas/v2/` khi hiện thực T5.1).
- Thời gian dùng ISO 8601 **kèm offset** (`2026-11-20T18:00:00+07:00`). Mọi quy tắc theo khung giờ được tính theo `branchTimeZone` (IANA, ví dụ `Asia/Ho_Chi_Minh`).
- Tiền là số thập phân, đơn vị theo `currency` (SmashCourt: `VND`).

### 3.2. Header

| Header | Bắt buộc | Mô tả |
| :--- | :---: | :--- |
| `X-Api-Key` | Mọi endpoint | Khóa service-to-service do TADSS cấp cho từng client |
| `X-Idempotency-Key` | `POST /decisions`, `POST /override`, `POST /outcome` | 1..128 ký tự; SmashCourt dùng `bookingId` (đánh giá) hoặc `{decisionId}:{action}` (override) |
| `X-Actor-Id` | `POST /override` | Định danh Manager do SmashCourt lấy từ JWT đã xác thực |
| `X-Correlation-ID` | Tùy chọn | Được echo lại trong response và gắn vào log, trace, audit |

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
    "history": { "completedCount": 12, "noShowCount": 1, "cancelledCount": 2, "lastCompletedAt": "2026-11-02T20:00:00+07:00" }
  },
  "totalPrice": 228000,
  "currency": "VND",
  "channel": "ONLINE",
  "requestedAt": "2026-11-18T09:15:00+07:00",
  "metadata": { "promotionCode": "WINTER" }
}
```

| Trường | Ràng buộc |
| :--- | :--- |
| `items` | 1..20 phần tử; mỗi `endTime > startTime`, thời lượng 30 phút – 12 giờ; `price ≥ 0` |
| `customer.customerId` | Bắt buộc khi `isGuest = false`; `null` khi `isGuest = true` |
| `customer.tier` | Một trong `STANDARD`, `SILVER`, `GOLD`, `PLATINUM`, `DIAMOND` (client tự ánh xạ hạng nội bộ) |
| `customer.history` | Tùy chọn; số đếm ≥ 0 |
| `totalPrice` | Tổng sau giảm giá, ≥ 0 |

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
| `recommendations[].type` | `REQUIRE_APPROVAL`, `ALTERNATIVE_SLOT`, `ALTERNATIVE_RESOURCE`, `PRIORITY_ESCALATION` |

### 3.4. `GET /api/v2/decisions/{decisionId}`

Trả chi tiết: snapshot request đã chuẩn hóa, kết quả máy (gồm `strategyStatus`), telemetry thực thi, danh sách override, outcome (nếu có), dấu thời gian.

### 3.5. `GET /api/v2/decisions/pending-approvals?branchId={id}&page=1&pageSize=20`

- Chỉ gồm **bản quyết định mới nhất** của mỗi `reservationId` thỏa: `status = NEED_APPROVAL`, chưa override, chưa có outcome, `approvalExpiresAt > now`.
- Mỗi item: `decisionId`, `reservationId`, `branchId`, `totalPrice`, `currency`, `earliestStart`, `customer.isGuest`, `customer.tier`, `reasonCode`, `fallbackUsed`, `approvalExpiresAt`, `createdAt`.

### 3.6. `POST /api/v2/decisions/{decisionId}/override`

```json
{ "action": "APPROVE", "deposit": { "rate": 0.3 }, "reason": "Khách quen, chi nhánh bảo lãnh" }
```

- `action`: `APPROVE` (bắt buộc `deposit.rate` hoặc `deposit.amount`) \| `REJECT`. `reason` 5..500 ký tự.
- Mỗi quyết định có **tối đa một** override (ràng buộc `UNIQUE(decision_id)` trên bảng `decision_overrides`). Gửi lại cùng `X-Idempotency-Key` và cùng payload → trả lại kết quả cũ (200).
- **Response 200:** `decisionId`, `action`, `deposit` (đã quy đổi `rate` và `amount`), `actorId`, `reason`, `overriddenAt`.

### 3.7. `POST /api/v2/decisions/{decisionId}/outcome`

```json
{ "outcome": "NO_SHOW", "occurredAt": "2026-11-20T18:30:00+07:00" }
```

Mỗi quyết định có tối đa một outcome. Gửi trùng với cùng payload → 200; khác payload → 409.

### 3.8. Mã lỗi (RFC 7807 `application/problem+json`, trường `code`)

| HTTP | `code` | Khi nào |
| :---: | :--- | :--- |
| 400 | `VALIDATION_ERROR`, `DOMAIN_INVARIANT_VIOLATION`, `MISSING_HEADER` | Payload/header sai |
| 401 | `UNAUTHORIZED` | Thiếu hoặc sai `X-Api-Key` |
| 404 | `DECISION_NOT_FOUND` | Không có `decisionId` |
| 409 | `IDEMPOTENCY_KEY_REUSE` | Dùng lại key với payload khác |
| 409 | `NOT_PENDING_APPROVAL`, `ALREADY_OVERRIDDEN`, `APPROVAL_EXPIRED`, `OUTCOME_ALREADY_RECORDED` | Vi phạm trạng thái |
| 503 | `PERSISTENCE_UNAVAILABLE` | Không ghi được audit (fail-closed) |
| 504 | `GATEWAY_TIMEOUT` | Vượt deadline toàn cục |
| 500 | `INTERNAL_ERROR` | Lỗi không mong đợi (ẩn chi tiết kỹ thuật) |

### 3.9. Idempotency

| Trạng thái key | Payload | Hành vi |
| :--- | :--- | :--- |
| Chưa tồn tại | Bất kỳ | Thực thi, lưu audit, 200 |
| Đã tồn tại | Giống (`request_hash` trùng) | Trả lại kết quả đã lưu, không chạy lại strategy, 200 |
| Đã tồn tại | Khác | 409 `IDEMPOTENCY_KEY_REUSE` |

Hai request đồng thời cùng key: ràng buộc unique của PostgreSQL bảo đảm chỉ một bản ghi; request thua đọc lại và trả kết quả của request thắng.

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

- Mỗi `booking_court` → một phần tử `items` (`resourceId = courtId`, `resourceTypeId = courtTypeId`, giá từng sân).
- Khách vãng lai → `isGuest = true`, `customerId = null`, `tier = STANDARD`.
- `history` tính từ các booking đã kết thúc của khách.
- `X-Idempotency-Key = bookingId`, nên đánh giá lại cùng booking (khi client retry) không sinh quyết định mới.

### 4.3. Xử lý kết quả

- Lưu `decisionId`, mức cọc và nguồn áp dụng (`TADSS`, `MANAGER`, `FULL_PAYMENT_FALLBACK`) trên booking/hóa đơn để đối soát.
- Timeout `TimeoutMs`, lỗi 5xx, 401 hoặc lỗi mạng → áp dụng 100%, ghi log kèm correlation id. Không retry POST quá một lần; lần retry dùng lại cùng idempotency key.
- Không bao giờ để lỗi của TADSS làm hỏng booking hoặc trả lỗi 500 cho khách.

### 4.4. Thay đổi dữ liệu SmashCourt (chi tiết hóa ở Phase 6)

- Thêm giá trị `PENDING_APPROVAL` cho enum `booking_status`.
- Hóa đơn: số tiền cọc, tỷ lệ cọc, `tadss_decision_id`, nguồn áp dụng cọc; trạng thái `PARTIALLY_PAID` (đã có) dùng sau khi thanh toán cọc.
- Job: hết hạn duyệt (hủy + giải phóng sân), báo outcome.
- Script SQL cập nhật `TADSS-Runtime/docs/database/schema_smashcourt.sql` theo quy trình hiện có của SmashCourt-BE.

---

## 5. CƠ SỞ DỮ LIỆU TADSS

TADSS dùng schema `tadss` trên cùng máy chủ PostgreSQL với SmashCourt. Cấu trúc bảng, trigger chặn sửa và index của v2: xem [`DECISION_PERSISTENCE_INVARIANTS.md`](../architecture/DECISION_PERSISTENCE_INVARIANTS.md).

**Rollback:** tắt `EnableTadssDecision` để SmashCourt quay về luồng 100%; schema `tadss` độc lập nên có thể gỡ mà không ảnh hưởng dữ liệu SmashCourt.

---

## 6. QUẢN LÝ PHIÊN BẢN

- Contract v1.0.0 (Milestone 2) giữ nguyên trong `API_CONTRACT_V1.md`, `openapi-v1.yaml`, `schemas/` làm hồ sơ lịch sử.
- Contract v2.0.0 đóng băng tại M5 với: `openapi-v2.yaml`, `schemas/v2/`, public API baseline v2 của SDK. Sau khi đóng băng, mọi thay đổi wire phải qua quy trình tại Mục 6 của [`CONTRACT_V1_FREEZE.md`](CONTRACT_V1_FREEZE.md).
- Endpoint `/api/v1` được gỡ khi v2 đóng băng vì chưa có consumer nào tích hợp v1.
