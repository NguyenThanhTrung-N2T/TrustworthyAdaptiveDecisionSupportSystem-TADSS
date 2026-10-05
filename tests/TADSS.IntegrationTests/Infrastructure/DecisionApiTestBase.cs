using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Strategies;
using TADSS.Runtime.Api.Common;
using TADSS.Infrastructure.Persistence.Entities;
using TADSS.UnitTests.TestData;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Lớp cơ sở cho integration test đi qua pipeline HTTP thật của runtime trên PostgreSQL + Redis thật (v2). Dựng một host TADSS thật
/// (<see cref="TadssWebApplicationFactory"/>) và chỉ thay hai thứ ở host test, không đụng mã Production (C-10): đồng hồ
/// (<see cref="Clock"/>, <see cref="FakeTimeProvider"/> — mọi mốc <c>evaluatedAt</c>, <c>created_at</c>, <c>approvalExpiresAt</c> đều lấy từ
/// <see cref="TimeProvider"/> nên test tất định và không trôi theo ngày chạy) và registry strategy (<see cref="Strategies"/> — đếm số lần strategy chạy,
/// ghi đè chính sách cache). Router strategy (<c>IStrategyRouter</c>) là router thật, đọc <c>Tadss:Routing</c> từ cấu hình cuối cùng của host
/// (gồm <see cref="ConfigurationOverrides"/>) và phân giải tên qua registry đã thay.
/// <para>
/// Mọi response 200 nhận qua <see cref="PostDecisionAsync"/> / <see cref="GetDecisionAsync"/> được kiểm tra schema ngay (CT-01,
/// <see cref="ResponseSchemaAssert"/>) nên test không thể quên kiểm tra.
/// </para>
/// </summary>
public abstract class DecisionApiTestBase : IntegrationTestBase
{
    /// <summary>Đường dẫn collection quyết định.</summary>
    protected const string DecisionsPath = "/api/v2/decisions";

    /// <summary>Mốc đồng hồ ban đầu của mọi test: trùng <c>requestedAt</c> của request mẫu (18/11/2026 09:15 +07:00).</summary>
    protected static readonly DateTimeOffset InitialInstant = new(2026, 11, 18, 2, 15, 0, TimeSpan.Zero);

    private WebApplicationFactory<Program>? _host;
    private HttpClient? _api;

    /// <summary>Hạ tầng thật dùng chung.</summary>
    protected DecisionApiTestBase(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>Đồng hồ của host test; tiến thời gian bằng <see cref="FakeTimeProvider.Advance"/>.</summary>
    protected FakeTimeProvider Clock { get; } = new(InitialInstant);

    /// <summary>
    /// <c>true</c> (mặc định) thì host dùng <see cref="Clock"/> đóng băng; lớp dẫn xuất trả <c>false</c> để host chạy bằng đồng hồ thật
    /// (<see cref="TimeProvider.System"/>). Đồng hồ đóng băng cũng đóng băng timer của Polly (timeout, retry delay, break duration)
    /// nên test chịu lỗi dựa vào timeout/breaker phải dùng đồng hồ thật.
    /// </summary>
    protected virtual bool UseFakeClock => true;

    /// <summary>
    /// Điểm mở rộng để lớp dẫn xuất thay thêm DI của host test (C-10) sau khi đồng hồ và registry mặc định đã được thay. Mặc định không làm gì.
    /// Được gọi khi host được dựng (lần truy cập <see cref="Host"/>/<see cref="Api"/> đầu tiên), không phải lúc <see cref="InitializeAsync"/>.
    /// </summary>
    protected virtual void ConfigureHostServices(IServiceCollection services)
    {
    }

    /// <summary>
    /// Điểm mở rộng để lớp dẫn xuất ghi đè cấu hình của host test (cặp khóa/giá trị, như người vận hành cấp qua biến môi trường), đi qua đăng ký DI thật
    /// (options, validator, composition root) thay vì thay service. Được đọc khi host được dựng và nạp sau mọi nguồn cấu hình có sẵn. Mặc định rỗng.
    /// </summary>
    protected virtual IEnumerable<KeyValuePair<string, string?>> ConfigurationOverrides => [];

    /// <summary>Registry strategy của host test (đếm lần chạy, ghi đè chính sách cache).</summary>
    protected ProbeStrategyRegistry Strategies =>
        (ProbeStrategyRegistry)Host.Services.GetRequiredService<IStrategyRegistry>();

    /// <summary>Host đã thay đồng hồ và registry.</summary>
    protected WebApplicationFactory<Program> Host =>
        _host ?? throw new InvalidOperationException("Host chỉ dùng được sau InitializeAsync.");

    /// <summary><see cref="HttpClient"/> mặc định của host.</summary>
    protected HttpClient Api => _api ??= Host.CreateClient();

    /// <inheritdoc />
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _host = CreateFactory(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(ConfigurationOverrides));
            builder.ConfigureTestServices(services =>
            {
                if (UseFakeClock)
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(Clock);
                }

                services.RemoveAll<IStrategyRegistry>();
                services.AddSingleton<IStrategyRegistry>(sp =>
                    new ProbeStrategyRegistry(ActivatorUtilities.CreateInstance<StrategyRegistry>(sp)));

                ConfigureHostServices(services);
            });
        });
    }

    /// <inheritdoc />
    public override async Task DisposeAsync()
    {
        _api?.Dispose();
        await base.DisposeAsync();
    }

    /// <summary>Tạo thêm một <see cref="HttpClient"/> tới host (dùng cho test gửi song song).</summary>
    protected HttpClient CreateApiClient() => Host.CreateClient();

    /// <summary>Khóa idempotency mới, duy nhất.</summary>
    protected static string NewKey() => "it-" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// <c>POST /api/v2/decisions</c>. <paramref name="idempotencyKey"/> <c>null</c> thì không gửi header. Response 200 phải hợp lệ theo
    /// <c>decision-result.schema.json</c> (CT-01).
    /// </summary>
    protected async Task<DecisionHttpResponse> PostDecisionAsync(
        string body,
        string? idempotencyKey,
        string? correlationId = null,
        HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DecisionsPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, idempotencyKey);
        }

        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.CorrelationId, correlationId);
        }

        var response = await Capture(await (client ?? Api).SendAsync(request));
        if (response.Status == HttpStatusCode.OK)
        {
            ResponseSchemaAssert.IsValidDecisionResult(response.Body);
        }

        return response;
    }

    /// <summary>
    /// <c>GET /api/v2/decisions/{decisionId}</c>. Response 200 phải hợp lệ theo <c>decision-detail.schema.json</c> (CT-01).
    /// </summary>
    protected async Task<DecisionHttpResponse> GetDecisionAsync(Guid decisionId)
    {
        var response = await Capture(await Api.GetAsync($"{DecisionsPath}/{decisionId}"));
        if (response.Status == HttpStatusCode.OK)
        {
            ResponseSchemaAssert.IsValidDecisionDetail(response.Body);
        }

        return response;
    }

    /// <summary>
    /// <c>POST /api/v2/decisions/{decisionId}/override</c>. Header <c>X-Idempotency-Key</c>/<c>X-Actor-Id</c> <c>null</c> thì không gửi. Response 200
    /// phải hợp lệ theo <c>override-response.schema.json</c> (CT-01).
    /// </summary>
    protected async Task<DecisionHttpResponse> PostOverrideAsync(
        Guid decisionId,
        string body,
        string? idempotencyKey,
        string? actorId,
        HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{DecisionsPath}/{decisionId}/override")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, idempotencyKey);
        }

        if (actorId is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.ActorId, actorId);
        }

        var response = await Capture(await (client ?? Api).SendAsync(request));
        if (response.Status == HttpStatusCode.OK)
        {
            ResponseSchemaAssert.IsValidOverrideResponse(response.Body);
        }

        return response;
    }

    /// <summary>
    /// <c>POST /api/v2/decisions/{decisionId}/outcome</c>. <paramref name="idempotencyKey"/> <c>null</c> thì không gửi header. Response 200 phải hợp lệ
    /// theo <c>outcome-response.schema.json</c> (CT-01).
    /// </summary>
    protected async Task<DecisionHttpResponse> PostOutcomeAsync(Guid decisionId, string body, string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{DecisionsPath}/{decisionId}/outcome")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, idempotencyKey);
        }

        var response = await Capture(await Api.SendAsync(request));
        if (response.Status == HttpStatusCode.OK)
        {
            ResponseSchemaAssert.IsValidOutcomeResponse(response.Body);
        }

        return response;
    }

    /// <summary>
    /// <c>GET /api/v2/decisions/pending-approvals{query}</c> (<paramref name="query"/> gồm cả dấu <c>?</c>, hoặc rỗng). Response 200 phải hợp lệ theo
    /// <c>pending-approvals.schema.json</c> (CT-01).
    /// </summary>
    protected async Task<DecisionHttpResponse> GetPendingApprovalsAsync(string query)
    {
        var response = await Capture(await Api.GetAsync($"{DecisionsPath}/pending-approvals{query}"));
        if (response.Status == HttpStatusCode.OK)
        {
            ResponseSchemaAssert.IsValidPendingApprovals(response.Body);
        }

        return response;
    }

    /// <summary>
    /// Khẳng định quyết định fallback (D-13) ở cả response lẫn hàng audit: <c>APPROVED</c> cọc 100% (tỷ lệ 1, số tiền = <c>totalPrice</c> của
    /// <paramref name="requestBody"/>), không có hạn duyệt, <c>FALLBACK</c> / <c>fallbackUsed = true</c>, đúng <paramref name="expectedReasonCode"/>;
    /// trạng thái gốc của "strategy" lưu trong audit cũng là <c>APPROVED</c> (không phải approval window đổi trạng thái).
    /// </summary>
    protected static void AssertFallbackFullDeposit(
        DecisionResultDto result,
        DecisionExecutionEntity row,
        string requestBody,
        string expectedReasonCode)
    {
        var totalPrice = ContractSamples.DeserializeRequest(requestBody).TotalPrice;

        result.Status.Should().Be(DecisionStatus.APPROVED);
        var deposit = ContractSamples.Required(result.Deposit);
        deposit.Rate.Should().Be(1m);
        deposit.Amount.Should().Be(totalPrice);
        result.ApprovalExpiresAt.Should().BeNull("quyết định fallback không chờ Manager duyệt");
        result.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        result.FallbackUsed.Should().BeTrue();
        result.ReasonCode.Should().Be(expectedReasonCode);
        result.Recommendations.Should().BeEmpty();

        row.Status.Should().Be(DecisionStatus.APPROVED);
        row.StrategyStatus.Should().Be(DecisionStatus.APPROVED);
        row.DepositRate.Should().Be(1m);
        row.DepositAmount.Should().Be(totalPrice);
        row.ApprovalExpiresAt.Should().BeNull();
        row.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        row.FallbackUsed.Should().BeTrue();
        row.ReasonCode.Should().Be(expectedReasonCode);
    }

    /// <summary>Đọc trực tiếp mọi hàng <c>decision_overrides</c> của database test (không qua API), theo thời điểm tạo.</summary>
    protected async Task<List<DecisionOverrideEntity>> ReadOverridesAsync()
    {
        await using var db = CreateDbContext();
        return await db.DecisionOverrides.AsNoTracking().OrderBy(row => row.CreatedAt).ToListAsync();
    }

    /// <summary>Đọc trực tiếp mọi hàng <c>decision_outcomes</c> của database test (không qua API), theo thời điểm ghi nhận.</summary>
    protected async Task<List<DecisionOutcomeEntity>> ReadOutcomesAsync()
    {
        await using var db = CreateDbContext();
        return await db.DecisionOutcomes.AsNoTracking().OrderBy(row => row.ReportedAt).ToListAsync();
    }

    /// <summary>
    /// Tạo một quyết định <c>NEED_APPROVAL</c> qua API (request giá trị lớn bắt đầu 20/11/2026 18:00 +07:00 mặc định) và trả kết quả đã kiểm
    /// <c>status</c> và <c>approvalExpiresAt</c>.
    /// </summary>
    protected async Task<DecisionResultDto> CreatePendingDecisionAsync(Guid? branchId = null, DateTimeOffset? start = null)
    {
        var begin = start ?? new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(7));
        var posted = await PostDecisionAsync(DecisionRequests.HighValue(begin, branchId, Guid.NewGuid()), NewKey());
        posted.Status.Should().Be(HttpStatusCode.OK);
        var result = posted.Result;
        result.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        result.ApprovalExpiresAt.Should().NotBeNull();
        return result;
    }

    /// <summary>Đọc trực tiếp mọi hàng <c>decision_executions</c> của database test bằng <c>DbContext</c> (không qua API), theo thời điểm tạo.</summary>
    protected async Task<List<DecisionExecutionEntity>> ReadExecutionsAsync()
    {
        await using var db = CreateDbContext();
        return await db.DecisionExecutions.AsNoTracking().OrderBy(execution => execution.CreatedAt).ToListAsync();
    }

    /// <summary>Các key cache của runtime (<c>tadss:v2:*</c>) hiện có trong Redis thật.</summary>
    protected string[] ReadCacheKeys()
    {
        var multiplexer = Infrastructure.RedisAdmin;
        return multiplexer.GetEndPoints()
            .SelectMany(endpoint => multiplexer.GetServer(endpoint).Keys(pattern: "tadss:v2:*"))
            .Select(key => key.ToString())
            .ToArray();
    }

    private static async Task<DecisionHttpResponse> Capture(HttpResponseMessage response)
    {
        using (response)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return new DecisionHttpResponse(response.StatusCode, response.Headers, response.Content.Headers.ContentType?.MediaType, bytes);
        }
    }
}
