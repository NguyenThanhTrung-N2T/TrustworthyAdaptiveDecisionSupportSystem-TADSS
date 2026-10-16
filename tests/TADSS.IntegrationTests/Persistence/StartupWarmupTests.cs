using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TADSS.Application.Options;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.Runtime.Api.Common;
using Xunit;

namespace TADSS.IntegrationTests.Persistence;

/// <summary>
/// IT-01: làm nóng lúc khởi động (<c>Tadss:Warmup</c>) trên PostgreSQL và Redis thật — mở sẵn kết nối tới database của instance nhưng KHÔNG
/// để lại bản ghi audit nào (bảng append-only, bất biến); sau đó request đầu tiên vẫn ghi đúng một bản <c>PRIMARY</c>.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class StartupWarmupTests : IntegrationTestBase
{
    public StartupWarmupTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>Khởi động có làm nóng: đủ kết nối mặc định đang mở tới database, 0 bản ghi audit; request đầu tiên 200 và đúng 1 bản ghi.</summary>
    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task Startup_WithWarmup_OpensConnectionsWithoutWritingAudit()
    {
        await using var factory = new TadssWebApplicationFactory(ConnectionString, Infrastructure.RedisConnectionString) { Warmup = true };
        using var client = factory.CreateClient();

        // CreateClient khởi động host: StartupTasks (gồm làm nóng) đã chạy xong trước khi trả về
        (await CountWarmupConnectionsAsync()).Should().BeGreaterThanOrEqualTo(
            new WarmupOptions().DatabaseConnections, "làm nóng mở song song đủ số kết nối mặc định và chúng ở lại trong pool");
        await using (var db = CreateDbContext())
        {
            (await db.DecisionExecutions.CountAsync()).Should().Be(0, "làm nóng không bao giờ ghi audit");
        }

        using var content = new StringContent(DecisionRequests.Valid(), System.Text.Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/decisions") { Content = content };
        request.Headers.Add(HttpHeaderNames.IdempotencyKey, "warmup-it-first-request");
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var db = CreateDbContext())
        {
            (await db.DecisionExecutions.CountAsync()).Should().Be(1);
        }
    }

    // Kết nối của chính app tới database của test (trừ kết nối đang dùng để đếm)
    private async Task<long> CountWarmupConnectionsAsync()
    {
        await using var connection = new NpgsqlConnection(Infrastructure.BuildConnectionString("postgres"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname = @database", connection);
        command.Parameters.AddWithValue("database", DatabaseName);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
