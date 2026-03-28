using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsUsPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_us_posting",
                table: "job_postings",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_us_posting",
                table: "job_postings");
        }
    }
}
