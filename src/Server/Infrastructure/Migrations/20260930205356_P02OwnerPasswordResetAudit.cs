using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class P02OwnerPasswordResetAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OwnerPasswordResetAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OperatorReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerPasswordResetAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPasswordResetAudits_GrantId_Kind",
                table: "OwnerPasswordResetAudits",
                columns: new[] { "GrantId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPasswordResetAudits_InstanceId_RequestReference_Kind",
                table: "OwnerPasswordResetAudits",
                columns: new[] { "InstanceId", "RequestReference", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnerPasswordResetAudits");
        }
    }
}
