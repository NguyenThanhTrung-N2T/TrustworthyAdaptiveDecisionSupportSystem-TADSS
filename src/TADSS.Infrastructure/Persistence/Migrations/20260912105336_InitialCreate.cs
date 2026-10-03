using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TADSS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tadss");

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
                name: "uq_decision_idempotency",
                schema: "tadss",
                table: "decision_executions",
                columns: new[] { "decision_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "decision_executions",
                schema: "tadss");
        }
    }
}
