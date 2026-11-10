using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChronoLith.Brocal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstallationLicense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installation_license",
                schema: "brocal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "text", nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    uploaded_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_license", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installation_license",
                schema: "brocal");
        }
    }
}
