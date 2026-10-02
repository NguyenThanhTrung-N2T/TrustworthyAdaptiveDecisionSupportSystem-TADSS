using Shared.DecisionContractSDK.Domain;

namespace Shared.DecisionContractSDK.V2.Domain;

/// <summary>
/// Ảnh chụp nhu cầu tại khung giờ của reservation, do client tính khi tạo booking (INTEGRATION_SPEC §3.3).
/// Không thuộc <c>request_hash</c> vì thay đổi giữa các lần retry.
/// </summary>
public sealed class DemandSnapshot
{
    /// <summary>Tỷ lệ tài nguyên hoạt động của chi nhánh đã có booking giao với khung giờ, trong <c>[0, 1]</c>.</summary>
    public decimal OccupancyRate { get; }

    /// <summary>Số người đăng ký chờ slot còn hạn.</summary>
    public int WaitlistCount { get; }

    /// <summary>Khởi tạo ảnh chụp nhu cầu.</summary>
    /// <param name="occupancyRate">Tỷ lệ kín trong <c>[0, 1]</c>.</param>
    /// <param name="waitlistCount">Số người chờ, không âm.</param>
    /// <exception cref="DomainInvariantException">Khi giá trị ngoài miền.</exception>
    public DemandSnapshot(decimal occupancyRate, int waitlistCount)
    {
        if (occupancyRate is < 0m or > 1m)
        {
            throw new DomainInvariantException("INVALID_OCCUPANCY_RATE", "OccupancyRate must be between 0 and 1.", nameof(OccupancyRate));
        }

        if (waitlistCount < 0)
        {
            throw new DomainInvariantException("INVALID_WAITLIST_COUNT", "WaitlistCount must be greater than or equal to 0.", nameof(WaitlistCount));
        }

        OccupancyRate = occupancyRate;
        WaitlistCount = waitlistCount;
    }
}
