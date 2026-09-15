# TADSS Decision Runtime — API Contract v1

- **Document Version**: `1.0.0`
- **Target Runtime**: `TADSS.Runtime.Api` (.NET 8)
- **Primary Consumer**: `SmashCourt-BE` (Core Booking & Management Backend)
- **Status**: `LOCKED / SOURCE OF TRUTH`
- **Last Updated**: `2026-09-13`

---

## 1. Purpose & Scope

Tài liệu này là bản đặc tả kỹ thuật chính thức (**Single Source of Truth**) cho giao diện tích hợp giữa **SmashCourt-BE** (Hệ thống quản trị và đặt sân) và **TADSS** (Trustworthy Adaptive Decision Support System - Decision Runtime Engine).

### 1.1 Mục tiêu thiết kế
1. **Contract-First & Provider-Independent**: Đảm bảo `SmashCourt-BE` chỉ phụ thuộc vào API Contract v1 mà không cần biết logic nội bộ hoặc công nghệ triển khai bên trong TADSS.
2. **Ranh giới trách nhiệm rõ ràng (Separation of Concerns)**:
   - `SmashCourt-BE`: Sở hữu dữ liệu nghiệp vụ, quản lý vòng đời Reservation, thực thi giao dịch thanh toán và cập nhật trạng thái đặt sân.
   - `TADSS`: Đóng vai trò là **Decision Engine** độc lập, đánh giá yêu cầu đặt sân (`POST /decisions`), hỗ trợ truy xuất kiểm toán (`GET /decisions/{id}`), quản lý hàng đợi phê duyệt của nhân viên (`GET /decisions/pending-approvals`) và tiếp nhận can thiệp thủ công (`POST /decisions/{id}/override`).
3. **Auditability & Reproducibility**: Mọi quyết định đều được định danh bằng `decision_id`, lưu vết kiểm toán bất biến (Immutable Audit Ledger) phục vụ nghiên cứu, đánh giá và đối soát.

---

## 2. Architecture & API Surface

### 2.1 Bản đồ Bề mặt API (API Surface Overview)

TADSS API v1 cung cấp 4 endpoints đại diện cho 3 nhóm năng lực:

```
                                      TADSS Decision Runtime
                                                │
                 ┌──────────────────────────────┼──────────────────────────────┐
                 │                              │                              │
          1. EXECUTION                      2. AUDIT                    3. GOVERNANCE
                 │                              │                              │
                 ▼                              ▼                              ▼
      POST /api/v1/decisions        GET /api/v1/decisions/{id}   GET /api/v1/decisions/pending-approvals
                                                                               │
                                                                               ▼
                                                                 POST /api/v1/decisions/{id}/override
```

| Nhóm năng lực | Endpoint & Method | Mô tả tóm tắt | SLA / Timeout |
| :--- | :--- | :--- | :---: |
| **1. Execution** | `POST /api/v1/decisions` | Đánh giá tự động yêu cầu đặt sân | Target `< 100ms`, Deadline `500ms` |
| **2. Audit** | `GET /api/v1/decisions/{decisionId}` | Tra cứu chi tiết một quyết định đã lưu | `< 50ms` |
| **3. Governance (Queue)** | `GET /api/v1/decisions/pending-approvals` | Lấy danh sách ca đang chờ Quản lý duyệt | `< 50ms` (Partial Index) |
| **4. Governance (Override)** | `POST /api/v1/decisions/{decisionId}/override` | Quản lý ghi đè phê duyệt/từ chối | `< 100ms` |

### 2.2 Ba tầng mô hình độc lập (3-Tier Model Separation)
Để tránh rò rỉ cấu trúc cơ sở dữ liệu vào API hoặc ràng buộc tầng Domain với định dạng JSON ngoài wire:

$$\text{API Payload (DTO)} \quad \neq \quad \text{Domain Model (Invariants)} \quad \neq \quad \text{Database Model (Audit Ledger)}$$

- **API Contract DTOs (`Shared.DecisionContractSDK.Contracts`)**: Cấu trúc dữ liệu thuần túy phục vụ truyền tải qua HTTP JSON.
- **Domain Models (`Shared.DecisionContractSDK.Domain`)**: Thực thể nghiệp vụ chứa business invariants, logic tính toán thời lượng và tính hợp lệ.
- **Persistence Entity (`TADSS.Runtime.Api.Infrastructure.Data.Entities`)**: Bảng `tadss.decision_executions` lưu trữ nhật ký audit, snapshot JSONB và telemetry phục vụ truy vết.

---

## 3. API Versioning & Identity Semantics

### 3.1 Cơ chế Versioning
- **URL Path Versioning**: Toàn bộ routes có tiền tố `/api/v1/`.
- Không gắn cờ `contractVersion` vào payload ở v1. Thay đổi tương thích (additive) được giữ trên v1; thay đổi phá vỡ (breaking change) sẽ chuyển sang `/api/v2/`.

### 3.2 Khái niệm Định danh (Identity Semantics)
- **`decision_id` (UUID)**: Định danh một **phiên ra quyết định logic** (Logical Decision Lifecycle).
- **`id` (UUID PK)**: Khóa chính vật lý của một dòng ghi nhật ký trong bảng `tadss.decision_executions`.
- **`reservation_id` (UUID)**: Mã đặt sân từ hệ thống nguồn `SmashCourt-BE`. Mối quan hệ $1 \text{ Reservation} : N \text{ Decisions}$ (mỗi lần khách đổi giờ, đánh giá lại, hoặc đặt lại sẽ sinh một `decision_id` mới).

---

## 4. HTTP Headers

### 4.1 Request Headers

| Header | Kiểu | Bắt buộc | Mô tả & Ràng buộc |
| :--- | :---: | :---: | :--- |
| `Content-Type` | String | **Bắt buộc** | Phải là `application/json; charset=utf-8`. |
| `X-Idempotency-Key` | String | **Bắt buộc** *(đối với `POST /decisions`)* | Chuỗi định danh duy nhất cho một phiên gọi nghiệp vụ từ phía client (UUID hoặc string `1..128` ký tự). Phục vụ chống trùng lặp và an toàn khi retry. |
| `X-Correlation-ID` | String | *Tùy chọn* | Chuỗi truy vết phân tán (Distributed Tracing ID). Nếu `SmashCourt-BE` gửi lên, TADSS sẽ dùng giá trị này. Nếu để trống, TADSS tự động sinh một UUID mới. |

### 4.2 Response Headers

| Header | Kiểu | Luôn có | Mô tả |
| :--- | :---: | :---: | :--- |
| `Content-Type` | String | Có | `application/json; charset=utf-8` hoặc `application/problem+json` (khi có lỗi). |
| `X-Correlation-ID` | String | Có | Luôn được echo lại trong response header để hỗ trợ liên kết log giữa 2 hệ thống. |
| `X-Decision-ID` | String | Có *(khi gọi POST /decisions)* | UUID đại diện cho bản ghi quyết định vừa được xử lý trong TADSS. |

---

## 5. Endpoint 1: Core Evaluation (`POST /api/v1/decisions`)

### 5.1 Request Payload (`DecisionRequest`)

```json
{
  "reservationId": "018e3a2b-8a71-7000-8c24-112233445566",
  "courtId": "018e3a2b-8a71-7000-8c24-aabbccddeeff",
  "courtTypeId": "018e3a2b-8a71-7000-8c24-001122334455",
  "branchId": "018e3a2b-8a71-7000-8c24-998877665544",
  "customerId": "018e3a2b-8a71-7000-8c24-123456789abc",
  "customerTier": "Gold",
  "startTime": "2026-09-15T18:00:00Z",
  "endTime": "2026-09-15T20:00:00Z",
  "bookingType": "Single",
  "totalPrice": 300000.00,
  "depositAmount": 150000.00,
  "paymentStatus": "PartiallyPaid",
  "priority": "Normal",
  "requestedAt": "2026-09-12T15:00:00Z",
  "metadata": {
    "source": "MobileApp",
    "voucherCode": "SUMMER2026"
  }
}
```

### 5.2 Response Payload (`DecisionResult`)

```json
{
  "decisionId": "018e3a2b-9f88-7111-a1b2-001122334455",
  "reservationId": "018e3a2b-8a71-7000-8c24-112233445566",
  "status": "APPROVED",
  "strategyName": "DefaultDecisionStrategy",
  "strategyVersion": "1.0.0",
  "executionTimeMs": 14,
  "fallbackUsed": false,
  "primaryReason": "AUTO_APPROVED_TIER_ELIGIBLE",
  "reasons": [
    "Khách hàng hạng Gold đáp ứng chính sách thanh toán cọc 50%",
    "Khung giờ khả dụng, không có xung đột lịch bảo trì"
  ],
  "recommendations": [],
  "evaluatedAt": "2026-09-12T15:00:00.014Z"
}
```

---

## 6. Endpoint 2: Audit Inspection (`GET /api/v1/decisions/{decisionId}`)

### 6.1 Mô tả & Quy tắc trả về
- **Route**: `GET /api/v1/decisions/{decisionId}`
- **Nguyên tắc**: Trả về DTO `DecisionDetailResponse`. Theo thiết kế schema hiện tại của `tadss.decision_executions`, hệ thống **chỉ lưu `request_hash` và `response_payload`**, không lưu raw request payload để tối ưu lưu trữ. Do đó, response thể hiện chính xác dữ liệu audit và telemetry hiện có.

### 6.2 Response Payload (`DecisionDetailResponse`)

```json
{
  "decisionId": "018e3a2b-9f88-7111-a1b2-001122334455",
  "reservationId": "018e3a2b-8a71-7000-8c24-112233445566",
  "requestHash": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
  "decision": {
    "status": "NEED_APPROVAL",
    "primaryReason": "DEPOSIT_BELOW_THRESHOLD",
    "reasons": [
      "Khách hàng hạng Standard chưa thanh toán đủ mức cọc tối thiểu 30%"
    ],
    "recommendations": [
      {
        "type": "REQUIRE_DEPOSIT",
        "description": "Yêu cầu khách hàng thanh toán bổ sung 60,000 VND tiền cọc để được duyệt tự động.",
        "suggestedPrice": 300000.00,
        "confidenceScore": 1.0
      }
    ]
  },
  "execution": {
    "strategyName": "DefaultDecisionStrategy",
    "strategyVersion": "1.0.0",
    "executionType": "AUTOMATED",
    "executionTimeMs": 14,
    "fallbackUsed": false,
    "failureReason": null,
    "cacheHit": false
  },
  "governance": {
    "needApproval": true,
    "isOverridden": false,
    "overriddenBy": null,
    "overrideReason": null,
    "overriddenAt": null
  },
  "timestamps": {
    "createdAt": "2026-09-12T15:00:00.014Z",
    "updatedAt": "2026-09-12T15:00:00.014Z"
  }
}
```

---

## 7. Endpoint 3: Pending Approvals Queue (`GET /api/v1/decisions/pending-approvals`)

### 7.1 Mô tả & Quy tắc truy vấn
- **Route**: `GET /api/v1/decisions/pending-approvals?page=1&pageSize=20`
- **Tối ưu cơ sở dữ liệu**: Sử dụng trực tiếp **Partial Index** `idx_decision_executions_pending_approval` (`WHERE need_approval = true AND is_overridden = false`).
- **Phạm vi Phase 1**: Không filter theo `branchId` ở v1 vì schema bảng `decision_executions` không lưu `branch_id`.

### 7.2 Response Payload (`PendingApprovalsResponse`)

```json
{
  "page": 1,
  "pageSize": 20,
  "totalItems": 3,
  "totalPages": 1,
  "items": [
    {
      "decisionId": "018e3a2b-9f88-7111-a1b2-001122334455",
      "reservationId": "018e3a2b-8a71-7000-8c24-112233445566",
      "status": "NEED_APPROVAL",
      "primaryReason": "DEPOSIT_BELOW_THRESHOLD",
      "strategyName": "DefaultDecisionStrategy",
      "strategyVersion": "1.0.0",
      "executionTimeMs": 14,
      "fallbackUsed": false,
      "createdAt": "2026-09-12T15:00:00.014Z"
    }
  ]
}
```

---

## 8. Endpoint 4: Manager Override (`POST /api/v1/decisions/{decisionId}/override`)

### 8.1 Mô tả & Quy tắc Bất biến (Invariant 3)
- **Route**: `POST /api/v1/decisions/{decisionId}/override`
- **Nguyên tắc**:
  1. Cột `response_payload` gốc của máy **tuyệt đối không bị sửa đổi** nhằm bảo tồn bằng chứng cho nghiên cứu / so sánh (Ablation study).
  2. Chỉ cập nhật các cờ quản trị: `is_overridden = true`, `overridden_by`, `override_reason`, và `updated_at = UtcNow`.
  3. **Disclaimer về Actor Identity**: Trường `overriddenBy` truyền trong body là giải pháp tường minh cho Phase 1 (khi chưa có Auth Context). Trong môi trường production, danh tính người thực hiện bắt buộc phải được trích xuất từ Authenticated Principal / JWT Token.

### 8.2 Request Payload (`OverrideDecisionRequest`)

```json
{
  "decision": {
    "status": "APPROVED"
  },
  "overrideReason": "Khách quen lâu năm, chi nhánh bảo lãnh miễn cọc",
  "overriddenBy": "manager-usr-001"
}
```

### 8.3 Response Payload (`OverrideDecisionResponse`)

```json
{
  "decisionId": "018e3a2b-9f88-7111-a1b2-001122334455",
  "reservationId": "018e3a2b-8a71-7000-8c24-112233445566",
  "originalStatus": "NEED_APPROVAL",
  "overriddenStatus": "APPROVED",
  "isOverridden": true,
  "overriddenBy": "manager-usr-001",
  "overrideReason": "Khách quen lâu năm, chi nhánh bảo lãnh miễn cọc",
  "overriddenAt": "2026-09-12T15:30:00.000Z"
}
```

---

## 9. Decision Recommendation Structure

```json
{
  "type": "AlternativeSlot",
  "description": "Khung giờ 18:00 - 20:00 đã kín. Gợi ý chuyển sang khung giờ 20:00 - 22:00 cùng ngày trên Sân 2.",
  "suggestedCourtId": "018e3a2b-8a71-7000-8c24-aabbccddeeff",
  "suggestedStartTime": "2026-09-15T20:00:00Z",
  "suggestedEndTime": "2026-09-15T22:00:00Z",
  "suggestedPrice": 280000.00,
  "confidenceScore": 0.95
}
```

---

## 10. Idempotency Semantics

| Trạng thái cơ sở dữ liệu | Payload mới | Hành vi xử lý của TADSS | HTTP Status Code |
| :--- | :--- | :--- | :---: |
| `X-Idempotency-Key` chưa từng tồn tại | Bất kỳ | Thực thi đánh giá bình thường, lưu bản ghi audit mới vào database | `200 OK` |
| `X-Idempotency-Key` đã tồn tại | **Giống hệt** (`request_hash` trùng khớp) | **Replay lại kết quả cũ** đã lưu trong database mà không chạy lại strategy engine | `200 OK` |
| `X-Idempotency-Key` đã tồn tại | **Khác nhau** (`request_hash` khác biệt) | **Từ chối xử lý ngay lập tức** do tái sử dụng key sai mục đích | `409 Conflict` |

Error Payload cho 409 Conflict:
```json
{
  "type": "https://tadss.smashcourt.vn/errors/idempotency-key-reuse",
  "title": "Idempotency Key Reuse Conflict",
  "status": 409,
  "detail": "The provided X-Idempotency-Key 'req-booking-20260912-001' has already been used with a different request payload.",
  "instance": "/api/v1/decisions",
  "code": "IDEMPOTENCY_KEY_REUSE"
}
```

---

## 11. Resilience & Timeout Semantics

Hệ thống thiết lập chuỗi chịu lỗi 2 tầng (Two-Tier Resilience Pipeline):

1. **Target Latency**: `< 100 ms` cho luồng đánh giá thông thường.
2. **Inner Strategy Timeout (150 ms per attempt)**:
   - Giới hạn thời gian thực thi của mỗi lượt gọi Strategy.
   - Khi Strategy bị timeout (>150ms) hoặc ném ngoại lệ: Polly kích hoạt Retry (tối đa 2 lần, tổng 3 attempts với base delay 30ms) $\rightarrow$ Circuit Breaker $\rightarrow$ Chuyển sang **Fallback Strategy** (`DefaultDecisionStrategy` cọc 100%).
   - Kết quả trả về: **HTTP 200 OK** kèm metadata `fallbackUsed: true`, `status: "NEED_APPROVAL"`.
3. **Outer Global Deadline (500 ms Hard Ceiling)**:
   - Giới hạn thời gian tối đa cho toàn bộ chu trình xử lý request.
   - Nếu tổng thời gian thực thi (bao gồm mọi retries, DB access, logic) vượt quá 500ms: Hệ thống ngắt toàn bộ pipeline (`TimeoutRejectedException`) và trả về **HTTP 504 Gateway Timeout** (không qua Fallback).

### 504 Gateway Timeout Response Example:
```http
HTTP/1.1 504 Gateway Timeout
Content-Type: application/problem+json; charset=utf-8
X-Correlation-ID: cor-9988-7766-5544

{
  "type": "https://tadss.smashcourt.vn/errors/gateway-timeout",
  "title": "Gateway Timeout",
  "status": 504,
  "detail": "The decision evaluation exceeded the global request deadline (500ms). Please retry with exponential backoff.",
  "instance": "/api/v1/decisions",
  "code": "GATEWAY_TIMEOUT",
  "retryAfterSeconds": 5
}
```

---

## 12. Error Contract (RFC 7807)

| HTTP Status | Ý nghĩa trong TADSS |
| :---: | :--- |
| `200 OK` | Xử lý thành công (kể cả kết quả business là `Approved`, `Rejected`, `NeedApproval`, `FallbackUsed`). |
| `400 Bad Request` | Sai định dạng JSON, thiếu header bắt buộc, hoặc vi phạm Domain Invariants (`DOMAIN_INVARIANT_VIOLATION`). |
| `404 Not Found` | Không tìm thấy `decisionId` tương ứng khi gọi GET hoặc Override. |
| `409 Conflict` | Xung đột `X-Idempotency-Key` (`IDEMPOTENCY_KEY_REUSE`) hoặc can thiệp override trên đơn đã bị override. |
| `504 Gateway Timeout` | Vượt quá thời hạn xử lý toàn cục. |
| `500 Internal Server Error` | Lỗi nội bộ không mong muốn (thông tin kỹ thuật được mask bảo mật). |

---

## 13. Decision Contract Matrix Toàn diện (4 Endpoints)

| Endpoint | HTTP Method & Route | Request Type | Response Type | Tầng Domain liên quan | Tầng DB Persistence | HTTP Codes |
| :--- | :--- | :--- | :--- | :--- | :--- | :---: |
| **1. Evaluate** | `POST /api/v1/decisions` | `DecisionRequest` | `DecisionResult` | `Reservation` Aggregate, `IReservationDecisionStrategy` | `INSERT` vào `decision_executions` | `200`, `400`, `409`, `504`, `500` |
| **2. Get Detail** | `GET /api/v1/decisions/{decisionId}` | *None (Route: UUID)* | `DecisionDetailResponse` | `DecisionId` ValueObject | `SELECT` từ `decision_executions` by `decision_id` | `200`, `400`, `404`, `500` |
| **3. Pending Queue** | `GET /api/v1/decisions/pending-approvals` | *Query: `page`, `pageSize`* | `PendingApprovalsResponse` | `PendingApprovalItem` | `SELECT` dùng Partial Index `pending_approval` | `200`, `400`, `500` |
| **4. Override** | `POST /api/v1/decisions/{decisionId}/override` | `OverrideDecisionRequest` | `OverrideDecisionResponse` | `HumanOverride` Policy | `UPDATE is_overridden, overridden_by...` | `200`, `400`, `404`, `409`, `500` |

---

## 14. Machine-Readable Contract

For automated client SDK generation, validation tooling, and automated contract testing, see:
- **OpenAPI 3.0 Specification**: [`docs/contracts/openapi-v1.yaml`](file:///d:/SINH%20VIEN/HK6/DA1/SmashCourt/TADSS-Runtime/docs/contracts/openapi-v1.yaml)
- **JSON Schemas (Draft 7 / 2020-12)**: [`docs/contracts/schemas/`](file:///d:/SINH%20VIEN/HK6/DA1/SmashCourt/TADSS-Runtime/docs/contracts/schemas/)
  - `decision-request.schema.json`
  - `decision-result.schema.json`
  - `decision-recommendation.schema.json`
  - `decision-detail-response.schema.json`
  - `pending-approvals-response.schema.json`
  - `override-decision-request.schema.json`
  - `override-decision-response.schema.json`
