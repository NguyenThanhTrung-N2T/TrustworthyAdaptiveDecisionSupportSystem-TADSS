using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TADSS.Runtime.Api.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Migration v2 (D-05): tạo lại schema audit theo DECISION_PERSISTENCE_INVARIANTS B.1–B.4 — bỏ bảng v1
    /// <c>decision_executions</c> (dữ liệu v1 chỉ là dữ liệu kiểm thử), dựng bảng v2 cùng tên, thêm
    /// <c>decision_overrides</c>, <c>decision_outcomes</c>, hàm <c>tadss.reject_mutation()</c> và 6 trigger append-only.
    /// Mã sinh tự động được chỉnh tay: <c>DropTable</c> tường minh thay cho chuỗi Alter/Drop từng cột, và phần SQL hàm/trigger.
    /// </summary>
    public partial class DecisionPersistenceV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Bỏ bảng v1 (kéo theo toàn bộ index của nó)
            migrationBuilder.DropTable(
                name: "decision_executions",
                schema: "tadss");

            // 2. Bảng v2 theo thứ tự an toàn khóa ngoại: decision_executions trước, override/outcome sau
            migrationBuilder.CreateTable(
                name: "decision_executions",
                schema: "tadss",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    strategy_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    strategy_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    strategy_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    deposit_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    deposit_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    execution_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    execution_time_ms = table.Column<int>(type: "integer", nullable: false),
                    fallback_used = table.Column<bool>(type: "boolean", nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    cache_hit = table.Column<bool>(type: "boolean", nullable: false),
                    approval_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    request_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    request_payload = table.Column<string>(type: "jsonb", nullable: false),
                    response_payload = table.Column<string>(type: "jsonb", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decision_executions", x => x.id);
                    table.UniqueConstraint("uq_decision_executions_decision_id", x => x.decision_id);
                    table.CheckConstraint("ck_de_approval", "(status = 'NEED_APPROVAL') = (approval_expires_at IS NOT NULL)");
                    table.CheckConstraint("ck_de_deposit", "(status = 'APPROVED') = (deposit_rate IS NOT NULL AND deposit_amount IS NOT NULL)");
                    table.CheckConstraint("ck_de_deposit_amount", "deposit_amount >= 0");
                    table.CheckConstraint("ck_de_deposit_rate", "deposit_rate BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_de_execution_time_ms", "execution_time_ms >= 0");
                    table.CheckConstraint("ck_de_execution_type", "execution_type IN ('AUTOMATED', 'FALLBACK', 'CACHE_HIT')");
                    table.CheckConstraint("ck_de_role", "role IN ('PRIMARY', 'SHADOW')");
                    table.CheckConstraint("ck_de_status", "status IN ('APPROVED', 'NEED_APPROVAL', 'REJECTED')");
                });

            migrationBuilder.CreateTable(
                name: "decision_outcomes",
                schema: "tadss",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decision_outcomes", x => x.id);
                    table.CheckConstraint("ck_dout_outcome", "outcome IN ('COMPLETED', 'NO_SHOW', 'CANCELLED_BY_CUSTOMER', 'CANCELLED_BY_STAFF', 'PAYMENT_EXPIRED', 'APPROVAL_EXPIRED', 'REJECTED')");
                    table.ForeignKey(
                        name: "fk_decision_outcomes_decision_id",
                        column: x => x.decision_id,
                        principalSchema: "tadss",
                        principalTable: "decision_executions",
                        principalColumn: "decision_id");
                });

            migrationBuilder.CreateTable(
                name: "decision_overrides",
                schema: "tadss",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    deposit_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    deposit_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    actor_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decision_overrides", x => x.id);
                    table.CheckConstraint("ck_do_action", "action IN ('APPROVE', 'REJECT')");
                    table.CheckConstraint("ck_do_deposit", "(action = 'APPROVE') = (deposit_rate IS NOT NULL AND deposit_amount IS NOT NULL)");
                    table.CheckConstraint("ck_do_deposit_amount", "deposit_amount >= 0");
                    table.CheckConstraint("ck_do_deposit_rate", "deposit_rate BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_do_reason_length", "char_length(reason) BETWEEN 5 AND 500");
                    table.ForeignKey(
                        name: "fk_decision_overrides_decision_id",
                        column: x => x.decision_id,
                        principalSchema: "tadss",
                        principalTable: "decision_executions",
                        principalColumn: "decision_id");
                });

            // 3. Index
            migrationBuilder.CreateIndex(
                name: "idx_de_created_at",
                schema: "tadss",
                table: "decision_executions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "idx_de_pending",
                schema: "tadss",
                table: "decision_executions",
                columns: new[] { "branch_id", "created_at" },
                filter: "status = 'NEED_APPROVAL' AND role = 'PRIMARY'");

            migrationBuilder.CreateIndex(
                name: "idx_de_request_hash",
                schema: "tadss",
                table: "decision_executions",
                column: "request_hash");

            migrationBuilder.CreateIndex(
                name: "idx_de_reservation_created",
                schema: "tadss",
                table: "decision_executions",
                columns: new[] { "reservation_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "uq_decision_idempotency_key",
                schema: "tadss",
                table: "decision_executions",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL AND role = 'PRIMARY'");

            migrationBuilder.CreateIndex(
                name: "uq_decision_outcomes_decision_id",
                schema: "tadss",
                table: "decision_outcomes",
                column: "decision_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_decision_overrides_decision_id",
                schema: "tadss",
                table: "decision_overrides",
                column: "decision_id",
                unique: true);

            // 4. Hàm và trigger append-only (B.4): chặn UPDATE/DELETE theo dòng và TRUNCATE theo câu lệnh
            migrationBuilder.Sql(@"
CREATE FUNCTION tadss.reject_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'Bảng %.% là append-only', TG_TABLE_SCHEMA, TG_TABLE_NAME USING ERRCODE = 'P0001';
END $$;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_de_append_only BEFORE UPDATE OR DELETE ON tadss.decision_executions
    FOR EACH ROW EXECUTE FUNCTION tadss.reject_mutation();
CREATE TRIGGER trg_de_no_truncate BEFORE TRUNCATE ON tadss.decision_executions
    FOR EACH STATEMENT EXECUTE FUNCTION tadss.reject_mutation();
CREATE TRIGGER trg_do_append_only BEFORE UPDATE OR DELETE ON tadss.decision_overrides
    FOR EACH ROW EXECUTE FUNCTION tadss.reject_mutation();
CREATE TRIGGER trg_do_no_truncate BEFORE TRUNCATE ON tadss.decision_overrides
    FOR EACH STATEMENT EXECUTE FUNCTION tadss.reject_mutation();
CREATE TRIGGER trg_dout_append_only BEFORE UPDATE OR DELETE ON tadss.decision_outcomes
    FOR EACH ROW EXECUTE FUNCTION tadss.reject_mutation();
CREATE TRIGGER trg_dout_no_truncate BEFORE TRUNCATE ON tadss.decision_outcomes
    FOR EACH STATEMENT EXECUTE FUNCTION tadss.reject_mutation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Gỡ trigger và hàm append-only
            migrationBuilder.Sql(@"
DROP TRIGGER trg_dout_no_truncate ON tadss.decision_outcomes;
DROP TRIGGER trg_dout_append_only ON tadss.decision_outcomes;
DROP TRIGGER trg_do_no_truncate ON tadss.decision_overrides;
DROP TRIGGER trg_do_append_only ON tadss.decision_overrides;
DROP TRIGGER trg_de_no_truncate ON tadss.decision_executions;
DROP TRIGGER trg_de_append_only ON tadss.decision_executions;");

            migrationBuilder.Sql("DROP FUNCTION tadss.reject_mutation();");

            // 2. Bỏ 3 bảng v2 (bảng có khóa ngoại trước)
            migrationBuilder.DropTable(
                name: "decision_outcomes",
                schema: "tadss");

            migrationBuilder.DropTable(
                name: "decision_overrides",
                schema: "tadss");

            migrationBuilder.DropTable(
                name: "decision_executions",
                schema: "tadss");

            // 3. Dựng lại bảng v1 đúng như model snapshot của migration AddCreatedAtIndex
            migrationBuilder.CreateTable(
                name: "decision_executions",
                schema: "tadss",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    strategy_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "1.0.0"),
                    execution_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    execution_time_ms = table.Column<int>(type: "integer", nullable: false),
                    fallback_used = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    cache_hit = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    need_approval = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_payload = table.Column<string>(type: "jsonb", nullable: false),
                    is_overridden = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    overridden_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    override_reason = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decision_executions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_decision_executions_created_at",
                schema: "tadss",
                table: "decision_executions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "idx_decision_executions_pending_approval",
                schema: "tadss",
                table: "decision_executions",
                column: "created_at",
                filter: "need_approval = true AND is_overridden = false");

            migrationBuilder.CreateIndex(
                name: "idx_decision_executions_request_hash",
                schema: "tadss",
                table: "decision_executions",
                column: "request_hash");

            migrationBuilder.CreateIndex(
                name: "idx_decision_executions_reservation_id",
                schema: "tadss",
                table: "decision_executions",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "uq_decision_id",
                schema: "tadss",
                table: "decision_executions",
                column: "decision_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_decision_idempotency_key",
                schema: "tadss",
                table: "decision_executions",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }
    }
}
