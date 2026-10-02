using Shared.DecisionContractSDK.Decisions;
using TADSS.Runtime.Api.V2.Infrastructure.Caching;

namespace TADSS.UnitTests.V2.Runtime.TestDoubles;

/// <summary>
/// <see cref="IStrategyDecisionCache"/> giả viết tay, thread-safe, trong bộ nhớ: ghi lại từng lần đọc/ghi (khóa, TTL),
/// cho phép gài sẵn giá trị, chạy một hành động mỗi lần đọc (ví dụ tiến đồng hồ giả) và cấu hình ném lỗi khi đọc/ghi
/// để kiểm tra việc <c>DecisionService</c> tự cô lập lỗi cache.
/// </summary>
internal sealed class FakeStrategyDecisionCache : IStrategyDecisionCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, StrategyDecision> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _getKeys = [];
    private readonly List<(string Key, StrategyDecision Decision, TimeSpan Ttl)> _sets = [];
    private Exception? _getFailure;
    private Exception? _setFailure;

    /// <summary>Hành động chạy ở đầu mỗi lần <see cref="GetAsync"/> (ví dụ tiến <c>FakeTimeProvider</c>).</summary>
    public Action? OnGet { get; set; }

    /// <summary>Các khóa đã được đọc, theo thứ tự.</summary>
    public IReadOnlyList<string> GetKeys
    {
        get
        {
            lock (_gate)
            {
                return _getKeys.ToArray();
            }
        }
    }

    /// <summary>Các lần ghi đã nhận, theo thứ tự.</summary>
    public IReadOnlyList<(string Key, StrategyDecision Decision, TimeSpan Ttl)> Sets
    {
        get
        {
            lock (_gate)
            {
                return _sets.ToArray();
            }
        }
    }

    /// <summary>Gài sẵn một quyết định cho khóa.</summary>
    public void Seed(string key, StrategyDecision decision)
    {
        lock (_gate)
        {
            _entries[key] = decision;
        }
    }

    /// <summary>Khiến mọi lần đọc ném <paramref name="exception"/>.</summary>
    public void FailGets(Exception exception)
    {
        lock (_gate)
        {
            _getFailure = exception;
        }
    }

    /// <summary>Khiến mọi lần ghi ném <paramref name="exception"/>.</summary>
    public void FailSets(Exception exception)
    {
        lock (_gate)
        {
            _setFailure = exception;
        }
    }

    public Task<StrategyDecision?> GetAsync(string key, CancellationToken cancellationToken)
    {
        OnGet?.Invoke();

        lock (_gate)
        {
            _getKeys.Add(key);

            if (_getFailure is not null)
            {
                return Task.FromException<StrategyDecision?>(_getFailure);
            }

            return Task.FromResult(_entries.GetValueOrDefault(key));
        }
    }

    public Task SetAsync(string key, StrategyDecision decision, TimeSpan ttl, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _sets.Add((key, decision, ttl));

            if (_setFailure is not null)
            {
                return Task.FromException(_setFailure);
            }

            _entries[key] = decision;
            return Task.CompletedTask;
        }
    }
}
