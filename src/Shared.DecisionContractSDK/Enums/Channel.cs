using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;

namespace Shared.DecisionContractSDK.Enums;

/// <summary>
/// Kênh tạo booking (P-02) — trùng tên với <c>BookingSource</c> của SmashCourt-BE.
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum Channel
{
    /// <summary>Khách tự đặt online.</summary>
    ONLINE = 0,

    /// <summary>Đặt tại quầy (SmashCourt chưa gọi TADSS cho kênh này — INTEGRATION_SPEC §4.2).</summary>
    WALK_IN = 1
}
