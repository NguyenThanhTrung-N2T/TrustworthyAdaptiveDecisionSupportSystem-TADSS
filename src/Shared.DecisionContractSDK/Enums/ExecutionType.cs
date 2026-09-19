namespace Shared.DecisionContractSDK.Enums;

/// <summary>
/// Hình thức thực thi ra quyết định.
/// </summary>
public enum ExecutionType
{
    /// <summary>
    /// Quyết định được đánh giá tự động bởi Strategy Engine.
    /// </summary>
    AUTOMATED = 0,

    /// <summary>
    /// Quyết định được sinh ra từ chiến lược an toàn Fallback do timeout hoặc lỗi.
    /// </summary>
    FALLBACK = 1,

    /// <summary>
    /// Quyết định được trích xuất từ bộ nhớ đệm Idempotency Cache.
    /// </summary>
    CACHE_HIT = 2
}
