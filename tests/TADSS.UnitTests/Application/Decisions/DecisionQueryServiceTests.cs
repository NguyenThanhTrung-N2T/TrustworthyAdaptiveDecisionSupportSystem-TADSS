using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Decisions;
using TADSS.Application.Exceptions;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Application.Decisions;

/// <summary>
/// UT-11: <see cref="DecisionQueryService"/> dựng <see cref="DecisionDetailDto"/> đúng INTEGRATION_SPEC §3.4 từ bản ghi đã
/// lưu (không chạy lại strategy, không tính lại), tách <c>status</c> và <c>strategyStatus</c>, ánh xạ override/outcome,
/// báo <see cref="DecisionNotFoundException"/> khi không có và để payload hỏng lan ra thành lỗi dữ liệu.
/// Kho audit là <see cref="FakeDecisionAuditStore"/>; kiểm tra schema (CT-01) dùng <c>decision-detail.schema.json</c>.
/// </summary>
public class DecisionQueryServiceTests
{
    private static readonly Guid DecisionId = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000d001");
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 11, 18, 2, 15, 0, 41, TimeSpan.Zero);
    private static readonly DateTimeOffset ApprovalExpiry = new(2026, 11, 18, 2, 45, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OverriddenAt = new(2026, 11, 18, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OccurredAt = new(2026, 11, 20, 18, 30, 0, TimeSpan.FromHours(7));
    private static readonly DateTimeOffset ReportedAt = new(2026, 11, 20, 19, 0, 2, TimeSpan.Zero);

    private static DecisionRequestDto Request() => ContractSamples.DeserializeRequest(ContractSamples.RequestJson);

    private static DepositDto Deposit(decimal rate, decimal amount) =>
        new() { Rate = rate, Amount = amount, Currency = "VND" };

    /// <summary>Bản ghi đủ trường; <c>status</c>/<c>strategyStatus</c> chọn theo ca, các trường INV-7 khớp nhau.</summary>
    private static DecisionExecutionRecord Record(
        DecisionStatus status = DecisionStatus.APPROVED,
        DecisionStatus strategyStatus = DecisionStatus.APPROVED,
        ExecutionType executionType = ExecutionType.FALLBACK,
        bool fallbackUsed = true,
        string? failureReason = "STRATEGY_TIMEOUT",
        bool cacheHit = false,
        string? correlationId = "corr-0001",
        string? responsePayload = null)
    {
        var request = Request();
        var deposit = status == DecisionStatus.APPROVED ? Deposit(0.5m, 114_000m) : null;
        var expiry = status == DecisionStatus.NEED_APPROVAL ? ApprovalExpiry : (DateTimeOffset?)null;
        var response = new DecisionResultDto
        {
            DecisionId = DecisionId,
            ReservationId = request.ReservationId,
            Status = status,
            Deposit = deposit,
            StrategyName = "DefaultDecisionStrategy",
            StrategyVersion = "2.0.0",
            ExecutionType = executionType,
            FallbackUsed = fallbackUsed,
            ReasonCode = "PEAK_HOUR_DEPOSIT",
            Reasons = ["Khung giờ cao điểm áp dụng cọc 50%."],
            Recommendations = [new DecisionRecommendationDto { Type = RecommendationType.REQUIRE_APPROVAL, Description = "Cần Manager duyệt." }],
            ApprovalExpiresAt = expiry,
            EvaluatedAt = EvaluatedAt
        };

        return new DecisionExecutionRecord(
            DecisionId: DecisionId,
            ReservationId: request.ReservationId,
            BranchId: request.BranchId,
            Role: ExecutionRole.PRIMARY,
            StrategyName: response.StrategyName,
            StrategyVersion: response.StrategyVersion,
            StrategyStatus: strategyStatus,
            Status: status,
            DepositRate: deposit?.Rate,
            DepositAmount: deposit?.Amount,
            ReasonCode: response.ReasonCode,
            ExecutionType: executionType,
            ExecutionTimeMs: 3,
            FallbackUsed: fallbackUsed,
            FailureReason: failureReason,
            CacheHit: cacheHit,
            ApprovalExpiresAt: expiry,
            RequestHash: new string('a', 64),
            RequestPayload: JsonSerializer.Serialize(request, TadssJson.Options),
            ResponsePayload: responsePayload ?? JsonSerializer.Serialize(response, TadssJson.Options),
            IdempotencyKey: "idem-key-0001",
            CorrelationId: correlationId,
            CreatedAt: EvaluatedAt);
    }

    private static DecisionOverrideRecord OverrideRecord() => new(
        DecisionId, OverrideAction.APPROVE, 0.3m, 68_400m, "manager-01", "Khách quen, chi nhánh bảo lãnh", "override-key-01", OverriddenAt);

    private static DecisionOutcomeRecord OutcomeRecord() => new(DecisionId, DecisionOutcome.NO_SHOW, OccurredAt, ReportedAt);

    private static (DecisionQueryService Service, FakeDecisionAuditStore Store) Create()
    {
        var store = new FakeDecisionAuditStore();
        return (new DecisionQueryService(store, new FakeTimeProvider(GovernanceTestData.Now)), store);
    }

    /// <summary>Bản ghi đủ trường được ánh xạ đúng từng trường, gồm strategyStatus, failureReason, cacheHit.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task GetDetailAsync_ExistingDecision_MapsEveryField()
    {
        var (service, store) = Create();
        var record = Record(cacheHit: true, executionType: ExecutionType.CACHE_HIT, fallbackUsed: false, failureReason: null);
        store.Seed(record);

        var detail = await service.GetDetailAsync(DecisionId, CancellationToken.None);

        detail.DecisionId.Should().Be(DecisionId);
        detail.ReservationId.Should().Be(record.ReservationId);
        detail.Request.ReservationId.Should().Be(record.ReservationId);
        detail.Request.TotalPrice.Should().Be(228_000m);
        detail.Request.Currency.Should().Be("VND");
        detail.Result.Status.Should().Be(DecisionStatus.APPROVED);
        detail.Result.StrategyStatus.Should().Be(DecisionStatus.APPROVED);
        var deposit = ContractSamples.Required(detail.Result.Deposit);
        deposit.Rate.Should().Be(0.5m);
        deposit.Amount.Should().Be(114_000m);
        deposit.Currency.Should().Be("VND");
        detail.Result.ReasonCode.Should().Be("PEAK_HOUR_DEPOSIT");
        detail.Result.Reasons.Should().Equal("Khung giờ cao điểm áp dụng cọc 50%.");
        detail.Result.Recommendations.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new DecisionRecommendationDto { Type = RecommendationType.REQUIRE_APPROVAL, Description = "Cần Manager duyệt." });
        detail.Result.ApprovalExpiresAt.Should().BeNull();
        detail.Execution.StrategyName.Should().Be("DefaultDecisionStrategy");
        detail.Execution.StrategyVersion.Should().Be("2.0.0");
        detail.Execution.ExecutionType.Should().Be(ExecutionType.CACHE_HIT);
        detail.Execution.ExecutionTimeMs.Should().Be(3);
        detail.Execution.FallbackUsed.Should().BeFalse();
        detail.Execution.FailureReason.Should().BeNull();
        detail.Execution.CacheHit.Should().BeTrue();
        detail.Override.Should().BeNull();
        detail.Outcome.Should().BeNull();
        detail.CreatedAt.Should().Be(EvaluatedAt);
        detail.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        detail.CorrelationId.Should().Be("corr-0001");
    }

    /// <summary>Telemetry fallback (failureReason, fallbackUsed) và correlationId rỗng đi qua nguyên vẹn.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task GetDetailAsync_FallbackWithoutCorrelationId_KeepsFailureReasonAndNullCorrelation()
    {
        var (service, store) = Create();
        store.Seed(Record(correlationId: null));

        var detail = await service.GetDetailAsync(DecisionId, CancellationToken.None);

        detail.Execution.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        detail.Execution.FallbackUsed.Should().BeTrue();
        detail.Execution.FailureReason.Should().Be("STRATEGY_TIMEOUT");
        detail.CorrelationId.Should().BeNull();
    }

    /// <summary>NEED_APPROVAL mang hạn duyệt từ response đã lưu và không có cọc.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task GetDetailAsync_NeedApproval_CarriesApprovalExpiryAndNoDeposit()
    {
        var (service, store) = Create();
        store.Seed(Record(status: DecisionStatus.NEED_APPROVAL, strategyStatus: DecisionStatus.NEED_APPROVAL));

        var detail = await service.GetDetailAsync(DecisionId, CancellationToken.None);

        detail.Result.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        detail.Result.Deposit.Should().BeNull();
        detail.Result.ApprovalExpiresAt.Should().Be(ApprovalExpiry);
    }

    /// <summary>Approval window không khả dụng: status APPROVED nhưng strategyStatus gốc vẫn là NEED_APPROVAL.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task GetDetailAsync_WindowUnavailable_ShowsStrategyStatusSeparately()
    {
        var (service, store) = Create();
        store.Seed(Record(status: DecisionStatus.APPROVED, strategyStatus: DecisionStatus.NEED_APPROVAL));

        var detail = await service.GetDetailAsync(DecisionId, CancellationToken.None);

        detail.Result.Status.Should().Be(DecisionStatus.APPROVED);
        detail.Result.StrategyStatus.Should().Be(DecisionStatus.NEED_APPROVAL);
    }

    /// <summary>Override và outcome được dựng đúng hình dạng §3.6/§3.7, cọc override mang tiền tệ của request.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task GetDetailAsync_WithOverrideAndOutcome_MapsBoth()
    {
        var (service, store) = Create();
        store.Seed(Record(status: DecisionStatus.NEED_APPROVAL, strategyStatus: DecisionStatus.NEED_APPROVAL));
        store.SeedOverride(OverrideRecord());
        store.SeedOutcome(OutcomeRecord());

        var detail = await service.GetDetailAsync(DecisionId, CancellationToken.None);

        var overrideDto = ContractSamples.Required(detail.Override);
        overrideDto.DecisionId.Should().Be(DecisionId);
        overrideDto.Action.Should().Be(OverrideAction.APPROVE);
        overrideDto.Deposit.Should().BeEquivalentTo(Deposit(0.3m, 68_400m));
        overrideDto.ActorId.Should().Be("manager-01");
        overrideDto.Reason.Should().Be("Khách quen, chi nhánh bảo lãnh");
        overrideDto.OverriddenAt.Should().Be(OverriddenAt);

        var outcomeDto = ContractSamples.Required(detail.Outcome);
        outcomeDto.DecisionId.Should().Be(DecisionId);
        outcomeDto.Outcome.Should().Be(DecisionOutcome.NO_SHOW);
        outcomeDto.OccurredAt.Should().Be(OccurredAt);
        outcomeDto.ReportedAt.Should().Be(ReportedAt);
    }

    /// <summary>Override REJECT không có cọc thì <c>deposit</c> là null.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task GetDetailAsync_RejectOverride_HasNullDeposit()
    {
        var (service, store) = Create();
        store.Seed(Record(status: DecisionStatus.NEED_APPROVAL, strategyStatus: DecisionStatus.NEED_APPROVAL));
        store.SeedOverride(OverrideRecord() with { Action = OverrideAction.REJECT, DepositRate = null, DepositAmount = null });

        var detail = await service.GetDetailAsync(DecisionId, CancellationToken.None);

        var overrideDto = ContractSamples.Required(detail.Override);
        overrideDto.Action.Should().Be(OverrideAction.REJECT);
        overrideDto.Deposit.Should().BeNull();
    }

    /// <summary>Không có bản ghi thì ném <see cref="DecisionNotFoundException"/> mang decisionId.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task GetDetailAsync_Unknown_ThrowsDecisionNotFound()
    {
        var (service, _) = Create();
        var unknown = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000ffff");

        var act = () => service.GetDetailAsync(unknown, CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<DecisionNotFoundException>()).Which;
        exception.DecisionId.Should().Be(unknown);
        exception.Message.Should().Contain(unknown.ToString());
    }

    /// <summary>Payload response hỏng không bị che: <see cref="JsonException"/> lan ra (thành 500).</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task GetDetailAsync_CorruptResponsePayload_PropagatesJsonException()
    {
        var (service, store) = Create();
        store.Seed(Record(responsePayload: "{ \"decisionId\": \"khong-phai-guid\" }"));

        var act = () => service.GetDetailAsync(DecisionId, CancellationToken.None);

        await act.Should().ThrowAsync<JsonException>();
    }

    /// <summary>Token đã hủy được chuyển xuống store và lan ra nguyên vẹn.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task GetDetailAsync_CancelledToken_PropagatesOperationCanceled()
    {
        var (service, store) = Create();
        store.Seed(Record());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.GetDetailAsync(DecisionId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>Chi tiết đầy đủ (có override và outcome) khi serialize bằng <c>TadssJson</c> hợp lệ theo schema chi tiết.</summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public async Task GetDetailAsync_ResultSerializesValidAgainstDetailSchema()
    {
        var (service, store) = Create();
        store.Seed(Record(status: DecisionStatus.APPROVED, strategyStatus: DecisionStatus.NEED_APPROVAL));
        store.SeedOverride(OverrideRecord());
        store.SeedOutcome(OutcomeRecord());

        var detail = await service.GetDetailAsync(DecisionId, CancellationToken.None);
        var json = JsonSerializer.Serialize(detail, TadssJson.Options);

        var results = ContractSchemas.Evaluate("decision-detail.schema.json", json);
        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    /// <summary>Chi tiết không có override/outcome/correlationId cũng hợp lệ theo schema (các trường vẫn xuất hiện với giá trị null).</summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public async Task GetDetailAsync_WithoutOverrideOutcomeCorrelation_SerializesValidAgainstDetailSchema()
    {
        var (service, store) = Create();
        store.Seed(Record(correlationId: null, status: DecisionStatus.NEED_APPROVAL, strategyStatus: DecisionStatus.NEED_APPROVAL));

        var detail = await service.GetDetailAsync(DecisionId, CancellationToken.None);
        var json = JsonSerializer.Serialize(detail, TadssJson.Options);

        var results = ContractSchemas.Evaluate("decision-detail.schema.json", json);
        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    private static readonly Guid PendingA = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000a001");
    private static readonly Guid PendingB = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000a002");

    /// <summary>
    /// Hàng chờ dựng item từ cột bản ghi và <c>request_payload</c>: đủ 12 trường, <c>earliestStart</c> là <c>startTime</c> sớm nhất theo thời điểm
    /// (giữ offset +07:00 của item đó), thời điểm cột trả theo UTC, kết quả bọc <see cref="PagedResult{T}"/>; chi nhánh khác và bản hết hạn không có mặt.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task PendingApprovals_ComposesItemsFromRecordAndPayload()
    {
        var (service, store) = Create();
        var otherBranch = Guid.Parse("1f2e3d4c-0000-4000-8000-0000000000ff");
        var createdA = GovernanceTestData.Now.AddMinutes(-3);
        var createdB = GovernanceTestData.Now.AddMinutes(-2);
        store.Seed(GovernanceTestData.Execution(
            PendingA,
            approvalExpiresAt: GovernanceTestData.Now.AddMinutes(10),
            createdAt: createdA,
            totalPrice: 228_000m,
            reasonCode: "PEAK_HOUR_NEED_APPROVAL",
            startTimes: ["2026-11-20T18:00:00+00:00", "2026-11-20T20:00:00+07:00"]));
        store.Seed(GovernanceTestData.Execution(
            PendingB,
            approvalExpiresAt: GovernanceTestData.Now.AddMinutes(20),
            createdAt: createdB,
            totalPrice: 20.01m,
            currency: "USD",
            // Bản ghi NEED_APPROVAL kèm fallbackUsed chỉ còn ở dữ liệu cũ (fallback hiện là APPROVED cọc 100%, D-13);
            // hàng chờ vẫn phải map nguyên vẹn cờ đã lưu
            fallbackUsed: true,
            reasonCode: "STRATEGY_TIMEOUT_FALLBACK",
            isGuest: true,
            tier: CustomerTier.STANDARD,
            startTimes: ["2026-11-21T09:00:00+07:00"]));
        store.Seed(GovernanceTestData.Execution(Guid.NewGuid(), branchId: otherBranch));
        store.Seed(GovernanceTestData.Execution(Guid.NewGuid(), approvalExpiresAt: GovernanceTestData.Now));

        var page = await service.GetPendingApprovalsAsync(GovernanceTestData.BranchId, 1, 20, CancellationToken.None);

        page.Page.Should().Be(1);
        page.PageSize.Should().Be(20);
        page.TotalItems.Should().Be(2);
        page.TotalPages.Should().Be(1);
        page.Items.Should().HaveCount(2);
        var first = page.Items[0];
        first.DecisionId.Should().Be(PendingA);
        first.ReservationId.Should().NotBeEmpty();
        first.BranchId.Should().Be(GovernanceTestData.BranchId);
        first.TotalPrice.Should().Be(228_000m);
        first.Currency.Should().Be("VND");
        first.EarliestStart.Should().Be(new DateTimeOffset(2026, 11, 20, 20, 0, 0, TimeSpan.FromHours(7)));
        first.EarliestStart.Offset.Should().Be(TimeSpan.FromHours(7), "giữ offset của item sớm nhất");
        first.Customer.IsGuest.Should().BeFalse();
        first.Customer.Tier.Should().Be(CustomerTier.GOLD);
        first.ReasonCode.Should().Be("PEAK_HOUR_NEED_APPROVAL");
        first.FallbackUsed.Should().BeFalse();
        first.ApprovalExpiresAt.Should().Be(GovernanceTestData.Now.AddMinutes(10));
        first.ApprovalExpiresAt.Offset.Should().Be(TimeSpan.Zero);
        first.CreatedAt.Should().Be(createdA);
        first.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        var second = page.Items[1];
        second.DecisionId.Should().Be(PendingB);
        second.TotalPrice.Should().Be(20.01m);
        second.Currency.Should().Be("USD");
        second.Customer.IsGuest.Should().BeTrue();
        second.Customer.Tier.Should().Be(CustomerTier.STANDARD);
        second.FallbackUsed.Should().BeTrue();
        second.ReasonCode.Should().Be("STRATEGY_TIMEOUT_FALLBACK");
        second.EarliestStart.Should().Be(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.FromHours(7)));
    }

    /// <summary>Hàng chờ rỗng vẫn là một trang hợp lệ (không có item, tổng 0).</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task PendingApprovals_NoRecords_ReturnsEmptyPage()
    {
        var (service, _) = Create();

        var page = await service.GetPendingApprovalsAsync(GovernanceTestData.BranchId, 3, 10, CancellationToken.None);

        page.Items.Should().BeEmpty();
        page.TotalItems.Should().Be(0);
        page.Page.Should().Be(3);
        page.PageSize.Should().Be(10);
        page.HasNext.Should().BeFalse();
    }

    /// <summary>Mốc hạn dùng đồng hồ của service đã cắt µs: bản hết hạn đúng bằng <c>now</c> bị loại, hơn một µs thì còn.</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task PendingApprovals_ExpiryComparedStrictlyWithClock()
    {
        var store = new FakeDecisionAuditStore();
        var service = new DecisionQueryService(store, new FakeTimeProvider(GovernanceTestData.Now.AddTicks(7)));
        store.Seed(GovernanceTestData.Execution(PendingA, approvalExpiresAt: GovernanceTestData.Now));
        store.Seed(GovernanceTestData.Execution(PendingB, approvalExpiresAt: GovernanceTestData.Now.AddTicks(10)));

        var page = await service.GetPendingApprovalsAsync(GovernanceTestData.BranchId, 1, 20, CancellationToken.None);

        page.Items.Should().ContainSingle().Which.DecisionId.Should().Be(PendingB);
    }

    public static TheoryData<int, int, string> InvalidPaging => new()
    {
        { 0, 20, "page" },
        { -1, 20, "page" },
        { 1, 0, "pageSize" },
        { 1, -5, "pageSize" },
        { 1, 101, "pageSize" },
    };

    /// <summary>page &lt; 1 hoặc pageSize ngoài [1, 100] → 400 <c>VALIDATION_ERROR</c> đúng field, không truy vấn kho.</summary>
    [Theory]
    [Trait("CatalogId", "UT-15")]
    [MemberData(nameof(InvalidPaging))]
    public async Task PendingApprovals_InvalidPaging_Throws400_Theory(int page, int pageSize, string expectedField)
    {
        var (service, store) = Create();

        var act = () => service.GetPendingApprovalsAsync(GovernanceTestData.BranchId, page, pageSize, CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<ContractValidationException>()).Which;
        exception.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        exception.FieldName.Should().Be(expectedField);
        store.PendingCount.Should().Be(0);
    }

    /// <summary>Biên hợp lệ: page 1, pageSize 1 và 100 được chấp nhận.</summary>
    [Theory]
    [Trait("CatalogId", "UT-15")]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    public async Task PendingApprovals_BoundaryPaging_IsAccepted_Theory(int page, int pageSize)
    {
        var (service, store) = Create();

        var result = await service.GetPendingApprovalsAsync(GovernanceTestData.BranchId, page, pageSize, CancellationToken.None);

        result.PageSize.Should().Be(pageSize);
        store.PendingCount.Should().Be(1);
    }
}
