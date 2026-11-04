using Microsoft.Extensions.Logging.Abstractions;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Infrastructure.Instances;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Instances;

/// <summary>Builds a miniature but structurally faithful SPT install for the instance tests.</summary>
public sealed class FakeBaseInstall : IDisposable
{
    private FakeBaseInstall(TempDirectory temp, string root, Core.Domain.InstanceLayout layout)
    {
        Temp = temp;
        Root = root;
        Layout = layout;
    }

    public TempDirectory Temp { get; }

    public string Root { get; }

    public Core.Domain.InstanceLayout Layout { get; }

    /// <summary>Creates a 4.1-style install (spt/SPT_Runtime) with one file per policy branch.</summary>
    public static FakeBaseInstall CreateSpt41(string sptVersion = "4.1.6")
    {
        return Create("spt/SPT_Runtime", Core.Domain.InstanceLayout.Spt41Plus, sptVersion);
    }

    /// <summary>Creates a 4.0-style install (spt/SPT).</summary>
    public static FakeBaseInstall CreateSpt40(string sptVersion = "4.0.13")
    {
        return Create("spt/SPT", Core.Domain.InstanceLayout.Spt40, sptVersion);
    }

    /// <summary>An <see cref="BaseInstallProbe" /> wired to no log output.</summary>
    public static BaseInstallProbe CreateProbe()
    {
        return new BaseInstallProbe(NullLogger<BaseInstallProbe>.Instance);
    }

    public void Dispose()
    {
        Temp.Dispose();
    }

    /// <summary>Writes raw bytes relative to the base root, for fixtures that must be binary.</summary>
    public string WriteFileBytes(string relativePath, byte[] content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>Writes or overwrites a file relative to the base root.</summary>
    public string WriteFile(string relativePath, string content = "content")
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Creates a directory relative to the base root.</summary>
    public string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Deletes a file relative to the base root.</summary>
    public void DeleteFile(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static FakeBaseInstall Create(string runtimeDirectory, Core.Domain.InstanceLayout layout, string sptVersion)
    {
        var temp = new TempDirectory();
        var root = temp.Combine("base");
        var install = new FakeBaseInstall(temp, root, layout);

        install.WriteFileBytes("gamefiles/EscapeFromTarkov.exe", FakePe.WithVersion("0.16.9.40743"));
        install.WriteFile("gamefiles/UnityPlayer.dll", "unity");
        install.WriteFile("gamefiles/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll", "patched-assembly");
        install.WriteFile("gamefiles/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak", "original-assembly");
        install.WriteFile("gamefiles/EscapeFromTarkov_Data/Resources/bundle.bundle", "bundle");
        install.WriteFile("spt/winhttp.dll", "doorstop");
        install.WriteFile("spt/doorstop_config.ini", "[General]\nenabled = true\n");
        install.WriteFile("spt/BepInEx/core/BepInEx.Preloader.dll", "preloader");
        install.WriteFile("spt/BepInEx/config/BepInEx.cfg", "[Logging]\n");
        install.WriteFile("spt/BepInEx/cache/chainloader.dat", "cache");
        install.WriteFile("spt/BepInEx/plugins/spt/spt-core.dll", "spt-core");
        install.WriteFile("spt/BepInEx/plugins/UserMod/UserMod.dll", "user-mod");
        install.WriteFile($"{runtimeDirectory}/SPT.Server.dll", "server");
        install.WriteFile($"{runtimeDirectory}/SPT_Data/database/items.json", "{}");
        install.WriteFile(
            $"{runtimeDirectory}/SPT_Data/configs/core.json",
            """{ "projectName": "SPT", "compatibleTarkovVersion": "0.16.9.40743" }"""
        );
        install.WriteFile($"{runtimeDirectory}/user/profiles/base.json", "{}");
        install.WriteFile(
            $"{runtimeDirectory}/SPT.Server.deps.json",
            $$"""
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0/linux-x64" },
              "libraries": {
                "SPT.Server/{{sptVersion}}-RELEASE+abcdef.20260918": { "type": "project" },
                "SPTushonka.Common/{{sptVersion}}-RELEASE": { "type": "project" }
              }
            }
            """
        );

        return install;
    }
}
