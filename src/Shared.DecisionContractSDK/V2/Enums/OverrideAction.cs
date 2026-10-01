using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;

namespace Shared.DecisionContractSDK.V2.Enums;

/// <summary>
/// Hành động của Manager trên quyết định chờ duyệt.
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum OverrideAction
{
    /// <summary>Duyệt kèm mức cọc.</summary>
    APPROVE = 0,

    /// <summary>Từ chối.</summary>
    REJECT = 1
}
