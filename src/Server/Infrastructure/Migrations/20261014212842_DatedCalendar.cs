using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DatedCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffHoursAudits_Kind",
                table: "StaffHoursAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessHoursAudits_Kind",
                table: "BusinessHoursAudits");

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "StaffHoursAudits",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Hours",
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldDefaultValue: "Hours");

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "BusinessHoursAudits",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Hours",
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldDefaultValue: "Hours");

            migrationBuilder.CreateTable(
                name: "BusinessDateExceptions",
                columns: table => new
                {
                    ScheduleId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    OpensAtMinute = table.Column<int>(type: "integer", nullable: true),
                    ClosesAtMinute = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessDateExceptions", x => new { x.ScheduleId, x.Date });
                    table.CheckConstraint("CK_BusinessDateExceptions_Date", "\"Date\" BETWEEN DATE '0001-01-01' AND DATE '9999-12-31'");
                    table.CheckConstraint("CK_BusinessDateExceptions_Hours", "(\"IsClosed\" AND \"OpensAtMinute\" IS NULL AND \"ClosesAtMinute\" IS NULL) OR (NOT \"IsClosed\" AND \"OpensAtMinute\" IS NOT NULL AND \"ClosesAtMinute\" IS NOT NULL AND \"OpensAtMinute\" BETWEEN 0 AND 1439 AND \"ClosesAtMinute\" BETWEEN 0 AND 1439 AND \"OpensAtMinute\" < \"ClosesAtMinute\")");
                    table.ForeignKey(
                        name: "FK_BusinessDateExceptions_BusinessHoursSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "BusinessHoursSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffDateExceptions",
                columns: table => new
                {
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    OpensAtMinute = table.Column<int>(type: "integer", nullable: true),
                    ClosesAtMinute = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffDateExceptions", x => new { x.StaffMemberId, x.Date });
                    table.CheckConstraint("CK_StaffDateExceptions_Date", "\"Date\" BETWEEN DATE '0001-01-01' AND DATE '9999-12-31'");
                    table.CheckConstraint("CK_StaffDateExceptions_Hours", "(\"IsClosed\" AND \"OpensAtMinute\" IS NULL AND \"ClosesAtMinute\" IS NULL) OR (NOT \"IsClosed\" AND \"OpensAtMinute\" IS NOT NULL AND \"ClosesAtMinute\" IS NOT NULL AND \"OpensAtMinute\" BETWEEN 0 AND 1439 AND \"ClosesAtMinute\" BETWEEN 0 AND 1439 AND \"OpensAtMinute\" < \"ClosesAtMinute\")");
                    table.ForeignKey(
                        name: "FK_StaffDateExceptions_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffLeaves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsRemoved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffLeaves", x => x.Id);
                    table.CheckConstraint("CK_StaffLeaves_RequestHash", "\"RequestHash\" ~ '^[0-9A-F]{64}$'");
                    table.CheckConstraint("CK_StaffLeaves_Time", "isfinite(\"StartsAt\") AND isfinite(\"EndsAt\") AND \"StartsAt\" < \"EndsAt\" AND \"EndsAt\" - \"StartsAt\" <= INTERVAL '366 days' AND date_trunc('minute', \"StartsAt\" AT TIME ZONE 'UTC') = \"StartsAt\" AT TIME ZONE 'UTC' AND date_trunc('minute', \"EndsAt\" AT TIME ZONE 'UTC') = \"EndsAt\" AT TIME ZONE 'UTC'");
                    table.ForeignKey(
                        name: "FK_StaffLeaves_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessDateBreaks",
                columns: table => new
                {
                    ScheduleId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    StartsAtMinute = table.Column<int>(type: "integer", nullable: false),
                    EndsAtMinute = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessDateBreaks", x => new { x.ScheduleId, x.Date, x.Position });
                    table.CheckConstraint("CK_BusinessDateBreaks_Time", "\"Position\" BETWEEN 1 AND 8 AND \"StartsAtMinute\" BETWEEN 0 AND 1439 AND \"EndsAtMinute\" BETWEEN 0 AND 1439 AND \"StartsAtMinute\" < \"EndsAtMinute\"");
                    table.ForeignKey(
                        name: "FK_BusinessDateBreaks_BusinessDateExceptions_ScheduleId_Date",
                        columns: x => new { x.ScheduleId, x.Date },
                        principalTable: "BusinessDateExceptions",
                        principalColumns: new[] { "ScheduleId", "Date" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffDateBreaks",
                columns: table => new
                {
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    StartsAtMinute = table.Column<int>(type: "integer", nullable: false),
                    EndsAtMinute = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffDateBreaks", x => new { x.StaffMemberId, x.Date, x.Position });
                    table.CheckConstraint("CK_StaffDateBreaks_Time", "\"Position\" BETWEEN 1 AND 8 AND \"StartsAtMinute\" BETWEEN 0 AND 1439 AND \"EndsAtMinute\" BETWEEN 0 AND 1439 AND \"StartsAtMinute\" < \"EndsAtMinute\"");
                    table.ForeignKey(
                        name: "FK_StaffDateBreaks_StaffDateExceptions_StaffMemberId_Date",
                        columns: x => new { x.StaffMemberId, x.Date },
                        principalTable: "StaffDateExceptions",
                        principalColumns: new[] { "StaffMemberId", "Date" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffHoursAudits_Kind",
                table: "StaffHoursAudits",
                sql: "\"Kind\" IN ('Hours','Breaks','Exception','Leave')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessHoursAudits_Kind",
                table: "BusinessHoursAudits",
                sql: "\"Kind\" IN ('Hours','Breaks','Exception')");

            migrationBuilder.CreateIndex(
                name: "IX_StaffLeaves_StaffMemberId_StartsAt_Id",
                table: "StaffLeaves",
                columns: new[] { "StaffMemberId", "StartsAt", "Id" });
            migrationBuilder.Sql("""
                ALTER TABLE "BusinessDateBreaks" ADD CONSTRAINT "EX_BusinessDateBreaks_Time"
                  EXCLUDE USING gist ("ScheduleId" WITH =, "Date" WITH =, int4range("StartsAtMinute", "EndsAtMinute", '[)') WITH &&);
                ALTER TABLE "StaffDateBreaks" ADD CONSTRAINT "EX_StaffDateBreaks_Time"
                  EXCLUDE USING gist ("StaffMemberId" WITH =, "Date" WITH =, int4range("StartsAtMinute", "EndsAtMinute", '[)') WITH &&);
                ALTER TABLE "StaffLeaves" ADD CONSTRAINT "EX_StaffLeaves_Time"
                  EXCLUDE USING gist ("StaffMemberId" WITH =, tstzrange("StartsAt", "EndsAt", '[)') WITH &&) WHERE (NOT "IsRemoved");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tarihli takvim veya tekrar kimliği/geçmişi kaybedilerek eski kurala dönülmez.
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM "BusinessDateExceptions") OR EXISTS (SELECT 1 FROM "StaffDateExceptions")
                    OR EXISTS (SELECT 1 FROM "StaffLeaves")
                    OR EXISTS (SELECT 1 FROM "BusinessHoursAudits" WHERE "Kind"='Exception')
                    OR EXISTS (SELECT 1 FROM "StaffHoursAudits" WHERE "Kind" IN ('Exception','Leave'))
                  THEN RAISE EXCEPTION 'Dated calendar or history requires a compatible forward fix'; END IF;
                END $guard$;
                """);
            migrationBuilder.DropTable(
                name: "BusinessDateBreaks");

            migrationBuilder.DropTable(
                name: "StaffDateBreaks");

            migrationBuilder.DropTable(
                name: "StaffLeaves");

            migrationBuilder.DropTable(
                name: "BusinessDateExceptions");

            migrationBuilder.DropTable(
                name: "StaffDateExceptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffHoursAudits_Kind",
                table: "StaffHoursAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessHoursAudits_Kind",
                table: "BusinessHoursAudits");

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "StaffHoursAudits",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Hours",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "Hours");

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "BusinessHoursAudits",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Hours",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "Hours");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffHoursAudits_Kind",
                table: "StaffHoursAudits",
                sql: "\"Kind\" IN ('Hours','Breaks')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessHoursAudits_Kind",
                table: "BusinessHoursAudits",
                sql: "\"Kind\" IN ('Hours','Breaks')");
        }
    }
}
