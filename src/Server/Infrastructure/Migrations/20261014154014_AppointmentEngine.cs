using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CustomerPhone = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.CheckConstraint("CK_Appointments_Customer", "length(btrim(\"CustomerName\")) BETWEEN 2 AND 100 AND \"CustomerPhone\" ~ '^\\+90[2345][0-9]{9}$'");
                    table.CheckConstraint("CK_Appointments_Request", "\"RequestKey\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"RequestHash\" ~ '^[0-9A-F]{64}$'");
                    table.ForeignKey(
                        name: "FK_Appointments_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    LeadMinutes = table.Column<int>(type: "integer", nullable: false),
                    HorizonDays = table.Column<int>(type: "integer", nullable: false),
                    SlotMinutes = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPolicies", x => x.Id);
                    table.CheckConstraint("CK_BookingPolicies_Values", "\"Id\" = 1 AND \"LeadMinutes\" BETWEEN 0 AND 43200 AND \"HorizonDays\" BETWEEN 1 AND 365 AND \"SlotMinutes\" BETWEEN 1 AND 60");
                });

            migrationBuilder.CreateTable(
                name: "AppointmentAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentAudits_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentAudits_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentTimeBlocks",
                columns: table => new
                {
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Occupied = table.Column<NpgsqlRange<DateTime>>(type: "tstzrange", nullable: false, computedColumnSql: "tstzrange(\"StartsAt\", \"EndsAt\", '[)')", stored: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentTimeBlocks", x => x.AppointmentId);
                    table.CheckConstraint("CK_AppointmentTimeBlocks_Snapshot", "length(btrim(\"StaffName\")) > 0 AND length(btrim(\"ServiceName\")) > 0 AND \"Price\" BETWEEN 0 AND 999999.99 AND \"Currency\" = 'TRY'");
                    table.CheckConstraint("CK_AppointmentTimeBlocks_Status", "\"Status\" IN ('Pending','Confirmed','Completed','NoShow','Cancelled')");
                    table.CheckConstraint("CK_AppointmentTimeBlocks_Time", "isfinite(\"StartsAt\") AND isfinite(\"EndsAt\") AND \"EndsAt\" > \"StartsAt\" AND \"DurationMinutes\" BETWEEN 1 AND 1440 AND \"EndsAt\" = \"StartsAt\" + \"DurationMinutes\" * INTERVAL '1 minute'");
                    table.ForeignKey(
                        name: "FK_AppointmentTimeBlocks_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentTimeBlocks_ServiceDefinitions_ServiceDefinitionId",
                        column: x => x.ServiceDefinitionId,
                        principalTable: "ServiceDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentTimeBlocks_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                ALTER TABLE "AppointmentTimeBlocks" ADD CONSTRAINT "EX_AppointmentTimeBlocks_StaffTime"
                EXCLUDE USING gist ("StaffMemberId" WITH =, "Occupied" WITH &&)
                WHERE ("Status" <> 'Cancelled');
                """);
            migrationBuilder.InsertData(
                table: "BookingPolicies",
                columns: new[] { "Id", "HorizonDays", "LeadMinutes", "SlotMinutes", "Version" },
                values: new object[] { 1, 30, 60, 15, new Guid("4928211e-eaba-4086-8a82-f2ca1a83908e") });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentAudits_ActorId",
                table: "AppointmentAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentAudits_AppointmentId_AppointmentVersion",
                table: "AppointmentAudits",
                columns: new[] { "AppointmentId", "AppointmentVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ActorId_RequestKey",
                table: "Appointments",
                columns: new[] { "ActorId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentTimeBlocks_ServiceDefinitionId",
                table: "AppointmentTimeBlocks",
                column: "ServiceDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentTimeBlocks_StaffMemberId_StartsAt",
                table: "AppointmentTimeBlocks",
                columns: new[] { "StaffMemberId", "StartsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentAudits");

            migrationBuilder.DropTable(
                name: "AppointmentTimeBlocks");

            migrationBuilder.DropTable(
                name: "BookingPolicies");

            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");
        }
    }
}
