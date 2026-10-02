# PHÂN LOẠI LỖI & KIẾN TRÚC CHỊU LỖI — v2.0

> **Vai trò của tài liệu:** nguồn duy nhất về miền lỗi, cấu hình pipeline chịu lỗi và ngữ nghĩa xử lý từng loại lỗi. Kịch bản kiểm thử tương ứng (FI-01…FI-14): [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).

Mục tiêu của tầng chịu lỗi không phải chứng minh "zero downtime" cho toàn hệ thống, mà là:

> **Luồng ra quyết định luôn kết thúc bằng một kết quả an toàn trong ngân sách thời gian, và sự cố của TADSS không bao giờ làm hỏng booking của SmashCourt.**

---

## 1. PIPELINE CHỊU LỖI (POLLY v8)

```text
Outer Timeout 500ms ─ deadline cho toàn bộ phần thực thi strategy
 └─ Fallback ─ FallbackDecisionStrategy → NEED_APPROVAL (fallbackUsed = true)
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

- **Cách ly strategy không hợp tác:** strategy được thực thi sao cho Inner Timeout vẫn cắt được kể cả khi strategy bỏ qua `CancellationToken` hoặc chạy đồng bộ nặng CPU (task bị bỏ, kết quả trễ bị bỏ qua). Đây là điều kiện cần cho strategy học máy ở Khóa luận.
- **Circuit breaker cho Redis:** khi Redis lỗi liên tiếp, cache bị bỏ qua trong một khoảng thời gian thay vì mọi request đều chờ hết timeout của Redis.
- **Quy tắc approval window:** xem Mục 1.1 trong [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md).

---

## 2. NGUYÊN TẮC BẤT BIẾN

1. **Ngân sách:** tổng thời gian các lần thử + delay ≤ outer timeout.
2. **Cô lập miền lỗi cache:** lỗi Redis không kích hoạt fallback của strategy, không đổi kết quả nghiệp vụ, không gây bão retry; cache được coi như miss.
3. **Audit bắt buộc (fail-closed):** không ghi được audit thì không trả quyết định — trả 503 `PERSISTENCE_UNAVAILABLE`. (Outbox bất đồng bộ là future work.)
4. **Client hủy request:** dừng xử lý, không tạo bản ghi audit, chỉ ghi log vận hành kèm correlation id.
5. **Fallback tất định, tách khỏi chính sách nghiệp vụ:** `FallbackDecisionStrategy` không gọi I/O, không ném lỗi, luôn trả `NEED_APPROVAL`; tách biệt với `DefaultDecisionStrategy`. Mức cọc 100% khi TADSS không khả dụng là chính sách của SmashCourt, không phải của TADSS.
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
NEED_APPROVAL     PG: fail-closed 503   không ghi audit       booking nhất quán
```

---

## 4. MA TRẬN NGỮ NGHĨA LỖI

| Mã cũ (v1) | Kịch bản | Miền | Xử lý | Kết quả | `execution_type` / `failure_reason` | Kiểm thử v2 |
| :---: | :--- | :---: | :--- | :--- | :--- | :---: |
| F1 | Strategy chậm ở lần thử đầu, lần sau thành công | 1 | Retry | Kết quả của strategy | `AUTOMATED` / `null` | FI-01 |
| F2 | Strategy chậm ở mọi lần thử | 1 | Fallback | `NEED_APPROVAL` | `FALLBACK` / `STRATEGY_TIMEOUT` | FI-02 |
| F3 | Strategy ném lỗi | 1 | Retry → Fallback | `NEED_APPROVAL` | `FALLBACK` / `STRATEGY_EXCEPTION` | FI-03 |
| F4 | Circuit đang mở | 1 | Fast-fail → Fallback | `NEED_APPROVAL` | `FALLBACK` / `CIRCUIT_BREAKER_OPEN` | FI-04 |
| — | Strategy không hợp tác (bỏ qua token, bận CPU) | 1 | Cách ly + timeout | `NEED_APPROVAL` | `FALLBACK` / `STRATEGY_TIMEOUT` | FI-05 |
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

- **Phase 4 (v1):** F1–F7 có test tự động và đạt, nhưng chạy với EF Core InMemory và decorator `Task.Delay`. F5 (Redis) được mô phỏng bằng cache service ném exception; F6 được mô phỏng bằng interceptor ném `DbUpdateException` và trả 500 (v2 đổi thành 503). Chưa kịch bản nào được kiểm chứng trên Redis/PostgreSQL thật.
- **Phase 5, gói 5.1 (đã xong):** integration test chạy trên PostgreSQL 16 / Redis 7 / Toxiproxy thật (Testcontainers). **FI-08** đã chạy trên PostgreSQL thật qua Toxiproxy (tắt proxy) và trả 503 `PERSISTENCE_UNAVAILABLE` (mã `RuntimeErrorCodes` thuở đó đã gỡ ở 5.2c, nay là `ErrorCodes` của SDK; `GlobalExceptionHandler` ánh xạ lỗi tạm thời của PostgreSQL). Lỗi **V-8** (mất kết nối PostgreSQL bị ánh xạ thành 400) được phát hiện khi phân tích; FI-08 trên PostgreSQL thật xác nhận chuỗi exception `InvalidOperationException → NpgsqlException(IsTransient) → EndOfStreamException`, nếu không sửa sẽ trả 400. F6 cũ (interceptor ném `DbUpdateException`) vẫn trả 500 và được thay ở gói 5.2.
- **Phase 5, gói 5.2c (đã xong) — ma trận FI chạy trên API v2:** FI-01, FI-02, FI-03, FI-04, FI-06, FI-08 (hai kịch bản: mất PostgreSQL trước khi xử lý và đứt giữa lúc ghi audit, cả hai qua Toxiproxy), FI-10. Strategy lỗi/chậm được tiêm bằng `ScriptedStrategy` (strategy v2 thật, hành vi theo kịch bản) với cấu hình resilience riêng từng kịch bản, đồng hồ thật; thời gian pipeline đo từ cột `execution_time_ms` của bản ghi audit, thời gian endpoint đo ở client (SC2: pipeline ≤ 500 ms, endpoint ≤ 700 ms). F6 (lỗi lưu không tạm thời → 500) không có test HTTP riêng, được phủ bởi test store và UT-08. FI-06 mới dùng cache double ném lỗi (breaker Redis và Toxiproxy cho Redis thuộc 5.4); FI-05, FI-07, FI-09 chưa làm (5.4–5.5).
- **Phase 5 (v2):** toàn bộ FI-01…FI-10 chạy trên hạ tầng thật (Testcontainers / docker-compose + Toxiproxy). FI-11…FI-14 chạy ở mức E2E sau khi tích hợp, kèm bộ truy vấn bất biến booking.
