using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "AppointmentAudits",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Created");

            migrationBuilder.CreateTable(
                name: "AppointmentStatusCommands",
                columns: table => new
                {
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentStatusCommands", x => new { x.ActorId, x.RequestKey });
                    table.CheckConstraint("CK_AppointmentStatusCommands_Request", "\"RequestKey\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ExpectedVersion\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Status\" IN ('Cancelled','Completed','NoShow')");
                    table.ForeignKey(
                        name: "FK_AppointmentStatusCommands_AppointmentAudits_AuditId",
                        column: x => x.AuditId,
                        principalTable: "AppointmentAudits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentStatusCommands_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentStatusCommands_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits",
                sql: "\"Kind\" IN ('Created','Cancelled','Completed','NoShow')");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentStatusCommands_AppointmentId",
                table: "AppointmentStatusCommands",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentStatusCommands_AuditId",
                table: "AppointmentStatusCommands",
                column: "AuditId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM "AppointmentStatusCommands") OR EXISTS (SELECT 1 FROM "AppointmentAudits" WHERE "Kind" <> 'Created') THEN
                    RAISE EXCEPTION 'Appointment status history exists; use a compatible forward migration.';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropTable(
                name: "AppointmentStatusCommands");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "AppointmentAudits");
        }
    }
}
