using FluentAssertions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Infrastructure.Persistence;
using TADSS.Infrastructure.Persistence.Entities;
using Xunit;
using OutcomeKind = Shared.DecisionContractSDK.Enums.DecisionOutcome;

namespace TADSS.UnitTests.Infrastructure.Persistence;

/// <summary>
/// UT-01: ánh xạ thuần giữa record của kho audit và entity EF (<see cref="DecisionAuditMapper"/>) — mọi tổ hợp
/// <c>status</c> × <c>executionType</c> × role đi hai chiều trả về đúng bản ghi ban đầu; các cột nullable, chuỗi jsonb,
/// <c>decimal</c> và <c>DateTimeOffset</c> (µs, offset 0) không bị đổi; override/outcome ánh xạ đủ cột.
/// </summary>
public class EfDecisionAuditStoreMappingTests
{
    private static readonly DateTimeOffset EvaluatedAt = new DateTimeOffset(2026, 10, 2, 8, 30, 15, TimeSpan.Zero).AddTicks(1234560);

    public static IEnumerable<object[]> StatusTypeRoleCombinations() =>
        from status in Enum.GetValues<DecisionStatus>()
        from executionType in Enum.GetValues<ExecutionType>()
        from role in Enum.GetValues<ExecutionRole>()
        select new object[] { status, executionType, role };

    public static IEnumerable<object[]> AllOutcomes() =>
        Enum.GetValues<OutcomeKind>().Select(outcome => new object[] { outcome });

    private static DecisionExecutionRecord BuildRecord(DecisionStatus status, ExecutionType executionType, ExecutionRole role) =>
        new(
            DecisionId: Guid.Parse("11111111-1111-4111-8111-111111111111"),
            ReservationId: Guid.Parse("22222222-2222-4222-8222-222222222222"),
            BranchId: Guid.Parse("33333333-3333-4333-8333-333333333333"),
            Role: role,
            StrategyName: "DefaultDecisionStrategy",
            StrategyVersion: "2.0.0",
            StrategyStatus: DecisionStatus.APPROVED,
            Status: status,
            DepositRate: status == DecisionStatus.APPROVED ? 0.3m : null,
            DepositAmount: status == DecisionStatus.APPROVED ? 90000.50m : null,
            ReasonCode: "RULE_APPROVED",
            ExecutionType: executionType,
            ExecutionTimeMs: 17,
            FallbackUsed: executionType == ExecutionType.FALLBACK,
            FailureReason: executionType == ExecutionType.FALLBACK ? "STRATEGY_TIMEOUT" : null,
            CacheHit: executionType == ExecutionType.CACHE_HIT,
            ApprovalExpiresAt: status == DecisionStatus.NEED_APPROVAL ? EvaluatedAt.AddHours(2) : null,
            RequestHash: new string('a', 64),
            RequestPayload: "{\"reservationId\":\"22222222-2222-4222-8222-222222222222\"}",
            ResponsePayload: "{\"status\":\"" + status + "\"}",
            IdempotencyKey: "idem-key-1",
            CorrelationId: "corr-1",
            CreatedAt: EvaluatedAt);

    [Theory]
    [MemberData(nameof(StatusTypeRoleCombinations))]
    [Trait("CatalogId", "UT-01")]
    public void Map_RecordToEntityAndBack_RoundTrips_Theory(DecisionStatus status, ExecutionType executionType, ExecutionRole role)
    {
        var record = BuildRecord(status, executionType, role);

        var roundTripped = DecisionAuditMapper.ToRecord(DecisionAuditMapper.ToEntity(record));

        roundTripped.Should().Be(record);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Map_RecordToEntity_CopiesEveryColumnAndLeavesTechnicalKeyEmpty()
    {
        var record = BuildRecord(DecisionStatus.NEED_APPROVAL, ExecutionType.FALLBACK, ExecutionRole.SHADOW);

        var entity = DecisionAuditMapper.ToEntity(record);

        entity.Id.Should().BeEmpty("khóa kỹ thuật do cơ sở dữ liệu sinh (gen_random_uuid())");
        entity.DecisionId.Should().Be(record.DecisionId);
        entity.ReservationId.Should().Be(record.ReservationId);
        entity.BranchId.Should().Be(record.BranchId);
        entity.Role.Should().Be(ExecutionRole.SHADOW);
        entity.StrategyName.Should().Be(record.StrategyName);
        entity.StrategyVersion.Should().Be(record.StrategyVersion);
        entity.StrategyStatus.Should().Be(record.StrategyStatus);
        entity.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        entity.ReasonCode.Should().Be(record.ReasonCode);
        entity.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        entity.ExecutionTimeMs.Should().Be(17);
        entity.FallbackUsed.Should().BeTrue();
        entity.FailureReason.Should().Be("STRATEGY_TIMEOUT");
        entity.CacheHit.Should().BeFalse();
        entity.ApprovalExpiresAt.Should().Be(record.ApprovalExpiresAt);
        entity.RequestHash.Should().Be(record.RequestHash);
        entity.IdempotencyKey.Should().Be("idem-key-1");
        entity.CorrelationId.Should().Be("corr-1");
        entity.CreatedAt.Should().Be(EvaluatedAt);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Map_NullableColumnsAbsent_StayNullInBothDirections()
    {
        var record = BuildRecord(DecisionStatus.REJECTED, ExecutionType.AUTOMATED, ExecutionRole.PRIMARY) with
        {
            IdempotencyKey = null,
            CorrelationId = null,
        };

        var entity = DecisionAuditMapper.ToEntity(record);
        var back = DecisionAuditMapper.ToRecord(entity);

        entity.DepositRate.Should().BeNull();
        entity.DepositAmount.Should().BeNull();
        entity.ApprovalExpiresAt.Should().BeNull();
        entity.FailureReason.Should().BeNull();
        entity.IdempotencyKey.Should().BeNull();
        entity.CorrelationId.Should().BeNull();
        back.DepositRate.Should().BeNull();
        back.DepositAmount.Should().BeNull();
        back.ApprovalExpiresAt.Should().BeNull();
        back.FailureReason.Should().BeNull();
        back.IdempotencyKey.Should().BeNull();
        back.CorrelationId.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Map_JsonbPayloads_PreservedByteForByte()
    {
        // Chuỗi cố ý không "đẹp": khoảng trắng, thứ tự khóa, Unicode, số giữ nguyên chữ số thập phân
        const string request = "{ \"b\" : 1,\"a\":\"Đặt sân cầu lông\",  \"rate\":0.50000 }";
        const string response = "{\"z\":[1, 2,3],\"escaped\":\"\\u00e9\"}";
        var record = BuildRecord(DecisionStatus.APPROVED, ExecutionType.AUTOMATED, ExecutionRole.PRIMARY) with
        {
            RequestPayload = request,
            ResponsePayload = response,
        };

        var entity = DecisionAuditMapper.ToEntity(record);
        var back = DecisionAuditMapper.ToRecord(entity);

        entity.RequestPayload.Should().Be(request);
        entity.ResponsePayload.Should().Be(response);
        back.RequestPayload.Should().Be(request);
        back.ResponsePayload.Should().Be(response);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Map_DecimalAndMicrosecondValues_PreservedExactly()
    {
        var record = BuildRecord(DecisionStatus.APPROVED, ExecutionType.AUTOMATED, ExecutionRole.PRIMARY) with
        {
            DepositRate = 0.1234m,
            DepositAmount = 12345678901.99m,
            CreatedAt = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999990),
        };

        var back = DecisionAuditMapper.ToRecord(DecisionAuditMapper.ToEntity(record));

        back.DepositRate.Should().Be(0.1234m);
        back.DepositAmount.Should().Be(12345678901.99m);
        back.CreatedAt.Ticks.Should().Be(record.CreatedAt.Ticks);
        back.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(OverrideAction.APPROVE, 0.5, 150000.0)]
    [InlineData(OverrideAction.REJECT, null, null)]
    [Trait("CatalogId", "UT-01")]
    public void MapOverride_EntityToRecord_CopiesEveryColumn(OverrideAction action, double? rate, double? amount)
    {
        var entity = new DecisionOverrideEntity
        {
            Id = Guid.NewGuid(),
            DecisionId = Guid.NewGuid(),
            Action = action,
            DepositRate = rate is null ? null : (decimal)rate,
            DepositAmount = amount is null ? null : (decimal)amount,
            ActorId = "manager-01",
            Reason = "Khách quen, duyệt cọc 50%",
            IdempotencyKey = "override-key-1",
            CreatedAt = EvaluatedAt,
        };

        var record = DecisionAuditMapper.ToRecord(entity);

        record.Should().Be(new DecisionOverrideRecord(
            entity.DecisionId,
            action,
            entity.DepositRate,
            entity.DepositAmount,
            "manager-01",
            "Khách quen, duyệt cọc 50%",
            "override-key-1",
            EvaluatedAt));
    }

    [Theory]
    [MemberData(nameof(AllOutcomes))]
    [Trait("CatalogId", "UT-01")]
    public void MapOutcome_EntityToRecord_CopiesEveryColumn(OutcomeKind outcome)
    {
        var occurredAt = EvaluatedAt.AddDays(1);
        var reportedAt = occurredAt.AddMinutes(5);
        var entity = new DecisionOutcomeEntity
        {
            Id = Guid.NewGuid(),
            DecisionId = Guid.NewGuid(),
            Outcome = outcome,
            OccurredAt = occurredAt,
            ReportedAt = reportedAt,
        };

        var record = DecisionAuditMapper.ToRecord(entity);

        record.Should().Be(new DecisionOutcomeRecord(entity.DecisionId, outcome, occurredAt, reportedAt));
    }

    [Theory]
    [InlineData(OverrideAction.APPROVE, 0.5, 150000.0)]
    [InlineData(OverrideAction.REJECT, null, null)]
    [Trait("CatalogId", "UT-01")]
    public void MapOverride_RecordToEntityAndBack_RoundTripsAndLeavesTechnicalKeyEmpty(OverrideAction action, double? rate, double? amount)
    {
        var record = new DecisionOverrideRecord(
            Guid.NewGuid(),
            action,
            rate is null ? null : (decimal)rate,
            amount is null ? null : (decimal)amount,
            "manager-01",
            "Khách quen, duyệt cọc 50%",
            "override-key-1",
            EvaluatedAt);

        var entity = DecisionAuditMapper.ToEntity(record);

        entity.Id.Should().BeEmpty("khóa kỹ thuật do cơ sở dữ liệu sinh");
        entity.DecisionId.Should().Be(record.DecisionId);
        DecisionAuditMapper.ToRecord(entity).Should().Be(record);
    }

    [Theory]
    [MemberData(nameof(AllOutcomes))]
    [Trait("CatalogId", "UT-01")]
    public void MapOutcome_RecordToEntityAndBack_RoundTripsAndLeavesTechnicalKeyEmpty(OutcomeKind outcome)
    {
        var record = new DecisionOutcomeRecord(Guid.NewGuid(), outcome, EvaluatedAt.AddDays(1), EvaluatedAt.AddDays(1).AddMinutes(5));

        var entity = DecisionAuditMapper.ToEntity(record);

        entity.Id.Should().BeEmpty("khóa kỹ thuật do cơ sở dữ liệu sinh");
        entity.DecisionId.Should().Be(record.DecisionId);
        DecisionAuditMapper.ToRecord(entity).Should().Be(record);
    }
}
