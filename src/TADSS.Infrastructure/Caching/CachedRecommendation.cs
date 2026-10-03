using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;

namespace TADSS.Infrastructure.Caching;

/// <summary>Hình dạng JSON của <see cref="DecisionRecommendation"/> trong Redis.</summary>
internal sealed class CachedRecommendation
{
    /// <summary>Loại khuyến nghị (chuỗi đúng tên thành viên trên wire).</summary>
    public required RecommendationType Type { get; init; }

    /// <summary>Mô tả cho người đọc.</summary>
    public required string Description { get; init; }
}
