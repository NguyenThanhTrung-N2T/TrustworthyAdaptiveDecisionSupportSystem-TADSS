# PHÂN LOẠI LỖI & KIẾN TRÚC CHỊU LỖI — v2.0

> **Vai trò của tài liệu:** nguồn duy nhất về miền lỗi, cấu hình pipeline chịu lỗi và ngữ nghĩa xử lý từng loại lỗi. Kịch bản kiểm thử tương ứng (FI-01…FI-14): [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).

Mục tiêu của tầng chịu lỗi không phải chứng minh "zero downtime" cho toàn hệ thống, mà là:

> **Luồng ra quyết định luôn kết thúc bằng một kết quả an toàn trong ngân sách thời gian, và sự cố của TADSS không bao giờ làm hỏng booking của SmashCourt.**

---

## 1. PIPELINE CHỊU LỖI (POLLY v8)

```text
Outer Timeout 500ms ─ deadline cho toàn bộ phần thực thi strategy
 └─ Fallback ─ FallbackDecisionPolicy → APPROVED cọc 100% (fallbackUsed = true)
     └─ Retry ─ tối đa 2 lần, delay cố định 30ms (tổng 3 lần thử)
         └─ Circuit Breaker ─ FailureRatio 0.5, SamplingDuration 30s, MinimumThroughput 10, BreakDuration 15s
             └─ Inner Timeout 120ms ─ mỗi lần thử
                 └─ Strategy.EvaluateAsync()
```

| Tham số (`Tadss:Resilience`) | Giá trị |
| :--- | :--- |
| `OuterTimeoutMs` | 500 |
| `InnerTimeoutMs` | 120 |
| `MaxRetryAttempts` | 2 |
| `RetryBaseDelayMs` | 30 |
| `CircuitBreakerFailureRatio` | 0.5 |
| `CircuitBreakerSamplingSeconds` | 30 |
| `CircuitBreakerMinimumThroughput` | 10 |
| `CircuitBreakerBreakDurationSeconds` | 15 |

**Ngân sách cấu hình:** 3 × 120ms + 2 × 30ms = 420ms < 500ms. Đây là ràng buộc cấu hình; độ trễ thực tế phải đo (SC2).

**Phạm vi của outer timeout:** chỉ bao phần thực thi strategy (retry, circuit breaker, các lần thử). Đọc/ghi cache và ghi audit nằm ngoài pipeline, nên độ trễ toàn endpoint = thời gian pipeline + cache + ghi audit + serialize. KPI SC2 đo riêng hai đại lượng này.

**Bổ sung ở v2 (Phase 5):**

- **Cách ly strategy không hợp tác (đã hiện thực, gói 5.4):** mọi strategy chạy qua `IsolatedStrategyInvoker` (`Task.Run` + `WaitAsync(token)`) ở lớp trong cùng của pipeline nên Inner Timeout cắt được cả strategy bỏ qua `CancellationToken` hoặc chạy đồng bộ lâu: request nhận fallback `APPROVED` cọc 100% trong ngân sách, task bị bỏ rơi được quan sát exception và đếm bằng `tadss.strategy.abandoned`. **Giới hạn:** TADSS giới hạn thời gian chờ và cô lập luồng request khỏi strategy không hợp tác, nhưng **không dừng được** strategy đó — CPU của task bị bỏ rơi vẫn bị tiêu thụ tới khi nó tự kết thúc, và nhiều task bị bỏ rơi cùng lúc có thể làm cạn thread pool. Mỗi strategy có pipeline và circuit breaker riêng (khóa theo `StrategyName`) nên breaker của strategy này mở không làm strategy khác fast-fail. Đây là điều kiện cần cho strategy học máy ở Khóa luận.
- **Circuit breaker cho Redis (đã hiện thực, gói 5.4):** mọi lệnh Redis của cache chạy trong pipeline riêng (breaker bọc timeout `RedisTimeoutMs`, tham số `Tadss:Cache:RedisBreaker*`); lỗi hoặc quá hạn liên tiếp thì breaker mở, request bỏ qua Redis ngay (dùng memory nếu `EnableFallbackToMemory`) thay vì mỗi request chờ hết timeout, và `tadss.cache.requests{result="bypass"}` tăng. Payload hỏng chỉ là miss, không tính vào breaker. Breaker theo từng instance; lệnh đầu tiên tới ngưỡng `RedisBreakerMinimumThroughput` vẫn chờ tới `RedisTimeoutMs`.
- **Quy tắc approval window:** xem Mục 1.1 trong [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md).

---

## 2. NGUYÊN TẮC BẤT BIẾN

1. **Ngân sách:** tổng thời gian các lần thử + delay ≤ outer timeout.
2. **Cô lập miền lỗi cache:** lỗi Redis không kích hoạt fallback của strategy, không đổi kết quả nghiệp vụ, không gây bão retry; cache được coi như miss.
3. **Audit bắt buộc (fail-closed):** không ghi được audit thì không trả quyết định — trả 503 `PERSISTENCE_UNAVAILABLE`. (Outbox bất đồng bộ là future work.)
4. **Client hủy request:** dừng xử lý, không tạo bản ghi audit, chỉ ghi log vận hành kèm correlation id.
5. **Fallback tất định, tách khỏi chính sách nghiệp vụ:** `FallbackDecisionPolicy` không gọi I/O, không ném lỗi, luôn trả `APPROVED` với cọc 100% (tỷ lệ 1, số tiền = `totalPrice`), giữ `reasonCode` theo nguyên nhân (`FALLBACK_STRATEGY_TIMEOUT` / `FALLBACK_STRATEGY_EXCEPTION` / `FALLBACK_CIRCUIT_OPEN`); tách biệt với `DefaultDecisionStrategy`. Mọi sự cố kỹ thuật dùng chung một quy tắc cọc 100% — giống khi SmashCourt tắt flag hoặc không gọi được TADSS; `NEED_APPROVAL` chỉ dành cho bất định nghiệp vụ do strategy quyết định, nên circuit mở không đẩy hàng loạt đơn sang Manager. Quyết định fallback không vào hàng chờ duyệt và không override được (D-13, 04/10/2026; trước đó fallback trả `NEED_APPROVAL`).
6. **Bất biến audit:** bản ghi quyết định, override và outcome không bị sửa sau khi ghi (DB trigger — xem [`DECISION_PERSISTENCE_INVARIANTS.md`](DECISION_PERSISTENCE_INVARIANTS.md)).
7. **SmashCourt không phụ thuộc cứng vào TADSS:** mọi lỗi phía TADSS (timeout, 5xx, 401, mất mạng) → SmashCourt áp cọc 100%, booking vẫn hợp lệ.

---

## 3. BỐN MIỀN LỖI

```text
                         Yêu cầu quyết định
                                │
   ┌──────────────────┬─────────┴─────────┬──────────────────────┐
   ▼                  ▼                   ▼                      ▼
[1] Strategy      [2] Hạ tầng TADSS   [3] Caller            [4] Tích hợp SmashCourt ↔ TADSS
timeout, lỗi,     Redis, PostgreSQL   hủy request           TADSS dừng/chậm, mất mạng,
circuit mở                                                  flag tắt
   │                  │                   │                      │
Fallback →        Redis: bỏ qua cache   Dừng ngay,            SmashCourt áp 100%,
APPROVED 100%     PG: fail-closed 503   không ghi audit       booking nhất quán
```

---

## 4. MA TRẬN NGỮ NGHĨA LỖI

| Mã F (tên test) | Kịch bản | Miền | Xử lý | Kết quả | `execution_type` / `failure_reason` | Kiểm thử |
| :---: | :--- | :---: | :--- | :--- | :--- | :---: |
| F1 | Strategy chậm ở lần thử đầu, lần sau thành công | 1 | Retry | Kết quả của strategy | `AUTOMATED` / `null` | FI-01 |
| F2 | Strategy chậm ở mọi lần thử | 1 | Fallback | `APPROVED` cọc 100% | `FALLBACK` / `STRATEGY_TIMEOUT` | FI-02 |
| F3 | Strategy ném lỗi | 1 | Retry → Fallback | `APPROVED` cọc 100% | `FALLBACK` / `STRATEGY_EXCEPTION` | FI-03 |
| F4 | Circuit đang mở | 1 | Fast-fail → Fallback | `APPROVED` cọc 100% | `FALLBACK` / `CIRCUIT_BREAKER_OPEN` | FI-04 |
| — | Strategy không hợp tác (bỏ qua token, bận CPU) | 1 | Cách ly + timeout | `APPROVED` cọc 100% | `FALLBACK` / `STRATEGY_TIMEOUT` | FI-05 |
| F5 | Redis mất kết nối / chậm | 2 | Bỏ qua cache (breaker Redis) | Kết quả bình thường | `AUTOMATED` / `null` | FI-06, FI-07 |
| F6 | PostgreSQL mất kết nối | 2 | Fail-closed | 503 `PERSISTENCE_UNAVAILABLE` | không ghi được | FI-08 |
| — | PostgreSQL chậm | 2 | Chờ trong giới hạn | Kết quả bình thường, độ trễ tăng | — | FI-09 |
| F7 | Client hủy request | 3 | Dừng ngay | Không có response | không ghi audit | FI-10 |
| — | TADSS dừng hoàn toàn | 4 | SmashCourt áp 100% | Booking hợp lệ | — | FI-11 |
| — | TADSS chậm hơn timeout của BE | 4 | SmashCourt áp 100%, retry tối đa 1 lần cùng idempotency key | Không có quyết định trùng | — | FI-12 |
| — | Tắt feature flag khi đang chạy | 4 | Reload cấu hình < 2s | Áp 100%, không gọi TADSS | — | FI-13 |
| — | Đứt kết nối BE ↔ TADSS giữa chừng | 4 | SmashCourt áp 100% | Booking và slot nhất quán | — | FI-14 |

---

## 5. HIỆN TRẠNG KIỂM CHỨNG

- **Gói 5.1 (đã xong):** integration test chạy trên PostgreSQL 16 / Redis 7 / Toxiproxy thật (Testcontainers). FI-08 trên PostgreSQL thật xác nhận chuỗi exception khi mất kết nối `InvalidOperationException → NpgsqlException(IsTransient) → EndOfStreamException`; `ExceptionClassifier` duyệt chuỗi này để trả 503 `PERSISTENCE_UNAVAILABLE` (mã trong `ErrorCodes` của SDK) thay vì 400 (lỗi V-8).
- **Gói 5.2c (đã xong) — ma trận FI chạy trên API v2:** FI-01, FI-02, FI-03, FI-04, FI-06, FI-08 (hai kịch bản: mất PostgreSQL trước khi xử lý và đứt giữa lúc ghi audit, cả hai qua Toxiproxy), FI-10. Strategy lỗi/chậm được tiêm bằng `ScriptedStrategy` (strategy v2 thật, hành vi theo kịch bản) với cấu hình resilience riêng từng kịch bản, đồng hồ thật; thời gian pipeline đo từ cột `execution_time_ms` của bản ghi audit, thời gian endpoint đo ở client (SC2: pipeline ≤ 500 ms, endpoint ≤ 700 ms). F6 (lỗi lưu không tạm thời → 500) không có test HTTP riêng, được phủ bởi test store và UT-08. FI-06 mới dùng cache double ném lỗi (breaker Redis và Toxiproxy cho Redis thuộc 5.4); FI-05 làm ở 5.4, FI-07 và FI-09 ở 5.5.
- **Gói 5.4 (đã xong):** FI-05 chạy trên API v2 (strategy `ScriptedStrategy` chặn đồng bộ 3 s và bỏ qua token bị cắt trong ngân sách, fallback `FALLBACK_STRATEGY_TIMEOUT`) cùng test đơn vị của `IsolatedStrategyInvoker` (đồng bộ, bất đồng bộ, exception muộn không thành `UnobservedTaskException`); FI-06 đầy đủ: Redis qua Toxiproxy bị ngắt thì request vẫn 200 không fallback, breaker mở và request sau bỏ qua Redis (metric `bypass`), cùng test đơn vị breaker (mở, nửa mở, đóng, payload hỏng không tính). Breaker theo strategy (strategy A mở, B vẫn `AUTOMATED`) và theo instance (SCALE-1) đã có test. Decorator tiêm lỗi theo cấu hình (`Tadss:Testing:FaultInjection`, chỉ môi trường `Testing`) phủ FI-02/FI-03 qua cấu hình và dành cho LT-03 ở gói 5.5. FI-07 (Redis chậm dưới tải) thuộc gói 5.5.
- **Gói 5.5 (đã xong):** FI-07 (Redis trễ 200 ms qua Toxiproxy: request vẫn 200, không fallback do cache, breaker Redis mở và metric `bypass` tăng; chạy cả dưới tải ở LT-03) và FI-09 (PostgreSQL trễ 300 ms: phản hồi đúng, không trùng bản ghi, độ trễ được ghi nhận) có integration test. **Đổi profile tiêm lỗi nóng (O-17):** ở môi trường `Testing`, `FaultInjectionControl` theo dõi file JSON (`TADSS_FAULT_INJECTION_FILE`) và đổi `Profile`/`DelayMs` lúc chạy — file cụt hoặc mất khóa `Profile` thì giữ cấu hình cũ, ghi Error và tăng `tadss.config.reload_failures`; `Enabled`/`TargetStrategy` chỉ đọc lúc khởi động. Evaluator dùng cơ chế này để bật/tắt FI-02/FI-03 theo lịch trong LT-03, còn FI-07/FI-08 bật/tắt qua Toxiproxy.
- **Phase 5 (v2):** toàn bộ FI-01…FI-10 chạy trên hạ tầng thật (Testcontainers và chế độ đánh giá `docker compose -p tadss-eval --profile evaluation run --rm --build evaluator`, D-12). FI-11…FI-14 chạy ở mức E2E sau khi tích hợp, kèm bộ truy vấn bất biến booking.
