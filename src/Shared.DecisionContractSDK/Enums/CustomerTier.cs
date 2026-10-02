using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;

namespace Shared.DecisionContractSDK.Enums;

/// <summary>
/// Hạng thành viên chuẩn hóa; client ánh xạ hạng nội bộ theo thứ hạng (SmashCourt: <c>MinPoints</c> tăng dần).
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum CustomerTier
{
    /// <summary>Hạng cơ bản (khách vãng lai cũng dùng hạng này).</summary>
    STANDARD = 0,

    /// <summary>Hạng thứ 2.</summary>
    SILVER = 1,

    /// <summary>Hạng thứ 3.</summary>
    GOLD = 2,

    /// <summary>Hạng thứ 4.</summary>
    PLATINUM = 3,

    /// <summary>Hạng thứ 5 trở lên.</summary>
    DIAMOND = 4
}
