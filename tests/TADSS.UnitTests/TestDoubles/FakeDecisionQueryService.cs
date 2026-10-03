using System.Text.Json;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.Runtime.Api.Services;
using TADSS.UnitTests.Sdk.Contracts;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// <see cref="IDecisionQueryService"/> giả viết tay cho test controller/HTTP: ghi lại các <c>decisionId</c> được tra và để
/// test chọn hành vi (mặc định trả chi tiết mẫu của INTEGRATION_SPEC §3.4).
/// </summary>
public sealed class FakeDecisionQueryService : IDecisionQueryService
{
    private readonly List<Guid> _requestedIds = [];

    /// <summary>Hành vi hiện tại; <c>null</c> nghĩa là trả chi tiết mẫu §3.4.</summary>
    public Func<Guid, Task<DecisionDetailDto>>? Behavior { get; set; }

    /// <summary>Các <c>decisionId</c> đã được tra theo thứ tự.</summary>
    public IReadOnlyList<Guid> RequestedIds => _requestedIds;

    /// <summary>Xóa lịch sử gọi và trả hành vi về mặc định.</summary>
    public void Reset()
    {
        _requestedIds.Clear();
        Behavior = null;
    }

    /// <summary>Chi tiết mẫu §3.4 đã deserialize.</summary>
    public static DecisionDetailDto SampleDetail() =>
        JsonSerializer.Deserialize<DecisionDetailDto>(DecisionDetailSchemaTests.DetailJson, TadssJson.Options)
            ?? throw new InvalidOperationException("Deserialize trả null.");

    /// <inheritdoc />
    public Task<DecisionDetailDto> GetDetailAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        _requestedIds.Add(decisionId);
        return Behavior is null ? Task.FromResult(SampleDetail()) : Behavior(decisionId);
    }
}
