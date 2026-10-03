using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TADSS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_decision_idempotency",
                schema: "tadss",
                table: "decision_executions");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_decision_id",
                schema: "tadss",
                table: "decision_executions");

            migrationBuilder.DropIndex(
                name: "uq_decision_idempotency_key",
                schema: "tadss",
                table: "decision_executions");

            migrationBuilder.CreateIndex(
                name: "uq_decision_idempotency",
                schema: "tadss",
                table: "decision_executions",
                columns: new[] { "decision_id", "idempotency_key" },
                unique: true);
        }
    }
}
