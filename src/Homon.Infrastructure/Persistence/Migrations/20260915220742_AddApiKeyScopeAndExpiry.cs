using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homon.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyScopeAndExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "ApiKeys",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "ApiKeys",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "ReadWrite");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "ApiKeys");
        }
    }
}
