using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChronoLith.Brocal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndicatorOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "indicator_messages",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    items = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indicator_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_indicator_messages_created_at",
                schema: "brocal",
                table: "indicator_messages",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_indicator_messages_status_kind_created_at",
                schema: "brocal",
                table: "indicator_messages",
                columns: new[] { "status", "kind", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indicator_messages",
                schema: "brocal");
        }
    }
}
