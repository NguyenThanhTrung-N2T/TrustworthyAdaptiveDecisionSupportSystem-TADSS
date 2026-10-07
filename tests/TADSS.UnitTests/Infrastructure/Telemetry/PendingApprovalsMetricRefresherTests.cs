using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Common;
using TADSS.Application.Exceptions;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Telemetry;
using TADSS.UnitTests.TestDoubles;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TADSS.UnitTests.Infrastructure.Telemetry;

/// <summary>
/// OB-5: <see cref="PendingApprovalsMetricRefresher"/> làm mới gauge <c>tadss.pending_approvals</c> theo chu kỳ bằng đồng hồ giả (không chờ thật):
/// tắt thì không truy vấn, chi nhánh hết đơn chờ báo 0 (không biến mất), lỗi truy vấn giữ giá trị cũ và chỉ log kiểu exception.
/// </summary>
public sealed class PendingApprovalsMetricRefresherTests : IDisposable
{
    private const int RefreshSeconds = 30;
    private static readonly Guid BranchA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid BranchB = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private readonly TadssMetrics _metrics = new();
    private readonly MetricRecorder _recorder;
    private readonly FakeTimeProvider _time = new();
    private readonly CapturingLogger<PendingApprovalsMetricRefresher> _logger = new();
    private readonly Mock<IDecisionQueryStore> _store = new();
    private readonly Queue<Func<IReadOnlyDictionary<Guid, long>>> _script = new();
    private int _calls;

    public PendingApprovalsMetricRefresherTests() => _recorder = new MetricRecorder(_metrics.Meter);

    public void Dispose()
    {
        _recorder.Dispose();
        _metrics.Dispose();
    }

    private PendingApprovalsMetricRefresher CreateRefresher(bool enabled = true)
    {
        _store
            .Setup(store => store.CountPendingApprovalsByBranchAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref _calls);
                return Task.FromResult(_script.Dequeue()());
            });
        var services = new ServiceCollection();
        services.AddScoped(_ => _store.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var options = new TelemetryOptions { Enabled = enabled, PendingApprovalsRefreshSeconds = RefreshSeconds };
        return new PendingApprovalsMetricRefresher(scopeFactory, MsOptions.Create(options), _metrics, _time, _logger);
    }

    private static IReadOnlyDictionary<Guid, long> Counts(params (Guid Branch, long Count)[] entries) =>
        entries.ToDictionary(entry => entry.Branch, entry => entry.Count);

    private Dictionary<string, long> Gauge() =>
        _recorder.ObserveGauge(TadssMetricNames.PendingApprovals).ToDictionary(sample => sample.Tags["branch"]!, sample => sample.Value);

    private async Task TickAsync(int expectedCalls)
    {
        _time.Advance(TimeSpan.FromSeconds(RefreshSeconds));
        await WaitUntilAsync(() => Volatile.Read(ref _calls) >= expectedCalls);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Điều kiện không đạt trong 10 giây.");
            }

            await Task.Delay(5);
        }
    }

    /// <summary>Telemetry tắt thì worker kết thúc ngay và không bao giờ truy vấn kho, dù thời gian trôi qua nhiều chu kỳ.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public async Task Refresher_Disabled_NeverQueries()
    {
        var refresher = CreateRefresher(enabled: false);

        await refresher.StartAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(RefreshSeconds * 3));
        await refresher.StopAsync(CancellationToken.None);

        _store.Verify(
            store => store.CountPendingApprovalsByBranchAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Gauge().Should().BeEmpty();
    }

    /// <summary>Không truy vấn ngay khi khởi động (chờ một chu kỳ); sau đó công bố số đếm, và chi nhánh hết đơn chờ báo 0 thay vì biến mất.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public async Task Refresher_PublishesCountsAndZeroForEmptiedBranch()
    {
        _script.Enqueue(() => Counts((BranchA, 2), (BranchB, 1)));
        _script.Enqueue(() => Counts((BranchA, 1)));
        var refresher = CreateRefresher();

        await refresher.StartAsync(CancellationToken.None);
        _calls.Should().Be(0, "chưa tới chu kỳ đầu thì chưa truy vấn");

        await TickAsync(expectedCalls: 1);
        await WaitUntilAsync(() => Gauge().Count == 2);
        var first = Gauge();

        await TickAsync(expectedCalls: 2);
        await WaitUntilAsync(() => Gauge().GetValueOrDefault(BranchB.ToString("D"), -1) == 0);
        var second = Gauge();
        await refresher.StopAsync(CancellationToken.None);

        first[BranchA.ToString("D")].Should().Be(2);
        first[BranchB.ToString("D")].Should().Be(1);
        second[BranchA.ToString("D")].Should().Be(1);
        second[BranchB.ToString("D")].Should().Be(0, "chi nhánh B hết đơn chờ nhưng series không được biến mất");
    }

    /// <summary>Lỗi truy vấn: giữ giá trị cũ, đúng một Warning chỉ có kiểu exception (không có message), worker vẫn chạy và phục hồi ở chu kỳ sau.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public async Task Refresher_StoreThrows_KeepsLastValuesAndLogsWarning()
    {
        _script.Enqueue(() => Counts((BranchA, 2), (BranchB, 1)));
        _script.Enqueue(() => throw new PersistenceUnavailableException("chuoi-ket-noi-bi-mat", new InvalidOperationException()));
        _script.Enqueue(() => Counts((BranchA, 4)));
        var refresher = CreateRefresher();

        await refresher.StartAsync(CancellationToken.None);
        await TickAsync(expectedCalls: 1);
        await WaitUntilAsync(() => Gauge().Count == 2);

        await TickAsync(expectedCalls: 2);
        await WaitUntilAsync(() => _logger.Entries.Any(entry => entry.Level == LogLevel.Warning));
        var afterFailure = Gauge();

        await TickAsync(expectedCalls: 3);
        await WaitUntilAsync(() => Gauge().GetValueOrDefault(BranchA.ToString("D")) == 4);
        await refresher.StopAsync(CancellationToken.None);

        afterFailure[BranchA.ToString("D")].Should().Be(2, "giữ giá trị cũ khi lỗi");
        afterFailure[BranchB.ToString("D")].Should().Be(1);
        var warning = _logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
        warning.Message.Should().Contain(nameof(PersistenceUnavailableException)).And.NotContain("chuoi-ket-noi-bi-mat");
        Gauge()[BranchB.ToString("D")].Should().Be(0, "sau khi phục hồi, chi nhánh B không còn đơn chờ");
    }
}
