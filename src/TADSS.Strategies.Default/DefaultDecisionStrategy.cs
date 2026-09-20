using System.Diagnostics;
using System.Globalization;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;

namespace TADSS.Strategies.Default;

/// <summary>
/// Chiến lược thẩm định đặt sân mặc định (Default Decision Strategy).
/// Đại diện cho chính sách bảo thủ tĩnh (Conservative Deterministic Baseline Policy),
/// đánh giá dựa trên tỷ lệ thanh toán cọc và hạng thành viên khách hàng.
/// </summary>
public class DefaultDecisionStrategy : IReservationDecisionStrategy
{
    /// <summary>
    /// Tên định danh duy nhất của chiến lược trong registry.
    /// </summary>
    public string StrategyName => "DefaultDecisionStrategy";

    /// <summary>
    /// Phiên bản của chiến lược thẩm định (SemVer).
    /// </summary>
    public string StrategyVersion => "1.0.0";

    /// <summary>
    /// Đánh giá yêu cầu đặt sân và đưa ra kết quả quyết định cùng khuyến nghị.
    /// </summary>
    /// <param name="reservation">Thực thể đặt sân (Rich Domain Model) đã qua xác thực bất biến.</param>
    /// <param name="cancellationToken">Token hủy thao tác bất đồng bộ.</param>
    /// <returns>Đối tượng kết quả thẩm định quyết định <see cref="DecisionResultDto"/>.</returns>
    public Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        var stopwatch = Stopwatch.StartNew();

        var reasons = new List<string>();
        var recommendations = new List<DecisionRecommendationDto>();
        string status;
        string primaryReason;

        // 1. Khách hàng đã thanh toán 100% chi phí
        if (reservation.Payment.IsFullyPaid)
        {
            status = DecisionStatus.APPROVED.ToString();
            primaryReason = ErrorCodes.ApprovedPaidInFull;
            reasons.Add("Khách hàng đã thanh toán 100% chi phí đặt sân.");
            reasons.Add("Khung giờ và sân đáp ứng đầy đủ điều kiện vận hành.");
        }
        // 2. Khách VIP hoặc Gold với mức cọc hợp lệ (VIP miễn cọc hoặc Gold cọc >= 30%)
        else if (IsTierMatch(reservation.Customer.Tier, "VIP"))
        {
            status = DecisionStatus.APPROVED.ToString();
            primaryReason = ErrorCodes.AutoApprovedTierEligible;
            reasons.Add("Khách hàng hạng VIP được hưởng chính sách phê duyệt ưu tiên.");
        }
        else if ((IsTierMatch(reservation.Customer.Tier, "GOLD") || IsTierMatch(reservation.Customer.Tier, "VÀNG")) && reservation.Payment.DepositPercentage >= 30m)
        {
            status = DecisionStatus.APPROVED.ToString();
            primaryReason = ErrorCodes.AutoApprovedTierEligible;
            reasons.Add($"Khách hàng hạng Gold đáp ứng chính sách cọc tối thiểu 30% (hiện tại: {reservation.Payment.DepositPercentage.ToString("F1", CultureInfo.InvariantCulture)}%).");
        }
        // 3. Đơn giá trị cao cần xem xét
        else if (reservation.Payment.TotalPrice >= 2_000_000m && reservation.Payment.DepositPercentage < 50m)
        {
            status = DecisionStatus.NEED_APPROVAL.ToString();
            primaryReason = ErrorCodes.ManualReviewHighValue;
            reasons.Add($"Đơn đặt sân giá trị cao ({reservation.Payment.TotalPrice.ToString("N0", CultureInfo.InvariantCulture)} VND) có mức cọc dưới 50%. Cần quản lý phê duyệt.");
            recommendations.Add(new DecisionRecommendationDto
            {
                Type = RecommendationType.REQUIRE_DEPOSIT.ToString(),
                Description = "Yêu cầu khách hàng thanh toán thêm cọc tối thiểu 50% để được tự động phê duyệt.",
                SuggestedPrice = reservation.Payment.TotalPrice,
                ConfidenceScore = 0.95
            });
        }
        // 4. Đơn tiêu chuẩn cọc >= 50%
        else if (reservation.Payment.DepositPercentage >= 50m)
        {
            status = DecisionStatus.APPROVED.ToString();
            primaryReason = ErrorCodes.ApprovedStandard;
            reasons.Add($"Mức cọc tiêu chuẩn {reservation.Payment.DepositPercentage.ToString("F1", CultureInfo.InvariantCulture)}% hợp lệ.");
        }
        // 5. Cọc dưới 30% -> NeedApproval
        else
        {
            status = DecisionStatus.NEED_APPROVAL.ToString();
            primaryReason = ErrorCodes.DepositBelowThreshold;
            var requiredDeposit = reservation.Payment.TotalPrice * 0.3m;
            var shortage = requiredDeposit - reservation.Payment.DepositAmount;

            reasons.Add($"Mức cọc hiện tại ({reservation.Payment.DepositPercentage.ToString("F1", CultureInfo.InvariantCulture)}%) thấp hơn mức tối thiểu 30%. Cần xem xét.");
            recommendations.Add(new DecisionRecommendationDto
            {
                Type = RecommendationType.REQUIRE_DEPOSIT.ToString(),
                Description = $"Yêu cầu khách hàng thanh toán bổ sung {shortage.ToString("N0", CultureInfo.InvariantCulture)} VND tiền cọc để đạt mức tối thiểu 30%.",
                SuggestedPrice = reservation.Payment.TotalPrice,
                ConfidenceScore = 1.0
            });
        }

        stopwatch.Stop();

        var result = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = reservation.ReservationId,
            Status = status,
            StrategyName = StrategyName,
            StrategyVersion = StrategyVersion,
            ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
            FallbackUsed = false,
            PrimaryReason = primaryReason,
            Reasons = reasons,
            Recommendations = recommendations,
            EvaluatedAt = DateTime.UtcNow
        };

        return Task.FromResult(result);
    }

    private static bool IsTierMatch(string? currentTier, string targetTier)
    {
        if (string.IsNullOrWhiteSpace(currentTier)) return false;
        return string.Equals(currentTier.Trim(), targetTier, StringComparison.OrdinalIgnoreCase);
    }
}
