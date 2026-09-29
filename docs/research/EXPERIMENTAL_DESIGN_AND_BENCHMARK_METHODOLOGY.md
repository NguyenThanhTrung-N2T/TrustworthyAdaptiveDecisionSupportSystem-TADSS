# 🔬 TADSS Experimental Design & Benchmark Methodology Specification

> **Tài liệu Đặc tả Thiết kế Thực nghiệm, Phương pháp Benchmark & Chuẩn hóa Dataset**  
> **Áp dụng cho:** **Đồ án 2 — Nền tảng TADSS Decision Runtime Engine (Phase 4 — Weeks 8 & 9)**  
> **Ranh giới Phạm vi (Scope Lock):** Toàn bộ thực nghiệm trong tài liệu này đánh giá **hạ tầng kỹ thuật phần mềm của TADSS Runtime Engine** (đường ống chịu lỗi Polly v8, bộ nhớ đệm chuẩn hóa RFC 8785 `Request-Hash Cache` L1/L2, và cơ chế lưu vết kiểm toán đồng bộ `Append-Only Audit Trail`) trên các chiến lược quy tắc nghiệp vụ tất định (`DefaultDecisionStrategy` / `MockAlternativeDecisionStrategy`) và các chiến lược tiêm lỗi có kiểm soát. Thực nghiệm Đồ án 2 **hoàn toàn không sử dụng hay đánh giá các mô hình AI/ML** (các mô hình học máy thuộc phạm vi mở rộng của Khóa luận tốt nghiệp ở giai đoạn sau, theo mục 5.2 của [`PROPOSAL_DO_AN_2.md`](../planning/PROPOSAL_DO_AN_2.md)).  
> **Mục tiêu Thực nghiệm:** Đo lường thực nghiệm latency, throughput và decision continuity của TADSS Runtime dưới các mức concurrency ($VUs \in \{1, 10, 50, 100\}$) và các trạng thái cache/resilience được kiểm soát chặt chẽ, từ đó kiểm chứng liệu các tiêu chí chấp nhận (Acceptance Criteria) về hiệu năng và tính liên tục quyết định có đạt được trong môi trường thử nghiệm hay không *(Không giả định trước kết quả thực nghiệm)*.

---

## 1. CÂU HỎI NGHIÊN CỨU KỸ THUẬT, CHỈ SỐ ĐỊNH LƯỢNG & PHÂN BIỆT NGÂN SÁCH LÝ THUYẾT VS. THỰC NGHIỆM

Thiết kế thực nghiệm của TADSS được dẫn dắt bởi 3 Câu hỏi Nghiên cứu Kỹ thuật (Engineering Research Questions — RQ) và nguyên tắc: **Không thiết kế benchmark để ép giả thuyết đúng; thiết kế benchmark để kiểm chứng giả thuyết bằng dữ liệu thực nghiệm.**

### 1.1. Phân biệt Ngân sách Cấu hình Lý thuyết (`420ms`) và Độ trễ Thực nghiệm Đo đạc ($P_{50}/P_{90}/P_{95}/P_{99}/\text{Max}$)

Công thức ngân sách cấu hình trong Polly v8:
$$\text{Configured Attempt-and-Delay Budget} = 3 \times 120\text{ms} + 2 \times 30\text{ms} = 420\text{ms} < 500\text{ms}\ (\text{Outer Timeout})$$
chỉ chứng minh **ràng buộc cấu hình kiến trúc (theoretical/configured resilience budget)** nằm dưới ngưỡng Outer Timeout `500ms`. Công thức này **chưa chứng minh** kịch bản $R_3$ thực tế dưới tải đồng thời $100\text{ VUs}$ luôn đạt $\le 500\text{ms}$ (do còn phụ thuộc vào thread-pool scheduling, HTTP serialization, và database persistence contention).

Do đó, Tuần 8 tách bạch rõ hai khái niệm:
* **Configured Resilience Budget (`420ms` / `500ms`):** Ngưỡng cắt cấu hình của chuỗi Polly v8.
* **Empirical Measured Latency ($P_{50}, P_{90}, P_{95}, P_{99}, \text{Max}$):** Độ trễ thực tế đo được ở từng mức tải $VUs \in \{1, 10, 50, 100\}$, dùng để đối chiếu và đánh giá với tiêu chí SLA/SC1/SC2.

---

### 🔬 RQ1 (Resilience & Decision Continuity):
> *Khi chiến lược ra quyết định (`IReservationDecisionStrategy`) gặp sự cố (quá tải, ném lỗi hoặc timeout) ở các mức tải đồng thời khác nhau, cơ chế Resilience (Retry + Circuit Breaker + Fallback) của TADSS Runtime duy trì tính liên tục của luồng ra quyết định với Tỷ lệ Duy trì Quyết định (Decision Continuity Rate — DCR) và phân phối độ trễ ($P_{50}, P_{90}, P_{95}, P_{99}, \text{Max}$) thực tế như thế nào so với tiêu chí chấp nhận đã định nghĩa?*

* **Công thức Định lượng Chỉ số:**
  $$\text{DCR} = \frac{\text{Completed Safe Decisions (Normal } \cup \text{ Fallback)}}{\text{Eligible Decision Requests}} \times 100\%$$
* **Bảng Quy ước Ngữ nghĩa Mẫu số (DCR Denominator Semantics):**

| Kịch bản (Scenario) | Hoàn tất Quyết định An toàn (Numerator) | Thuộc Mẫu số DCR (Eligible Denominator) | Giải thích Ngữ nghĩa |
| :--- | :---: | :---: | :--- |
| **$R_0$ — Normal Strategy** | Có (`AUTOMATED`) | **Có** | Thực thi chiến lược bình thường thành công. |
| **$R_1$ — Retry Recovery (`F1`)** | Có (`AUTOMATED`) | **Có** | Attempt 1 timeout, Retry thành công với Strategy gốc. |
| **$R_2$ — Circuit OPEN (`F4`)** | Có (`FALLBACK`) | **Có** | Mạch ngắt đang `OPEN`, fast-fail sang `FallbackDecisionStrategy` (`NEED_APPROVAL`). |
| **$R_3$ — Retry Exhaustion (`F2/F3`)** | Có (`FALLBACK`) | **Có** | Cạn kiệt 3 attempts, chuyển sang `FallbackDecisionStrategy` (`NEED_APPROVAL`). |
| **$F_6$ — Persistence Failure** | **Không** (HTTP 500/503) | **Có** | Lỗi ghi cơ sở dữ liệu kiểm toán (Policy A Fail-Closed) làm gián đoạn phản hồi quyết định. |
| **$F_7$ — Caller Cancellation** | **Không** (Aborted) | **KHÔNG (Loại trừ)** | Hành vi hủy chủ động từ phía client, không phản ánh sự cố của TADSS Runtime. |

> **Nguyên tắc Đánh giá:** Tuần 8 **không mặc định trước $\text{DCR} = 100\%$** hay **$R_2 < 10\text{ms}$**, mà thực hiện đo đạc $\text{DCR}$ thực tế và phân vị $P_{50}/P_{90}/P_{95}/P_{99}/\text{Max}$ của từng kịch bản $R_0..R_3$, sau đó mới đánh giá kết quả thực nghiệm đối chiếu với tiêu chí thiết kế.

---

### 🔬 RQ2 (Caching Overhead & Latency Envelope):
> *Cơ chế tuần tự hóa chuẩn hóa RFC 8785 kết hợp băm SHA-256 (Request Hash) và bộ nhớ đệm tạo ra chi phí phát sinh khi trượt cache (Cache Miss Overhead) và mức chênh lệch độ trễ khi trúng cache (Cache Benefit) thực tế là bao nhiêu tại các phân vị ($P_{50}, P_{90}, P_{95}, P_{99}, \text{Max}$) và mức tải $VUs \in \{1, 10, 50, 100\}$?*

* **Công thức Đo lường Thực nghiệm (Không đặt giả định trước về độ lớn):**
  $$\text{Cache Miss Overhead} = \text{Latency}(G_1) - \text{Latency}(G_0)$$
  $$\text{Cache Benefit} = \text{Latency}(G_0) - \text{Latency}(G_2)$$
  *(Nếu quá trình chuẩn hóa RFC 8785 + SHA-256 + Redis I/O khiến $G_1 - G_0$ đáng kể ở một số điều kiện mạng/concurrency, đó chính là phát hiện thực nghiệm có giá trị khoa học để phân tích đánh đổi kiến trúc).*

---

### 🔬 RQ3 (Audit Provenance & Telemetry Dataset Extraction):
> *Kiến trúc kiểm toán hai tầng (Immutable Machine Snapshot $\oplus$ Mutable Governance Metadata) kết hợp với Run Metadata của giao thức thực nghiệm có cung cấp đầy đủ khả năng truy xuất nguồn gốc (traceability & provenance) để tái tạo các tập dữ liệu chuẩn hóa phục vụ đánh giá thực nghiệm Resilience, Caching và Governance trong Đồ án 2 (đồng thời sẵn sàng làm dữ liệu nền tảng nếu tái sử dụng ở giai đoạn sau) hay không?*

---

## 2. NĂM TẦNG THỰC NGHIỆM CÓ KIỂM SOÁT (5-LAYER EXPERIMENTAL ARCHITECTURE — 19 SCENARIOS)

Thực nghiệm Tuần 8 được cấu trúc thành **5 tầng thực nghiệm trực giao (Layer A $\rightarrow$ Layer E, tổng cộng 19 kịch bản)** với sự cô lập trạng thái nghiêm ngặt (hỗ trợ chạy chế độ rút gọn `--suite Core7` gồm 28 lượt chạy hoặc chế độ toàn diện mặc định `--suite Full19` gồm 76 lượt chạy):

### Layer A — Cache Baseline Experiment ($G_0 \rightarrow G_1 \rightarrow G_2$)

```text
G0 — No Cache (Direct)          G1 — Cache Miss Path                G2 — Cache Hit Path
──────────────────────          ────────────────────                ───────────────────
Request                         Request                             Request
  ↓                               ↓                                   ↓
Strategy                        Canonicalization + SHA-256          Canonicalization + SHA-256
  ↓                               ↓                                   ↓
Persistence                     Cache MISS                          Cache HIT (Skip Strategy)
  ↓                               ↓                                   ↓
Response                        Strategy                            Persistence (Audit CACHE_HIT)
                                  ↓                                   ↓
                                Cache SET                           Response
                                  ↓
                                Persistence (Audit AUTOMATED)
                                  ↓
                                Response
```

* **Khóa Trạng thái Thực nghiệm (Experimental State Lock cho $G_0, G_1, G_2$):**
  Để phép trừ $G_1 - G_0$ và $G_0 - G_2$ phản ánh đúng hiệu ứng của tầng Cache mà không bị nhiễu bởi biến ngoại lai, cả 3 nhóm bắt buộc phải khóa cố định:
  1. Cùng cấu trúc Payload và phân phối dữ liệu đầu vào.
  2. Cùng Strategy (`DefaultDecisionStrategy`) và cùng `StrategyVersion` (`1.0.0`).
  3. Cùng trạng thái Database, trạng thái Cache Provider, và cùng `TtlHours = 24`.
  4. Cùng cấu hình Server, cùng máy vật lý/môi trường và cùng mức Concurrency ($VUs$).
  5. **Quy tắc Warm-up riêng cho $G_2$:** Trước khi bước vào pha Measurement của $G_2$, hệ thống bắt buộc phải nạp trước (pre-warm) bản ghi Cache tương ứng trong pha Warm-up để 100% request trong pha Measurement của $G_2$ là `CACHE_HIT` thuần túy (không lẫn kết nối khởi tạo hay request `MISS` đầu tiên).
* **Chỉ số đo ở Layer A:** $P_{50}, P_{90}, P_{95}, P_{99}, \text{Max}, \text{Mean}$, Throughput (RPS), Cache Hit Ratio (%), $G_1 - G_0$, và $G_0 - G_2$.

---

### Layer B — Core Resilience Experiment ($R_0, R_1, R_2, R_3$ — Cô lập Trạng thái $R_2 \neq R_3$)

| Kịch bản | Cơ chế Thực thi trong Kịch bản | Trạng thái Mạch (Circuit State) & Cô lập Thực nghiệm |
| :--- | :--- | :--- |
| **$R_0$ — Normal** | Chiến lược chạy bình thường không tiêm lỗi. | Circuit `CLOSED` xuyên suốt. |
| **$R_1$ — Retry Recovery** | Attempt 1 bị treo quá `InnerTimeoutMs (120ms)` $\rightarrow$ Polly hủy Attempt 1, chờ `30ms` $\rightarrow$ Attempt 2 trả kết quả thành công. | Circuit được giữ `CLOSED` để đo thuần túy chi phí phục hồi qua Retry (`1 retry / request`). |
| **$R_2$ — Circuit Open** | Kiểm tra **Fast-Fail + Fallback khi Circuit đã ở trạng thái `OPEN`**. | **Pre-tripped `OPEN` State:** Mạch ngắt được kích hoạt sang trạng thái `OPEN` ngay từ bước Setup/Warm-up và giữ `OPEN` trong suốt pha đo đạc để đo thời gian bypass trực tiếp sang `FallbackDecisionStrategy`. |
| **$R_3$ — Retry Exhaustion** | Kiểm tra **Failure Propagation qua cạn kiệt Retry (3 attempts) $\rightarrow$ Fallback**. | **Isolated from $R_2$:** Ngưỡng mở mạch được cấu hình cô lập trong kịch bản $R_3$ để mạch **không tự động đóng cắt thành $R_2$ giữa chừng**, đảm bảo 100% request trong $R_3$ thực sự đi hết cả 3 attempts + 2 retry delays trước khi chạm `FallbackDecisionStrategy`. |

* **Chỉ số đo ở Layer B:** Phân phối độ trễ ($P_{50}, P_{90}, P_{95}, P_{99}, \text{Max}, \text{Mean}$), Throughput (RPS), Completion Rate, **DCR (%)**, Fallback Rate (%), Retry Count, Circuit State, và Timeout Occurrence.

---

### Layer C — Real-World Cache Pathologies ($C_0, C_1, C_2, C_3$)

| Kịch bản | Cơ chế Mô phỏng trong Harness | Mục tiêu Kiểm chứng Thực nghiệm |
| :--- | :--- | :--- |
| **$C_0$ — Partial Cache Hit (50%)** | `Mixed50PercentHit` (50% request trùng khóa đã pre-warm, 50% request khóa mới). | Phân phối độ trễ hỗn hợp thực tế sau khi khởi động lại Cache hoặc mở ca đặt chỗ mới (`Hit Ratio = 50%`). |
| **$C_1$ — Slow Redis WAN Latency** | `NetworkLatencyCacheDecorator` tiêm độ trễ mạng WAN (`25–45ms` RTT mỗi thao tác `Get`/`Set`). | Định lượng chi phí mạng WAN thực tế khi Redis Cloud đặt khác vùng mạng so với Runtime API. |
| **$C_2$ — Cache Thundering Herd** | `IdenticalCanonicalKey` trên khóa lạnh kết hợp cửa sổ tính toán `15–20ms` (`ThunderingHerdColdComputeDelay`). | Đo số lượng luồng đồng thời đâm xuyên qua cùng 1 khóa lạnh tại thời điểm $t=0$ trước khi luồng đầu tiên kịp ghi Cache (Cache Stampede). |
| **$C_3$ — Cache Eviction Thrashing** | `BoundedCapacityCacheService` giới hạn `32` entries dưới tập khóa làm việc xoay vòng `128` keys. | Đo sự suy giảm tỷ lệ trúng đích (Cache Hit Ratio) khi tập khóa hoạt động vượt quá dung lượng bộ nhớ đệm (LRU Eviction Thrashing). |

---

### Layer D — Strategy & Persistence Performance ($D_0, D_1, D_2$)

| Kịch bản | Cơ chế Mô phỏng trong Harness | Mục tiêu Kiểm chứng Thực nghiệm |
| :--- | :--- | :--- |
| **$D_0$ — Heavy Multi-Rule Strategy** | `VariableDelayBenchmarkStrategy` mô phỏng tập quy tắc nghiệp vụ phức tạp chạy sát ngưỡng cắt (`80–105ms` vs. `InnerTimeoutMs = 120ms`). | Kiểm định hành vi hệ thống khi Strategy thực thi nặng nhưng vẫn hợp lệ dưới ngưỡng cắt 120ms. |
| **$D_1$ — DB Audit Write Contention** | `SimulatedDbContentionInterceptor` tiêm độ trễ ghi `SavingChangesAsync` (`20–40ms`). | Chứng minh độ trễ tranh chấp khóa ghi kiểm toán đồng bộ xuống DB chỉ cộng vào latency đầu-cuối mà **không** kích hoạt nhầm Polly Retry của tầng Strategy. |
| **$D_2$ — Bimodal Strategy Jitter** | `BimodalJitterBenchmarkStrategy` (80% request nhanh `5ms` / 20% request chậm `95ms`). | Chứng minh sự phân tách giữa trung vị $P_{50}$ và độ trễ đuôi $P_{95}/P_{99}$ dưới tải biến thiên hai đỉnh. |

---

### Layer E — Combined Production Stress & Self-Healing Recovery ($E_0, E_1, E_2, E_3, E_4$)

| Kịch bản | Cơ chế Mô phỏng trong Harness | Mục tiêu Kiểm chứng Thực nghiệm |
| :--- | :--- | :--- |
| **$E_0$ — Composite Production Mix** | 80% Cache Hit + 20% Cache Miss (Strategy `5–10ms` + DB Audit `2–5ms`). | Đường cơ sở vận hành hỗn hợp chuẩn Production đạt mục tiêu SLA $P_{95} < 150\text{ms}$. |
| **$E_1$ — Compound WAN Cache + Heavy Strategy** | 50% Cache Hit với WAN Cache (`15–25ms`) + Heavy Strategy (`65–85ms`). | Đo hiệu ứng cộng hưởng độ trễ giữa tầng bộ đệm từ xa và tầng tính toán chính sách nặng. |
| **$E_2$ — Cascading Triple Stress** | 100% Cache Miss với WAN Cache (`15–25ms`) + Heavy Strategy (`90–110ms`) + Slow DB Audit (`15–25ms`). | Kiểm tra sức chịu đựng của ngân sách `OuterTimeoutMs = 500ms` khi cả 3 tầng hạ tầng cùng suy giảm hiệu năng đồng thời. |
| **$E_3$ — Intermittent Transient Faults** | `IntermittentFaultBenchmarkStrategy` gây lỗi thoáng qua ở ~14.3% request tại Attempt 1 và hồi phục ở Attempt 2. | Kiểm chứng khả năng tự chữa lành (Self-Healing) bằng Polly Retry mà không cần kích hoạt Fallback (`DCR = 100%`, `Fallback = 0%`). |
| **$E_4$ — Circuit Breaker Recovery** | `RecoveringCircuitBenchmarkStrategy` mở mạch ở Warm-up, chờ qua `BreakDuration`, sau đó phục hồi ở Measurement. | Kiểm chứng chu trình khép kín `OPEN` $\rightarrow$ `HALF-OPEN` (Probe OK) $\rightarrow$ `CLOSED` (`RECOVERED_CLOSED`). |

---

### Ma trận Đồng thời 19 × 4 (Concurrency Scaling Matrix: $VUs \in \{1, 10, 50, 100\}$)

Mỗi kịch bản trong 19 kịch bản ($G_0..G_2$, $R_0..R_3$, $C_0..C_3$, $D_0..D_2$, $E_0..E_4$) đều được thực thi độc lập qua đủ **4 mức tải đồng thời**:
$$\text{Concurrency Levels (VUs)} = \{1, 10, 50, 100\}$$

Tạo thành **Ma trận Thực nghiệm Toàn diện 76 Lượt chạy ($19 \text{ Scenarios} \times 4 \text{ VU Levels}$)** (hoặc **28 Lượt chạy** khi chọn chế độ `--suite Core7`).

---

## 3. GIAO THỨC THỰC NGHIỆM CÓ KIỂM SOÁT (CONTROLLED EXPERIMENTAL PROTOCOL)

### 3.1. Ranh giới Kiến trúc của `TADSS.BenchmarkRunner` (Non-Intrusive Laboratory Harness)
Công cụ `tests/TADSS.BenchmarkRunner` được thiết kế như một **Laboratory Harness độc lập** giao tiếp với `TADSS.Runtime.Api` qua HTTP, tuyệt đối **không làm biến đổi mã nguồn Production Runtime** để phục vụ benchmark:

```text
                    ┌─────────────────────────────────────────┐
                    │         TADSS.BenchmarkRunner           │
                    │  (Non-Intrusive Laboratory Harness)     │
                    │                                         │
                    │  • Scenario & State Controller          │
                    │  • Concurrent HttpClient Load Generator │
                    │  • High-Resolution Stopwatch Telemetry  │
                    │  • Statistical Result Aggregator        │
                    │  • Traceable JSON / CSV / MD Exporter   │
                    └────────────────────┬────────────────────┘
                                         │ HTTP (POST /api/v1/decisions)
                                         ▼
                    ┌─────────────────────────────────────────┐
                    │            TADSS.Runtime.Api            │
                    │          (Production Runtime)           │
                    │                                         │
                    │  • Decision Pipeline & Strategy         │
                    │  • Request-Hash Cache (L2/L1)           │
                    │  • Polly v8 5-Layer Resilience          │
                    │  • Synchronous Audit Persistence        │
                    └─────────────────────────────────────────┘
```

### 3.2. Định dạng Siêu dữ liệu Lượt chạy (Traceable Run Metadata Schema)
Mỗi lượt chạy $(Scenario \times Concurrency)$ bắt buộc phải sinh kèm một bản ghi **Run Metadata** chuẩn hóa để bảo đảm tính truy vết (traceability) từ số liệu thô lên bảng tổng hợp Chương 3 / Chương 4:

```json
{
  "run_id": "EXP-W8-20260927-R3-VU100",
  "timestamp_utc": "2026-09-27T14:00:00Z",
  "scenario": "R3",
  "experiment_layer": "LayerB_Resilience",
  "concurrency": 100,
  "total_requests": 1000,
  "warmup_requests": 200,
  "strategy": "ExhaustiveTimeoutBenchmarkStrategy",
  "strategy_version": "1.0.0",
  "cache_state": "bypassed",
  "redis_state": "available",
  "database_state": "seeded_isolated",
  "environment_profile": "ControlledSelfHosted",
  "outer_timeout_ms": 500,
  "inner_timeout_ms": 120,
  "retry_count": 2,
  "retry_delay_ms": 30
}
```

### 3.3. Chuỗi Xử lý Dữ liệu Thực nghiệm (End-to-End Evidence Pipeline)
1. **Run Metadata + Raw Measurements:** Ghi nhận từng request (`run_id`, `request_index`, `scenario`, `concurrency`, `client_elapsed_ms`, `server_execution_time_ms`, `http_status`, `decision_status`, `execution_type`, `fallback_used`, `cache_hit`, `failure_reason`).
2. **Statistical Aggregation:** Tính toán phân vị nội suy chuẩn xác ($P_{50}, P_{90}, P_{95}, P_{99}, \text{Max}, \text{Mean}, \text{StdDev}$), Throughput (RPS), Cache Hit Ratio, $G_1 - G_0$, $G_0 - G_2$, DCR (%), Fallback Rate (%).
3. **Structured Export:** Xuất ra `benchmark_runs_metadata.json`, `benchmark_summary.csv`, `benchmark_raw_samples.csv` và báo cáo học thuật `WEEK8_BENCHMARK_REPORT.md` tại `docs/research/benchmarks/`.

---

## 4. CHUẨN HÓA TẬP DỮ LIỆU KIỂM TOÁN & THỰC NGHIỆM CHO TUẦN 9 (TELEMETRY DATASET PIPELINE)

> **📌 Đặc tả Đóng băng Chi tiết (Frozen Specification v1.0):** Xem toàn văn cấu trúc cột, hợp đồng kết nối 1-1 và 8 bất biến truy xuất nguồn gốc tại [`DATASET_SCHEMA_SPECIFICATION_V1.md`](./DATASET_SCHEMA_SPECIFICATION_V1.md).

Để **không làm phình hay ô nhiễm Database Schema của Production** (`Zero Production Schema Pollution`), dữ liệu kiểm toán thực tế từ bảng `tadss.decision_executions` được thu thập trực tiếp trong vòng đời lượt chạy (`In-Vivo Harvest` trước khi `ScenarioHostController.DisposeAsync()` giải phóng In-Memory DB) và kết nối 1-1 với `RawMeasurementRecord` $\oplus$ `RunMetadata` qua khóa định danh logic `DecisionId`.

Từ tập bản ghi đã qua kiểm định nguồn gốc, **Dataset Normalizer** (`tests/TADSS.BenchmarkRunner/Normalization/`) xuất **3 bộ dữ liệu chuẩn hóa (`CSV` & `JSONL`)** phục vụ Chương 4 Báo cáo Đồ án 2:
1. **Resilience Evaluation Dataset** (`resilience_evaluation_dataset.csv` — **23 cột**, 11 kịch bản `R0..R3, D0..D2, E1..E4` $\times$ 4 VUs = 44 lượt chạy) — Đối chứng hành vi chịu lỗi, trạng thái mạch ngắt, retry, fallback và tỷ lệ duy trì quyết định ($\text{DCR}$).
2. **Latency & Caching Dataset** (`latency_caching_dataset.csv` — **19 cột**, 8 kịch bản `G0..G2, C0..C3, E0` $\times$ 4 VUs = 32 lượt chạy) — Đối chứng phân phối độ trễ, độ chênh lệch `client_server_latency_delta_ms`, mã băm chuẩn hóa RFC 8785 `request_hash` và hiệu quả bộ nhớ đệm.
3. **Governance & Audit Provenance Dataset** (`governance_audit_dataset.csv` — **31 cột**, thiết kế dưới dạng *Transition-Evidence Dataset* cho cả nhóm `POSITIVE_CONTROL` `G-A, G-B` và `NEGATIVE_CONTROL` `G-C1, G-C2`) — Đối chứng tính toàn vẹn của nhật ký kiểm toán hai tầng (**9-field Machine Snapshot + invariant decision-state field `NeedApproval`** $\oplus$ **5-field Mutable Governance Metadata** của thao tác quản trị kết quả quyết định TADSS) và vòng đời hàng đợi `pending-approvals`.

Toàn bộ 3 bộ dữ liệu đều được kiểm định tự động qua **8 Bất biến Truy xuất Nguồn gốc (`INV-P1` $\rightarrow$ `INV-P8`)** (bao gồm kiểm chứng tính duy nhất của `DecisionId` trong từng `run_id` tại `INV-P1`, định dạng SHA-256 tại `INV-P3a`, tính tất định chuẩn hóa RFC 8785 tại `INV-P3b`, và chuyển trạng thái `UpdatedAt_t1 >= UpdatedAt_t0` tại `INV-P7`) trước khi xuất biên bản `dataset_provenance_manifest.json` và báo cáo `WEEK9_DATASET_NORMALIZATION_REPORT.md`.

