using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wesa.Harness.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddP6SandboxWorkflowStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SandboxExecutionId",
                schema: "harness",
                table: "pipeline_stages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SandboxProfile",
                schema: "harness",
                table: "pipeline_stages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceStageKey",
                schema: "harness",
                table: "pipeline_stages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SandboxExecutionId",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "SandboxProfile",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "SourceStageKey",
                schema: "harness",
                table: "pipeline_stages");
        }
    }
}
