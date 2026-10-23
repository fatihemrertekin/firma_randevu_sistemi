using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "BookingSmsAttempts",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Otp");

            migrationBuilder.CreateTable(
                name: "AppointmentReminderJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetApprovalId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProtectedPhone = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedOnceAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SuppressedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SuppressedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentReminderJobs", x => x.Id);
                    table.CheckConstraint("CK_AppointmentReminderJobs_Lease", "(\"Status\" = 'Processing' AND \"LeaseId\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL) OR (\"Status\" <> 'Processing' AND \"LeaseId\" IS NULL AND \"LeaseUntil\" IS NULL)");
                    table.CheckConstraint("CK_AppointmentReminderJobs_Payload", "(\"Status\" IN ('Pending','Processing') AND \"ProtectedPhone\" IS NOT NULL AND \"SuppressedAt\" IS NULL) OR (\"Status\" NOT IN ('Pending','Processing') AND \"ProtectedPhone\" IS NULL)");
                    table.CheckConstraint("CK_AppointmentReminderJobs_Started", "(\"Attempts\" = 0 AND \"StartedOnceAt\" IS NULL) OR (\"Attempts\" > 0 AND \"StartedOnceAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_AppointmentReminderJobs_State", "\"Status\" IN ('Pending','Processing','Accepted','Unknown','Failed','Cancelled','TooLate','Expired','Budget','Suppressed') AND \"Attempts\" BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_AppointmentReminderJobs_Time", "isfinite(\"StartsAt\") AND \"DueAt\" = \"StartsAt\" - INTERVAL '3 hours' AND \"Deadline\" = \"DueAt\" + INTERVAL '15 minutes' AND isfinite(\"NextAttemptAt\")");
                    table.ForeignKey(
                        name: "FK_AppointmentReminderJobs_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentReminderJobs_AspNetUsers_SuppressedBy",
                        column: x => x.SuppressedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_BookingSmsAttempts_Purpose",
                table: "BookingSmsAttempts",
                sql: "\"Purpose\" IN ('Otp','Reminder')");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminderJobs_AppointmentId",
                table: "AppointmentReminderJobs",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminderJobs_Status_NextAttemptAt",
                table: "AppointmentReminderJobs",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminderJobs_SuppressedBy",
                table: "AppointmentReminderJobs",
                column: "SuppressedBy");
            migrationBuilder.Sql("""
                CREATE FUNCTION guard_appointment_reminder_history() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF (OLD."StartedOnceAt" IS NOT NULL AND NEW."StartedOnceAt" IS DISTINCT FROM OLD."StartedOnceAt") OR
                       NEW."Attempts" < OLD."Attempts" OR
                       (OLD."SuppressedAt" IS NOT NULL AND (NEW."SuppressedAt" IS DISTINCT FROM OLD."SuppressedAt" OR
                        NEW."SuppressedBy" IS DISTINCT FROM OLD."SuppressedBy")) THEN
                        RAISE EXCEPTION 'Hatırlatma gönderim/engelleme tarihçesi sıfırlanamaz.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_AppointmentReminderJobs_History" BEFORE UPDATE ON "AppointmentReminderJobs"
                FOR EACH ROW EXECUTE FUNCTION guard_appointment_reminder_history();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM "AppointmentReminderJobs") OR
                   EXISTS (SELECT 1 FROM "BookingSmsAttempts" WHERE "Purpose" = 'Reminder') THEN
                    RAISE EXCEPTION 'Hatırlatma veya harcama kaydı varken Down uygulanamaz; kapalı imaj geri dönüşü kullanın.';
                END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "AppointmentReminderJobs");
            migrationBuilder.Sql("DROP FUNCTION guard_appointment_reminder_history();");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BookingSmsAttempts_Purpose",
                table: "BookingSmsAttempts");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "BookingSmsAttempts");
        }
    }
}
