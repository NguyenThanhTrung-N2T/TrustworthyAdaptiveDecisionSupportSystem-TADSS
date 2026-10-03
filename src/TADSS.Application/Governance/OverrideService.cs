using Microsoft.Extensions.Logging;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Common;
using TADSS.Application.Decisions;
using TADSS.Application.Exceptions;

namespace TADSS.Application.Governance;

/// <summary>
/// Hiện thực <see cref="IOverrideService"/> (INTEGRATION_SPEC §3.6, F-03, F-04). Điều phối: kiểm tra request, đọc quyết định qua
/// <see cref="IDecisionQueryStore"/>, quyết định replay/xung đột/từ chối, quy đổi cọc bằng <see cref="DepositCalculator"/> (một nơi duy nhất)
/// rồi ghi qua <see cref="IDecisionGovernanceStore"/>. Dùng một <c>nowUtc</c> (cắt µs, offset 0) cho cả kiểm tra hạn duyệt lẫn <c>created_at</c>.
/// Không log <c>reason</c>.
/// <para>
/// Ghi chú: kiểm tra "chưa có outcome" rồi mới ghi override không khóa nguyên tử với việc ghi outcome (F-06); tranh chấp
/// override-override do ràng buộc <c>UNIQUE(decision_id)</c> bảo đảm.
/// </para>
/// </summary>
public sealed class OverrideService : IOverrideService
{
    private readonly IDecisionQueryStore _queryStore;
    private readonly IDecisionGovernanceStore _governanceStore;
    private readonly DepositCalculator _depositCalculator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OverrideService> _logger;

    /// <summary>Khởi tạo service override.</summary>
    /// <param name="queryStore">Kho đọc chi tiết quyết định.</param>
    /// <param name="governanceStore">Kho ghi override.</param>
    /// <param name="depositCalculator">Bộ quy đổi cọc dùng chung.</param>
    /// <param name="timeProvider">Đồng hồ.</param>
    /// <param name="logger">Logger.</param>
    public OverrideService(
        IDecisionQueryStore queryStore,
        IDecisionGovernanceStore governanceStore,
        DepositCalculator depositCalculator,
        TimeProvider timeProvider,
        ILogger<OverrideService> logger)
    {
        _queryStore = queryStore ?? throw new ArgumentNullException(nameof(queryStore));
        _governanceStore = governanceStore ?? throw new ArgumentNullException(nameof(governanceStore));
        _depositCalculator = depositCalculator ?? throw new ArgumentNullException(nameof(depositCalculator));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<OverrideResponseDto> OverrideAsync(
        Guid decisionId,
        OverrideRequestDto request,
        string actorId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        // (1) Sai hình thức → 400, trước mọi truy cập kho
        OverrideRequestValidator.Validate(request);

        // (2) Không có hoặc SHADOW (không bao giờ lộ cho client) → 404
        var detail = await _queryStore.GetDecisionDetailAsync(decisionId, cancellationToken);
        if (detail is null || detail.Execution.Role != ExecutionRole.PRIMARY)
        {
            throw new DecisionNotFoundException(decisionId);
        }

        var execution = detail.Execution;
        var order = DecisionPayloadSerializer.DeserializeRequest(execution.RequestPayload);
        OverrideRequestValidator.ValidateAgainstOrder(request, order.TotalPrice, order.Currency);

        // (3) Đã có override: replay hoặc xung đột
        if (detail.Override is { } existing)
        {
            return ResolveExisting(existing, request, actorId, idempotencyKey, order.Currency);
        }

        // (4) Không còn chờ duyệt hoặc đã có outcome
        if (execution.Status != DecisionStatus.NEED_APPROVAL || detail.Outcome is not null)
        {
            throw DecisionStateConflictException.NotPendingApproval(decisionId);
        }

        // (5) Hết hạn duyệt (so nghiêm ngặt: đúng bằng now là đã hết hạn)
        var nowUtc = AuditTimestamps.TruncateToMicroseconds(_timeProvider.GetUtcNow());
        if (execution.ApprovalExpiresAt is not { } expiresAt || expiresAt <= nowUtc)
        {
            throw DecisionStateConflictException.ApprovalExpired(decisionId);
        }

        // (6) Ghi; thua tranh chấp UNIQUE(decision_id) thì xử lý như (3) với bản thắng
        var record = BuildRecord(decisionId, request, actorId, idempotencyKey, order, nowUtc);
        var inserted = await _governanceStore.InsertOverrideAsync(record, cancellationToken);
        if (inserted.IsExistingWinner)
        {
            return ResolveExisting(inserted.Stored, request, actorId, idempotencyKey, order.Currency);
        }

        LogOverride(decisionId, request.Action, isReplay: false);
        return GovernanceResponseMapper.ToResponse(inserted.Stored, order.Currency);
    }

    private DecisionOverrideRecord BuildRecord(
        Guid decisionId,
        OverrideRequestDto request,
        string actorId,
        string idempotencyKey,
        DecisionRequestDto order,
        DateTimeOffset nowUtc)
    {
        // Một nơi quy đổi cọc: rate → amount (Calculate) hoặc amount → rate (FromAmount); REJECT không có cọc
        var deposit = request.Deposit switch
        {
            { Rate: { } rate } => _depositCalculator.Calculate(order.TotalPrice, rate, order.Currency),
            { Amount: { } amount } => _depositCalculator.FromAmount(order.TotalPrice, amount, order.Currency),
            _ => null,
        };

        return new DecisionOverrideRecord(
            decisionId,
            request.Action,
            deposit?.Rate,
            deposit?.Amount,
            actorId,
            request.Reason,
            idempotencyKey,
            nowUtc);
    }

    private OverrideResponseDto ResolveExisting(
        DecisionOverrideRecord existing,
        OverrideRequestDto request,
        string actorId,
        string idempotencyKey,
        string currency)
    {
        // Khác key → quyết định đã được override bằng yêu cầu khác (kể cả cùng nội dung)
        if (!string.Equals(existing.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
        {
            throw DecisionStateConflictException.AlreadyOverridden(existing.DecisionId);
        }

        // Cùng key khác payload → dùng lại key sai cách
        if (!OverridePayloadComparer.IsSamePayload(existing, request, actorId))
        {
            throw new IdempotencyConflictException(
                idempotencyKey,
                "Idempotency key đã được dùng cho một override có nội dung khác.");
        }

        LogOverride(existing.DecisionId, existing.Action, isReplay: true);
        return GovernanceResponseMapper.ToResponse(existing, currency);
    }

    private void LogOverride(Guid decisionId, OverrideAction action, bool isReplay)
    {
        // Không log reason (văn bản tự do của người dùng)
        _logger.LogInformation(
            "Override quyết định {DecisionId}: {Action}, replay {IsReplay}.",
            decisionId,
            action,
            isReplay);
    }
}
