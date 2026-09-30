using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class P02OwnerMfaRecoveryAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OwnerMfaRecoveryAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OperatorReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerMfaRecoveryAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerMfaRecoveryAudits_InstanceId_RequestReference",
                table: "OwnerMfaRecoveryAudits",
                columns: new[] { "InstanceId", "RequestReference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnerMfaRecoveryAudits");
        }
    }
}
