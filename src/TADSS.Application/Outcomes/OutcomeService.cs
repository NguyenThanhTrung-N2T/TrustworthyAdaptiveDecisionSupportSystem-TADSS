using Microsoft.Extensions.Logging;
using Shared.DecisionContractSDK.DTOs.Outcomes;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Common;
using TADSS.Application.Exceptions;
using TADSS.Application.Governance;

namespace TADSS.Application.Outcomes;

/// <summary>
/// Hiện thực <see cref="IOutcomeService"/> (INTEGRATION_SPEC §3.7, F-05). Chống trùng theo payload (<c>outcome</c> và <c>occurredAt</c> sau khi cắt µs,
/// để lần gửi lại khớp giá trị đã đọc lại từ <c>timestamptz</c>); thua tranh chấp <c>UNIQUE(decision_id)</c> được xử lý theo payload của bản thắng.
/// Mốc <c>reportedAt</c> là <c>nowUtc</c> cắt µs (offset 0).
/// <para>
/// Ghi chú: kiểm tra override/outcome không khóa nguyên tử với nhau (F-06); bảng không có cột idempotency key nên khóa không được lưu.
/// </para>
/// </summary>
public sealed class OutcomeService : IOutcomeService
{
    private readonly IDecisionQueryStore _queryStore;
    private readonly IDecisionGovernanceStore _governanceStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutcomeService> _logger;

    /// <summary>Khởi tạo service outcome.</summary>
    /// <param name="queryStore">Kho đọc chi tiết quyết định.</param>
    /// <param name="governanceStore">Kho ghi outcome.</param>
    /// <param name="timeProvider">Đồng hồ.</param>
    /// <param name="logger">Logger.</param>
    public OutcomeService(
        IDecisionQueryStore queryStore,
        IDecisionGovernanceStore governanceStore,
        TimeProvider timeProvider,
        ILogger<OutcomeService> logger)
    {
        _queryStore = queryStore ?? throw new ArgumentNullException(nameof(queryStore));
        _governanceStore = governanceStore ?? throw new ArgumentNullException(nameof(governanceStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<OutcomeResponseDto> RecordAsync(Guid decisionId, OutcomeRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Không có hoặc SHADOW (không bao giờ lộ cho client) → 404
        var detail = await _queryStore.GetDecisionDetailAsync(decisionId, cancellationToken);
        if (detail is null || detail.Execution.Role != ExecutionRole.PRIMARY)
        {
            throw new DecisionNotFoundException(decisionId);
        }

        var occurredAt = AuditTimestamps.TruncateToMicroseconds(request.OccurredAt);
        if (detail.Outcome is { } existing)
        {
            return ResolveExisting(existing, request, occurredAt);
        }

        var record = new DecisionOutcomeRecord(
            decisionId,
            request.Outcome,
            occurredAt,
            AuditTimestamps.TruncateToMicroseconds(_timeProvider.GetUtcNow()));
        var inserted = await _governanceStore.InsertOutcomeAsync(record, cancellationToken);
        if (inserted.IsExistingWinner)
        {
            return ResolveExisting(inserted.Stored, request, occurredAt);
        }

        LogOutcome(decisionId, request.Outcome, isReplay: false);
        return GovernanceResponseMapper.ToResponse(inserted.Stored);
    }

    private OutcomeResponseDto ResolveExisting(DecisionOutcomeRecord existing, OutcomeRequestDto request, DateTimeOffset occurredAt)
    {
        // So theo thời điểm (DateTimeOffset so theo UTC), cả hai phía đã cắt về µs
        var samePayload = existing.Outcome == request.Outcome
            && AuditTimestamps.TruncateToMicroseconds(existing.OccurredAt) == occurredAt;
        if (!samePayload)
        {
            throw DecisionStateConflictException.OutcomeAlreadyRecorded(existing.DecisionId);
        }

        LogOutcome(existing.DecisionId, existing.Outcome, isReplay: true);
        return GovernanceResponseMapper.ToResponse(existing);
    }

    private void LogOutcome(Guid decisionId, DecisionOutcome outcome, bool isReplay)
    {
        _logger.LogInformation(
            "Outcome quyết định {DecisionId}: {Outcome}, replay {IsReplay}.",
            decisionId,
            outcome,
            isReplay);
    }
}
