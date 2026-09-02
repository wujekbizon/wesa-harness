using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wesa.Harness.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddP5SandboxExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sandbox_executions",
                schema: "harness",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    Profile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OutputPath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    GateOutcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ResultText = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sandbox_executions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sandbox_executions_SourceTaskId_CreatedAt",
                schema: "harness",
                table: "sandbox_executions",
                columns: new[] { "SourceTaskId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sandbox_executions",
                schema: "harness");
        }
    }
}
