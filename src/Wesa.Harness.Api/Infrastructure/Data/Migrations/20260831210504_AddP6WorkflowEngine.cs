using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wesa.Harness.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddP6WorkflowEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApprovalId",
                schema: "harness",
                table: "pipeline_stages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentStagesJson",
                schema: "harness",
                table: "pipeline_stages",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                schema: "harness",
                table: "pipeline_stages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Condition",
                schema: "harness",
                table: "pipeline_stages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "on-success");

            migrationBuilder.AddColumn<string>(
                name: "DependsOnJson",
                schema: "harness",
                table: "pipeline_stages",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "Iteration",
                schema: "harness",
                table: "pipeline_stages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "harness",
                table: "pipeline_stages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "task");

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                schema: "harness",
                table: "pipeline_stages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                schema: "harness",
                table: "pipeline_stages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "legacy-model");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                schema: "harness",
                table: "pipeline_stages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "ollama-provider");

            migrationBuilder.AddColumn<int>(
                name: "RepeatCount",
                schema: "harness",
                table: "pipeline_stages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "StageKey",
                schema: "harness",
                table: "pipeline_stages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "legacy-stage");

            migrationBuilder.AddColumn<bool>(
                name: "CancelRequested",
                schema: "harness",
                table: "pipeline_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DefinitionVersion",
                schema: "harness",
                table: "pipeline_runs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<Guid>(
                name: "SessionId",
                schema: "harness",
                table: "approvals",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineRunId",
                schema: "harness",
                table: "approvals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_approvals_PipelineRunId",
                schema: "harness",
                table: "approvals",
                column: "PipelineRunId");

            migrationBuilder.Sql(
                """
                UPDATE harness.pipeline_stages AS stage
                   SET "StageKey" = 'legacy-' || stage."Sequence",
                       "Provider" = run."Provider",
                       "Model" = run."Model",
                       "DependsOnJson" = CASE
                           WHEN stage."Sequence" = 1 THEN '[]'::jsonb
                           ELSE jsonb_build_array('legacy-' || (stage."Sequence" - 1))
                       END,
                       "AttachmentStagesJson" = CASE
                           WHEN stage."Sequence" = 1 THEN '[]'::jsonb
                           ELSE jsonb_build_array('legacy-' || (stage."Sequence" - 1))
                       END
                  FROM harness.pipeline_runs AS run
                 WHERE stage."PipelineRunId" = run."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_approvals_PipelineRunId",
                schema: "harness",
                table: "approvals");

            migrationBuilder.DropColumn(
                name: "ApprovalId",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "AttachmentStagesJson",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "Condition",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "DependsOnJson",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "Iteration",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "Model",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "Provider",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "RepeatCount",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "StageKey",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "CancelRequested",
                schema: "harness",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "DefinitionVersion",
                schema: "harness",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "PipelineRunId",
                schema: "harness",
                table: "approvals");

            migrationBuilder.AlterColumn<Guid>(
                name: "SessionId",
                schema: "harness",
                table: "approvals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
