# BẤT BIẾN LƯU TRỮ QUYẾT ĐỊNH & SCHEMA `tadss` — v2.0

> **Vai trò của tài liệu:** nguồn duy nhất về cấu trúc bảng, ràng buộc, trigger, index và bất biến của schema `tadss`. Contract HTTP: [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md).
>
> **Database:** PostgreSQL 16, dùng chung máy chủ với SmashCourt, tách ở schema `tadss`. Lịch sử migration nằm ở `tadss.__EFMigrationsHistory`.

---

## A. HIỆN TRẠNG v1 (Phase 1–4, đang chạy)

Một bảng `tadss.decision_executions` (20 cột) gồm hai phần:

- **Snapshot máy:** `decision_id`, `reservation_id`, `strategy_name`, `strategy_version`, `execution_type`, `execution_time_ms`, `fallback_used`, `failure_reason`, `cache_hit`, `need_approval`, `request_hash`, `response_payload` (jsonb), `created_at`.
- **Metadata quản trị (được UPDATE khi override):** `is_overridden`, `overridden_by`, `override_reason`, `version` (concurrency token), `updated_at`.

Ràng buộc: `UNIQUE(decision_id)`; unique partial index `uq_decision_idempotency_key` (`WHERE idempotency_key IS NOT NULL`); partial index hàng chờ `WHERE need_approval AND NOT is_overridden`.

**Hạn chế được ghi nhận khi re-baseline:**

1. Override `UPDATE` trực tiếp lên bản ghi máy; tính bất biến chỉ là quy ước trong mã, DB không chặn.
2. Không lưu trạng thái đích (APPROVED/REJECTED) và mức cọc của override.
3. Không lưu input của quyết định (chỉ lưu hash), nên không replay được và không dùng làm dữ liệu cho Khóa luận.
4. Không có `branch_id` để lọc hàng chờ; mỗi lần đánh giá lại cùng reservation sinh thêm một mục chờ.

Thiết kế v2 dưới đây khắc phục các hạn chế trên (hiện thực ở task T5.3 — Phase 5). Dữ liệu v1 trong schema `tadss` chỉ là dữ liệu kiểm thử nên migration v2 được phép tạo lại bảng; việc tạo/áp dụng migration chỉ thực hiện khi nhóm xác nhận.

---

## B. THIẾT KẾ v2

### B.1. Bảng `tadss.decision_executions` — bất biến

```sql
CREATE TABLE tadss.decision_executions (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    decision_id         uuid         NOT NULL UNIQUE,
    reservation_id      uuid         NOT NULL,
    branch_id           uuid         NOT NULL,
    role                varchar(10)  NOT NULL CHECK (role IN ('PRIMARY', 'SHADOW')),
    strategy_name       varchar(100) NOT NULL,
    strategy_version    varchar(20)  NOT NULL,
    strategy_status     varchar(20)  NOT NULL,   -- kết quả gốc của strategy/fallback
    status              varchar(20)  NOT NULL CHECK (status IN ('APPROVED', 'NEED_APPROVAL', 'REJECTED')),
    deposit_rate        numeric(5,4) NULL CHECK (deposit_rate BETWEEN 0 AND 1),
    deposit_amount      numeric(14,2) NULL CHECK (deposit_amount >= 0),
    reason_code         varchar(64)  NOT NULL,
    execution_type      varchar(20)  NOT NULL CHECK (execution_type IN ('AUTOMATED', 'FALLBACK', 'CACHE_HIT')),
    execution_time_ms   integer      NOT NULL CHECK (execution_time_ms >= 0),
    fallback_used       boolean      NOT NULL,
    failure_reason      text         NULL,
    cache_hit           boolean      NOT NULL,
    approval_expires_at timestamptz  NULL,
    request_hash        char(64)     NOT NULL,
    request_payload     jsonb        NOT NULL,   -- request v2 đã chuẩn hóa
    response_payload    jsonb        NOT NULL,
    idempotency_key     varchar(128) NULL,
    correlation_id      varchar(128) NULL,
    created_at          timestamptz  NOT NULL,
    CONSTRAINT ck_de_deposit  CHECK ((status = 'APPROVED') = (deposit_rate IS NOT NULL AND deposit_amount IS NOT NULL)),
    CONSTRAINT ck_de_approval CHECK ((status = 'NEED_APPROVAL') = (approval_expires_at IS NOT NULL))
);

CREATE UNIQUE INDEX uq_decision_idempotency_key ON tadss.decision_executions (idempotency_key)
    WHERE idempotency_key IS NOT NULL AND role = 'PRIMARY';
CREATE INDEX idx_de_reservation_created ON tadss.decision_executions (reservation_id, created_at DESC);
CREATE INDEX idx_de_pending ON tadss.decision_executions (branch_id, created_at)
    WHERE status = 'NEED_APPROVAL' AND role = 'PRIMARY';
CREATE INDEX idx_de_request_hash ON tadss.decision_executions (request_hash);
CREATE INDEX idx_de_created_at ON tadss.decision_executions (created_at);
```

### B.2. Bảng `tadss.decision_overrides` — append-only

```sql
CREATE TABLE tadss.decision_overrides (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    decision_id     uuid          NOT NULL UNIQUE REFERENCES tadss.decision_executions (decision_id),
    action          varchar(10)   NOT NULL CHECK (action IN ('APPROVE', 'REJECT')),
    deposit_rate    numeric(5,4)  NULL CHECK (deposit_rate BETWEEN 0 AND 1),
    deposit_amount  numeric(14,2) NULL CHECK (deposit_amount >= 0),
    actor_id        varchar(100)  NOT NULL,
    reason          text          NOT NULL CHECK (char_length(reason) BETWEEN 5 AND 500),
    idempotency_key varchar(128)  NOT NULL,
    created_at      timestamptz   NOT NULL,
    CONSTRAINT ck_do_deposit CHECK ((action = 'APPROVE') = (deposit_rate IS NOT NULL AND deposit_amount IS NOT NULL))
);
```

### B.3. Bảng `tadss.decision_outcomes` — append-only

```sql
CREATE TABLE tadss.decision_outcomes (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    decision_id uuid        NOT NULL UNIQUE REFERENCES tadss.decision_executions (decision_id),
    outcome     varchar(40) NOT NULL CHECK (outcome IN ('COMPLETED', 'NO_SHOW', 'CANCELLED_BY_CUSTOMER',
                    'CANCELLED_BY_STAFF', 'PAYMENT_EXPIRED', 'APPROVAL_EXPIRED', 'REJECTED')),
    occurred_at timestamptz NOT NULL,
    reported_at timestamptz NOT NULL
);
```

### B.4. Trigger chặn sửa/xóa

```sql
CREATE FUNCTION tadss.reject_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'Bảng %.% là append-only', TG_TABLE_SCHEMA, TG_TABLE_NAME USING ERRCODE = 'P0001';
END $$;

-- Áp dụng cho cả 3 bảng
CREATE TRIGGER trg_de_append_only BEFORE UPDATE OR DELETE ON tadss.decision_executions
    FOR EACH ROW EXECUTE FUNCTION tadss.reject_mutation();
CREATE TRIGGER trg_de_no_truncate BEFORE TRUNCATE ON tadss.decision_executions
    FOR EACH STATEMENT EXECUTE FUNCTION tadss.reject_mutation();
-- (tương tự cho decision_overrides và decision_outcomes)
```

### B.5. Truy vấn hàng chờ duyệt

```sql
WITH latest AS (
    SELECT DISTINCT ON (reservation_id) *
    FROM tadss.decision_executions
    WHERE role = 'PRIMARY' AND branch_id = @branchId
    ORDER BY reservation_id, created_at DESC
)
SELECT l.*
FROM latest l
LEFT JOIN tadss.decision_overrides o ON o.decision_id = l.decision_id
LEFT JOIN tadss.decision_outcomes  r ON r.decision_id = l.decision_id
WHERE l.status = 'NEED_APPROVAL'
  AND o.id IS NULL AND r.id IS NULL
  AND l.approval_expires_at > now()
ORDER BY l.approval_expires_at
LIMIT @pageSize OFFSET @offset;
```

"Thay thế bản cũ" được thực hiện bằng cách chỉ lấy bản mới nhất của mỗi reservation, không cần sửa bản ghi cũ.

---

## C. BẤT BIẾN v2

| Mã | Bất biến | Cơ chế bảo đảm | Kiểm thử |
| :--- | :--- | :--- | :--- |
| **INV-1** | Mỗi lần đánh giá có một `decision_id` duy nhất; quan hệ reservation : decision là 1 : N | `UNIQUE(decision_id)` | IT-01 |
| **INV-2** | Cùng `idempotency_key` (PRIMARY) chỉ có một bản ghi; payload khác bị từ chối | Unique partial index + so sánh `request_hash` | IT-02, IT-03, IT-04 |
| **INV-3** | `request_hash` tất định, không phụ thuộc múi giờ/culture của máy chủ | Chuẩn hóa canonical theo thứ tự trường cố định, thời gian quy về UTC từ giá trị có offset | UT-06 |
| **INV-4** | Ba bảng audit không bị sửa/xóa sau khi ghi | Trigger B.4 | IT-05 |
| **INV-5** | Mỗi quyết định có tối đa một override; chỉ override được quyết định `PRIMARY`, `NEED_APPROVAL`, chưa hết hạn; APPROVE phải có mức cọc | `UNIQUE(decision_id)` + CHECK + kiểm tra ở ứng dụng | IT-06, IT-07, IT-08 |
| **INV-6** | Mỗi quyết định có tối đa một outcome | `UNIQUE(decision_id)` | IT-10 |
| **INV-7** | `APPROVED` ⇔ có mức cọc; `NEED_APPROVAL` ⇔ có hạn duyệt | CHECK `ck_de_deposit`, `ck_de_approval` | IT-01, UT-10 |
| **INV-8** | Bản ghi `SHADOW` không ảnh hưởng response và không vào hàng chờ | Lọc `role = 'PRIMARY'` | IT-12 |
| **INV-9** | Thời gian `created_at` do ứng dụng gán theo UTC | `AuditTimestampInterceptor` | IT-01 |
| **INV-10** | Request trả về thành công khi và chỉ khi bản ghi audit đã được lưu (fail-closed) | Ghi đồng bộ trước khi trả response | FI-08, SC3 |

### C.1. Định danh

- `id`: khóa chính vật lý.
- `decision_id`: định danh logic của một lần đánh giá, trả về cho client.
- `reservation_id`: định danh đơn bên client (SmashCourt `bookingId`).

### C.2. Quy tắc tính `request_hash`

1. Lấy các trường nghiệp vụ của request v2 (không gồm `requestedAt`, `metadata` và `demand` — ảnh chụp nhu cầu thay đổi giữa các lần retry cùng idempotency key; danh sách trường được khai báo trong mã). `demand` vẫn được lưu trong `request_payload`.
2. Thời gian quy về UTC từ giá trị có offset, định dạng `yyyy-MM-ddTHH:mm:ssZ`; chuỗi phân loại viết hoa; số tiền định dạng bất biến theo culture.
3. Dựng object canonical theo thứ tự trường cố định, serialize JSON không khoảng trắng.
4. SHA-256 trên UTF-8 → 64 ký tự hex thường.
