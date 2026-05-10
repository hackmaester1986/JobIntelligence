using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SplitRepostMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "avg_repost_count",
                table: "companies",
                newName: "repost_rate");

            migrationBuilder.AddColumn<int>(
                name: "total_repost_count",
                table: "companies",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "total_repost_count",
                table: "companies");

            migrationBuilder.RenameColumn(
                name: "repost_rate",
                table: "companies",
                newName: "avg_repost_count");
        }
    }
}
