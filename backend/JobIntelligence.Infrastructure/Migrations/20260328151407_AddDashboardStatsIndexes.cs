using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardStatsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_companies_active_job_count",
                table: "companies");

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_active_company_id",
                table: "job_postings",
                column: "company_id",
                filter: "is_active = true")
                .Annotation("Npgsql:IndexInclude", new[] { "is_remote", "is_hybrid", "first_seen_at" });

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_active_department",
                table: "job_postings",
                column: "department",
                filter: "is_active = true AND department IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_active_seniority",
                table: "job_postings",
                column: "seniority_level",
                filter: "is_active = true AND seniority_level IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_companies_tech_hiring_active_job_count",
                table: "companies",
                column: "active_job_count",
                descending: new bool[0],
                filter: "is_tech_hiring IS NOT FALSE AND active_job_count > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_job_postings_active_company_id",
                table: "job_postings");

            migrationBuilder.DropIndex(
                name: "IX_job_postings_active_department",
                table: "job_postings");

            migrationBuilder.DropIndex(
                name: "IX_job_postings_active_seniority",
                table: "job_postings");

            migrationBuilder.DropIndex(
                name: "IX_companies_tech_hiring_active_job_count",
                table: "companies");

            migrationBuilder.CreateIndex(
                name: "IX_companies_active_job_count",
                table: "companies",
                column: "active_job_count");
        }
    }
}
