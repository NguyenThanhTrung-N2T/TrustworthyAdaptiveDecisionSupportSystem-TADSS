using System.Text.Json;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.DTOs.Outcomes;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Middlewares;

namespace TADSS.Runtime.Api.Services;

/// <summary>
/// Hiện thực <see cref="IDecisionQueryService"/>: đọc <see cref="DecisionDetailRecord"/> qua <see cref="IDecisionAuditStore"/>
/// (không dùng DbContext) rồi dựng <see cref="DecisionDetailDto"/> theo INTEGRATION_SPEC §3.4. Phép biến đổi thuần: không đọc
/// đồng hồ, không ghi gì, không chạy lại strategy.
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
    private readonly IDecisionAuditStore _auditStore;

    /// <summary>Khởi tạo service tra cứu.</summary>
    /// <param name="auditStore">Kho audit (đọc chi tiết theo <c>decisionId</c>).</param>
    public DecisionQueryService(IDecisionAuditStore auditStore)
    {
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
    }

    /// <inheritdoc />
    public async Task<DecisionDetailDto> GetDetailAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        var detail = await _auditStore.GetDecisionDetailAsync(decisionId, cancellationToken)
            ?? throw new DecisionNotFoundException(decisionId);

        var execution = detail.Execution;
        var request = Deserialize<DecisionRequestDto>(execution.RequestPayload, "request_payload");
        var response = Deserialize<DecisionResultDto>(execution.ResponsePayload, "response_payload");

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
            Override = detail.Override is { } overrideRecord ? ToResponse(overrideRecord, request.Currency) : null,
            Outcome = detail.Outcome is { } outcomeRecord ? ToResponse(outcomeRecord) : null,
            CreatedAt = execution.CreatedAt.ToUniversalTime(),
            CorrelationId = execution.CorrelationId
        };
    }

    private static OverrideResponseDto ToResponse(DecisionOverrideRecord record, string currency) => new()
    {
        DecisionId = record.DecisionId,
        Action = record.Action,
        // ck_do_deposit: rate và amount cùng có ⇔ APPROVE
        Deposit = record is { DepositRate: { } rate, DepositAmount: { } amount }
            ? new DepositDto { Rate = rate, Amount = amount, Currency = currency }
            : null,
        ActorId = record.ActorId,
        Reason = record.Reason,
        OverriddenAt = record.CreatedAt.ToUniversalTime()
    };

    private static OutcomeResponseDto ToResponse(DecisionOutcomeRecord record) => new()
    {
        DecisionId = record.DecisionId,
        Outcome = record.Outcome,
        OccurredAt = record.OccurredAt,
        ReportedAt = record.ReportedAt.ToUniversalTime()
    };

    private static T Deserialize<T>(string payload, string column)
        where T : class =>
        JsonSerializer.Deserialize<T>(payload, TadssJson.Options)
            ?? throw new JsonException($"Cột {column} của bản ghi audit là JSON null.");
}
