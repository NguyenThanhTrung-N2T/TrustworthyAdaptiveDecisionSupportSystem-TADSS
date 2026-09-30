# 📐 TADSS Research Dataset Schema & Provenance Invariants Specification (v1.0 — FROZEN)

> **Tài liệu Đặc tả Đóng băng Cấu trúc 3 Bộ Dữ liệu Nghiên cứu, Hợp đồng Kết nối (Join Contract) và 8 Bất biến Truy xuất Nguồn gốc (Provenance Invariants)**  
> **Phiên bản Schema:** `1.0.0` | **Trạng thái:** `FROZEN & IMPLEMENTED (MILESTONE 4 COMPLETE — 100% INV-P1..P8 VERIFIED)`  
> **Áp dụng cho:** **Đồ án 2 — Nền tảng TADSS Decision Runtime Engine (Phase 4 — Milestone 4 / Tuần 9)**  
> **Ranh giới Phạm vi (Scope Lock):** Tài liệu này đặc tả cấu trúc dữ liệu thực nghiệm phục vụ đánh giá **hạ tầng kỹ thuật phần mềm của TADSS Runtime Engine** (Resilience Pipeline Polly v8, Request-Hash Caching RFC 8785, và Two-Tier Append-Only Audit Ledger kèm cơ chế Quản trị Kết quả Quyết định Human-in-the-Loop). Đặc tả này **hoàn toàn không chứa các đặc trưng hay nhãn huấn luyện AI/ML** (thuộc phạm vi mở rộng của Khóa luận tốt nghiệp ở giai đoạn sau) và **không thực hiện nghiên cứu triệt tiêu (ablation study)**.

---

## 1. MỤC TIÊU KIẾN TRÚC & NGUYÊN TẮC KHÔNG Ô NHIỄM PRODUCTION SCHEMA

1. **Zero Production Schema Pollution:** Bảng kiểm toán vật lý `tadss.decision_executions` trong `TADSS.Runtime.Api` chỉ lưu trữ đúng 19 cột nghiệp vụ và kiểm toán cốt lõi của Production Runtime. Tuyệt đối không thêm các cột chỉ phục vụ benchmark (như `run_id`, `scenario_id`, `concurrency_level`, `client_elapsed_ms`) vào bảng Production.
2. **External Evidence Reconstruction:** Phân hệ `Normalization/` trong `tests/TADSS.BenchmarkRunner` thu thập (harvest) các bản ghi `DecisionExecution` từ `TadssDbContext` ngay khi lượt chạy kết thúc (trước khi `ScenarioHostController` giải phóng `WebApplicationFactory`) và kết nối 1-1 với `RawMeasurementRecord` $\oplus$ `RunMetadata` thông qua khóa định danh logic `DecisionId`.
3. **Traceable Multi-Dataset Projection:** Từ tập bản ghi kết nối đã qua kiểm định nguồn gốc, `DatasetNormalizerEngine` chiếu (project) thành **3 bộ dữ liệu chuẩn hóa độc lập** tương ứng với 3 Câu hỏi Nghiên cứu Kỹ thuật (**RQ1, RQ2, RQ3**).

---

## 2. ĐẶC TẢ ĐÓNG BĂNG 3 BỘ DỮ LIỆU NGHIÊN CỨU (STEP 1 — FROZEN SCHEMAS)

### 2.1. Bộ 1 — `resilience_evaluation_dataset` (`CSV` & `JSONL`) — Phục vụ RQ1
* **Mục đích:** Đối chứng thực nghiệm khả năng chịu lỗi của đường ống Polly v8 5 tầng, tỷ lệ duy trì quyết định ($\text{DCR}$), hành vi Retry, Circuit Breaker và Fallback.
* **Phạm vi kịch bản:** **11 kịch bản $\times$ 4 mức VUs = 44 lượt chạy** thuộc:
  * **Layer B (`R0, R1, R2, R3`):** Đường cơ sở và các kịch bản tiêm lỗi cô lập (Retry Recovery, Circuit Open Fast-Fail, Retry Exhaustion).
  * **Layer D (`D0, D1, D2`):** Chiến lược đa quy tắc nặng sát ngưỡng cắt, tranh chấp ghi DB kiểm toán, và biến thiên độ trễ hai đỉnh (Bimodal Jitter).
  * **Layer E (`E1, E2, E3, E4`):** Cộng hưởng độ trễ, suy giảm 3 tầng đồng thời (Cascading Triple Stress), tự chữa lành qua Retry (`E3`) và chu trình đóng lại mạch ngắt `OPEN` $\rightarrow$ `HALF-OPEN` $\rightarrow$ `CLOSED` (`E4`).
  * *(Lưu ý phân hoạch `Layer E`: Các kịch bản thuộc `Layer E` (`E0..E4`) được phân hoạch có chủ đích giữa Dataset 1 (`E1..E4`: trọng tâm chịu lỗi cộng hưởng & tự phục hồi phục vụ RQ1) và Dataset 2 (`E0`: tải hỗn hợp chuẩn 80% Cache Hit / 20% Cache Miss phục vụ RQ2) theo đúng câu hỏi nghiên cứu chính của từng kịch bản).*
* **Tổng số cột:** **23 cột** (đánh số tường minh từ `1` đến `23`: `6 Run Context + 2 Join Identity + 8 Execution & Fault + 4 Latency & Timeout + 3 Run/Audit = 23`):

| # | Tên cột (`snake_case`) | Kiểu dữ liệu | Nguồn dữ liệu | Mô tả & Ngữ nghĩa học thuật |
| :-: | :--- | :--- | :--- | :--- |
| **1** | `schema_version` | `string` | `Normalizer` | Phiên bản đặc tả dữ liệu cố định (`"1.0.0"`). |
| **2** | `run_id` | `string` | `RunMetadata` | Mã định danh lượt chạy thực nghiệm (VD: `EXP-W8-20260927-R3-VU100`). |
| **3** | `experiment_layer` | `string` | `RunMetadata` | Phân tầng thực nghiệm (`LayerB_Resilience`, `LayerD_StrategyPersistence`, `LayerE_CombinedStress`). |
| **4** | `scenario_id` | `string` | `RunMetadata` | Mã kịch bản (`R0..R3`, `D0..D2`, `E1..E4`). |
| **5** | `concurrency_level` | `int` | `RunMetadata` | Mức tải đồng thời ($VUs \in \{1, 10, 50, 100\}$). |
| **6** | `sample_index` | `int` | `ClientRaw` | Chỉ số thứ tự request trong pha Measurement (`0..N-1`). |
| **7** | `decision_id` | `guid` | `JoinKey` | **Khóa kết nối 1:1** giữa `RawMeasurementRecord.DecisionId` và `DecisionExecution.DecisionId`. |
| **8** | `reservation_id` | `guid` | `AuditLedger` | Mã định danh đơn đặt sân (`DecisionExecution.ReservationId`). |
| **9** | `strategy_name` | `string` | `AuditLedger` | Tên chiến lược đã thực thi (`DecisionExecution.StrategyName`). |
| **10** | `strategy_version` | `string` | `AuditLedger` | Phiên bản chiến lược (`DecisionExecution.StrategyVersion`). |
| **11** | `execution_type` | `string` | `ClientRaw/DB` | Phương thức hoàn tất quyết định (`AUTOMATED`, `FALLBACK`, `CACHE_HIT` — xác minh từ [`DecisionService.cs:141`](../../src/TADSS.Runtime.Api/Services/DecisionService.cs#L141) và [`MeasurementWorker.cs:118-120`](../../tests/TADSS.BenchmarkRunner/Harness/MeasurementWorker.cs#L118-L120), được đối chiếu chéo bởi `INV-P4`). |
| **12** | `decision_status` | `string` | `ClientRaw/DB` | Trạng thái kết quả quyết định (`APPROVED`, `REJECTED`, `NEED_APPROVAL`). |
| **13** | `fallback_used` | `bool` | `ClientRaw/DB` | Cờ xác nhận đường ống đã kích hoạt `FallbackDecisionStrategy` (`DecisionExecution.FallbackUsed`, đối chiếu chéo bởi `INV-P4`). |
| **14** | `failure_reason` | `string?` | `AuditLedger` | Nguyên nhân gốc kích hoạt Fallback theo đúng từ vựng canonical của Runtime (`STRATEGY_TIMEOUT`, `CIRCUIT_BREAKER_OPEN`, `STRATEGY_EXCEPTION`, hoặc `null` — chuẩn hóa từ [`FailureReasonPrefixes.cs`](../../src/TADSS.Runtime.Api/Infrastructure/Resilience/FailureReasonPrefixes.cs)). |
| **15** | `need_approval` | `bool` | `AuditLedger` | Cờ trạng thái quyết định yêu cầu xem xét quản trị (`DecisionExecution.NeedApproval`). |
| **16** | `cache_hit` | `bool` | `ClientRaw/DB` | Cờ xác nhận trúng bộ nhớ đệm (`DecisionExecution.CacheHit`) — phục vụ kiểm định chéo `INV-P4` & `INV-P5`. |
| **17** | `http_status_code` | `int` | `ClientRaw` | Mã trạng thái phản hồi HTTP (`200`, `500`, `504`, ...). |
| **18** | `client_elapsed_ms` | `double` | `ClientRaw` | Độ trễ đầu-cuối (End-to-End RTT) đo bằng `Stopwatch` tại Client (`ms`, làm tròn 3 chữ số thập phân). |
| **19** | `server_execution_time_ms` | `int` | `AuditLedger` | Thời gian thực thi tầng Strategy ghi nhận trong `DecisionExecution.ExecutionTimeMs` (`ms`). |
| **20** | `within_configured_outer_timeout` | `bool` | `Derived` | `true` nếu `client_elapsed_ms <= configured_outer_timeout_ms` (`500ms` — ngân sách timeout cấu hình của đường ống, phân biệt rõ với SLA nghiệp vụ). |
| **21** | `circuit_setup_mode` | `string` | `RunMetadata` | Trạng thái thiết lập Circuit Breaker của lượt chạy (`CLOSED`, `PRE_OPENED`, `ISOLATED_CLOSED`, `RECOVERED_CLOSED`). |
| **22** | `is_eligible_for_dcr` | `bool` | `ClientRaw` | Thuộc mẫu số tính $\text{DCR}$ (`true` cho mọi request hợp lệ không bị hủy bởi caller `F7`). |
| **23** | `created_at_utc` | `string` | `AuditLedger` | Thời điểm tạo bản ghi kiểm toán chuẩn ISO 8601 UTC (`DecisionExecution.CreatedAt`). |

---

### 2.2. Bộ 2 — `latency_caching_dataset` (`CSV` & `JSONL`) — Phục vụ RQ2
* **Mục đích:** Định lượng chi phí chuẩn hóa RFC 8785 + SHA-256 khi trượt cache ($\text{Cache Miss Overhead} = G_1 - G_0$), lợi ích khi trúng cache ($\text{Cache Benefit} = G_0 - G_2$), và phân phối độ trễ dưới các bệnh lý bộ nhớ đệm thực tế (Partial Hit, WAN RTT, Thundering Herd, LRU Eviction Thrashing).
* **Phạm vi kịch bản:** **8 kịch bản $\times$ 4 mức VUs = 32 lượt chạy** thuộc:
  * **Layer A (`G0, G1, G2`):** Đường cơ sở đánh giá bộ nhớ đệm (Bypass, 100% Miss, 100% Hit).
  * **Layer C (`C0, C1, C2, C3`):** Các bệnh lý bộ nhớ đệm thực tế (50% Hit, WAN Latency, Thundering Herd, Eviction Thrashing).
  * **Layer E (`E0`):** Tải hỗn hợp chuẩn vận hành thực tế (80% Cache Hit / 20% Cache Miss — thuộc nhóm đánh giá hiệu năng bộ nhớ đệm RQ2).
* **Tổng số cột:** **19 cột** (đánh số tường minh từ `1` đến `19`):

| # | Tên cột (`snake_case`) | Kiểu dữ liệu | Nguồn dữ liệu | Mô tả & Ngữ nghĩa học thuật |
| :-: | :--- | :--- | :--- | :--- |
| **1** | `schema_version` | `string` | `Normalizer` | Phiên bản đặc tả dữ liệu cố định (`"1.0.0"`). |
| **2** | `run_id` | `string` | `RunMetadata` | Mã định danh lượt chạy thực nghiệm. |
| **3** | `experiment_layer` | `string` | `RunMetadata` | Phân tầng thực nghiệm (`LayerA_CacheBaseline`, `LayerC_CachePathologies`, `LayerE_CombinedStress`). |
| **4** | `scenario_id` | `string` | `RunMetadata` | Mã kịch bản (`G0..G2`, `C0..C3`, `E0`). |
| **5** | `concurrency_level` | `int` | `RunMetadata` | Mức tải đồng thời ($VUs \in \{1, 10, 50, 100\}$). |
| **6** | `sample_index` | `int` | `ClientRaw` | Chỉ số thứ tự request trong pha Measurement (`0..N-1`). |
| **7** | `decision_id` | `guid` | `JoinKey` | **Khóa kết nối 1:1** giữa Client và Server Audit Ledger. |
| **8** | `reservation_id` | `guid` | `AuditLedger` | Mã định danh đơn đặt sân (`DecisionExecution.ReservationId`). |
| **9** | `cache_state_mode` | `string` | `RunMetadata` | Chế độ bộ nhớ đệm của kịch bản (`bypassed`, `cold_miss_path`, `warm_hit_path`, `partial_50_percent_warm`, ...). |
| **10** | `cache_hit` | `bool` | `ClientRaw/DB` | Cờ xác nhận trúng bộ nhớ đệm (`DecisionExecution.CacheHit`, đối chiếu chéo bởi `INV-P4`). |
| **11** | `execution_type` | `string` | `ClientRaw/DB` | Phương thức hoàn tất quyết định (`CACHE_HIT` hoặc `AUTOMATED`, đối chiếu chéo bởi `INV-P4`). |
| **12** | `request_hash` | `string` | `AuditLedger` | Chuỗi băm SHA-256 chuẩn hóa RFC 8785 (64 ký tự hex thường) trong `DecisionExecution.RequestHash`. |
| **13** | `idempotency_key` | `string` | `AuditLedger` | Khóa chống trùng lặp của request (`DecisionExecution.IdempotencyKey`). |
| **14** | `client_elapsed_ms` | `double` | `ClientRaw` | Độ trễ đầu-cuối (End-to-End RTT) đo tại Client (`ms`). |
| **15** | `server_execution_time_ms` | `int` | `AuditLedger` | Thời gian thực thi tầng Strategy trên Server (`0ms` khi `cache_hit == true`). |
| **16** | `client_server_latency_delta_ms` | `double` | `Derived` | `max(0, client_elapsed_ms - server_execution_time_ms)`: Độ chênh lệch độ trễ quan sát được ngoài thời gian thực thi Strategy (`observed latency delta`, bao gồm HTTP/loopback transport, JSON serialization/deserialization, đồng bộ ghi DB Audit, và lập lịch luồng — **không quy kết cho riêng một thành phần đơn lẻ**). |
| **17** | `run_cache_hit_ratio_percent` | `double` | `RunSummary` | Tỷ lệ trúng bộ nhớ đệm của toàn lượt chạy (`0.0` đến `100.0`). |
| **18** | `within_sla_150ms` | `bool` | `Derived` | `true` nếu `client_elapsed_ms <= 150.0` (`150ms` là ngưỡng hiệu năng mục tiêu thực nghiệm SC1 cho đường truyền bình thường/cache, **không phải là SLA vận hành phổ quát áp dụng cho mọi kịch bản chịu lỗi có Retry/Fallback**). |
| **19** | `created_at_utc` | `string` | `AuditLedger` | Thời điểm tạo bản ghi kiểm toán chuẩn ISO 8601 UTC (`DecisionExecution.CreatedAt`). |

---

### 2.3. Bộ 3 — `governance_audit_dataset` (`CSV` & `JSONL`) — Phục vụ RQ3
* **Mục đích & Taxonomy Dữ liệu Kiểm toán:** Kiểm chứng thực nghiệm kiến trúc **Nhật ký Kiểm toán Hai tầng (Two-Tier Audit Ledger)** trên thực thể `DecisionExecution`.
  > **Định nghĩa Thuật ngữ (Terminology Clarification):** Thuật ngữ **"Two-Tier Audit Ledger"** trong đặc tả này chỉ **sự phân tách logic về phân loại dữ liệu (logical data classification)** giữa **Tầng 1 — Bằng chứng thực thi máy bất biến (`Immutable Machine Snapshot`)** và **Tầng 2 — Siêu dữ liệu quản trị khả biến (`Mutable Governance Metadata`)** ngay bên trong **cùng một bản ghi kiểm toán vật lý (`tadss.decision_executions`)**, hoàn toàn không phải là kiến trúc hai cơ sở dữ liệu vật lý hay sổ cái phân tán.
  1. **Tier 1 — 9-Field Immutable Machine Snapshot (`9 thuộc tính chụp trạng thái máy bất biến`):** `DecisionId`, `ReservationId`, `StrategyName`, `StrategyVersion`, `ExecutionType`, `ExecutionTimeMs`, `RequestHash`, `ResponsePayload`, `CreatedAt` **cộng với trường trạng thái quyết định bất biến (`invariant decision-state field`) `NeedApproval`** (không xếp `NeedApproval` làm trường thứ 10 của Machine Snapshot, nhưng giá trị của nó được giữ cố định từ thời điểm ra quyết định ban đầu).
  2. **Tier 2 — 5-Field Mutable Governance Metadata (`5 thuộc tính quản trị khả biến trong thực thể DB`):** `IsOverridden`, `OverriddenBy`, `OverrideReason`, `Version`, `UpdatedAt`.
* **Bản chất Bằng chứng của Schema 3 (Transition-Evidence Dataset Semantics):**
  * Schema 3 (`31 cột`) được thiết kế là một **Transition-Evidence Dataset (bộ dữ liệu bằng chứng chuyển trạng thái)**, **không lưu lặp lại toàn bộ 5 trường quản trị khả biến ở cả hai thời điểm $t_0$ và $t_1$** (nhằm giữ cấu trúc 31 cột tinh gọn, không phình lên 36–37 cột).
  * Cụ thể:
    * Với **9-Field Machine Snapshot + `NeedApproval`**: Bộ dữ liệu lưu đầy đủ cặp giá trị `(t0, t1)` để tự thân tệp CSV/JSONL chứng minh tính bất biến tuyệt đối trước và sau thao tác ghi đè.
    * Với **Mutable Governance Metadata**: Bộ dữ liệu lưu các cột phản ánh sự chuyển trạng thái (`is_overridden_t0`, `is_overridden_t1`, `overridden_by_t1`, `override_reason_t1`) và cột dẫn xuất `version_transition`. Trong khi đó, bộ kiểm định `DatasetProvenanceVerifier` kiểm tra trực tiếp trên đối tượng thực thể `DecisionExecution` trong bộ nhớ tại $t_0$ và $t_1$ đối với cả 5 trường (`IsOverridden`, `OverriddenBy`, `OverrideReason`, `Version`, `UpdatedAt`) và xuất bằng chứng xác nhận vào `dataset_provenance_manifest.json`.
* **Ranh giới Ngữ nghĩa của Thao tác Override (Decision-Outcome Governance vs. Booking Transaction):**
  * API `POST /api/v1/decisions/{id}/override` là thao tác **Quản trị Kết quả Quyết định của TADSS (`Decision-Outcome Governance Override`)**: người vận hành/quản lý ghi đè **kết quả quyết định (`OverriddenStatus` $\in \{\text{"APPROVED"}, \text{"REJECTED"}\}$)** trên sổ cái kiểm toán của TADSS, **không phải là giao dịch duyệt đơn đặt sân vật lý (booking approval transaction)** của ứng dụng SmashCourt:
    ```text
    TADSS Runtime Engine
      └── Owns Decision Evaluation & Governance Override Outcome (APPROVED / REJECTED)
                │
                ▼
    SmashCourt Application Backend
      └── Owns Booking Transaction State & Court Reservation Lifecycle
    ```
* **Phạm vi kịch bản (Controlled Governance Lifecycle Suite):**
  * **Nhóm `POSITIVE_CONTROL` (`G-A`, `G-B` — Valid Decision-Outcome Override & Snapshot Immutability):**
    * **`G-A` (`Fallback` $\rightarrow$ `PendingQueue` $\rightarrow$ `Decision-Outcome Override`):** Quyết định sinh ra từ nhánh `FALLBACK` (`NeedApproval = true`, `Version = 1` tại $t_0$) $\rightarrow$ xác nhận xuất hiện trong `GET /api/v1/decisions/pending-approvals` $\rightarrow$ người vận hành gọi `POST /api/v1/decisions/{id}/override` để ghi đè kết quả quyết định TADSS (`HTTP 200`) $\rightarrow$ chụp trạng thái $t_1$ (`IsOverridden = true`, `Version = 2`, `UpdatedAt_t1 >= UpdatedAt_t0`) và xác nhận đã rút khỏi hàng đợi `pending-approvals`.
    * **`G-B` (`Automated NEED_APPROVAL` $\rightarrow$ `PendingQueue` $\rightarrow$ `Decision-Outcome Override`):** Quyết định sinh ra từ chiến lược tự động với kết quả cần xem xét (`ExecutionType = AUTOMATED`, `NeedApproval = true`, `Version = 1` tại $t_0$) $\rightarrow$ người vận hành gọi `POST /api/v1/decisions/{id}/override` (`HTTP 200`) $\rightarrow$ kiểm chứng bất biến Snapshot và chuyển trạng thái quản trị tại $t_1$.
  * **Nhóm `NEGATIVE_CONTROL` (`G-C1`, `G-C2` — Guard Verification & State Preservation):**
    * **`G-C1` (`Duplicate Override Rejection`):** Gọi `POST /api/v1/decisions/{id}/override` lần thứ 2 trên bản ghi đã được ghi đè (`IsOverridden = true`, `Version = 2` tại $t_0$) $\rightarrow$ hệ thống bắt buộc từ chối với `HTTP 400 Bad Request` (`ErrorCodes.BadRequest`) và giữ nguyên toàn bộ trạng thái bản ghi tại $t_1$ (`version_transition = "2->2"`).
    * **`G-C2` (`Non-Approvable Automated Decision Rejection`):** Gọi `POST /api/v1/decisions/{id}/override` trên quyết định tự động không yêu cầu xem xét (`ExecutionType = AUTOMATED`, `NeedApproval = false`, `Version = 1` tại $t_0$) $\rightarrow$ hệ thống bắt buộc từ chối với `HTTP 400 Bad Request` và giữ nguyên trạng thái tại $t_1$ (`IsOverridden = false`, `version_transition = "1->1"`).
* **Tổng số cột:** **31 cột** (đánh số tường minh từ `1` đến `31`):

| # | Tên cột (`snake_case`) | Kiểu dữ liệu | Nguồn dữ liệu | Mô tả & Ngữ nghĩa học thuật |
| :-: | :--- | :--- | :--- | :--- |
| **1** | `schema_version` | `string` | `Normalizer` | Phiên bản đặc tả dữ liệu cố định (`"1.0.0"`). |
| **2** | `governance_case_id` | `string` | `GovHarness` | Mã ca kiểm định quản trị (`G-A-001`, `G-B-001`, `G-C1-001`, `G-C2-001`, ...). |
| **3** | `governance_scenario_group` | `string` | `GovHarness` | Nhóm kịch bản (`G-A_FALLBACK_OVERRIDE`, `G-B_AUTOMATED_REVIEW_OVERRIDE`, `G-C1_DUPLICATE_OVERRIDE_GUARD`, `G-C2_NON_APPROVABLE_GUARD`). |
| **4** | `governance_case_type` | `string` | `GovHarness` | Phân loại kiểm chứng: `"POSITIVE_CONTROL"` (`G-A, G-B`) hoặc `"NEGATIVE_CONTROL"` (`G-C1, G-C2`). |
| **5** | `decision_id` | `guid` | `AuditLedger` | Mã định danh quyết định (`DecisionExecution.DecisionId`, xác nhận `t0 == t1`). |
| **6** | `reservation_id` | `guid` | `AuditLedger` | Mã định danh đơn đặt sân (`DecisionExecution.ReservationId`, xác nhận `t0 == t1`). |
| **7** | `strategy_name_t0` | `string` | `Snapshot_t0` | Tên chiến lược tại $t_0$ (trước thao tác Override). |
| **8** | `strategy_name_t1` | `string` | `Snapshot_t1` | Tên chiến lược tại $t_1$ (sau thao tác Override — kiểm chứng `t0 == t1`). |
| **9** | `strategy_version_t0` | `string` | `Snapshot_t0` | Phiên bản chiến lược tại $t_0$. |
| **10** | `strategy_version_t1` | `string` | `Snapshot_t1` | Phiên bản chiến lược tại $t_1$ (kiểm chứng `t0 == t1`). |
| **11** | `execution_type_t0` | `string` | `Snapshot_t0` | Phương thức thực thi tại $t_0$ (`FALLBACK` hoặc `AUTOMATED`). |
| **12** | `execution_type_t1` | `string` | `Snapshot_t1` | Phương thức thực thi tại $t_1$ (kiểm chứng `t0 == t1`). |
| **13** | `execution_time_ms_t0` | `int` | `Snapshot_t0` | Thời gian thực thi máy tại $t_0$ (`ms`). |
| **14** | `execution_time_ms_t1` | `int` | `Snapshot_t1` | Thời gian thực thi máy tại $t_1$ (`ms` — kiểm chứng `t0 == t1`). |
| **15** | `request_hash_t0` | `string` | `Snapshot_t0` | Chuỗi băm SHA-256 của request tại $t_0$. |
| **16** | `request_hash_t1` | `string` | `Snapshot_t1` | Chuỗi băm SHA-256 của request tại $t_1$ (kiểm chứng `t0 == t1`). |
| **17** | `response_payload_sha256_t0` | `string` | `Snapshot_t0` | Mã băm SHA-256 của cột `ResponsePayload` JSON gốc tại $t_0$. |
| **18** | `response_payload_sha256_t1` | `string` | `Snapshot_t1` | Mã băm SHA-256 của cột `ResponsePayload` JSON tại $t_1$ (kiểm chứng không sửa JSON gốc: `t0 == t1`). |
| **19** | `created_at_utc_t0` | `string` | `Snapshot_t0` | Thời điểm tạo bản ghi `CreatedAt` tại $t_0$. |
| **20** | `created_at_utc_t1` | `string` | `Snapshot_t1` | Thời điểm tạo bản ghi `CreatedAt` tại $t_1$ (kiểm chứng `t0 == t1`). |
| **21** | `need_approval_t0` | `bool` | `Snapshot_t0` | Trường trạng thái quyết định bất biến `NeedApproval` tại $t_0$. |
| **22** | `need_approval_t1` | `bool` | `Snapshot_t1` | Trường trạng thái quyết định bất biến `NeedApproval` tại $t_1$ (kiểm chứng `t0 == t1`). |
| **23** | `was_in_pending_queue_t0` | `bool` | `QueueCheck_t0` | Kết quả kiểm tra sự hiện diện trong `GET /api/v1/decisions/pending-approvals` tại $t_0$. |
| **24** | `in_pending_queue_t1` | `bool` | `QueueCheck_t1` | Kết quả kiểm tra sự hiện diện trong `GET /api/v1/decisions/pending-approvals` tại $t_1$. |
| **25** | `override_http_status` | `int` | `GovHarness` | Mã HTTP trả về từ lệnh `POST /override` (`200` cho `POSITIVE_CONTROL`, `400` cho `NEGATIVE_CONTROL`). |
| **26** | `is_overridden_t0` | `bool` | `Snapshot_t0` | Cờ `IsOverridden` tại $t_0$. |
| **27** | `is_overridden_t1` | `bool` | `Snapshot_t1` | Cờ `IsOverridden` tại $t_1$ (`true` sau khi ghi đè hợp lệ). |
| **28** | `overridden_by_t1` | `string?` | `Snapshot_t1` | Định danh người vận hành thực hiện ghi đè kết quả quyết định tại $t_1$ (`DecisionExecution.OverriddenBy`). |
| **29** | `override_reason_t1` | `string?` | `Snapshot_t1` | Lý do ghi đè kết quả quyết định tại $t_1$ (`DecisionExecution.OverrideReason`). |
| **30** | `version_transition` | `string` | `Derived` | Chuỗi dẫn xuất biểu diễn chuyển trạng thái phiên bản (VD: `"1->2"` cho `POSITIVE_CONTROL`, `"2->2"` cho `G-C1`, `"1->1"` cho `G-C2`). |
| **31** | `machine_snapshot_immutable_verified` | `bool` | `Verifier` | `true` khi toàn bộ **9 thuộc tính Machine Snapshot** (`DecisionId`, `ReservationId`, `StrategyName`, `StrategyVersion`, `ExecutionType`, `ExecutionTimeMs`, `RequestHash`, `ResponsePayload`, `CreatedAt`) + trường `NeedApproval` thỏa mãn `t0 == t1` tuyệt đối. |

---

## 3. HỢP ĐỒNG KẾT NỐI DỮ LIỆU (STEP 2A — CLIENT ↔ AUDIT LEDGER JOIN CONTRACT)

### 3.1. Vòng đời Thu thập Bản ghi Kiểm toán (In-Memory DB Harvest Lifecycle)
Trong `BenchmarkHostBuilder.cs`, mỗi lượt chạy `(Scenario, Concurrency)` khởi tạo một `WebApplicationFactory` độc lập gắn với cơ sở dữ liệu cô lập `UseInMemoryDatabase("tadss_bench_{Guid}")` và `AuditTimestampInterceptor`. Để thu thập chính xác 100% bản ghi kiểm toán mà không mất dữ liệu khi `DisposeAsync()`:

```text
[1] Warm-up & Circuit-Trip Phase
      ├── Gửi các request khởi động (IdempotencyKey: "WARMUP-*", "WARMUP-*-TRIP-*")
      └── Ghi nhận vào DB nhưng KHÔNG nằm trong MeasurementRecords
                │
                ▼
[2] Controlled Measurement Phase (MeasurementWorker.RunAllAsync)
      ├── Gửi N request đo đạc (IdempotencyKey: "{RunId}-REQ-{requestIndex:D6}")
      ├── Deserialize DecisionResultDto -> trích xuất DecisionId & ReservationId
      └── Tính ExpectedRequestHash = RequestHasher.ComputeHash(payload)
                │
                ▼
[3] In-Vivo Audit Ledger Harvest (Host Still Alive — TRƯỚC ScenarioHostController.DisposeAsync)
      ├── AuditLedgerHarvester mở IServiceScope từ ScenarioHostController
      ├── Truy vấn TadssDbContext.DecisionExecutions.AsNoTracking()
      └── Lọc tập SelectedAuditExecutions theo tập khóa duy nhất MeasurementDecisionIds
                │
                ▼
[4] Deterministic 1:1 Inner Join & Provenance Verification
      ├── Kiểm chứng tính duy nhất của DecisionId trong M_valid của từng run_id
      ├── Kết nối RawMeasurementRecord (r) ⋈ DecisionExecution (e) trên r.DecisionId == e.DecisionId
      ├── Kiểm định tự động 8 bất biến INV-P1 .. INV-P8
      └── Chiếu (Project) sang 3 bộ dữ liệu chuẩn hóa CSV & JSONL
```

### 3.2. Định nghĩa Toán học Tập mẫu Kết nối & Ràng buộc Duy nhất `DecisionId`
Gọi $\mathcal{M}$ là danh sách tất cả `RawMeasurementRecord` thu được từ pha Measurement của một lượt chạy `run_id`.
1. **Tập các bản ghi đo đạc hợp lệ có định danh quyết định ($\mathcal{M}_{\text{valid}}$):**
   $$\mathcal{M}_{\text{valid}} = [ r \in \mathcal{M} \mid r.\text{DecisionId} \neq \text{Guid.Empty} ]$$
   *(Các request bị hủy chủ động bởi caller `F7` hoặc lỗi hạ tầng lưu trữ `F6` không sinh `DecisionResultDto` sẽ có `DecisionId == Guid.Empty` và được hạch toán riêng trong chỉ số Completion Rate / DCR, không tham gia phép nối 1:1 với bảng `decision_executions`).*
2. **Ràng buộc Tính Duy nhất của `DecisionId` trong từng Lượt chạy (Uniqueness Prerequisite):**
   Mọi phần tử trong $\mathcal{M}_{\text{valid}}$ của cùng một `run_id` bắt buộc phải mang một `DecisionId` phân biệt:
   $$\text{Count}(\mathcal{M}_{\text{valid}}) = \text{Count}(\text{Distinct}(\{ r.\text{DecisionId} \mid r \in \mathcal{M}_{\text{valid}} \})) = |\mathcal{K}_{\text{meas}}|$$
3. **Tập bản ghi kiểm toán được chọn từ `tadss.decision_executions` ($\mathcal{E}_{\text{all}}$):**
   $$\mathcal{E}_{\text{selected}} = \{ e \in \mathcal{E}_{\text{all}} \mid e.\text{DecisionId} \in \mathcal{K}_{\text{meas}} \}$$
4. **Hạch toán Toàn diện Quần thể Mẫu số DCR (`DCR Population Accounting` trong `dataset_provenance_manifest.json`):**
   Để đảm bảo các lỗi không sinh bản ghi kiểm toán (`F6` — lỗi hạ tầng lưu trữ DB hoặc `F7` — caller hủy chủ động request) **không bao giờ bị "tàng hình"** khi lọc $\mathcal{M}_{\text{valid}}$ phục vụ phép nối 1:1, biên bản nguồn gốc `dataset_provenance_manifest.json` bắt buộc ghi nhận đầy đủ 7 chỉ số kiểm kê quần thể đo đạc từ $\mathcal{M}$:
   * `measurement_request_count` ($\lvert \mathcal{M} \rvert$): Tổng số request phát ra trong pha Measurement.
   * `completed_decision_count` ($\lvert \mathcal{M}_{\text{valid}} \rvert$): Tổng số request hoàn tất sinh `DecisionId != Guid.Empty`.
   * `caller_cancelled_count` ($\lvert \mathcal{M}_{F7} \rvert$): Số request bị hủy từ phía caller (`IsEligibleForDcr == false`).
   * `persistence_failure_count` ($\lvert \mathcal{M}_{F6} \rvert$): Số request hợp lệ nhưng thất bại trước hoặc trong khi ghi kiểm toán (`IsEligibleForDcr == true && DecisionId == Guid.Empty`).
   * `dcr_eligible_count`: Tổng số mẫu thuộc mẫu số $\text{DCR}$ ($\lvert \mathcal{M} \rvert - \lvert \mathcal{M}_{F7} \rvert$).
   * `dcr_completed_count`: Tổng số mẫu thuộc tử số $\text{DCR}$ (`IsEligibleForDcr && IsSafeCompletedDecision`).
   * `overall_dcr_percent`: $\frac{\text{dcr\_completed\_count}}{\text{dcr\_eligible\_count}} \times 100\%$.

```text
RawMeasurement (M) ──► Manifest DCR Population Accounting (M, M_valid, F6, F7, DCR%)
       │
       │ Filter r.DecisionId != Guid.Empty
       │ + Verify DecisionId uniqueness within run_id:
       │   Count(M_valid) == Count(Distinct(M_valid.DecisionId))
       ▼
M_valid (Unique DecisionIds = K_meas)
       │
       │ 1:1 Bijective Join on DecisionId
       ▼
E_selected (SelectedAuditExecutions)
```

---

## 4. TÁM BẤT BIẾN TRUY XUẤT NGUỒN GỐC DỮ LIỆU (STEP 2B — 8 PROVENANCE INVARIANTS)

Bộ kiểm định `DatasetProvenanceVerifier` bắt buộc kiểm tra tự động toàn bộ **8 bất biến (`INV-P1` $\rightarrow$ `INV-P8`)** trên mọi lượt chạy. Nếu bất kỳ bất biến nào vi phạm, quá trình chuẩn hóa phải báo lỗi tường minh (Fail-Fast):

| Mã Invariant | Tên Bất biến | Biểu thức Kiểm định & Quy tắc Ràng buộc |
| :--- | :--- | :--- |
| **`INV-P1`** | **Per-Run `DecisionId` Uniqueness & Bijective 1:1 Join** | Trong từng `run_id`:<br>1. **Prerequisite — Uniqueness Check (kiểm tra điều kiện tiên quyết TRƯỚC khi kiểm chứng song ánh):** `DecisionId` trong $\mathcal{M}_{\text{valid}}$ ($r.\text{DecisionId} \neq \text{Guid.Empty}$) phải duy nhất tuyệt đối: $\text{Count}(\mathcal{M}_{\text{valid}}) = \text{Count}(\text{Distinct}(\mathcal{M}_{\text{valid}}.\text{DecisionId}))$.<br>2. **1:1 Bijection:** $\forall r \in \mathcal{M}_{\text{valid}}, \exists! e \in \mathcal{E}_{\text{selected}} : r.\text{DecisionId} = e.\text{DecisionId}$ và $\text{Count}(\mathcal{M}_{\text{valid}}) = \lvert \mathcal{E}_{\text{selected}} \rvert$. |
| **`INV-P2`** | **Zero Warm-up & Circuit-Trip Contamination** | • **Primary Selection:** $\mathcal{E}_{\text{selected}}$ được xác định thuần túy bằng phép lọc tập khóa $\mathcal{K}_{\text{meas}}$ ($\text{MeasurementDecisionIds}$), hoàn toàn độc lập với chuỗi ký tự.<br>• **Secondary Contamination Check:** $\forall e \in \mathcal{E}_{\text{selected}}$, kiểm tra bổ sung `e.IdempotencyKey` khớp mẫu `{RunId}-REQ-{6-digit}` và tuyệt đối không chứa `"WARMUP-"` hoặc `"TRIP-"`.<br>*(Caveat: Quy tắc chuỗi `IdempotencyKey` chỉ đóng vai trò lớp kiểm tra phụ thứ cấp, giữ tương thích ngược với 16,400 mẫu của Tuần 8).* |
| **`INV-P3a`** | **SHA-256 RequestHash Format Integrity** | $\forall e \in \mathcal{E}_{\text{selected}}$: Chuỗi `e.RequestHash` không rỗng và khớp chính xác biểu thức chính quy `^[0-9a-f]{64}$` (64 ký tự hex chữ thường chuẩn SHA-256). |
| **`INV-P3b`** | **RFC 8785 Canonical RequestHash Determinism** | • Trên mọi cặp $(r, e)$ đã kết nối: `e.RequestHash == r.ExpectedRequestHash` (đối chiếu trực tiếp với `RequestHasher.ComputeHash(payload)`).<br>• Trên các kịch bản dùng chung khóa chuẩn hóa (`G2`, `C2`), toàn bộ 100% bản ghi trong lượt chạy phải sinh ra duy nhất một giá trị `RequestHash` ($\lvert \{e.\text{RequestHash} \mid e \in \mathcal{E}_{\text{selected}}\} \rvert = 1$). |
| **`INV-P4`** | **Client-to-Ledger Telemetry Consistency** | Trên mọi cặp $(r, e)$ đã kết nối (kiểm chứng trên cả Schema 2.1 và Schema 2.2):<br>$r.\text{ExecutionType} = e.\text{ExecutionType} \land r.\text{FallbackUsed} = e.\text{FallbackUsed} \land r.\text{CacheHit} = e.\text{CacheHit}$. |
| **`INV-P5`** | **Fallback Safety Invariant (Engine vs. Phase 4 Policy)** | $\forall e \in \mathcal{E}_{\text{selected}}$ khi $e.\text{FallbackUsed} = \text{true}$:<br>• **Engine-Level Invariants:** $e.\text{ExecutionType} = \text{"FALLBACK"} \land e.\text{CacheHit} = \text{false}$.<br>• **Phase 4 Reference Fallback Policy (`FallbackDecisionStrategy`):** $e.\text{NeedApproval} = \text{true}$ *(chính sách dự phòng tham chiếu của Phase 4; các chiến lược dự phòng trong tương lai có thể cấu hình kết quả mặc định khác mà không phá vỡ bất biến kiến trúc Engine)*. |
| **`INV-P6`** | **Immutable 9-Field Machine Snapshot + `NeedApproval` Under Override** | Trên toàn bộ các ca kiểm định trong `governance_audit_dataset` (`G-A, G-B, G-C1, G-C2`), cả **9 thuộc tính Machine Snapshot** (`DecisionId`, `ReservationId`, `StrategyName`, `StrategyVersion`, `ExecutionType`, `ExecutionTimeMs`, `RequestHash`, `ResponsePayload`, `CreatedAt`) cùng **trường trạng thái quyết định bất biến `NeedApproval`** tại thời điểm $t_0$ và $t_1$ phải bất biến tuyệt đối ($\text{Snapshot}_{t_0}(e) = \text{Snapshot}_{t_1}(e)$ và $\text{NeedApproval}_{t_0} = \text{NeedApproval}_{t_1}$). |
| **`INV-P7`** | **Governance State Transition (`POSITIVE_CONTROL`) & Guard Preservation (`NEGATIVE_CONTROL`)** | • **Với `POSITIVE_CONTROL` (`G-A, G-B`, `override_http_status == 200`):**<br>  - `IsOverridden`: $\text{false} \rightarrow \text{true}$<br>  - `OverriddenBy`: $\text{null/empty} \rightarrow \text{non-empty}$<br>  - `OverrideReason`: $\text{null/empty} \rightarrow \text{non-empty}$<br>  - `Version`: $1 \rightarrow 2$ (`version_transition == "1->2"`)<br>  - `UpdatedAt`: $e.\text{UpdatedAt}_{t_1} \ge e.\text{UpdatedAt}_{t_0}$ *(với $e.\text{UpdatedAt}_{t_0} = e.\text{CreatedAt}_{t_0}$ theo vòng đời khởi tạo của `AuditTimestampInterceptor`)*.<br>• **Với `NEGATIVE_CONTROL` (`G-C1, G-C2`, `override_http_status == 400`):**<br>  - Lệnh ghi đè bất hợp lệ bị từ chối và cả 5 trường quản trị giữ nguyên không đổi giữa $t_0$ và $t_1$: `IsOverridden_t1 == IsOverridden_t0`, `OverriddenBy_t1 == OverriddenBy_t0`, `OverrideReason_t1 == OverrideReason_t0`, `Version_t1 == Version_t0` (`version_transition` là `"2->2"` cho `G-C1` và `"1->1"` cho `G-C2`), và `UpdatedAt_t1 == UpdatedAt_t0`.<br>  *(Lưu ý: Bộ kiểm định `DatasetProvenanceVerifier` đối chiếu trực tiếp cả 5 trường trên thực thể `t0/t1` trong bộ nhớ và xuất biên bản vào `dataset_provenance_manifest.json`, trong khi tệp CSV/JSONL lưu các cột bằng chứng chuyển trạng thái tại mục 2.3).* |
| **`INV-P8`** | **Pending Queue Hot-Index Lifecycle Consistency** | • Với `G-A, G-B` (`NeedApproval = true`, `IsOverridden_t0 = false`): Bản ghi bắt buộc có mặt trong kết quả `GET /api/v1/decisions/pending-approvals` tại $t_0$ (`was_in_pending_queue_t0 == true`) và tự động rút khỏi danh sách tại $t_1$ ngay sau khi ghi đè thành công (`in_pending_queue_t1 == false`).<br>• Với `G-C2` (`NeedApproval = false`): Bản ghi không bao giờ xuất hiện trong hàng đợi chờ duyệt ở cả $t_0$ và $t_1$ (`was_in_pending_queue_t0 == false && in_pending_queue_t1 == false`). |

---

## 5. DANH MỤC HIỆN VẬT ĐẦU RA CỦA TUẦN 9 (WEEK 9 DELIVERABLES MANIFEST)

Khi thực thi đường ống chuẩn hóa dữ liệu (`TADSS.BenchmarkRunner`), các tệp hiện vật sau được xuất tự động vào thư mục `docs/research/datasets/`:

1. `docs/research/datasets/resilience_evaluation_dataset.csv` (& `.jsonl`) — Bộ dữ liệu đánh giá khả năng chịu lỗi (23 cột).
2. `docs/research/datasets/latency_caching_dataset.csv` (& `.jsonl`) — Bộ dữ liệu đánh giá độ trễ và bộ nhớ đệm (19 cột).
3. `docs/research/datasets/governance_audit_dataset.csv` (& `.jsonl`) — Bộ dữ liệu bằng chứng chuyển trạng thái quản trị kết quả quyết định và kiểm chứng nhật ký kiểm toán 2 tầng (31 cột).
4. `docs/research/datasets/dataset_provenance_manifest.json` — Biên bản kiểm định tự động 8 bất biến `INV-P1` $\rightarrow$ `INV-P8` cùng thống kê số lượng dòng, mã băm SHA-256 của từng tệp dataset.
5. `docs/research/datasets/WEEK9_DATASET_NORMALIZATION_REPORT.md` — Báo cáo tổng hợp kết quả chuẩn hóa dữ liệu và kiểm chứng câu hỏi nghiên cứu RQ3 phục vụ Chương 4 Báo cáo Đồ án 2.
