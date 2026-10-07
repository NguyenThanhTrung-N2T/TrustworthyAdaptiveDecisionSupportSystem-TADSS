using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Common;
using TADSS.Infrastructure.Options;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.Runtime.Api.Composition;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.IntegrationTests.FaultInjection;

/// <summary>
/// FI-03 / UT-09 (O-17): đổi <b>nóng</b> profile tiêm lỗi trên host <c>Testing</c> thật qua file JSON do biến <c>TADSS_FAULT_INJECTION_FILE</c> chỉ tới (evaluator ghi file này giữa lượt tải):
/// ghi file <c>Throw</c> → trong 10 giây request fallback; ghi <c>None</c> → hết fallback; file cụt → giữ profile cũ, đếm lỗi nạp lại và vẫn phục vụ. Biến môi trường được đặt trước khi host dựng
/// (nguồn cấu hình thêm ở đầu <c>Program</c>) và gỡ khi test xong. Đồng hồ thật; cấu hình chịu lỗi có dư ngân sách như <c>ConfiguredFaultInjectionTests</c>, breaker nghỉ 2 giây để kiểm hồi phục.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class FaultInjectionReloadTests : DecisionApiTestBase
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(10);

    private string _directory = string.Empty;

    public FaultInjectionReloadTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    private string FilePath => Path.Combine(_directory, "fault.json");

    /// <inheritdoc />
    protected override bool UseFakeClock => false;

    /// <inheritdoc />
    protected override IEnumerable<KeyValuePair<string, string?>> ConfigurationOverrides =>
    [
        // Profile/DelayMs chỉ đến từ file (cấu hình đặt qua ConfigureAppConfiguration nạp sau file nên sẽ đè lên nó)
        new("Tadss:Testing:FaultInjection:Enabled", "true"),
        new("Tadss:Resilience:OuterTimeoutMs", "500"),
        new("Tadss:Resilience:InnerTimeoutMs", "100"),
        new("Tadss:Resilience:MaxRetryAttempts", "2"),
        new("Tadss:Resilience:RetryBaseDelayMs", "20"),
        new("Tadss:Resilience:CircuitBreakerBreakDurationSeconds", "2"),
    ];

    /// <inheritdoc />
    public override async Task InitializeAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "tadss-fault-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        WriteProfile("None");
        Environment.SetEnvironmentVariable(TestingConfigurationExtensions.FaultInjectionFileVariable, FilePath);
        await base.InitializeAsync();
    }

    /// <inheritdoc />
    public override async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable(TestingConfigurationExtensions.FaultInjectionFileVariable, null);
        await base.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Ghi file profile nguyên tử (file tạm rồi đổi tên), như evaluator.</summary>
    private void WriteProfile(string profile)
    {
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, $$"""{ "Tadss": { "Testing": { "FaultInjection": { "Profile": "{{profile}}", "DelayMs": 1000 } } } }""");
        File.Move(temp, FilePath, overwrite: true);
    }

    private async Task<bool> NextRequestIsFallbackAsync(int index)
    {
        var response = await PostDecisionAsync(DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(7).AddMinutes(index)), NewKey());
        response.Status.Should().Be(HttpStatusCode.OK, "dù profile nào request vẫn phải 200");
        return response.Result.ExecutionType == ExecutionType.FALLBACK;
    }

    private async Task WaitUntilAsync(Func<int, Task<bool>> condition, string because)
    {
        var deadline = DateTime.UtcNow + SettleTimeout;
        for (var attempt = 0; ; attempt++)
        {
            if (await condition(attempt))
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Điều kiện không đạt trong {SettleTimeout.TotalSeconds} giây: {because}");
            }

            await Task.Delay(100);
        }
    }

    /// <summary>
    /// FI-03: file <c>None</c> → POST 200 <c>AUTOMATED</c>; ghi file <c>Throw</c> → trong 10 giây POST trả fallback <c>FALLBACK_STRATEGY_EXCEPTION</c>; ghi lại <c>None</c> → hết fallback
    /// (sau thời gian nghỉ của breaker). Không khởi động lại host.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-03")]
    public async Task PostDecision_FaultFileChanged_ProfileSwitchesLive()
    {
        (await NextRequestIsFallbackAsync(0)).Should().BeFalse("file None: strategy chạy bình thường");

        WriteProfile("Throw");
        string? firstFallbackCode = null;
        await WaitUntilAsync(async attempt =>
        {
            var response = await PostDecisionAsync(DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(7).AddMinutes(100 + attempt)), NewKey());
            if (response.Result.ExecutionType != ExecutionType.FALLBACK)
            {
                return false;
            }

            firstFallbackCode = response.Result.ReasonCode;
            return true;
        }, "file Throw phải làm request fallback");
        firstFallbackCode.Should().Be(ReasonCodes.FallbackStrategyException);

        WriteProfile("None");
        await WaitUntilAsync(async attempt => !await NextRequestIsFallbackAsync(200 + attempt), "file None phải hết fallback (sau thời gian nghỉ của breaker)");
    }

    /// <summary>
    /// File JSON cụt (nửa nội dung, ghi không nguyên tử): profile giữ nguyên (<c>None</c> nên request vẫn <c>AUTOMATED</c>), <c>tadss.config.reload_failures</c> của section tiêm lỗi tăng,
    /// tiến trình không sập và request vẫn được phục vụ; ghi lại file hợp lệ thì áp dụng bình thường.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public async Task Reload_TruncatedJsonFile_KeepsPrevious()
    {
        using var recorder = new MetricRecorder(Host.Services.GetRequiredService<TadssMetrics>().Meter);
        (await NextRequestIsFallbackAsync(0)).Should().BeFalse();

        File.WriteAllText(FilePath, """{ "Tadss": { "Testing": { "FaultInjection": { "Prof""", Encoding.UTF8);

        await WaitUntilAsync(
            _ => Task.FromResult(recorder.Sum(TadssMetricNames.ConfigReloadFailures, (TadssMetricNames.LabelSection, FaultInjectionOptions.SectionName)) >= 1),
            "file cụt phải được đếm là nạp lại thất bại");
        (await NextRequestIsFallbackAsync(1)).Should().BeFalse("profile cũ (None) được giữ và host vẫn phục vụ");

        WriteProfile("Throw");
        await WaitUntilAsync(async attempt => await NextRequestIsFallbackAsync(300 + attempt), "file hợp lệ sau khi cụt vẫn được áp dụng");
    }
}
