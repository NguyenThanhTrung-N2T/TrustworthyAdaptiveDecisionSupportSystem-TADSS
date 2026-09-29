# TÀI LIỆU THIẾT KẾ KIẾN TRÚC PHẦN MỀM CHUẨN (SOFTWARE ARCHITECTURE SPECIFICATION - SAS)

Tài liệu này đặc tả chi tiết kiến trúc phần mềm, các thành phần hệ thống (C4 Model) và thiết kế thực thể miền (Domain Interfaces) của hệ thống **Decision Runtime Platform (TADSS)**.

---

## 1. PHÂN RÃ HỆ THỐNG & .NET SOLUTION STRUCTURE (`TADSS.slnx` — 7 Projects)

Mô hình phân rã dự án thành 7 project độc lập trong `TADSS.slnx` (tách biệt hoàn toàn khỏi `CaseStudy.SmashCourt` — kết nối qua HTTP REST / SDK), loại bỏ liên kết ngược và bảo đảm tính di động (Portability):

```text
TADSS.slnx (7-Project Solution)
├── src/
│   ├── Shared.DecisionContractSDK/          # Decision Contract SDK (Shared Kernel — 0 external NuGet deps)
│   │   ├── Domain/                          # Reservation (Aggregate Root), Value Objects
│   │   ├── Interfaces/                      # IReservationDecisionStrategy
│   │   ├── Enums/                           # DecisionStatus, RecommendationType, BookingType, CustomerTier, ...
│   │   ├── DTOs/                            # DecisionRequestDto, DecisionResultDto, OverrideDecisionDto, ...
│   │   └── Common/                          # ContractVersionConstants, ContractVersionValidator, RequestHasher
│   │
│   ├── TADSS.Runtime.Api/                   # Decision Runtime Platform (ASP.NET Core 8 Web API)
│   │   ├── Controllers/                     # DecisionController, HealthController
│   │   ├── Services/                        # DecisionService, StrategyResolver (O(1) DI Lookup)
│   │   └── Infrastructure/
│   │       ├── Data/                        # TadssDbContext, DecisionExecution, AuditTimestampInterceptor
│   │       ├── Resilience/                  # DecisionResiliencePipeline (Polly v8), FallbackDecisionStrategy
│   │       ├── Caching/                     # ResilientDecisionCacheService (L1/L2)
│   │       └── Events/                      # DecisionEvaluatedAuditHandler, DecisionOverriddenAuditHandler
│   │
│   ├── TADSS.Strategies.Default/            # DefaultDecisionStrategy (Conservative Baseline Policy - Đồ án 2)
│   │   └── DefaultDecisionStrategy.cs
│   │
│   └── TADSS.Strategies.MockAlternative/    # MockAlternativeDecisionStrategy (30% / 50% Dynamic Deposit - Đồ án 2)
│       └── MockAlternativeDecisionStrategy.cs
│
└── tests/
    ├── TADSS.UnitTests/                     # Unit, Resilience, Audit & Dataset Normalization Tests (481 tests)
    ├── TADSS.IntegrationTests/              # Full HTTP Pipeline Integration Tests (26 tests)
    └── TADSS.BenchmarkRunner/               # 5-Layer Benchmark Harness (76 runs) & Dataset Normalizer (INV-P1..P8)
```

---

## 2. THIẾT KẾ KHUNG THỰC THỂ MIỀN (DOMAIN INTERFACES & SDK STRUCTS — FROZEN CONTRACT v1.0.0)

Chuẩn hóa các thực thể miền bên trong **Shared.DecisionContractSDK** theo mẫu thiết kế DDD và hợp đồng đóng băng `1.0.0`.

### 2.1. Cấu trúc Lớp Entity & Aggregate Signatures
*   **Reservation (Aggregate Root)**
    *   `Guid Id`, `Guid CourtId`, `Guid? CourtTypeId`, `Guid BranchId`, `Guid CustomerId`
    *   `CustomerTier CustomerTier` (`Standard`, `Silver`, `Gold`, `Platinum`)
    *   `TimeRange TimeSlot` (Value Object kiểm chứng `StartTime < EndTime`, thời lượng 30 phút – 8 giờ)
    *   `BookingType BookingType` (`Single`, `Recurring`, `Tournament`, `Vip`)
    *   `Money TotalPrice`, `Money DepositAmount` (Value Object kiểm chứng `>= 0` và `DepositAmount <= TotalPrice`)
    *   `PaymentStatus PaymentStatus` (`Unpaid`, `PartiallyPaid`, `Paid`, `Refunded`, `Expired`)
    *   `PriorityLevel Priority` (`Low`, `Normal`, `High`, `Critical`)
    *   `DateTime RequestedAt`, `IReadOnlyDictionary<string, string>? Metadata`
    *   *Behaviors:* `Create(...)`, `GetDuration()`, `IsPeakHour()`, `GetRequiredDepositRatio()`, `RequiresHighValueReview()`, `IsEligibleForAutoApproval()`, `GetRemainingBalance()`

### 2.2. Giao diện Chiến lược Quyết định: `IReservationDecisionStrategy`
```csharp
namespace Shared.DecisionContractSDK.Interfaces;

using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs;

public interface IReservationDecisionStrategy
{
    string StrategyName { get; }
    string StrategyVersion { get; }
    Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default);
}
```

### 2.3. Hợp đồng dữ liệu yêu cầu: `DecisionRequestDto`
```csharp
namespace Shared.DecisionContractSDK.DTOs;

public sealed record DecisionRequestDto
{
    public Guid ReservationId { get; init; }
    public Guid CourtId { get; init; }
    public Guid? CourtTypeId { get; init; }
    public Guid BranchId { get; init; }
    public Guid CustomerId { get; init; }
    public string CustomerTier { get; init; } = "STANDARD";
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public string BookingType { get; init; } = "SINGLE";
    public decimal TotalPrice { get; init; }
    public decimal DepositAmount { get; init; }
    public string PaymentStatus { get; init; } = "UNPAID";
    public string Priority { get; init; } = "NORMAL";
    public DateTime RequestedAt { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}
```

### 2.4. Hợp đồng dữ liệu đầu ra: `DecisionResultDto` & `DecisionRecommendationDto`
*   **DecisionResultDto**
    *   `Guid DecisionId`, `Guid ReservationId`
    *   `string Status` (`"APPROVED"`, `"REJECTED"`, `"NEED_APPROVAL"`)
    *   `string StrategyName`, `string StrategyVersion`
    *   `int ExecutionTimeMs`, `bool FallbackUsed`
    *   `string PrimaryReason`, `List<string> Reasons`
    *   `List<DecisionRecommendationDto> Recommendations`
    *   `DateTime EvaluatedAt`
*   **DecisionRecommendationDto**
    *   `string Type` (`"ALTERNATIVE_SLOT"`, `"ALTERNATIVE_COURT"`, `"PRICE_ADJUSTMENT"`, `"REQUIRE_DEPOSIT"`, `"REQUIRE_APPROVAL"`, `"PRIORITY_ESCALATION"`)
    *   `string Description`
    *   `Guid? SuggestedCourtId`, `DateTime? SuggestedStartTime`, `DateTime? SuggestedEndTime`
    *   `decimal? SuggestedPrice`, `double? ConfidenceScore`

### 2.5. Hợp đồng dữ liệu lỗi chuẩn hóa: `ErrorResponseDto`
```csharp
namespace Shared.DecisionContractSDK.DTOs;

public sealed record ErrorResponseDto
{
    public string ErrorCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? CorrelationId { get; init; }
    public DateTime Timestamp { get; init; }
    public List<ValidationErrorItemDto>? Details { get; init; }
}
```

---

## 3. DECISION EXECUTION ENGINE & INFRASTRUCTURE MODULES

Hệ thống **Decision Runtime Platform (TADSS)** triển khai các module hạ tầng cốt lõi:

```
                  ┌─────────────────────────────────────────┐
                  │        DECISION EXECUTION ENGINE        │
                  └────────────────────┬────────────────────┘
                                       │
      ┌────────────────────────┬───────┼────────┬────────────────────────┐
      ▼                        ▼       ▼        ▼                        ▼
┌───────────┐            ┌──────────┐┌───┐┌──────────┐             ┌───────────┐
│ Strategy  │            │  Policy  ││ O ││Feature   │             │Versioning │
│ Registry  │            │  Engine  ││ b ││Flags     │             │& Migration│
└───────────┘            └──────────┘└───┘└──────────┘             └───────────┘
```

1.  **Strategy Registry (DI-based Strategy Resolution):** Đăng ký các Strategy vào `IServiceCollection` và phân giải qua `StrategyResolver` với độ phức tạp $O(1)$.
2.  **Safety Fallback Continuity (`FallbackDecisionStrategy`):** Khi Polly kích hoạt chế độ Fallback do strategy timeout hoặc lỗi nội bộ, hệ thống trả về quyết định an toàn xác định `Status = NEED_APPROVAL` (`FallbackUsed = true`, `ExecutionTimeMs = 0`), tách biệt hoàn toàn với chính sách nghiệp vụ thông thường (`DefaultDecisionStrategy`).
3.  **Observability (Serilog + OpenTelemetry):** Đo lường và lưu vết thời gian xử lý (`execution_time_ms`), cache hit/miss và failure reasons.
4.  **Feature Flags (`IFeatureFlagProvider`):** Bật/tắt nóng tính năng qua `IOptionsMonitor` dưới 2 giây.
5.  **Versioning & Migration:** Tự động đối chiếu `ContractVersion` để từ chối hoặc fallback khi có breaking changes trong payload.

### 3.1. Polly Resilience Pipeline Configuration (C# Spec)

```csharp
var resiliencePipeline = new ResiliencePipelineBuilder<DecisionExecutionOutcome>()
    .AddTimeout(TimeSpan.FromMilliseconds(500)) // Outer timeout (Overall Request SLA Budget)
    .AddFallback(new FallbackStrategyOptions<DecisionExecutionOutcome>
    {
        // Kích hoạt FallbackDecisionStrategy (chế độ an toàn NEED_APPROVAL) khi strategy bị timeout hoặc lỗi
        FallbackAction = args => ...
    })
    .AddRetry(new RetryStrategyOptions<DecisionExecutionOutcome>
    {
        MaxRetryAttempts = 2, // 2 retries = 3 attempts total
        BackoffType = DelayBackoffType.Constant,
        Delay = TimeSpan.FromMilliseconds(30)
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions<DecisionExecutionOutcome>
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 10,
        BreakDuration = TimeSpan.FromSeconds(15)
    })
    .AddTimeout(TimeSpan.FromMilliseconds(120)) // Inner per-attempt timeout (120ms limit)
    .Build();
```

---

## 4. THIẾT KẾ CƠ SỞ DỮ LIỆU (DATABASE SCHEMA: tadss.decision_executions)

### 4.1. DDL Script Bảng Duy nhất `tadss.decision_executions`

```sql
CREATE SCHEMA IF NOT EXISTS tadss;

CREATE TABLE IF NOT EXISTS tadss.decision_executions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    decision_id UUID NOT NULL,
    reservation_id UUID NOT NULL,
    strategy_name VARCHAR(100) NOT NULL,
    strategy_version VARCHAR(20) NOT NULL,
    execution_type VARCHAR(50) NOT NULL, -- 'AUTOMATED', 'CACHE_HIT', 'FALLBACK'
    execution_time_ms INTEGER NOT NULL,
    fallback_used BOOLEAN NOT NULL DEFAULT FALSE,
    failure_reason TEXT NULL,
    cache_hit BOOLEAN NOT NULL DEFAULT FALSE,
    need_approval BOOLEAN NOT NULL DEFAULT FALSE,
    request_hash VARCHAR(64) NOT NULL,
    response_payload JSONB NOT NULL,
    
    -- Các trường Override & Concurrency
    is_overridden BOOLEAN NOT NULL DEFAULT FALSE,
    overridden_by VARCHAR(100) NULL,
    override_reason TEXT NULL,
    idempotency_key VARCHAR(128) NULL,
    version INTEGER NOT NULL DEFAULT 1,
    
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT uq_decision_id UNIQUE (decision_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_decision_idempotency_key ON tadss.decision_executions (idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_decision_executions_request_hash ON tadss.decision_executions (request_hash);
CREATE INDEX IF NOT EXISTS idx_decision_executions_reservation_id ON tadss.decision_executions (reservation_id);
CREATE INDEX IF NOT EXISTS idx_decision_executions_created_at ON tadss.decision_executions (created_at DESC);
CREATE INDEX IF NOT EXISTS idx_decision_executions_pending_approval ON tadss.decision_executions (need_approval, is_overridden) WHERE need_approval = TRUE AND is_overridden = FALSE;
```

---

## 5. CHI TIẾT LUỒNG ĐI CỦA REQUEST (SEQUENCE DIAGRAM)

```mermaid
sequenceDiagram
    autonumber
    participant App as Case Study App (SmashCourt)
    participant TADSS as Decision Runtime (TADSS)
    participant Cache as Request-Hash Cache (Redis)
    participant PollyPipeline as Polly Resilience Pipeline
    participant Strategy as Active Decision Strategy
    participant FallbackStr as FallbackDecisionStrategy (NEED_APPROVAL)
    participant DB as Database (PostgreSQL - Schema tadss)

    App->>TADSS: POST /api/v1/decisions (DecisionRequest)
    TADSS->>TADSS: Canonicalize Representation & Compute SHA256 Hash
    TADSS->>Cache: Check exact-match key (decision:hash:...)
    
    alt Trường hợp HIT Cache
        Cache-->>TADSS: Trả về DecisionResult (Lưu trong Cache)
        TADSS->>DB: Publish MediatR DecisionEvaluatedAuditNotification (đồng bộ: execution_type = 'CACHE_HIT', fallback_used = FALSE)
    else Trường hợp MISS Cache
        TADSS->>PollyPipeline: Gọi thực thi bọc bởi Resilience Pipeline (Overall budget: 500ms)
        Note over PollyPipeline: Configured Pipeline: Outer 500ms -> Fallback -> Retry 2 -> CB -> Inner 120ms
        
        alt Trường hợp Strategy phản hồi thành công
            PollyPipeline->>Strategy: EvaluateAsync(request, ct)
            Strategy-->>PollyPipeline: Trả về DecisionResult (30% hoặc 50% cọc)
            PollyPipeline-->>TADSS: Trả về kết quả thành công
            TADSS->>Cache: Lưu DecisionResult vào Cache
            TADSS->>DB: Publish MediatR DecisionEvaluatedAuditNotification (đồng bộ: execution_type = 'AUTOMATED', fallback_used = FALSE)
        else Trường hợp Strategy bị timeout (>120ms) hoặc lỗi 500
            PollyPipeline->>Strategy: EvaluateAsync(request, ct)
            Note over Strategy: Gặp sự cố timeout hoặc ném Exception...
            PollyPipeline->>PollyPipeline: Thực thi Retry (tối đa 2 lần) & Inner Timeout (120ms)
            PollyPipeline->>PollyPipeline: Mở mạch Circuit Breaker khi tỷ lệ lỗi vượt ngưỡng
            PollyPipeline->>FallbackStr: EvaluateFailSafeAsync(request, ct)
            FallbackStr-->>PollyPipeline: Trả về DecisionResult (Status = NEED_APPROVAL, mặc định 100%)
            PollyPipeline-->>TADSS: Trả về kết quả Fallback
            TADSS->>DB: Publish MediatR DecisionEvaluatedAuditNotification (đồng bộ: execution_type = 'FALLBACK', fallback_used = TRUE)
        end
    end
    
    TADSS-->>App: Trả về DecisionResult
```

---

## 6. QUY TẮC PHÊ DUYỆT HUMAN-IN-THE-LOOP & NO AUTO-APPROVE

```
DecisionResult.Status:
  ├── Approved      ──► Tiếp tục luồng đặt sân ──► Tạo giao dịch VNPay
  ├── NeedApproval  ──► Booking.Status = PendingDecisionApproval (TẠM CHẶN VNPAY)
  │                      └── Chờ Manager duyệt trên Dashboard:
  │                            ├── [Approve]  ──► Duyệt mức cọc đề xuất ──► Tiếp tục sang VNPay
  │                            └── [Override] ──► Sửa mức cọc (kèm Reason) ──► Tiếp tục sang VNPay
  │                      └── Hết hạn duyệt (Timeout 15 phút):
  │                            └── Chuyển sang Expired / Cancelled (TUYỆT ĐỐI KHÔNG AUTO-APPROVE)
  └── Rejected      ──► Hủy Booking ──► Thông báo lý do từ chối
```
