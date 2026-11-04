using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YuiTushonkaLauncher.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRememberedServerProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RememberedForEndpoint",
                table: "ServerProfiles",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RememberedProfileId",
                table: "ServerProfiles",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RememberedProfileUsername",
                table: "ServerProfiles",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RememberedForEndpoint",
                table: "ServerProfiles");

            migrationBuilder.DropColumn(
                name: "RememberedProfileId",
                table: "ServerProfiles");

            migrationBuilder.DropColumn(
                name: "RememberedProfileUsername",
                table: "ServerProfiles");
        }
    }
}
