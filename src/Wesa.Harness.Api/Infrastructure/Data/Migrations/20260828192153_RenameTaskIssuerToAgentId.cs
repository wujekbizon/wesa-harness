using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wesa.Harness.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameTaskIssuerToAgentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Issuer",
                schema: "harness",
                table: "tasks",
                newName: "AgentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AgentId",
                schema: "harness",
                table: "tasks",
                newName: "Issuer");
        }
    }
}
