using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBusinessLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessLogos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessLogos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    Png = table.Column<byte[]>(type: "bytea", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: true)
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
        }
    }
}
