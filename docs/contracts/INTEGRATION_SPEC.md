# TÀI LIỆU ĐẶC TẢ TÍCH HỢP HỆ THỐNG (INTEGRATION SPECIFICATION)

Tài liệu này đặc tả chi tiết giao thức kết nối, kế hoạch di cư cơ sở dữ liệu, chiến lược phiên bản và kế hoạch kiểm thử chất lượng tích hợp giữa ứng dụng đặt sân **SmashCourt** và **Decision Runtime Platform (TADSS)**.

---

## 1. SMASHCOURT ➔ TADSS CONTRACT SPECIFICATION

### 1.1. Endpoint Đánh giá Quyết định (`POST /api/v1/decisions`)

*   **Phương thức:** `POST`
*   **Đường dẫn:** `http://<tadss-host>:<port>/api/v1/decisions`
*   **Headers:**
    *   `Content-Type: application/json`
    *   `X-Contract-Version: 1.0.0`
    *   `X-Correlation-Id: <GUID>` (Được sinh bởi SmashCourt để trace chéo log)

#### Request Payload Schema (Decision Contract v1)
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "properties": {
    "reservationId": { "type": "string", "format": "uuid" },
    "courtId": { "type": "string", "format": "uuid" },
    "courtTypeId": { "type": ["string", "null"], "format": "uuid" },
    "branchId": { "type": "string", "format": "uuid" },
    "customerId": { "type": "string", "format": "uuid" },
    "customerTier": { "type": "string", "default": "STANDARD", "description": "Mapped from client LoyaltyTier table" },
    "startTime": { "type": "string", "format": "date-time" },
    "endTime": { "type": "string", "format": "date-time" },
    "bookingType": { "type": "string", "default": "SINGLE" },
    "totalPrice": { "type": "number", "minimum": 0 },
    "depositAmount": { "type": "number", "minimum": 0 },
    "paymentStatus": { "type": "string", "enum": ["UNPAID", "PARTIALLY_PAID", "PAID", "REFUNDED"] },
    "priority": { "type": "string", "default": "NORMAL" },
    "requestedAt": { "type": "string", "format": "date-time" },
    "metadata": { "type": "object", "additionalProperties": { "type": "string" } }
  },
  "required": [
    "reservationId", "courtId", "branchId", "customerId",
    "startTime", "endTime", "bookingType", "totalPrice",
    "depositAmount", "paymentStatus", "requestedAt"
  ]
}
```

#### Response Payload Schema (Decision Contract v1)
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "properties": {
    "decisionId": { "type": "string", "format": "uuid" },
    "reservationId": { "type": "string", "format": "uuid" },
    "status": { "type": "string", "enum": ["APPROVED", "REJECTED", "NEED_APPROVAL"] },
    "strategyName": { "type": "string" },
    "strategyVersion": { "type": "string" },
    "executionTimeMs": { "type": "integer" },
    "fallbackUsed": { "type": "boolean" },
    "primaryReason": { "type": "string" },
    "reasons": { "type": "array", "items": { "type": "string" } },
    "recommendations": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "type": { "type": "string", "enum": ["ALTERNATIVE_SLOT", "ALTERNATIVE_COURT", "PRICE_ADJUSTMENT", "REQUIRE_DEPOSIT", "REQUIRE_APPROVAL", "PRIORITY_ESCALATION"] },
          "description": { "type": "string" },
          "suggestedCourtId": { "type": ["string", "null"], "format": "uuid" },
          "suggestedStartTime": { "type": ["string", "null"], "format": "date-time" },
          "suggestedEndTime": { "type": ["string", "null"], "format": "date-time" },
          "suggestedPrice": { "type": ["number", "null"] },
          "confidenceScore": { "type": ["number", "null"] }
        },
        "required": ["type", "description"]
      }
    },
    "evaluatedAt": { "type": "string", "format": "date-time" }
  },
  "required": [
    "decisionId", "reservationId", "status", "strategyName",
    "strategyVersion", "executionTimeMs", "fallbackUsed",
    "primaryReason", "reasons", "recommendations", "evaluatedAt"
  ]
}
```

### 1.2. Cơ chế Bật/Tắt Nóng & Xử lý Lỗi (Feature Flags & Fallback)

*   **Cấu hình Feature Flag (`EnableTadssDecision`):**
    Được cấu hình trong file `appsettings.json` và reload nóng qua `IOptionsMonitor` trong SmashCourt Backend:
    ```json
    {
      "TadssConfig": {
        "EnableTadssDecision": true,
        "TadssEndpoint": "http://localhost:5296/api/v1/decisions",
        "DefaultDepositRate": 1.0
      }
    }
    ```
*   **Luồng hoạt động khi đặt sân:**
    1. Nếu `EnableTadssDecision = false`: SmashCourt tự động áp dụng chính sách cọc mặc định (100% giá đặt sân) trực tiếp mà không gọi TADSS.
    2. Nếu `EnableTadssDecision = true`: SmashCourt gửi request đến TADSS.
       * Nếu TADSS phản hồi thành công: Áp dụng đề xuất `deposit_rate` lấy từ `Recommendation.Parameters.deposit_rate`.
       * Nếu trả về `NeedApproval` (ví dụ cọc 50% giờ cao điểm): Tạm dừng thanh toán VNPay, chờ Manager duyệt/ghi đè trên Dashboard.
       * **Quy tắc NO Auto-Approve:** Đơn ở trạng thái `NeedApproval` nếu quá hạn duyệt (timeout 15 phút, có thể cấu hình) sẽ tự động chuyển sang `Expired` hoặc `Cancelled`, tuyệt đối không tự động duyệt để bảo vệ an toàn dòng tiền.
       * Nếu TADSS gặp sự cố sập kết nối hoặc trả về lỗi 5xx: HttpClient catch lỗi và tự động kích hoạt Fallback cọc 100% (`DefaultDepositRate`).

---

## 2. KẾ HOẠCH DI CƯ CƠ SỞ DỮ LIỆU (DATABASE MIGRATION PLAN)

Cơ sở dữ liệu của TADSS Platform được tích hợp trực tiếp vào PostgreSQL 16 hiện tại của SmashCourt (trên **Neon Cloud `ap-southeast-1`** hoặc Local), cô lập hoàn toàn ở tầng **Logical Schema** có tên là `tadss` để tránh xung đột cấu trúc dữ liệu chính.

```
PostgreSQL Database (SmashCourt DB)
├── public schema (branches, courts, bookings, payments, loyalty...)
└── tadss schema (decision_executions)
```

### 2.1. Cấu trúc Bảng Duy nhất `tadss.decision_executions` (Single-Table Audit Pattern)

Bảng `tadss.decision_executions` lưu toàn bộ nhật ký thực thi, thông số telemetry và các trường phê duyệt của Manager:

```sql
CREATE SCHEMA IF NOT EXISTS tadss;

CREATE TABLE IF NOT EXISTS tadss.decision_executions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    decision_id UUID NOT NULL,
    reservation_id UUID NOT NULL,
    strategy_name VARCHAR(100) NOT NULL,
    strategy_version VARCHAR(20) NOT NULL,
    execution_type VARCHAR(50) NOT NULL, -- 'AUTOMATED', 'CACHE_HIT', 'FALLBACK'
    execution_time_ms INTEGER NOT NULL,
    fallback_used BOOLEAN NOT NULL DEFAULT FALSE,
    failure_reason TEXT NULL,
    cache_hit BOOLEAN NOT NULL DEFAULT FALSE,
    need_approval BOOLEAN NOT NULL DEFAULT FALSE,
    request_hash VARCHAR(64) NOT NULL,
    response_payload JSONB NOT NULL,
    
    -- Các trường Override & Concurrency
    is_overridden BOOLEAN NOT NULL DEFAULT FALSE,
    overridden_by VARCHAR(100) NULL,
    override_reason TEXT NULL,
    idempotency_key VARCHAR(128) NULL,
    version INTEGER NOT NULL DEFAULT 1,
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_decision_id UNIQUE (decision_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_decision_idempotency_key ON tadss.decision_executions (idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_decision_executions_request_hash ON tadss.decision_executions (request_hash);
CREATE INDEX IF NOT EXISTS idx_decision_executions_reservation_id ON tadss.decision_executions (reservation_id);
CREATE INDEX IF NOT EXISTS idx_decision_executions_created_at ON tadss.decision_executions (created_at);
CREATE INDEX IF NOT EXISTS idx_decision_executions_pending_approval ON tadss.decision_executions (created_at DESC) WHERE (need_approval = true AND is_overridden = false);
```

### 2.2. Kịch bản Khôi phục khi gặp Sự cố (Rollback Strategy)

Nếu bản Migration gây lỗi hoặc cần gỡ bỏ hoàn toàn:
1.  Chuyển `EnableTadssDecision = false` tại cấu hình của SmashCourt để cô lập hoàn toàn luồng đặt chỗ.
2.  Chạy script hạ cấp:
     ```sql
     DROP TABLE IF EXISTS tadss.decision_executions CASCADE;
     DROP SCHEMA IF EXISTS tadss CASCADE;
     ```

---

## 3. CƠ CHẾ REQUEST-HASH CACHING & CHUẨN HÓA DỮ LIỆU

Nhằm tối ưu hóa hiệu năng phản hồi và tránh tính toán lại không cần thiết, TADSS áp dụng cơ chế Request-Hash Cache (Redis 7.x / 8.x trên **Redis Cloud Managed `ap-southeast-1`** hoặc Local ở tầng L2, kết hợp `IMemoryCache` L1 fallback, SHA-256 exact-match).

### 3.1. Quy trình Chuẩn hóa Payload (Canonicalization - 4 bước)

> **⚠️ NGUYÊN TẮC VÀNG (ARCHITECTURAL INVARIANT):** Canonicalization chỉ chuẩn hóa hình thức biểu diễn (representation), **TUYỆT ĐỐI GIỮ NGUYÊN GIÁ TRỊ NGHIỆP VỤ** (`startTime`, `endTime`, `totalPrice`, `depositAmount`). Các giá trị thời gian có ý nghĩa nghiệp vụ phải được bảo toàn chính xác; chỉ phần nhiễu hệ thống dưới mức giây (sub-second ticks) mới được cắt bỏ về giây chuẩn UTC.

1.  **Sắp xếp Keys (Key Sorting):** Sắp xếp toàn bộ keys trong đối tượng JSON/Metadata theo thứ tự alphabet (RFC 8785 JCS).
2.  **Chuẩn hóa Chuỗi Định danh & Phân loại:** Chuẩn hóa chuỗi UUID (`reservationId`, `customerId`, `courtId`, `branchId`) và in hoa (`ToUpperInvariant`) các trường phân loại (`customerTier`, `bookingType`, `paymentStatus`, `priority`).
3.  **Chuẩn hóa Định dạng Timestamp & Loại bỏ Nhiễu Thời gian Gửi:** Chuyển đổi `startTime` và `endTime` sang định dạng ISO 8601 UTC mức giây (`yyyy-MM-ddTHH:mm:ssZ`, cắt bỏ sub-second ticks nhưng giữ nguyên giờ/phút nghiệp vụ), đồng thời **loại trừ `requestedAt`** (thời điểm gửi HTTP request) khỏi đối tượng băm để các lần gọi đánh giá cùng một lịch đặt sân luôn khớp Cache Key.
4.  **Định dạng Số Thập phân Bất biến & Xóa Khoảng trắng:** Định dạng `totalPrice`, `depositAmount` theo `F2` (`CultureInfo.InvariantCulture`), loại bỏ khoảng trắng thừa và chuẩn hóa trường `null`.

### 3.2. Thuật toán tạo RequestHash & Cache Key (Tách biệt Request Identity và Strategy Identity)

```csharp
// 1. Request Identity: Chuẩn hóa representation của DecisionRequestDto và băm SHA-256
var normalizedPayload = CanonicalizeJson(decisionRequest);
using var sha256 = System.Security.Cryptography.SHA256.Create();
var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalizedPayload));
var requestHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

// 2. Cache Key: Kết hợp RequestHash với Strategy Identity (strategyName + strategyVersion)
var cacheKey = $"decision:hash:{requestHash}:{strategyName}:v{strategyVersion}";
```

### 3.3. Chính sách TTL & Invalidation

*   **TTL Configuration (Operational / Experimental Parameter):** Cấu hình mặc định cho baseline vận hành và thực nghiệm là `TtlHours = 24` (24 giờ, cấu hình qua `TadssCacheOptions` trong `appsettings.json`, có thể điều chỉnh linh hoạt theo từng kịch bản thực nghiệm mà không phải là bất biến kiến trúc cứng).
*   **Strategy Version Isolation:** Khi nâng cấp chiến lược (ví dụ `Default:v1.0.0` $\rightarrow$ `Default:v2.0.0`), hậu tố `:v{strategyVersion}` trên cache key tự động thay đổi, không bao giờ lấy lại kết quả cũ của phiên bản trước.
*   **Memory Fallback (F5):** Nếu Redis L2 gặp sự cố kết nối hoặc vượt quá `RedisTimeoutMs` (150ms), `ResilientDecisionCacheService` tự động giáng cấp sang `IMemoryCache` L1 in-process mà không gây gián đoạn API.

---

## 4. CHI TIẾT CHIẾN LƯỢC PHIÊN BẢN (VERSIONING STRATEGY)

*   **Hợp đồng API:** Header `X-Contract-Version: 1.0.0`. Khi có breaking change, nâng major version lên `2.0.0`.
*   **Phân giải Chiến lược qua DI (DI-based Strategy Resolution):** Đăng ký các Strategy vào `IServiceCollection` và phân giải qua `StrategyFactory` ($O(1)$ Lookup) theo `strategyName` và `strategyVersion`.
