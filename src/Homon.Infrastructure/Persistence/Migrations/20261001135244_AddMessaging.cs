using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Homon.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reporters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Identifier = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                    BodyVisibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reporters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reporters_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReporterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                    Body = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RecurrenceDeclaration = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    NextExpectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReportedByKeyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_ApiKeys_ReportedByKeyId",
                        column: x => x.ReportedByKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Messages_Reporters_ReporterId",
                        column: x => x.ReporterId,
                        principalTable: "Reporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ReportedByKeyId",
                table: "Messages",
                column: "ReportedByKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ReporterId_ReceivedAt",
                table: "Messages",
                columns: new[] { "ReporterId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Reporters_ApiKeyId",
                table: "Reporters",
                column: "ApiKeyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reporters_Identifier",
                table: "Reporters",
                column: "Identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reporters_NormalizedName",
                table: "Reporters",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "Reporters");
        }
    }
}
