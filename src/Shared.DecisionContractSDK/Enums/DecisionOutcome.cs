using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;

namespace Shared.DecisionContractSDK.Enums;

/// <summary>
/// Kết quả thực tế của booking, client báo về sau khi booking kết thúc (INTEGRATION_SPEC §2.3).
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum DecisionOutcome
{
    /// <summary>Khách đến và hoàn tất.</summary>
    COMPLETED = 0,

    /// <summary>Khách không đến.</summary>
    NO_SHOW = 1,

    /// <summary>Khách hủy.</summary>
    CANCELLED_BY_CUSTOMER = 2,

    /// <summary>Nhân viên hủy.</summary>
    CANCELLED_BY_STAFF = 3,

    /// <summary>Hết hạn thanh toán cọc.</summary>
    PAYMENT_EXPIRED = 4,

    /// <summary>Hết hạn duyệt mà Manager chưa xử lý.</summary>
    APPROVAL_EXPIRED = 5,

    /// <summary>Bị từ chối (bởi strategy hoặc Manager).</summary>
    REJECTED = 6
}
