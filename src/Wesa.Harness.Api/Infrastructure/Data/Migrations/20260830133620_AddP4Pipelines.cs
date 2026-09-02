using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wesa.Harness.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddP4Pipelines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pipeline_runs",
                schema: "harness",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Definition = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Model = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OriginalPrompt = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CurrentStage = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_stages",
                schema: "harness",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AgentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TaskType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    OutputPath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_stages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pipeline_stages_pipeline_runs_PipelineRunId",
                        column: x => x.PipelineRunId,
                        principalSchema: "harness",
                        principalTable: "pipeline_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_WorkspaceId_CreatedAt",
                schema: "harness",
                table: "pipeline_runs",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_stages_PipelineRunId_Sequence",
                schema: "harness",
                table: "pipeline_stages",
                columns: new[] { "PipelineRunId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pipeline_stages",
                schema: "harness");

            migrationBuilder.DropTable(
                name: "pipeline_runs",
                schema: "harness");
        }
    }
}
