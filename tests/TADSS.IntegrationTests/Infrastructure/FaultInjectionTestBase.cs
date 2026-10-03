using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Shared.DecisionContractSDK.Decisions;
using TADSS.Application.Abstractions.Caching;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Strategies;
using TADSS.Infrastructure.Persistence;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Resilience;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Lớp cơ sở cho test tiêm lỗi (FI-xx) trên host thật: dùng <b>đồng hồ thật</b> (đồng hồ đóng băng sẽ đóng băng timer của Polly nên timeout,
/// retry delay và break duration không bao giờ đến hạn) và cho mỗi test chọn cấu hình chịu lỗi riêng (<see cref="Resilience"/>, phải qua
/// <see cref="ResilienceOptionsValidator"/>), strategy giả (<see cref="Strategy"/>), cache hỏng, điểm cắt trước khi ghi audit
/// (<see cref="AuditProbe"/>) và đường PostgreSQL qua Toxiproxy (<see cref="ProxyPostgres"/>). Mọi thay thế DI chỉ nằm trong host test (C-10).
/// <para>
/// Test phải khai báo kịch bản (<see cref="Resilience"/>, <see cref="Strategy"/>…) trước khi gửi request đầu tiên: host được dựng khi
/// <see cref="DecisionApiTestBase.Api"/> được truy cập lần đầu. Mọi proxy Toxiproxy luôn được khôi phục trong <see cref="DisposeAsync"/>.
/// </para>
/// </summary>
public abstract class FaultInjectionTestBase : DecisionApiTestBase
{
    /// <summary>Hạ tầng thật dùng chung.</summary>
    protected FaultInjectionTestBase(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>Strategy giả duy nhất của host test; luôn có sẵn, test chỉ cần đặt <see cref="ScriptedStrategy.Behavior"/>.</summary>
    protected ScriptedStrategy Strategy { get; } = new();

    /// <summary>Cấu hình <c>Tadss:Resilience</c> của test; mặc định là cấu hình production (Outer 500, Inner 120, retry 2 × 30 ms = 420 ms).</summary>
    protected ResilienceOptions Resilience { get; set; } = new();

    /// <summary>Cache thay cho cache thật; <c>null</c> thì giữ cache Redis thật.</summary>
    protected IStrategyDecisionCache? Cache { get; set; }

    /// <summary>Trạng thái dùng chung với decorator của hai port audit (<see cref="IDecisionEvaluationStore"/>/<see cref="IDecisionQueryStore"/>) (đếm lần ghi, điểm cắt trước khi ghi).</summary>
    protected AuditInsertProbe AuditProbe { get; } = new();

    /// <summary>Đi tới PostgreSQL qua Toxiproxy (để test ngắt được kết nối); mặc định đi thẳng.</summary>
    protected bool ProxyPostgres { get; set; }

    /// <summary>Ghi nhận thời điểm request kết thúc ở phía server (kể cả khi bị hủy).</summary>
    protected RequestCompletionProbe Completion { get; } = new();

    /// <summary>Log của <see cref="StrategyPipeline"/> (dùng để quan sát chuyển trạng thái của circuit breaker).</summary>
    protected CapturingLogger<StrategyPipeline> PipelineLogs { get; } = new();

    /// <inheritdoc />
    protected override bool UseFakeClock => false;

    /// <inheritdoc />
    public override async Task DisposeAsync()
    {
        // Luôn bật lại proxy để lỗi mạng không rò sang test khác trong collection (kể cả khi test thất bại giữa chừng)
        try
        {
            await Infrastructure.Toxiproxy.ResetAsync();
        }
        finally
        {
            await base.DisposeAsync();
        }
    }

    /// <inheritdoc />
    protected override void ConfigureHostServices(IServiceCollection services)
    {
        var validation = new ResilienceOptionsValidator().Validate(null, Resilience);
        validation.Succeeded.Should().BeTrue("cấu hình chịu lỗi của test phải qua ResilienceOptionsValidator: {0}", validation.FailureMessage);
        services.Configure<ResilienceOptions>(options =>
        {
            options.OuterTimeoutMs = Resilience.OuterTimeoutMs;
            options.InnerTimeoutMs = Resilience.InnerTimeoutMs;
            options.MaxRetryAttempts = Resilience.MaxRetryAttempts;
            options.RetryBaseDelayMs = Resilience.RetryBaseDelayMs;
            options.CircuitBreakerSamplingSeconds = Resilience.CircuitBreakerSamplingSeconds;
            options.CircuitBreakerFailureRatio = Resilience.CircuitBreakerFailureRatio;
            options.CircuitBreakerMinimumThroughput = Resilience.CircuitBreakerMinimumThroughput;
            options.CircuitBreakerBreakDurationSeconds = Resilience.CircuitBreakerBreakDurationSeconds;
        });

        services.RemoveAll<IStrategyRegistry>();
        services.AddSingleton<IStrategyRegistry>(new ScriptedStrategyRegistry(Strategy));

        if (Cache is { } cache)
        {
            services.RemoveAll<IStrategyDecisionCache>();
            services.AddSingleton(cache);
        }

        services.AddSingleton(AuditProbe);
        services.RemoveAll<IDecisionEvaluationStore>();
        services.RemoveAll<IDecisionQueryStore>();
        services.AddScoped(sp =>
        {
            var store = sp.GetRequiredService<EfDecisionAuditStore>();
            return new CuttableDecisionAuditStore(store, store, AuditProbe);
        });
        services.AddScoped<IDecisionEvaluationStore>(sp => sp.GetRequiredService<CuttableDecisionAuditStore>());
        services.AddScoped<IDecisionQueryStore>(sp => sp.GetRequiredService<CuttableDecisionAuditStore>());

        if (ProxyPostgres)
        {
            TadssWebApplicationFactory.ReplaceDbContext(services, Infrastructure.BuildProxiedConnectionString(DatabaseName));
        }

        services.AddSingleton<IStartupFilter>(Completion);
        services.AddSingleton<ILogger<StrategyPipeline>>(PipelineLogs);
    }

    /// <summary>
    /// Request mẫu có giờ bắt đầu cách hiện tại 7 ngày (đồng hồ thật), đủ xa để approval window còn khả dụng và
    /// <c>NEED_APPROVAL</c> của fallback không bị đổi thành <c>APPROVED</c> cọc 100%.
    /// </summary>
    protected static string UpcomingRequest() => DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(7));

    /// <summary>
    /// Làm nóng host bằng một request <c>POST</c> thật cho strategy khỏe (dựng host, nạp model EF, mở kết nối PostgreSQL, JIT đường validate/ghi audit)
    /// để các phép đo thời gian của test không chứa chi phí khởi động lạnh. Hành vi của strategy được khôi phục sau đó. Request này <b>đã</b> để lại
    /// một hàng audit và tăng <see cref="ScriptedStrategy.InvocationCount"/> một lần; hàm trả về idempotency key của nó để test loại ra khỏi phép đếm.
    /// </summary>
    protected async Task<string> WarmUpHostAsync()
    {
        var original = Strategy.Behavior;
        Strategy.Behavior = (_, _) => Task.FromResult(ScriptedStrategy.Approve());
        try
        {
            var key = NewKey();
            var response = await PostDecisionAsync(UpcomingRequest(), key);
            response.Status.Should().Be(System.Net.HttpStatusCode.OK, "request làm nóng phải thành công");
            return key;
        }
        finally
        {
            Strategy.Behavior = original;
        }
    }
}

/// <summary>
/// Startup filter chỉ dùng trong host test: bọc toàn bộ pipeline HTTP để biết chính xác khi nào server xử lý xong một request
/// (kể cả khi client hủy giữa chừng; chỉ tính <c>POST</c> để lệnh <c>GET</c> làm nóng không làm nhiễu), nhờ đó test khẳng định "không hàng audit" sau khi request đã kết thúc thay vì chờ theo thời gian.
/// </summary>
public sealed class RequestCompletionProbe : IStartupFilter
{
    private readonly TaskCompletionSource<long> _firstCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Hoàn tất (giá trị là mốc <see cref="Stopwatch.GetTimestamp"/>) khi request <c>POST</c> đầu tiên kết thúc ở phía server.</summary>
    public Task<long> FirstCompleted => _firstCompleted.Task;

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, pipeline) =>
        {
            try
            {
                await pipeline().ConfigureAwait(false);
            }
            finally
            {
                if (HttpMethods.IsPost(context.Request.Method))
                {
                    _firstCompleted.TrySetResult(Stopwatch.GetTimestamp());
                }
            }
        });
        next(app);
    };
}

/// <summary>
/// Logger ghi lại mọi dòng log vào bộ nhớ để test quan sát hành vi nội bộ của host (ví dụ chuyển trạng thái breaker). Được đăng ký thẳng cho
/// <c>ILogger&lt;T&gt;</c> trong host test vì Serilog của app thay <c>ILoggerFactory</c> nên một <c>ILoggerProvider</c> thêm vào sẽ không nhận được log.
/// </summary>
/// <typeparam name="T">Kiểu mà logger phục vụ.</typeparam>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> _messages = new();

    /// <summary>Các dòng log đã ghi, theo thứ tự thời gian.</summary>
    public IReadOnlyCollection<string> Messages => _messages;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _messages.Enqueue(formatter(state, exception));
}
