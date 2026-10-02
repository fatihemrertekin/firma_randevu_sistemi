using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StaffMemberDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffMembers", x => x.Id);
                    table.CheckConstraint("CK_StaffMembers_Name", "length(btrim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "StaffMemberAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MemberVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffMemberAudits", x => x.Id);
                    table.CheckConstraint("CK_StaffMemberAudits_Kind", "\"Kind\" IN ('Created','Renamed','Activated','Deactivated')");
                    table.ForeignKey(
                        name: "FK_StaffMemberAudits_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffMemberAudits_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffMemberAudits_ActorId",
                table: "StaffMemberAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMemberAudits_StaffMemberId_MemberVersion",
                table: "StaffMemberAudits",
                columns: new[] { "StaffMemberId", "MemberVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffMembers_Name_Id",
                table: "StaffMembers",
                columns: new[] { "Name", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffMemberAudits");

            migrationBuilder.DropTable(
                name: "StaffMembers");
        }
    }
}
