using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YuiTushonkaLauncher.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstalledModParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParentModGuid",
                table: "InstalledMods",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstalledMods_InstanceId_ParentModGuid",
                table: "InstalledMods",
                columns: new[] { "InstanceId", "ParentModGuid" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstalledMods_InstanceId_ParentModGuid",
                table: "InstalledMods");

            migrationBuilder.DropColumn(
                name: "ParentModGuid",
                table: "InstalledMods");
        }
    }
}
