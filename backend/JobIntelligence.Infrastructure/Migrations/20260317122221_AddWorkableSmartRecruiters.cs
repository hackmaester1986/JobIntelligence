using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkableSmartRecruiters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SmartRecruitersSlug",
                table: "companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkableSlug",
                table: "companies",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SmartRecruitersSlug",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "WorkableSlug",
                table: "companies");
        }
    }
}
