using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DefinitionDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffMemberAudits_Kind",
                table: "StaffMemberAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServiceDefinitionAudits_Kind",
                table: "ServiceDefinitionAudits");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "StaffMembers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ServiceDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffMembers_DeletedInactive",
                table: "StaffMembers",
                sql: "NOT \"IsDeleted\" OR NOT \"IsActive\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffMemberAudits_Kind",
                table: "StaffMemberAudits",
                sql: "\"Kind\" IN ('Created','Renamed','Activated','Deactivated','Deleted')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServiceDefinitions_DeletedInactive",
                table: "ServiceDefinitions",
                sql: "NOT \"IsDeleted\" OR NOT \"IsActive\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServiceDefinitionAudits_Kind",
                table: "ServiceDefinitionAudits",
                sql: "\"Kind\" IN ('Created','Updated','Activated','Deactivated','Deleted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffMembers_DeletedInactive",
                table: "StaffMembers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffMemberAudits_Kind",
                table: "StaffMemberAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServiceDefinitions_DeletedInactive",
                table: "ServiceDefinitions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServiceDefinitionAudits_Kind",
                table: "ServiceDefinitionAudits");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "StaffMembers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ServiceDefinitions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffMemberAudits_Kind",
                table: "StaffMemberAudits",
                sql: "\"Kind\" IN ('Created','Renamed','Activated','Deactivated')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServiceDefinitionAudits_Kind",
                table: "ServiceDefinitionAudits",
                sql: "\"Kind\" IN ('Created','Updated','Activated','Deactivated')");
        }
    }
}
