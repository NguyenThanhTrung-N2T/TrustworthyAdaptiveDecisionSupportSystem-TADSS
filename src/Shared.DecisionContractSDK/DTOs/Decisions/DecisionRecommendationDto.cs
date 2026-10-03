using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>Khuyến nghị kèm quyết định trên wire: <c>{ type, description }</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DecisionRecommendationDto
{
    /// <summary>Loại khuyến nghị.</summary>
    public required RecommendationType Type { get; init; }

    /// <summary>Mô tả cho người đọc.</summary>
    public required string Description { get; init; }
}
