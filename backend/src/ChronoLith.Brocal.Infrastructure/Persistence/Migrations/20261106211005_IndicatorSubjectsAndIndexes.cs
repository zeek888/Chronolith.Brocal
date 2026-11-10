using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChronoLith.Brocal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndicatorSubjectsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "keep_retrying",
                schema: "brocal",
                table: "indicator_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Retirements already queued are retried until delivered as well.
            migrationBuilder.Sql("UPDATE brocal.indicator_messages SET keep_retrying = TRUE WHERE kind = 'Retire' AND status = 'Pending';");

            migrationBuilder.CreateTable(
                name: "indicator_subjects",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    has_next_expiry = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indicator_subjects", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_site_id_status",
                schema: "brocal",
                table: "work_orders",
                columns: new[] { "site_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_site_id_draft",
                schema: "brocal",
                table: "tickets",
                column: "site_id",
                filter: "status = 'Draft'");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_site_id_service_date",
                schema: "brocal",
                table: "tickets",
                columns: new[] { "site_id", "service_date" });

            migrationBuilder.CreateIndex(
                name: "ix_sites_shell_catalog_id",
                schema: "brocal",
                table: "sites",
                column: "shell_catalog_id",
                filter: "shell_catalog_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_incidents_site_id_occurred_at",
                schema: "brocal",
                table: "incidents",
                columns: new[] { "site_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_compliance_documents_subject_id_document_type_id",
                schema: "brocal",
                table: "compliance_documents",
                columns: new[] { "subject_id", "document_type_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indicator_subjects",
                schema: "brocal");

            migrationBuilder.DropIndex(
                name: "ix_work_orders_site_id_status",
                schema: "brocal",
                table: "work_orders");

            migrationBuilder.DropIndex(
                name: "ix_tickets_site_id_draft",
                schema: "brocal",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_site_id_service_date",
                schema: "brocal",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_sites_shell_catalog_id",
                schema: "brocal",
                table: "sites");

            migrationBuilder.DropIndex(
                name: "ix_incidents_site_id_occurred_at",
                schema: "brocal",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "ix_compliance_documents_subject_id_document_type_id",
                schema: "brocal",
                table: "compliance_documents");

            migrationBuilder.DropColumn(
                name: "keep_retrying",
                schema: "brocal",
                table: "indicator_messages");
        }
    }
}
