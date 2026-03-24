using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyHiringStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "active_job_count",
                table: "companies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "avg_job_lifetime_days",
                table: "companies",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "avg_repost_count",
                table: "companies",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "duplicate_job_count",
                table: "companies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "remote_job_count",
                table: "companies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "salary_disclosure_rate",
                table: "companies",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "stats_computed_at",
                table: "companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "total_jobs_ever_seen",
                table: "companies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_companies_active_job_count",
                table: "companies",
                column: "active_job_count");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_companies_active_job_count",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "active_job_count",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "avg_job_lifetime_days",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "avg_repost_count",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "duplicate_job_count",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "remote_job_count",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "salary_disclosure_rate",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "stats_computed_at",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "total_jobs_ever_seen",
                table: "companies");
        }
    }
}
