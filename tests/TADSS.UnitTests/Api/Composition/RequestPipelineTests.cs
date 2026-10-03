using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using TADSS.Runtime.Api.Common;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-08 / UT-14: thứ tự middleware của <c>UseTadssRequestPipeline</c> (E-12) được khóa bằng hành vi quan sát được, không đọc
/// reflection: correlation id có trên mọi response lỗi (exception handler bọc controller, correlation id bọc exception handler)
/// và Swagger chỉ mở ở môi trường Development.
/// </summary>
public class RequestPipelineTests : IClassFixture<DecisionsApiFactory>
{
    private const string DevelopmentSettingsFile = "appsettings.Development.json";
    private const string SwaggerPath = "/swagger/v2/swagger.json";

    private readonly DecisionsApiFactory _factory;

    public RequestPipelineTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
    }

    /// <summary>Controller ném lỗi: response lỗi vẫn mang <c>X-Correlation-ID</c> và <c>traceId</c> của problem+json bằng đúng correlation id đó.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task RequestPipeline_ErrorResponseCarriesCorrelationId()
    {
        using var client = _factory.CreateClient();
        _factory.DecisionService.Behavior = _ => throw new InvalidOperationException("SECRET-pipeline-boom");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/decisions")
        {
            Content = new StringContent(ContractSamples.RequestJson, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, "booking-pipeline-1");
        request.Headers.TryAddWithoutValidation(HttpHeaderNames.CorrelationId, "corr-pipeline-1");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.GetValues(HttpHeaderNames.CorrelationId).Should().ContainSingle().Which.Should().Be("corr-pipeline-1");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("SECRET-pipeline-boom");
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("traceId").GetString().Should().Be("corr-pipeline-1");
    }

    /// <summary>
    /// Swagger chỉ có ở Development: <c>Testing</c> trả 404, <c>Development</c> trả 200 tại <c>/swagger/v2/swagger.json</c>. Host Development
    /// bỏ nguồn <c>appsettings.Development.json</c> nên test không phụ thuộc cấu hình cục bộ của người chạy.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [InlineData("Testing", HttpStatusCode.NotFound)]
    [InlineData("Development", HttpStatusCode.OK)]
    public async Task RequestPipeline_SwaggerOnlyInDevelopment_Theory(string environment, HttpStatusCode expected)
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var developmentSources = configuration.Sources
                    .OfType<JsonConfigurationSource>()
                    .Where(source => source.Path == DevelopmentSettingsFile)
                    .ToList();
                foreach (var source in developmentSources)
                {
                    configuration.Sources.Remove(source);
                }
            });
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(SwaggerPath);

        response.StatusCode.Should().Be(expected);
        var root = factory.Services.GetRequiredService<IConfiguration>() as IConfigurationRoot
            ?? throw new InvalidOperationException("IConfiguration của host không phải IConfigurationRoot.");
        root.Providers.OfType<JsonConfigurationProvider>()
            .Should().NotContain(provider => provider.Source.Path == DevelopmentSettingsFile, "test không được đọc cấu hình Development cục bộ");
    }
}
