using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TalentSync.Matching.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInboxAndParkingLot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "matching");

            migrationBuilder.CreateTable(
                name: "Inbox",
                schema: "matching",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    EventId = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ParkingLot",
                schema: "matching",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    EventId = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    PartitionId = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    Envelope = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParkedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkingLot", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Inbox_Source_EventId",
                schema: "matching",
                table: "Inbox",
                columns: new[] { "Source", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Inbox",
                schema: "matching");

            migrationBuilder.DropTable(
                name: "ParkingLot",
                schema: "matching");
        }
    }
}
