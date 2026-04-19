using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResumeUploadTool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "resumes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    raw_text = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    location = table.Column<string>(type: "text", nullable: true),
                    years_of_experience = table.Column<int>(type: "integer", nullable: true),
                    education_level = table.Column<string>(type: "text", nullable: true),
                    education_field = table.Column<string>(type: "text", nullable: true),
                    skills = table.Column<string[]>(type: "jsonb", nullable: false),
                    recent_job_titles = table.Column<string[]>(type: "jsonb", nullable: false),
                    industries = table.Column<string[]>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resumes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resume_embeddings",
                columns: table => new
                {
                    resume_id = table.Column<long>(type: "bigint", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    embedding_text = table.Column<string>(type: "text", nullable: false),
                    embedded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resume_embeddings", x => x.resume_id);
                    table.ForeignKey(
                        name: "FK_resume_embeddings_resumes_resume_id",
                        column: x => x.resume_id,
                        principalTable: "resumes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resume_embeddings");

            migrationBuilder.DropTable(
                name: "resumes");
        }
    }
}
