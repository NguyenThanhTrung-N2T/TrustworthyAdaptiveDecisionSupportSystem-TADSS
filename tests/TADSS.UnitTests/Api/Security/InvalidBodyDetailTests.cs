using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Security;

/// <summary>
/// SEC-3 (F-23): 400 do lỗi giải mã body không bao giờ lộ chi tiết nội bộ của framework (tên kiểu .NET, <c>LineNumber</c>, <c>BytePositionInLine</c>)
/// trong <c>detail</c> hay <c>errors</c>; trường vẫn được chỉ ra bằng đường dẫn JSON của client.
/// </summary>
public class InvalidBodyDetailTests : IClassFixture<DecisionsApiFactory>
{
    private static readonly string[] ForbiddenFragments =
        ["Shared.DecisionContractSDK", "System.", "LineNumber", "BytePositionInLine", "Path:"];

    private readonly DecisionsApiFactory _factory;

    public InvalidBodyDetailTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
    }

    private static string Mutated(Action<JsonObject> mutate) => ContractSamples.MutateRequest(mutate);

    public static TheoryData<string, string, string?> Cases => new()
    {
        { "unknown-field", Mutated(root => root["bogusField"] = 1), "bogusField" },
        { "wrong-type", Mutated(root => root["totalPrice"] = "không-phải-số"), "totalPrice" },
        { "numeric-enum", Mutated(root => ContractSamples.Object(root["customer"])["tier"] = 1), "customer.tier" },
        { "broken-json-with-path", "{\"reservationId\": ", "reservationId" },
        { "broken-json-no-path", "{", null },
        { "empty-body", string.Empty, null },
    };

    /// <summary>
    /// Trường lạ, sai kiểu, enum số, JSON hỏng, body rỗng: 400 <c>VALIDATION_ERROR</c>; <c>detail</c> và <c>errors</c> không chứa chuỗi nội bộ của framework;
    /// khi xác định được trường thì <c>detail</c> nêu trường và <c>errors</c> có khóa đó.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "SEC-3")]
    [MemberData(nameof(Cases))]
    public async Task InvalidBody_DetailHasNoFrameworkInternals_Theory(string caseName, string body, string? expectedField)
    {
        using var client = _factory.CreateClient();
        using var request = SecurityTestHost.Post(body);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, caseName);
        var text = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(text);
        problem.RootElement.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR", caseName);
        var detail = problem.RootElement.GetProperty("detail").GetString() ?? string.Empty;
        var errors = problem.RootElement.TryGetProperty("errors", out var errorsElement) ? errorsElement.GetRawText() : string.Empty;
        foreach (var fragment in ForbiddenFragments)
        {
            detail.Should().NotContain(fragment, caseName);
            errors.Should().NotContain(fragment, caseName);
        }

        if (expectedField is null)
        {
            detail.Should().Contain("Request body không phải JSON hợp lệ theo contract.", caseName);
        }
        else
        {
            detail.Should().Contain($"Giá trị của trường '{expectedField}' không hợp lệ.", caseName);
            problem.RootElement.GetProperty("errors").TryGetProperty(expectedField, out _).Should().BeTrue(caseName);
        }

        _factory.DecisionService.Calls.Should().BeEmpty(caseName);
    }

    /// <summary>Tên trường rất dài do client gửi bị cắt, nên <c>detail</c> không phình theo kích thước body.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-3")]
    public async Task InvalidBody_VeryLongUnknownFieldName_IsTruncated()
    {
        using var client = _factory.CreateClient();
        var longName = new string('x', 5000);
        using var request = SecurityTestHost.Post(Mutated(root => root[longName] = 1));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var detail = problem.RootElement.GetProperty("detail").GetString() ?? string.Empty;
        detail.Should().NotContain(longName);
        Encoding.UTF8.GetByteCount(detail).Should().BeLessThan(1000);
    }
}
