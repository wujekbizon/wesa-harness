using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wesa.Harness.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddP7WorkflowExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtensionInputsJson",
                schema: "harness",
                table: "pipeline_stages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtensionOperation",
                schema: "harness",
                table: "pipeline_stages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtensionPluginId",
                schema: "harness",
                table: "pipeline_stages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResultArtifactId",
                schema: "harness",
                table: "pipeline_stages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultJson",
                schema: "harness",
                table: "pipeline_stages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultSha256",
                schema: "harness",
                table: "pipeline_stages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultStoragePath",
                schema: "harness",
                table: "pipeline_stages",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtensionInputsJson",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "ExtensionOperation",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "ExtensionPluginId",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "ResultArtifactId",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "ResultJson",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "ResultSha256",
                schema: "harness",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "ResultStoragePath",
                schema: "harness",
                table: "pipeline_stages");
        }
    }
}
