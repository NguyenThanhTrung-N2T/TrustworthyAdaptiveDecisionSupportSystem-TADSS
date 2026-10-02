# BẤT BIẾN LƯU TRỮ QUYẾT ĐỊNH & SCHEMA `tadss` — v2.0

> **Vai trò của tài liệu:** nguồn duy nhất về cấu trúc bảng, ràng buộc, trigger, index và bất biến của schema `tadss`. Contract HTTP: [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md).
>
> **Database:** PostgreSQL 16, dùng chung máy chủ với SmashCourt, tách ở schema `tadss`. Lịch sử migration nằm ở `tadss.__EFMigrationsHistory`.

---

## A. v1 (Phase 1–4) — ĐÃ GỠ Ở GÓI 5.2c

> Mục này chỉ là hồ sơ lịch sử. Bảng v1 và mọi mã dùng nó đã bị gỡ: migration `20261002101457_DecisionPersistenceV2` **xóa** bảng `tadss.decision_executions` v1 (kể cả dữ liệu) rồi tạo ba bảng v2 ở Mục B; `Down` dựng lại bảng v1 rỗng. Migration chưa được áp lên Neon (việc người dùng quyết định riêng).

Bảng v1 là một bảng `tadss.decision_executions` (20 cột) gồm hai phần:

- **Snapshot máy:** `decision_id`, `reservation_id`, `strategy_name`, `strategy_version`, `execution_type`, `execution_time_ms`, `fallback_used`, `failure_reason`, `cache_hit`, `need_approval`, `request_hash`, `response_payload` (jsonb), `created_at`.
- **Metadata quản trị (được UPDATE khi override):** `is_overridden`, `overridden_by`, `override_reason`, `version` (concurrency token), `updated_at`.

Ràng buộc: `UNIQUE(decision_id)`; unique partial index `uq_decision_idempotency_key` (`WHERE idempotency_key IS NOT NULL`); partial index hàng chờ `WHERE need_approval AND NOT is_overridden`.

**Hạn chế được ghi nhận khi re-baseline:**

1. Override `UPDATE` trực tiếp lên bản ghi máy; tính bất biến chỉ là quy ước trong mã, DB không chặn.
2. Không lưu trạng thái đích (APPROVED/REJECTED) và mức cọc của override.
3. Không lưu input của quyết định (chỉ lưu hash), nên không replay được và không dùng làm dữ liệu cho Khóa luận.
4. Không có `branch_id` để lọc hàng chờ; mỗi lần đánh giá lại cùng reservation sinh thêm một mục chờ.

Thiết kế v2 ở Mục B khắc phục các hạn chế trên. Dữ liệu v1 trong schema `tadss` chỉ là dữ liệu kiểm thử nên migration v2 tạo lại bảng thay vì chuyển đổi.

---

## B. THIẾT KẾ v2

> **Trạng thái (gói 5.2c):** B.1–B.4 **đã hiện thực** bằng migration `20261002101457_DecisionPersistenceV2` (EF entity/configuration trong `Infrastructure/Data/`, trigger và hàm viết bằng SQL trong migration) và có test trên PostgreSQL thật (IT-05, IT-15). Hiện chỉ `decision_executions` được ghi bởi mã (`EfDecisionAuditStore`: tìm theo idempotency key, chèn, đọc chi tiết); `decision_overrides`, `decision_outcomes` và truy vấn hàng chờ B.5 chưa có mã ghi/đọc ở runtime — thuộc gói 5.3.
>
> **Ghi chú hiện thực:**
> - Tên ràng buộc do migration đặt (DDL dưới đây khai báo CHECK không tên): `ck_de_{role,status,deposit_rate,deposit_amount,execution_type,execution_time_ms,deposit,approval}`, `ck_do_{action,deposit_rate,deposit_amount,reason_length,deposit}`, `ck_dout_outcome`; unique `uq_decision_executions_decision_id`, `uq_decision_overrides_decision_id`, `uq_decision_outcomes_decision_id`; FK `fk_decision_overrides_decision_id`, `fk_decision_outcomes_decision_id` (NO ACTION).
> - `numeric(5,4)`: mức cọc chỉ được lưu tối đa 4 chữ số thập phân. Thay vì để PostgreSQL làm tròn âm thầm, cấu hình strategy (`Validate()`) từ chối tỷ lệ có hơn 4 chữ số và `DecisionResponseComposer` ném lỗi (không ghi audit) nếu kết quả `APPROVED` có tỷ lệ vượt quá 4 chữ số. `0.50000` vẫn hợp lệ (so theo giá trị).
> - `timestamptz` có độ phân giải microsecond: `DecisionService` lấy thời điểm từ `TimeProvider` một lần rồi cắt về bội số 10 tick (µs) để giá trị trả về/băm/ghi khớp giá trị đọc lại. Npgsql chỉ nhận `DateTimeOffset` offset 0 cho `timestamptz`; mapper không chuẩn hóa offset nên offset khác 0 là lỗi lập trình.
> - `jsonb` chuẩn hóa văn bản (khoảng trắng, thứ tự khóa) khi lưu: `request_payload`/`response_payload` đọc lại tương đương về JSON nhưng không bảo đảm giống từng byte với chuỗi đã ghi (số giữ nguyên chữ số thập phân).
> - `TRUNCATE` thường lên `decision_executions` bị PostgreSQL từ chối ở bước kiểm tra FK (SQLSTATE `0A000`) trước khi trigger chạy; `TRUNCATE ... CASCADE` và `TRUNCATE` lên hai bảng còn lại đi qua trigger `*_no_truncate` (P0001).

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
| **INV-9** | Thời gian `created_at` do ứng dụng gán theo UTC (offset 0, độ phân giải µs) | `DecisionService` gán từ `TimeProvider`; không còn interceptor (đã gỡ ở 5.2c) và không có `DEFAULT now()` ở DB | UT-01 (mapper), IT-01, IT-05 |
| **INV-10** | Request trả về thành công khi và chỉ khi bản ghi audit đã được lưu (fail-closed) | Ghi đồng bộ trước khi trả response | FI-08, SC3 |

**Trạng thái sau gói 5.2c:** INV-1, INV-2, INV-3, INV-4, INV-7, INV-9, INV-10 có test khóa đang xanh (IT-01…IT-05, UT-06, UT-11, FI-08). INV-5, INV-6: mới có phần DB (`UNIQUE`, CHECK, FK — test IT-05); phần kiểm tra ở ứng dụng và IT-06…IT-10 thuộc gói 5.3. INV-8: ràng buộc DB (`uq_decision_idempotency_key` chỉ áp cho `PRIMARY`, test IT-05) và `Find` lọc `role = 'PRIMARY'` đã có; chưa có đường ghi `SHADOW` và IT-12 thuộc gói 5.4.

### C.1. Định danh

- `id`: khóa chính vật lý.
- `decision_id`: định danh logic của một lần đánh giá, trả về cho client.
- `reservation_id`: định danh đơn bên client (SmashCourt `bookingId`).

### C.2. Quy tắc tính `request_hash`

1. Lấy các trường nghiệp vụ của request v2 (không gồm `requestedAt`, `metadata` và `demand` — ảnh chụp nhu cầu thay đổi giữa các lần retry cùng idempotency key; danh sách trường được khai báo trong mã). `demand` vẫn được lưu trong `request_payload`.
2. Thời gian quy về UTC từ giá trị có offset, định dạng `yyyy-MM-ddTHH:mm:ssZ`; chuỗi phân loại viết hoa; số tiền định dạng bất biến theo culture.
3. Dựng object canonical theo thứ tự trường cố định, serialize JSON không khoảng trắng.
4. SHA-256 trên UTF-8 → 64 ký tự hex thường.
