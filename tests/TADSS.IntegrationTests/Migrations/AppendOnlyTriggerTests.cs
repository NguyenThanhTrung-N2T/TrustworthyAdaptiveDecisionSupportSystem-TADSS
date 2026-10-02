using FluentAssertions;
using Npgsql;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests.Migrations;

/// <summary>
/// IT-05 (INV-4): ba bảng audit v2 là append-only — mọi <c>UPDATE</c>, <c>DELETE</c> và <c>TRUNCATE</c> chạy bằng SQL trực tiếp
/// đều bị trigger <c>tadss.reject_mutation()</c> từ chối với SqlState <c>P0001</c>, và dữ liệu không đổi.
/// Database của mỗi test được nhân bản từ mẫu đã migrate nên không cần (và không được) dọn bảng audit bằng DELETE/TRUNCATE.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class AppendOnlyTriggerTests : IntegrationTestBase
{
    private const string RaiseExceptionSqlState = "P0001";

    public AppendOnlyTriggerTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    [Theory]
    [Trait("CatalogId", "IT-05")]
    [InlineData(AuditSchemaSql.Executions, "UPDATE tadss.decision_executions SET reason_code = 'CHANGED'")]
    [InlineData(AuditSchemaSql.Executions, "DELETE FROM tadss.decision_executions")]
    [InlineData(AuditSchemaSql.Executions, "TRUNCATE TABLE tadss.decision_executions CASCADE")]
    [InlineData(AuditSchemaSql.Overrides, "UPDATE tadss.decision_overrides SET reason = 'Noi dung da bi sua'")]
    [InlineData(AuditSchemaSql.Overrides, "DELETE FROM tadss.decision_overrides")]
    [InlineData(AuditSchemaSql.Overrides, "TRUNCATE TABLE tadss.decision_overrides")]
    [InlineData(AuditSchemaSql.Outcomes, "UPDATE tadss.decision_outcomes SET outcome = 'NO_SHOW'")]
    [InlineData(AuditSchemaSql.Outcomes, "DELETE FROM tadss.decision_outcomes")]
    [InlineData(AuditSchemaSql.Outcomes, "TRUNCATE TABLE tadss.decision_outcomes")]
    public async Task AppendOnly_UpdateDeleteTruncateOnAnyAuditTable_Rejected_Theory(string table, string statement)
    {
        await SeedAllTablesAsync();
        var before = await AuditSchemaSql.TableContentAsync(ConnectionString, table);

        var act = () => AuditSchemaSql.ExecuteAsync(ConnectionString, statement);

        var exception = (await act.Should().ThrowAsync<PostgresException>()).Which;
        exception.SqlState.Should().Be(RaiseExceptionSqlState);
        exception.MessageText.Should().Contain("append-only");
        (await AuditSchemaSql.TableContentAsync(ConnectionString, table)).Should().Be(before, "dữ liệu không được đổi");
        (await AuditSchemaSql.ScalarAsync<int>(ConnectionString, $"SELECT count(*) FROM {table}")).Should().Be(1);
    }

    [Fact]
    [Trait("CatalogId", "IT-05")]
    public async Task AppendOnly_TruncateExecutionsWithoutCascade_Rejected()
    {
        await SeedAllTablesAsync();

        var act = () => AuditSchemaSql.ExecuteAsync(ConnectionString, "TRUNCATE TABLE tadss.decision_executions");

        // Bị chặn dù không CASCADE: bảng đang được override/outcome tham chiếu nên PostgreSQL từ chối trước cả trigger
        await act.Should().ThrowAsync<PostgresException>();
        (await AuditSchemaSql.ScalarAsync<int>(ConnectionString, $"SELECT count(*) FROM {AuditSchemaSql.Executions}")).Should().Be(1);
    }

    [Fact]
    [Trait("CatalogId", "IT-05")]
    public async Task AppendOnly_InsertIntoAuditTables_Allowed()
    {
        var decisionId = await AuditSchemaSql.InsertValidExecutionAsync(ConnectionString);
        await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Overrides, AuditSchemaSql.ValidOverride(decisionId));
        await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Outcomes, AuditSchemaSql.ValidOutcome(decisionId));

        foreach (var table in new[] { AuditSchemaSql.Executions, AuditSchemaSql.Overrides, AuditSchemaSql.Outcomes })
        {
            (await AuditSchemaSql.ScalarAsync<int>(ConnectionString, $"SELECT count(*) FROM {table}")).Should().Be(1);
        }
    }

    private async Task SeedAllTablesAsync()
    {
        var decisionId = await AuditSchemaSql.InsertValidExecutionAsync(ConnectionString);
        await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Overrides, AuditSchemaSql.ValidOverride(decisionId));
        await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Outcomes, AuditSchemaSql.ValidOutcome(decisionId));
    }
}
