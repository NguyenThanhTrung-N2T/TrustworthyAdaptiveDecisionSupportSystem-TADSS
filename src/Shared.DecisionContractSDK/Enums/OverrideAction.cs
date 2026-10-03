using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;

namespace Shared.DecisionContractSDK.Enums;

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
