using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.Application.Common;
using TADSS.Application.Options;
using TADSS.Application.Routing;
using TADSS.Application.Shadow;
using TADSS.Application.Strategies;
using TADSS.UnitTests.Application.Routing;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TADSS.UnitTests.Application.Shadow;

/// <summary>
/// IT-12 (A-11): <see cref="ShadowDispatcher"/> — tắt thì không làm gì, bật thì kiểm strategy shadow trong registry khi dựng, bỏ qua khi trùng
/// strategy chính, lấy mẫu theo <c>SampleRate</c>, hàng đợi có giới hạn: đầy thì bỏ việc và đếm <c>tadss.shadow.dropped</c>, <c>Dispatch</c> không bao giờ ném.
/// </summary>
public class ShadowDispatcherTests
{
    private const string Default = StrategyRouterTests.Default;
    private const string Mock = StrategyRouterTests.Mock;

    private static ShadowWorkItem Item() =>
        new(
            ContractSamples.DeserializeRequest(ContractSamples.RequestJson),
            DomainTestData.CreateReservation(),
            RequestHash: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            NowUtc: new DateTimeOffset(2026, 11, 18, 2, 15, 0, TimeSpan.Zero),
            CorrelationId: "corr-0001");

    private static ShadowDispatcher Create(ShadowOptions options, TadssMetrics? metrics = null, IStrategyRegistry? registry = null) =>
        new(
            MsOptions.Create(options),
            registry ?? StrategyRouterTests.Registry(),
            metrics ?? new TadssMetrics(),
            NullLogger<ShadowDispatcher>.Instance);

    /// <summary>Tắt thì không có việc nào được xếp hàng, kể cả strategy shadow là tên lạ (không bị kiểm khi tắt).</summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public void Dispatch_Disabled_NoOp()
    {
        var dispatcher = Create(new ShadowOptions { Enabled = false, Strategy = "KhongTonTai" });

        dispatcher.Dispatch(Default, Item());

        dispatcher.Reader.Count.Should().Be(0);
        dispatcher.Reader.Completion.IsCompleted.Should().BeTrue("hàng đợi của chế độ tắt là hàng đợi rỗng đã đóng");
    }

    /// <summary>Bật mà strategy shadow không có trong registry thì ném lỗi nêu tên lạ và danh sách hợp lệ.</summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public void Constructor_EnabledWithUnknownStrategy_Throws()
    {
        var act = () => Create(new ShadowOptions { Enabled = true, Strategy = "KhongTonTai" });

        act.Should().Throw<InvalidOperationException>().WithMessage($"*KhongTonTai*{Default}*");
    }

    /// <summary>Strategy chính trùng strategy shadow (không phân biệt hoa thường) thì bỏ qua, không xếp hàng và không tính là bị bỏ do đầy.</summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public void Dispatch_SameStrategyAsPrimary_Skips()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);
        var dispatcher = Create(new ShadowOptions { Enabled = true, Strategy = Mock }, metrics);

        dispatcher.Dispatch(Mock.ToUpperInvariant(), Item());

        dispatcher.Reader.Count.Should().Be(0);
        recorder.Sum(TadssMetrics.ShadowDropped).Should().Be(0);
    }

    /// <summary><c>SampleRate</c> 0 không gửi việc nào, 1 luôn gửi.</summary>
    [Theory]
    [Trait("CatalogId", "IT-12")]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 1)]
    public void Dispatch_SampleRateZeroAndOne_Theory(double sampleRate, int expectedQueued)
    {
        var dispatcher = Create(new ShadowOptions { Enabled = true, Strategy = Mock, SampleRate = sampleRate });

        dispatcher.Dispatch(Default, Item());

        dispatcher.Reader.Count.Should().Be(expectedQueued);
    }

    /// <summary>Hàng đợi sức chứa 1, gửi 3 việc mà không ai đọc: còn 1 việc, 2 việc bị bỏ và đếm đúng.</summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public void Dispatch_QueueFull_DropsAndCounts()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);
        var dispatcher = Create(new ShadowOptions { Enabled = true, Strategy = Mock, QueueCapacity = 1 }, metrics);

        for (var i = 0; i < 3; i++)
        {
            dispatcher.Dispatch(Default, Item());
        }

        dispatcher.Reader.Count.Should().Be(1);
        recorder.Sum(TadssMetrics.ShadowDropped).Should().Be(2);
    }

    /// <summary>Việc nằm trong hàng đợi là đúng việc đã gửi (cùng request, hash, mốc, correlation).</summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public void Dispatch_Accepted_QueuesSameItem()
    {
        var dispatcher = Create(new ShadowOptions { Enabled = true, Strategy = Mock });
        var item = Item();

        dispatcher.Dispatch(Default, item);

        dispatcher.Reader.TryRead(out var queued).Should().BeTrue();
        queued.Should().BeSameAs(item);
    }
}
