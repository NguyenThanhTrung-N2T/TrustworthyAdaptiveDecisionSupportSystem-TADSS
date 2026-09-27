# 📜 TADSS Decision Persistence & Data Invariants Specification

> **Tài liệu đặc tả các Invariants cốt lõi của tầng lưu trữ quyết định (Decision Persistence Layer)**  
> **Database:** `tadssdb` | **Schema:** `tadss` | **Bảng chính:** `tadss.decision_executions`  
> **Phân loại kiến trúc:** *Decision Execution Audit Ledger with Immutable Machine Snapshot and Mutable Governance Metadata*  
> **📌 Khóa Phạm vi (Scope Lock):** Toàn bộ 4 Invariants lưu trữ dưới đây thuộc phạm vi bắt buộc của **Đồ án 2 (`TADSS Runtime Engine`)** nhằm bảo đảm tính toàn vẹn kiểm toán (Audit Provenance) và cơ chế ghi đè an toàn (Human-in-the-Loop). Việc khai thác dữ liệu lịch sử này cho các bài toán học máy hoặc nghiên cứu triệt tiêu thuộc giai đoạn tương lai **`[OUT-OF-SCOPE ĐỒ ÁN 2 — Khóa luận]`**.

---

## 1. INVARIANT 1: `decision_id` Lifecycle & Idempotency Semantics

### 1.1. Định nghĩa Định danh
* **`id` (UUIDv4):** Khóa chính vật lý (*Physical Primary Key*) của bản ghi kiểm toán trong bảng `tadss.decision_executions`.
* **`decision_id` (UUIDv4):** Mã định danh logic duy nhất (*Globally Unique Logical Decision Evaluation ID*) của một phiên thực thi đánh giá do engine TADSS sinh ra.
* **`reservation_id` (UUIDv4):** Mã định danh đơn đặt sân (*Booking Entity Reference*) từ hệ thống SmashCourt.

### 1.2. Mối quan hệ Cardinality ($1:N$)
$$\text{Reservation} \ (1) \longrightarrow (N) \ \text{DecisionExecutions}$$
Một đơn đặt sân (`reservation_id`) có thể trải qua nhiều chu kỳ ra quyết định trong vòng đời của nó:
1. Đánh giá sơ bộ khi khách chọn khung giờ (*Initial Quote*).
2. Đánh giá lại trước khi thanh toán cọc (*Pre-checkout Re-evaluation*).
3. Đánh giá lại khi khách yêu cầu đổi lịch (*Rescheduling*).
4. Đánh giá hậu can thiệp của Quản lý (*Post-override Evaluation*).

Mỗi chu kỳ đánh giá độc lập sẽ được cấp phát một **`decision_id`** duy nhất (`CONSTRAINT uq_decision_id UNIQUE (decision_id)`). Tra cứu `GET /api/v1/decisions/{decisionId}` luôn trả về chính xác 1 bản ghi execution duy nhất.

### 1.3. Cơ chế Global Idempotency & Replay
* Ràng buộc duy nhất toàn cục: `CREATE UNIQUE INDEX uq_decision_idempotency_key ON tadss.decision_executions (idempotency_key) WHERE idempotency_key IS NOT NULL`.
* **Quy tắc thực thi:**
  * **Key mới:** Thực thi Strategy, lưu bản ghi mới vào DB, trả về HTTP 200 OK.
  * **Key đã tồn tại + Payload trùng khớp (`request_hash` giống nhau):** Database chặn ghi đúp, Application trả về bản ghi `response_payload` đã thực thi trước đó mà **không chạy lại thuật toán Strategy** (Replay HTTP 200 OK).
  * **Key đã tồn tại + Payload khác biệt:** Database chặn ghi đúp, Application từ chối và trả về **HTTP 409 Conflict (`IDEMPOTENCY_KEY_REUSE`)**.

---

## 2. INVARIANT 2: `request_hash` Canonicalization & Hashing Rules

### 2.1. Mục đích
`request_hash` đóng vai trò là "dấu vân tay ngữ nghĩa" (*Deterministic Semantic Fingerprint*) của dữ liệu đầu vào, dùng cho cơ chế **Exact-Match Request-Hash Cache (Redis SHA-256)** (tra cứu nhanh $O(1)$) và phục vụ thống kê phân tích mẫu quyết định.

### 2.2. Thuật toán Chuẩn hóa (Canonicalization - RFC 8785 / JCS)
Để đảm bảo hai payload có cùng ý nghĩa nghiệp vụ luôn sinh ra cùng một mã băm SHA-256:
1. **Trích xuất thuộc tính:** Chỉ trích xuất các trường tác động trực tiếp đến quyết định (Schedule, CustomerInfo, ResourceInfo, PaymentPolicy).
2. **Sắp xếp khóa (Key Sorting):** Sắp xếp thứ tự các khóa theo bảng mã ASCII (Lexicographical order).
3. **Chuẩn hóa giá trị:** Trim khoảng trắng thừa, chuẩn hóa chữ hoa cho Enum, chuẩn hóa timestamp sang ISO 8601 UTC string (giữ nguyên semantic values).
4. **Băm SHA-256:** Tính toán chuỗi băm SHA-256 từ mảng byte UTF-8 của JSON chuẩn hóa $\rightarrow$ Xuất ra đúng **64 ký tự Hexadecimal in thường (`[0-9a-f]{64}`)**.

### 2.3. Quy tắc Bất biến về Index
* **Index Non-Unique:** `CREATE INDEX idx_decision_executions_request_hash ON tadss.decision_executions(request_hash);`
* **Lý do:** Cùng một `request_hash = ABC` có thể được thực thi bởi các `strategy_version` khác nhau (v1.0.0 vs v1.1.0) hoặc tại các thời điểm khác nhau. Cả hai lần chạy đều được lưu trữ để phục vụ kiểm toán lịch sử phiên bản chính sách (và mở rộng cho đối chứng thực nghiệm `[OUT-OF-SCOPE ĐỒ ÁN 2 — Khóa luận]`).

---

## 3. INVARIANT 3: Human Override Invariant & Ledger Semantics

### 3.1. Phân định Cấu trúc Bản ghi
Bản ghi kiểm toán gồm 2 phần tách bạch:
1. **Immutable Machine Decision Snapshot:** `decision_id`, `reservation_id`, `strategy_name`, `strategy_version`, `execution_type`, `execution_time_ms`, `fallback_used`, `failure_reason`, `cache_hit`, `need_approval`, `request_hash`, `response_payload`, `created_at` — **Không bao giờ bị sửa đổi**.
2. **Mutable Governance Metadata:** `is_overridden`, `overridden_by`, `override_reason`, `version`, `updated_at` — **Được phép cập nhật khi Manager can thiệp ghi đè**.

### 3.2. Ràng buộc Logic Trạng thái Override
Một bản ghi quyết định chỉ được coi là đã ghi đè khi và chỉ khi thỏa mãn đồng thời:
$$\forall e \in \text{DecisionExecutions}, \quad e.\text{is\_overridden} = \text{TRUE} \iff \begin{cases} e.\text{overridden\_by} \neq \text{NULL} \land \text{LEN}(e.\text{overridden\_by}) > 0 \\ e.\text{override\_reason} \neq \text{NULL} \land \text{LEN}(e.\text{override\_reason}) > 0 \\ e.\text{updated\_at} \ge e.\text{created\_at} \end{cases}$$

Khi Quản lý can thiệp ghi đè, `response_payload` gốc **giữ nguyên 100%**, đảm bảo bảo tồn nguyên vẹn dữ liệu máy phục vụ truy vết kiểm toán vận hành (Immutable Audit Trail) (và làm nguồn dữ liệu đối chứng cho giai đoạn sau `[OUT-OF-SCOPE ĐỒ ÁN 2 — Khóa luận]`).

---

## 4. INVARIANT 4: Audit Timestamp Ownership (Application Interceptor)

### 4.1. Quyền sở hữu duy nhất (Single Source of Truth)
Toàn bộ thời gian kiểm toán (`created_at`, `updated_at`) thuộc quyền sở hữu tuyệt đối của **`AuditTimestampInterceptor`** tại tầng Application, không phụ thuộc vào SQL trigger hay server timestamp của PostgreSQL.

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

-- 2. Logical Decision Evaluation Uniqueness (1:1 per evaluation lifecycle)
CREATE UNIQUE INDEX uq_decision_id 
ON tadss.decision_executions (decision_id);

-- 3. Global Request Idempotency Key
CREATE UNIQUE INDEX uq_decision_idempotency_key 
ON tadss.decision_executions (idempotency_key) 
WHERE idempotency_key IS NOT NULL;

-- 4. Telemetry & Entity Lookup
CREATE INDEX idx_decision_executions_request_hash 
ON tadss.decision_executions (request_hash);

CREATE INDEX idx_decision_executions_reservation_id 
ON tadss.decision_executions (reservation_id);

CREATE INDEX idx_decision_executions_created_at 
ON tadss.decision_executions (created_at);

-- 5. Manager Dashboard Partial Index (Hot Queue)
CREATE INDEX idx_decision_executions_pending_approval 
ON tadss.decision_executions (created_at DESC) 
WHERE (need_approval = TRUE AND is_overridden = FALSE);
```
