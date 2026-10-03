using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Decisions;
using TADSS.Runtime.Api.Configuration;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Application.Decisions;

/// <summary>
/// UT-11 / CT-01: chạy <see cref="DecisionService"/> trên đồ thị DI thật của <c>AddDecisionRuntime</c> với
/// <c>appsettings.json</c> được phát hành (registry, pipeline Polly, cache memory và composer thật; chỉ kho audit và đồng hồ
/// là test double) để chứng minh các thành phần đã được kiểm riêng lẻ ghép lại đúng: response hợp lệ theo
/// <c>decision-result.schema.json</c>, replay idempotency, và cache hit cho khóa mới cùng payload.
/// </summary>
public class DecisionServiceEndToEndTests
{
    private const string ResultSchema = "decision-result.schema.json";

    private static readonly DateTimeOffset Now = new(2026, 11, 18, 2, 15, 0, TimeSpan.Zero);

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepositoryPaths.Resolve("src", "TADSS.Runtime.Api", "appsettings.json"), optional: false)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddDecisionRuntime(configuration);
        return services.BuildServiceProvider();
    }

    private static DecisionResultDto Deserialize(string payload) =>
        ContractSamples.Required(JsonSerializer.Deserialize<DecisionResultDto>(payload, TadssJson.Options));

    [Fact]
    [Trait("CatalogId", "UT-11")]
    [Trait("CatalogId", "CT-01")]
    public async Task EvaluateAsync_RealGraphFirstCall_ReturnsSchemaValidResponseWithOnePrimaryRecord()
    {
        using var provider = BuildProvider();
        var store = new FakeDecisionAuditStore();
        var service = ActivatorUtilities.CreateInstance<DecisionService>(provider, store);

        var response = await service.EvaluateAsync(ContractSamples.DeserializeRequest(ContractSamples.RequestJson), "idem-e2e-0001", "corr-e2e", CancellationToken.None);

        var results = ContractSchemas.Evaluate(ResultSchema, JsonSerializer.Serialize(response, TadssJson.Options));
        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
        response.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        response.StrategyName.Should().Be("DefaultDecisionStrategy");
        var record = store.Records.Should().ContainSingle().Subject;
        record.Role.Should().Be(ExecutionRole.PRIMARY);
        record.DecisionId.Should().Be(response.DecisionId);
        Deserialize(record.ResponsePayload).Should().BeEquivalentTo(response);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_RealGraphSameKeySamePayload_ReplaysFirstResponseWithoutNewRecord()
    {
        using var provider = BuildProvider();
        var store = new FakeDecisionAuditStore();
        var service = ActivatorUtilities.CreateInstance<DecisionService>(provider, store);

        var first = await service.EvaluateAsync(ContractSamples.DeserializeRequest(ContractSamples.RequestJson), "idem-e2e-0001", "corr-e2e", CancellationToken.None);
        var replay = await service.EvaluateAsync(ContractSamples.DeserializeRequest(ContractSamples.RequestJson), "idem-e2e-0001", "corr-e2e", CancellationToken.None);

        replay.Should().BeEquivalentTo(first);
        store.Records.Should().ContainSingle("replay không ghi bản ghi mới");
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_RealGraphNewKeySamePayload_ReturnsCacheHitWithNewDecisionId()
    {
        // Strategy mặc định bật cache (CacheTtlMinutes = 60 trong appsettings.json) và chỉ dùng memory vì host test không đăng ký Redis
        using var provider = BuildProvider();
        var store = new FakeDecisionAuditStore();
        var service = ActivatorUtilities.CreateInstance<DecisionService>(provider, store);

        var first = await service.EvaluateAsync(ContractSamples.DeserializeRequest(ContractSamples.RequestJson), "idem-e2e-0001", "corr-e2e", CancellationToken.None);
        var second = await service.EvaluateAsync(ContractSamples.DeserializeRequest(ContractSamples.RequestJson), "idem-e2e-0002", "corr-e2e", CancellationToken.None);

        first.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        second.ExecutionType.Should().Be(ExecutionType.CACHE_HIT);
        second.DecisionId.Should().NotBe(first.DecisionId);
        second.Status.Should().Be(first.Status);
        second.ReasonCode.Should().Be(first.ReasonCode);
        store.Records.Should().HaveCount(2).And.OnlyContain(record => record.Role == ExecutionRole.PRIMARY);
    }
}
