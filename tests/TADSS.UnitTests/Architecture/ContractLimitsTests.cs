using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.Application.Common;
using TADSS.Application.Decisions;
using TADSS.Runtime.Api.Common;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// UT-08: giới hạn 128 ký tự của contract nằm ở một nơi (<see cref="ContractLimits"/>) và controller, service, middleware cùng tuân theo —
/// 128 được chấp nhận, 129 bị chặn (<c>X-Idempotency-Key</c> → 400) hoặc bị thay bằng GUID (correlation id), đúng hành vi trước khi gom hằng.
/// </summary>
public class ContractLimitsTests : IClassFixture<DecisionsApiFactory>
{
    private const string DecisionsPath = "/api/v2/decisions";

    private readonly DecisionsApiFactory _factory;
    private readonly HttpClient _client;

    public ContractLimitsTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> PostAsync(string idempotencyKey, string correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DecisionsPath)
        {
            Content = new StringContent(ContractSamples.RequestJson, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, idempotencyKey);
        request.Headers.TryAddWithoutValidation(HttpHeaderNames.CorrelationId, correlationId);
        return await _client.SendAsync(request);
    }

    /// <summary>Giá trị của contract: cả hai giới hạn là 128.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void ContractLimits_Values_Are128()
    {
        ContractLimits.MaxIdempotencyKeyLength.Should().Be(128);
        ContractLimits.MaxCorrelationIdLength.Should().Be(128);
    }

    /// <summary>Controller và middleware cùng theo <see cref="ContractLimits"/>: 128 ký tự qua, 129 ký tự bị chặn/thay thế.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task ContractLimits_MatchControllerServiceAndMiddleware()
    {
        var atLimitKey = new string('k', ContractLimits.MaxIdempotencyKeyLength);
        var atLimitCorrelation = new string('c', ContractLimits.MaxCorrelationIdLength);

        using var accepted = await PostAsync(atLimitKey, atLimitCorrelation);
        using var longKey = await PostAsync(atLimitKey + "k", atLimitCorrelation);
        using var longCorrelation = await PostAsync(atLimitKey, atLimitCorrelation + "c");

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        accepted.Headers.GetValues(HttpHeaderNames.CorrelationId).Should().ContainSingle().Which.Should().Be(atLimitCorrelation);

        longKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        longCorrelation.StatusCode.Should().Be(HttpStatusCode.OK);
        var replaced = longCorrelation.Headers.GetValues(HttpHeaderNames.CorrelationId).Should().ContainSingle().Subject;
        Guid.TryParse(replaced, out _).Should().BeTrue("correlation id dài hơn giới hạn bị thay bằng GUID mới");
        replaced.Should().NotBe(atLimitCorrelation + "c");

        _factory.DecisionService.Calls.Select(call => call.IdempotencyKey.Length)
            .Should().OnlyContain(length => length == ContractLimits.MaxIdempotencyKeyLength);
    }

    /// <summary>Service cũng từ chối key dài hơn giới hạn (lớp phòng thủ thứ hai sau controller).</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task ContractLimits_ServiceRejectsIdempotencyKeyOverLimit()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([TestConnectionStrings.Pair]);
        builder.Services.AddLogging();
        builder.Services.AddRuntimeCore(builder.Configuration);
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDecisionService>();
        var request = ContractSamples.DeserializeRequest(ContractSamples.RequestJson);

        var act = async () => await service.EvaluateAsync(
            request, new string('k', ContractLimits.MaxIdempotencyKeyLength + 1), null, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("idempotencyKey");
    }
}
