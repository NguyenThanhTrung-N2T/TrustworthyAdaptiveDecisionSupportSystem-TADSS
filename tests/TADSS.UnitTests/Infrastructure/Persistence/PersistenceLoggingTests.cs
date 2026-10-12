using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Persistence;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Persistence;

/// <summary>
/// UT-08: <c>AddPersistenceDbContext</c> hạ log EF của lỗi mong đợi (tranh chấp idempotency 23505) xuống Debug (A-13),
/// để mỗi lần thua tranh chấp không sinh hai dòng Error nhiễu; lỗi thật vẫn được ghi ở các tầng phía trên.
/// </summary>
public class PersistenceLoggingTests
{
    private static DbContextOptions<TadssDbContext> BuildOptions()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new DatabaseOptions { ConnectionString = TestConnectionStrings.Value }));
        services.AddPersistenceDbContext();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<DbContextOptions<TadssDbContext>>();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void AddPersistenceDbContext_CommandErrorAndSaveChangesFailed_LoggedAtDebug()
    {
        var warnings = BuildOptions().GetExtension<CoreOptionsExtension>().WarningsConfiguration;

        warnings.GetLevel(RelationalEventId.CommandError).Should().Be(LogLevel.Debug);
        warnings.GetLevel(CoreEventId.SaveChangesFailed).Should().Be(LogLevel.Debug);
    }
}
