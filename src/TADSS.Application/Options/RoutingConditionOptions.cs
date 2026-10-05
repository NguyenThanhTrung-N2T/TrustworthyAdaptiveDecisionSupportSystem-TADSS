using Shared.DecisionContractSDK.Enums;

namespace TADSS.Application.Options;

/// <summary>
/// Điều kiện của một rule định tuyến (<c>Tadss:Routing:Rules:{i}:When</c>). Mọi điều kiện được đặt đều phải khớp (AND);
/// giá trị <c>null</c> hoặc danh sách rỗng nghĩa là không ràng buộc.
/// </summary>
public sealed class RoutingConditionOptions
{
    /// <summary>
    /// <c>true</c>: chỉ khớp giờ cao điểm; <c>false</c>: chỉ khớp ngoài giờ cao điểm. Giờ cao điểm theo lịch dùng chung (<c>Tadss:Calendar</c>)
    /// và giờ địa phương của chi nhánh, xét thời điểm bắt đầu sớm nhất của reservation.
    /// </summary>
    public bool? PeakHour { get; set; }

    /// <summary>
    /// Chi nhánh áp dụng (khớp khi <c>branchId</c> của request thuộc danh sách).
    /// </summary>
    public List<Guid> BranchIds { get; set; } = [];

    /// <summary>
    /// Kênh tạo booking áp dụng (<c>ONLINE</c>, <c>WALK_IN</c>).
    /// </summary>
    public List<Channel> Channels { get; set; } = [];

    /// <summary>
    /// Hạng khách áp dụng (<c>STANDARD</c>…<c>DIAMOND</c>).
    /// </summary>
    public List<CustomerTier> Tiers { get; set; } = [];

    /// <summary>
    /// <c>true</c>: chỉ khách vãng lai; <c>false</c>: chỉ thành viên.
    /// </summary>
    public bool? IsGuest { get; set; }

    /// <summary>
    /// Có ít nhất một điều kiện ràng buộc (giá trị khác <c>null</c> hoặc danh sách không rỗng).
    /// </summary>
    public bool HasAnyCondition() =>
        PeakHour is not null
        || IsGuest is not null
        || BranchIds is { Count: > 0 }
        || Channels is { Count: > 0 }
        || Tiers is { Count: > 0 };
}
