using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StaffServiceAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffServiceAssignmentAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MemberVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffServiceAssignmentAudits", x => x.Id);
                    table.CheckConstraint("CK_StaffServiceAssignmentAudits_Kind", "\"Kind\" IN ('Assigned','Unassigned')");
                    table.ForeignKey(
                        name: "FK_StaffServiceAssignmentAudits_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffServiceAssignmentAudits_ServiceDefinitions_ServiceDefi~",
                        column: x => x.ServiceDefinitionId,
                        principalTable: "ServiceDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffServiceAssignmentAudits_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffServiceAssignments",
                columns: table => new
                {
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDefinitionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffServiceAssignments", x => new { x.StaffMemberId, x.ServiceDefinitionId });
                    table.ForeignKey(
                        name: "FK_StaffServiceAssignments_ServiceDefinitions_ServiceDefinitio~",
                        column: x => x.ServiceDefinitionId,
                        principalTable: "ServiceDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffServiceAssignments_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffServiceAssignmentAudits_ActorId",
                table: "StaffServiceAssignmentAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffServiceAssignmentAudits_ServiceDefinitionId",
                table: "StaffServiceAssignmentAudits",
                column: "ServiceDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffServiceAssignmentAudits_StaffMemberId_ServiceDefinitio~",
                table: "StaffServiceAssignmentAudits",
                columns: new[] { "StaffMemberId", "ServiceDefinitionId", "MemberVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffServiceAssignments_ServiceDefinitionId",
                table: "StaffServiceAssignments",
                column: "ServiceDefinitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffServiceAssignmentAudits");

            migrationBuilder.DropTable(
                name: "StaffServiceAssignments");
        }
    }
}
