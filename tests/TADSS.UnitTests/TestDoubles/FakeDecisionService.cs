using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.Runtime.Api.Services;
using TADSS.UnitTests.TestData;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// <see cref="IDecisionService"/> giả viết tay cho test controller/HTTP: ghi lại mọi lần gọi (request, key, correlation id)
/// và để test chọn hành vi (mặc định trả response mẫu của INTEGRATION_SPEC §3.3).
/// </summary>
public sealed class FakeDecisionService : IDecisionService
{
    /// <summary>Một lần gọi <see cref="EvaluateAsync"/>.</summary>
    public sealed record Call(DecisionRequestDto Request, string IdempotencyKey, string? CorrelationId);

    private readonly List<Call> _calls = [];

    /// <summary>Hành vi hiện tại; <c>null</c> nghĩa là trả <see cref="V2Samples.ResultJson"/>.</summary>
    public Func<DecisionRequestDto, Task<DecisionResultDto>>? Behavior { get; set; }

    /// <summary>Các lần gọi theo thứ tự.</summary>
    public IReadOnlyList<Call> Calls => _calls;

    /// <summary>Xóa lịch sử gọi và trả hành vi về mặc định.</summary>
    public void Reset()
    {
        _calls.Clear();
        Behavior = null;
    }

    /// <summary>Response mẫu §3.3 đã deserialize.</summary>
    public static DecisionResultDto SampleResult() =>
        System.Text.Json.JsonSerializer.Deserialize<DecisionResultDto>(V2Samples.ResultJson, Shared.DecisionContractSDK.Common.TadssJson.Options)
            ?? throw new InvalidOperationException("Deserialize trả null.");

    /// <inheritdoc />
    public Task<DecisionResultDto> EvaluateAsync(
        DecisionRequestDto request,
        string idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        _calls.Add(new Call(request, idempotencyKey, correlationId));
        return Behavior is null ? Task.FromResult(SampleResult()) : Behavior(request);
    }
}
