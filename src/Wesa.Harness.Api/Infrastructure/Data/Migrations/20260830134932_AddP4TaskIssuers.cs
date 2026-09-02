using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wesa.Harness.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddP4TaskIssuers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Issuer",
                schema: "harness",
                table: "tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "harness-api");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Issuer",
                schema: "harness",
                table: "tasks");
        }
    }
}
