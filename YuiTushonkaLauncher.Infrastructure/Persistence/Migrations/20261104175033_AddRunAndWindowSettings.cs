using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YuiTushonkaLauncher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunAndWindowSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows must keep the documented defaults: ask before stopping, and keep the window open.
            migrationBuilder.AddColumn<bool>(
                name: "AskBeforeStoppingProcesses",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "GameStartWindow",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "KeepOpen");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AskBeforeStoppingProcesses",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "GameStartWindow",
                table: "Settings");
        }
    }
}
