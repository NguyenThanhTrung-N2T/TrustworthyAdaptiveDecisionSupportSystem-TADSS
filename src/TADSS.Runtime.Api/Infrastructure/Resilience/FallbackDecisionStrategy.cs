using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;

namespace TADSS.Runtime.Api.Infrastructure.Resilience;

/// <summary>
/// Chiến lược an toàn dự phòng (Fallback Decision Strategy).
/// Tự động kích hoạt khi chiến lược chính bị timeout (>500ms) hoặc gặp ngoại lệ bất ngờ.
/// Invariant: Không thực hiện external I/O và không bao giờ ném ngoại lệ kể cả khi thiếu ngữ cảnh Reservation.
/// </summary>
public class FallbackDecisionStrategy : IReservationDecisionStrategy
{
    public string StrategyName => "FallbackDecisionStrategy";
    public string StrategyVersion => "1.0.0";

    public Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default)
        => EvaluateFailSafeAsync(reservation, cancellationToken);

    public Task<DecisionResultDto> EvaluateFailSafeAsync(Reservation? reservation, CancellationToken cancellationToken = default)
    {
        var result = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = reservation?.ReservationId ?? Guid.Empty,
            Status = DecisionStatus.NEED_APPROVAL.ToString(),
            StrategyName = StrategyName,
            StrategyVersion = StrategyVersion,
            ExecutionTimeMs = 0,
            FallbackUsed = true,
            PrimaryReason = ErrorCodes.FallbackSafetyMode,
            Reasons =
            [
                "Chế độ an toàn tự động kích hoạt do chiến lược chính gặp sự cố hoặc quá thời hạn xử lý.",
                "Đơn đặt sân được chuyển sang trạng thái Cần duyệt thủ công và mặc định áp dụng mức thanh toán 100% chi phí để đảm bảo an toàn."
            ],
            Recommendations =
            [
                new DecisionRecommendationDto
                {
                    Type = RecommendationType.REQUIRE_APPROVAL.ToString(),
                    Description = "Yêu cầu Quản lý chi nhánh kiểm tra, duyệt đơn thủ công hoặc áp dụng mức thanh toán mặc định 100%.",
                    SuggestedPrice = reservation?.Payment.TotalPrice,
                    ConfidenceScore = 1.0
                }
            ],
            EvaluatedAt = DateTime.UtcNow
        };

        return Task.FromResult(result);
    }
}
