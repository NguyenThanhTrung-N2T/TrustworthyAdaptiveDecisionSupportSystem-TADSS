using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using TADSS.Infrastructure.Persistence;
using Xunit;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Lớp cơ sở cho integration test: mỗi test nhận một database PostgreSQL riêng (nhân bản từ mẫu đã migrate),
/// một <see cref="TadssWebApplicationFactory"/> trỏ tới container thật và một <see cref="HttpClient"/>.
/// Vòng đời database: tạo → test → xóa.
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly List<string> _ownedDatabases = new();
    private TadssWebApplicationFactory? _factory;
    private HttpClient? _client;

    protected IntegrationTestBase(TadssInfrastructureFixture infrastructure)
    {
        Infrastructure = infrastructure;
    }

    protected TadssInfrastructureFixture Infrastructure { get; }

    protected string DatabaseName { get; private set; } = string.Empty;

    protected string ConnectionString { get; private set; } = string.Empty;

    protected TadssWebApplicationFactory Factory =>
        _factory ?? throw new InvalidOperationException("Factory chỉ dùng được sau InitializeAsync.");

    protected HttpClient Client => _client ??= Factory.CreateClient();

    /// <summary>Tạo factory dẫn xuất để thay DI cho riêng một test; được giải phóng cùng <see cref="Factory"/>.</summary>
    protected WebApplicationFactory<Program> CreateFactory(Action<IWebHostBuilder> configure) =>
        Factory.WithWebHostBuilder(configure);

    /// <summary>Tạo thêm một database cho riêng test này (ví dụ database trống); được xóa trong <see cref="DisposeAsync"/>.</summary>
    protected async Task<string> CreateAdditionalDatabaseAsync(bool fromTemplate = true)
    {
        var name = await Infrastructure.CreateDatabaseAsync(fromTemplate);
        _ownedDatabases.Add(name);
        return name;
    }

    /// <summary>DbContext đọc trực tiếp database của test (mặc định) hoặc một database khác.</summary>
    protected TadssDbContext CreateDbContext(string? connectionString = null)
    {
        var options = new DbContextOptionsBuilder<TadssDbContext>()
            .UseNpgsql(connectionString ?? ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "tadss"))
            .Options;
        return new TadssDbContext(options);
    }

    public virtual async Task InitializeAsync()
    {
        // Container Redis dùng chung cả lượt chạy: dọn trước mỗi test để CACHE_HIT không đến từ test khác
        await Infrastructure.FlushRedisAsync();
        DatabaseName = await CreateAdditionalDatabaseAsync();
        ConnectionString = Infrastructure.BuildConnectionString(DatabaseName);
        _factory = new TadssWebApplicationFactory(ConnectionString, Infrastructure.RedisConnectionString);
    }

    public virtual async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            // WebApplicationFactory tự giải phóng các factory dẫn xuất từ WithWebHostBuilder
            await _factory.DisposeAsync();
        }

        // Xóa sau khi app đã dừng để không còn kết nối mới tới database
        foreach (var database in _ownedDatabases)
        {
            await Infrastructure.DropDatabaseAsync(database);
        }
    }
}
