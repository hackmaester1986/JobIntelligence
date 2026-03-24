using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameWorkdayTenantIdToHost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "workday_tenant_id",
                table: "companies",
                newName: "workday_host");

            migrationBuilder.RenameIndex(
                name: "IX_companies_workday_tenant_id",
                table: "companies",
                newName: "IX_companies_workday_host");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "workday_host",
                table: "companies",
                newName: "workday_tenant_id");

            migrationBuilder.RenameIndex(
                name: "IX_companies_workday_host",
                table: "companies",
                newName: "IX_companies_workday_tenant_id");
        }
    }
}
