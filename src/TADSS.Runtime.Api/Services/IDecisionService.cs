using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;

namespace TADSS.Runtime.Api.Services;

/// <summary>
/// Service trung tâm xử lý vòng đời quyết định, kiểm toán và quản trị tại TADSS.
/// </summary>
public interface IDecisionService
{
    Task<DecisionResultDto> EvaluateDecisionAsync(DecisionRequestDto request, string? idempotencyKey, CancellationToken cancellationToken = default);
    Task<DecisionDetailDto?> GetDecisionDetailAsync(Guid decisionId, CancellationToken cancellationToken = default);
    Task<PagedResult<PendingApprovalItemDto>> GetPendingApprovalsAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<OverrideDecisionResponseDto?> OverrideDecisionAsync(Guid decisionId, OverrideDecisionRequestDto request, CancellationToken cancellationToken = default);
}
