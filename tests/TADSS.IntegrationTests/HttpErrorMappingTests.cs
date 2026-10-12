using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.Runtime.Api.Common;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.IntegrationTests;

/// <summary>
/// UT-02/UT-08 ở mức HTTP thật: lỗi request (header, body, bất biến domain) ra đúng mã và dạng problem+json, không để lại bản ghi audit và
/// không chạy strategy; correlation id quá dài bị thay bằng GUID và ghi vào audit trong giới hạn cột. Request lỗi thì không có response 200 nào
/// cần kiểm tra schema; các ca 200 trong lớp này đi qua <see cref="ResponseSchemaAssert"/> như mọi ca khác (CT-01).
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class HttpErrorMappingTests : DecisionApiTestBase
{
    private const string ProblemContentType = "application/problem+json";

    public HttpErrorMappingTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>Tên mọi ca âm của schema request (dùng chung với UnitTests qua <c>Compile Link</c>).</summary>
    public static TheoryData<string> InvalidBodyCases
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var row in RequestSchemaNegativeCases.All)
            {
                data.Add((string)row[0]);
            }

            return data;
        }
    }

    /// <summary>Không có header <c>X-Idempotency-Key</c>: 400 <c>MISSING_HEADER</c>, không bản ghi, strategy không chạy.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task PostDecision_MissingIdempotencyKey_Returns400MissingHeader()
    {
        var response = await PostDecisionAsync(DecisionRequests.Valid(), idempotencyKey: null);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.ContentType.Should().Be(ProblemContentType);
        response.Problem.GetProperty("code").GetString().Should().Be("MISSING_HEADER");
        (await ReadExecutionsAsync()).Should().BeEmpty();
        Strategies.InvocationCount.Should().Be(0);
    }

    /// <summary>Biên 128/129 ký tự của key: 129 là 400 <c>VALIDATION_ERROR</c>, 128 thành công và ghi một hàng.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task PostDecision_KeyOver128Chars_Returns400_While128Succeeds()
    {
        var tooLong = await PostDecisionAsync(DecisionRequests.Valid(), new string('k', 129));
        var atLimit = await PostDecisionAsync(DecisionRequests.Valid(), new string('k', 128));

        tooLong.Status.Should().Be(HttpStatusCode.BadRequest);
        tooLong.Problem.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        atLimit.Status.Should().Be(HttpStatusCode.OK);
        (await ReadExecutionsAsync()).Should().ContainSingle().Which.IdempotencyKey.Should().HaveLength(128);
    }

    /// <summary>Mỗi ca âm của schema request: 400 <c>VALIDATION_ERROR</c>, không bản ghi, strategy không chạy.</summary>
    [Theory]
    [MemberData(nameof(InvalidBodyCases))]
    [Trait("CatalogId", "UT-02")]
    public async Task PostDecision_InvalidBody_Returns400ValidationError_Theory(string caseName)
    {
        var response = await PostDecisionAsync(RequestSchemaNegativeCases.Json(caseName), NewKey());

        response.Status.Should().Be(HttpStatusCode.BadRequest, caseName);
        response.ContentType.Should().Be(ProblemContentType);
        response.Problem.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR", caseName);
        (await ReadExecutionsAsync()).Should().BeEmpty(caseName);
        Strategies.InvocationCount.Should().Be(0, caseName);
    }

    /// <summary>
    /// Body đúng schema nhưng vi phạm bất biến domain — item 15 phút (P-01) hoặc <c>endTime</c> không sau <c>startTime</c>: 400
    /// <c>DOMAIN_INVARIANT_VIOLATION</c>, không bản ghi, strategy không chạy.
    /// </summary>
    [Theory]
    [InlineData("item-15-minutes")]
    [InlineData("end-equals-start")]
    [Trait("CatalogId", "UT-08")]
    public async Task PostDecision_DomainInvariantViolation_Returns400DomainInvariant(string caseName)
    {
        var body = DecisionRequests.Mutated(root =>
        {
            var item = ContractSamples.Object(root["items"]?[0]);
            item["endTime"] = caseName == "item-15-minutes" ? "2026-11-20T18:15:00+07:00" : "2026-11-20T18:00:00+07:00";
        });

        var response = await PostDecisionAsync(body, NewKey());

        response.Status.Should().Be(HttpStatusCode.BadRequest, caseName);
        response.ContentType.Should().Be(ProblemContentType);
        response.Problem.GetProperty("code").GetString().Should().Be("DOMAIN_INVARIANT_VIOLATION", caseName);
        (await ReadExecutionsAsync()).Should().BeEmpty(caseName);
        Strategies.InvocationCount.Should().Be(0, caseName);
    }

    /// <summary>
    /// Body lớn hơn trần 64 KB (Content-Length 65537 byte, JSON hợp lệ đệm khoảng trắng): 413 <c>PAYLOAD_TOO_LARGE</c> trước khi đọc body, không bản ghi
    /// audit, strategy không chạy (SEC-2 trên hạ tầng thật).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "SEC-2")]
    public async Task PostDecision_BodyOverLimit_Returns413_NoRecordNoStrategy()
    {
        var body = DecisionRequests.Valid();
        var bytes = Encoding.UTF8.GetBytes(body.PadRight(65537 - (Encoding.UTF8.GetByteCount(body) - body.Length)));
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentLength = bytes.Length;
        using var request = new HttpRequestMessage(HttpMethod.Post, DecisionsPath) { Content = content };
        request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, NewKey());

        using var response = await Api.SendAsync(request);

        bytes.Length.Should().Be(65537);
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType?.MediaType.Should().Be(ProblemContentType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("PAYLOAD_TOO_LARGE");
        (await ReadExecutionsAsync()).Should().BeEmpty();
        Strategies.InvocationCount.Should().Be(0);
    }

    /// <summary>
    /// <c>X-Correlation-ID</c> dài 200 ký tự: không 500; middleware thay bằng GUID mới, header response và cột <c>correlation_id</c> đều là GUID đó
    /// (không vượt giới hạn 128 ký tự của cột).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task PostDecision_CorrelationIdOver128_ReplacedAndPersistedWithinColumnLimit()
    {
        var oversized = new string('c', 200);

        var response = await PostDecisionAsync(DecisionRequests.Valid(), NewKey(), oversized);

        response.Status.Should().Be(HttpStatusCode.OK);
        var echoed = response.Header(HttpHeaderNames.CorrelationId);
        echoed.Should().NotBe(oversized);
        Guid.TryParse(echoed, out _).Should().BeTrue("correlation id mới là GUID");
        var row = (await ReadExecutionsAsync()).Should().ContainSingle().Subject;
        row.CorrelationId.Should().Be(echoed);
        row.CorrelationId.Should().HaveLength(36);
    }
}
