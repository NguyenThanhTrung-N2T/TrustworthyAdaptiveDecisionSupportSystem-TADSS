using Shared.DecisionContractSDK.DTOs.Decisions;

namespace TADSS.Runtime.Api.Infrastructure.Caching;

/// <summary>
/// Hợp đồng dịch vụ Caching phân tán cho kết quả đánh giá quyết định của TADSS.
/// </summary>
public interface IDecisionCacheService
{
    /// <summary>
    /// Lấy kết quả quyết định đã lưu trong cache theo cache key.
    /// </summary>
    Task<DecisionResultDto?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lưu kết quả quyết định vào cache với TTL tùy chọn.
    /// </summary>
    Task SetAsync(string key, DecisionResultDto result, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xóa một bản ghi cache theo key.
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
