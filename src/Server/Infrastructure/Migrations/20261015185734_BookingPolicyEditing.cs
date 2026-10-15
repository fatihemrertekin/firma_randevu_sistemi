using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BookingPolicyEditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingPolicyAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeadMinutes = table.Column<int>(type: "integer", nullable: false),
                    HorizonDays = table.Column<int>(type: "integer", nullable: false),
                    SlotMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPolicyAudits", x => x.Id);
                    table.CheckConstraint("CK_BookingPolicyAudits_Values", "\"PreviousVersion\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"PolicyVersion\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"PreviousVersion\" <> \"PolicyVersion\" AND \"LeadMinutes\" BETWEEN 0 AND 43200 AND \"HorizonDays\" BETWEEN 1 AND 365 AND \"LeadMinutes\" < \"HorizonDays\" * 1440 AND \"SlotMinutes\" BETWEEN 1 AND 60");
                    table.ForeignKey(
                        name: "FK_BookingPolicyAudits_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_BookingPolicies_Window",
                table: "BookingPolicies",
                sql: "\"LeadMinutes\" < \"HorizonDays\" * 1440");

            migrationBuilder.CreateIndex(
                name: "IX_BookingPolicyAudits_ActorId",
                table: "BookingPolicyAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingPolicyAudits_OccurredAt_Id",
                table: "BookingPolicyAudits",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_BookingPolicyAudits_PolicyVersion",
                table: "BookingPolicyAudits",
                column: "PolicyVersion",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM "BookingPolicyAudits") THEN
                    RAISE EXCEPTION 'Booking policy history must be preserved; use a forward fix.';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropTable(
                name: "BookingPolicyAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BookingPolicies_Window",
                table: "BookingPolicies");
        }
    }
}
