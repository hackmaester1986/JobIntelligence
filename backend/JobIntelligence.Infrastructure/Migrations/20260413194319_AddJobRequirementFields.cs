using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobRequirementFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EducationLevel",
                table: "job_postings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresUsAuthorization",
                table: "job_postings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YearsExperienceMax",
                table: "job_postings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YearsExperienceMin",
                table: "job_postings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EducationLevel",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "RequiresUsAuthorization",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "YearsExperienceMax",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "YearsExperienceMin",
                table: "job_postings");
        }
    }
}
