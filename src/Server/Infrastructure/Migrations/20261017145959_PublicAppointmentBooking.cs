using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PublicAppointmentBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentAudits_Actor",
                table: "AppointmentAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits");

            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentId",
                table: "BookingVerifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConsumedAt",
                table: "BookingVerifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReplayUntil",
                table: "BookingVerifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorId",
                table: "Appointments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "BookingPolicyVersion",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerCancellationLeadMinutes",
                table: "Appointments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresBusinessApproval",
                table: "Appointments",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Appointments",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Owner");

            migrationBuilder.CreateIndex(
                name: "IX_BookingVerifications_AppointmentId",
                table: "BookingVerifications",
                column: "AppointmentId",
                unique: true,
                filter: "\"AppointmentId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BookingVerification_Consumption",
                table: "BookingVerifications",
                sql: "(\"AppointmentId\" IS NULL AND \"ConsumedAt\" IS NULL AND \"ReplayUntil\" IS NULL) OR (\"AppointmentId\" IS NOT NULL AND \"ConsumedAt\" IS NOT NULL AND isfinite(\"ConsumedAt\") AND \"ReplayUntil\" IS NOT NULL AND \"ReplayUntil\" = \"ConsumedAt\" + INTERVAL '24 hours' AND \"VerifiedAt\" IS NOT NULL AND \"VerifiedUntil\" IS NOT NULL AND \"ConsumedAt\" >= \"VerifiedAt\" AND \"ConsumedAt\" < \"VerifiedUntil\")");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_RequestKey",
                table: "Appointments",
                column: "RequestKey",
                unique: true,
                filter: "\"Source\" = 'Public'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_Source",
                table: "Appointments",
                sql: "(\"Source\" = 'Owner' AND \"ActorId\" IS NOT NULL AND \"BookingPolicyVersion\" IS NULL AND \"CustomerCancellationLeadMinutes\" IS NULL AND \"RequiresBusinessApproval\" IS NULL) OR (\"Source\" = 'Public' AND \"ActorId\" IS NULL AND \"BookingPolicyVersion\" IS NOT NULL AND \"BookingPolicyVersion\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CustomerCancellationLeadMinutes\" IS NOT NULL AND \"CustomerCancellationLeadMinutes\" BETWEEN 0 AND 43200 AND \"RequiresBusinessApproval\" IS NOT NULL AND \"CustomerPhone\" ~ '^\\+905[0-9]{9}$')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Actor",
                table: "AppointmentAudits",
                sql: "(\"Kind\" IN ('Expired','PublicCreated') AND \"ActorId\" IS NULL) OR (\"Kind\" NOT IN ('Expired','PublicCreated') AND \"ActorId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits",
                sql: "\"Kind\" IN ('Created','PublicCreated','Cancelled','Completed','NoShow','Moved','Confirmed','Expired')");

            migrationBuilder.AddForeignKey(
                name: "FK_BookingVerifications_Appointments_AppointmentId",
                table: "BookingVerifications",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM "Appointments" WHERE "Source" = 'Public')
                    OR EXISTS (SELECT 1 FROM "BookingVerifications" WHERE "ConsumedAt" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Müşteri randevusu veya tüketilmiş doğrulama varken Down uygulanamaz.';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_BookingVerifications_Appointments_AppointmentId",
                table: "BookingVerifications");

            migrationBuilder.DropIndex(
                name: "IX_BookingVerifications_AppointmentId",
                table: "BookingVerifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BookingVerification_Consumption",
                table: "BookingVerifications");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_RequestKey",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_Source",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentAudits_Actor",
                table: "AppointmentAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                table: "BookingVerifications");

            migrationBuilder.DropColumn(
                name: "ConsumedAt",
                table: "BookingVerifications");

            migrationBuilder.DropColumn(
                name: "ReplayUntil",
                table: "BookingVerifications");

            migrationBuilder.DropColumn(
                name: "BookingPolicyVersion",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "CustomerCancellationLeadMinutes",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "RequiresBusinessApproval",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Appointments");

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorId",
                table: "Appointments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Actor",
                table: "AppointmentAudits",
                sql: "(\"Kind\" = 'Expired' AND \"ActorId\" IS NULL) OR (\"Kind\" <> 'Expired' AND \"ActorId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits",
                sql: "\"Kind\" IN ('Created','Cancelled','Completed','NoShow','Moved','Confirmed','Expired')");
        }
    }
}
