using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CustomerBookingPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomerCancellationLeadMinutes",
                table: "BookingPolicyAudits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresBusinessApproval",
                table: "BookingPolicyAudits",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerCancellationLeadMinutes",
                table: "BookingPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresBusinessApproval",
                table: "BookingPolicies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BookingPolicyAudits_CustomerPolicy",
                table: "BookingPolicyAudits",
                sql: "(\"CustomerCancellationLeadMinutes\" IS NULL AND \"RequiresBusinessApproval\" IS NULL) OR (\"CustomerCancellationLeadMinutes\" IS NOT NULL AND \"CustomerCancellationLeadMinutes\" BETWEEN 0 AND 43200 AND \"RequiresBusinessApproval\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BookingPolicies_CustomerCancellation",
                table: "BookingPolicies",
                sql: "\"CustomerCancellationLeadMinutes\" BETWEEN 0 AND 43200");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM "BookingPolicies" WHERE "CustomerCancellationLeadMinutes" <> 120 OR NOT "RequiresBusinessApproval")
                    OR EXISTS (SELECT 1 FROM "BookingPolicyAudits" WHERE "CustomerCancellationLeadMinutes" IS NOT NULL OR "RequiresBusinessApproval" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Customer booking policy and history must be preserved; use a forward fix.';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_BookingPolicyAudits_CustomerPolicy",
                table: "BookingPolicyAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BookingPolicies_CustomerCancellation",
                table: "BookingPolicies");

            migrationBuilder.DropColumn(
                name: "CustomerCancellationLeadMinutes",
                table: "BookingPolicyAudits");

            migrationBuilder.DropColumn(
                name: "RequiresBusinessApproval",
                table: "BookingPolicyAudits");

            migrationBuilder.DropColumn(
                name: "CustomerCancellationLeadMinutes",
                table: "BookingPolicies");

            migrationBuilder.DropColumn(
                name: "RequiresBusinessApproval",
                table: "BookingPolicies");
        }
    }
}
