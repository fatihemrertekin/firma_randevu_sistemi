using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StaffWorkingHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffHoursAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffHoursAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffHoursAudits_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffHoursAudits_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffWorkingDays",
                columns: table => new
                {
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    OpensAtMinute = table.Column<int>(type: "integer", nullable: true),
                    ClosesAtMinute = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffWorkingDays", x => new { x.StaffMemberId, x.Day });
                    table.CheckConstraint("CK_StaffWorkingDays_Day", "\"Day\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_StaffWorkingDays_Hours", "(\"IsClosed\" AND \"OpensAtMinute\" IS NULL AND \"ClosesAtMinute\" IS NULL) OR (NOT \"IsClosed\" AND \"OpensAtMinute\" IS NOT NULL AND \"ClosesAtMinute\" IS NOT NULL AND \"OpensAtMinute\" BETWEEN 0 AND 1439 AND \"ClosesAtMinute\" BETWEEN 0 AND 1439 AND \"OpensAtMinute\" < \"ClosesAtMinute\")");
                    table.ForeignKey(
                        name: "FK_StaffWorkingDays_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffHoursAudits_ActorId",
                table: "StaffHoursAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffHoursAudits_StaffMemberId_MemberVersion",
                table: "StaffHoursAudits",
                columns: new[] { "StaffMemberId", "MemberVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffHoursAudits");

            migrationBuilder.DropTable(
                name: "StaffWorkingDays");
        }
    }
}
