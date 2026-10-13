using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChronoLith.Brocal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomationWorkflowsAndRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_opt_outs",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automation_opt_outs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "business_rules",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    document = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    triggers = table.Column<string>(type: "jsonb", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    match = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    conditions = table.Column<string>(type: "jsonb", nullable: false),
                    effect = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    severity = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    recipients = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_job_runs",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    triggered_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    companies = table.Column<int>(type: "integer", nullable: false),
                    notices = table.Column<int>(type: "integer", nullable: false),
                    failures = table.Column<int>(type: "integer", nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_job_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    document = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    match = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    conditions = table.Column<string>(type: "jsonb", nullable: false),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_instances",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    from_global_definition = table.Column<bool>(type: "boolean", nullable: false),
                    document = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    completion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    facts_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    facts = table.Column<string>(type: "jsonb", nullable: false),
                    conditions = table.Column<string>(type: "jsonb", nullable: false),
                    match = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    decisions = table.Column<string>(type: "jsonb", nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    current_step = table.Column<int>(type: "integer", nullable: false),
                    step_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closing_comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_instances", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_automation_opt_outs_tenant_id",
                schema: "brocal",
                table: "automation_opt_outs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_automation_opt_outs_tenant_id_kind_definition_id",
                schema: "brocal",
                table: "automation_opt_outs",
                columns: new[] { "tenant_id", "kind", "definition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_rules_tenant_id_document",
                schema: "brocal",
                table: "business_rules",
                columns: new[] { "tenant_id", "document" });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_job_runs_started_at",
                schema: "brocal",
                table: "scheduled_job_runs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_tenant_id_document",
                schema: "brocal",
                table: "workflow_definitions",
                columns: new[] { "tenant_id", "document" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_tenant_id",
                schema: "brocal",
                table: "workflow_instances",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_tenant_id_document_document_id",
                schema: "brocal",
                table: "workflow_instances",
                columns: new[] { "tenant_id", "document", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_tenant_id_requested_by_id",
                schema: "brocal",
                table: "workflow_instances",
                columns: new[] { "tenant_id", "requested_by_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_tenant_id_status_requested_at",
                schema: "brocal",
                table: "workflow_instances",
                columns: new[] { "tenant_id", "status", "requested_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_opt_outs",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "business_rules",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "scheduled_job_runs",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "workflow_definitions",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "workflow_instances",
                schema: "brocal");
        }
    }
}
