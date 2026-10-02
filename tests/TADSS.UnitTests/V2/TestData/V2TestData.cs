using Shared.DecisionContractSDK.V2.Decisions;
using Shared.DecisionContractSDK.V2.Domain;
using Shared.DecisionContractSDK.V2.Enums;

namespace TADSS.UnitTests.V2.TestData;

/// <summary>Dữ liệu mẫu dùng chung cho test SDK v2 và strategy v2.</summary>
internal static class V2TestData
{
    public static readonly TimeZoneInfo HoChiMinh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    public static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Thứ Sáu 20/11/2026 18:00 giờ Việt Nam (giờ cao điểm theo lịch mặc định).</summary>
    public static readonly DateTimeOffset FridayPeakStart = new(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(7));

    /// <summary>Lịch cao điểm mặc định: thứ Hai–Sáu 17:00–21:00, thứ Bảy–Chủ Nhật 08:00–21:00.</summary>
    public static BranchCalendar StandardCalendar()
    {
        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(day => new PeakWindow(day, new TimeOnly(17, 0), new TimeOnly(21, 0)));
        var weekend = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }
            .Select(day => new PeakWindow(day, new TimeOnly(8, 0), new TimeOnly(21, 0)));
        return new BranchCalendar(weekdays.Concat(weekend));
    }

    /// <summary>Ngữ cảnh đánh giá; mặc định 18/11/2026 02:15:00Z với lịch cao điểm chuẩn.</summary>
    public static DecisionContext CreateContext(DateTimeOffset? evaluatedAtUtc = null, BranchCalendar? calendar = null) =>
        new(evaluatedAtUtc ?? new DateTimeOffset(2026, 11, 18, 2, 15, 0, TimeSpan.Zero), calendar ?? StandardCalendar());

    public static ReservationItem CreateItem(DateTimeOffset start, double hours = 2, decimal price = 240_000m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(hours), price);

    public static CustomerHistory CreateHistory(int noShowCount = 0) =>
        new(completedCount: 12, noShowCount: noShowCount, cancelledCount: 2, lateCancelledCount: 0, paymentExpiredCount: 0, lastCompletedAt: null);

    public static CustomerProfile CreateMember(CustomerTier tier = CustomerTier.GOLD, CustomerHistory? history = null) =>
        new(isGuest: false, customerId: Guid.NewGuid(), tier: tier,
            memberSince: new DateTimeOffset(2025, 3, 14, 8, 0, 0, TimeSpan.FromHours(7)), history: history);

    public static CustomerProfile CreateGuest(CustomerHistory? history = null) =>
        new(isGuest: true, customerId: null, tier: CustomerTier.STANDARD, memberSince: null, history: history);

    public static Reservation CreateReservation(
        IReadOnlyList<ReservationItem>? items = null,
        CustomerProfile? customer = null,
        decimal totalPrice = 228_000m,
        TimeZoneInfo? timeZone = null,
        string currency = "VND",
        Channel channel = Channel.ONLINE,
        Guid? reservationId = null,
        Guid? branchId = null,
        DemandSnapshot? demand = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            reservationId: reservationId ?? Guid.NewGuid(),
            branchId: branchId ?? Guid.NewGuid(),
            branchTimeZone: timeZone ?? HoChiMinh,
            items: items ?? new[] { CreateItem(FridayPeakStart) },
            customer: customer ?? CreateMember(),
            totalPrice: totalPrice,
            currency: currency,
            channel: channel,
            requestedAt: new DateTimeOffset(2026, 11, 18, 9, 15, 0, TimeSpan.FromHours(7)),
            demand: demand,
            metadata: metadata);
}
