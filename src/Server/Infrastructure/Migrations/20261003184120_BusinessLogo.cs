using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BusinessLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessLogoAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogoVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessLogoAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessLogoAudits_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessLogos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    Png = table.Column<byte[]>(type: "bytea", nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessLogos", x => x.Id);
                    table.CheckConstraint("CK_BusinessLogos_Image", "(\"Png\" IS NULL AND \"Width\" IS NULL AND \"Height\" IS NULL) OR (\"Png\" IS NOT NULL AND octet_length(\"Png\") BETWEEN 1 AND 1100000 AND \"Width\" IS NOT NULL AND \"Height\" IS NOT NULL AND \"Width\" BETWEEN 1 AND 512 AND \"Height\" BETWEEN 1 AND 512)");
                    table.CheckConstraint("CK_BusinessLogos_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.InsertData(
                table: "BusinessLogos",
                columns: new[] { "Id", "Height", "Png", "Version", "Width" },
                values: new object[] { 1, null, null, new Guid("a4ae913c-2ed5-4ebd-8b86-8ae3d311b348"), null });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessLogoAudits_ActorId",
                table: "BusinessLogoAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessLogoAudits_LogoVersion",
                table: "BusinessLogoAudits",
                column: "LogoVersion",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessLogoAudits");

            migrationBuilder.DropTable(
                name: "BusinessLogos");
        }
    }
}
