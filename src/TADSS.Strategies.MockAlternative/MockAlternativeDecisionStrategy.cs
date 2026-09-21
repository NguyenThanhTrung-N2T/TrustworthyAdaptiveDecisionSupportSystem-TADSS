using System.Diagnostics;
using System.Globalization;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;

namespace TADSS.Strategies.MockAlternative;

/// <summary>
/// Chiến lược thẩm định đặt sân thay thế mô phỏng (Mock Alternative Decision Strategy).
/// <para>
/// Triển khai chính sách trình diễn tất định có chủ đích (Deliberately Distinct Deterministic Demonstration Policy)
/// nhằm kiểm chứng khả năng phân giải và thay thế chiến lược linh hoạt (Strategy Interchangeability) của TADSS Runtime.
/// </para>
/// <remarks>
/// Đây là chiến lược phục vụ nghiên cứu và thực nghiệm đối chứng (Ablation Baseline), không đại diện cho quy tắc nghiệp vụ cố định duy nhất.
/// </remarks>
/// </summary>
public class MockAlternativeDecisionStrategy : IReservationDecisionStrategy
{
    /// <summary>
    /// Tên định danh duy nhất của chiến lược trong registry.
    /// </summary>
    public string StrategyName => "MockAlternativeDecisionStrategy";

    /// <summary>
    /// Phiên bản của chiến lược thẩm định (SemVer).
    /// </summary>
    public string StrategyVersion => "1.0.0";

    /// <summary>
    /// Đánh giá yêu cầu đặt sân dựa trên phân tích khung giờ cao điểm và tỷ lệ cọc linh hoạt.
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
        // 2. Khách VIP được hưởng chính sách phê duyệt ưu tiên
        else if (IsTierMatch(reservation.Customer.Tier, "VIP"))
        {
            status = DecisionStatus.APPROVED.ToString();
            primaryReason = ErrorCodes.AutoApprovedTierEligible;
            reasons.Add("Khách hàng hạng VIP được hưởng chính sách phê duyệt ưu tiên.");
        }
        // 3. Phân tích khung giờ cao điểm (Peak Hours)
        else if (IsPeakHour(reservation.Schedule.StartTime))
        {
            if (reservation.Payment.DepositPercentage >= 50m)
            {
                status = DecisionStatus.APPROVED.ToString();
                primaryReason = ErrorCodes.ApprovedStandard;
                reasons.Add($"Khung giờ cao điểm: Mức cọc {reservation.Payment.DepositPercentage.ToString("F1", CultureInfo.InvariantCulture)}% đáp ứng chính sách tối thiểu 50%.");
            }
            else
            {
                status = DecisionStatus.NEED_APPROVAL.ToString();
                primaryReason = ErrorCodes.DepositBelowThreshold;
                var requiredDeposit = reservation.Payment.TotalPrice * 0.5m;
                var shortage = requiredDeposit - reservation.Payment.DepositAmount;

                reasons.Add($"Khung giờ cao điểm yêu cầu mức cọc tối thiểu 50% (hiện tại: {reservation.Payment.DepositPercentage.ToString("F1", CultureInfo.InvariantCulture)}%). Cần quản lý xem xét.");

                recommendations.Add(new DecisionRecommendationDto
                {
                    Type = RecommendationType.REQUIRE_DEPOSIT.ToString(),
                    Description = $"Yêu cầu khách hàng thanh toán bổ sung {shortage.ToString("N0", CultureInfo.InvariantCulture)} VND tiền cọc để đạt mức tối thiểu 50% trong giờ cao điểm.",
                    SuggestedPrice = reservation.Payment.TotalPrice,
                    ConfidenceScore = 1.0
                });

                recommendations.Add(new DecisionRecommendationDto
                {
                    Type = RecommendationType.ALTERNATIVE_SLOT.ToString(),
                    Description = "Gợi ý chuyển sang khung giờ thấp điểm (trước 17:00 các ngày trong tuần) để được áp dụng chính sách cọc 30%.",
                    SuggestedPrice = reservation.Payment.TotalPrice,
                    ConfidenceScore = 0.85
                });
            }
        }
        // 4. Khung giờ thấp điểm (Off-Peak Hours)
        else
        {
            if (reservation.Payment.DepositPercentage >= 30m)
            {
                status = DecisionStatus.APPROVED.ToString();
                primaryReason = ErrorCodes.ApprovedStandard;
                reasons.Add($"Khung giờ thấp điểm: Mức cọc {reservation.Payment.DepositPercentage.ToString("F1", CultureInfo.InvariantCulture)}% đáp ứng chính sách tối thiểu 30%.");
            }
            else
            {
                status = DecisionStatus.NEED_APPROVAL.ToString();
                primaryReason = ErrorCodes.DepositBelowThreshold;
                var requiredDeposit = reservation.Payment.TotalPrice * 0.3m;
                var shortage = requiredDeposit - reservation.Payment.DepositAmount;

                reasons.Add($"Khung giờ thấp điểm yêu cầu mức cọc tối thiểu 30% (hiện tại: {reservation.Payment.DepositPercentage.ToString("F1", CultureInfo.InvariantCulture)}%). Cần xem xét.");

                recommendations.Add(new DecisionRecommendationDto
                {
                    Type = RecommendationType.REQUIRE_DEPOSIT.ToString(),
                    Description = $"Yêu cầu khách hàng thanh toán bổ sung {shortage.ToString("N0", CultureInfo.InvariantCulture)} VND tiền cọc để đạt mức tối thiểu 30%.",
                    SuggestedPrice = reservation.Payment.TotalPrice,
                    ConfidenceScore = 1.0
                });
            }
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

    /// <summary>
    /// Kiểm tra khung giờ có phải là giờ cao điểm hay không.
    /// <para>
    /// Thứ 2 - Thứ 6: 17:00 - 21:00 (17h đến trước 21h).
    /// Thứ 7 - Chủ Nhật: 08:00 - 21:00 (8h đến trước 21h).
    /// </para>
    /// </summary>
    public static bool IsPeakHour(DateTime dateTime)
    {
        var dayOfWeek = dateTime.DayOfWeek;
        var timeOfDay = dateTime.TimeOfDay;

        var isWeekend = dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        if (isWeekend)
        {
            return timeOfDay >= new TimeSpan(8, 0, 0) && timeOfDay < new TimeSpan(21, 0, 0);
        }

        // Weekdays: 17:00 - 21:00
        return timeOfDay >= new TimeSpan(17, 0, 0) && timeOfDay < new TimeSpan(21, 0, 0);
    }

    private static bool IsTierMatch(string? currentTier, string targetTier)
    {
        if (string.IsNullOrWhiteSpace(currentTier)) return false;
        return string.Equals(currentTier.Trim(), targetTier, StringComparison.OrdinalIgnoreCase);
    }
}
