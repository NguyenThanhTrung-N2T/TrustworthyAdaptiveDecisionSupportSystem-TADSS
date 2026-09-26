using MediatR;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;

namespace TADSS.Runtime.Api.Infrastructure.Data.Auditing;

/// <summary>
/// In-process synchronous domain notification dispatched when a decision evaluation completes
/// (AUTOMATED, CACHE_HIT, or FALLBACK) to persist the append-only audit record within the same
/// PostgreSQL transaction boundary (Fail-Closed).
/// </summary>
/// <param name="Execution">The immutable machine execution snapshot to persist.</param>
public sealed record DecisionEvaluatedAuditNotification(DecisionExecution Execution) : INotification;

/// <summary>
/// Synchronous MediatR handler that writes the append-only <see cref="DecisionExecution"/> record
/// to <see cref="TadssDbContext"/> within the active request scope and transaction boundary.
/// </summary>
/// <remarks>
/// Architectural Invariant: Dispatched exactly once as the terminal persistence step of a decision evaluation.
/// This handler owns the single <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(CancellationToken)"/>
/// commit for the request scope; callers must not issue a duplicate save outside this handler.
/// </remarks>
public sealed class DecisionEvaluatedAuditNotificationHandler : INotificationHandler<DecisionEvaluatedAuditNotification>
{
    private readonly TadssDbContext _dbContext;

    public DecisionEvaluatedAuditNotificationHandler(TadssDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task Handle(DecisionEvaluatedAuditNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(notification.Execution);

        _dbContext.DecisionExecutions.Add(notification.Execution);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// In-process synchronous domain notification dispatched when a Manager overrides a NEED_APPROVAL decision,
/// updating only governance metadata while preserving the original machine snapshot (Invariant 3).
/// </summary>
public sealed record DecisionOverriddenAuditNotification(
    DecisionExecution Execution,
    string OverriddenBy,
    string OverrideReason) : INotification;

/// <summary>
/// Synchronous MediatR handler that records human-in-the-loop override governance metadata
/// within the same PostgreSQL transaction boundary.
/// </summary>
/// <remarks>
/// Architectural Invariant: Dispatched exactly once as the terminal persistence step of a governance override.
/// This handler owns the single <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(CancellationToken)"/>
/// commit for the override operation.
/// </remarks>
public sealed class DecisionOverriddenAuditNotificationHandler : INotificationHandler<DecisionOverriddenAuditNotification>
{
    private readonly TadssDbContext _dbContext;

    public DecisionOverriddenAuditNotificationHandler(TadssDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task Handle(DecisionOverriddenAuditNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(notification.Execution);

        notification.Execution.IsOverridden = true;
        notification.Execution.OverriddenBy = notification.OverriddenBy;
        notification.Execution.OverrideReason = notification.OverrideReason;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
