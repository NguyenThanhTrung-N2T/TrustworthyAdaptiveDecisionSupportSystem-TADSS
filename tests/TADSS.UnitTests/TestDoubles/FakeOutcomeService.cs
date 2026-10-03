using Shared.DecisionContractSDK.DTOs.Outcomes;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Outcomes;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// <see cref="IOutcomeService"/> giả viết tay cho test controller/HTTP: ghi lại mọi lần gọi (decisionId, request) và để test chọn hành vi
/// (mặc định trả response mẫu của INTEGRATION_SPEC §3.7).
/// </summary>
public sealed class FakeOutcomeService : IOutcomeService
{
    /// <summary>Một lần gọi <see cref="RecordAsync"/>.</summary>
    public sealed record Call(Guid DecisionId, OutcomeRequestDto Request);

    private readonly List<Call> _calls = [];

    /// <summary>Hành vi hiện tại; <c>null</c> nghĩa là trả <see cref="SampleResponse"/>.</summary>
    public Func<Call, Task<OutcomeResponseDto>>? Behavior { get; set; }

    /// <summary>Các lần gọi theo thứ tự.</summary>
    public IReadOnlyList<Call> Calls => _calls;

    /// <summary>Xóa lịch sử gọi và trả hành vi về mặc định.</summary>
    public void Reset()
    {
        _calls.Clear();
        Behavior = null;
    }

    /// <summary>Response mẫu §3.7: <c>NO_SHOW</c> xảy ra 18:30 (+07:00), ghi nhận lúc 19:00:02 UTC.</summary>
    public static OutcomeResponseDto SampleResponse(Guid decisionId) => new()
    {
        DecisionId = decisionId,
        Outcome = DecisionOutcome.NO_SHOW,
        OccurredAt = new DateTimeOffset(2026, 11, 20, 11, 30, 0, TimeSpan.Zero),
        ReportedAt = new DateTimeOffset(2026, 11, 20, 19, 0, 2, TimeSpan.Zero),
    };

    /// <inheritdoc />
    public Task<OutcomeResponseDto> RecordAsync(Guid decisionId, OutcomeRequestDto request, CancellationToken cancellationToken)
    {
        var call = new Call(decisionId, request);
        _calls.Add(call);
        return Behavior is null ? Task.FromResult(SampleResponse(decisionId)) : Behavior(call);
    }
}
