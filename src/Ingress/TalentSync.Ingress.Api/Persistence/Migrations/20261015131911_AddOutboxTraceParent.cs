using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TalentSync.Ingress.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxTraceParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                schema: "ingress",
                table: "Outbox",
                type: "varchar(55)",
                unicode: false,
                maxLength: 55,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TraceParent",
                schema: "ingress",
                table: "Outbox");
        }
    }
}
