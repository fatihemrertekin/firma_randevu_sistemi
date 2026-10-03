using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditLogIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StaffServiceAssignmentAudits_OccurredAt_Id",
                table: "StaffServiceAssignmentAudits",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_StaffPasswordResetAudits_OccurredAt_Id",
                table: "StaffPasswordResetAudits",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_StaffMemberAudits_OccurredAt_Id",
                table: "StaffMemberAudits",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_StaffInvitationAudits_OccurredAt_Id",
                table: "StaffInvitationAudits",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_StaffHoursAudits_OccurredAt_Id",
                table: "StaffHoursAudits",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_StaffDeactivationAudits_OccurredAt_Id",
                table: "StaffDeactivationAudits",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceDefinitionAudits_OccurredAt_Id",
                table: "ServiceDefinitionAudits",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPasswordResetAudits_OccurredAt_Id",
                table: "OwnerPasswordResetAudits",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerMfaRecoveryAudits_OccurredAt_Id",
                table: "OwnerMfaRecoveryAudits",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProfileAudits_OccurredAt_Id",
                table: "BusinessProfileAudits",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessLogoAudits_OccurredAt_Id",
                table: "BusinessLogoAudits",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessHoursAudits_OccurredAt_Id",
                table: "BusinessHoursAudits",
                columns: new[] { "OccurredAt", "Id" },
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StaffServiceAssignmentAudits_OccurredAt_Id",
                table: "StaffServiceAssignmentAudits");

            migrationBuilder.DropIndex(
                name: "IX_StaffPasswordResetAudits_OccurredAt_Id",
                table: "StaffPasswordResetAudits");

            migrationBuilder.DropIndex(
                name: "IX_StaffMemberAudits_OccurredAt_Id",
                table: "StaffMemberAudits");

            migrationBuilder.DropIndex(
                name: "IX_StaffInvitationAudits_OccurredAt_Id",
                table: "StaffInvitationAudits");

            migrationBuilder.DropIndex(
                name: "IX_StaffHoursAudits_OccurredAt_Id",
                table: "StaffHoursAudits");

            migrationBuilder.DropIndex(
                name: "IX_StaffDeactivationAudits_OccurredAt_Id",
                table: "StaffDeactivationAudits");

            migrationBuilder.DropIndex(
                name: "IX_ServiceDefinitionAudits_OccurredAt_Id",
                table: "ServiceDefinitionAudits");

            migrationBuilder.DropIndex(
                name: "IX_OwnerPasswordResetAudits_OccurredAt_Id",
                table: "OwnerPasswordResetAudits");

            migrationBuilder.DropIndex(
                name: "IX_OwnerMfaRecoveryAudits_OccurredAt_Id",
                table: "OwnerMfaRecoveryAudits");

            migrationBuilder.DropIndex(
                name: "IX_BusinessProfileAudits_OccurredAt_Id",
                table: "BusinessProfileAudits");

            migrationBuilder.DropIndex(
                name: "IX_BusinessLogoAudits_OccurredAt_Id",
                table: "BusinessLogoAudits");

            migrationBuilder.DropIndex(
                name: "IX_BusinessHoursAudits_OccurredAt_Id",
                table: "BusinessHoursAudits");
        }
    }
}
