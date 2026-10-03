using System.Text.Json;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.DTOs.Outcomes;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Common;
using TADSS.Application.Exceptions;
using TADSS.Application.Governance;

namespace TADSS.Application.Decisions;

/// <summary>
/// Hiện thực <see cref="IDecisionQueryService"/>: đọc <see cref="DecisionDetailRecord"/> qua <see cref="IDecisionQueryStore"/>
/// (không dùng DbContext) rồi dựng <see cref="DecisionDetailDto"/> theo INTEGRATION_SPEC §3.4. Phép biến đổi thuần: không đọc
/// đồng hồ (chỉ hàng chờ dùng mốc hiện tại để loại bản hết hạn duyệt), không ghi gì, không chạy lại strategy.
/// <para>
/// <b>Nguồn dữ liệu:</b> <c>request</c> là <c>request_payload</c> deserialize bằng <see cref="TadssJson.Options"/>;
/// <c>result.status</c>/<c>deposit</c>/<c>reasonCode</c>/<c>reasons</c>/<c>recommendations</c>/<c>approvalExpiresAt</c> lấy từ
/// <c>response_payload</c> đã lưu (một <see cref="DecisionResultDto"/> — nơi duy nhất có <c>reasons</c>, <c>recommendations</c> và
/// <c>currency</c> của cọc), còn <c>strategyStatus</c> và toàn bộ <c>execution</c> lấy từ các cột của bản ghi. Override/outcome
/// lấy từ bản ghi tương ứng; tiền tệ của cọc override lấy từ request (bảng <c>decision_overrides</c> không có cột tiền tệ).
/// Payload hỏng/không giải mã được thì <see cref="JsonException"/> lan ra (500) thay vì bị che.
/// </para>
/// </summary>
public sealed class DecisionQueryService : IDecisionQueryService
{
    private readonly IDecisionQueryStore _queryStore;
    private readonly TimeProvider _timeProvider;

    /// <summary>Khởi tạo service tra cứu.</summary>
    /// <param name="queryStore">Kho audit (đọc chi tiết theo <c>decisionId</c> và hàng chờ duyệt).</param>
    /// <param name="timeProvider">Đồng hồ (mốc hết hạn duyệt của hàng chờ).</param>
    public DecisionQueryService(IDecisionQueryStore queryStore, TimeProvider timeProvider)
    {
        _queryStore = queryStore ?? throw new ArgumentNullException(nameof(queryStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<DecisionDetailDto> GetDetailAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        var detail = await _queryStore.GetDecisionDetailAsync(decisionId, cancellationToken)
            ?? throw new DecisionNotFoundException(decisionId);

        var execution = detail.Execution;
        var request = DecisionPayloadSerializer.DeserializeRequest(execution.RequestPayload);
        var response = DecisionPayloadSerializer.DeserializeResponse(execution.ResponsePayload);

        return new DecisionDetailDto
        {
            DecisionId = execution.DecisionId,
            ReservationId = execution.ReservationId,
            Request = request,
            Result = new DecisionDetailResultDto
            {
                Status = response.Status,
                StrategyStatus = execution.StrategyStatus,
                Deposit = response.Deposit,
                ReasonCode = response.ReasonCode,
                Reasons = response.Reasons,
                Recommendations = response.Recommendations,
                ApprovalExpiresAt = response.ApprovalExpiresAt
            },
            Execution = new ExecutionDetailDto
            {
                StrategyName = execution.StrategyName,
                StrategyVersion = execution.StrategyVersion,
                ExecutionType = execution.ExecutionType,
                ExecutionTimeMs = execution.ExecutionTimeMs,
                FallbackUsed = execution.FallbackUsed,
                FailureReason = execution.FailureReason,
                CacheHit = execution.CacheHit
            },
            Override = detail.Override is { } overrideRecord ? GovernanceResponseMapper.ToResponse(overrideRecord, request.Currency) : null,
            Outcome = detail.Outcome is { } outcomeRecord ? GovernanceResponseMapper.ToResponse(outcomeRecord) : null,
            CreatedAt = execution.CreatedAt.ToUniversalTime(),
            CorrelationId = execution.CorrelationId
        };
    }

    /// <inheritdoc />
    public async Task<PagedResult<PendingApprovalItemDto>> GetPendingApprovalsAsync(
        Guid branchId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            throw new ContractValidationException(ErrorCodes.ValidationError, "page phải lớn hơn hoặc bằng 1.", "page");
        }

        if (pageSize < 1 || pageSize > ContractLimits.MaxPageSize)
        {
            throw new ContractValidationException(
                ErrorCodes.ValidationError, $"pageSize phải nằm trong [1, {ContractLimits.MaxPageSize}].", "pageSize");
        }

        // nowUtc (offset 0, cắt µs) cho store: Npgsql từ chối offset khác 0 và hạn duyệt cũng lưu theo µs
        var nowUtc = AuditTimestamps.TruncateToMicroseconds(_timeProvider.GetUtcNow());
        var result = await _queryStore.GetPendingApprovalsAsync(branchId, page, pageSize, nowUtc, cancellationToken);

        var items = result.Items.Select(GovernanceResponseMapper.ToPendingItem).ToArray();
        return PagedResult<PendingApprovalItemDto>.Create(items, page, pageSize, result.TotalItems);
    }
}
