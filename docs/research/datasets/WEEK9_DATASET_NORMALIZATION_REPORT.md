# 📊 TADSS Week 9 — Audit Telemetry & Research Dataset Normalization Report

> **Báo cáo Chuẩn hóa Dữ liệu Nghiên cứu & Kiểm định Nguồn gốc Kiểm toán (Phase 4 — Milestone 4 / Tuần 9)**  
> **Phiên bản Đặc tả Schema:** `1.0.0` ([`DATASET_SCHEMA_SPECIFICATION_V1.md`](../DATASET_SCHEMA_SPECIFICATION_V1.md))  
> **Thời điểm Tạo Báo cáo (UTC):** `2026-09-29T16:39:55Z`  
> **Ranh giới Phạm vi (Scope Lock):** Toàn bộ dữ liệu được trích xuất từ sổ cái kiểm toán `tadss.decision_executions` kết hợp với siêu dữ liệu thực nghiệm của `TADSS.BenchmarkRunner` nhằm đánh giá thực nghiệm hạ tầng kỹ thuật phần mềm (Resilience, Caching, và Two-Tier Audit Ledger kèm Quản trị Kết quả Quyết định Human-in-the-Loop). Hoàn toàn **không sử dụng hay chứa các thuộc tính AI/ML** (thuộc phạm vi Khóa luận tốt nghiệp giai đoạn sau).

---

## 1. TỔNG QUAN ĐƯỜNG ỐNG THU HOẠCH & CHUẨN HÓA DỮ LIỆU (IN-VIVO HARVEST SUMMARY)

| Chỉ số Kiểm kê Nguồn gốc (Provenance Metric) | Giá trị Ghi nhận | Giải thích Kỹ thuật |
| :--- | :---: | :--- |
| **Tổng số Lượt chạy Thực nghiệm đã Thu hoạch (`Runs`)** | `76` | Thu hoạch trực tiếp từ `TadssDbContext` trước khi `DisposeAsync()` giải phóng In-Memory DB. |
| **Tổng số Bản ghi trong `tadss.decision_executions`** | `18.672` | Bao gồm cả các bản ghi khởi động (`Warm-up`), mở mạch (`Circuit-Trip`), và đo đạc (`Measurement`). |
| **Số Bản ghi Khởi động / Mở mạch đã Loại trừ** | `2.272` | Bị loại trừ chính xác tuyệt đối bởi bộ lọc tập khóa $\mathcal{K}_{\text{meas}}$ (`INV-P2`). |
| **Số Bản ghi Đo đạc Hợp lệ được Kết nối 1:1 ($\lvert \mathcal{E}_{\text{selected}} \rvert$)** | `16.400` | Khớp song ánh 1:1 với `RawMeasurementRecord` qua khóa `DecisionId` (`INV-P1`). |
| **Số Ca Kiểm định Vòng đời Quản trị (`G-A, G-B, G-C1, G-C2`)** | `50` | Gồm các ca `POSITIVE_CONTROL` (ghi đè hợp lệ) và `NEGATIVE_CONTROL` (chặn ghi đè trái phép). |
| **Trạng thái Kiểm định 8 Bất biến Nguồn gốc (`INV-P1..P8`)** | **`100% PASSED`** | Kiểm định tự động trước khi xuất hiện vật CSV/JSONL. |

---

## 2. KẾT QUẢ KIỂM ĐỊNH 8 BẤT BIẾN TRUY XUẤT NGUỒN GỐC (`INV-P1` → `INV-P8`)

| Mã Bất biến | Tên Bất biến Kiểm định | Số mẫu Kiểm tra | Số Vi phạm | Trạng thái | Chi tiết Bằng chứng Thực nghiệm |
| :--- | :--- | :---: | :---: | :---: | :--- |
| **`INV-P1`** | Per-Run DecisionId Uniqueness & Bijective 1:1 Eligible Measurement Join | `16.400` | `0` | **✅ PASS** | 100% of 16400 eligible measurement records across 76 run(s) have unique DecisionIds and 1:1 bijective match in tadss.decision_executions. |
| **`INV-P2`** | Zero Warm-up & Circuit-Trip Contamination | `16.400` | `0` | **✅ PASS** | Filtered out 2272 warm-up/circuit-trip DB rows; 100% of 16400 selected audit rows belong strictly to K_meas and match '{RunId}-REQ-{6-digit}'. |
| **`INV-P3a`** | SHA-256 RequestHash Format Integrity | `16.400` | `0` | **✅ PASS** | All 16400 persisted RequestHash values conform to 64-character lowercase hex SHA-256 format (`^[0-9a-f]{64}$`). |
| **`INV-P3b`** | RFC 8785 Canonical RequestHash Determinism | `16.400` | `0` | **✅ PASS** | 100% of 16400 joined records match RequestHasher.ComputeHash(payload), and single-key scenarios (G2, C2) produce a single deterministic hash. |
| **`INV-P4`** | Client-to-Ledger Telemetry Consistency | `16.400` | `0` | **✅ PASS** | 100% of 16400 joined pairs exhibit exact agreement on ExecutionType, FallbackUsed, and CacheHit between Client telemetry and Server Audit Ledger. |
| **`INV-P5`** | Fallback Safety Invariant (Engine + Phase 4 Policy) | `1.607` | `0` | **✅ PASS** | All 1607 fallback executions satisfy Engine invariants (ExecutionType=='FALLBACK', CacheHit==false) and Phase 4 FallbackDecisionStrategy policy (NeedApproval==true). |
| **`INV-P6`** | Immutable 9-Field Machine Snapshot + NeedApproval Under Override | `50` | `0` | **✅ PASS** | 100% of 50 governance lifecycle cases preserved all 9 Machine Snapshot fields (DecisionId, ReservationId, StrategyName, StrategyVersion, ExecutionType, ExecutionTimeMs, RequestHash, ResponsePayload, CreatedAt) + NeedApproval unchanged between t0 and t1. |
| **`INV-P7`** | Governance State Transition (POSITIVE_CONTROL) & Guard Preservation (NEGATIVE_CONTROL) | `50` | `0` | **✅ PASS** | Verified all 50 governance cases: POSITIVE_CONTROL transitions (IsOverridden false->true, Version 1->2, UpdatedAt_t1 >= UpdatedAt_t0, HTTP 200) and NEGATIVE_CONTROL guard rejections (all 5 mutable fields unchanged, HTTP 400). |
| **`INV-P8`** | Pending Queue Hot-Index Lifecycle Consistency | `50` | `0` | **✅ PASS** | 100% of 50 governance cases satisfied GET /pending-approvals hot-queue membership at t0 and removal/absence at t1. |

---

## 3. DANH MỤC 3 BỘ DỮ LIỆU NGHIÊN CỨU CHUẨN HÓA ĐÃ XUẤT BẢN

| Tên Bộ Dữ liệu | Câu hỏi Nghiên cứu | Số Cột | Số Dòng | Tệp CSV & Mã băm SHA-256 | Tệp JSONL & Mã băm SHA-256 |
| :--- | :--- | :---: | :---: | :--- | :--- |
| **`resilience_evaluation_dataset`** | RQ1 (Resilience & Decision Continuity) | `23` | `7.600` | `resilience_evaluation_dataset.csv`<br>`b72b85120c307565...` | `resilience_evaluation_dataset.jsonl`<br>`d143ad82f3f0b49a...` |
| **`latency_caching_dataset`** | RQ2 (Caching Overhead & Latency Envelope) | `19` | `8.800` | `latency_caching_dataset.csv`<br>`074eedbbd1c97d42...` | `latency_caching_dataset.jsonl`<br>`dc15554947fc470f...` |
| **`governance_audit_dataset`** | RQ3 (Audit Provenance & Decision-Outcome Governance) | `31` | `50` | `governance_audit_dataset.csv`<br>`776479b1acbd62e5...` | `governance_audit_dataset.jsonl`<br>`f972251b50851f48...` |

---

## 4. TỔNG HỢP THỐNG KÊ THEO TỪNG BỘ DỮ LIỆU

### 4.1. Bộ 1 — `resilience_evaluation_dataset` (23 cột — Phục vụ RQ1)
* **Tổng số dòng mẫu:** `7.600` dòng (trải đều qua các kịch bản Layer B `R0..R3`, Layer D `D0..D2`, và Layer E `E1..E4`).
* **Số bản ghi kích hoạt Fallback (`fallback_used == true`):** `1.607` dòng (100% thỏa mãn `execution_type == "FALLBACK"`, `cache_hit == false`, và `need_approval == true` theo `INV-P5`).
* **Tỷ lệ mẫu nằm trong ngân sách Outer Timeout cấu hình (`<= 500ms`):** `7.600 / 7.600` (`100.00%`).

### 4.2. Bộ 2 — `latency_caching_dataset` (19 cột — Phục vụ RQ2)
* **Tổng số dòng mẫu:** `8.800` dòng (trải đều qua các kịch bản Layer A `G0..G2`, Layer C `C0..C3`, và Layer E `E0`).
* **Số bản ghi trúng bộ nhớ đệm (`cache_hit == true`):** `3.920` dòng (100% có `execution_type == "CACHE_HIT"` và `server_execution_time_ms == 0`).
* **Độ chênh lệch độ trễ trung bình (`client_server_latency_delta_ms`):** `14.513 ms` (phản ánh tổng độ trễ quan sát được ngoài tầng Strategy).
* **Số mẫu đạt mục tiêu SLA thực nghiệm `<= 150ms`:** `8.800 / 8.800`.

### 4.3. Bộ 3 — `governance_audit_dataset` (31 cột — Transition-Evidence Dataset phục vụ RQ3)
* **Tổng số ca kiểm định quản trị kết quả quyết định:** `50` ca (`30` ca `POSITIVE_CONTROL` thuộc `G-A, G-B` và `20` ca `NEGATIVE_CONTROL` thuộc `G-C1, G-C2`).
* **Tỷ lệ bảo toàn bất biến 9 trường Machine Snapshot + `NeedApproval` (`t0 == t1`):** `50 / 50` (`100.00%`).
* **Kiểm chứng chuyển trạng thái quản trị (`INV-P7`) & hàng đợi chờ duyệt (`INV-P8`):**
  * `G-A` & `G-B` (`POSITIVE_CONTROL`): `HTTP 200`, `is_overridden: false -> true`, `version_transition: "1->2"`, `was_in_pending_queue_t0: true -> in_pending_queue_t1: false`.
  * `G-C1` (`Duplicate Override Guard`): `HTTP 400`, bảo toàn nguyên trạng phiên bản `version_transition: "2->2"`.
  * `G-C2` (`Non-Approvable Guard`): `HTTP 400`, bảo toàn nguyên trạng phiên bản `version_transition: "1->1"`, không xuất hiện trong `pending-approvals`.

---

## 5. KẾT LUẬN THỰC NGHIỆM CHO CÂU HỎI NGHIÊN CỨU KỸ THUẬT RQ3

Kết quả thu hoạch, kết nối 1:1 và kiểm định tự động 8 bất biến nguồn gốc (`INV-P1` $\rightarrow$ `INV-P8`) chứng minh bằng thực nghiệm rằng:
1. **Zero Production Schema Pollution:** Kiến trúc sổ cái kiểm toán `tadss.decision_executions` kết hợp với khóa định danh logic `DecisionId` cho phép tái tạo hoàn chỉnh cả 3 bộ dữ liệu nghiên cứu chuẩn hóa mà không cần thêm bất kỳ cột phụ trợ thực nghiệm nào vào cơ sở dữ liệu Production.
2. **Two-Tier Audit Ledger Integrity:** Mọi thao tác quản trị ghi đè kết quả quyết định (`POST /api/v1/decisions/{id}/override`) chỉ cập nhật 5 thuộc tính quản trị khả biến (`IsOverridden`, `OverriddenBy`, `OverrideReason`, `Version`, `UpdatedAt`) và giữ nguyên tuyệt đối 100% đối với 9 thuộc tính Machine Snapshot cùng trường `NeedApproval`.
