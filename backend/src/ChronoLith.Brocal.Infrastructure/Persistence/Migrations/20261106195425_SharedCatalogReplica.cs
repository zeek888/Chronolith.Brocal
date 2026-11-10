using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChronoLith.Brocal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SharedCatalogReplica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "shell_field_id",
                schema: "brocal",
                table: "sites",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "catalog_feed_state",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cursor = table.Column<long>(type: "bigint", nullable: false),
                    latest = table.Column<long>(type: "bigint", nullable: false),
                    last_polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_full_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_applied = table.Column<int>(type: "integer", nullable: false),
                    last_run_errors = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    last_error_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_feed_state", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "catalog_push_messages",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    method = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    shell_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_push_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shared_catalog_items",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    last_change_id = table.Column<long>(type: "bigint", nullable: true),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shared_catalog_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_push_messages_entity_id_status",
                schema: "brocal",
                table: "catalog_push_messages",
                columns: new[] { "entity_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_push_messages_status_next_attempt_at",
                schema: "brocal",
                table: "catalog_push_messages",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shared_catalog_items_catalog_code",
                schema: "brocal",
                table: "shared_catalog_items",
                columns: new[] { "catalog", "code" });

            migrationBuilder.CreateIndex(
                name: "ix_shared_catalog_items_catalog_parent_id",
                schema: "brocal",
                table: "shared_catalog_items",
                columns: new[] { "catalog", "parent_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_feed_state",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "catalog_push_messages",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "shared_catalog_items",
                schema: "brocal");

            migrationBuilder.DropColumn(
                name: "shell_field_id",
                schema: "brocal",
                table: "sites");
        }
    }
}
