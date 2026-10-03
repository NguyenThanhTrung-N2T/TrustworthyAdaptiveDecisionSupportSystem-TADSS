using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TADSS.Application.Decisions;
using TADSS.Application.Governance;
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

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        // appsettings.json chỉ có chuỗi giữ chỗ (bị DatabaseOptionsValidator từ chối); chuỗi test không bao giờ mở kết nối
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection([TestConnectionStrings.Pair]));
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
