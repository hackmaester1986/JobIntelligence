using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebEnrichedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "WebEnrichedAt",
                table: "companies",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WebEnrichedAt",
                table: "companies");
        }
    }
}
