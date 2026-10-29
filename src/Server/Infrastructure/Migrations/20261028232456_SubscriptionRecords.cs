using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    AmountTry = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPeriods", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionPeriods_Amount", "\"AmountTry\" > 0 AND \"AmountTry\" <> 'NaN'::numeric");
                    table.CheckConstraint("CK_SubscriptionPeriods_Dates", "\"StartsOn\" >= DATE '0001-01-01' AND \"EndsOn\" <= DATE '9999-12-31' AND \"StartsOn\" < \"EndsOn\" AND \"EndsOn\" = (date_trunc('month', \"StartsOn\"::timestamp) + INTERVAL '1 month')::date AND \"DueOn\" BETWEEN DATE '0001-01-01' AND DATE '9999-12-31'");
                    table.CheckConstraint("CK_SubscriptionPeriods_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Version\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"RecordedBy\" <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(\"RecordedAt\")");
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountTry = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ReversesPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReplacesPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPayments", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionPayments_Amount", "\"AmountTry\" <> 0 AND \"AmountTry\" <> 'NaN'::numeric");
                    table.CheckConstraint("CK_SubscriptionPayments_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"RequestKey\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"RecordedBy\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"RequestHash\" ~ '^[0-9A-F]{64}$' AND isfinite(\"RecordedAt\") AND \"PaidOn\" BETWEEN DATE '0001-01-01' AND DATE '9999-12-31'");
                    table.CheckConstraint("CK_SubscriptionPayments_Kind", "(\"ReversesPaymentId\" IS NULL AND \"AmountTry\" > 0 AND \"Reference\" IS NOT NULL AND \"Reference\" ~ '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,79}$' AND ((\"ReplacesPaymentId\" IS NULL AND \"Reason\" IS NULL) OR (\"ReplacesPaymentId\" IS NOT NULL AND \"Reason\" IS NOT NULL AND length(btrim(\"Reason\")) > 0))) OR (\"ReversesPaymentId\" IS NOT NULL AND \"ReplacesPaymentId\" IS NULL AND \"AmountTry\" < 0 AND \"Reference\" IS NULL AND \"Reason\" IS NOT NULL AND length(btrim(\"Reason\")) > 0)");
                    table.ForeignKey(
                        name: "FK_SubscriptionPayments_SubscriptionPayments_ReplacesPaymentId",
                        column: x => x.ReplacesPaymentId,
                        principalTable: "SubscriptionPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPayments_SubscriptionPayments_ReversesPaymentId",
                        column: x => x.ReversesPaymentId,
                        principalTable: "SubscriptionPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPayments_SubscriptionPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "SubscriptionPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_PeriodId",
                table: "SubscriptionPayments",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_Reference",
                table: "SubscriptionPayments",
                column: "Reference",
                unique: true,
                filter: "\"ReplacesPaymentId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_ReplacesPaymentId",
                table: "SubscriptionPayments",
                column: "ReplacesPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_RequestKey",
                table: "SubscriptionPayments",
                column: "RequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_ReversesPaymentId",
                table: "SubscriptionPayments",
                column: "ReversesPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_StartsOn",
                table: "SubscriptionPeriods",
                column: "StartsOn",
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE "SubscriptionPeriods" ADD CONSTRAINT "EX_SubscriptionPeriods_Range"
                  EXCLUDE USING gist (daterange("StartsOn", "EndsOn", '[)') WITH &&);

                CREATE FUNCTION "ProtectSubscriptionPayment"() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
                DECLARE budget numeric; total numeric; original public."SubscriptionPayments"%ROWTYPE;
                BEGIN
                  IF TG_OP <> 'INSERT' THEN
                    RAISE EXCEPTION 'Subscription payments are append-only' USING ERRCODE = '23514';
                  END IF;
                  SELECT "AmountTry" INTO budget FROM public."SubscriptionPeriods"
                    WHERE "Id" = NEW."PeriodId" FOR UPDATE;
                  IF NOT FOUND THEN
                    RAISE EXCEPTION 'Subscription period missing' USING ERRCODE = '23503';
                  END IF;
                  IF NEW."ReversesPaymentId" IS NOT NULL THEN
                    SELECT * INTO original FROM public."SubscriptionPayments"
                      WHERE "Id" = NEW."ReversesPaymentId" AND "PeriodId" = NEW."PeriodId" AND "AmountTry" > 0;
                    IF NOT FOUND OR NEW."AmountTry" <> -original."AmountTry"
                      OR NEW."RecordedAt" < original."RecordedAt" OR NEW."PaidOn" < original."PaidOn" THEN
                      RAISE EXCEPTION 'Invalid subscription reversal' USING ERRCODE = '23514';
                    END IF;
                  END IF;
                  IF NEW."ReplacesPaymentId" IS NOT NULL THEN
                    SELECT * INTO original FROM public."SubscriptionPayments"
                      WHERE "Id" = NEW."ReplacesPaymentId" AND "AmountTry" > 0 AND "Reference" = NEW."Reference";
                    IF NOT FOUND OR NEW."RecordedAt" < original."RecordedAt" OR NOT EXISTS
                      (SELECT 1 FROM public."SubscriptionPayments" WHERE "ReversesPaymentId" = original."Id") THEN
                      RAISE EXCEPTION 'Original payment must be reversed before replacement' USING ERRCODE = '23514';
                    END IF;
                  END IF;
                  SELECT COALESCE(SUM("AmountTry"), 0) INTO total FROM public."SubscriptionPayments" WHERE "PeriodId" = NEW."PeriodId";
                  IF total + NEW."AmountTry" < 0 OR total + NEW."AmountTry" > budget THEN
                    RAISE EXCEPTION 'Subscription balance exceeded' USING ERRCODE = '23514';
                  END IF;
                  -- RepeatableRead altında ikinci yazıcı da eski snapshot ile bakiye çoğaltamasın.
                  UPDATE public."SubscriptionPeriods" SET "Version" = gen_random_uuid() WHERE "Id" = NEW."PeriodId";
                  RETURN NEW;
                END; $$;
                CREATE TRIGGER "TR_SubscriptionPayments_AppendOnly" BEFORE INSERT OR UPDATE OR DELETE ON "SubscriptionPayments"
                  FOR EACH ROW EXECUTE FUNCTION "ProtectSubscriptionPayment"();

                CREATE FUNCTION "ProtectPaidSubscriptionPeriod"() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM public."SubscriptionPayments" WHERE "PeriodId" = OLD."Id") AND
                    ROW(NEW."Id", NEW."StartsOn", NEW."EndsOn", NEW."DueOn", NEW."AmountTry", NEW."RecordedBy", NEW."RecordedAt")
                    IS DISTINCT FROM ROW(OLD."Id", OLD."StartsOn", OLD."EndsOn", OLD."DueOn", OLD."AmountTry", OLD."RecordedBy", OLD."RecordedAt") THEN
                    RAISE EXCEPTION 'Recorded subscription period is immutable' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END; $$;
                CREATE TRIGGER "TR_SubscriptionPeriods_History" BEFORE UPDATE ON "SubscriptionPeriods"
                  FOR EACH ROW EXECUTE FUNCTION "ProtectPaidSubscriptionPeriod"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "SubscriptionPeriods") OR EXISTS (SELECT 1 FROM "SubscriptionPayments") THEN
                    RAISE EXCEPTION 'Subscription records exist; preserve data and roll back the application image instead';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "SubscriptionPayments");

            migrationBuilder.DropTable(
                name: "SubscriptionPeriods");

            migrationBuilder.Sql("DROP FUNCTION \"ProtectSubscriptionPayment\"(); DROP FUNCTION \"ProtectPaidSubscriptionPeriod\"();");
        }
    }
}
