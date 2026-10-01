using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;

namespace Shared.DecisionContractSDK.V2.Enums;

/// <summary>
/// Cách quyết định được tạo ra.
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum ExecutionType
{
    /// <summary>Strategy chạy và trả kết quả trong ngân sách thời gian.</summary>
    AUTOMATED = 0,

    /// <summary>Strategy lỗi, timeout hoặc circuit mở; dùng FallbackDecisionStrategy.</summary>
    FALLBACK = 1,

    /// <summary>Dùng lại StrategyDecision đã cache.</summary>
    CACHE_HIT = 2
}
