using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WeeklyBreaks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "StaffHoursAudits",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Hours");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "BusinessHoursAudits",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Hours");

            migrationBuilder.CreateTable(
                name: "BusinessWeeklyBreaks",
                columns: table => new
                {
                    ScheduleId = table.Column<int>(type: "integer", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    StartsAtMinute = table.Column<int>(type: "integer", nullable: false),
                    EndsAtMinute = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessWeeklyBreaks", x => new { x.ScheduleId, x.Day, x.Position });
                    table.CheckConstraint("CK_BusinessWeeklyBreaks_Time", "\"Day\" BETWEEN 0 AND 6 AND \"Position\" BETWEEN 1 AND 8 AND \"StartsAtMinute\" BETWEEN 0 AND 1439 AND \"EndsAtMinute\" BETWEEN 0 AND 1439 AND \"StartsAtMinute\" < \"EndsAtMinute\"");
                    table.ForeignKey(
                        name: "FK_BusinessWeeklyBreaks_BusinessOpeningDays_ScheduleId_Day",
                        columns: x => new { x.ScheduleId, x.Day },
                        principalTable: "BusinessOpeningDays",
                        principalColumns: new[] { "ScheduleId", "Day" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffWeeklyBreaks",
                columns: table => new
                {
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    StartsAtMinute = table.Column<int>(type: "integer", nullable: false),
                    EndsAtMinute = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffWeeklyBreaks", x => new { x.StaffMemberId, x.Day, x.Position });
                    table.CheckConstraint("CK_StaffWeeklyBreaks_Time", "\"Day\" BETWEEN 0 AND 6 AND \"Position\" BETWEEN 1 AND 8 AND \"StartsAtMinute\" BETWEEN 0 AND 1439 AND \"EndsAtMinute\" BETWEEN 0 AND 1439 AND \"StartsAtMinute\" < \"EndsAtMinute\"");
                    table.ForeignKey(
                        name: "FK_StaffWeeklyBreaks_StaffWorkingDays_StaffMemberId_Day",
                        columns: x => new { x.StaffMemberId, x.Day },
                        principalTable: "StaffWorkingDays",
                        principalColumns: new[] { "StaffMemberId", "Day" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffHoursAudits_Kind",
                table: "StaffHoursAudits",
                sql: "\"Kind\" IN ('Hours','Breaks')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessHoursAudits_Kind",
                table: "BusinessHoursAudits",
                sql: "\"Kind\" IN ('Hours','Breaks')");
            migrationBuilder.Sql("""
                ALTER TABLE "BusinessWeeklyBreaks" ADD CONSTRAINT "EX_BusinessWeeklyBreaks_Time"
                  EXCLUDE USING gist ("ScheduleId" WITH =, "Day" WITH =, int4range("StartsAtMinute", "EndsAtMinute", '[)') WITH &&);
                ALTER TABLE "StaffWeeklyBreaks" ADD CONSTRAINT "EX_StaffWeeklyBreaks_Time"
                  EXCLUDE USING gist ("StaffMemberId" WITH =, "Day" WITH =, int4range("StartsAtMinute", "EndsAtMinute", '[)') WITH &&);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Mola tanımını veya işlem geçmişinin anlamını silerek eski kurala dönme.
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM "BusinessWeeklyBreaks") OR EXISTS (SELECT 1 FROM "StaffWeeklyBreaks")
                    OR EXISTS (SELECT 1 FROM "BusinessHoursAudits" WHERE "Kind"='Breaks')
                    OR EXISTS (SELECT 1 FROM "StaffHoursAudits" WHERE "Kind"='Breaks')
                  THEN RAISE EXCEPTION 'Break configuration or history requires a compatible forward fix'; END IF;
                END $guard$;
                """);
            migrationBuilder.DropTable(
                name: "BusinessWeeklyBreaks");

            migrationBuilder.DropTable(
                name: "StaffWeeklyBreaks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffHoursAudits_Kind",
                table: "StaffHoursAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessHoursAudits_Kind",
                table: "BusinessHoursAudits");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "StaffHoursAudits");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "BusinessHoursAudits");
        }
    }
}
