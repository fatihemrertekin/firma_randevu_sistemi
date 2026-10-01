using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class P02BusinessProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessProfileAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessProfileAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessProfileAudits_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessProfiles", x => x.Id);
                    table.CheckConstraint("CK_BusinessProfiles_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.InsertData(
                table: "BusinessProfiles",
                columns: new[] { "Id", "Address", "Email", "Name", "Phone", "Version" },
                values: new object[] { 1, null, null, "", null, new Guid("21dd6c8a-8755-4111-a7f1-a527f9b1c6b3") });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProfileAudits_ActorId",
                table: "BusinessProfileAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProfileAudits_ProfileVersion",
                table: "BusinessProfileAudits",
                column: "ProfileVersion",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessProfileAudits");

            migrationBuilder.DropTable(
                name: "BusinessProfiles");
        }
    }
}
