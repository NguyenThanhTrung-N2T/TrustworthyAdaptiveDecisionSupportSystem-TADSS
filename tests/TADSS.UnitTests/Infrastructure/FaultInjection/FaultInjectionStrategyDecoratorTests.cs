using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using TADSS.Infrastructure.FaultInjection;
using TADSS.Infrastructure.Options;
using TADSS.Runtime.Api.Composition;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.FaultInjection;

/// <summary>
/// UT-09 / FI-02 / FI-03 / FI-05: <see cref="FaultInjectionStrategyDecorator"/> tiêm lỗi vào strategy theo profile cấu hình (A-07) —
/// <c>Delay</c> tôn trọng token, <c>Throw</c> ném message cố định, <c>IgnoreToken</c> chờ không nhìn token, <c>BusyCpu</c> quay vòng đồng bộ —
/// và giữ nguyên danh tính của strategy thật (tên, phiên bản), chỉ tắt cache khi có lỗi được tiêm.
/// </summary>
public class FaultInjectionStrategyDecoratorTests
{
    private const int DelayMs = 1000;

    private static StrategyDecision Approved() =>
        StrategyDecision.Approve(0.5m, ReasonCodes.TierDeposit, ["Cọc theo hạng thành viên."]);

    private static FaultInjectionControl Options(FaultInjectionProfile profile, int delayMs = DelayMs) =>
        new FaultInjectionControlHarness(new FaultInjectionOptions { Enabled = true, Profile = profile, DelayMs = delayMs }).Control;

    private static Task<StrategyDecision> EvaluateAsync(FaultInjectionStrategyDecorator decorator, CancellationToken token) =>
        decorator.EvaluateAsync(DomainTestData.CreateReservation(), DomainTestData.CreateContext(), token);

    /// <summary>
    /// Mỗi profile với token đã hủy sẵn và delay ngắn (đồng hồ thật): <c>None</c>, <c>IgnoreToken</c>, <c>BusyCpu</c> không nhìn token nên trả quyết định
    /// của strategy thật và gọi nó đúng một lần; <c>Throw</c> ném <see cref="InvalidOperationException"/> với message cố định và không gọi strategy thật.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "FI-03")]
    [InlineData(FaultInjectionProfile.None, false)]
    [InlineData(FaultInjectionProfile.Throw, true)]
    [InlineData(FaultInjectionProfile.IgnoreToken, false)]
    [InlineData(FaultInjectionProfile.BusyCpu, false)]
    public async Task EvaluateAsync_Profile_Behaves_Theory(FaultInjectionProfile profile, bool expectThrow)
    {
        var expected = Approved();
        var inner = new DelegateStrategy((_, _, _) => Task.FromResult(expected));
        var decorator = new FaultInjectionStrategyDecorator(inner, Options(profile, delayMs: 30), TimeProvider.System);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => EvaluateAsync(decorator, cts.Token);

        if (expectThrow)
        {
            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("Lỗi tiêm có chủ đích (FaultInjection).");
            inner.InvocationCount.Should().Be(0, "Throw không gọi strategy thật");
        }
        else
        {
            (await act()).Should().BeSameAs(expected);
            inner.InvocationCount.Should().Be(1);
        }
    }

    /// <summary>FI-02: <c>Delay</c> chờ đủ <c>DelayMs</c> theo <see cref="TimeProvider"/> rồi mới gọi strategy thật.</summary>
    [Fact]
    [Trait("CatalogId", "FI-02")]
    public async Task EvaluateAsync_DelayProfile_WaitsThenCallsInner()
    {
        var clock = new FakeTimeProvider();
        var expected = Approved();
        var inner = new DelegateStrategy((_, _, _) => Task.FromResult(expected));
        var decorator = new FaultInjectionStrategyDecorator(inner, Options(FaultInjectionProfile.Delay), clock);

        var pending = EvaluateAsync(decorator, CancellationToken.None);

        pending.IsCompleted.Should().BeFalse("chưa đủ DelayMs");
        inner.InvocationCount.Should().Be(0);
        clock.Advance(TimeSpan.FromMilliseconds(DelayMs - 1));
        pending.IsCompleted.Should().BeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(1));
        (await pending).Should().BeSameAs(expected);
        inner.InvocationCount.Should().Be(1);
    }

    /// <summary>FI-02: <c>Delay</c> tôn trọng token — hủy giữa chừng thì ném <see cref="OperationCanceledException"/> và không gọi strategy thật.</summary>
    [Fact]
    [Trait("CatalogId", "FI-02")]
    public async Task EvaluateAsync_DelayProfileTokenCancelledMidway_ThrowsOperationCanceled()
    {
        var clock = new FakeTimeProvider();
        var inner = new DelegateStrategy((_, _, _) => Task.FromResult(Approved()));
        var decorator = new FaultInjectionStrategyDecorator(inner, Options(FaultInjectionProfile.Delay), clock);
        using var cts = new CancellationTokenSource();

        var pending = EvaluateAsync(decorator, cts.Token);
        clock.Advance(TimeSpan.FromMilliseconds(DelayMs / 2));
        await cts.CancelAsync();

        await pending.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        inner.InvocationCount.Should().Be(0);
    }

    /// <summary>FI-05: <c>IgnoreToken</c> chờ <c>DelayMs</c> nhưng không nhìn token — hủy giữa chừng vẫn không làm nó dừng, tới hạn thì gọi strategy thật.</summary>
    [Fact]
    [Trait("CatalogId", "FI-05")]
    public async Task EvaluateAsync_IgnoreTokenProfile_KeepsWaitingAfterCancellation()
    {
        var clock = new FakeTimeProvider();
        var expected = Approved();
        var inner = new DelegateStrategy((_, _, _) => Task.FromResult(expected));
        var decorator = new FaultInjectionStrategyDecorator(inner, Options(FaultInjectionProfile.IgnoreToken), clock);
        using var cts = new CancellationTokenSource();

        var pending = EvaluateAsync(decorator, cts.Token);
        await cts.CancelAsync();

        pending.IsCompleted.Should().BeFalse("hủy token không làm IgnoreToken dừng chờ");
        clock.Advance(TimeSpan.FromMilliseconds(DelayMs));
        (await pending).Should().BeSameAs(expected);
        inner.InvocationCount.Should().Be(1);
    }

    /// <summary>FI-05: <c>BusyCpu</c> quay vòng <b>đồng bộ</b> ít nhất <c>DelayMs</c> trước khi trả task (task trả về đã hoàn tất) rồi gọi strategy thật.</summary>
    [Fact]
    [Trait("CatalogId", "FI-05")]
    public void EvaluateAsync_BusyCpuProfile_BlocksCallingThreadForDelay()
    {
        const int busyMs = 60;
        var inner = new DelegateStrategy((_, _, _) => Task.FromResult(Approved()));
        var decorator = new FaultInjectionStrategyDecorator(inner, Options(FaultInjectionProfile.BusyCpu, busyMs), TimeProvider.System);

        var stopwatch = Stopwatch.StartNew();
        var pending = EvaluateAsync(decorator, CancellationToken.None);
        stopwatch.Stop();

        pending.IsCompleted.Should().BeTrue("quay vòng xong ngay trong lời gọi, không trả task đang chờ");
        stopwatch.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(busyMs - 1);
        inner.InvocationCount.Should().Be(1);
    }

    /// <summary>UT-09: bọc <c>DefaultDecisionStrategy</c> thì tên và phiên bản giữ nguyên; cache giữ chính sách thật khi <c>None</c> và tắt khi có lỗi được tiêm.</summary>
    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData(FaultInjectionProfile.None, true)]
    [InlineData(FaultInjectionProfile.Delay, false)]
    [InlineData(FaultInjectionProfile.Throw, false)]
    [InlineData(FaultInjectionProfile.IgnoreToken, false)]
    [InlineData(FaultInjectionProfile.BusyCpu, false)]
    public void Identity_MatchesInnerStrategy(FaultInjectionProfile profile, bool cacheFollowsInner)
    {
        var inner = StrategyRegistrations.CreateDefault(new ConfigurationBuilder().Build(), DomainTestData.StandardCalendar()).Strategy;
        var decorator = new FaultInjectionStrategyDecorator(inner, Options(profile), TimeProvider.System);

        decorator.StrategyName.Should().Be(inner.StrategyName);
        decorator.StrategyVersion.Should().Be(inner.StrategyVersion);
        inner.CachePolicy.IsEnabled.Should().BeTrue("điều kiện của test: strategy thật bật cache");
        if (cacheFollowsInner)
        {
            decorator.CachePolicy.Should().BeSameAs(inner.CachePolicy);
        }
        else
        {
            decorator.CachePolicy.IsEnabled.Should().BeFalse("lỗi tiêm phải luôn tới được pipeline, không bị cache che");
        }
    }
}
