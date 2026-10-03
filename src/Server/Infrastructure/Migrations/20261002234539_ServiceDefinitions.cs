using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ServiceDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceDefinitions", x => x.Id);
                    table.CheckConstraint("CK_ServiceDefinitions_Currency", "\"Currency\" = 'TRY'");
                    table.CheckConstraint("CK_ServiceDefinitions_Duration", "\"DurationMinutes\" BETWEEN 1 AND 1440");
                    table.CheckConstraint("CK_ServiceDefinitions_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_ServiceDefinitions_Price", "\"Price\" BETWEEN 0 AND 999999.99");
                });

            migrationBuilder.CreateTable(
                name: "ServiceDefinitionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ServiceVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceDefinitionAudits", x => x.Id);
                    table.CheckConstraint("CK_ServiceDefinitionAudits_Kind", "\"Kind\" IN ('Created','Updated','Activated','Deactivated')");
                    table.ForeignKey(
                        name: "FK_ServiceDefinitionAudits_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceDefinitionAudits_ServiceDefinitions_ServiceDefinitio~",
                        column: x => x.ServiceDefinitionId,
                        principalTable: "ServiceDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceDefinitionAudits_ActorId",
                table: "ServiceDefinitionAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceDefinitionAudits_ServiceDefinitionId_ServiceVersion",
                table: "ServiceDefinitionAudits",
                columns: new[] { "ServiceDefinitionId", "ServiceVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceDefinitions_Name_Id",
                table: "ServiceDefinitions",
                columns: new[] { "Name", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceDefinitionAudits");

            migrationBuilder.DropTable(
                name: "ServiceDefinitions");
        }
    }
}
