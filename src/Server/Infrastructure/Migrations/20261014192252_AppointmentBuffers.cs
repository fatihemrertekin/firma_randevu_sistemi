using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations;

public partial class AppointmentBuffers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Mevcut blokları sıfır tamponla koru; aralık/constraint değişimi tek migration transaction'ındadır.
        migrationBuilder.Sql("""
            ALTER TABLE "ServiceDefinitions" ADD COLUMN "BufferBeforeMinutes" integer NOT NULL DEFAULT 0,
              ADD COLUMN "BufferAfterMinutes" integer NOT NULL DEFAULT 0,
              ADD CONSTRAINT "CK_ServiceDefinitions_Buffers" CHECK ("BufferBeforeMinutes" BETWEEN 0 AND 120 AND "BufferAfterMinutes" BETWEEN 0 AND 120);
            ALTER TABLE "AppointmentTimeBlocks" DROP CONSTRAINT "EX_AppointmentTimeBlocks_StaffTime";
            ALTER TABLE "AppointmentTimeBlocks" DROP COLUMN "Occupied",
              ADD COLUMN "BufferBeforeMinutes" integer NOT NULL DEFAULT 0,
              ADD COLUMN "BufferAfterMinutes" integer NOT NULL DEFAULT 0,
              ADD COLUMN "OccupiedStartsAt" timestamptz,
              ADD COLUMN "OccupiedEndsAt" timestamptz;
            UPDATE "AppointmentTimeBlocks" SET "OccupiedStartsAt" = "StartsAt", "OccupiedEndsAt" = "EndsAt";
            ALTER TABLE "AppointmentTimeBlocks" ALTER COLUMN "OccupiedStartsAt" SET NOT NULL, ALTER COLUMN "OccupiedEndsAt" SET NOT NULL,
              ADD COLUMN "Occupied" tstzrange GENERATED ALWAYS AS (tstzrange("OccupiedStartsAt", "OccupiedEndsAt", '[)')) STORED NOT NULL,
              ADD CONSTRAINT "CK_AppointmentTimeBlocks_Buffers" CHECK (
                "BufferBeforeMinutes" BETWEEN 0 AND 120 AND "BufferAfterMinutes" BETWEEN 0 AND 120
                AND isfinite("OccupiedStartsAt") AND isfinite("OccupiedEndsAt")
                AND ("OccupiedStartsAt" AT TIME ZONE 'UTC') = ("StartsAt" AT TIME ZONE 'UTC') - "BufferBeforeMinutes" * INTERVAL '1 minute'
                AND ("OccupiedEndsAt" AT TIME ZONE 'UTC') = ("EndsAt" AT TIME ZONE 'UTC') + "BufferAfterMinutes" * INTERVAL '1 minute'),
              ADD CONSTRAINT "EX_AppointmentTimeBlocks_StaffTime" EXCLUDE USING gist ("StaffMemberId" WITH =, "Occupied" WITH &&) WHERE ("Status" <> 'Cancelled');
            DROP INDEX "IX_AppointmentTimeBlocks_StaffMemberId_StartsAt";
            CREATE INDEX "IX_AppointmentTimeBlocks_StaffMemberId_OccupiedStartsAt" ON "AppointmentTimeBlocks" ("StaffMemberId", "OccupiedStartsAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Sıfır dışı tamponu silerek eski kurala sessiz dönüş yapma.
        migrationBuilder.Sql("""
            DO $guard$ BEGIN
              IF EXISTS (SELECT 1 FROM "ServiceDefinitions" WHERE "BufferBeforeMinutes" <> 0 OR "BufferAfterMinutes" <> 0)
                OR EXISTS (SELECT 1 FROM "AppointmentTimeBlocks" WHERE "BufferBeforeMinutes" <> 0 OR "BufferAfterMinutes" <> 0)
              THEN RAISE EXCEPTION 'Nonzero buffers require a compatible forward fix'; END IF;
            END $guard$;
            ALTER TABLE "AppointmentTimeBlocks" DROP CONSTRAINT "EX_AppointmentTimeBlocks_StaffTime", DROP CONSTRAINT "CK_AppointmentTimeBlocks_Buffers";
            DROP INDEX "IX_AppointmentTimeBlocks_StaffMemberId_OccupiedStartsAt";
            ALTER TABLE "AppointmentTimeBlocks" DROP COLUMN "Occupied", DROP COLUMN "OccupiedStartsAt", DROP COLUMN "OccupiedEndsAt",
              DROP COLUMN "BufferBeforeMinutes", DROP COLUMN "BufferAfterMinutes",
              ADD COLUMN "Occupied" tstzrange GENERATED ALWAYS AS (tstzrange("StartsAt", "EndsAt", '[)')) STORED NOT NULL,
              ADD CONSTRAINT "EX_AppointmentTimeBlocks_StaffTime" EXCLUDE USING gist ("StaffMemberId" WITH =, "Occupied" WITH &&) WHERE ("Status" <> 'Cancelled');
            ALTER TABLE "ServiceDefinitions" DROP CONSTRAINT "CK_ServiceDefinitions_Buffers", DROP COLUMN "BufferBeforeMinutes", DROP COLUMN "BufferAfterMinutes";
            CREATE INDEX "IX_AppointmentTimeBlocks_StaffMemberId_StartsAt" ON "AppointmentTimeBlocks" ("StaffMemberId", "StartsAt");
            """);
    }
}
