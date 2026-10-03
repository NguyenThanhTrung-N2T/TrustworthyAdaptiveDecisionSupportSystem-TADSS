using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Decisions;
using TADSS.Application.Exceptions;
using TADSS.Application.Governance;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Application.Governance;

/// <summary>
/// UT-15: <see cref="OverrideService"/> theo F-03/F-04: quy đổi cọc một nơi (rate → amount, amount → rate), REJECT không cọc, thứ tự kiểm tra
/// (không tồn tại/SHADOW → 404, đã override: replay/xung đột key/<c>ALREADY_OVERRIDDEN</c>, không chờ duyệt, hết hạn), thua tranh chấp
/// <c>UNIQUE(decision_id)</c> xử lý như đã có bản thắng, một mốc <c>nowUtc</c> cắt µs cho cả kiểm tra hạn lẫn <c>created_at</c>, và log không chứa <c>reason</c>.
/// Kho là <see cref="FakeDecisionAuditStore"/>, đồng hồ là <see cref="FakeTimeProvider"/>.
/// </summary>
public class OverrideServiceTests
{
    private const string ActorId = "manager-01";
    private const string Key = "override-key-0001";
    private const string Reason = "Khách quen, chi nhánh bảo lãnh";

    private static readonly Guid DecisionId = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000d001");
    private static readonly DateTimeOffset Now = GovernanceTestData.Now;

    private sealed class Fixture
    {
        public Fixture(DateTimeOffset? now = null)
        {
            Time = new FakeTimeProvider(now ?? Now);
            Service = new OverrideService(Store, Store, new DepositCalculator(), Time, Logger);
        }

        public FakeDecisionAuditStore Store { get; } = new();

        public FakeTimeProvider Time { get; }

        public CapturingLogger<OverrideService> Logger { get; } = new();

        public OverrideService Service { get; }

        public Task<OverrideResponseDto> OverrideAsync(
            OverrideRequestDto request, string actorId = ActorId, string idempotencyKey = Key, Guid? decisionId = null) =>
            Service.OverrideAsync(decisionId ?? DecisionId, request, actorId, idempotencyKey, CancellationToken.None);
    }

    private static OverrideRequestDto ApproveRate(decimal rate = 0.3m, string reason = Reason) => new()
    {
        Action = OverrideAction.APPROVE,
        Deposit = new OverrideDepositDto { Rate = rate },
        Reason = reason,
    };

    private static OverrideRequestDto ApproveAmount(decimal amount, string reason = Reason) => new()
    {
        Action = OverrideAction.APPROVE,
        Deposit = new OverrideDepositDto { Amount = amount },
        Reason = reason,
    };

    private static OverrideRequestDto Reject(string reason = Reason) => new() { Action = OverrideAction.REJECT, Reason = reason };

    private static DecisionOverrideRecord ExistingApprove(string key = Key, string actor = ActorId, string reason = Reason) =>
        new(DecisionId, OverrideAction.APPROVE, 0.3m, 68_400m, actor, reason, key, Now.AddMinutes(-5));

    /// <summary>Rate 0.3 trên đơn 228000 VND: ghi rate 0.3 và amount 68400 (quy đổi một nơi), response khớp bản ghi.</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task Override_ApproveWithRate_PersistsConvertedDeposit()
    {
        var fixture = new Fixture();
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));

        var response = await fixture.OverrideAsync(ApproveRate(0.3m));

        var stored = fixture.Store.Overrides[DecisionId];
        stored.Action.Should().Be(OverrideAction.APPROVE);
        stored.DepositRate.Should().Be(0.3m);
        stored.DepositAmount.Should().Be(68_400m);
        stored.ActorId.Should().Be(ActorId);
        stored.Reason.Should().Be(Reason);
        stored.IdempotencyKey.Should().Be(Key);
        stored.CreatedAt.Should().Be(Now);
        response.DecisionId.Should().Be(DecisionId);
        response.Action.Should().Be(OverrideAction.APPROVE);
        var deposit = ContractSamples.Required(response.Deposit);
        deposit.Rate.Should().Be(0.3m);
        deposit.Amount.Should().Be(68_400m);
        deposit.Currency.Should().Be("VND");
        response.ActorId.Should().Be(ActorId);
        response.Reason.Should().Be(Reason);
        response.OverriddenAt.Should().Be(Now);
        response.OverriddenAt.Offset.Should().Be(TimeSpan.Zero);
    }

    public static TheoryData<decimal, decimal, decimal> AmountCases => new()
    {
        { 228_000m, 50_000m, 0.2193m },
        { 228_000m, 228_000m, 1m },
        { 0m, 0m, 0m },
    };

    /// <summary>Gửi amount: giữ nguyên amount và suy ra rate làm tròn 4 chữ số (total 0 + amount 0 → rate 0).</summary>
    [Theory]
    [Trait("CatalogId", "UT-15")]
    [MemberData(nameof(AmountCases))]
    public async Task Override_ApproveWithAmount_DerivesRate_Theory(decimal totalPrice, decimal amount, decimal expectedRate)
    {
        var fixture = new Fixture();
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId, totalPrice: totalPrice));

        var response = await fixture.OverrideAsync(ApproveAmount(amount));

        var stored = fixture.Store.Overrides[DecisionId];
        stored.DepositRate.Should().Be(expectedRate);
        stored.DepositAmount.Should().Be(amount);
        var deposit = ContractSamples.Required(response.Deposit);
        deposit.Rate.Should().Be(expectedRate);
        deposit.Amount.Should().Be(amount);
    }

    /// <summary>REJECT không ghi rate/amount và response có <c>deposit = null</c>.</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task Override_Reject_HasNoDeposit()
    {
        var fixture = new Fixture();
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));

        var response = await fixture.OverrideAsync(Reject());

        var stored = fixture.Store.Overrides[DecisionId];
        stored.Action.Should().Be(OverrideAction.REJECT);
        stored.DepositRate.Should().BeNull();
        stored.DepositAmount.Should().BeNull();
        response.Action.Should().Be(OverrideAction.REJECT);
        response.Deposit.Should().BeNull();
    }

    /// <summary>Tiền cọc theo tiền tệ của quyết định: USD làm tròn 2 chữ số và response mang tiền tệ đó.</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task Override_ApproveWithRate_UsesCurrencyOfDecision()
    {
        var fixture = new Fixture();
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId, totalPrice: 20.01m, currency: "USD"));

        var response = await fixture.OverrideAsync(ApproveRate(0.5m));

        var deposit = ContractSamples.Required(response.Deposit);
        deposit.Amount.Should().Be(10.01m);
        deposit.Currency.Should().Be("USD");
    }

    /// <summary>Mốc <c>created_at</c> và <c>overriddenAt</c> là cùng một <c>nowUtc</c> đã cắt về µs, offset 0.</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task Override_Success_UsesSingleTruncatedUtcNow()
    {
        var withSubMicroTicks = Now.AddTicks(7);
        var fixture = new Fixture(withSubMicroTicks);
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));

        var response = await fixture.OverrideAsync(ApproveRate());

        fixture.Store.Overrides[DecisionId].CreatedAt.Should().Be(Now);
        fixture.Store.Overrides[DecisionId].CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        response.OverriddenAt.Should().Be(Now);
    }

    /// <summary>Hạn duyệt đúng bằng <c>now</c> sau khi cắt µs: hết hạn (so nghiêm ngặt, F-03 bước 5); hơn một µs thì còn hạn.</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task Override_ExpiryComparedWithTruncatedNow()
    {
        var fixture = new Fixture(Now.AddTicks(7));
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId, approvalExpiresAt: Now));
        var stillValid = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000d002");
        fixture.Store.Seed(GovernanceTestData.Execution(stillValid, approvalExpiresAt: Now.AddTicks(10)));

        var expired = () => fixture.OverrideAsync(ApproveRate());
        var response = await fixture.OverrideAsync(ApproveRate(), decisionId: stillValid);

        (await expired.Should().ThrowAsync<DecisionStateConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.ApprovalExpired);
        response.DecisionId.Should().Be(stillValid);
    }

    /// <summary>Request sai hình thức bị chặn trước mọi truy cập kho (F-03 bước 1).</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task Override_MalformedRequest_Throws400BeforeTouchingStore()
    {
        var fixture = new Fixture();
        var request = new OverrideRequestDto { Action = OverrideAction.APPROVE, Reason = Reason };

        var act = () => fixture.OverrideAsync(request);

        (await act.Should().ThrowAsync<ContractValidationException>()).Which.FieldName.Should().Be("deposit");
        fixture.Store.DetailCount.Should().Be(0);
        fixture.Store.OverrideInsertCount.Should().Be(0);
    }

    /// <summary>Amount lớn hơn tổng tiền của quyết định → 400 <c>deposit.amount</c> và không ghi gì.</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task Override_AmountAboveTotal_Throws400AndWritesNothing()
    {
        var fixture = new Fixture();
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));

        var act = () => fixture.OverrideAsync(ApproveAmount(228_001m));

        (await act.Should().ThrowAsync<ContractValidationException>()).Which.FieldName.Should().Be("deposit.amount");
        fixture.Store.OverrideInsertCount.Should().Be(0);
    }

    public static TheoryData<string> CheckOrderScenarios => new()
    {
        "Missing",
        "Shadow",
        "ExistingSameKeySamePayload",
        "ExistingSameKeyDifferentPayload",
        "ExistingSameKeyDifferentActor",
        "ExistingSameKeyDifferentReasonCase",
        "ExistingDifferentKey",
        "ExistingDifferentKeyAndExpired",
        "Approved",
        "HasOutcome",
        "ExpiredExactlyNow",
    };

    /// <summary>Thứ tự kiểm tra F-03: dừng ở bước đầu tiên không đạt và không ghi gì khi bị từ chối hoặc replay.</summary>
    [Theory]
    [Trait("CatalogId", "UT-15")]
    [MemberData(nameof(CheckOrderScenarios))]
    public async Task Override_CheckOrder_Theory(string scenario)
    {
        var fixture = new Fixture();
        var request = ApproveRate(0.3m);
        switch (scenario)
        {
            case "Shadow":
                fixture.Store.Seed(GovernanceTestData.Execution(DecisionId, role: ExecutionRole.SHADOW));
                break;
            case "ExistingSameKeySamePayload":
                fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));
                fixture.Store.SeedOverride(ExistingApprove());
                break;
            case "ExistingSameKeyDifferentPayload":
                fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));
                fixture.Store.SeedOverride(ExistingApprove());
                request = ApproveRate(0.4m);
                break;
            case "ExistingSameKeyDifferentActor":
                fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));
                fixture.Store.SeedOverride(ExistingApprove(actor: "manager-02"));
                break;
            case "ExistingSameKeyDifferentReasonCase":
                fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));
                fixture.Store.SeedOverride(ExistingApprove(reason: Reason.ToUpperInvariant()));
                break;
            case "ExistingDifferentKey":
                fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));
                fixture.Store.SeedOverride(ExistingApprove(key: "another-key"));
                break;
            case "ExistingDifferentKeyAndExpired":
                fixture.Store.Seed(GovernanceTestData.Execution(DecisionId, approvalExpiresAt: Now.AddMinutes(-1)));
                fixture.Store.SeedOverride(ExistingApprove(key: "another-key"));
                break;
            case "Approved":
                fixture.Store.Seed(GovernanceTestData.Execution(DecisionId, status: DecisionStatus.APPROVED));
                break;
            case "HasOutcome":
                fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));
                fixture.Store.SeedOutcome(new DecisionOutcomeRecord(DecisionId, DecisionOutcome.COMPLETED, Now.AddHours(1), Now.AddHours(2)));
                break;
            case "ExpiredExactlyNow":
                fixture.Store.Seed(GovernanceTestData.Execution(DecisionId, approvalExpiresAt: Now));
                break;
        }

        var act = () => fixture.OverrideAsync(request);

        switch (scenario)
        {
            case "Missing":
            case "Shadow":
                await act.Should().ThrowAsync<DecisionNotFoundException>();
                break;
            case "ExistingSameKeySamePayload":
                var replay = await act();
                replay.DecisionId.Should().Be(DecisionId);
                replay.Action.Should().Be(OverrideAction.APPROVE);
                ContractSamples.Required(replay.Deposit).Amount.Should().Be(68_400m);
                replay.OverriddenAt.Should().Be(Now.AddMinutes(-5), "replay trả đúng bản đã lưu, không phải mốc mới");
                break;
            case "ExistingSameKeyDifferentPayload":
            case "ExistingSameKeyDifferentActor":
            case "ExistingSameKeyDifferentReasonCase":
                (await act.Should().ThrowAsync<IdempotencyConflictException>()).Which.IdempotencyKey.Should().Be(Key);
                break;
            case "ExistingDifferentKey":
            case "ExistingDifferentKeyAndExpired":
                (await act.Should().ThrowAsync<DecisionStateConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.AlreadyOverridden);
                break;
            case "Approved":
            case "HasOutcome":
                (await act.Should().ThrowAsync<DecisionStateConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.NotPendingApproval);
                break;
            case "ExpiredExactlyNow":
                (await act.Should().ThrowAsync<DecisionStateConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.ApprovalExpired);
                break;
        }

        fixture.Store.OverrideInsertCount.Should().Be(0, "mọi nhánh trên đều dừng trước khi insert");
    }

    /// <summary>Thua tranh chấp <c>UNIQUE(decision_id)</c>: cùng key + payload → 200 với bản thắng; khác key → <c>ALREADY_OVERRIDDEN</c>.</summary>
    [Theory]
    [Trait("CatalogId", "UT-15")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Override_LostRace_TreatedAsExisting_Theory(bool sameKeyAndPayload)
    {
        var fixture = new Fixture();
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));
        var winner = ExistingApprove(key: sameKeyAndPayload ? Key : "winner-key");
        fixture.Store.SimulateLostOverrideRace(winner);

        var act = () => fixture.OverrideAsync(ApproveRate(0.3m));

        if (sameKeyAndPayload)
        {
            var response = await act();
            response.OverriddenAt.Should().Be(winner.CreatedAt, "trả bản của request thắng");
            ContractSamples.Required(response.Deposit).Amount.Should().Be(68_400m);
        }
        else
        {
            (await act.Should().ThrowAsync<DecisionStateConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.AlreadyOverridden);
        }

        fixture.Store.OverrideInsertCount.Should().Be(1);
    }

    /// <summary>Thua tranh chấp với bản thắng cùng key nhưng khác payload → <see cref="IdempotencyConflictException"/>.</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task Override_LostRace_SameKeyDifferentPayload_ThrowsIdempotencyConflict()
    {
        var fixture = new Fixture();
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));
        fixture.Store.SimulateLostOverrideRace(ExistingApprove());

        var act = () => fixture.OverrideAsync(ApproveRate(0.4m));

        await act.Should().ThrowAsync<IdempotencyConflictException>();
    }

    /// <summary>Log Information khi thành công có decisionId, action và cờ replay; tuyệt đối không có <c>reason</c>.</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task Override_Success_LogsInformationWithoutReason()
    {
        var fixture = new Fixture();
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));
        const string secretReason = "ly-do-khong-duoc-log-123";

        await fixture.OverrideAsync(ApproveRate(0.3m, reason: secretReason));
        await fixture.OverrideAsync(ApproveRate(0.3m, reason: secretReason));

        var entries = fixture.Logger.Entries;
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(entry => entry.Level == LogLevel.Information);
        entries.Should().OnlyContain(entry => entry.Message.Contains(DecisionId.ToString(), StringComparison.Ordinal)
            && entry.Message.Contains("APPROVE", StringComparison.Ordinal));
        entries.Should().NotContain(entry => entry.Message.Contains(secretReason, StringComparison.Ordinal));
        entries[0].Message.Should().Contain("False");
        entries[1].Message.Should().Contain("True");
    }

    /// <summary>Lỗi lưu trữ tạm thời khi ghi lan ra nguyên vẹn để thành 503.</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task Override_PersistenceUnavailable_Propagates()
    {
        var fixture = new Fixture();
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));
        fixture.Store.FailGovernanceWithPersistenceUnavailable();

        var act = () => fixture.OverrideAsync(ApproveRate());

        await act.Should().ThrowAsync<PersistenceUnavailableException>();
    }
}
