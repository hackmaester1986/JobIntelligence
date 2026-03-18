using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMlFeatureFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description_hash",
                table: "job_postings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "previous_posting_id",
                table: "job_postings",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "company_job_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    collection_run_id = table.Column<long>(type: "bigint", nullable: false),
                    snapshot_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    active_job_count = table.Column<int>(type: "integer", nullable: false),
                    new_count = table.Column<int>(type: "integer", nullable: false),
                    removed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_job_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_job_snapshots_collection_runs_collection_run_id",
                        column: x => x.collection_run_id,
                        principalTable: "collection_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_company_job_snapshots_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_company_id_description_hash",
                table: "job_postings",
                columns: new[] { "company_id", "description_hash" });

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_previous_posting_id",
                table: "job_postings",
                column: "previous_posting_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_job_snapshots_collection_run_id",
                table: "company_job_snapshots",
                column: "collection_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_job_snapshots_company_id_snapshot_at",
                table: "company_job_snapshots",
                columns: new[] { "company_id", "snapshot_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_job_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_job_postings_company_id_description_hash",
                table: "job_postings");

            migrationBuilder.DropIndex(
                name: "IX_job_postings_previous_posting_id",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "description_hash",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "previous_posting_id",
                table: "job_postings");
        }
    }
}
