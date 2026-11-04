using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YuiTushonkaLauncher.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLinuxRuntimeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtraProtonRoots",
                table: "Settings",
                type: "TEXT",
                maxLength: 8192,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "UmuRunPath",
                table: "Settings",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseGameMode",
                table: "RuntimeProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraProtonRoots",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "UmuRunPath",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "UseGameMode",
                table: "RuntimeProfiles");
        }
    }
}
