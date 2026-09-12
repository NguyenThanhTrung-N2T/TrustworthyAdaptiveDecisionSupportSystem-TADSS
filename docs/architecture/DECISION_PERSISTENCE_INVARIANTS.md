# 📜 TADSS Decision Persistence & Data Invariants Specification

> **Tài liệu đặc tả các Invariants cốt lõi của tầng lưu trữ quyết định (Decision Persistence Layer)**  
> **Schema:** `tadss` | **Bảng chính:** `tadss.decision_executions`  
> **Áp dụng cho:** Đồ án 2 & Khóa luận tốt nghiệp (Research & Production-grade Baseline)

---

## 1. INVARIANT 1: `decision_id` Lifecycle & Retry Semantics

### 1.1. Định nghĩa Định danh
* **`id` (UUIDv4):** Khóa chính vật lý (*Physical Primary Key*) của bản ghi kiểm toán trong bảng `tadss.decision_executions`.
* **`decision_id` (UUIDv4):** Mã định danh logic (*Logical Decision Run ID*) của một phiên thực thi đánh giá do engine TADSS sinh ra.
* **`reservation_id` (UUIDv4):** Mã định danh đơn đặt sân (*Booking Entity Reference*) từ hệ thống SmashCourt.

### 1.2. Mối quan hệ Cardinality ($1:N$)
$$\text{Reservation} \ (1) \longrightarrow (N) \ \text{DecisionExecutions}$$
Một đơn đặt sân (`reservation_id`) có thể trải qua nhiều chu kỳ ra quyết định trong vòng đời của nó:
1. Đánh giá sơ bộ khi khách chọn khung giờ (*Initial Quote*).
2. Đánh giá lại trước khi thanh toán cọc (*Pre-checkout Re-evaluation*).
3. Đánh giá lại khi khách yêu cầu đổi lịch (*Rescheduling*).
4. Đánh giá hậu can thiệp của Quản lý (*Post-override Evaluation*).

Mỗi chu kỳ đánh giá độc lập sẽ được cấp phát một **`decision_id`** duy nhất.

### 1.3. Cơ chế Idempotency & Retry
* Ràng buộc duy nhất: `CONSTRAINT uq_decision_idempotency UNIQUE (decision_id, idempotency_key)`.
* **Quy tắc thực thi:** Khi mạng bị chập chờn hoặc Client gửi lại (retry) cùng một request chứa cặp `(decision_id, idempotency_key)`:
  * Database sẽ chặn việc ghi đúp thông qua Unique Constraint.
  * Tầng Application bắt được lỗi hoặc đọc từ Cache/DB và trả về đúng bản ghi `response_payload` đã thực thi trước đó mà **không chạy lại thuật toán Strategy**.

---

## 2. INVARIANT 2: `request_hash` Canonicalization & Hashing Rules

### 2.1. Mục đích
`request_hash` đóng vai trò là "dấu vân tay ngữ nghĩa" (*Deterministic Semantic Fingerprint*) của dữ liệu đầu vào, dùng cho cơ chế **Redis Decision Cache** (tra cứu nhanh $O(1)$) và phục vụ thống kê phân tích mẫu quyết định.

### 2.2. Thuật toán Chuẩn hóa (Canonicalization - RFC 8785 / JCS)
Để đảm bảo hai payload có cùng ý nghĩa nghiệp vụ luôn sinh ra cùng một mã băm SHA-256:
1. **Trích xuất thuộc tính:** Chỉ trích xuất các trường tác động trực tiếp đến quyết định:
   * `ReservationSchedule`: `Date`, `StartTime`, `EndTime`, `DayType`
   * `CustomerInfo`: `UserId`, `LoyaltyTier`, `NoShowRate`, `BookingHistoryCount`
   * `ResourceInfo`: `CourtId`, `BranchId`, `CourtTypeId`
   * `PricingPolicy`: `TotalPrice`, `UnitPrices`
2. **Sắp xếp khóa (Key Sorting):** Sắp xếp thứ tự các khóa theo bảng mã ASCII (Lexicographical order).
3. **Chuẩn hóa giá trị:** Trim khoảng trắng thừa, chuẩn hóa chữ hoa cho Enum, chuẩn hóa định dạng số thập phân.
4. **Băm SHA-256:** Tính toán chuỗi băm SHA-256 từ mảng byte UTF-8 của JSON chuẩn hóa $\rightarrow$ Xuất ra đúng **64 ký tự Hexadecimal in thường (`[0-9a-f]{64}`)**.

### 2.3. Quy tắc Bất biến về Index
* **Index Non-Unique:** `CREATE INDEX idx_decision_executions_request_hash ON tadss.decision_executions(request_hash);`
* **Lý do:** Cùng một `request_hash = ABC` có thể được thực thi bởi các `strategy_version` khác nhau (v1.0.0 vs v1.1.0) hoặc tại các thời điểm khác nhau. Cả hai lần chạy đều phải được lưu trữ để phục vụ **Ablation Study** (Nghiên cứu triệt tiêu) trong Khóa luận.

---

## 3. INVARIANT 3: Human Override Invariant (Human-in-the-loop Consistency)

### 3.1. Ràng buộc Logic Trạng thái
Một bản ghi quyết định chỉ được coi là đã ghi đè khi và chỉ khi thỏa mãn đồng thời các điều kiện:
$$\forall e \in \text{DecisionExecutions}, \quad e.\text{is\_overridden} = \text{TRUE} \iff \begin{cases} e.\text{overridden\_by} \neq \text{NULL} \land \text{LEN}(e.\text{overridden\_by}) > 0 \\ e.\text{override\_reason} \neq \text{NULL} \land \text{LEN}(e.\text{override\_reason}) > 0 \\ e.\text{updated\_at} \ge e.\text{created\_at} \end{cases}$$

### 3.2. Tính Bất biến của Nhật ký Máy (Audit Immutability)
* Khi Quản lý can thiệp ghi đè (ví dụ: Chấp nhận miễn cọc cho khách VIP quen dù máy yêu cầu cọc 100%):
  * Cột `response_payload` (chứa kết quả tính toán gốc của Strategy) **giữ nguyên 100% không bị sửa đổi**.
  * Cập nhật các trường: `is_overridden = TRUE`, `overridden_by = 'Manager_ID'`, `override_reason = 'Khách quen chi nhánh'`, `updated_at = UtcNow`.
* **Mục đích:** Bảo tồn nguyên vẹn dữ liệu so sánh giữa quyết định của máy và quyết định của con người phục vụ báo cáo khoa học.

---

## 4. INVARIANT 4: Audit Timestamp Ownership (Application Interceptor)

### 4.1. Quyền sở hữu duy nhất (Single Source of Truth)
Toàn bộ thời gian kiểm toán (`created_at`, `updated_at`) thuộc quyền sở hữu tuyệt đối của **`AuditTimestampInterceptor`** tại tầng Application (thực thi trước khi ghi vào Database), không phụ thuộc vào SQL trigger hay server timestamp của PostgreSQL.

### 4.2. Quy tắc Vòng đời Timestamp
| Hành động EF Core | `created_at` | `updated_at` |
| :--- | :--- | :--- |
| **`EntityState.Added` (Thêm mới)** | `DateTime.UtcNow` | `DateTime.UtcNow` ($= \text{created\_at}$) |
| **`EntityState.Modified` (Cập nhật/Override)** | **Không đổi (Immutable)** | `DateTime.UtcNow` ($> \text{created\_at}$) |

* **Định dạng:** Chuẩn ISO 8601 UTC (`timestamp with time zone` / `TIMESTAMPTZ` trong PostgreSQL).

---

## 5. TỔNG HỢP KIẾN TRÚC INDEX BẢNG `tadss.decision_executions`

```sql
-- 1. Primary Key
CONSTRAINT PK_decision_executions PRIMARY KEY (id);

-- 2. Idempotency Protection
CREATE UNIQUE INDEX uq_decision_idempotency 
ON tadss.decision_executions (decision_id, idempotency_key);

-- 3. Telemetry & Entity Lookup
CREATE INDEX idx_decision_executions_request_hash 
ON tadss.decision_executions (request_hash);

CREATE INDEX idx_decision_executions_reservation_id 
ON tadss.decision_executions (reservation_id);

CREATE INDEX idx_decision_executions_created_at 
ON tadss.decision_executions (created_at);

-- 4. Manager Dashboard Partial Index (Hot Queue)
CREATE INDEX idx_decision_executions_pending_approval 
ON tadss.decision_executions (created_at) 
WHERE (need_approval = TRUE AND is_overridden = FALSE);
```
