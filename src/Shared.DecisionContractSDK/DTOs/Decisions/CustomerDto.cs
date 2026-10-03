using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>Thông tin khách của request.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CustomerDto
{
    /// <summary>Định danh khách; bắt buộc khi <see cref="IsGuest"/> là <c>false</c>, <c>null</c> khi là khách vãng lai.</summary>
    public Guid? CustomerId { get; init; }

    /// <summary>Khách vãng lai.</summary>
    public required bool IsGuest { get; init; }

    /// <summary>Hạng thành viên chuẩn hóa.</summary>
    public required CustomerTier Tier { get; init; }

    /// <summary>Thời điểm tạo tài khoản (tùy chọn, có offset); <c>null</c> với khách vãng lai.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public DateTimeOffset? MemberSince { get; init; }

    /// <summary>Lịch sử đặt chỗ (tùy chọn).</summary>
    public CustomerHistoryDto? History { get; init; }
}
