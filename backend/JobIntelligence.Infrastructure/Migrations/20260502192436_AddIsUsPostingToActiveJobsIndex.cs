using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsUsPostingToActiveJobsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_job_postings_active_company_id",
                table: "job_postings");

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_active_company_id",
                table: "job_postings",
                column: "company_id",
                filter: "is_active = true")
                .Annotation("Npgsql:IndexInclude", new[] { "is_remote", "is_hybrid", "first_seen_at", "is_us_posting" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_job_postings_active_company_id",
                table: "job_postings");

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_active_company_id",
                table: "job_postings",
                column: "company_id",
                filter: "is_active = true")
                .Annotation("Npgsql:IndexInclude", new[] { "is_remote", "is_hybrid", "first_seen_at" });
        }
    }
}
