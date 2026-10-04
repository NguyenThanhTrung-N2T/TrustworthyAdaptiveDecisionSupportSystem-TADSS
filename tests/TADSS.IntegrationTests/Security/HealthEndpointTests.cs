using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TADSS.Infrastructure.Options;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests.Security;

/// <summary>
/// SEC-5: <c>/health/live</c> và <c>/health/ready</c> trên PostgreSQL + Redis thật, ngắt từng đường qua Toxiproxy. PostgreSQL bắt buộc (mất → ready 503
/// <c>Unhealthy</c>, live vẫn 200); Redis chỉ làm ready <c>Degraded</c> (vẫn 200); body chỉ là chữ trạng thái, không lộ chuỗi kết nối hay tên exception;
/// endpoint gộp <c>/health</c> đã gỡ (404). Host đi tới PostgreSQL và Redis qua Toxiproxy (chỉ cho health check, qua <c>DatabaseOptions</c>/<c>CacheOptions</c>);
/// mọi proxy luôn được bật lại trong <c>DisposeAsync</c> để lỗi mạng không rò sang test khác.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class HealthEndpointTests : IntegrationTestBase
{
    private const string LivePath = "/health/live";
    private const string ReadyPath = "/health/ready";

    private HttpClient? _api;

    public HealthEndpointTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>Client của host mà health check đi qua Toxiproxy; dựng khi truy cập lần đầu.</summary>
    private HttpClient Api => _api ??= CreateFactory(builder => builder.ConfigureTestServices(services =>
    {
        services.PostConfigure<DatabaseOptions>(options => options.ConnectionString = Infrastructure.BuildProxiedConnectionString(DatabaseName));
        services.PostConfigure<CacheOptions>(options => options.RedisConnectionString = Infrastructure.ProxiedRedisConnectionString);
    })).CreateClient();

    /// <inheritdoc />
    public override async Task DisposeAsync()
    {
        _api?.Dispose();

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

    /// <summary>Mọi thứ bình thường: ready 200 với body đúng chữ <c>Healthy</c>; live 200.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-5")]
    public async Task Ready_AllHealthy_Is200Healthy()
    {
        var (readyStatus, readyBody) = await GetAsync(ReadyPath);
        var (liveStatus, liveBody) = await GetAsync(LivePath);

        readyStatus.Should().Be(HttpStatusCode.OK);
        readyBody.Should().Be("Healthy");
        liveStatus.Should().Be(HttpStatusCode.OK);
        liveBody.Should().Be("Healthy");
    }

    /// <summary>Ngắt proxy <c>postgres</c>: ready 503 <c>Unhealthy</c> (PostgreSQL bắt buộc), live vẫn 200 vì không chạy check nào; bật lại thì ready hồi phục.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-5")]
    public async Task Ready_PostgresCut_Is503_LiveStays200()
    {
        await Infrastructure.Toxiproxy.SetEnabledAsync(TadssInfrastructureFixture.PostgresProxy, enabled: false);

        var (readyStatus, readyBody) = await GetAsync(ReadyPath);
        var (liveStatus, _) = await GetAsync(LivePath);

        readyStatus.Should().Be(HttpStatusCode.ServiceUnavailable);
        readyBody.Should().Be("Unhealthy");
        liveStatus.Should().Be(HttpStatusCode.OK);

        await Infrastructure.Toxiproxy.ResetAsync();
        (await GetAsync(ReadyPath)).Status.Should().Be(HttpStatusCode.OK, "hết lỗi mạng thì readiness tự hồi phục");
    }

    /// <summary>
    /// Ngắt proxy <c>redis</c> sau khi kết nối đã thiết lập: ready vẫn 200 nhưng body <c>Degraded</c> (cache có fallback memory), live 200;
    /// bật lại thì ready về <c>Healthy</c>.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "SEC-5")]
    public async Task Ready_RedisCut_Is200Degraded()
    {
        (await GetAsync(ReadyPath)).Body.Should().Be("Healthy", "làm nóng: tạo multiplexer khi Redis còn thông");
        await Infrastructure.Toxiproxy.SetEnabledAsync(TadssInfrastructureFixture.RedisProxy, enabled: false);

        var (readyStatus, readyBody) = await GetAsync(ReadyPath);
        var (liveStatus, _) = await GetAsync(LivePath);

        readyStatus.Should().Be(HttpStatusCode.OK);
        readyBody.Should().Be("Degraded");
        liveStatus.Should().Be(HttpStatusCode.OK);

        await Infrastructure.Toxiproxy.ResetAsync();
        (await GetAsync(ReadyPath)).Body.Should().Be("Healthy", "Redis hồi phục thì readiness về Healthy");
    }

    /// <summary>
    /// Ở cả ba tình huống (bình thường, mất PostgreSQL, mất Redis) body của <c>/health/live</c> và <c>/health/ready</c> chỉ là chữ trạng thái:
    /// không chứa <c>Host=</c>, <c>password</c>, tên exception hay tên hạ tầng.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "SEC-5")]
    [InlineData("healthy")]
    [InlineData("postgres-cut")]
    [InlineData("redis-cut")]
    public async Task HealthBodies_NeverLeakInfrastructureDetails_Theory(string scenario)
    {
        if (scenario == "redis-cut")
        {
            await GetAsync(ReadyPath);
            await Infrastructure.Toxiproxy.SetEnabledAsync(TadssInfrastructureFixture.RedisProxy, enabled: false);
        }
        else if (scenario == "postgres-cut")
        {
            await Infrastructure.Toxiproxy.SetEnabledAsync(TadssInfrastructureFixture.PostgresProxy, enabled: false);
        }

        foreach (var path in new[] { LivePath, ReadyPath })
        {
            var (_, body) = await GetAsync(path);

            body.Should().BeOneOf("Healthy", "Degraded", "Unhealthy");
            body.Should().NotContainEquivalentOf("Host=")
                .And.NotContainEquivalentOf("password")
                .And.NotContainEquivalentOf("Exception")
                .And.NotContainEquivalentOf("Npgsql")
                .And.NotContainEquivalentOf("StackExchange")
                .And.NotContainEquivalentOf("redis");
        }
    }

    /// <summary>Endpoint gộp <c>/health</c> đã gỡ (F-16): 404, không phải 401 hay 200.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-5")]
    public async Task AggregateHealthEndpoint_IsRemoved()
    {
        var (status, _) = await GetAsync("/health");

        status.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(HttpStatusCode Status, string Body)> GetAsync(string path)
    {
        using var response = await Api.GetAsync(path);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
