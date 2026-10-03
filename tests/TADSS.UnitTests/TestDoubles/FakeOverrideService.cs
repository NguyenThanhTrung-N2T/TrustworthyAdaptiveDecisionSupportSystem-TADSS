using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using TADSS.Application.Governance;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// <see cref="IOverrideService"/> giả viết tay cho test controller/HTTP: ghi lại mọi lần gọi (decisionId, request, actor, key) và để test chọn
/// hành vi (mặc định trả response duyệt cọc 30% mẫu của INTEGRATION_SPEC §3.6).
/// </summary>
public sealed class FakeOverrideService : IOverrideService
{
    /// <summary>Một lần gọi <see cref="OverrideAsync"/>.</summary>
    public sealed record Call(Guid DecisionId, OverrideRequestDto Request, string ActorId, string IdempotencyKey);

    private readonly List<Call> _calls = [];

    /// <summary>Hành vi hiện tại; <c>null</c> nghĩa là trả <see cref="SampleResponse"/>.</summary>
    public Func<Call, Task<OverrideResponseDto>>? Behavior { get; set; }

    /// <summary>Các lần gọi theo thứ tự.</summary>
    public IReadOnlyList<Call> Calls => _calls;

    /// <summary>Xóa lịch sử gọi và trả hành vi về mặc định.</summary>
    public void Reset()
    {
        _calls.Clear();
        Behavior = null;
    }

    /// <summary>Response mẫu: duyệt cọc 30% của đơn 228.000 VND.</summary>
    public static OverrideResponseDto SampleResponse(Guid decisionId) => new()
    {
        DecisionId = decisionId,
        Action = Shared.DecisionContractSDK.Enums.OverrideAction.APPROVE,
        Deposit = new DepositDto { Rate = 0.3m, Amount = 68_400m, Currency = "VND" },
        ActorId = "manager-01",
        Reason = "Khách quen, chi nhánh bảo lãnh",
        OverriddenAt = new DateTimeOffset(2026, 11, 18, 3, 0, 0, TimeSpan.Zero),
    };

    /// <inheritdoc />
    public Task<OverrideResponseDto> OverrideAsync(
        Guid decisionId,
        OverrideRequestDto request,
        string actorId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var call = new Call(decisionId, request, actorId, idempotencyKey);
        _calls.Add(call);
        return Behavior is null ? Task.FromResult(SampleResponse(decisionId)) : Behavior(call);
    }
}
