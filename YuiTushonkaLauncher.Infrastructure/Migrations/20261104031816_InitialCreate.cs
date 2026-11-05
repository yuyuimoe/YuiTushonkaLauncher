using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YuiTushonkaLauncher.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Addons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ForgeId = table.Column<int>(type: "INTEGER", nullable: true),
                    ParentForgeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IsDetached = table.Column<bool>(type: "INTEGER", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Addons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerGuid = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    OwnerKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DependencyGuid = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    VersionRange = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dependencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Instances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    BaseGamePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    SptVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TarkovVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Layout = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RuntimeProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ServerPort = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastLaunchedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Patch_IsPatched = table.Column<bool>(type: "INTEGER", nullable: false),
                    Patch_PatchSetVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Patch_OriginalSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Patch_PatchedSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Patch_BackupPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Patch_AppliedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SptVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TarkovVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ManifestSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Mods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ForgeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Guid = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Author = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Executable = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    PrefixPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    ExtraArguments = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    InstancesRoot = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Theme = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    MaxConcurrentDownloads = table.Column<int>(type: "INTEGER", nullable: false),
                    LogRetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                    LogRetentionFiles = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstRunCompleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SptReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ForgeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TarkovVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ModCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReleaseUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false),
                    FetchedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SptReleases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AddonVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AddonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ModVersionConstraint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DownloadUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ContentLength = table.Column<long>(type: "INTEGER", nullable: true),
                    PublishedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddonVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AddonVersions_Addons_AddonId",
                        column: x => x.AddonId,
                        principalTable: "Addons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    BackendUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    AutoSyncMods = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastProfileId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServerProfiles_Instances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "Instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModListEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModListId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Guid = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IsPinned = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModListEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModListEntries_ModLists_ModListId",
                        column: x => x.ModListId,
                        principalTable: "ModLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstalledMods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Guid = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsPinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    Recognition = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    InstalledAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstalledMods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstalledMods_Instances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "Instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InstalledMods_Mods_ModId",
                        column: x => x.ModId,
                        principalTable: "Mods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ModVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SptConstraint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DownloadUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ContentLength = table.Column<long>(type: "INTEGER", nullable: true),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PublishedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModVersions_Mods_ModId",
                        column: x => x.ModId,
                        principalTable: "Mods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstalledFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstalledModId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Length = table.Column<long>(type: "INTEGER", nullable: false),
                    IsConfig = table.Column<bool>(type: "INTEGER", nullable: false),
                    OriginalSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstalledFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstalledFiles_InstalledMods_InstalledModId",
                        column: x => x.InstalledModId,
                        principalTable: "InstalledMods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Addons_ForgeId",
                table: "Addons",
                column: "ForgeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Addons_ParentForgeId",
                table: "Addons",
                column: "ParentForgeId");

            migrationBuilder.CreateIndex(
                name: "IX_AddonVersions_AddonId_Version",
                table: "AddonVersions",
                columns: new[] { "AddonId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dependencies_OwnerGuid_OwnerKind_DependencyGuid_Kind",
                table: "Dependencies",
                columns: new[] { "OwnerGuid", "OwnerKind", "DependencyGuid", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstalledFiles_InstalledModId_RelativePath",
                table: "InstalledFiles",
                columns: new[] { "InstalledModId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstalledMods_InstanceId_Guid",
                table: "InstalledMods",
                columns: new[] { "InstanceId", "Guid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstalledMods_ModId",
                table: "InstalledMods",
                column: "ModId");

            migrationBuilder.CreateIndex(
                name: "IX_Instances_Name",
                table: "Instances",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Instances_Path",
                table: "Instances",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModListEntries_ModListId_Guid_Kind",
                table: "ModListEntries",
                columns: new[] { "ModListId", "Guid", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModLists_ManifestSha256",
                table: "ModLists",
                column: "ManifestSha256");

            migrationBuilder.CreateIndex(
                name: "IX_Mods_ForgeId",
                table: "Mods",
                column: "ForgeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mods_Guid",
                table: "Mods",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModVersions_ModId_Version",
                table: "ModVersions",
                columns: new[] { "ModId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeProfiles_IsDefault",
                table: "RuntimeProfiles",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeProfiles_Name",
                table: "RuntimeProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServerProfiles_InstanceId",
                table: "ServerProfiles",
                column: "InstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SptReleases_ForgeId",
                table: "SptReleases",
                column: "ForgeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SptReleases_Version",
                table: "SptReleases",
                column: "Version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AddonVersions");

            migrationBuilder.DropTable(
                name: "Dependencies");

            migrationBuilder.DropTable(
                name: "InstalledFiles");

            migrationBuilder.DropTable(
                name: "ModListEntries");

            migrationBuilder.DropTable(
                name: "ModVersions");

            migrationBuilder.DropTable(
                name: "RuntimeProfiles");

            migrationBuilder.DropTable(
                name: "ServerProfiles");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "SptReleases");

            migrationBuilder.DropTable(
                name: "Addons");

            migrationBuilder.DropTable(
                name: "InstalledMods");

            migrationBuilder.DropTable(
                name: "ModLists");

            migrationBuilder.DropTable(
                name: "Instances");

            migrationBuilder.DropTable(
                name: "Mods");
        }
    }
}
