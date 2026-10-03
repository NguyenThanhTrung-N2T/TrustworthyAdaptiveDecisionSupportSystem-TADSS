using System.Net;
using System.Text.Json;
using FluentAssertions;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.IntegrationTests.Security;

/// <summary>
/// IT-11: xác thực <c>X-Api-Key</c> trên host thật (PostgreSQL/Redis Testcontainers): thiếu hoặc sai khóa → 401 problem+json
/// <c>UNAUTHORIZED</c> kèm <c>WWW-Authenticate</c>; có khóa → request đi tiếp tới database. Client mặc định của factory đã gửi khóa nên test
/// bỏ header một cách tường minh.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class ApiKeyIntegrationTests : IntegrationTestBase
{
    private static readonly string PendingPath = $"/api/v2/decisions/pending-approvals?branchId={Guid.NewGuid()}";

    public ApiKeyIntegrationTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>Không header và khóa sai đều 401 đúng dạng; khóa đúng thì 200 (hàng chờ rỗng của chi nhánh lạ).</summary>
    [Fact]
    [Trait("CatalogId", "IT-11")]
    public async Task ApiKeyIntegration_MissingAndInvalidKey_Return401()
    {
        using var client = TestApiKeys.CreateClientWithoutKey(Factory);

        using var missing = await client.GetAsync(PendingPath);
        using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, PendingPath);
        invalidRequest.Headers.TryAddWithoutValidation(TestApiKeys.HeaderName, "wrong-api-key-value");
        using var invalid = await client.SendAsync(invalidRequest);
        using var valid = await Client.GetAsync(PendingPath);

        foreach (var rejected in new[] { missing, invalid })
        {
            rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            rejected.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
            rejected.Headers.WwwAuthenticate.ToString().Should().Be("ApiKey header=\"X-Api-Key\"");
            using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            problem.RootElement.GetProperty("code").GetString().Should().Be("UNAUTHORIZED");
            problem.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        }

        valid.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
