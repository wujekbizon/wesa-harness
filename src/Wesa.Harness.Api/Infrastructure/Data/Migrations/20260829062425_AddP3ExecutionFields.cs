using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wesa.Harness.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddP3ExecutionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ArtifactId",
                schema: "harness",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GateOutcome",
                schema: "harness",
                table: "tasks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                schema: "harness",
                table: "tasks",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                schema: "harness",
                table: "tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResultText",
                schema: "harness",
                table: "tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoragePath",
                schema: "harness",
                table: "artifacts",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArtifactId",
                schema: "harness",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "GateOutcome",
                schema: "harness",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "Model",
                schema: "harness",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "Provider",
                schema: "harness",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "ResultText",
                schema: "harness",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "StoragePath",
                schema: "harness",
                table: "artifacts");
        }
    }
}
