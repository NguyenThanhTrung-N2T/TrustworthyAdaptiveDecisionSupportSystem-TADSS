using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Exceptions;
using TADSS.Infrastructure.Persistence.Entities;

namespace TADSS.Infrastructure.Persistence;

/// <summary>
/// Hiện thực EF Core/PostgreSQL của <see cref="IDecisionAuditStore"/> trên schema <c>tadss</c>. Đăng ký scoped: mỗi scope (request)
/// dùng một <see cref="TadssDbContext"/> riêng, nên store không được chia sẻ giữa các luồng song song.
/// <para>
/// Chỉ thêm và đọc (INV-4): không tạo <c>UPDATE</c>/<c>DELETE</c>; mọi truy vấn đọc là <c>AsNoTracking</c>; entity không lọt ra ngoài
/// (xem <see cref="DecisionAuditMapper"/>).
/// </para>
/// <para>
/// Dịch lỗi: lỗi kết nối/timeout/<c>40001</c>/<c>40P01</c> (theo <see cref="PersistenceFailureDetector.IsRetryable"/>) →
/// <see cref="PersistenceUnavailableException"/> (thông báo không chứa chuỗi kết nối, SQL hay giá trị; exception gốc được giữ);
/// unique <c>uq_decision_idempotency_key</c> khi insert → trả bản thắng; mọi lỗi khác (CHECK, FK, unique khác, lỗi dữ liệu) giữ nguyên
/// exception gốc. <see cref="OperationCanceledException"/> của caller lan ra nguyên vẹn.
/// </para>
/// </summary>
public sealed class EfDecisionAuditStore : IDecisionAuditStore
{
    private const string UniqueViolation = "23505";
    private const string IdempotencyConstraint = "uq_decision_idempotency_key";

    private readonly TadssDbContext _db;
    private readonly ILogger<EfDecisionAuditStore> _logger;

    /// <summary>
    /// Khởi tạo store trên <paramref name="db"/> của scope hiện tại.
    /// </summary>
    /// <param name="db">DbContext của scope.</param>
    /// <param name="logger">Logger (chỉ ghi loại lỗi, không ghi dữ liệu).</param>
    public EfDecisionAuditStore(TadssDbContext db, ILogger<EfDecisionAuditStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<DecisionExecutionRecord?> FindPrimaryByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        return TranslateFailuresAsync(() => FindPrimaryCoreAsync(idempotencyKey, cancellationToken));
    }

    /// <inheritdoc />
    public Task<InsertExecutionResult> InsertExecutionAsync(DecisionExecutionRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        return TranslateFailuresAsync(() => InsertCoreAsync(record, cancellationToken));
    }

    /// <inheritdoc />
    public Task<DecisionDetailRecord?> GetDecisionDetailAsync(Guid decisionId, CancellationToken cancellationToken) =>
        TranslateFailuresAsync(() => GetDetailCoreAsync(decisionId, cancellationToken));

    private async Task<DecisionExecutionRecord?> FindPrimaryCoreAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        var entity = await _db.DecisionExecutions
            .AsNoTracking()
            .Where(execution => execution.IdempotencyKey == idempotencyKey && execution.Role == ExecutionRole.PRIMARY)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : DecisionAuditMapper.ToRecord(entity);
    }

    private async Task<InsertExecutionResult> InsertCoreAsync(DecisionExecutionRecord record, CancellationToken cancellationToken)
    {
        var entity = DecisionAuditMapper.ToEntity(record);
        _db.DecisionExecutions.Add(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotencyConflict(ex))
        {
            // Một request khác cùng idempotency key đã thắng: đọc lại bản thắng (AsNoTracking nên không đụng entity thất bại)
            var winner = (record.IdempotencyKey is { } key ? await FindPrimaryCoreAsync(key, cancellationToken) : null)
                ?? throw new InvalidOperationException(
                    "Insert vi phạm uq_decision_idempotency_key nhưng không đọc lại được bản ghi PRIMARY đã thắng.");

            _logger.LogInformation("Insert bị chặn bởi {Constraint}; trả bản ghi đã thắng.", IdempotencyConstraint);
            return new InsertExecutionResult(winner, IsExistingWinner: true);
        }
        finally
        {
            // Store không giữ trạng thái theo dõi: entity (thành công hay thất bại) luôn được gỡ khỏi change tracker của scope,
            // để insert kế tiếp cùng scope không vướng xung đột khóa trong bộ nhớ
            Detach(entity);
        }

        return new InsertExecutionResult(record, IsExistingWinner: false);
    }

    private async Task<DecisionDetailRecord?> GetDetailCoreAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        // Một truy vấn LEFT JOIN: mỗi quyết định có tối đa một override và một outcome (unique decision_id)
        var row = await (
                from execution in _db.DecisionExecutions.AsNoTracking()
                where execution.DecisionId == decisionId
                join @override in _db.DecisionOverrides.AsNoTracking() on execution.DecisionId equals @override.DecisionId into overrides
                from @override in overrides.DefaultIfEmpty()
                join outcome in _db.DecisionOutcomes.AsNoTracking() on execution.DecisionId equals outcome.DecisionId into outcomes
                from outcome in outcomes.DefaultIfEmpty()
                select new { execution, @override, outcome })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new DecisionDetailRecord(
            DecisionAuditMapper.ToRecord(row.execution),
            row.@override is null ? null : DecisionAuditMapper.ToRecord(row.@override),
            row.outcome is null ? null : DecisionAuditMapper.ToRecord(row.outcome));
    }

    private void Detach(DecisionExecution entity) => _db.Entry(entity).State = EntityState.Detached;

    /// <summary>Đúng khi chuỗi exception chứa unique violation <c>23505</c> của đúng constraint <c>uq_decision_idempotency_key</c>.</summary>
    private static bool IsIdempotencyConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: UniqueViolation, ConstraintName: IdempotencyConstraint })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Chạy <paramref name="operation"/>; lỗi lưu trữ có thể thử lại được bọc thành <see cref="PersistenceUnavailableException"/>,
    /// còn hủy của caller và mọi lỗi khác lan ra nguyên vẹn.
    /// </summary>
    private async Task<T> TranslateFailuresAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (ex is not OperationCanceledException && PersistenceFailureDetector.IsRetryable(ex))
        {
            _logger.LogWarning("Kho audit tạm thời không khả dụng ({ExceptionType}).", ex.GetType().Name);
            throw new PersistenceUnavailableException(
                "Kho lưu trữ quyết định tạm thời không khả dụng; vui lòng thử lại sau.",
                ex);
        }
    }
}
