# BẤT BIẾN LƯU TRỮ QUYẾT ĐỊNH & SCHEMA `tadss` — v2.0

> **Vai trò của tài liệu:** nguồn duy nhất về cấu trúc bảng, ràng buộc, trigger, index và bất biến của schema `tadss`. Contract HTTP: [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md).
>
> **Database:** PostgreSQL 16, dùng chung máy chủ với SmashCourt, tách ở schema `tadss`. Lịch sử migration nằm ở `tadss.__EFMigrationsHistory`.

---

## A. LỊCH SỬ MIGRATION

| Migration | Nội dung |
| :--- | :--- |
| `20260912105336_InitialCreate`, `20260915152502_UpdateUniqueIndexes`, `20260930190021_AddCreatedAtIndex` | Schema của Phase 1–4 (một bảng `decision_executions`). Giữ lại chỉ để lịch sử migration khớp với database đã áp chúng (Neon); không có mã nào dùng schema này |
| `20261002101457_DecisionPersistenceV2` | **Xóa** bảng cũ (kể cả dữ liệu — chỉ là dữ liệu kiểm thử) rồi tạo ba bảng, hàm và trigger ở Mục B; `Down` dựng lại bảng cũ rỗng. Chưa áp lên Neon (người dùng quyết định riêng) |

Thiết kế ở Mục B khắc phục các hạn chế của schema cũ: override sửa trực tiếp bản ghi máy (DB không chặn), không lưu trạng thái đích và mức cọc của override, không lưu input quyết định, không có `branch_id` cho hàng chờ.

### A.1. Vị trí mã và lệnh EF

Mã persistence nằm trong project `TADSS.Infrastructure` (EF Core chỉ được dùng trong thư mục `Persistence/`); port của nó nằm ở `TADSS.Application/Abstractions/Persistence`.

| Thứ | Vị trí |
| :--- | :--- |
| `TadssDbContext`, `EfDecisionAuditStore`, `DecisionAuditMapper`, `PersistenceFailureDetector` (internal) | `src/TADSS.Infrastructure/Persistence/` |
| Entity: `DecisionExecutionEntity`, `DecisionOverrideEntity`, `DecisionOutcomeEntity` (hậu tố `Entity` để không trùng enum `DecisionOutcome` của SDK) | `src/TADSS.Infrastructure/Persistence/Entities/` |
| Cấu hình ánh xạ: `DecisionExecutionConfiguration`, `DecisionOverrideConfiguration`, `DecisionOutcomeConfiguration` | `src/TADSS.Infrastructure/Persistence/Configurations/` |
| Migration và `TadssDbContextModelSnapshot` | `src/TADSS.Infrastructure/Persistence/Migrations/` |

Lệnh EF luôn chỉ rõ cả project chứa migration và project khởi động (composition root), chạy với môi trường `Testing` để không đọc cấu hình cục bộ. Kèm biến `ConnectionStrings__DefaultConnection` giả (không phải secret, không mở kết nối): design-time khởi động host, mà validator từ chối chuỗi giữ chỗ trong `appsettings.json` và `Testing` không nạp `appsettings.Development.json`:

```bash
# Liệt kê migration, không kết nối database
ASPNETCORE_ENVIRONMENT=Testing ConnectionStrings__DefaultConnection="Host=localhost;Database=x" dotnet ef migrations list --no-connect --project src/TADSS.Infrastructure --startup-project src/TADSS.Runtime.Api

# Kiểm tra model và snapshot còn khớp nhau (chỉ đọc)
ASPNETCORE_ENVIRONMENT=Testing ConnectionStrings__DefaultConnection="Host=localhost;Database=x" dotnet ef migrations has-pending-model-changes --project src/TADSS.Infrastructure --startup-project src/TADSS.Runtime.Api
```

Chỉ tạo hoặc áp migration (`migrations add`, `database update`) khi có yêu cầu; `Tadss:Database:ApplyMigrationsOnStartup` được `StartupTasks` (Runtime.Api) xử lý.

---

## B. THIẾT KẾ v2

> **Trạng thái (sau gói 5.3):** B.1–B.5 **đã hiện thực** và có test trên PostgreSQL thật. B.1–B.4 bằng migration `20261002101457_DecisionPersistenceV2` (EF entity/configuration trong `src/TADSS.Infrastructure/Persistence/` — xem Mục A.1, trigger và hàm viết bằng SQL trong migration; IT-05, IT-15). `decision_executions` được ghi bởi `EfDecisionAuditStore` (hiện thực `IDecisionEvaluationStore` — tìm theo idempotency key, chèn — và `IDecisionQueryStore` — đọc chi tiết và hàng chờ B.5, qua `PendingApprovalsQuery`); `decision_overrides` và `decision_outcomes` được ghi bởi `EfDecisionGovernanceStore` (hiện thực `IDecisionGovernanceStore`), phục vụ `POST /override` và `POST /outcome` (IT-06…IT-10). Cách phân loại lỗi và thứ tự kiểm tra của override: INTEGRATION_SPEC §3.6.
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
-- Trang: @nowUtc do ứng dụng truyền từ TimeProvider (không dùng now())
SELECT c.*
FROM tadss.decision_executions c
WHERE c.role = 'PRIMARY' AND c.branch_id = @branchId AND c.status = 'NEED_APPROVAL'
  AND c.approval_expires_at > @nowUtc
  AND NOT EXISTS (SELECT 1 FROM tadss.decision_executions n
                  WHERE n.reservation_id = c.reservation_id AND n.branch_id = c.branch_id AND n.role = 'PRIMARY'
                    AND (n.created_at, n.decision_id) > (c.created_at, c.decision_id))
  AND NOT EXISTS (SELECT 1 FROM tadss.decision_overrides o WHERE o.decision_id = c.decision_id)
  AND NOT EXISTS (SELECT 1 FROM tadss.decision_outcomes r WHERE r.decision_id = c.decision_id)
ORDER BY c.approval_expires_at, c.decision_id
LIMIT @pageSize OFFSET @offset;

-- Tổng: cùng các điều kiện WHERE ở trên
SELECT count(*) FROM tadss.decision_executions c WHERE /* như trên */;
```

"Thay thế bản cũ" được thực hiện bằng cách loại những bản đã có bản `PRIMARY` mới hơn cùng `reservation_id` và chi nhánh (so theo `(created_at, decision_id)` nên hai bản cùng `created_at` vẫn có đúng một bản thắng), không cần sửa bản ghi cũ. Dạng này (ứng viên `PRIMARY` + `NEED_APPROVAL` của chi nhánh, rồi `NOT EXISTS`) dùng được partial index `idx_de_pending` (chỉ chứa bản `NEED_APPROVAL`) và `idx_de_reservation_created` cho bước loại bản cũ; dạng `DISTINCT ON` trên mọi bản `PRIMARY` của chi nhánh không dùng được `idx_de_pending`. `decision_id` phá thế hòa nên phân trang tất định. Trang và tổng là hai câu SQL riêng, không chung transaction.

---

## C. BẤT BIẾN v2

| Mã | Bất biến | Cơ chế bảo đảm | Kiểm thử |
| :--- | :--- | :--- | :--- |
| **INV-1** | Mỗi lần đánh giá có một `decision_id` duy nhất; quan hệ reservation : decision là 1 : N | `UNIQUE(decision_id)` | IT-01 |
| **INV-2** | Cùng `idempotency_key` (PRIMARY) chỉ có một bản ghi; payload khác bị từ chối | Unique partial index + so sánh `request_hash` | IT-02, IT-03, IT-04 |
| **INV-3** | `request_hash` tất định, không phụ thuộc múi giờ/culture của máy chủ | Chuẩn hóa canonical theo thứ tự trường cố định, thời gian quy về UTC từ giá trị có offset | UT-06 |
| **INV-4** | Ba bảng audit không bị sửa/xóa sau khi ghi | Trigger B.4 | IT-05 |
| **INV-5** | Mỗi quyết định có tối đa một override; chỉ override được quyết định `PRIMARY`, `NEED_APPROVAL`, chưa hết hạn; APPROVE phải có mức cọc | `UNIQUE(decision_id)` + CHECK + kiểm tra ở ứng dụng. Giới hạn đã biết: kiểm tra "chưa có outcome" rồi mới ghi override không khóa nguyên tử với việc ghi outcome (khóa hàng `FOR UPDATE` sẽ đưa quy tắc nghiệp vụ vào adapter); cửa sổ tranh chấp chỉ có ở đúng thời điểm hết hạn duyệt, xem INTEGRATION_SPEC §4.3 | IT-06, IT-07, IT-08 |
| **INV-6** | Mỗi quyết định có tối đa một outcome | `UNIQUE(decision_id)` | IT-10 |
| **INV-7** | `APPROVED` ⇔ có mức cọc; `NEED_APPROVAL` ⇔ có hạn duyệt | CHECK `ck_de_deposit`, `ck_de_approval` | IT-01, UT-10 |
| **INV-8** | Bản ghi `SHADOW` không ảnh hưởng response và không vào hàng chờ | Lọc `role = 'PRIMARY'`; `ShadowExecutor` ghi `SHADOW` sau khi bản `PRIMARY` đã lưu, qua hàng đợi có giới hạn, không khóa idempotency | IT-12 |
| **INV-9** | Thời gian `created_at` do ứng dụng gán theo UTC (offset 0, độ phân giải µs) | `DecisionService` gán từ `TimeProvider`; không có interceptor ghi timestamp và không có `DEFAULT now()` ở DB | UT-01 (mapper), IT-01, IT-05 |
| **INV-10** | Request trả về thành công khi và chỉ khi bản ghi audit đã được lưu (fail-closed) | Ghi đồng bộ trước khi trả response | FI-08, SC3 |

**Trạng thái sau gói 5.4:** INV-1…INV-10 có test khóa đang xanh (IT-01…IT-10, UT-06, UT-11, UT-15, FI-08): phần DB của INV-5/INV-6 (`UNIQUE`, CHECK, FK — IT-05) cùng phần kiểm tra ở ứng dụng và đường HTTP (IT-06…IT-10). INV-8: ràng buộc DB (`uq_decision_idempotency_key` chỉ áp cho `PRIMARY`, test IT-05), `Find` lọc `role = 'PRIMARY'`, và override/outcome/hàng chờ coi bản `SHADOW` là không tồn tại hoặc không lọt hàng chờ; đường ghi `SHADOW` (gói 5.4, `Tadss:Shadow`): worker nền ghi bản `SHADOW` (`idempotency_key` NULL) sau khi chính request vừa ghi bản `PRIMARY` mới (không gửi khi replay hay thua tranh chấp), ghép cặp với `PRIMARY` qua (`reservation_id`, `request_hash`, `created_at`) và `correlation_id` mà không cần cột mới; IT-12 khóa: response `PRIMARY` giống hệt lượt đối chứng tắt shadow, shadow lỗi hoặc treo chỉ để lại bản `SHADOW` là `FALLBACK`, bản `SHADOW` không vào hàng chờ và override/outcome với `decisionId` của nó là 404, replay không tạo thêm bản `SHADOW`.

### C.1. Định danh

- `id`: khóa chính vật lý.
- `decision_id`: định danh logic của một lần đánh giá, trả về cho client.
- `reservation_id`: định danh đơn bên client (SmashCourt `bookingId`).

### C.2. Quy tắc tính `request_hash`

1. Lấy các trường nghiệp vụ của request v2 (không gồm `requestedAt`, `metadata` và `demand` — ảnh chụp nhu cầu thay đổi giữa các lần retry cùng idempotency key; danh sách trường được khai báo trong mã). `demand` vẫn được lưu trong `request_payload`.
2. Thời gian quy về UTC từ giá trị có offset, định dạng `yyyy-MM-ddTHH:mm:ssZ`; chuỗi phân loại viết hoa; số tiền định dạng bất biến theo culture.
3. Dựng object canonical theo thứ tự trường cố định, serialize JSON không khoảng trắng.
4. SHA-256 trên UTF-8 → 64 ký tự hex thường.
