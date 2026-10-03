using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.Infrastructure.Persistence;
using Xunit;

namespace TADSS.IntegrationTests.Migrations;

/// <summary>
/// IT-15: migration EF Core áp dụng được trên PostgreSQL trống và model snapshot khớp model hiện tại
/// (không có thay đổi model nào chưa được tạo migration); migration v2 bỏ bảng v1 và tạo ba bảng audit,
/// <c>Down</c> khôi phục đúng schema v1 và <c>Up</c> lại cho ra đúng schema v2; cờ
/// <c>Tadss:Database:ApplyMigrationsOnStartup</c> chỉ tự áp migration khi bật và ghi log Warning.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class MigrationIntegrationTests : IntegrationTestBase
{
    private static readonly string[] AuditTables = ["decision_executions", "decision_overrides", "decision_outcomes"];

    public MigrationIntegrationTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    [Fact]
    [Trait("CatalogId", "IT-15")]
    public async Task Migrate_EmptyDatabase_AppliesAllMigrationsWithoutPendingModelChanges()
    {
        // Database của test được nhân bản từ template mà fixture đã migrate trên DB trống
        await using var db = CreateDbContext();

        var applied = await db.Database.GetAppliedMigrationsAsync();

        applied.Should().Equal(db.Database.GetMigrations());
        HasPendingModelChanges(db).Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "IT-15")]
    public async Task Startup_ApplyMigrationsOnStartupEnabled_LogsWarningAndMigrates()
    {
        var emptyDatabase = await CreateAdditionalDatabaseAsync(fromTemplate: false);
        var connectionString = Infrastructure.BuildConnectionString(emptyDatabase);
        var logs = new CapturingLoggerProvider();
        await using var factory = new TadssWebApplicationFactory(connectionString, Infrastructure.RedisConnectionString);
        var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tadss:Database:ApplyMigrationsOnStartup", "true");
            // Serilog thay ILoggerFactory của host; thay lại bằng factory thường để bắt log của Program
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILoggerFactory>();
                services.AddSingleton<ILoggerFactory>(_ => LoggerFactory.Create(logging => logging.AddProvider(logs)));
            });
        });

        _ = configured.Server; // khởi động host

        logs.Snapshot().Should().Contain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("ApplyMigrationsOnStartup")
            && entry.Message.Contains("E-LOCAL"));

        await using var db = CreateDbContext(connectionString);
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations());
        foreach (var table in AuditTables)
        {
            (await TableExistsAsync(connectionString, table)).Should().BeTrue($"bảng {table} phải có sau khi tự áp migration");
        }
    }

    [Fact]
    [Trait("CatalogId", "IT-15")]
    public async Task Startup_ApplyMigrationsOnStartupDefault_DoesNotTouchDatabase()
    {
        var emptyDatabase = await CreateAdditionalDatabaseAsync(fromTemplate: false);
        var connectionString = Infrastructure.BuildConnectionString(emptyDatabase);
        await using var factory = new TadssWebApplicationFactory(connectionString, Infrastructure.RedisConnectionString);

        _ = factory.Server; // khởi động host với cấu hình mặc định

        await using var db = CreateDbContext(connectionString);
        (await db.Database.GetAppliedMigrationsAsync()).Should().BeEmpty();
        foreach (var table in AuditTables)
        {
            (await TableExistsAsync(connectionString, table)).Should().BeFalse($"database trống không được có bảng {table}");
        }
    }

    [Fact]
    [Trait("CatalogId", "IT-15")]
    public async Task Migrate_FromV1Schema_DropsLegacyTableAndCreatesV2()
    {
        var database = await CreateAdditionalDatabaseAsync(fromTemplate: false);
        var connectionString = Infrastructure.BuildConnectionString(database);
        await using var db = CreateDbContext(connectionString);
        var migrations = db.Database.GetMigrations().ToList();
        var migrator = db.GetService<IMigrator>();

        // Áp ba migration cũ → schema v1 với một dòng dữ liệu kiểm thử
        await migrator.MigrateAsync(migrations[^2]);
        (await ColumnExistsAsync(connectionString, "decision_executions", "need_approval")).Should().BeTrue("v1 có cột need_approval");
        (await TableExistsAsync(connectionString, "decision_overrides")).Should().BeFalse();
        await AuditSchemaSql.ExecuteAsync(connectionString, """
            INSERT INTO tadss.decision_executions
                (decision_id, reservation_id, strategy_name, execution_type, execution_time_ms, request_hash,
                 response_payload, created_at, updated_at)
            VALUES (gen_random_uuid(), gen_random_uuid(), 'DefaultDecisionStrategy', 'AUTOMATED', 5, 'hash', '{}', now(), now())
            """);

        await migrator.MigrateAsync(migrations[^1]);

        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(migrations);
        foreach (var legacyColumn in new[] { "need_approval", "is_overridden", "updated_at", "version" })
        {
            (await ColumnExistsAsync(connectionString, "decision_executions", legacyColumn)).Should().BeFalse($"cột v1 {legacyColumn} đã bị bỏ");
        }

        foreach (var table in AuditTables)
        {
            (await TableExistsAsync(connectionString, table)).Should().BeTrue($"bảng {table} phải có ở v2");
        }

        (await ColumnExistsAsync(connectionString, "decision_executions", "branch_id")).Should().BeTrue();
        (await AuditSchemaSql.ScalarAsync<int>(connectionString, $"SELECT count(*) FROM {AuditSchemaSql.Executions}"))
            .Should().Be(0, "dữ liệu v1 chỉ là dữ liệu kiểm thử và bị bỏ cùng bảng");
    }

    [Fact]
    [Trait("CatalogId", "IT-15")]
    public async Task Migrate_DownThenUp_RoundTrips()
    {
        // Mốc v1: database trống chỉ áp tới migration trước migration v2
        var legacyDatabase = await CreateAdditionalDatabaseAsync(fromTemplate: false);
        var legacyConnection = Infrastructure.BuildConnectionString(legacyDatabase);
        await using (var legacyDb = CreateDbContext(legacyConnection))
        {
            var previous = legacyDb.Database.GetMigrations().ToList()[^2];
            await legacyDb.GetService<IMigrator>().MigrateAsync(previous);
        }

        var expectedV1 = await AuditSchemaSql.SchemaFingerprintAsync(legacyConnection);
        var expectedV2 = await AuditSchemaSql.SchemaFingerprintAsync(ConnectionString);
        expectedV2.Should().NotBeEquivalentTo(expectedV1);

        await using var db = CreateDbContext();
        var migrations = db.Database.GetMigrations().ToList();
        var migrator = db.GetService<IMigrator>();

        // Down: về đúng schema v1
        await migrator.MigrateAsync(migrations[^2]);
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(migrations.Take(migrations.Count - 1));
        (await AuditSchemaSql.SchemaFingerprintAsync(ConnectionString)).Should().Equal(expectedV1);
        (await FunctionExistsAsync(ConnectionString)).Should().BeFalse("Down phải gỡ hàm reject_mutation");

        // Up lại: về đúng schema v2
        await migrator.MigrateAsync(migrations[^1]);
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(migrations);
        (await AuditSchemaSql.SchemaFingerprintAsync(ConnectionString)).Should().Equal(expectedV2);
        HasPendingModelChanges(db).Should().BeFalse();
    }

    private static Task<bool> TableExistsAsync(string connectionString, string table) =>
        AuditSchemaSql.ScalarAsync<bool>(connectionString,
            $"SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'tadss' AND table_name = '{table}')");

    private static Task<bool> ColumnExistsAsync(string connectionString, string table, string column) =>
        AuditSchemaSql.ScalarAsync<bool>(connectionString,
            $"SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'tadss' AND table_name = '{table}' AND column_name = '{column}')");

    private static Task<bool> FunctionExistsAsync(string connectionString) =>
        AuditSchemaSql.ScalarAsync<bool>(connectionString,
            "SELECT EXISTS (SELECT 1 FROM pg_proc WHERE pronamespace = 'tadss'::regnamespace AND proname = 'reject_mutation')");

    private static bool HasPendingModelChanges(TadssDbContext db)
    {
        var snapshotModel = db.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        if (snapshotModel is IMutableModel mutableModel)
        {
            snapshotModel = mutableModel.FinalizeModel();
        }

        if (snapshotModel is not null)
        {
            snapshotModel = db.GetService<IModelRuntimeInitializer>().Initialize(snapshotModel);
        }

        var designTimeModel = db.GetService<IDesignTimeModel>().Model;
        return db.GetService<IMigrationsModelDiffer>()
            .HasDifferences(snapshotModel?.GetRelationalModel(), designTimeModel.GetRelationalModel());
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    /// <summary>Logger provider ghi lại mọi dòng log (an toàn đa luồng) để test kiểm tra log của host.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Snapshot()
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (entries)
                {
                    entries.Add(new LogEntry(logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
