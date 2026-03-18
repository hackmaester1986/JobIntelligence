using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixCompanyJobSourceRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_companies_job_sources_JobSourceId",
                table: "companies");

            migrationBuilder.DropIndex(
                name: "IX_companies_JobSourceId",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "JobSourceId",
                table: "companies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "JobSourceId",
                table: "companies",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_companies_JobSourceId",
                table: "companies",
                column: "JobSourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_companies_job_sources_JobSourceId",
                table: "companies",
                column: "JobSourceId",
                principalTable: "job_sources",
                principalColumn: "id");
        }
    }
}
