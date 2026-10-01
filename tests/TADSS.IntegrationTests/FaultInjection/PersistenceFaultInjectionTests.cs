using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.Runtime.Api.Common;
using Xunit;

namespace TADSS.IntegrationTests.FaultInjection;

/// <summary>
/// FI-08: PostgreSQL mất kết nối (Toxiproxy tắt proxy) → TADSS fail-closed với 503 PERSISTENCE_UNAVAILABLE,
/// không trả quyết định và không để lại bản ghi audit dở dang.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class PersistenceFaultInjectionTests : IntegrationTestBase
{
    public PersistenceFaultInjectionTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    public override async Task DisposeAsync()
    {
        // Luôn khôi phục proxy để không ảnh hưởng test khác trong collection;
        // base.DisposeAsync luôn chạy kể cả khi ResetAsync ném lỗi
        try { await Infrastructure.Toxiproxy.ResetAsync(); }
        finally { await base.DisposeAsync(); }
    }

    [Fact]
    [Trait("CatalogId", "FI-08")]
    public async Task PostDecision_PostgresConnectionLost_Returns503PersistenceUnavailableWithoutAuditRecord()
    {
        // Arrange: app kết nối PostgreSQL qua Toxiproxy
        await using var proxiedFactory = new TadssWebApplicationFactory(
            Infrastructure.BuildProxiedConnectionString(DatabaseName),
            Infrastructure.RedisConnectionString);
        using var client = proxiedFactory.CreateClient();

        var healthyKey = "FI08-OK-" + Guid.NewGuid();
        using var healthyResponse = await PostDecisionAsync(client, healthyKey);
        healthyResponse.StatusCode.Should().Be(HttpStatusCode.OK, "đường đi qua Toxiproxy phải hoạt động trước khi tiêm lỗi");

        // Act: cắt kết nối PostgreSQL
        await Infrastructure.Toxiproxy.SetEnabledAsync(TadssInfrastructureFixture.PostgresProxy, enabled: false);
        var failedKey = "FI08-DOWN-" + Guid.NewGuid();
        using var failedResponse = await PostDecisionAsync(client, failedKey);

        // Assert: fail-closed
        failedResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        failedResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await failedResponse.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be(RuntimeErrorCodes.PersistenceUnavailable);

        // Khôi phục và kiểm tra không có bản ghi dở dang
        await Infrastructure.Toxiproxy.ResetAsync();
        await using var db = CreateDbContext();
        (await db.DecisionExecutions.CountAsync(e => e.IdempotencyKey == healthyKey)).Should().Be(1);
        (await db.DecisionExecutions.CountAsync(e => e.IdempotencyKey == failedKey)).Should().Be(0);
    }

    private static async Task<HttpResponseMessage> PostDecisionAsync(HttpClient client, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(new DecisionRequestDto
            {
                ReservationId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                CustomerTier = "VIP",
                CourtId = Guid.NewGuid(),
                BranchId = Guid.NewGuid(),
                CourtTypeId = null,
                StartTime = DateTime.UtcNow.AddHours(2),
                EndTime = DateTime.UtcNow.AddHours(4),
                TotalPrice = 300000m,
                DepositAmount = 150000m,
                PaymentStatus = "PARTIALLY_PAID",
                BookingType = "SINGLE",
                Priority = "NORMAL",
                RequestedAt = DateTime.UtcNow
            })
        };
        request.Headers.Add("X-Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }
}
