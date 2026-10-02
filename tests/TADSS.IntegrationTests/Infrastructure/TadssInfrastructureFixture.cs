using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;
using TADSS.Runtime.Api.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Testcontainers.Toxiproxy;
using Xunit;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Hạ tầng thật dùng chung cho cả lượt chạy integration test: một Docker network, PostgreSQL 16 và Redis 7.
/// Áp migration một lần vào database mẫu; mỗi test nhân bản database riêng từ mẫu để cô lập dữ liệu.
/// </summary>
public sealed class TadssInfrastructureFixture : IAsyncLifetime
{
    public const string PostgresImage = "postgres:16.15-alpine";
    public const string RedisImage = "redis:7.4-alpine";
    public const string ToxiproxyImage = "ghcr.io/shopify/toxiproxy:2.12.0";
    public const string PostgresProxy = "postgres";
    public const string RedisProxy = "redis";
    private const string TemplateDatabase = "tadss_template";
    private const int PostgresProxyPort = 8666;
    private const int RedisProxyPort = 8667;

    private readonly INetwork _network;
    private readonly PostgreSqlContainer _postgres;
    private readonly RedisContainer _redis;
    private readonly ToxiproxyContainer _toxiproxy;
    private ToxiproxyControl? _toxiproxyControl;
    private ConnectionMultiplexer? _redisAdmin;

    public TadssInfrastructureFixture()
    {
        _network = new NetworkBuilder().Build();
        _postgres = new PostgreSqlBuilder(PostgresImage)
            .WithNetwork(_network)
            .WithNetworkAliases("postgres")
            .Build();
        _redis = new RedisBuilder(RedisImage)
            .WithNetwork(_network)
            .WithNetworkAliases("redis")
            .Build();
        _toxiproxy = new ToxiproxyBuilder(ToxiproxyImage)
            .WithNetwork(_network)
            .WithPortBinding(PostgresProxyPort, true)
            .WithPortBinding(RedisProxyPort, true)
            .Build();
    }

    /// <summary>Chuỗi kết nối Redis (host:port) trỏ thẳng tới container.</summary>
    public string RedisConnectionString => _redis.GetConnectionString();

    /// <summary>
    /// Kết nối quản trị (<c>AllowAdmin</c>) thẳng tới Redis của container, dùng để dọn dữ liệu giữa các test và đọc key/giá trị mà
    /// cache của app đã ghi. Container chỉ phục vụ test nên được phép <c>FLUSHALL</c>.
    /// </summary>
    public IConnectionMultiplexer RedisAdmin =>
        _redisAdmin ?? throw new InvalidOperationException("Redis admin chỉ dùng được sau InitializeAsync.");

    /// <summary>Điều khiển Toxiproxy để tiêm lỗi mạng tới PostgreSQL/Redis.</summary>
    public ToxiproxyControl Toxiproxy =>
        _toxiproxyControl ?? throw new InvalidOperationException("Toxiproxy chỉ dùng được sau InitializeAsync.");

    /// <summary>Chuỗi kết nối Redis đi qua Toxiproxy.</summary>
    public string ProxiedRedisConnectionString =>
        $"{_toxiproxy.Hostname}:{_toxiproxy.GetMappedPublicPort(RedisProxyPort)}";

    /// <summary>Chuỗi kết nối PostgreSQL đi qua Toxiproxy; timeout kết nối 5 giây để lỗi được phát hiện nhanh.</summary>
    public string BuildProxiedConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Host = _toxiproxy.Hostname,
            Port = _toxiproxy.GetMappedPublicPort(PostgresProxyPort),
            Database = database,
            Timeout = 5
        }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _toxiproxy.StartAsync());

        _toxiproxyControl = new ToxiproxyControl(new Uri(
            $"http://{_toxiproxy.Hostname}:{_toxiproxy.GetMappedPublicPort(ToxiproxyBuilder.ToxiproxyControlPort)}"));
        await _toxiproxyControl.CreateProxyAsync(PostgresProxy, PostgresProxyPort, "postgres:5432");
        await _toxiproxyControl.CreateProxyAsync(RedisProxy, RedisProxyPort, "redis:6379");
        await VerifyProxiedPathsAsync();

        var redisAdminOptions = ConfigurationOptions.Parse(RedisConnectionString);
        redisAdminOptions.AllowAdmin = true;
        _redisAdmin = await ConnectionMultiplexer.ConnectAsync(redisAdminOptions);

        await ExecuteAdminAsync($"CREATE DATABASE {TemplateDatabase}");

        var options = new DbContextOptionsBuilder<TadssDbContext>()
            .UseNpgsql(BuildConnectionString(TemplateDatabase),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "tadss"))
            .Options;

        await using (var db = new TadssDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        // CREATE DATABASE ... TEMPLATE yêu cầu không còn kết nối nào tới database mẫu
        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>Xóa mọi key trong Redis của container để cache không rò rỉ giữa các test.</summary>
    public async Task FlushRedisAsync()
    {
        foreach (var endpoint in RedisAdmin.GetEndPoints())
        {
            await RedisAdmin.GetServer(endpoint).FlushAllDatabasesAsync();
        }
    }

    /// <summary>Tạo database mới (mặc định nhân bản từ mẫu đã migrate) và trả về tên database.</summary>
    public async Task<string> CreateDatabaseAsync(bool fromTemplate = true)
    {
        var name = $"tadss_test_{Guid.NewGuid():N}";
        var sql = fromTemplate
            ? $"CREATE DATABASE {name} TEMPLATE {TemplateDatabase}"
            : $"CREATE DATABASE {name}";
        await ExecuteAdminAsync(sql);
        return name;
    }

    /// <summary>
    /// Xóa database của một test. <c>WITH (FORCE)</c> (PostgreSQL ≥ 13) ngắt các kết nối còn mở,
    /// kể cả kết nối đi qua Toxiproxy.
    /// </summary>
    public async Task DropDatabaseAsync(string database)
    {
        // Bỏ các kết nối trong pool của database này để không giữ kết nối chết sau khi xóa
        await using (var pooled = new NpgsqlConnection(BuildConnectionString(database)))
        {
            NpgsqlConnection.ClearPool(pooled);
        }

        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
    }

    /// <summary>Chuỗi kết nối trực tiếp tới một database trong container PostgreSQL.</summary>
    public string BuildConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    /// <summary>
    /// Kiểm tra máy chạy test đi được tới PostgreSQL và Redis qua Toxiproxy
    /// (host → cổng ánh xạ của Toxiproxy → container upstream trong Docker network).
    /// </summary>
    private async Task VerifyProxiedPathsAsync()
    {
        try
        {
            // Dùng database hệ thống "postgres" để không giữ kết nối tới database mẫu
            await using var connection = new NpgsqlConnection(BuildProxiedConnectionString("postgres"));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();

            await using var redis = await ConnectionMultiplexer.ConnectAsync(ProxiedRedisConnectionString);
            await redis.GetDatabase().PingAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Không đi được tới PostgreSQL/Redis qua Toxiproxy — kiểm tra cấu hình proxy và Docker network.", ex);
        }
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        _toxiproxyControl?.Dispose();
        if (_redisAdmin is not null)
        {
            await _redisAdmin.DisposeAsync();
        }

        await _toxiproxy.DisposeAsync();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
        await _network.DisposeAsync();
    }
}
