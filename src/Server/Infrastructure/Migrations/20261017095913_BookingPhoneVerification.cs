using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BookingPhoneVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingSmsPeriods",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingSmsPeriods", x => x.Key);
                    table.CheckConstraint("CK_BookingSmsPeriod_Attempts", "\"Attempts\" BETWEEN 0 AND 2000");
                });

            migrationBuilder.CreateTable(
                name: "BookingVerificationQuotaEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PhoneHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingVerificationQuotaEvents", x => x.Id);
                    table.CheckConstraint("CK_BookingVerificationQuota_Kind", "\"KeyId\" ~ '^[0-9A-F]{64}$' AND \"Kind\" IN ('Send','Wrong') AND \"RetainUntil\" > \"OccurredAt\"");
                });

            migrationBuilder.CreateTable(
                name: "BookingVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PhoneHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProtectedPhone = table.Column<string>(type: "text", nullable: true),
                    UsedCodeHashes = table.Column<string[]>(type: "text[]", nullable: false),
                    CodeVersion = table.Column<int>(type: "integer", nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResendAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingVerifications", x => x.Id);
                    table.CheckConstraint("CK_BookingVerification_Attempts", "\"FailedAttempts\" BETWEEN 0 AND 5 AND \"CodeVersion\" BETWEEN 1 AND 3 AND cardinality(\"UsedCodeHashes\") BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_BookingVerification_Hashes", "\"RequestHash\" ~ '^[0-9A-F]{64}$' AND \"SecretHash\" ~ '^[0-9A-F]{64}$' AND \"PhoneHash\" ~ '^[0-9A-F]{64}$' AND \"IpHash\" ~ '^[0-9A-F]{64}$' AND (\"CodeHash\" IS NULL OR \"CodeHash\" ~ '^[0-9A-F]{64}$')");
                    table.CheckConstraint("CK_BookingVerification_Times", "\"ExpiresAt\" > \"CreatedAt\" AND \"ResendAt\" > \"CreatedAt\" AND ((\"VerifiedAt\" IS NULL AND \"VerifiedUntil\" IS NULL) OR (\"VerifiedAt\" IS NOT NULL AND \"VerifiedUntil\" > \"VerifiedAt\" AND \"CodeHash\" IS NULL))");
                });

            migrationBuilder.CreateTable(
                name: "BookingSmsJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResendKey = table.Column<Guid>(type: "uuid", nullable: true),
                    CodeVersion = table.Column<int>(type: "integer", nullable: false),
                    ProtectedPayload = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResendAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingSmsJobs", x => x.Id);
                    table.CheckConstraint("CK_BookingSmsJob_Lease", "(\"Status\" = 'Processing' AND \"LeaseId\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL) OR (\"Status\" <> 'Processing' AND \"LeaseId\" IS NULL AND \"LeaseUntil\" IS NULL)");
                    table.CheckConstraint("CK_BookingSmsJob_State", "\"Status\" IN ('Pending','Processing','Accepted','Failed','Unknown','Cancelled','Expired') AND \"Attempts\" BETWEEN 0 AND 3 AND \"CodeVersion\" >= 1");
                    table.ForeignKey(
                        name: "FK_BookingSmsJobs_BookingVerifications_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "BookingVerifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingSmsJobs_ChallengeId_CodeVersion",
                table: "BookingSmsJobs",
                columns: new[] { "ChallengeId", "CodeVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingSmsJobs_ChallengeId_ResendKey",
                table: "BookingSmsJobs",
                columns: new[] { "ChallengeId", "ResendKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingSmsJobs_Status_NextAttemptAt",
                table: "BookingSmsJobs",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingVerificationQuotaEvents_Kind_IpHash_OccurredAt",
                table: "BookingVerificationQuotaEvents",
                columns: new[] { "Kind", "IpHash", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingVerificationQuotaEvents_Kind_OccurredAt",
                table: "BookingVerificationQuotaEvents",
                columns: new[] { "Kind", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingVerificationQuotaEvents_Kind_PhoneHash_OccurredAt",
                table: "BookingVerificationQuotaEvents",
                columns: new[] { "Kind", "PhoneHash", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingVerifications_RequestKey",
                table: "BookingVerifications",
                column: "RequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingVerifications_UpdatedAt",
                table: "BookingVerifications",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM "BookingVerifications") OR EXISTS (SELECT 1 FROM "BookingSmsJobs")
                    OR EXISTS (SELECT 1 FROM "BookingVerificationQuotaEvents") OR EXISTS (SELECT 1 FROM "BookingSmsPeriods") THEN
                    RAISE EXCEPTION 'Telefon doğrulama kayıtları veya kota varken Down uygulanamaz.';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropTable(
                name: "BookingSmsJobs");

            migrationBuilder.DropTable(
                name: "BookingSmsPeriods");

            migrationBuilder.DropTable(
                name: "BookingVerificationQuotaEvents");

            migrationBuilder.DropTable(
                name: "BookingVerifications");
        }
    }
}
