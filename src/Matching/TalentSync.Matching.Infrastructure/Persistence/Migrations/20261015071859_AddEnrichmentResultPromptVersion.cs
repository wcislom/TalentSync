using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TalentSync.Matching.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichmentResultPromptVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_EnrichmentResults_CandidateId_CvHash",
                schema: "matching",
                table: "EnrichmentResults");

            // Rows stored before prompt versioning. No computed version equals "unversioned", so these CVs are
            // enriched again on their next qualification instead of replaying an answer to an unknown prompt.
            migrationBuilder.AddColumn<string>(
                name: "PromptVersion",
                schema: "matching",
                table: "EnrichmentResults",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                defaultValue: "unversioned");

            migrationBuilder.CreateIndex(
                name: "UX_EnrichmentResults_CandidateId_CvHash_PromptVersion",
                schema: "matching",
                table: "EnrichmentResults",
                columns: new[] { "CandidateId", "CvHash", "PromptVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_EnrichmentResults_CandidateId_CvHash_PromptVersion",
                schema: "matching",
                table: "EnrichmentResults");

            migrationBuilder.DropColumn(
                name: "PromptVersion",
                schema: "matching",
                table: "EnrichmentResults");

            migrationBuilder.CreateIndex(
                name: "UX_EnrichmentResults_CandidateId_CvHash",
                schema: "matching",
                table: "EnrichmentResults",
                columns: new[] { "CandidateId", "CvHash" },
                unique: true);
        }
    }
}
