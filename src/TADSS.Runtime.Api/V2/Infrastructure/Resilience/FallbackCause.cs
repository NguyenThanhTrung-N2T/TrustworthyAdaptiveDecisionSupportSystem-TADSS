namespace TADSS.Runtime.Api.V2.Infrastructure.Resilience;

/// <summary>
/// Nguyên nhân khiến pipeline v2 phải dùng quyết định fallback (RESILIENCE_AND_FAULT_TAXONOMY F2–F4).
/// </summary>
public enum FallbackCause
{
    /// <summary>F2: strategy vượt Inner timeout ở mọi lượt thử.</summary>
    Timeout = 0,

    /// <summary>F3: strategy ném lỗi (kể cả trả <c>null</c> hoặc lỗi bất biến khi dựng quyết định) ở mọi lượt thử.</summary>
    Exception = 1,

    /// <summary>F4: circuit breaker đang mở nên không gọi strategy.</summary>
    CircuitOpen = 2
}
