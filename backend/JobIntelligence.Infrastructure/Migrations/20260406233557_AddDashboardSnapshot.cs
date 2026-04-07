using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dashboard_snapshots",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    snapshot_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_us = table.Column<bool>(type: "boolean", nullable: false),
                    total_active_jobs = table.Column<long>(type: "bigint", nullable: false),
                    total_companies = table.Column<long>(type: "bigint", nullable: false),
                    remote_jobs = table.Column<long>(type: "bigint", nullable: false),
                    hybrid_jobs = table.Column<long>(type: "bigint", nullable: false),
                    onsite_jobs = table.Column<long>(type: "bigint", nullable: false),
                    active_today = table.Column<long>(type: "bigint", nullable: false),
                    top_companies = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    jobs_by_seniority = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    top_departments = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dashboard_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dashboard_snapshots_is_us_snapshot_at",
                table: "dashboard_snapshots",
                columns: new[] { "is_us", "snapshot_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dashboard_snapshots");
        }
    }
}
