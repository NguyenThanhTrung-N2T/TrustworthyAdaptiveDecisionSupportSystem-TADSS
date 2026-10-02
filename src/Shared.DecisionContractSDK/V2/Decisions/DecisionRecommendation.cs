using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.Decisions;

/// <summary>
/// Khuyến nghị kèm quyết định (ví dụ cần duyệt, gợi ý khung giờ khác).
/// </summary>
public sealed class DecisionRecommendation
{
    /// <summary>Loại khuyến nghị.</summary>
    public RecommendationType Type { get; }

    /// <summary>Mô tả cho người đọc.</summary>
    public string Description { get; }

    /// <summary>Khởi tạo khuyến nghị.</summary>
    /// <param name="type">Loại khuyến nghị đã định nghĩa.</param>
    /// <param name="description">Mô tả, không rỗng.</param>
    /// <exception cref="DomainInvariantException">Khi loại không hợp lệ hoặc mô tả rỗng.</exception>
    public DecisionRecommendation(RecommendationType type, string description)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainInvariantException("INVALID_RECOMMENDATION_TYPE", $"Recommendation type '{type}' is not defined.", nameof(Type));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new DomainInvariantException("INVALID_RECOMMENDATION_DESCRIPTION", "Description must not be empty.", nameof(Description));
        }

        Type = type;
        Description = description;
    }
}
