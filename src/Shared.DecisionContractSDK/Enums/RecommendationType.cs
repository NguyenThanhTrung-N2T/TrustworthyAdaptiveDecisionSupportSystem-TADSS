using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;

namespace Shared.DecisionContractSDK.Enums;

/// <summary>
/// Loại khuyến nghị kèm quyết định.
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum RecommendationType
{
    /// <summary>Cần Manager duyệt.</summary>
    REQUIRE_APPROVAL = 0,

    /// <summary>Gợi ý khung giờ khác.</summary>
    ALTERNATIVE_SLOT = 1,

    /// <summary>Gợi ý tài nguyên (sân) khác.</summary>
    ALTERNATIVE_RESOURCE = 2,

    /// <summary>Gợi ý ưu tiên xử lý.</summary>
    PRIORITY_ESCALATION = 3
}
