using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Infrastructure.Persistence;

/// <summary>The launcher's local SQLite database.</summary>
public sealed class LauncherDbContext(DbContextOptions<LauncherDbContext> options) : DbContext(options)
{
    public DbSet<Instance> Instances
    {
        get { return Set<Instance>(); }
    }

    public DbSet<InstalledMod> InstalledMods
    {
        get { return Set<InstalledMod>(); }
    }

    public DbSet<InstalledFile> InstalledFiles
    {
        get { return Set<InstalledFile>(); }
    }

    public DbSet<SptRelease> SptReleases
    {
        get { return Set<SptRelease>(); }
    }

    public DbSet<Mod> Mods
    {
        get { return Set<Mod>(); }
    }

    public DbSet<ModVersion> ModVersions
    {
        get { return Set<ModVersion>(); }
    }

    public DbSet<Addon> Addons
    {
        get { return Set<Addon>(); }
    }

    public DbSet<AddonVersion> AddonVersions
    {
        get { return Set<AddonVersion>(); }
    }

    public DbSet<Dependency> Dependencies
    {
        get { return Set<Dependency>(); }
    }

    public DbSet<ModList> ModLists
    {
        get { return Set<ModList>(); }
    }

    public DbSet<ModListEntry> ModListEntries
    {
        get { return Set<ModListEntry>(); }
    }

    public DbSet<RuntimeProfile> RuntimeProfiles
    {
        get { return Set<RuntimeProfile>(); }
    }

    public DbSet<ServerProfile> ServerProfiles
    {
        get { return Set<ServerProfile>(); }
    }

    internal DbSet<SettingsRecord> Settings
    {
        get { return Set<SettingsRecord>(); }
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // SQLite cannot ORDER BY or compare DateTimeOffset stored as text; binary keeps the order.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureInstances(modelBuilder);
        ConfigureCatalog(modelBuilder);
        ConfigureLists(modelBuilder);
        ConfigureRuntimes(modelBuilder);
        ConfigureSettings(modelBuilder);
    }

    private static void ConfigureInstances(ModelBuilder modelBuilder)
    {
        var instance = modelBuilder.Entity<Instance>();
        instance.ToTable("Instances");
        instance.HasKey(row => row.Id);
        instance.Property(row => row.Name).HasMaxLength(64).IsRequired();
        instance.Property(row => row.Path).HasMaxLength(1024).IsRequired();
        instance.Property(row => row.BaseGamePath).HasMaxLength(1024).IsRequired();
        instance.Property(row => row.TarkovVersion).HasMaxLength(64).IsRequired();
        instance.Property(row => row.SptVersion).HasConversion(Conversions.SemanticVersionConverter).HasMaxLength(32).IsRequired();
        instance.Property(row => row.Layout).HasConversion<string>().HasMaxLength(16);
        instance.HasIndex(row => row.Name).IsUnique();
        instance.HasIndex(row => row.Path).IsUnique();

        instance.OwnsOne(
            row => row.Patch,
            patch =>
            {
                patch.Property(value => value.PatchSetVersion).HasMaxLength(32);
                patch.Property(value => value.OriginalSha256).HasMaxLength(64);
                patch.Property(value => value.PatchedSha256).HasMaxLength(64);
                patch.Property(value => value.BackupPath).HasMaxLength(1024);
            }
        );

        var installed = modelBuilder.Entity<InstalledMod>();
        installed.ToTable("InstalledMods");
        installed.HasKey(row => row.Id);
        installed.Property(row => row.Guid).HasMaxLength(256).IsRequired();
        installed.Property(row => row.Name).HasMaxLength(256).IsRequired();
        installed.Property(row => row.Version).HasConversion(Conversions.SemanticVersionConverter).HasMaxLength(32).IsRequired();
        installed.Property(row => row.Kind).HasConversion<string>().HasMaxLength(16);
        installed.Property(row => row.Recognition).HasConversion<string>().HasMaxLength(16);
        installed.Property(row => row.Source).HasConversion<string>().HasMaxLength(16);
        installed.Property(row => row.ParentModGuid).HasMaxLength(256);
        installed.HasIndex(row => new { row.InstanceId, row.Guid }).IsUnique();
        installed.HasIndex(row => new { row.InstanceId, row.ParentModGuid });
        installed
            .HasOne(row => row.Instance)
            .WithMany(row => row.InstalledMods)
            .HasForeignKey(row => row.InstanceId)
            .OnDelete(DeleteBehavior.Cascade);
        installed.HasOne(row => row.Mod).WithMany().HasForeignKey(row => row.ModId).OnDelete(DeleteBehavior.SetNull);

        var file = modelBuilder.Entity<InstalledFile>();
        file.ToTable("InstalledFiles");
        file.HasKey(row => row.Id);
        file.Property(row => row.RelativePath).HasMaxLength(1024).IsRequired();
        file.Property(row => row.Sha256).HasMaxLength(64).IsRequired();
        file.Property(row => row.OriginalSha256).HasMaxLength(64);
        file.HasIndex(row => new { row.InstalledModId, row.RelativePath }).IsUnique();
        file.HasOne(row => row.InstalledMod)
            .WithMany(row => row.Files)
            .HasForeignKey(row => row.InstalledModId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureCatalog(ModelBuilder modelBuilder)
    {
        var release = modelBuilder.Entity<SptRelease>();
        release.ToTable("SptReleases");
        release.HasKey(row => row.Id);
        release.Property(row => row.Version).HasConversion(Conversions.SemanticVersionConverter).HasMaxLength(32).IsRequired();
        release.Property(row => row.TarkovVersion).HasMaxLength(64);
        release.Property(row => row.ReleaseUrl).HasMaxLength(1024);
        release.HasIndex(row => row.Version).IsUnique();
        release.HasIndex(row => row.ForgeId).IsUnique();

        var mod = modelBuilder.Entity<Mod>();
        mod.ToTable("Mods");
        mod.HasKey(row => row.Id);
        mod.Property(row => row.Guid).HasMaxLength(256).IsRequired();
        mod.Property(row => row.Name).HasMaxLength(256).IsRequired();
        mod.Property(row => row.Author).HasMaxLength(256).IsRequired();
        mod.Property(row => row.Kind).HasConversion<string>().HasMaxLength(16);
        mod.Property(row => row.ThumbnailUrl).HasMaxLength(1024);
        mod.HasIndex(row => row.Guid).IsUnique();
        mod.HasIndex(row => row.ForgeId).IsUnique();

        var modVersion = modelBuilder.Entity<ModVersion>();
        modVersion.ToTable("ModVersions");
        modVersion.HasKey(row => row.Id);
        modVersion.Property(row => row.Version).HasConversion(Conversions.SemanticVersionConverter).HasMaxLength(32).IsRequired();
        modVersion
            .Property(row => row.SptConstraint)
            .HasConversion(Conversions.VersionRangeConverter, Conversions.VersionRangeValueComparer)
            .HasMaxLength(128)
            .IsRequired();
        modVersion.Property(row => row.DownloadUrl).HasMaxLength(2048).IsRequired();
        modVersion.Property(row => row.Sha256).HasMaxLength(64);
        modVersion.HasIndex(row => new { row.ModId, row.Version }).IsUnique();
        modVersion.HasOne(row => row.Mod).WithMany(row => row.Versions).HasForeignKey(row => row.ModId).OnDelete(DeleteBehavior.Cascade);

        var addon = modelBuilder.Entity<Addon>();
        addon.ToTable("Addons");
        addon.HasKey(row => row.Id);
        addon.Property(row => row.Name).HasMaxLength(256).IsRequired();
        addon.Property(row => row.Slug).HasMaxLength(256).IsRequired();
        addon.Property(row => row.ThumbnailUrl).HasMaxLength(1024);
        addon.HasIndex(row => row.ForgeId).IsUnique();
        addon.HasIndex(row => row.ParentForgeId);

        var addonVersion = modelBuilder.Entity<AddonVersion>();
        addonVersion.ToTable("AddonVersions");
        addonVersion.HasKey(row => row.Id);
        addonVersion.Property(row => row.Version).HasConversion(Conversions.SemanticVersionConverter).HasMaxLength(32).IsRequired();
        addonVersion
            .Property(row => row.ModVersionConstraint)
            .HasConversion(Conversions.VersionRangeConverter, Conversions.VersionRangeValueComparer)
            .HasMaxLength(128)
            .IsRequired();
        addonVersion.Property(row => row.DownloadUrl).HasMaxLength(2048).IsRequired();
        addonVersion.HasIndex(row => new { row.AddonId, row.Version }).IsUnique();
        addonVersion
            .HasOne(row => row.Addon)
            .WithMany(row => row.Versions)
            .HasForeignKey(row => row.AddonId)
            .OnDelete(DeleteBehavior.Cascade);

        var dependency = modelBuilder.Entity<Dependency>();
        dependency.ToTable("Dependencies");
        dependency.HasKey(row => row.Id);
        dependency.Property(row => row.OwnerGuid).HasMaxLength(256).IsRequired();
        dependency.Property(row => row.DependencyGuid).HasMaxLength(256).IsRequired();
        dependency.Property(row => row.OwnerKind).HasConversion<string>().HasMaxLength(16);
        dependency.Property(row => row.Kind).HasConversion<string>().HasMaxLength(16);
        dependency
            .Property(row => row.VersionRange)
            .HasConversion(Conversions.VersionRangeConverter, Conversions.VersionRangeValueComparer)
            .HasMaxLength(128)
            .IsRequired();
        dependency
            .HasIndex(row => new
            {
                row.OwnerGuid,
                row.OwnerKind,
                row.DependencyGuid,
                row.Kind,
            })
            .IsUnique();
    }

    private static void ConfigureLists(ModelBuilder modelBuilder)
    {
        var list = modelBuilder.Entity<ModList>();
        list.ToTable("ModLists");
        list.HasKey(row => row.Id);
        list.Property(row => row.Name).HasMaxLength(256).IsRequired();
        list.Property(row => row.SptVersion).HasConversion(Conversions.SemanticVersionConverter).HasMaxLength(32).IsRequired();
        list.Property(row => row.TarkovVersion).HasMaxLength(64).IsRequired();
        list.Property(row => row.ManifestSha256).HasMaxLength(64);
        list.HasIndex(row => row.ManifestSha256);

        var entry = modelBuilder.Entity<ModListEntry>();
        entry.ToTable("ModListEntries");
        entry.HasKey(row => row.Id);
        entry.Property(row => row.Guid).HasMaxLength(256).IsRequired();
        entry.Property(row => row.Name).HasMaxLength(256).IsRequired();
        entry.Property(row => row.Version).HasConversion(Conversions.SemanticVersionConverter).HasMaxLength(32).IsRequired();
        entry.Property(row => row.Kind).HasConversion<string>().HasMaxLength(16);
        entry
            .HasIndex(row => new
            {
                row.ModListId,
                row.Guid,
                row.Kind,
            })
            .IsUnique();
        entry.HasOne(row => row.ModList).WithMany(row => row.Entries).HasForeignKey(row => row.ModListId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureRuntimes(ModelBuilder modelBuilder)
    {
        var runtime = modelBuilder.Entity<RuntimeProfile>();
        runtime.ToTable("RuntimeProfiles");
        runtime.HasKey(row => row.Id);
        runtime.Property(row => row.Name).HasMaxLength(128).IsRequired();
        runtime.Property(row => row.Kind).HasConversion<string>().HasMaxLength(16);
        runtime.Property(row => row.Executable).HasMaxLength(1024);
        runtime.Property(row => row.PrefixPath).HasMaxLength(1024);
        runtime.Property(row => row.ExtraArguments).HasMaxLength(1024);
        runtime
            .Property(row => row.Environment)
            .HasConversion(Conversions.StringMapConverter, Conversions.StringMapValueComparer)
            .IsRequired();
        runtime.HasIndex(row => row.Name).IsUnique();
        runtime.HasIndex(row => row.IsDefault);

        var server = modelBuilder.Entity<ServerProfile>();
        server.ToTable("ServerProfiles");
        server.HasKey(row => row.Id);
        server.Property(row => row.BackendUrl).HasMaxLength(512).IsRequired();
        server.Property(row => row.RememberedProfileId).HasMaxLength(64);
        server.Property(row => row.RememberedProfileUsername).HasMaxLength(64);
        server.Property(row => row.RememberedForEndpoint).HasMaxLength(300);
        server.Property(row => row.LastProfileId).HasMaxLength(64);
        server.HasIndex(row => row.InstanceId).IsUnique();
        server
            .HasOne(row => row.Instance)
            .WithOne(row => row.ServerProfile)
            .HasForeignKey<ServerProfile>(row => row.InstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureSettings(ModelBuilder modelBuilder)
    {
        var settings = modelBuilder.Entity<SettingsRecord>();
        settings.ToTable("Settings");
        settings.HasKey(row => row.Id);
        settings.Property(row => row.Id).ValueGeneratedNever();
        settings.Property(row => row.InstancesRoot).HasMaxLength(1024);
        settings.Property(row => row.Language).HasMaxLength(16).IsRequired();
        settings.Property(row => row.Theme).HasMaxLength(16).IsRequired();
        settings.Property(row => row.ExtraProtonRoots).HasMaxLength(8192).IsRequired().HasDefaultValue("[]");
        settings.Property(row => row.UmuRunPath).HasMaxLength(1024);
    }
}
