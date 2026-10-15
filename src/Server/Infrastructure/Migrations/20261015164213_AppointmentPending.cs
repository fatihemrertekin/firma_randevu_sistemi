using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentPending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM "AppointmentTimeBlocks" WHERE "Status"='Pending') THEN
                    RAISE EXCEPTION 'Existing Pending rows need explicit expiry review before this migration.';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentStatusCommands_Request",
                table: "AppointmentStatusCommands");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits");

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingExpiresAt",
                table: "AppointmentTimeBlocks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorId",
                table: "AppointmentAudits",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentTimeBlocks_PendingExpiresAt_AppointmentId",
                table: "AppointmentTimeBlocks",
                columns: new[] { "PendingExpiresAt", "AppointmentId" },
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentTimeBlocks_Pending",
                table: "AppointmentTimeBlocks",
                sql: "(\"PendingExpiresAt\" IS NULL OR isfinite(\"PendingExpiresAt\")) AND (\"Status\" <> 'Pending' OR (\"PendingExpiresAt\" IS NOT NULL AND \"PendingExpiresAt\" <= \"StartsAt\"))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentStatusCommands_Request",
                table: "AppointmentStatusCommands",
                sql: "\"RequestKey\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ExpectedVersion\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Status\" IN ('Cancelled','Completed','NoShow','Confirmed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Actor",
                table: "AppointmentAudits",
                sql: "(\"Kind\" = 'Expired' AND \"ActorId\" IS NULL) OR (\"Kind\" <> 'Expired' AND \"ActorId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits",
                sql: "\"Kind\" IN ('Created','Cancelled','Completed','NoShow','Moved','Confirmed','Expired')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM "AppointmentTimeBlocks" WHERE "PendingExpiresAt" IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM "AppointmentAudits" WHERE "Kind" IN ('Confirmed','Expired') OR "ActorId" IS NULL)
                    OR EXISTS (SELECT 1 FROM "AppointmentStatusCommands" WHERE "Status"='Confirmed') THEN
                    RAISE EXCEPTION 'Pending history exists; use a compatible forward migration.';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_AppointmentTimeBlocks_PendingExpiresAt_AppointmentId",
                table: "AppointmentTimeBlocks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentTimeBlocks_Pending",
                table: "AppointmentTimeBlocks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentStatusCommands_Request",
                table: "AppointmentStatusCommands");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentAudits_Actor",
                table: "AppointmentAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits");

            migrationBuilder.DropColumn(
                name: "PendingExpiresAt",
                table: "AppointmentTimeBlocks");

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorId",
                table: "AppointmentAudits",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentStatusCommands_Request",
                table: "AppointmentStatusCommands",
                sql: "\"RequestKey\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ExpectedVersion\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Status\" IN ('Cancelled','Completed','NoShow')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits",
                sql: "\"Kind\" IN ('Created','Cancelled','Completed','NoShow','Moved')");
        }
    }
}
