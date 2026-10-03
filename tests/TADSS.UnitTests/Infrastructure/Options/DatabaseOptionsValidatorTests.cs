using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TADSS.Infrastructure.Options;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Options;

/// <summary>
/// SEC-4: thiếu hoặc sai chuỗi kết nối PostgreSQL thì dừng ngay lúc khởi động ở mọi môi trường, và message lỗi không lộ giá trị chuỗi.
/// </summary>
public class DatabaseOptionsValidatorTests
{
    private const string PlaceholderConnectionString = "your-connection-string";

    private static readonly DatabaseOptionsValidator Validator = new();

    /// <summary><c>null</c>, rỗng, ô giữ chỗ và chuỗi thiếu Host bị từ chối (message không chứa giá trị); chuỗi test hợp lệ đạt.</summary>
    [Theory]
    [Trait("CatalogId", "SEC-4")]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(PlaceholderConnectionString, false)]
    [InlineData("Port=5432", false)]
    [InlineData(TestConnectionStrings.Value, true)]
    public void Validate_ConnectionString_Theory(string? connectionString, bool expectedValid)
    {
        var result = Validator.Validate(null, new DatabaseOptions { ConnectionString = connectionString });

        result.Succeeded.Should().Be(expectedValid);
        if (!expectedValid)
        {
            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("ConnectionStrings:DefaultConnection");
            if (!string.IsNullOrEmpty(connectionString))
            {
                result.FailureMessage.Should().NotContain(connectionString);
            }
        }
    }

    /// <summary>Host ở môi trường <c>Testing</c> với chuỗi giữ chỗ không khởi động được: <see cref="OptionsValidationException"/>, message không chứa chuỗi.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-4")]
    public void Host_PlaceholderConnectionString_FailsOnStart()
    {
        using var factory = new DecisionsApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection([new KeyValuePair<string, string?>(TestConnectionStrings.Key, PlaceholderConnectionString)])));

        var start = () => factory.CreateClient();

        var failure = start.Should().Throw<OptionsValidationException>().Which;
        failure.Message.Should().Contain("ConnectionStrings:DefaultConnection").And.NotContain(PlaceholderConnectionString);
    }
}
