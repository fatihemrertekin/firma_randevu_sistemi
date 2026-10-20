using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BookingSmsBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingSmsBudgetPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UnitPriceTry = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    DailyCalls = table.Column<int>(type: "integer", nullable: false),
                    MonthlyCalls = table.Column<int>(type: "integer", nullable: false),
                    TotalCalls = table.Column<int>(type: "integer", nullable: false),
                    DailyTry = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    MonthlyTry = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    TotalTry = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingSmsBudgetPolicies", x => x.Id);
                    table.CheckConstraint("CK_BookingSmsBudgetPolicy_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND length(\"InstanceId\") BETWEEN 1 AND 100 AND \"KeyId\" ~ '^[0-9A-F]{64}$'");
                    table.CheckConstraint("CK_BookingSmsBudgetPolicy_Limits", "\"UnitPriceTry\" > 0 AND \"UnitPriceTry\" <= 0.50 AND \"DailyCalls\" BETWEEN 1 AND 20 AND \"MonthlyCalls\" BETWEEN 1 AND 200 AND \"TotalCalls\" BETWEEN 1 AND 200 AND \"DailyTry\" > 0 AND \"DailyTry\" <= 10 AND \"MonthlyTry\" > 0 AND \"MonthlyTry\" <= 100 AND \"TotalTry\" > 0 AND \"TotalTry\" <= 100");
                    table.CheckConstraint("CK_BookingSmsBudgetPolicy_Times", "isfinite(\"StartsAt\") AND isfinite(\"EndsAt\") AND \"EndsAt\" > \"StartsAt\" AND \"EndsAt\" <= \"StartsAt\" + INTERVAL '31 days' AND (\"RevokedAt\" IS NULL OR isfinite(\"RevokedAt\"))");
                });

            migrationBuilder.CreateTable(
                name: "BookingSmsAttempts",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservedTry = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    ReservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingSmsAttempts", x => new { x.JobId, x.Number });
                    table.CheckConstraint("CK_BookingSmsAttempt_Budget", "\"JobId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Number\" BETWEEN 1 AND 3 AND \"ReservedTry\" > 0 AND \"ReservedTry\" <= 0.50 AND isfinite(\"ReservedAt\") AND isfinite(\"RetainUntil\") AND \"RetainUntil\" >= \"ReservedAt\" + INTERVAL '30 days'");
                    table.CheckConstraint("CK_BookingSmsAttempt_Reference", "\"ProviderReference\" IS NULL OR \"ProviderReference\" ~ '^[0-9]{1,80}$'");
                    table.CheckConstraint("CK_BookingSmsAttempt_State", "(\"StartedAt\" IS NULL AND \"LeaseId\" IS NULL AND \"Outcome\" IN ('Reserved','Released') AND \"ProviderReference\" IS NULL) OR (\"StartedAt\" IS NOT NULL AND isfinite(\"StartedAt\") AND \"StartedAt\" >= \"ReservedAt\" AND \"LeaseId\" IS NOT NULL AND \"Outcome\" IN ('Started','Accepted','TemporaryFailure','PermanentFailure','Unknown'))");
                    table.ForeignKey(
                        name: "FK_BookingSmsAttempts_BookingSmsBudgetPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "BookingSmsBudgetPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingSmsAttempts_PolicyId_ReservedAt",
                table: "BookingSmsAttempts",
                columns: new[] { "PolicyId", "ReservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingSmsAttempts_ReservedAt",
                table: "BookingSmsAttempts",
                column: "ReservedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BookingSmsAttempts_RetainUntil",
                table: "BookingSmsAttempts",
                column: "RetainUntil");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "BookingSmsAttempts") OR EXISTS (SELECT 1 FROM "BookingSmsBudgetPolicies") THEN
                        RAISE EXCEPTION 'SMS bütçe onayı veya deneme tarihçesi varken Down uygulanamaz.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "BookingSmsAttempts");

            migrationBuilder.DropTable(
                name: "BookingSmsBudgetPolicies");
        }
    }
}
