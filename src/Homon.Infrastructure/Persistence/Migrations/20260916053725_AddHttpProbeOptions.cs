using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homon.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHttpProbeOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HttpOptions",
                table: "Probes",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HttpOptions",
                table: "Probes");
        }
    }
}
