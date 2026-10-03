using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TADSS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_decision_executions_created_at",
                schema: "tadss",
                table: "decision_executions",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_decision_executions_created_at",
                schema: "tadss",
                table: "decision_executions");
        }
    }
}
