using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TADSS.Infrastructure.Persistence;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Persistence;

/// <summary>
/// UT-01: model EF Core của ba bảng audit v2 khớp DECISION_PERSISTENCE_INVARIANTS B.1–B.3 — cột, kiểu, độ dài,
/// nullability, CHECK, unique, khóa ngoại và index. Chỉ đọc relational model từ Npgsql provider với chuỗi kết nối giả
/// (không mở kết nối, không dùng EF InMemory).
/// </summary>
public class DecisionPersistenceModelTests
{
    private const string Schema = "tadss";
    private const string Executions = "decision_executions";
    private const string Overrides = "decision_overrides";
    private const string Outcomes = "decision_outcomes";

    private static readonly IRelationalModel RelationalModel = BuildRelationalModel();

    private static IRelationalModel BuildRelationalModel()
    {
        var options = new DbContextOptionsBuilder<TadssDbContext>()
            .UseNpgsql("Host=localhost;Database=none")
            .Options;
        using var context = new TadssDbContext(options);
        return context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
    }

    private static ITable Table(string name) =>
        RelationalModel.FindTable(name, Schema) ?? throw new InvalidOperationException($"Model không có bảng {Schema}.{name}.");

    private static string[] PrimaryKeyColumns(ITable table) =>
        table.PrimaryKey is { } primaryKey ? primaryKey.Columns.Select(column => column.Name).ToArray() : [];

    private static string Describe(IColumn column) => $"{column.Name}|{column.StoreType}|{(column.IsNullable ? "null" : "not null")}";

    private static readonly string[] ExecutionColumns =
    [
        "id|uuid|not null",
        "decision_id|uuid|not null",
        "reservation_id|uuid|not null",
        "branch_id|uuid|not null",
        "role|character varying(10)|not null",
        "strategy_name|character varying(100)|not null",
        "strategy_version|character varying(20)|not null",
        "strategy_status|character varying(20)|not null",
        "status|character varying(20)|not null",
        "deposit_rate|numeric(5,4)|null",
        "deposit_amount|numeric(14,2)|null",
        "reason_code|character varying(64)|not null",
        "execution_type|character varying(20)|not null",
        "execution_time_ms|integer|not null",
        "fallback_used|boolean|not null",
        "failure_reason|text|null",
        "cache_hit|boolean|not null",
        "approval_expires_at|timestamp with time zone|null",
        "request_hash|character(64)|not null",
        "request_payload|jsonb|not null",
        "response_payload|jsonb|not null",
        "idempotency_key|character varying(128)|null",
        "correlation_id|character varying(128)|null",
        "created_at|timestamp with time zone|not null",
    ];

    private static readonly string[] OverrideColumns =
    [
        "id|uuid|not null",
        "decision_id|uuid|not null",
        "action|character varying(10)|not null",
        "deposit_rate|numeric(5,4)|null",
        "deposit_amount|numeric(14,2)|null",
        "actor_id|character varying(100)|not null",
        "reason|text|not null",
        "idempotency_key|character varying(128)|not null",
        "created_at|timestamp with time zone|not null",
    ];

    private static readonly string[] OutcomeColumns =
    [
        "id|uuid|not null",
        "decision_id|uuid|not null",
        "outcome|character varying(40)|not null",
        "occurred_at|timestamp with time zone|not null",
        "reported_at|timestamp with time zone|not null",
    ];

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Model_DecisionExecutions_MatchesPersistenceInvariantsColumns()
    {
        var table = Table(Executions);

        table.Columns.Select(Describe).Should().BeEquivalentTo(ExecutionColumns);
        table.Columns.Should().HaveCount(24, "23 cột nghiệp vụ của B.1 cộng khóa kỹ thuật id");
        table.Columns.Select(column => column.Name).Should().NotContain(["updated_at", "version", "is_overridden"],
            "bảng append-only không có cột có thể sửa");
        table.Columns.Single(column => column.Name == "id").DefaultValueSql.Should().Be("gen_random_uuid()");
        PrimaryKeyColumns(table).Should().Equal("id");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Model_DecisionOverrides_MatchesPersistenceInvariantsColumns()
    {
        var table = Table(Overrides);

        table.Columns.Select(Describe).Should().BeEquivalentTo(OverrideColumns);
        table.Columns.Single(column => column.Name == "id").DefaultValueSql.Should().Be("gen_random_uuid()");
        PrimaryKeyColumns(table).Should().Equal("id");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Model_DecisionOutcomes_MatchesPersistenceInvariantsColumns()
    {
        var table = Table(Outcomes);

        table.Columns.Select(Describe).Should().BeEquivalentTo(OutcomeColumns);
        table.Columns.Single(column => column.Name == "id").DefaultValueSql.Should().Be("gen_random_uuid()");
        PrimaryKeyColumns(table).Should().Equal("id");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Model_DecisionExecutionsIndexes_MatchB1()
    {
        var indexes = Table(Executions).Indexes.ToDictionary(index => index.Name);

        indexes.Keys.Should().BeEquivalentTo(
            "uq_decision_idempotency_key", "idx_de_reservation_created", "idx_de_pending", "idx_de_request_hash", "idx_de_created_at");

        var idempotency = indexes["uq_decision_idempotency_key"];
        idempotency.IsUnique.Should().BeTrue();
        idempotency.Columns.Select(column => column.Name).Should().Equal("idempotency_key");
        idempotency.Filter.Should().Be("idempotency_key IS NOT NULL AND role = 'PRIMARY'");

        var reservation = indexes["idx_de_reservation_created"];
        reservation.IsUnique.Should().BeFalse();
        reservation.Columns.Select(column => column.Name).Should().Equal("reservation_id", "created_at");
        reservation.MappedIndexes.Single().IsDescending.Should().Equal(false, true);

        var pending = indexes["idx_de_pending"];
        pending.Columns.Select(column => column.Name).Should().Equal("branch_id", "created_at");
        pending.Filter.Should().Be("status = 'NEED_APPROVAL' AND role = 'PRIMARY'");

        indexes["idx_de_request_hash"].Columns.Select(column => column.Name).Should().Equal("request_hash");
        indexes["idx_de_created_at"].Columns.Select(column => column.Name).Should().Equal("created_at");
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(Executions, "check:ck_de_role:role IN ('PRIMARY', 'SHADOW')")]
    [InlineData(Executions, "check:ck_de_status:status IN ('APPROVED', 'NEED_APPROVAL', 'REJECTED')")]
    [InlineData(Executions, "check:ck_de_deposit_rate:deposit_rate BETWEEN 0 AND 1")]
    [InlineData(Executions, "check:ck_de_deposit_amount:deposit_amount >= 0")]
    [InlineData(Executions, "check:ck_de_execution_type:execution_type IN ('AUTOMATED', 'FALLBACK', 'CACHE_HIT')")]
    [InlineData(Executions, "check:ck_de_execution_time_ms:execution_time_ms >= 0")]
    [InlineData(Executions, "check:ck_de_deposit:(status = 'APPROVED') = (deposit_rate IS NOT NULL AND deposit_amount IS NOT NULL)")]
    [InlineData(Executions, "check:ck_de_approval:(status = 'NEED_APPROVAL') = (approval_expires_at IS NOT NULL)")]
    [InlineData(Executions, "unique:uq_decision_executions_decision_id:decision_id")]
    [InlineData(Overrides, "check:ck_do_action:action IN ('APPROVE', 'REJECT')")]
    [InlineData(Overrides, "check:ck_do_deposit_rate:deposit_rate BETWEEN 0 AND 1")]
    [InlineData(Overrides, "check:ck_do_deposit_amount:deposit_amount >= 0")]
    [InlineData(Overrides, "check:ck_do_reason_length:char_length(reason) BETWEEN 5 AND 500")]
    [InlineData(Overrides, "check:ck_do_deposit:(action = 'APPROVE') = (deposit_rate IS NOT NULL AND deposit_amount IS NOT NULL)")]
    [InlineData(Overrides, "unique-index:uq_decision_overrides_decision_id:decision_id")]
    [InlineData(Overrides, "fk:fk_decision_overrides_decision_id:decision_id->decision_executions(decision_id):NoAction")]
    [InlineData(Outcomes, "check:ck_dout_outcome:outcome IN ('COMPLETED', 'NO_SHOW', 'CANCELLED_BY_CUSTOMER', 'CANCELLED_BY_STAFF', 'PAYMENT_EXPIRED', 'APPROVAL_EXPIRED', 'REJECTED')")]
    [InlineData(Outcomes, "unique-index:uq_decision_outcomes_decision_id:decision_id")]
    [InlineData(Outcomes, "fk:fk_decision_outcomes_decision_id:decision_id->decision_executions(decision_id):NoAction")]
    public void Model_CheckConstraintsAndUniqueIndexes_Declared_Theory(string tableName, string expected)
    {
        var table = Table(tableName);

        DescribeConstraints(table).Should().Contain(expected);
    }

    private static IEnumerable<string> DescribeConstraints(ITable table)
    {
        foreach (var check in table.CheckConstraints)
        {
            yield return $"check:{check.Name}:{check.Sql}";
        }

        foreach (var unique in table.UniqueConstraints.Where(constraint => !constraint.GetIsPrimaryKey()))
        {
            yield return $"unique:{unique.Name}:{string.Join(",", unique.Columns.Select(column => column.Name))}";
        }

        foreach (var index in table.Indexes.Where(index => index.IsUnique))
        {
            yield return $"unique-index:{index.Name}:{string.Join(",", index.Columns.Select(column => column.Name))}";
        }

        foreach (var foreignKey in table.ForeignKeyConstraints)
        {
            yield return $"fk:{foreignKey.Name}:{string.Join(",", foreignKey.Columns.Select(column => column.Name))}"
                + $"->{foreignKey.PrincipalTable.Name}({string.Join(",", foreignKey.PrincipalColumns.Select(column => column.Name))})"
                + $":{foreignKey.OnDeleteAction}";
        }
    }
}
