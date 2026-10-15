using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentMove : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits");

            migrationBuilder.CreateTable(
                name: "AppointmentMoveCommands",
                columns: table => new
                {
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentMoveCommands", x => new { x.ActorId, x.RequestKey });
                    table.CheckConstraint("CK_AppointmentMoveCommands_Request", "\"RequestKey\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ExpectedVersion\" <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(\"StartsAt\") AND date_trunc('minute', \"StartsAt\" AT TIME ZONE 'UTC') = (\"StartsAt\" AT TIME ZONE 'UTC')");
                    table.ForeignKey(
                        name: "FK_AppointmentMoveCommands_AppointmentAudits_AuditId",
                        column: x => x.AuditId,
                        principalTable: "AppointmentAudits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentMoveCommands_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentMoveCommands_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentMoveCommands_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits",
                sql: "\"Kind\" IN ('Created','Cancelled','Completed','NoShow','Moved')");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentMoveCommands_AppointmentId",
                table: "AppointmentMoveCommands",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentMoveCommands_AuditId",
                table: "AppointmentMoveCommands",
                column: "AuditId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentMoveCommands_StaffMemberId",
                table: "AppointmentMoveCommands",
                column: "StaffMemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM "AppointmentMoveCommands") OR EXISTS (SELECT 1 FROM "AppointmentAudits" WHERE "Kind"='Moved') THEN
                    RAISE EXCEPTION 'Appointment move history exists; use a compatible forward migration.';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropTable(
                name: "AppointmentMoveCommands");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits",
                sql: "\"Kind\" IN ('Created','Cancelled','Completed','NoShow')");
        }
    }
}

