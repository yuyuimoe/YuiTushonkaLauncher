using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YuiTushonkaLauncher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServerList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveServer",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Servers",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveServer",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Servers",
                table: "Settings");
        }
    }
}
