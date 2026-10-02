using TADSS.Runtime.Api.V2.Infrastructure.Data;
using TADSS.Runtime.Api.V2.Middlewares;

namespace TADSS.UnitTests.V2.Runtime.TestDoubles;

/// <summary>
/// <see cref="IDecisionAuditStore"/> giả viết tay, thread-safe, trong bộ nhớ: tìm theo idempotency key của bản ghi
/// <c>PRIMARY</c>, đếm số lần insert, có thể cấu hình ném <see cref="PersistenceUnavailableException"/> hoặc giả lập thua
/// tranh chấp (trả một bản thắng định sẵn). Hành vi bám đúng hợp đồng của <see cref="IDecisionAuditStore"/> để Task 9
/// kiểm tra <c>DecisionService</c> mà không cần PostgreSQL.
/// </summary>
internal sealed class FakeDecisionAuditStore : IDecisionAuditStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DecisionExecutionRecord> _primaryByKey = new(StringComparer.Ordinal);
    private readonly List<DecisionExecutionRecord> _records = [];
    private int _insertCount;
    private int _findCount;
    private Exception? _insertFailure;
    private DecisionExecutionRecord? _simulatedWinner;

    /// <summary>Số lần <see cref="InsertExecutionAsync"/> được gọi (kể cả lần ném lỗi hoặc thua tranh chấp).</summary>
    public int InsertCount => Volatile.Read(ref _insertCount);

    /// <summary>Số lần <see cref="FindPrimaryByIdempotencyKeyAsync"/> được gọi.</summary>
    public int FindCount => Volatile.Read(ref _findCount);

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

    private void Store(DecisionExecutionRecord record)
    {
        _records.Add(record);

        if (record.Role == ExecutionRole.PRIMARY && record.IdempotencyKey is { } key)
        {
            _primaryByKey[key] = record;
        }
    }
}
