# 🔬 TADSS Experimental Design & Benchmark Methodology Specification

> **Tài liệu Đặc tả Thiết kế Thực nghiệm, Phương pháp Benchmark & Chuẩn hóa Dataset**  
> **Áp dụng cho:** Khóa luận tốt nghiệp & Nghiên cứu Khoa học (Phase 4 — Weeks 8 & 9)  
> **Mục tiêu:** Chuyển đổi hệ thống phần mềm thành các bằng chứng thực nghiệm định lượng có thể đo lường và phản biện khoa học.

---

## 1. CÂU HỎI NGHIÊN CỨU & CHỈ SỐ ĐỊNH LƯỢNG (RESEARCH QUESTIONS & METRICS)

Thiết kế thực nghiệm của TADSS được dẫn dắt bởi 3 Câu hỏi Nghiên cứu (Research Questions - RQ) và các chỉ số đo lường tường minh:

### 🔬 RQ1 (Resilience & Decision Continuity):
> *Khi các chiến lược đánh giá quyết định (Strategy/Model) gặp sự cố (quá tải, ném lỗi hoặc timeout), cơ chế Resilience (Circuit Breaker + Fallback) có đảm bảo duy trì được tính liên tục của luồng ra quyết định với Tỷ lệ Duy trì Quyết định (Decision Continuity Rate) mong muốn hay không?*

* **Công thức Định lượng Chỉ số:**
  $$\text{Decision Continuity Rate (DCR)} = \frac{\text{Successful Decision Responses (Normal } \cup \text{ Fallback)}}{\text{Eligible Decision Attempts}}$$
* **Quy ước Ngữ nghĩa về Mẫu số (Denominator Semantics):**
  * **Mẫu số (Eligible Decision Attempts):** Bao gồm toàn bộ các request hợp lệ gửi đến hệ thống.
  * **LOẠI TRỪ (Excluded):** Các request bị hủy do phía gọi (*Caller Cancellation - F7*) vì đây là hành vi chủ động của client, không phản ánh sự cố hệ thống.
  * **TÍNH LÀ THÀNH CÔNG (Successful Continuity):** Cả quyết định tính toán bình thường (*Normal Strategy*) và quyết định cứu cánh an toàn (*Deterministic Fallback - F2, F3, F4*) đều được tính là đáp ứng thành công luồng ra quyết định liên tục.
  * **TÍNH LÀ THẤT BẠI (Failed Continuity):** Các lỗi hạ tầng lưu trữ (*DB Persistence Failure - F6*) hoặc vượt quá Outer Timeout Deadline (HTTP 504) làm gián đoạn phản hồi quyết định.

---

### 🔬 RQ2 (Caching Overhead & Latency Envelope):
> *Cơ chế tuần tự hóa chuẩn hóa RFC 8785 kết hợp băm SHA-256 (Request Hash) và Distributed Cache (Redis) mang lại lợi ích giảm độ trễ (Cache Benefit) và chi phí hạ tầng (Cache Miss Overhead) như thế nào trong phong bì độ trễ thực nghiệm ($P_{50}, P_{95}, P_{99}$)?*

* **Công thức Đánh giá:**
  $$\text{Cache Miss Overhead} = \text{Latency}(G_1) - \text{Latency}(G_0)$$
  $$\text{Cache Benefit} = \text{Latency}(G_0) - \text{Latency}(G_2)$$

---

### 🔬 RQ3 (Audit Provenance & Dataset Extraction):
> *Kiến trúc kiểm toán hai tầng (Immutable Machine Snapshot $\oplus$ Mutable Governance Metadata) có cung cấp đủ nguồn gốc dữ liệu (provenance) và thông tin quản trị cần thiết để kết hợp cùng Metadata thực nghiệm tạo thành các tập dữ liệu sơ cấp phục vụ nghiên cứu Resilience, Ablation Study và Governance Analysis hay không?*

---

## 2. THIẾT KẾ THỰC NGHIỆM TRỰC GIAO (ORTHOGONAL EXPERIMENTAL DESIGNS)

Phân tách bài toán thành **2 thí nghiệm độc lập**, đảm bảo **mức tải đồng thời (Concurrency Level - VUs) phải giống hệt nhau khi so sánh giữa các nhóm**:

```text
┌─────────────────────────────────────────────────────────────────────────────────────────────┐
│                              ORTHOGONAL EXPERIMENTAL MATRIX                                 │
├─────────────────────────────────────────────────────────────┬───────────────────────────────┤
│                THÍ NGHIỆM 1: CACHING & HIỆU NĂNG            │     THÍ NGHIỆM 2: ĐỘ CHỊU LỖI │
│                      (Cache Experiment)                     │        (Resilience Experiment)│
├─────────────────────────────────────────────────────────────┼───────────────────────────────┤
│ • G₀ (Direct Execution): Trực tiếp, Không qua Redis.        │ • R₀ (Normal): Thực thi       │
│ • G₁ (Cache Miss Path): Lookup Redis + Strategy execution   │      chiến lược bình thường.  │
│      + Write Redis (Đo Overhead khi Miss).                  │ • R₁ (Recoverable Timeout):   │
│ • G₂ (Cache Hit Path): Lookup Redis + Return Cache          │      Timeout ➔ Retry ➔ Thành  │
│      (Đo Lợi ích khi Hit O(1)).                             │      công với Strategy gốc.   │
│                                                             │ • R₂ (Circuit Breaker OPEN):  │
│                                                             │      Bypass Strategy ➔ Fallback.│
│                                                             │ • R₃ (Retry Exhaustion):      │
│                                                             │      Timeout/Exception ➔ Retry│
│                                                             │      cạn kiệt ➔ Fallback.     │
└─────────────────────────────────────────────────────────────┴───────────────────────────────┘
```

---

## 3. PHƯƠNG PHÁP BENCHMARK & KIỂM SOÁT MÔI TRƯỜNG (BENCHMARK CONTROLS)

Tuân thủ nghiêm ngặt nguyên tắc **"Measure First $\rightarrow$ Optimize Second"**:

### 3.1. Các Yếu tố Kiểm soát Môi trường (Environmental Controls)
Mọi lượt chạy benchmark phải ghi nhận và cố định các thông số:
* **Hardware & Runtime:** CPU Core count, RAM, OS version, .NET 8 SDK / Runtime version.
* **Infrastructure Dependencies:** PostgreSQL 16 version, Redis 7 version (cấu hình memory, connection limits, `RedisTimeoutMs = 150ms`).
* **Cache Identity Invariant vs. TTL Configuration:**
  * *Architectural Invariant:* `RequestHash = SHA256(Canonical(DecisionRequest))` (bảo toàn 100% giá trị thời gian nghiệp vụ, chỉ cắt bỏ sub-second ticks về giây UTC) và `CacheKey = decision:hash:{requestHash}:{strategyName}:v{strategyVersion}`.
  * *Experimental Configuration Parameter:* `Default TTL for benchmark baseline = 24h` (`TtlHours = 24` trong `appsettings.json` — tham số cấu hình thực nghiệm, không phải bất biến kiến trúc cứng).
* **Workload Configuration:** So sánh đối chứng tại các mức Concurrency giống hệt nhau:
  $$\text{Workload Scenarios} = \left\{ \text{VUs} \in \{1, 10, 50, 100\}, \ N \approx 10.000 \text{ requests per condition/VU level} \right\}$$

### 3.2. Quy trình Thực hiện Đo đạc 3 Pha (3-Phase Execution Pipeline)
Nhằm loại trừ sai số do .NET JIT Compilation, Connection Pool Warmup, và Redis lazy initialization:
1. **Pha Khởi động (Warm-up Phase):** Chạy 500 – 1.000 requests giả lập trong 15–30 giây để ổn định JIT cache và pool kết nối. Không tính các request này vào báo cáo.
2. **Pha Đo đạc (Measurement Phase):** Chạy liên tục trong khoảng thời gian xác định hoặc theo số lượng request kiểm soát ($N \approx 10.000$).
3. **Pha Tổng hợp Thống kê (Statistical Aggregation):** Thu thập raw timestamps và tính toán:
   $$\text{Metrics} = \left\{ P_{50}, P_{90}, P_{95}, P_{99}, \text{Max}, \text{Mean}, \text{Throughput (RPS)}, \text{Error Rate (\%)} \right\}$$

---

## 4. CHUẨN HÓA TẬP DỮ LIỆU THỰC NGHIỆM (RESEARCH DATASET PIPELINE)

Để **không làm phình hay ô nhiễm Database Schema của Production**, chúng ta kết hợp bảng Audit gốc với Metadata thực nghiệm thông qua **Dataset Normalizer**:

```text
┌───────────────────────────────────────┐       ┌───────────────────────────────────────┐
│     BẢNG AUDIT PRODUCTION             │       │     METADATA THỰC NGHIỆM              │
│     (tadss.decision_executions)       │       │     (Test Harness / Fault Injector)   │
├───────────────────────────────────────┤       ├───────────────────────────────────────┤
│ • decision_id, reservation_id         │       │ • experiment_id (e.g. EXP-2026-001)   │
│ • strategy_name, strategy_version     │       │ • scenario_id (e.g. CACHE_G1, RESIL_R3)│
│ • execution_type (AUTOMATED/FALLBACK) │   +   │ • run_timestamp, target_environment   │
│ • execution_time_ms                   │       │ • concurrency_level (VUs = 1..100)    │
│ • fallback_used, failure_reason       │       │ • injected_fault_type (None, T/O...)  │
│ • cache_hit, need_approval            │       │ • cache_condition (G0, G1, G2)        │
│ • is_overridden, override_reason      │       │                                       │
│ • created_at, updated_at              │       │                                       │
└───────────────────┬───────────────────┘       └───────────────────┬───────────────────┘
                    │                                               │
                    └───────────────────────┬───────────────────────┘
                                            │
                                            ▼
                        ┌───────────────────────────────────────┐
                        │       RESEARCH DATASET NORMALIZER     │
                        │       (scripts/export_dataset.py)     │
                        └───────────────────┬───────────────────┘
                                            │
                                            ▼
                        ┌───────────────────────────────────────┐
                        │       STANDARDIZED RESEARCH DATASET   │
                        │       (CSV / JSONL for Thesis)        │
                        ├───────────────────────────────────────┤
                        │ • 1. Resilience Evaluation Dataset    │
                        │ • 2. Latency & Caching Dataset        │
                        │ • 3. Ablation & Governance Dataset    │
                        └───────────────────────────────────────┘
```

---

## 5. LỘ TRÌNH THỰC HIỆN TUẦN 8 & TUẦN 9

* **Tuần 8 (Benchmark & Latency Envelope):**
  * Xây dựng kịch bản benchmark tự động cho $G_0, G_1, G_2$ và $R_0, R_1, R_2, R_3$.
  * Thực thi đo đạc trong điều kiện kiểm soát và lập biểu đồ phân phối độ trễ.
* **Tuần 9 (Dataset Export & Ablation Prep):**
  * Xây dựng công cụ `Dataset Normalizer` trích xuất 3 bộ dữ liệu chuẩn hóa.
  * Hoàn thiện báo cáo thực nghiệm làm cơ sở viết Chương 4 (Thực nghiệm & Đánh giá) trong Khóa luận.
