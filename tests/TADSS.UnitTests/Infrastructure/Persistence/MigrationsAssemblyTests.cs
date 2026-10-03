using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TADSS.Infrastructure;
using TADSS.Infrastructure.Persistence;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Migration nằm trong assembly <c>TADSS.Infrastructure</c> và giữ nguyên bốn ID đã áp lên các database có sẵn
/// (đổi ID hoặc assembly làm EF coi migration là mới và áp lại).
/// </summary>
public class MigrationsAssemblyTests
{
    private static readonly string[] ExpectedMigrationIds =
    [
        "20260912105336_InitialCreate",
        "20260915152502_UpdateUniqueIndexes",
        "20260930190021_AddCreatedAtIndex",
        "20261002101457_DecisionPersistenceV2",
    ];

    /// <summary>Đọc <see cref="IMigrationsAssembly"/> từ <see cref="TadssDbContext"/> (Npgsql, không kết nối): assembly là Infrastructure và tập ID không đổi.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void Migrations_AssemblyIsInfrastructure_AndIdsUnchanged()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=none",
            })
            .Build());
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TadssDbContext>();

        var migrationsAssembly = context.GetService<IMigrationsAssembly>();

        migrationsAssembly.Assembly.GetName().Name.Should().Be("TADSS.Infrastructure");
        migrationsAssembly.Migrations.Keys.Should().BeEquivalentTo(ExpectedMigrationIds);
    }
}
