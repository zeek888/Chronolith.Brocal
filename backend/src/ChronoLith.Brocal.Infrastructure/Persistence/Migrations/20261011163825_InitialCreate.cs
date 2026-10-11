using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChronoLith.Brocal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "brocal");

            migrationBuilder.CreateTable(
                name: "accounting_settings",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customers_account = table.Column<string>(type: "text", nullable: false),
                    revenue_account = table.Column<string>(type: "text", nullable: false),
                    vat_transferred_account = table.Column<string>(type: "text", nullable: false),
                    vat_pending_account = table.Column<string>(type: "text", nullable: false),
                    bank_account = table.Column<string>(type: "text", nullable: false),
                    format = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounting_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "attendance",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    crew_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    check_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    check_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    overtime_hours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    user_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    changes = table.Column<string>(type: "jsonb", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "collection_notes",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estimate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contacted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    channel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    promised_payment_on = table.Column<DateOnly>(type: "date", nullable: true),
                    next_action_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_notes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "compliance_documents",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    issuer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    hours = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compliance_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "compliance_requirements",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_document_type_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    unit_document_type_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compliance_requirements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contracts",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    ceiling_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    customer_representative = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contracts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "crews",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leader_id = table.Column<Guid>(type: "uuid", nullable: true),
                    shift = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    member_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    unit_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crews", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    tax_regime = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    cfdi_use = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    payment_form = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    credit_days = table.Column<int>(type: "integer", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    shell_company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requires_copade = table.Column<bool>(type: "boolean", nullable: false),
                    contacts = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_types",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    applies_to = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    authority = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    validity_months = table.Column<int>(type: "integer", nullable: true),
                    warning_days = table.Column<int>(type: "integer", nullable: true),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    curp = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: true),
                    rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    nss = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    position = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    hired_on = table.Column<DateOnly>(type: "date", nullable: true),
                    terminated_on = table.Column<DateOnly>(type: "date", nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    emergency_contact = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    blood_type = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    license_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    license_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    license_expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    hourly_cost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    photo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "estimates",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    folio_number = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ticket_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    copade_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    copade_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estimates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_reports",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    folio_number = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    purpose = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    advance_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_reports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "files",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    owner_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "folio_sequences",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_folio_sequences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fuel_loads",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    loaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    liters = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    price_per_liter = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    odometer = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: true),
                    hours = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: true),
                    is_full_tank = table.Column<bool>(type: "boolean", nullable: false),
                    station = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    payment_method = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receipt_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    efficiency = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    efficiency_unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    deviation_percent = table.Column<decimal>(type: "numeric(7,1)", precision: 7, scale: 1, nullable: true),
                    is_anomaly = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fuel_loads", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "incidents",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    folio_number = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    severity = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    employee_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    immediate_actions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    root_cause = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    lost_days = table.Column<int>(type: "integer", nullable: true),
                    actions = table.Column<string>(type: "jsonb", nullable: false),
                    photo_file_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    reported_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reported_by_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inspection_templates",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    items = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspection_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inspections",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    inspector_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inspector_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    answers = table.Column<string>(type: "jsonb", nullable: false),
                    score = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    findings = table.Column<int>(type: "integer", nullable: false),
                    critical_findings = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_items",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    unit_name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    brand = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    part_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    min_stock = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    max_stock = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    standard_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    average_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_movements",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    difference = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    moved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    maintenance_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    crew_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_movements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    folio = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver = table.Column<string>(type: "jsonb", nullable: false),
                    issuer = table.Column<string>(type: "jsonb", nullable: true),
                    payment_form = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ticket_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    estimate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    copade_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    stamped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pac_provider = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    xml_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pdf_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_motive = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    substitution_uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    cancellation_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    last_payment_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_orders",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    folio_number = table.Column<long>(type: "bigint", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    odometer_at_service = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: true),
                    hours_at_service = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: true),
                    reported_failure = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    diagnosis = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    work_performed = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tasks = table.Column<string>(type: "jsonb", nullable: false),
                    labor_hours = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    labor_cost_per_hour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    technicians = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    external_provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    external_invoice = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    external_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    parts_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    labor_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_plans",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    interval = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: false),
                    warning_before = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: false),
                    tasks = table.Column<string[]>(type: "text[]", nullable: false),
                    estimated_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    is_tenant_admin = table.Column<bool>(type: "boolean", nullable: false),
                    role_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_access_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memberships", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_channels",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    use_own_smtp = table.Column<bool>(type: "boolean", nullable: false),
                    smtp_host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    smtp_port = table.Column<int>(type: "integer", nullable: false),
                    smtp_use_ssl = table.Column<bool>(type: "boolean", nullable: false),
                    smtp_user = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    smtp_password_protected = table.Column<string>(type: "text", nullable: true),
                    from_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    from_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    whats_app_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    whats_app_phone_number_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    whats_app_token_protected = table.Column<string>(type: "text", nullable: true),
                    whats_app_template = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    whats_app_language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_rules",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(60)", maxLength: 60, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    channels = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    role_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    emails = table.Column<string[]>(type: "text[]", nullable: false),
                    phones = table.Column<string[]>(type: "text[]", nullable: false),
                    notify_customer_contacts = table.Column<bool>(type: "boolean", nullable: false),
                    notify_assignees = table.Column<bool>(type: "boolean", nullable: false),
                    days_before = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    recipient = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    subject = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(60)", maxLength: 60, nullable: false),
                    deduplication_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    template_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    template_parameters = table.Column<string>(type: "jsonb", nullable: true),
                    attachment_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    payment_form = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    complement_status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    complement_series = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    complement_folio = table.Column<long>(type: "bigint", nullable: true),
                    complement_uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    complement_xml_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    complement_stamped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_lists",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_lists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quotes",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    folio_number = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    terms = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vat = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    template_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    permissions = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "safety_talks",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    held_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    instructor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    attendee_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    external_attendees = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    evidence_file_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_safety_talks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_types",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    icon = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    requires_signature = table.Column<bool>(type: "boolean", nullable: false),
                    min_photos = table.Column<int>(type: "integer", nullable: false),
                    fields = table.Column<string>(type: "jsonb", nullable: false),
                    default_service_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "services",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    unit_name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sat_product_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    sat_unit_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tax_object = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sites",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    field_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    municipality = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    shell_catalog_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sites", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_levels",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_levels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tax_id = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    plan = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    trial_ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    branding = table.Column<string>(type: "jsonb", nullable: false),
                    quotas = table.Column<string>(type: "jsonb", nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false),
                    fiscal = table.Column<string>(type: "jsonb", nullable: false),
                    enabled_modules = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    folio_number = table.Column<long>(type: "bigint", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    unit_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    crew_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    observations = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    customer_representative = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    answers = table.Column<string>(type: "jsonb", nullable: false),
                    capture_latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    capture_longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    captured_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    captured_by_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    signer_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    signer_position = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    signature_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    signed_latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    signed_longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoiced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    pdf_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tickets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tool_assignments",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    serial_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    crew_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expected_return_on = table.Column<DateOnly>(type: "date", nullable: true),
                    returned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    return_condition = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tool_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trips",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    folio_number = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    cfdi_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trailer_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "jsonb", nullable: false),
                    destination = table.Column<string>(type: "jsonb", nullable: false),
                    distance_km = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    goods = table.Column<string>(type: "jsonb", nullable: false),
                    total_weight_kg = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    freight_service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    freight_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    id_ccp = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    stamped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trips", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unit_meter_readings",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    odometer = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: true),
                    hours = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: true),
                    source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_meter_readings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unit_positions",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    speed_kmh = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    odometer = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: true),
                    source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_positions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unit_status_changes",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    from = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    to = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    changed_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_status_changes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "units",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    brand = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    plates = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    serial_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    meter_kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    current_odometer = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: false),
                    current_hours = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: false),
                    meters_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    capacity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    capacity_unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    fuel_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    fuel_tank_liters = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    expected_efficiency = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    vehicle_config = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    gross_weight_tons = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    sct_permit_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    sct_permit_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    insurer_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    insurance_policy = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    environmental_insurer = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    environmental_policy = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    default_operator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    photo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acquired_on = table.Column<DateOnly>(type: "date", nullable: true),
                    acquisition_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    last_latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    last_longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    last_position_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_notifications",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    deduplication_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(60)", maxLength: 60, nullable: false),
                    severity = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    link = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    responsible = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "work_orders",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    folio_number = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    priority = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    customer_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    scheduled_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scheduled_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    unit_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    crew_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    planned_services = table.Column<string>(type: "jsonb", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_items",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    vendor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    has_invoice = table.Column<bool>(type: "boolean", nullable: false),
                    invoice_uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    receipt_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_items_expense_reports_report_id",
                        column: x => x.report_id,
                        principalSchema: "brocal",
                        principalTable: "expense_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sat_product_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    sat_unit_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    unit_name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_object = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "brocal",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_parts",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_parts", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_parts_maintenance_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "brocal",
                        principalTable: "maintenance_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_plan_units",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_service_on = table.Column<DateOnly>(type: "date", nullable: true),
                    last_odometer = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: true),
                    last_hours = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_plan_units", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_plan_units_maintenance_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "brocal",
                        principalTable: "maintenance_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_applications",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    partiality = table.Column<int>(type: "integer", nullable: false),
                    previous_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    remaining_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_applications", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_applications_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "brocal",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "price_list_items",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_list_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_price_list_items_price_lists_price_list_id",
                        column: x => x.price_list_id,
                        principalSchema: "brocal",
                        principalTable: "price_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quote_lines",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    gross = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    vat = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_quote_lines_quotes_quote_id",
                        column: x => x.quote_id,
                        principalSchema: "brocal",
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_lines",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    price_source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_lines_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "brocal",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_photos",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caption = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_photos_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "brocal",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounting_settings_tenant_id",
                schema: "brocal",
                table: "accounting_settings",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_key_hash",
                schema: "brocal",
                table: "api_keys",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_tenant_id",
                schema: "brocal",
                table: "api_keys",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_tenant_id",
                schema: "brocal",
                table: "attendance",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_tenant_id_date",
                schema: "brocal",
                table: "attendance",
                columns: new[] { "tenant_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_tenant_id_employee_id_date",
                schema: "brocal",
                table: "attendance",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_ticket_id",
                schema: "brocal",
                table: "attendance",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_tenant_id_entity_type_entity_id",
                schema: "brocal",
                table: "audit_entries",
                columns: new[] { "tenant_id", "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_tenant_id_occurred_at",
                schema: "brocal",
                table: "audit_entries",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_collection_notes_tenant_id",
                schema: "brocal",
                table: "collection_notes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_notes_tenant_id_customer_id_contacted_at",
                schema: "brocal",
                table: "collection_notes",
                columns: new[] { "tenant_id", "customer_id", "contacted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_compliance_documents_document_type_id",
                schema: "brocal",
                table: "compliance_documents",
                column: "document_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_documents_tenant_id",
                schema: "brocal",
                table: "compliance_documents",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_documents_tenant_id_expires_on",
                schema: "brocal",
                table: "compliance_documents",
                columns: new[] { "tenant_id", "expires_on" });

            migrationBuilder.CreateIndex(
                name: "ix_compliance_documents_tenant_id_subject_subject_id",
                schema: "brocal",
                table: "compliance_documents",
                columns: new[] { "tenant_id", "subject", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_compliance_requirements_tenant_id",
                schema: "brocal",
                table: "compliance_requirements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_requirements_tenant_id_code",
                schema: "brocal",
                table: "compliance_requirements",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_compliance_requirements_tenant_id_name",
                schema: "brocal",
                table: "compliance_requirements",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_tenant_id",
                schema: "brocal",
                table: "contracts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_tenant_id_code",
                schema: "brocal",
                table: "contracts",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contracts_tenant_id_customer_id",
                schema: "brocal",
                table: "contracts",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_tenant_id_name",
                schema: "brocal",
                table: "contracts",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_crews_tenant_id",
                schema: "brocal",
                table: "crews",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_crews_tenant_id_code",
                schema: "brocal",
                table: "crews",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_crews_tenant_id_name",
                schema: "brocal",
                table: "crews",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_id",
                schema: "brocal",
                table: "customers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_id_code",
                schema: "brocal",
                table: "customers",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_id_name",
                schema: "brocal",
                table: "customers",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_id_rfc",
                schema: "brocal",
                table: "customers",
                columns: new[] { "tenant_id", "rfc" });

            migrationBuilder.CreateIndex(
                name: "ix_document_types_tenant_id",
                schema: "brocal",
                table: "document_types",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_types_tenant_id_code",
                schema: "brocal",
                table: "document_types",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_types_tenant_id_name",
                schema: "brocal",
                table: "document_types",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id",
                schema: "brocal",
                table: "employees",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_code",
                schema: "brocal",
                table: "employees",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_name",
                schema: "brocal",
                table: "employees",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_estimates_tenant_id",
                schema: "brocal",
                table: "estimates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_estimates_tenant_id_customer_id_status",
                schema: "brocal",
                table: "estimates",
                columns: new[] { "tenant_id", "customer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_estimates_tenant_id_folio_number",
                schema: "brocal",
                table: "estimates",
                columns: new[] { "tenant_id", "folio_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_items_report_id",
                schema: "brocal",
                table: "expense_items",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_items_tenant_id",
                schema: "brocal",
                table: "expense_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_reports_tenant_id",
                schema: "brocal",
                table: "expense_reports",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_reports_tenant_id_employee_id",
                schema: "brocal",
                table: "expense_reports",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_expense_reports_tenant_id_folio_number",
                schema: "brocal",
                table: "expense_reports",
                columns: new[] { "tenant_id", "folio_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_files_tenant_id",
                schema: "brocal",
                table: "files",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_files_tenant_id_owner_type_owner_id",
                schema: "brocal",
                table: "files",
                columns: new[] { "tenant_id", "owner_type", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_folio_sequences_tenant_id",
                schema: "brocal",
                table: "folio_sequences",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_folio_sequences_tenant_id_key",
                schema: "brocal",
                table: "folio_sequences",
                columns: new[] { "tenant_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fuel_loads_tenant_id",
                schema: "brocal",
                table: "fuel_loads",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_fuel_loads_tenant_id_unit_id_loaded_at",
                schema: "brocal",
                table: "fuel_loads",
                columns: new[] { "tenant_id", "unit_id", "loaded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_incidents_tenant_id",
                schema: "brocal",
                table: "incidents",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_incidents_tenant_id_folio_number",
                schema: "brocal",
                table: "incidents",
                columns: new[] { "tenant_id", "folio_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incidents_tenant_id_occurred_at",
                schema: "brocal",
                table: "incidents",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inspection_templates_tenant_id",
                schema: "brocal",
                table: "inspection_templates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inspection_templates_tenant_id_code",
                schema: "brocal",
                table: "inspection_templates",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inspection_templates_tenant_id_name",
                schema: "brocal",
                table: "inspection_templates",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_inspections_tenant_id",
                schema: "brocal",
                table: "inspections",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inspections_tenant_id_performed_at",
                schema: "brocal",
                table: "inspections",
                columns: new[] { "tenant_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inspections_unit_id",
                schema: "brocal",
                table: "inspections",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_tenant_id",
                schema: "brocal",
                table: "inventory_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_tenant_id_code",
                schema: "brocal",
                table: "inventory_items",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_tenant_id_name",
                schema: "brocal",
                table: "inventory_items",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_maintenance_order_id",
                schema: "brocal",
                table: "inventory_movements",
                column: "maintenance_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_tenant_id",
                schema: "brocal",
                table: "inventory_movements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_tenant_id_item_id_moved_at",
                schema: "brocal",
                table: "inventory_movements",
                columns: new[] { "tenant_id", "item_id", "moved_at" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_invoice_id",
                schema: "brocal",
                table: "invoice_lines",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_tenant_id",
                schema: "brocal",
                table: "invoice_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_ticket_id",
                schema: "brocal",
                table: "invoice_lines",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id",
                schema: "brocal",
                table: "invoices",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_customer_id_status",
                schema: "brocal",
                table: "invoices",
                columns: new[] { "tenant_id", "customer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_due_on",
                schema: "brocal",
                table: "invoices",
                columns: new[] { "tenant_id", "due_on" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_series_folio",
                schema: "brocal",
                table: "invoices",
                columns: new[] { "tenant_id", "series", "folio" },
                unique: true,
                filter: "folio IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_uuid",
                schema: "brocal",
                table: "invoices",
                column: "uuid");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_orders_tenant_id",
                schema: "brocal",
                table: "maintenance_orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_orders_tenant_id_folio_number",
                schema: "brocal",
                table: "maintenance_orders",
                columns: new[] { "tenant_id", "folio_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_orders_tenant_id_unit_id_status",
                schema: "brocal",
                table: "maintenance_orders",
                columns: new[] { "tenant_id", "unit_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_parts_item_id",
                schema: "brocal",
                table: "maintenance_parts",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_parts_order_id",
                schema: "brocal",
                table: "maintenance_parts",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_parts_tenant_id",
                schema: "brocal",
                table: "maintenance_parts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plan_units_plan_id_unit_id",
                schema: "brocal",
                table: "maintenance_plan_units",
                columns: new[] { "plan_id", "unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plan_units_tenant_id",
                schema: "brocal",
                table: "maintenance_plan_units",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plan_units_unit_id",
                schema: "brocal",
                table: "maintenance_plan_units",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_tenant_id",
                schema: "brocal",
                table: "maintenance_plans",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_tenant_id_code",
                schema: "brocal",
                table: "maintenance_plans",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_tenant_id_name",
                schema: "brocal",
                table: "maintenance_plans",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_memberships_email",
                schema: "brocal",
                table: "memberships",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_identity_id",
                schema: "brocal",
                table: "memberships",
                column: "identity_id");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_tenant_id",
                schema: "brocal",
                table: "memberships",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_tenant_id_email",
                schema: "brocal",
                table: "memberships",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memberships_tenant_id_identity_id",
                schema: "brocal",
                table: "memberships",
                columns: new[] { "tenant_id", "identity_id" },
                unique: true,
                filter: "identity_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notification_channels_tenant_id",
                schema: "brocal",
                table: "notification_channels",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_rules_tenant_id",
                schema: "brocal",
                table: "notification_rules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_rules_tenant_id_event",
                schema: "brocal",
                table: "notification_rules",
                columns: new[] { "tenant_id", "event" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_status_next_attempt_at",
                schema: "brocal",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "brocal",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id_deduplication_key",
                schema: "brocal",
                table: "outbox_messages",
                columns: new[] { "tenant_id", "deduplication_key" },
                unique: true,
                filter: "deduplication_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_applications_invoice_id",
                schema: "brocal",
                table: "payment_applications",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_applications_payment_id",
                schema: "brocal",
                table: "payment_applications",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_applications_tenant_id",
                schema: "brocal",
                table: "payment_applications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id",
                schema: "brocal",
                table: "payments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_customer_id_paid_at",
                schema: "brocal",
                table: "payments",
                columns: new[] { "tenant_id", "customer_id", "paid_at" });

            migrationBuilder.CreateIndex(
                name: "ix_price_list_items_price_list_id_service_id",
                schema: "brocal",
                table: "price_list_items",
                columns: new[] { "price_list_id", "service_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_list_items_service_id",
                schema: "brocal",
                table: "price_list_items",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_list_items_tenant_id",
                schema: "brocal",
                table: "price_list_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_tenant_id",
                schema: "brocal",
                table: "price_lists",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_tenant_id_code",
                schema: "brocal",
                table: "price_lists",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_tenant_id_customer_id_contract_id",
                schema: "brocal",
                table: "price_lists",
                columns: new[] { "tenant_id", "customer_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_tenant_id_name",
                schema: "brocal",
                table: "price_lists",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_quote_lines_quote_id",
                schema: "brocal",
                table: "quote_lines",
                column: "quote_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_lines_service_id",
                schema: "brocal",
                table: "quote_lines",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_lines_tenant_id",
                schema: "brocal",
                table: "quote_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_tenant_id",
                schema: "brocal",
                table: "quotes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_tenant_id_customer_id",
                schema: "brocal",
                table: "quotes",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quotes_tenant_id_folio_number",
                schema: "brocal",
                table: "quotes",
                columns: new[] { "tenant_id", "folio_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id",
                schema: "brocal",
                table: "roles",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_name",
                schema: "brocal",
                table: "roles",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_safety_talks_tenant_id",
                schema: "brocal",
                table: "safety_talks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_safety_talks_tenant_id_held_at",
                schema: "brocal",
                table: "safety_talks",
                columns: new[] { "tenant_id", "held_at" });

            migrationBuilder.CreateIndex(
                name: "ix_service_types_tenant_id",
                schema: "brocal",
                table: "service_types",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_types_tenant_id_code",
                schema: "brocal",
                table: "service_types",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_types_tenant_id_name",
                schema: "brocal",
                table: "service_types",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_services_tenant_id",
                schema: "brocal",
                table: "services",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_services_tenant_id_code",
                schema: "brocal",
                table: "services",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_services_tenant_id_name",
                schema: "brocal",
                table: "services",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_sites_tenant_id",
                schema: "brocal",
                table: "sites",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sites_tenant_id_code",
                schema: "brocal",
                table: "sites",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sites_tenant_id_customer_id",
                schema: "brocal",
                table: "sites",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sites_tenant_id_name",
                schema: "brocal",
                table: "sites",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_levels_tenant_id",
                schema: "brocal",
                table: "stock_levels",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_levels_tenant_id_item_id_warehouse_id",
                schema: "brocal",
                table: "stock_levels",
                columns: new[] { "tenant_id", "item_id", "warehouse_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                schema: "brocal",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ticket_lines_service_id",
                schema: "brocal",
                table: "ticket_lines",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_lines_tenant_id",
                schema: "brocal",
                table: "ticket_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_lines_ticket_id",
                schema: "brocal",
                table: "ticket_lines",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_photos_tenant_id",
                schema: "brocal",
                table: "ticket_photos",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_photos_ticket_id",
                schema: "brocal",
                table: "ticket_photos",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_invoice_id",
                schema: "brocal",
                table: "tickets",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id",
                schema: "brocal",
                table: "tickets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id_client_id",
                schema: "brocal",
                table: "tickets",
                columns: new[] { "tenant_id", "client_id" },
                unique: true,
                filter: "client_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id_customer_id",
                schema: "brocal",
                table: "tickets",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id_folio_number",
                schema: "brocal",
                table: "tickets",
                columns: new[] { "tenant_id", "folio_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id_service_date",
                schema: "brocal",
                table: "tickets",
                columns: new[] { "tenant_id", "service_date" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id_status",
                schema: "brocal",
                table: "tickets",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_work_order_id",
                schema: "brocal",
                table: "tickets",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_tool_assignments_tenant_id",
                schema: "brocal",
                table: "tool_assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tool_assignments_tenant_id_crew_id",
                schema: "brocal",
                table: "tool_assignments",
                columns: new[] { "tenant_id", "crew_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_assignments_tenant_id_employee_id",
                schema: "brocal",
                table: "tool_assignments",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trips_tenant_id",
                schema: "brocal",
                table: "trips",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_trips_tenant_id_folio_number",
                schema: "brocal",
                table: "trips",
                columns: new[] { "tenant_id", "folio_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trips_tenant_id_unit_id",
                schema: "brocal",
                table: "trips",
                columns: new[] { "tenant_id", "unit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_unit_meter_readings_tenant_id",
                schema: "brocal",
                table: "unit_meter_readings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_meter_readings_tenant_id_unit_id_recorded_at",
                schema: "brocal",
                table: "unit_meter_readings",
                columns: new[] { "tenant_id", "unit_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_unit_positions_tenant_id",
                schema: "brocal",
                table: "unit_positions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_positions_tenant_id_unit_id_recorded_at",
                schema: "brocal",
                table: "unit_positions",
                columns: new[] { "tenant_id", "unit_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_unit_status_changes_tenant_id",
                schema: "brocal",
                table: "unit_status_changes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_status_changes_tenant_id_unit_id_changed_at",
                schema: "brocal",
                table: "unit_status_changes",
                columns: new[] { "tenant_id", "unit_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_units_tenant_id",
                schema: "brocal",
                table: "units",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_units_tenant_id_code",
                schema: "brocal",
                table: "units",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_units_tenant_id_name",
                schema: "brocal",
                table: "units",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_units_tenant_id_status",
                schema: "brocal",
                table: "units",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_tenant_id",
                schema: "brocal",
                table: "user_notifications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_tenant_id_deduplication_key",
                schema: "brocal",
                table: "user_notifications",
                columns: new[] { "tenant_id", "deduplication_key" },
                filter: "deduplication_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_tenant_id_membership_id_read_at",
                schema: "brocal",
                table: "user_notifications",
                columns: new[] { "tenant_id", "membership_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id",
                schema: "brocal",
                table: "warehouses",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id_code",
                schema: "brocal",
                table: "warehouses",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id_name",
                schema: "brocal",
                table: "warehouses",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_tenant_id",
                schema: "brocal",
                table: "work_orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_tenant_id_folio_number",
                schema: "brocal",
                table: "work_orders",
                columns: new[] { "tenant_id", "folio_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_tenant_id_scheduled_start",
                schema: "brocal",
                table: "work_orders",
                columns: new[] { "tenant_id", "scheduled_start" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_tenant_id_status",
                schema: "brocal",
                table: "work_orders",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_settings",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "api_keys",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "attendance",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "collection_notes",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "compliance_documents",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "compliance_requirements",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "contracts",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "crews",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "document_types",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "estimates",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "expense_items",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "files",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "folio_sequences",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "fuel_loads",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "incidents",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "inspection_templates",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "inspections",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "inventory_items",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "inventory_movements",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "maintenance_parts",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "maintenance_plan_units",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "memberships",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "notification_channels",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "notification_rules",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "payment_applications",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "price_list_items",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "quote_lines",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "safety_talks",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "service_types",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "services",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "sites",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "stock_levels",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "ticket_lines",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "ticket_photos",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "tool_assignments",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "trips",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "unit_meter_readings",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "unit_positions",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "unit_status_changes",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "units",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "user_notifications",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "warehouses",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "work_orders",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "expense_reports",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "maintenance_orders",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "maintenance_plans",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "price_lists",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "quotes",
                schema: "brocal");

            migrationBuilder.DropTable(
                name: "tickets",
                schema: "brocal");
        }
    }
}
