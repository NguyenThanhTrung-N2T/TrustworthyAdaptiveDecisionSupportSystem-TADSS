using System.Text.Json.Nodes;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;

namespace TADSS.UnitTests.TestData;

/// <summary>
/// Dữ liệu dựng sẵn cho test use case quản trị (override, outcome, hàng chờ duyệt): bản ghi đánh giá <c>PRIMARY</c>/<c>SHADOW</c>
/// với <c>request_payload</c> đọc được (tổng tiền, tiền tệ, khách, các item) và các trường INV-7 nhất quán.
/// </summary>
internal static class GovernanceTestData
{
    /// <summary>Mốc "bây giờ" mặc định của các test quản trị.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 18, 2, 30, 0, TimeSpan.Zero);

    /// <summary>Chi nhánh của request mẫu (<see cref="ContractSamples.RequestJson"/>).</summary>
    public static readonly Guid BranchId = Guid.Parse("1f2e3d4c-0000-4000-8000-000000000001");

    /// <summary>
    /// Dựng bản ghi đánh giá. <paramref name="status"/> <c>NEED_APPROVAL</c> thì có hạn duyệt (mặc định <see cref="Now"/> + 30 phút),
    /// <c>APPROVED</c> thì có cọc; <paramref name="startTimes"/> là các <c>startTime</c> của item (chuỗi ISO 8601 có offset).
    /// </summary>
    public static DecisionExecutionRecord Execution(
        Guid decisionId,
        DecisionStatus status = DecisionStatus.NEED_APPROVAL,
        ExecutionRole role = ExecutionRole.PRIMARY,
        DateTimeOffset? approvalExpiresAt = null,
        decimal totalPrice = 228_000m,
        string currency = "VND",
        Guid? reservationId = null,
        Guid? branchId = null,
        DateTimeOffset? createdAt = null,
        bool fallbackUsed = false,
        string reasonCode = "PEAK_HOUR_NEED_APPROVAL",
        string[]? startTimes = null,
        bool isGuest = false,
        CustomerTier tier = CustomerTier.GOLD)
    {
        var reservation = reservationId ?? Guid.NewGuid();
        var branch = branchId ?? BranchId;
        var payload = ContractSamples.MutateRequest(root =>
        {
            root["reservationId"] = reservation.ToString();
            root["branchId"] = branch.ToString();
            root["totalPrice"] = totalPrice;
            root["currency"] = currency;
            var customer = ContractSamples.Object(root["customer"]);
            customer["isGuest"] = isGuest;
            customer["tier"] = tier.ToString();
            if (startTimes is not null)
            {
                var items = new JsonArray();
                foreach (var start in startTimes)
                {
                    items.Add(new JsonObject
                    {
                        ["resourceId"] = "c0a8012e-0000-4000-8000-0000000000aa",
                        ["resourceTypeId"] = "c0a8012e-0000-4000-8000-0000000000bb",
                        ["startTime"] = start,
                        ["endTime"] = DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture).AddHours(1).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                        ["price"] = 100000,
                    });
                }

                root["items"] = items;
            }
        });

        var needsApproval = status == DecisionStatus.NEED_APPROVAL;
        var approved = status == DecisionStatus.APPROVED;
        return new DecisionExecutionRecord(
            DecisionId: decisionId,
            ReservationId: reservation,
            BranchId: branch,
            Role: role,
            StrategyName: "DefaultDecisionStrategy",
            StrategyVersion: "2.0.0",
            StrategyStatus: status,
            Status: status,
            DepositRate: approved ? 0.5m : null,
            DepositAmount: approved ? totalPrice / 2 : null,
            ReasonCode: reasonCode,
            ExecutionType: ExecutionType.AUTOMATED,
            ExecutionTimeMs: 3,
            FallbackUsed: fallbackUsed,
            FailureReason: null,
            CacheHit: false,
            ApprovalExpiresAt: needsApproval ? approvalExpiresAt ?? Now.AddMinutes(30) : null,
            RequestHash: new string('a', 64),
            RequestPayload: payload,
            ResponsePayload: ContractSamples.ResultJson,
            IdempotencyKey: $"eval-{decisionId:N}",
            CorrelationId: null,
            CreatedAt: createdAt ?? Now.AddMinutes(-1));
    }
}
