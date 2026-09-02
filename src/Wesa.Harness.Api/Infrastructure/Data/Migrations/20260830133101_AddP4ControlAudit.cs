using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wesa.Harness.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddP4ControlAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "control_audit",
                schema: "harness",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CommandType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_control_audit", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_control_audit_OccurredAt",
                schema: "harness",
                table: "control_audit",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "control_audit",
                schema: "harness");
        }
    }
}
