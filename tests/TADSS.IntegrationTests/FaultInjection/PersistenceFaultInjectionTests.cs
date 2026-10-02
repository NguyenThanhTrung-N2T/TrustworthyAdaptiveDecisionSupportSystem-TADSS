using System.Net;
using FluentAssertions;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests.FaultInjection;

/// <summary>
/// FI-08 (F6 trong RESILIENCE_AND_FAULT_TAXONOMY) ở mức HTTP thật: mất kết nối PostgreSQL (Toxiproxy tắt proxy) ⇒ 503 <c>PERSISTENCE_UNAVAILABLE</c> kèm
/// <c>Retry-After: 5</c>, không bản ghi dở dang và client không nhận quyết định (INV-10, fail-closed). Host đi tới PostgreSQL qua Toxiproxy; số hàng
/// đọc lại bằng một <c>DbContext</c> riêng đi thẳng tới database (không qua proxy). Proxy luôn được bật lại trong <c>DisposeAsync</c> của lớp cơ sở.
/// F6 cũ (<c>DbUpdateException</c> → 500) không chuyển: lỗi non-transient của store → 500 được phủ bởi test store (Task 6) và bộ phân loại (UT-08).
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class PersistenceFaultInjectionTests : FaultInjectionTestBase
{
    private const string ProblemContentType = "application/problem+json";

    public PersistenceFaultInjectionTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
        ProxyPostgres = true;
    }

    /// <summary>
    /// Kết nối PostgreSQL bị ngắt <b>trước</b> khi xử lý: 503 <c>PERSISTENCE_UNAVAILABLE</c>, <c>Retry-After: 5</c>, 0 hàng. Bước tra idempotency (đứng
    /// trước strategy) là nơi phát hiện lỗi nên strategy chưa được gọi. Sau khi bật lại proxy, cùng request thành công và ghi đúng một hàng (hệ thống tự hồi phục).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-08")]
    public async Task PostDecision_PostgresConnectionLost_Returns503WithRetryAfterAndNoRecord()
    {
        var key = NewKey();
        var body = UpcomingRequest();
        await Infrastructure.Toxiproxy.SetEnabledAsync(TadssInfrastructureFixture.PostgresProxy, enabled: false);

        var response = await PostDecisionAsync(body, key);

        response.Status.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.ContentType.Should().Be(ProblemContentType);
        response.Problem.GetProperty("code").GetString().Should().Be("PERSISTENCE_UNAVAILABLE");
        response.Header("Retry-After").Should().Be("5");
        (await ReadExecutionsAsync()).Should().BeEmpty("không để lại bản ghi dở dang");
        Strategy.InvocationCount.Should().Be(0, "tra idempotency thất bại trước khi tới strategy");

        await Infrastructure.Toxiproxy.ResetAsync();
        var recovered = await PostDecisionAsync(body, key);

        recovered.Status.Should().Be(HttpStatusCode.OK, "hết lỗi mạng thì cùng request thành công");
        (await ReadExecutionsAsync()).Should().ContainSingle();
    }

    /// <summary>
    /// Biến thể: kết nối bị cắt <b>đúng lúc ghi audit</b> — decorator của <c>IDecisionAuditStore</c> tắt proxy ngay trước <c>InsertExecutionAsync</c>, tức là sau khi
    /// strategy đã chạy xong và tra idempotency đã thành công. Kỳ vọng: 503 <c>PERSISTENCE_UNAVAILABLE</c> + <c>Retry-After: 5</c>, 0 hàng, strategy đã chạy đúng 1 lần,
    /// đã thử ghi đúng 1 lần và client không nhận bất kỳ quyết định nào (INV-10 trên đường ghi).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-08")]
    public async Task PostDecision_PostgresCutDuringAuditInsert_Returns503AndNoRecord()
    {
        AuditProbe.BeforeInsert = _ =>
            Infrastructure.Toxiproxy.SetEnabledAsync(TadssInfrastructureFixture.PostgresProxy, enabled: false);

        var response = await PostDecisionAsync(UpcomingRequest(), NewKey());

        response.Status.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.ContentType.Should().Be(ProblemContentType);
        response.Problem.GetProperty("code").GetString().Should().Be("PERSISTENCE_UNAVAILABLE");
        response.Header("Retry-After").Should().Be("5");
        response.Problem.TryGetProperty("decisionId", out _).Should().BeFalse("client không được nhận quyết định nào");
        response.Body.Should().NotContain("FI_RULE_APPROVED", "kết quả của strategy không được lộ ra khi chưa ghi được audit");
        Strategy.InvocationCount.Should().Be(1, "strategy đã chạy xong trước khi ghi audit");
        AuditProbe.InsertAttempts.Should().Be(1);
        (await ReadExecutionsAsync()).Should().BeEmpty("ghi audit thất bại thì không có hàng nào");
    }
}
