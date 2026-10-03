using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Exceptions;

namespace TADSS.Infrastructure.Persistence;

/// <summary>
/// Hiện thực EF Core/PostgreSQL của <see cref="IDecisionGovernanceStore"/> trên schema <c>tadss</c>: ghi override
/// (<c>decision_overrides</c>) và outcome (<c>decision_outcomes</c>). Đăng ký scoped: mỗi scope (request) dùng một
/// <see cref="TadssDbContext"/> riêng, nên store không được chia sẻ giữa các luồng song song.
/// <para>
/// Chỉ thêm (INV-4): không tạo <c>UPDATE</c>/<c>DELETE</c>; đọc lại bản thắng là <c>AsNoTracking</c>; entity không lọt ra ngoài
/// (xem <see cref="DecisionAuditMapper"/>) và luôn được gỡ khỏi change tracker sau khi ghi.
/// </para>
/// <para>
/// Dịch lỗi qua <see cref="PersistenceFailureTranslator"/>: lỗi có thể thử lại → <see cref="PersistenceUnavailableException"/>
/// (hoặc <see cref="OperationCanceledException"/> nếu caller đã hủy). Unique <c>uq_decision_overrides_decision_id</c> /
/// <c>uq_decision_outcomes_decision_id</c> (INV-5, INV-6) → trả bản đã có; mọi lỗi khác (CHECK, khóa ngoại, unique khác) giữ nguyên exception gốc.
/// </para>
/// </summary>
public sealed class EfDecisionGovernanceStore : IDecisionGovernanceStore
{
    private const string UniqueViolation = "23505";
    private const string OverrideConstraint = "uq_decision_overrides_decision_id";
    private const string OutcomeConstraint = "uq_decision_outcomes_decision_id";

    private readonly TadssDbContext _db;
    private readonly ILogger<EfDecisionGovernanceStore> _logger;

    /// <summary>
    /// Khởi tạo store trên <paramref name="db"/> của scope hiện tại.
    /// </summary>
    /// <param name="db">DbContext của scope.</param>
    /// <param name="logger">Logger (chỉ ghi loại lỗi, không ghi dữ liệu).</param>
    public EfDecisionGovernanceStore(TadssDbContext db, ILogger<EfDecisionGovernanceStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<InsertOverrideResult> InsertOverrideAsync(DecisionOverrideRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        return PersistenceFailureTranslator.ExecuteAsync(() => InsertOverrideCoreAsync(record, cancellationToken), _logger, cancellationToken);
    }

    /// <inheritdoc />
    public Task<InsertOutcomeResult> InsertOutcomeAsync(DecisionOutcomeRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        return PersistenceFailureTranslator.ExecuteAsync(() => InsertOutcomeCoreAsync(record, cancellationToken), _logger, cancellationToken);
    }

    private async Task<InsertOverrideResult> InsertOverrideCoreAsync(DecisionOverrideRecord record, CancellationToken cancellationToken)
    {
        var entity = DecisionAuditMapper.ToEntity(record);
        _db.DecisionOverrides.Add(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolationOf(ex, OverrideConstraint))
        {
            // Một override khác của cùng quyết định đã thắng: đọc lại bản thắng (AsNoTracking nên không đụng entity thất bại)
            var winnerEntity = await _db.DecisionOverrides
                .AsNoTracking()
                .Where(existing => existing.DecisionId == record.DecisionId)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException($"Insert vi phạm {OverrideConstraint} nhưng không đọc lại được override đã thắng.");

            _logger.LogInformation("Insert override bị chặn bởi {Constraint}; trả bản đã có.", OverrideConstraint);
            return new InsertOverrideResult(DecisionAuditMapper.ToRecord(winnerEntity), IsExistingWinner: true);
        }
        finally
        {
            // Store không giữ trạng thái theo dõi: entity (thành công hay thất bại) luôn được gỡ khỏi change tracker của scope
            _db.Entry(entity).State = EntityState.Detached;
        }

        return new InsertOverrideResult(record, IsExistingWinner: false);
    }

    private async Task<InsertOutcomeResult> InsertOutcomeCoreAsync(DecisionOutcomeRecord record, CancellationToken cancellationToken)
    {
        var entity = DecisionAuditMapper.ToEntity(record);
        _db.DecisionOutcomes.Add(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolationOf(ex, OutcomeConstraint))
        {
            // Một outcome khác của cùng quyết định đã thắng: đọc lại bản thắng
            var winnerEntity = await _db.DecisionOutcomes
                .AsNoTracking()
                .Where(existing => existing.DecisionId == record.DecisionId)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException($"Insert vi phạm {OutcomeConstraint} nhưng không đọc lại được outcome đã thắng.");

            _logger.LogInformation("Insert outcome bị chặn bởi {Constraint}; trả bản đã có.", OutcomeConstraint);
            return new InsertOutcomeResult(DecisionAuditMapper.ToRecord(winnerEntity), IsExistingWinner: true);
        }
        finally
        {
            _db.Entry(entity).State = EntityState.Detached;
        }

        return new InsertOutcomeResult(record, IsExistingWinner: false);
    }

    /// <summary>Đúng khi chuỗi exception chứa unique violation <c>23505</c> của đúng constraint <paramref name="constraintName"/>.</summary>
    private static bool IsUniqueViolationOf(Exception exception, string constraintName)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: UniqueViolation } postgres && postgres.ConstraintName == constraintName)
            {
                return true;
            }
        }

        return false;
    }
}
