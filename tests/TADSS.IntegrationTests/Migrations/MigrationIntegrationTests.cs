using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.Runtime.Api.Infrastructure.Data;
using Xunit;

namespace TADSS.IntegrationTests.Migrations;

/// <summary>
/// IT-15: migration EF Core áp dụng được trên PostgreSQL trống và model snapshot khớp model hiện tại
/// (không có thay đổi model nào chưa được tạo migration).
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class MigrationIntegrationTests : IntegrationTestBase
{
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
    public async Task Startup_ApplyMigrationsOnStartupEnabled_MigratesEmptyDatabase()
    {
        var emptyDatabase = await CreateAdditionalDatabaseAsync(fromTemplate: false);
        var connectionString = Infrastructure.BuildConnectionString(emptyDatabase);
        await using var factory = new TadssWebApplicationFactory(connectionString, Infrastructure.RedisConnectionString);
        var configured = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Tadss:Database:ApplyMigrationsOnStartup", "true"));

        _ = configured.Server; // khởi động host

        await using var db = CreateDbContext(connectionString);
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations());
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
    }

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
}
