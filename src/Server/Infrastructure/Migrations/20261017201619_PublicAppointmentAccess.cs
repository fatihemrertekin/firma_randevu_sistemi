using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PublicAppointmentAccess : Migration
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

            migrationBuilder.CreateTable(
                name: "PublicAppointmentAccesses",
                columns: table => new
                {
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstIssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancellationRequestKey = table.Column<Guid>(type: "uuid", nullable: true),
                    CancellationExpectedVersion = table.Column<Guid>(type: "uuid", nullable: true),
                    CancellationAuditId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicAppointmentAccesses", x => x.AppointmentId);
                    table.CheckConstraint("CK_PublicAppointmentAccesses_Cancellation", "(\"CancellationRequestKey\" IS NULL AND \"CancellationExpectedVersion\" IS NULL AND \"CancellationAuditId\" IS NULL) OR (\"CancellationRequestKey\" IS NOT NULL AND \"CancellationRequestKey\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CancellationExpectedVersion\" IS NOT NULL AND \"CancellationExpectedVersion\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CancellationAuditId\" IS NOT NULL)");
                    table.CheckConstraint("CK_PublicAppointmentAccesses_Time", "isfinite(\"FirstIssuedAt\") AND isfinite(\"ExpiresAt\") AND \"ExpiresAt\" > \"FirstIssuedAt\" AND \"ExpiresAt\" <= \"FirstIssuedAt\" + INTERVAL '31 days'");
                    table.CheckConstraint("CK_PublicAppointmentAccesses_Token", "\"TokenHash\" ~ '^[0-9A-F]{64}$' AND \"Version\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_PublicAppointmentAccesses_AppointmentAudits_CancellationAud~",
                        column: x => x.CancellationAuditId,
                        principalTable: "AppointmentAudits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublicAppointmentAccesses_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Actor",
                table: "AppointmentAudits",
                sql: "(\"Kind\" IN ('Expired','PublicCreated','PublicCancelled') AND \"ActorId\" IS NULL) OR (\"Kind\" NOT IN ('Expired','PublicCreated','PublicCancelled') AND \"ActorId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits",
                sql: "\"Kind\" IN ('Created','PublicCreated','PublicCancelled','Cancelled','Completed','NoShow','Moved','Confirmed','Expired')");

            migrationBuilder.CreateIndex(
                name: "IX_PublicAppointmentAccesses_CancellationAuditId",
                table: "PublicAppointmentAccesses",
                column: "CancellationAuditId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "PublicAppointmentAccesses") OR
                       EXISTS (SELECT 1 FROM "AppointmentAudits" WHERE "Kind" = 'PublicCancelled') THEN
                        RAISE EXCEPTION 'Müşteri erişimi veya iptal tarihçesi varken geri migration uygulanamaz.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "PublicAppointmentAccesses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentAudits_Actor",
                table: "AppointmentAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Actor",
                table: "AppointmentAudits",
                sql: "(\"Kind\" IN ('Expired','PublicCreated') AND \"ActorId\" IS NULL) OR (\"Kind\" NOT IN ('Expired','PublicCreated') AND \"ActorId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentAudits_Kind",
                table: "AppointmentAudits",
                sql: "\"Kind\" IN ('Created','PublicCreated','Cancelled','Completed','NoShow','Moved','Confirmed','Expired')");
        }
    }
}
