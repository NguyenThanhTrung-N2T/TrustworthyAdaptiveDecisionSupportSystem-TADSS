using FluentAssertions;
using Microsoft.Extensions.Logging;
using TADSS.Application.Common;
using TADSS.Infrastructure.FaultInjection;
using TADSS.Infrastructure.Options;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.FaultInjection;

/// <summary>
/// UT-09 / FI-03 (O-17): <see cref="FaultInjectionControl"/> đổi <b>nóng</b> <c>Profile</c>/<c>DelayMs</c> khi cấu hình đổi, mà không khởi động lại: bản mới hợp lệ được áp (một dòng Warning),
/// bản sai (không bind được hoặc không qua validator) bị bỏ và giữ bản cũ (Error + đếm <c>tadss.config.reload_failures</c>), <c>Enabled</c>/<c>TargetStrategy</c> đổi lúc chạy không được áp
/// (Information), và đọc đồng thời không bao giờ thấy cặp (profile, delay) lai.
/// </summary>
public class FaultInjectionControlTests
{
    private const string Section = FaultInjectionOptions.SectionName;

    private static FaultInjectionOptions Initial(FaultInjectionProfile profile = FaultInjectionProfile.None, int delayMs = 1000) =>
        new() { Enabled = true, Profile = profile, DelayMs = delayMs };

    /// <summary>Đổi <c>Profile</c> sang <c>Throw</c> khi đang chạy: control áp ngay, giữ <c>DelayMs</c>, ghi đúng một Warning nêu profile mới và không đếm lỗi.</summary>
    [Fact]
    [Trait("CatalogId", "FI-03")]
    public void Reload_ValidProfile_AppliesWithoutRestart()
    {
        using var harness = new FaultInjectionControlHarness(Initial());
        using var recorder = new MetricRecorder(harness.Metrics.Meter);

        harness.Set(Initial(FaultInjectionProfile.Throw));

        harness.Control.Current.Should().Be(new FaultInjectionSnapshot(FaultInjectionProfile.Throw, 1000));
        var warning = harness.Logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain("Throw");
        recorder.Sum(TadssMetricNames.ConfigReloadFailures).Should().Be(0);
    }

    /// <summary>Cấu hình sai (<c>DelayMs = 0</c> không qua validator; <c>Profile = "Bogus"</c> không bind được): giữ bản cũ, một Error và <c>reload_failures</c> của section = 1.</summary>
    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData("DelayMs", "0")]
    [InlineData("Profile", "Bogus")]
    public void Reload_Invalid_KeepsPreviousAndCountsFailure_Theory(string key, string value)
    {
        using var harness = new FaultInjectionControlHarness(Initial(FaultInjectionProfile.Delay, 250));
        using var recorder = new MetricRecorder(harness.Metrics.Meter);

        harness.SetRaw(key, value);

        harness.Control.Current.Should().Be(new FaultInjectionSnapshot(FaultInjectionProfile.Delay, 250));
        harness.Logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error);
        recorder.Sum(TadssMetricNames.ConfigReloadFailures, (TadssMetricNames.LabelSection, Section)).Should().Be(1);
    }

    /// <summary>Section biến mất (file cụt hoặc bị xóa): coi là nạp lại thất bại, giữ bản cũ, một Error và đếm một lần lỗi — không âm thầm về profile mặc định.</summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Reload_SectionDisappears_KeepsPreviousAndCountsFailure()
    {
        using var harness = new FaultInjectionControlHarness(Initial(FaultInjectionProfile.Throw, 700));
        using var recorder = new MetricRecorder(harness.Metrics.Meter);

        harness.Clear();

        harness.Control.Current.Should().Be(new FaultInjectionSnapshot(FaultInjectionProfile.Throw, 700));
        harness.Logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error);
        recorder.Sum(TadssMetricNames.ConfigReloadFailures, (TadssMetricNames.LabelSection, Section)).Should().Be(1);
    }

    /// <summary>
    /// Khóa <c>Profile</c> mất nhưng section vẫn còn (file cụt nên nguồn JSON bị xóa, trong khi <c>Enabled</c> đến từ nguồn khác): không được âm thầm về profile mặc định (gỡ lỗi đang tiêm);
    /// giữ bản cũ và đếm lỗi.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Reload_ProfileKeyDisappears_KeepsPreviousAndCountsFailure()
    {
        using var harness = new FaultInjectionControlHarness(Initial(FaultInjectionProfile.Throw, 700));
        using var recorder = new MetricRecorder(harness.Metrics.Meter);

        harness.SetRaw("Profile", null);

        harness.Control.Current.Should().Be(new FaultInjectionSnapshot(FaultInjectionProfile.Throw, 700));
        recorder.Sum(TadssMetricNames.ConfigReloadFailures, (TadssMetricNames.LabelSection, Section)).Should().Be(1);
    }

    /// <summary>Sau một lần nạp sai, lần nạp hợp lệ kế tiếp vẫn được áp (callback không bị hỏng sau lỗi).</summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Reload_ValidAfterInvalid_Applies()
    {
        using var harness = new FaultInjectionControlHarness(Initial());

        harness.SetRaw("Profile", "Bogus");
        harness.Set(Initial(FaultInjectionProfile.IgnoreToken, 40));

        harness.Control.Current.Should().Be(new FaultInjectionSnapshot(FaultInjectionProfile.IgnoreToken, 40));
    }

    /// <summary>
    /// 8 luồng đọc <c>Current</c> liên tục trong khi cấu hình đổi xen kẽ (Delay, 1000) ↔ (Throw, 5000) 1000 lần: mọi snapshot đọc được là một trong ba cặp gốc
    /// (ban đầu, hai cặp được ghi), không bao giờ có cặp lai như (Throw, 1000).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public async Task Current_ConcurrentReload_NeverMixesFields()
    {
        var first = new FaultInjectionSnapshot(FaultInjectionProfile.Delay, 1000);
        var second = new FaultInjectionSnapshot(FaultInjectionProfile.Throw, 5000);
        using var harness = new FaultInjectionControlHarness(Initial(first.Profile, first.DelayMs));
        using var stop = new CancellationTokenSource();
        var observed = new List<FaultInjectionSnapshot>[8];

        var readers = Enumerable.Range(0, observed.Length).Select(index => Task.Run(() =>
        {
            observed[index] = [];
            while (!stop.IsCancellationRequested)
            {
                observed[index].Add(harness.Control.Current);
            }
        })).ToArray();
        for (var i = 0; i < 1000; i++)
        {
            harness.Set(Initial(second.Profile, second.DelayMs));
            harness.Set(Initial(first.Profile, first.DelayMs));
        }

        await stop.CancelAsync();
        await Task.WhenAll(readers);

        var all = observed.SelectMany(list => list).Distinct().ToList();
        all.Should().NotBeEmpty();
        all.Should().OnlyContain(snapshot => snapshot == first || snapshot == second, "không được có cặp (profile, delay) lai");
    }

    /// <summary>
    /// <c>Enabled</c> hoặc <c>TargetStrategy</c> đổi lúc chạy không được áp: một Information nêu "sau khi khởi động lại", profile hiện hành không đổi, không đếm lỗi.
    /// Khi cùng lúc đổi cả <c>Profile</c> thì profile vẫn được áp (chỉ Enabled/Target bị bỏ qua).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Reload_EnabledOrTarget_NotApplied()
    {
        using var harness = new FaultInjectionControlHarness(Initial(FaultInjectionProfile.Delay, 300));
        using var recorder = new MetricRecorder(harness.Metrics.Meter);

        harness.Set(new FaultInjectionOptions { Enabled = false, Profile = FaultInjectionProfile.Delay, DelayMs = 300, TargetStrategy = "Other" });

        harness.Control.Current.Should().Be(new FaultInjectionSnapshot(FaultInjectionProfile.Delay, 300));
        harness.Logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Information)
            .Which.Message.Should().Contain("khởi động lại");
        harness.Logs.Entries.Should().NotContain(entry => entry.Level >= LogLevel.Warning);
        recorder.Sum(TadssMetricNames.ConfigReloadFailures).Should().Be(0);

        harness.Set(new FaultInjectionOptions { Enabled = false, Profile = FaultInjectionProfile.Throw, DelayMs = 300, TargetStrategy = "Other" });

        harness.Control.Current.Profile.Should().Be(FaultInjectionProfile.Throw);
    }

    /// <summary>Cấu hình đổi nhưng phần tiêm lỗi không đổi (ví dụ section khác thay đổi): không log, không đếm lỗi, snapshot giữ nguyên tham chiếu.</summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Reload_NoEffectiveChange_IsSilent()
    {
        using var harness = new FaultInjectionControlHarness(Initial(FaultInjectionProfile.Delay, 300));
        var before = harness.Control.Current;

        harness.Set(Initial(FaultInjectionProfile.Delay, 300));

        harness.Control.Current.Should().BeSameAs(before);
        harness.Logs.Entries.Should().BeEmpty();
    }
}
