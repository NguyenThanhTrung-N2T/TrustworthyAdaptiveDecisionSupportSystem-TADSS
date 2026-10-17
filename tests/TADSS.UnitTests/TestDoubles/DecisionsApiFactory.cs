using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TADSS.Application.Decisions;
using TADSS.Application.Governance;
using TADSS.Application.Options;
using TADSS.Application.Outcomes;
using TADSS.UnitTests.TestData;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// Host in-process của TADSS cho test HTTP không cần PostgreSQL/Redis: chạy ở môi trường <c>Testing</c> (không nạp
/// <c>appsettings.Development.json</c>/User Secrets) và dùng chuỗi kết nối giả (<see cref="TestConnectionStrings"/>), thay <see cref="IDecisionService"/>/<see cref="IDecisionQueryService"/>/<see cref="IOverrideService"/>/<see cref="IOutcomeService"/>
/// bằng double nên <c>TadssDbContext</c>, health check và multiplexer Redis không bao giờ bị chạm tới.
/// </summary>
public sealed class DecisionsApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Tên môi trường của host thử nghiệm.</summary>
    public const string EnvironmentName = "Testing";

    /// <summary>Thời gian tối đa chờ host khởi động trong <see cref="CreateHost"/> (lưới an toàn, host bình thường khởi động trong vài giây).</summary>
    private static readonly TimeSpan HostStartLimit = TimeSpan.FromMinutes(2);

    /// <summary>Môi trường của host (mặc định <see cref="EnvironmentName"/>); đặt <c>Staging</c> để kiểm tra các quy tắc fail-closed ngoài Development/Testing.</summary>
    internal string HostEnvironment { get; init; } = EnvironmentName;

    /// <summary>
    /// API key được cấu hình cho host (mặc định chỉ <see cref="TestApiKeys.Default"/>); danh sách rỗng nghĩa là <c>ApiKeys</c> rỗng.
    /// Mọi <see cref="HttpClient"/> mặc định gửi <see cref="TestApiKeys.DefaultKey"/> (xem <see cref="ConfigureClient"/>).
    /// </summary>
    internal IReadOnlyList<TestApiKeys.Client> ApiKeys { get; init; } = TestApiKeys.DefaultClients;

    /// <summary>
    /// <c>true</c> (mặc định): <see cref="IDecisionService"/>/<see cref="IDecisionQueryService"/> là double. <c>false</c>: giữ service thật
    /// (chỉ dùng để kiểm đồ thị DI, không gọi service). Phải đặt trước khi host được tạo.
    /// </summary>
    public bool UseDoubles { get; init; } = true;

    /// <summary>Double của <see cref="IDecisionService"/>.</summary>
    public FakeDecisionService DecisionService { get; } = new();

    /// <summary>Double của <see cref="IDecisionQueryService"/>.</summary>
    public FakeDecisionQueryService QueryService { get; } = new();

    /// <summary>Double của <see cref="IOverrideService"/>.</summary>
    public FakeOverrideService OverrideService { get; } = new();

    /// <summary>Double của <see cref="IOutcomeService"/>.</summary>
    public FakeOutcomeService OutcomeService { get; } = new();

    /// <summary>Trả cả bốn double về trạng thái ban đầu (gọi ở đầu mỗi test dùng chung fixture).</summary>
    public void ResetDoubles()
    {
        DecisionService.Reset();
        QueryService.Reset();
        OverrideService.Reset();
        OutcomeService.Reset();
    }

    /// <summary>Mọi client (kể cả của factory dẫn xuất từ <c>WithWebHostBuilder</c>) mặc định gửi API key mặc định nên test cũ không phải sửa từng chỗ.</summary>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        TestApiKeys.ApplyDefaultKey(client);
    }

    /// <summary>
    /// Khởi động host tất định (thay <c>Build()</c> + <c>Start()</c> mặc định): <c>Program</c> chạy trên thread riêng và chỉ được vào
    /// <c>Host.StartAsync</c> sau khi thread này đã đăng ký chờ host (xem <see cref="GatedHostLifetime"/>), nên host khởi động thất bại luôn
    /// lộ đúng exception gốc (vd. <c>OptionsValidationException</c> của <c>ValidateOnStart</c>) thay vì <see cref="ObjectDisposedException"/>
    /// khi máy bận. Áp cho cả factory dẫn xuất qua <c>WithWebHostBuilder</c>. Cổng chỉ che lỗi xảy ra bên trong <c>Host.StartAsync</c>; lỗi ở
    /// <c>StartupTasks</c> (trước <c>app.Run()</c>) không race vì <c>Program.cs</c> không dispose <c>app</c> khi đó — giả định này phải giữ.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        builder.ConfigureServices(services => GatedHostLifetime.Decorate(services, gate.Task));
        try
        {
            var host = builder.Build();
            // StartAsync của host trì hoãn (DeferredHost) resolve IHostApplicationLifetime và đăng ký ApplicationStarted đồng bộ, trước lần await
            // đầu tiên; chỉ sau đó mới mở cổng cho Program vào Host.StartAsync (nơi có thể thất bại và dispose host)
            var start = host.StartAsync();
            gate.TrySetResult();
            // Chờ có hạn: lỗi của cổng sau này làm test đỏ nhanh (TimeoutException) thay vì treo cả lượt test
            start.WaitAsync(HostStartLimit).GetAwaiter().GetResult();
            return host;
        }
        finally
        {
            // Build lỗi trước khi tới StartAsync: vẫn mở cổng để thread của Program không treo
            gate.TrySetResult();
        }
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironment);
        // appsettings.json chỉ có chuỗi giữ chỗ (bị DatabaseOptionsValidator từ chối) và ApiKeys rỗng: cấp chuỗi test (không bao giờ mở kết nối)
        // và API key giả qua cấu hình, như người vận hành cấp qua biến môi trường.
        // Tắt làm nóng: nó mở kết nối tới chuỗi giả (lỗi, chờ retry) và chạy strategy ở mỗi lần khởi động host, làm chậm cả bộ test trên CI ít CPU
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection([TestConnectionStrings.Pair, new($"{WarmupOptions.SectionName}:{nameof(WarmupOptions.Enabled)}", "false")]);
            configuration.AddInMemoryCollection(TestApiKeys.ToConfiguration(ApiKeys));
        });
        if (!UseDoubles)
        {
            return;
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDecisionService>();
            services.RemoveAll<IDecisionQueryService>();
            services.RemoveAll<IOverrideService>();
            services.RemoveAll<IOutcomeService>();
            services.AddSingleton<IDecisionService>(DecisionService);
            services.AddSingleton<IDecisionQueryService>(QueryService);
            services.AddSingleton<IOverrideService>(OverrideService);
            services.AddSingleton<IOutcomeService>(OutcomeService);
        });
    }
}
