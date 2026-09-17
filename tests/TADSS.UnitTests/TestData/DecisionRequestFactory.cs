using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;

namespace TADSS.UnitTests.TestData;

internal static class DecisionRequestFactory
{
    public static readonly DateTime RequestedAt =
        new(2026, 9, 12, 15, 0, 0, 123, DateTimeKind.Utc);

    public static readonly DateTime StartTime =
        new(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime EndTime =
        new(2026, 9, 15, 20, 0, 0, DateTimeKind.Utc);

    public static DecisionRequestDto ValidDecisionRequest() => new()
    {
        ReservationId = Guid.Parse("018e3a2b-8a71-7000-8c24-112233445566"),
        CourtId = Guid.Parse("018e3a2b-8a71-7000-8c24-aabbccddeeff"),
        CourtTypeId = Guid.Parse("018e3a2b-8a71-7000-8c24-001122334455"),
        BranchId = Guid.Parse("018e3a2b-8a71-7000-8c24-998877665544"),
        CustomerId = Guid.Parse("018e3a2b-8a71-7000-8c24-123456789abc"),
        CustomerTier = "GOLD",
        StartTime = StartTime,
        EndTime = EndTime,
        BookingType = "SINGLE",
        TotalPrice = 300000m,
        DepositAmount = 150000m,
        PaymentStatus = "PARTIALLY_PAID",
        Priority = "NORMAL",
        RequestedAt = RequestedAt,
        Metadata = new Dictionary<string, string>
        {
            ["source"] = "MobileApp",
            ["tenant"] = "smashcourt"
        }
    };

    public static DecisionResultDto ValidDecisionResult() => new()
    {
        DecisionId = Guid.Parse("018e3a2b-9f88-7111-a1b2-001122334455"),
        ReservationId = Guid.Parse("018e3a2b-8a71-7000-8c24-112233445566"),
        Status = "NEED_APPROVAL",
        StrategyName = "DefaultDecisionStrategy",
        StrategyVersion = "1.0.0",
        ExecutionTimeMs = 14,
        FallbackUsed = false,
        PrimaryReason = "DEPOSIT_BELOW_THRESHOLD",
        Reasons = ["Khách hàng hạng Standard chưa thanh toán đủ mức cọc tối thiểu 30%"],
        Recommendations =
        [
            new DecisionRecommendationDto
            {
                Type = "REQUIRE_DEPOSIT",
                Description = "Yêu cầu thanh toán bổ sung 60,000 VND",
                SuggestedCourtId = null,
                SuggestedStartTime = null,
                SuggestedEndTime = null,
                SuggestedPrice = 300000m,
                ConfidenceScore = 1.0
            }
        ],
        EvaluatedAt = RequestedAt.AddMilliseconds(14)
    };

    public static OverrideDecisionRequestDto ValidOverrideRequest() => new()
    {
        Decision = new OverrideTargetPayloadDto { Status = "APPROVED" },
        OverrideReason = "Khách hàng thân thiết được quản lý xác nhận ngoại lệ",
        OverriddenBy = "manager-usr-001"
    };
}
