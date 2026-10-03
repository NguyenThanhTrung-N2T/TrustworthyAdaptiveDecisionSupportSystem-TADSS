using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.Infrastructure.Persistence.Entities;
using Xunit;

namespace TADSS.IntegrationTests.Migrations;

/// <summary>
/// IT-05 (INV-1, INV-2, INV-5..8): ràng buộc của schema audit v2 trên PostgreSQL thật — mỗi <c>CHECK</c> từ chối dòng sai
/// (23514), khóa ngoại và unique từ chối dòng mồ côi/trùng (23503/23505), index idempotency chỉ ràng buộc bản PRIMARY,
/// và model EF ghi/đọc lại đủ cột.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class SchemaConstraintTests : IntegrationTestBase
{
    private const string CheckViolation = "23514";
    private const string ForeignKeyViolation = "23503";
    private const string UniqueViolation = "23505";

    /// <summary>Mỗi trường hợp: bảng, các cột bị sửa so với dòng hợp lệ, và tên CHECK mà PostgreSQL phải báo.</summary>
    private static readonly Dictionary<string, (string Table, (string Column, object? Value)[] Changes, string Constraint)> InvalidRows = new()
    {
        ["approved_without_deposit"] = Case(AuditSchemaSql.Executions, "ck_de_deposit", ("deposit_rate", null), ("deposit_amount", null)),
        ["approved_with_only_rate"] = Case(AuditSchemaSql.Executions, "ck_de_deposit", ("deposit_amount", null)),
        ["rejected_with_deposit"] = Case(AuditSchemaSql.Executions, "ck_de_deposit", ("status", "REJECTED")),
        ["need_approval_without_expiry"] = Case(AuditSchemaSql.Executions, "ck_de_approval", ("status", "NEED_APPROVAL"), ("deposit_rate", null), ("deposit_amount", null)),
        ["approved_with_expiry"] = Case(AuditSchemaSql.Executions, "ck_de_approval", ("approval_expires_at", new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero))),
        ["unknown_role"] = Case(AuditSchemaSql.Executions, "ck_de_role", ("role", "OBSERVER")),
        ["unknown_status"] = Case(AuditSchemaSql.Executions, "ck_de_status", ("status", "PENDING"), ("deposit_rate", null), ("deposit_amount", null)),
        ["deposit_rate_above_one"] = Case(AuditSchemaSql.Executions, "ck_de_deposit_rate", ("deposit_rate", 1.5m)),
        ["deposit_rate_negative"] = Case(AuditSchemaSql.Executions, "ck_de_deposit_rate", ("deposit_rate", -0.1m)),
        ["deposit_amount_negative"] = Case(AuditSchemaSql.Executions, "ck_de_deposit_amount", ("deposit_amount", -1m)),
        ["unknown_execution_type"] = Case(AuditSchemaSql.Executions, "ck_de_execution_type", ("execution_type", "MANUAL")),
        ["negative_execution_time"] = Case(AuditSchemaSql.Executions, "ck_de_execution_time_ms", ("execution_time_ms", -1)),
        ["override_unknown_action"] = Case(AuditSchemaSql.Overrides, "ck_do_action", ("action", "DEFER")),
        ["override_approve_without_deposit"] = Case(AuditSchemaSql.Overrides, "ck_do_deposit", ("deposit_rate", null), ("deposit_amount", null)),
        ["override_reject_with_deposit"] = Case(AuditSchemaSql.Overrides, "ck_do_deposit", ("action", "REJECT")),
        ["override_rate_above_one"] = Case(AuditSchemaSql.Overrides, "ck_do_deposit_rate", ("deposit_rate", 1.2m)),
        ["override_negative_amount"] = Case(AuditSchemaSql.Overrides, "ck_do_deposit_amount", ("deposit_amount", -5m)),
        ["override_reason_too_short"] = Case(AuditSchemaSql.Overrides, "ck_do_reason_length", ("reason", "abcd")),
        ["override_reason_too_long"] = Case(AuditSchemaSql.Overrides, "ck_do_reason_length", ("reason", new string('x', 501))),
        ["outcome_unknown_value"] = Case(AuditSchemaSql.Outcomes, "ck_dout_outcome", ("outcome", "LOST")),
    };

    private static (string Table, (string Column, object? Value)[] Changes, string Constraint) Case(
        string table, string constraint, params (string Column, object? Value)[] changes) => (table, changes, constraint);

    public SchemaConstraintTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    [Theory]
    [Trait("CatalogId", "IT-05")]
    [InlineData("approved_without_deposit")]
    [InlineData("approved_with_only_rate")]
    [InlineData("rejected_with_deposit")]
    [InlineData("need_approval_without_expiry")]
    [InlineData("approved_with_expiry")]
    [InlineData("unknown_role")]
    [InlineData("unknown_status")]
    [InlineData("deposit_rate_above_one")]
    [InlineData("deposit_rate_negative")]
    [InlineData("deposit_amount_negative")]
    [InlineData("unknown_execution_type")]
    [InlineData("negative_execution_time")]
    [InlineData("override_unknown_action")]
    [InlineData("override_approve_without_deposit")]
    [InlineData("override_reject_with_deposit")]
    [InlineData("override_rate_above_one")]
    [InlineData("override_negative_amount")]
    [InlineData("override_reason_too_short")]
    [InlineData("override_reason_too_long")]
    [InlineData("outcome_unknown_value")]
    public async Task Constraints_InvalidRows_Rejected_Theory(string caseName)
    {
        var (table, changes, constraint) = InvalidRows[caseName];
        var row = await BuildRowAsync(table);
        foreach (var (column, value) in changes)
        {
            row[column] = value;
        }

        var act = () => AuditSchemaSql.InsertAsync(ConnectionString, table, row);

        var exception = (await act.Should().ThrowAsync<PostgresException>()).Which;
        exception.SqlState.Should().Be(CheckViolation);
        exception.ConstraintName.Should().Be(constraint);
        (await AuditSchemaSql.ScalarAsync<int>(ConnectionString, $"SELECT count(*) FROM {table}"))
            .Should().Be(0, "dòng sai không được lưu");
    }

    [Theory]
    [Trait("CatalogId", "IT-05")]
    [InlineData(AuditSchemaSql.Overrides)]
    [InlineData(AuditSchemaSql.Outcomes)]
    public async Task ForeignKey_OverrideOrOutcomeForUnknownDecision_Rejected_Theory(string table)
    {
        var orphanDecisionId = Guid.NewGuid();
        var row = table == AuditSchemaSql.Overrides
            ? AuditSchemaSql.ValidOverride(orphanDecisionId)
            : AuditSchemaSql.ValidOutcome(orphanDecisionId);

        var act = () => AuditSchemaSql.InsertAsync(ConnectionString, table, row);

        var exception = (await act.Should().ThrowAsync<PostgresException>()).Which;
        exception.SqlState.Should().Be(ForeignKeyViolation);
    }

    [Theory]
    [Trait("CatalogId", "IT-05")]
    [InlineData(AuditSchemaSql.Executions, "uq_decision_executions_decision_id")]
    [InlineData(AuditSchemaSql.Overrides, "uq_decision_overrides_decision_id")]
    [InlineData(AuditSchemaSql.Outcomes, "uq_decision_outcomes_decision_id")]
    public async Task UniqueDecisionId_DuplicateRow_Rejected_Theory(string table, string constraint)
    {
        var decisionId = await AuditSchemaSql.InsertValidExecutionAsync(ConnectionString);
        if (table == AuditSchemaSql.Overrides)
        {
            await AuditSchemaSql.InsertAsync(ConnectionString, table, AuditSchemaSql.ValidOverride(decisionId));
        }
        else if (table == AuditSchemaSql.Outcomes)
        {
            await AuditSchemaSql.InsertAsync(ConnectionString, table, AuditSchemaSql.ValidOutcome(decisionId));
        }

        var duplicate = table switch
        {
            AuditSchemaSql.Overrides => AuditSchemaSql.ValidOverride(decisionId),
            AuditSchemaSql.Outcomes => AuditSchemaSql.ValidOutcome(decisionId),
            _ => AuditSchemaSql.ValidExecution(decisionId),
        };

        var act = () => AuditSchemaSql.InsertAsync(ConnectionString, table, duplicate);

        var exception = (await act.Should().ThrowAsync<PostgresException>()).Which;
        exception.SqlState.Should().Be(UniqueViolation);
        exception.ConstraintName.Should().Be(constraint);
    }

    [Fact]
    [Trait("CatalogId", "IT-05")]
    public async Task IdempotencyIndex_PrimaryUniqueButShadowAllowed()
    {
        const string key = "idem-key-001";
        var primary = AuditSchemaSql.ValidExecution();
        primary["idempotency_key"] = key;
        await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Executions, primary);

        var secondPrimary = AuditSchemaSql.ValidExecution();
        secondPrimary["idempotency_key"] = key;
        var duplicatePrimary = () => AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Executions, secondPrimary);

        var exception = (await duplicatePrimary.Should().ThrowAsync<PostgresException>()).Which;
        exception.SqlState.Should().Be(UniqueViolation);
        exception.ConstraintName.Should().Be("uq_decision_idempotency_key");

        // Bản SHADOW cùng khóa được phép, kể cả nhiều bản SHADOW
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var shadow = AuditSchemaSql.ValidExecution();
            shadow["role"] = "SHADOW";
            shadow["idempotency_key"] = key;
            await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Executions, shadow);
        }

        (await AuditSchemaSql.ScalarAsync<int>(ConnectionString,
            $"SELECT count(*) FROM {AuditSchemaSql.Executions} WHERE idempotency_key = '{key}'")).Should().Be(3);
    }

    [Fact]
    [Trait("CatalogId", "IT-05")]
    public async Task EfCore_InsertValidAuditRows_PersistsAndReadsBackAllColumns()
    {
        var decisionId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 10, 2, 8, 30, 15, 123, TimeSpan.Zero);
        var expiresAt = createdAt.AddHours(2);
        var execution = new DecisionExecutionEntity
        {
            DecisionId = decisionId,
            ReservationId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            Role = ExecutionRole.PRIMARY,
            StrategyName = "DefaultDecisionStrategy",
            StrategyVersion = "1.0.0",
            StrategyStatus = DecisionStatus.NEED_APPROVAL,
            Status = DecisionStatus.NEED_APPROVAL,
            ReasonCode = "HIGH_RISK",
            ExecutionType = ExecutionType.FALLBACK,
            ExecutionTimeMs = 480,
            FallbackUsed = true,
            FailureReason = "TIMEOUT",
            CacheHit = false,
            ApprovalExpiresAt = expiresAt,
            RequestHash = new string('b', 64),
            RequestPayload = """{"reservationId":"x"}""",
            ResponsePayload = """{"status":"NEED_APPROVAL"}""",
            IdempotencyKey = "ef-key",
            CorrelationId = "corr-1",
            CreatedAt = createdAt,
        };
        var decisionOverride = new DecisionOverrideEntity
        {
            DecisionId = decisionId,
            Action = OverrideAction.APPROVE,
            DepositRate = 0.4m,
            DepositAmount = 120000.50m,
            ActorId = "manager-01",
            Reason = "Khach quen",
            IdempotencyKey = "override-key",
            CreatedAt = createdAt.AddMinutes(5),
        };
        var outcome = new DecisionOutcomeEntity
        {
            DecisionId = decisionId,
            Outcome = Shared.DecisionContractSDK.Enums.DecisionOutcome.NO_SHOW,
            OccurredAt = createdAt.AddDays(1),
            ReportedAt = createdAt.AddDays(1).AddMinutes(10),
        };

        await using (var writer = CreateDbContext())
        {
            writer.DecisionExecutions.Add(execution);
            await writer.SaveChangesAsync();
            writer.DecisionOverrides.Add(decisionOverride);
            writer.DecisionOutcomes.Add(outcome);
            await writer.SaveChangesAsync();
        }

        await using var reader = CreateDbContext();
        var storedExecution = await reader.DecisionExecutions.AsNoTracking().SingleAsync();
        storedExecution.Should().BeEquivalentTo(execution, options => options
            .Excluding(e => e.Id)
            .Excluding(e => e.RequestPayload)
            .Excluding(e => e.ResponsePayload));
        storedExecution.Id.Should().NotBeEmpty();
        storedExecution.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        storedExecution.RequestPayload.Should().Contain("reservationId");
        storedExecution.ResponsePayload.Should().Contain("NEED_APPROVAL");

        (await reader.DecisionOverrides.AsNoTracking().SingleAsync()).Should().BeEquivalentTo(decisionOverride, o => o.Excluding(x => x.Id));
        (await reader.DecisionOutcomes.AsNoTracking().SingleAsync()).Should().BeEquivalentTo(outcome, o => o.Excluding(x => x.Id));

        // Enum được lưu dạng chuỗi tên, đúng với CHECK trong schema
        (await AuditSchemaSql.ScalarAsync<string>(ConnectionString,
            $"SELECT role || '/' || status || '/' || execution_type FROM {AuditSchemaSql.Executions}"))
            .Should().Be("PRIMARY/NEED_APPROVAL/FALLBACK");
    }

    private async Task<Dictionary<string, object?>> BuildRowAsync(string table)
    {
        if (table == AuditSchemaSql.Executions)
        {
            return AuditSchemaSql.ValidExecution();
        }

        var decisionId = await AuditSchemaSql.InsertValidExecutionAsync(ConnectionString);
        return table == AuditSchemaSql.Overrides
            ? AuditSchemaSql.ValidOverride(decisionId)
            : AuditSchemaSql.ValidOutcome(decisionId);
    }
}
