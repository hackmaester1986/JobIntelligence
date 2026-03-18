using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_sources",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    base_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    api_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    rate_limit_rps = table.Column<short>(type: "smallint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "skill_taxonomy",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    canonical_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    aliases = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    parent_skill_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skill_taxonomy", x => x.id);
                    table.ForeignKey(
                        name: "FK_skill_taxonomy_skill_taxonomy_parent_skill_id",
                        column: x => x.parent_skill_id,
                        principalTable: "skill_taxonomy",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "collection_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source_id = table.Column<int>(type: "integer", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "running"),
                    jobs_fetched = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    jobs_new = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    jobs_updated = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    jobs_removed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_collection_runs_job_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "job_sources",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    canonical_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    greenhouse_board_token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lever_company_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    industry = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    employee_count_range = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    linkedin_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    headquarters_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    headquarters_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    JobSourceId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.id);
                    table.ForeignKey(
                        name: "FK_companies_job_sources_JobSourceId",
                        column: x => x.JobSourceId,
                        principalTable: "job_sources",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "job_postings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_id = table.Column<int>(type: "integer", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    department = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    team = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    seniority_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    employment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    location_raw = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    location_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    location_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    location_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_remote = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_hybrid = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    salary_min = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    salary_max = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    salary_currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    salary_period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    salary_disclosed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    description_html = table.Column<string>(type: "text", nullable: true),
                    apply_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    apply_url_domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    posted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    removed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    repost_count = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    authenticity_score = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    authenticity_label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    authenticity_scored_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sagemaker_model_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    raw_data = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_postings", x => x.id);
                    table.ForeignKey(
                        name: "FK_job_postings_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_job_postings_job_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "job_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_skills",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_posting_id = table.Column<long>(type: "bigint", nullable: false),
                    skill_id = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    extraction_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_skills", x => x.id);
                    table.ForeignKey(
                        name: "FK_job_skills_job_postings_job_posting_id",
                        column: x => x.job_posting_id,
                        principalTable: "job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_job_skills_skill_taxonomy_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skill_taxonomy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_posting_id = table.Column<long>(type: "bigint", nullable: false),
                    snapshot_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    changed_fields = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    raw_data = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_job_snapshots_job_postings_job_posting_id",
                        column: x => x.job_posting_id,
                        principalTable: "job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ml_predictions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_posting_id = table.Column<long>(type: "bigint", nullable: false),
                    model_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    predicted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    authenticity_score = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    features = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    endpoint_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ml_predictions", x => x.id);
                    table.ForeignKey(
                        name: "FK_ml_predictions_job_postings_job_posting_id",
                        column: x => x.job_posting_id,
                        principalTable: "job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_collection_runs_source_id_started_at",
                table: "collection_runs",
                columns: new[] { "source_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_companies_greenhouse_board_token",
                table: "companies",
                column: "greenhouse_board_token");

            migrationBuilder.CreateIndex(
                name: "IX_companies_JobSourceId",
                table: "companies",
                column: "JobSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_companies_lever_company_slug",
                table: "companies",
                column: "lever_company_slug");

            migrationBuilder.CreateIndex(
                name: "IX_companies_normalized_name",
                table: "companies",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_authenticity_label_posted_at",
                table: "job_postings",
                columns: new[] { "authenticity_label", "posted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_company_id_posted_at",
                table: "job_postings",
                columns: new[] { "company_id", "posted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_is_active_posted_at",
                table: "job_postings",
                columns: new[] { "is_active", "posted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_is_remote_posted_at",
                table: "job_postings",
                columns: new[] { "is_remote", "posted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_seniority_level_posted_at",
                table: "job_postings",
                columns: new[] { "seniority_level", "posted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_source_id_external_id",
                table: "job_postings",
                columns: new[] { "source_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_skills_job_posting_id",
                table: "job_skills",
                column: "job_posting_id");

            migrationBuilder.CreateIndex(
                name: "IX_job_skills_job_posting_id_skill_id",
                table: "job_skills",
                columns: new[] { "job_posting_id", "skill_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_skills_skill_id_created_at",
                table: "job_skills",
                columns: new[] { "skill_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_job_snapshots_job_posting_id_snapshot_at",
                table: "job_snapshots",
                columns: new[] { "job_posting_id", "snapshot_at" });

            migrationBuilder.CreateIndex(
                name: "IX_job_sources_name",
                table: "job_sources",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ml_predictions_job_posting_id_predicted_at",
                table: "ml_predictions",
                columns: new[] { "job_posting_id", "predicted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_skill_taxonomy_canonical_name",
                table: "skill_taxonomy",
                column: "canonical_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_skill_taxonomy_parent_skill_id",
                table: "skill_taxonomy",
                column: "parent_skill_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_runs");

            migrationBuilder.DropTable(
                name: "job_skills");

            migrationBuilder.DropTable(
                name: "job_snapshots");

            migrationBuilder.DropTable(
                name: "ml_predictions");

            migrationBuilder.DropTable(
                name: "skill_taxonomy");

            migrationBuilder.DropTable(
                name: "job_postings");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "job_sources");
        }
    }
}
