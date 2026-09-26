# 🛡️ TADSS Failure Taxonomy & Resilience Architecture Specification

> **Tài liệu đặc tả Phân loại Lỗi và Kiến trúc Chịu lỗi (Resilience Architecture)**  
> **Áp dụng cho:** TADSS-Runtime Engine (Phase 4 — Weeks 7, 8, 9)  
> **Trạng thái:** *Locked Architectural Baseline for Empirical Fault Injection*  

---

## 1. TỔNG QUAN VÀ NGUYÊN TẮC THIẾT KẾ (CORE PRINCIPLES)

Mục tiêu của tầng Resilience trong TADSS không phải là chứng minh "Zero Downtime" toàn diện của toàn bộ hệ sinh thái đặt sân, mà là chứng minh:

> **Decision-Path Availability & Continuity under Dependency and Strategy Failure**  
> *(Khả năng duy trì luồng ra quyết định liên tục và an toàn khi các phụ thuộc tính toán gặp sự cố)*

### Các Nguyên tắc & Ràng buộc Bất biến (Core Invariants):
1. **Resilience Budget Invariant:**
   $$\text{Total Resilience Execution Budget} < \text{Outer Decision Timeout Budget (500ms)}$$
   *Tổng thời gian thực thi của mọi lần thử (Inner Attempt Timeouts) cộng với độ trễ Retry Backoff Delays bắt buộc phải nằm trong giới hạn của Hard Deadline toàn cục (500ms).*
2. **Failure Domain Isolation & Cache Degradation Invariant:**
   *Sự cố hạ tầng Cache (Redis Unavailable) **TUYỆT ĐỐI KHÔNG ĐƯỢC** kích hoạt Fallback của Strategy, không làm thay đổi kết quả nghiệp vụ, không gây bão Retry (Retry Storm), và không được ghi nhận là lỗi của Strategy.* Khi Redis gặp sự cố:
   - Nếu `EnableFallbackToMemory = true` (mặc định): Hệ thống suy thoái êm sang L1 In-Memory Cache (nếu L1 Miss $\rightarrow$ tính toán bình thường và ghi tạm vào L1; nếu L1 Hit $\rightarrow$ trả kết quả cache tương đương ngữ nghĩa).
   - Nếu không có L1 Cache hoặc L1 Miss: Kích hoạt ngữ nghĩa **Cache Bypass / Miss Semantics** (Strategy tính toán bình thường).
   - **Business-Decision Equivalence:** Dù đọc từ L1 Memory Cache hay tính toán trực tiếp từ Strategy, kết quả nghiệp vụ của quyết định hoàn toàn tương đương.
3. **Mandatory Audit Policy Invariant (Policy A):**
   *Trong Phase 4 baseline, việc lưu trữ bản ghi kiểm toán (`decision_executions`) được coi là một phần không thể tách rời của một phiên ra quyết định thành công. Nếu cơ sở dữ liệu PostgreSQL không thể ghi bản ghi audit $\rightarrow$ Hệ thống từ chối trả quyết định cho client và báo lỗi HTTP 500/503. (Chính sách B - Outbox Queue bất đồng bộ là mở rộng tương lai, không thuộc phạm vi Phase 4).*
4. **Caller Cancellation Invariant:**
   *Khi client chủ động hủy request (`CancellationToken` kích hoạt) $\rightarrow$ Hệ thống dừng xử lý ngay lập tức. Hành vi này **TUYỆT ĐỐI KHÔNG ĐƯỢC** tạo bản ghi quyết định hoàn tất trong bảng `decision_executions` (tránh rác audit), nhưng vẫn được phép ghi log vận hành (Application/Operational Log) kèm `CorrelationId` và thời gian thực thi để phục vụ giám sát hệ thống.*
5. **Deterministic Fallback Semantics (`FallbackDecisionStrategy` $\neq$ `DefaultDecisionStrategy`):**
   *Khi Strategy gặp sự cố quá ngân sách retry hoặc Circuit Breaker ở trạng thái `OPEN`, hệ thống chuyển sang `FallbackDecisionStrategy` (thiết kế có đường dẫn thực thi hữu hạn, không phụ thuộc I/O ngoài, không ném lỗi kể cả khi thiếu ngữ cảnh, có thể dự đoán và kiểm toán) — tách biệt hoàn toàn với `DefaultDecisionStrategy` (chính sách nghiệp vụ cơ sở thông thường). TADSS fallback chỉ chịu trách nhiệm duy trì decision-path continuity và chuyển sang trạng thái `NEED_APPROVAL`. Transactional/business fallback policy, nếu có, thuộc trách nhiệm của reservation platform và nằm ngoài decision authority của TADSS. Bản ghi kiểm toán ghi nhận rõ ràng:*
   $$\text{ExecutionType} = \text{FALLBACK}, \quad \text{FallbackUsed} = \text{TRUE}, \quad \text{FailureReason} = \text{STRATEGY\_TIMEOUT} \mid \text{STRATEGY\_EXCEPTION} \mid \text{CIRCUIT\_BREAKER\_OPEN}$$
6. **Immutability by Governance Invariant:**
   *Dữ liệu quyết định do máy sinh ra (`response_payload`, `strategy_name`, `strategy_version`, `execution_time_ms`, `fallback_used`, `failure_reason`) là **bất biến theo quy tắc quản trị ứng dụng sau khi thực thi (immutable by application/governance invariant after execution)**. Mọi can thiệp ghi đè của con người chỉ cập nhật vào metadata quản trị (`is_overridden`, `overridden_by`, `override_reason`, `version`, `updated_at`).*

---

## 2. PHÂN LOẠI 3 MIỀN LỖI (FAILURE TAXONOMY)

```text
                               ┌───────────────────────────────────┐
                               │       Decision Request Arrives    │
                               └─────────────────┬─────────────────┘
                                                 │
                   ┌─────────────────────────────┼─────────────────────────────┐
                   ▼                             ▼                             ▼
        [1. Decision Dependency]      [2. Infra Persistence]        [3. Caller Behavior]
        - Strategy Timeout            - Redis Connection Drop       - CancellationToken
        - Strategy Exception          - PostgreSQL Connection/Write   Triggered (Client Abort)
        - Circuit Breaker OPEN          Timeout or Refusal                     │
                   │                             │                             ▼
                   ▼                             ▼                    ┌──────────────────┐
        ┌──────────────────────┐        ┌──────────────────┐          │ Prompt Abort     │
        │ Deterministic        │        │ Policy A:        │          │ No Retry         │
        │ Fallback Strategy    │        │ Mandatory Audit  │          │ No Fallback      │
        │ (Audit: FALLBACK)    │        │ (Redis: Bypass)  │          │ App Log only     │
        └──────────────────────┘        └──────────────────┘          └──────────────────┘
```

---

## 3. MA TRẬN NGỮ NGHĨA VÀ HÀNH VI CHỊU LỖI (FAILURE SEMANTICS MATRIX)

Bảng ma trận chi tiết 7 kịch bản lỗi cốt lõi (F1 ➔ F7) dùng làm đặc tả kỹ thuật cho bộ **Fault Injection Integration Tests** trong Tuần 7:

| Mã | Kịch bản Lỗi (Fault Scenario) | Miền Lỗi | Cơ chế Phát hiện (Detection) | Chính sách (Policy) | Quyết định Trả về (Decision) | Retry? | Fallback? | Ghi nhận Audit Ledger | `failure_reason` |
| :---: | :--- | :--- | :--- | :--- | :--- | :---: | :---: | :--- | :--- |
| **F1** | Per-attempt strategy timeout trong ngân sách | Decision | Task timeout cancellation (120ms) | Polly Constant Retry | Kết quả Strategy gốc (lần retry sau thành công) | **Có** | Không | Ghi nhận Execution bình thường | `null` |
| **F2** | Strategy Timeout cạn kiệt ngân sách Retry | Decision | Retry attempts exhausted / Outer timeout | Fallback Execution | Quyết định từ `Deterministic Fallback` | **Có** (đã cạn) | **Có** | `ExecutionType = FALLBACK`, `FallbackUsed = true` | `STRATEGY_TIMEOUT` |
| **F3** | Strategy ném ngoại lệ nội bộ không phục hồi | Decision | Exception Catch Filter | Fallback Execution | Quyết định từ `Deterministic Fallback` | Tùy Exception | **Có** | `ExecutionType = FALLBACK`, `FallbackUsed = true` | `STRATEGY_EXCEPTION` |
| **F4** | Circuit Breaker đang ở trạng thái `OPEN` | Decision | Polly Circuit State Check | Fast-fail Bypass | Quyết định từ `Deterministic Fallback` | **Không** | **Có** | `ExecutionType = FALLBACK` ($T \approx 0\text{ms}$) | `CIRCUIT_BREAKER_OPEN` |
| **F5** | Redis không khả dụng / Connection drop | Infra Cache | `RedisConnectionException` | L1 In-Memory Fallback / Cache Miss Semantics | Quyết định tương đương nghiệp vụ (từ L1 hoặc Strategy) | **Không** | **Không** | `cache_hit = true/false`, `fallback_used = false`, ghi log Warning | `null` |
| **F6** | PostgreSQL Down / Write Timeout | Infra DB | `NpgsqlException` / `DbUpdateException` | Policy A (Mandatory Audit Consistency) | Lỗi hệ thống (HTTP 500 / 503 Service Unavailable) | **Không** | **Không** | Không thể ghi DB (Log Error cấp hệ thống) | `PERSISTENCE_FAILURE` |
| **F7** | Caller chủ động Cancel Request | Caller | `CancellationToken.IsCancellationRequested` | Immediate Abort | Hủy xử lý (HTTP Aborted) | **Không** | **Không** | **KHÔNG** tạo bản ghi quyết định, chỉ ghi App Log | N/A |

---

## 4. KẾ HOẠCH BÀO CHẾ & KIỂM CHỨNG (VERIFICATION PLAN FOR WEEK 7)

1. **Unit Test Suite:** Kiểm chứng từng Policy Handler (Polly Timeout, Circuit Breaker State Transitions `CLOSED` $\rightarrow$ `OPEN` $\rightarrow$ `HALF-OPEN` $\rightarrow$ `CLOSED`).
2. **Integration Test Suite (Fault Injection Harness):**
   * Giả lập độ trễ mạng và timeout có kiểm soát ($> 120\text{ms}$).
   * Ném lỗi chiến lược có kiểm soát.
   * Ngắt kết nối Redis để kiểm chứng suy thoái êm sang L1 In-Memory Cache và xác nhận Strategy vẫn chạy bình thường.
   * Ngắt kết nối PostgreSQL để kiểm chứng Policy A trả về HTTP 500/503.
   * Sử dụng đồng bộ hóa tín hiệu (`ExecutionStarted`) để kích hoạt `CancellationTokenSource.Cancel()` deterministically và xác minh không sinh bản ghi audit rác.
3. **Auditing Invariant Verification:** Đảm bảo 100% test cases kiểm tra đúng giá trị của các cột `execution_type`, `fallback_used`, `failure_reason`, `cache_hit`, và `execution_time_ms` trong cơ sở dữ liệu.
