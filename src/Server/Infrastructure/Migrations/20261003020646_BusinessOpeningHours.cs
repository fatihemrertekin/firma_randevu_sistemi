using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BusinessOpeningHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessHoursAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessHoursAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessHoursAudits_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessHoursSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    IsConfigured = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessHoursSchedules", x => x.Id);
                    table.CheckConstraint("CK_BusinessHoursSchedules_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "BusinessOpeningDays",
                columns: table => new
                {
                    ScheduleId = table.Column<int>(type: "integer", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    OpensAtMinute = table.Column<int>(type: "integer", nullable: true),
                    ClosesAtMinute = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessOpeningDays", x => new { x.ScheduleId, x.Day });
                    table.CheckConstraint("CK_BusinessOpeningDays_Day", "\"Day\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_BusinessOpeningDays_Hours", "(\"IsClosed\" AND \"OpensAtMinute\" IS NULL AND \"ClosesAtMinute\" IS NULL) OR (NOT \"IsClosed\" AND \"OpensAtMinute\" IS NOT NULL AND \"ClosesAtMinute\" IS NOT NULL AND \"OpensAtMinute\" BETWEEN 0 AND 1439 AND \"ClosesAtMinute\" BETWEEN 0 AND 1439 AND \"OpensAtMinute\" < \"ClosesAtMinute\")");
                    table.ForeignKey(
                        name: "FK_BusinessOpeningDays_BusinessHoursSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "BusinessHoursSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "BusinessHoursSchedules",
                columns: new[] { "Id", "IsConfigured", "Version" },
                values: new object[] { 1, false, new Guid("d317d899-8208-41f1-9b8e-c6fbde437cde") });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessHoursAudits_ActorId",
                table: "BusinessHoursAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessHoursAudits_ScheduleVersion",
                table: "BusinessHoursAudits",
                column: "ScheduleVersion",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessHoursAudits");

            migrationBuilder.DropTable(
                name: "BusinessOpeningDays");

            migrationBuilder.DropTable(
                name: "BusinessHoursSchedules");
        }
    }
}
