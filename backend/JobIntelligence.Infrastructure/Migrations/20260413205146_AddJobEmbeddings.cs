using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace JobIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "job_embeddings",
                columns: table => new
                {
                    job_posting_id = table.Column<long>(type: "bigint", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    embedding_text = table.Column<string>(type: "text", nullable: false),
                    embedded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_embeddings", x => x.job_posting_id);
                    table.ForeignKey(
                        name: "FK_job_embeddings_job_postings_job_posting_id",
                        column: x => x.job_posting_id,
                        principalTable: "job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_embeddings");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
