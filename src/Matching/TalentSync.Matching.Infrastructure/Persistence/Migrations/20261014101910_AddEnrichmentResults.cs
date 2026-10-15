using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TalentSync.Matching.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichmentResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnrichmentResults",
                schema: "matching",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CandidateId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CvHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Result = table.Column<string>(type: "json", nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrichmentResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_EnrichmentResults_CandidateId_CvHash",
                schema: "matching",
                table: "EnrichmentResults",
                columns: new[] { "CandidateId", "CvHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnrichmentResults",
                schema: "matching");
        }
    }
}
