using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Decisions;
using TADSS.Application.Exceptions;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// Hiện thực giả viết tay của cả ba port audit (<see cref="IDecisionEvaluationStore"/>, <see cref="IDecisionQueryStore"/> và
/// <see cref="IDecisionGovernanceStore"/>), thread-safe, trong bộ nhớ: tìm theo idempotency key của bản ghi
/// <c>PRIMARY</c>, đếm số lần insert, có thể cấu hình ném <see cref="PersistenceUnavailableException"/> hoặc giả lập thua
/// tranh chấp (trả một bản thắng định sẵn). Hành vi bám đúng hợp đồng của các port audit để kiểm tra use case
/// mà không cần PostgreSQL. Tra chi tiết theo <c>decisionId</c> (mọi role), cho phép gài sẵn override/outcome, ghi override/outcome
/// (một bản cho mỗi quyết định như unique <c>decision_id</c>) và dựng hàng chờ duyệt theo đúng ngữ nghĩa của store thật.
/// </summary>
internal sealed class FakeDecisionAuditStore : IDecisionEvaluationStore, IDecisionQueryStore, IDecisionGovernanceStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DecisionExecutionRecord> _primaryByKey = new(StringComparer.Ordinal);
    private readonly List<DecisionExecutionRecord> _records = [];
    private int _insertCount;
    private readonly Dictionary<Guid, DecisionOverrideRecord> _overrides = new();
    private readonly Dictionary<Guid, DecisionOutcomeRecord> _outcomes = new();
    private int _findCount;
    private int _detailCount;
    private Exception? _insertFailure;
    private DecisionExecutionRecord? _simulatedWinner;
    private DecisionOverrideRecord? _simulatedOverrideWinner;
    private DecisionOutcomeRecord? _simulatedOutcomeWinner;
    private int _overrideInsertCount;
    private int _outcomeInsertCount;
    private int _pendingCount;
    private Exception? _governanceFailure;

    /// <summary>Số lần <see cref="InsertOverrideAsync"/> được gọi (kể cả lần ném lỗi hoặc thua tranh chấp).</summary>
    public int OverrideInsertCount => Volatile.Read(ref _overrideInsertCount);

    /// <summary>Số lần <see cref="InsertOutcomeAsync"/> được gọi (kể cả lần ném lỗi hoặc thua tranh chấp).</summary>
    public int OutcomeInsertCount => Volatile.Read(ref _outcomeInsertCount);

    /// <summary>Số lần <see cref="GetPendingApprovalsAsync"/> được gọi.</summary>
    public int PendingCount => Volatile.Read(ref _pendingCount);

    /// <summary>Các override đang có trong store (gồm cả bản gài sẵn), theo <c>decision_id</c>.</summary>
    public IReadOnlyDictionary<Guid, DecisionOverrideRecord> Overrides
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<Guid, DecisionOverrideRecord>(_overrides);
            }
        }
    }

    /// <summary>Các outcome đang có trong store (gồm cả bản gài sẵn), theo <c>decision_id</c>.</summary>
    public IReadOnlyDictionary<Guid, DecisionOutcomeRecord> Outcomes
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<Guid, DecisionOutcomeRecord>(_outcomes);
            }
        }
    }

    /// <summary>Số lần <see cref="InsertExecutionAsync"/> được gọi (kể cả lần ném lỗi hoặc thua tranh chấp).</summary>
    public int InsertCount => Volatile.Read(ref _insertCount);

    /// <summary>Số lần <see cref="FindPrimaryByIdempotencyKeyAsync"/> được gọi.</summary>
    public int FindCount => Volatile.Read(ref _findCount);

    /// <summary>Số lần <see cref="GetDecisionDetailAsync"/> được gọi.</summary>
    public int DetailCount => Volatile.Read(ref _detailCount);

    /// <summary>Các bản ghi đã được lưu thật (theo thứ tự ghi), không gồm bản thắng định sẵn chưa được gài vào.</summary>
    public IReadOnlyList<DecisionExecutionRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return _records.ToArray();
            }
        }
    }

    /// <summary>Bản ghi vừa insert thành công gần nhất; <c>null</c> khi chưa có.</summary>
    public DecisionExecutionRecord? LastInserted
    {
        get
        {
            lock (_gate)
            {
                return _records.Count == 0 ? null : _records[^1];
            }
        }
    }

    /// <summary>Gài sẵn một bản ghi <c>PRIMARY</c> như thể đã được request khác lưu trước đó.</summary>
    public void Seed(DecisionExecutionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            Store(record);
        }
    }

    /// <summary>Gài sẵn một override cho quyết định <see cref="DecisionOverrideRecord.DecisionId"/> (hiện trong <see cref="GetDecisionDetailAsync"/>).</summary>
    public void SeedOverride(DecisionOverrideRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            _overrides[record.DecisionId] = record;
        }
    }

    /// <summary>Gài sẵn một outcome cho quyết định <see cref="DecisionOutcomeRecord.DecisionId"/> (hiện trong <see cref="GetDecisionDetailAsync"/>).</summary>
    public void SeedOutcome(DecisionOutcomeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            _outcomes[record.DecisionId] = record;
        }
    }

    /// <summary>Khiến mọi lần insert ném <see cref="PersistenceUnavailableException"/> (mô phỏng PostgreSQL không khả dụng).</summary>
    public void FailInsertWithPersistenceUnavailable(string message = "PostgreSQL không khả dụng.")
    {
        lock (_gate)
        {
            _insertFailure = new PersistenceUnavailableException(message, new TimeoutException(message));
        }
    }

    /// <summary>
    /// Giả lập thua tranh chấp idempotency: lần insert kế tiếp không lưu bản ghi mới mà trả <paramref name="winner"/>
    /// với <c>IsExistingWinner = true</c> (bản thắng được gài vào store như thể request kia đã ghi trước).
    /// </summary>
    public void SimulateLostRace(DecisionExecutionRecord winner)
    {
        ArgumentNullException.ThrowIfNull(winner);

        lock (_gate)
        {
            _simulatedWinner = winner;
        }
    }

    /// <summary>
    /// Giả lập thua tranh chấp override: lần <see cref="InsertOverrideAsync"/> kế tiếp không lưu bản mới mà trả <paramref name="winner"/>
    /// với <c>IsExistingWinner = true</c> (bản thắng được gài vào store như thể request kia đã ghi trước).
    /// </summary>
    public void SimulateLostOverrideRace(DecisionOverrideRecord winner)
    {
        ArgumentNullException.ThrowIfNull(winner);

        lock (_gate)
        {
            _simulatedOverrideWinner = winner;
        }
    }

    /// <summary>Như <see cref="SimulateLostOverrideRace"/> nhưng cho <see cref="InsertOutcomeAsync"/>.</summary>
    public void SimulateLostOutcomeRace(DecisionOutcomeRecord winner)
    {
        ArgumentNullException.ThrowIfNull(winner);

        lock (_gate)
        {
            _simulatedOutcomeWinner = winner;
        }
    }

    /// <summary>Khiến mọi lần ghi override/outcome ném <see cref="PersistenceUnavailableException"/> (mô phỏng PostgreSQL không khả dụng).</summary>
    public void FailGovernanceWithPersistenceUnavailable(string message = "PostgreSQL không khả dụng.")
    {
        lock (_gate)
        {
            _governanceFailure = new PersistenceUnavailableException(message, new TimeoutException(message));
        }
    }

    public Task<DecisionExecutionRecord?> FindPrimaryByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _findCount);

        lock (_gate)
        {
            return Task.FromResult(_primaryByKey.GetValueOrDefault(idempotencyKey));
        }
    }

    public Task<InsertExecutionResult> InsertExecutionAsync(DecisionExecutionRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _insertCount);

        lock (_gate)
        {
            if (_insertFailure is not null)
            {
                return Task.FromException<InsertExecutionResult>(_insertFailure);
            }

            if (_simulatedWinner is not null)
            {
                var winner = _simulatedWinner;
                _simulatedWinner = null;
                Store(winner);
                return Task.FromResult(new InsertExecutionResult(winner, IsExistingWinner: true));
            }

            // Mô phỏng unique partial index uq_decision_idempotency_key: trả bản thắng thay vì ném
            if (record.Role == ExecutionRole.PRIMARY
                && record.IdempotencyKey is { } key
                && _primaryByKey.TryGetValue(key, out var existing))
            {
                return Task.FromResult(new InsertExecutionResult(existing, IsExistingWinner: true));
            }

            Store(record);
            return Task.FromResult(new InsertExecutionResult(record, IsExistingWinner: false));
        }
    }

    public Task<DecisionDetailRecord?> GetDecisionDetailAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _detailCount);

        lock (_gate)
        {
            // Tra theo decisionId, không lọc role (như store thật)
            var execution = _records.FirstOrDefault(record => record.DecisionId == decisionId);
            if (execution is null)
            {
                return Task.FromResult<DecisionDetailRecord?>(null);
            }

            return Task.FromResult<DecisionDetailRecord?>(new DecisionDetailRecord(
                execution,
                _overrides.GetValueOrDefault(decisionId),
                _outcomes.GetValueOrDefault(decisionId)));
        }
    }

    public Task<InsertOverrideResult> InsertOverrideAsync(DecisionOverrideRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _overrideInsertCount);

        lock (_gate)
        {
            if (_governanceFailure is not null)
            {
                return Task.FromException<InsertOverrideResult>(_governanceFailure);
            }

            if (_simulatedOverrideWinner is not null)
            {
                var winner = _simulatedOverrideWinner;
                _simulatedOverrideWinner = null;
                _overrides[winner.DecisionId] = winner;
                return Task.FromResult(new InsertOverrideResult(winner, IsExistingWinner: true));
            }

            // Mô phỏng unique uq_decision_overrides_decision_id: trả bản đã có thay vì ném
            if (_overrides.TryGetValue(record.DecisionId, out var existing))
            {
                return Task.FromResult(new InsertOverrideResult(existing, IsExistingWinner: true));
            }

            _overrides[record.DecisionId] = record;
            return Task.FromResult(new InsertOverrideResult(record, IsExistingWinner: false));
        }
    }

    public Task<InsertOutcomeResult> InsertOutcomeAsync(DecisionOutcomeRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _outcomeInsertCount);

        lock (_gate)
        {
            if (_governanceFailure is not null)
            {
                return Task.FromException<InsertOutcomeResult>(_governanceFailure);
            }

            if (_simulatedOutcomeWinner is not null)
            {
                var winner = _simulatedOutcomeWinner;
                _simulatedOutcomeWinner = null;
                _outcomes[winner.DecisionId] = winner;
                return Task.FromResult(new InsertOutcomeResult(winner, IsExistingWinner: true));
            }

            // Mô phỏng unique uq_decision_outcomes_decision_id: trả bản đã có thay vì ném
            if (_outcomes.TryGetValue(record.DecisionId, out var existing))
            {
                return Task.FromResult(new InsertOutcomeResult(existing, IsExistingWinner: true));
            }

            _outcomes[record.DecisionId] = record;
            return Task.FromResult(new InsertOutcomeResult(record, IsExistingWinner: false));
        }
    }

    public Task<PendingApprovalsPage> GetPendingApprovalsAsync(
        Guid branchId, int page, int pageSize, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _pendingCount);

        lock (_gate)
        {
            // Cùng ngữ nghĩa với store thật: PRIMARY + NEED_APPROVAL còn hạn (nghiêm ngặt), không bị bản PRIMARY mới hơn cùng đơn
            // và chi nhánh thay thế (so (created_at, decision_id)), chưa có override/outcome; SHADOW không bao giờ có mặt
            var pending = _records
                .Where(record => record.Role == ExecutionRole.PRIMARY
                    && record.BranchId == branchId
                    && record.Status == DecisionStatus.NEED_APPROVAL
                    && record.ApprovalExpiresAt > nowUtc
                    && !_overrides.ContainsKey(record.DecisionId)
                    && !_outcomes.ContainsKey(record.DecisionId)
                    && !_records.Any(newer => newer.Role == ExecutionRole.PRIMARY
                        && newer.ReservationId == record.ReservationId
                        && newer.BranchId == record.BranchId
                        && IsNewer(newer, record)))
                .OrderBy(record => record.ApprovalExpiresAt)
                .ThenBy(record => record.DecisionId)
                .ToList();

            var items = pending.Skip((int)((long)(page - 1) * pageSize)).Take(pageSize).ToList();
            return Task.FromResult(new PendingApprovalsPage(items, pending.Count));
        }
    }

    public Task<IReadOnlyDictionary<Guid, long>> CountPendingApprovalsByBranchAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            // Cùng điều kiện với GetPendingApprovalsAsync, gộp theo chi nhánh
            IReadOnlyDictionary<Guid, long> counts = _records
                .Where(record => record.Role == ExecutionRole.PRIMARY
                    && record.Status == DecisionStatus.NEED_APPROVAL
                    && record.ApprovalExpiresAt > nowUtc
                    && !_overrides.ContainsKey(record.DecisionId)
                    && !_outcomes.ContainsKey(record.DecisionId)
                    && !_records.Any(newer => newer.Role == ExecutionRole.PRIMARY
                        && newer.ReservationId == record.ReservationId
                        && newer.BranchId == record.BranchId
                        && IsNewer(newer, record)))
                .GroupBy(record => record.BranchId)
                .ToDictionary(group => group.Key, group => (long)group.Count());
            return Task.FromResult(counts);
        }
    }

    private static bool IsNewer(DecisionExecutionRecord candidate, DecisionExecutionRecord other) =>
        candidate.CreatedAt > other.CreatedAt
        || (candidate.CreatedAt == other.CreatedAt && candidate.DecisionId.CompareTo(other.DecisionId) > 0);

    private void Store(DecisionExecutionRecord record)
    {
        _records.Add(record);

        if (record.Role == ExecutionRole.PRIMARY && record.IdempotencyKey is { } key)
        {
            _primaryByKey[key] = record;
        }
    }
}
