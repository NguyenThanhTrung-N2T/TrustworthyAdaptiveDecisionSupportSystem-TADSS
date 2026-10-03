using TADSS.Application.Abstractions.Persistence;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Trạng thái dùng chung giữa test và <see cref="CuttableDecisionAuditStore"/> (store là scoped, mỗi request một bản): đếm số lần ghi audit
/// được thử và giữ hành động cần chạy ngay trước <c>InsertExecutionAsync</c>.
/// </summary>
public sealed class AuditInsertProbe
{
    private int _insertAttempts;

    /// <summary>Số lần <c>InsertExecutionAsync</c> được gọi (kể cả lần thất bại).</summary>
    public int InsertAttempts => Volatile.Read(ref _insertAttempts);

    /// <summary>Hành động chạy ngay trước khi ủy quyền cho store thật (ví dụ ngắt proxy PostgreSQL); <c>null</c> thì không làm gì.</summary>
    public Func<CancellationToken, Task>? BeforeInsert { get; set; }

    internal void RecordAttempt() => Interlocked.Increment(ref _insertAttempts);
}

/// <summary>
/// Decorator của <see cref="IDecisionAuditStore"/> chỉ dùng trong host test (C-10): chạy <see cref="AuditInsertProbe.BeforeInsert"/> ngay trước khi
/// ủy quyền <c>InsertExecutionAsync</c> cho store thật, tức là sau khi strategy đã chạy xong — dùng để cắt kết nối PostgreSQL đúng lúc ghi audit (FI-08).
/// Mọi thao tác khác đi thẳng tới store thật.
/// </summary>
public sealed class CuttableDecisionAuditStore : IDecisionAuditStore
{
    private readonly IDecisionAuditStore _inner;
    private readonly AuditInsertProbe _probe;

    /// <summary>Bọc <paramref name="inner"/>.</summary>
    public CuttableDecisionAuditStore(IDecisionAuditStore inner, AuditInsertProbe probe)
    {
        _inner = inner;
        _probe = probe;
    }

    /// <inheritdoc />
    public Task<DecisionExecutionRecord?> FindPrimaryByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        _inner.FindPrimaryByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

    /// <inheritdoc />
    public async Task<InsertExecutionResult> InsertExecutionAsync(DecisionExecutionRecord record, CancellationToken cancellationToken)
    {
        _probe.RecordAttempt();
        if (_probe.BeforeInsert is { } beforeInsert)
        {
            await beforeInsert(cancellationToken).ConfigureAwait(false);
        }

        return await _inner.InsertExecutionAsync(record, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<DecisionDetailRecord?> GetDecisionDetailAsync(Guid decisionId, CancellationToken cancellationToken) =>
        _inner.GetDecisionDetailAsync(decisionId, cancellationToken);
}
