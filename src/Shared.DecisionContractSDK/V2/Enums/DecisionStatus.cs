using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;

namespace Shared.DecisionContractSDK.V2.Enums;

/// <summary>
/// Trạng thái quyết định trả cho client (INTEGRATION_SPEC §3.3).
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum DecisionStatus
{
    /// <summary>Được duyệt; luôn kèm mức cọc.</summary>
    APPROVED = 0,

    /// <summary>Cần Manager duyệt; luôn kèm hạn duyệt.</summary>
    NEED_APPROVAL = 1,

    /// <summary>Bị từ chối theo chính sách.</summary>
    REJECTED = 2
}
