using Shared.DecisionContractSDK.Domain;

namespace Shared.DecisionContractSDK.Domain;

/// <summary>
/// Lịch sử đặt chỗ của khách, do client tính từ các booking đã kết thúc (INTEGRATION_SPEC §3.3).
/// </summary>
public sealed class CustomerHistory
{
    /// <summary>Số booking đã hoàn tất.</summary>
    public int CompletedCount { get; }

    /// <summary>Số lần không đến.</summary>
    public int NoShowCount { get; }

    /// <summary>Số lần khách tự hủy và được hoàn đủ theo chính sách hủy.</summary>
    public int CancelledCount { get; }

    /// <summary>Số lần khách tự hủy nhưng không được hoàn đủ (hủy muộn).</summary>
    public int LateCancelledCount { get; }

    /// <summary>Số booking bị hệ thống hủy do hết hạn thanh toán.</summary>
    public int PaymentExpiredCount { get; }

    /// <summary>Thời điểm hoàn tất gần nhất (nếu có).</summary>
    public DateTimeOffset? LastCompletedAt { get; }

    /// <summary>Khởi tạo lịch sử khách; mọi số đếm không âm.</summary>
    /// <param name="completedCount">Số booking đã hoàn tất.</param>
    /// <param name="noShowCount">Số lần không đến.</param>
    /// <param name="cancelledCount">Số lần tự hủy được hoàn đủ.</param>
    /// <param name="lateCancelledCount">Số lần hủy muộn.</param>
    /// <param name="paymentExpiredCount">Số lần hết hạn thanh toán.</param>
    /// <param name="lastCompletedAt">Thời điểm hoàn tất gần nhất.</param>
    /// <exception cref="DomainInvariantException">Khi có số đếm âm.</exception>
    public CustomerHistory(
        int completedCount,
        int noShowCount,
        int cancelledCount,
        int lateCancelledCount,
        int paymentExpiredCount,
        DateTimeOffset? lastCompletedAt)
    {
        if (completedCount < 0 || noShowCount < 0 || cancelledCount < 0 || lateCancelledCount < 0 || paymentExpiredCount < 0)
        {
            throw new DomainInvariantException(
                "INVALID_CUSTOMER_HISTORY",
                "History counts must be greater than or equal to 0.",
                nameof(CustomerHistory));
        }

        CompletedCount = completedCount;
        NoShowCount = noShowCount;
        CancelledCount = cancelledCount;
        LateCancelledCount = lateCancelledCount;
        PaymentExpiredCount = paymentExpiredCount;
        LastCompletedAt = lastCompletedAt;
    }
}
