using FluentAssertions;
using Xunit;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Cổng chất lượng G3: integration test chỉ chạy trên hạ tầng thật — provider EF Core InMemory
/// không được có mặt trong output của project integration test.
/// </summary>
public class InfrastructureGuardTests
{
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void IntegrationTestOutput_DoesNotContainEfCoreInMemoryProvider()
    {
        var inMemoryProvider = Type.GetType(
            "Microsoft.EntityFrameworkCore.InMemoryDbContextOptionsExtensions, Microsoft.EntityFrameworkCore.InMemory",
            throwOnError: false);

        inMemoryProvider.Should().BeNull("integration test phải chạy trên PostgreSQL thật qua Testcontainers");
    }
}
