using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGeocoding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "job_postings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "job_postings",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "geocode_cache",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    city = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: true),
                    country = table.Column<string>(type: "text", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "nominatim"),
                    geocoded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_geocode_cache", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_geo",
                table: "job_postings",
                columns: new[] { "latitude", "longitude" },
                filter: "latitude IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_geocode_cache_location",
                table: "geocode_cache",
                columns: new[] { "city", "state", "country" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "geocode_cache");

            migrationBuilder.DropIndex(
                name: "IX_job_postings_geo",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "latitude",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "job_postings");
        }
    }
}
