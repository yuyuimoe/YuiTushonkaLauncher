using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Dialogs;
using YuiTushonkaLauncher.Infrastructure.Paths;
using YuiTushonkaLauncher.Infrastructure.Persistence;
using YuiTushonkaLauncher.Navigation;
using YuiTushonkaLauncher.Startup;
using YuiTushonkaLauncher.ViewModels;
using YuiTushonkaLauncher.Views;

// The visual smoke's camera: every screen of the launcher, rendered by Skia with no display, written as a PNG.
//   dotnet run --project tools/YuiTushonkaLauncher.Tools.Shots -- --out /tmp/shots
var outDir = "/tmp/ytt-shots";
for (var index = 0; index < args.Length - 1; index++)
{
    if (args[index] == "--out")
    {
        outDir = args[index + 1];
    }
}

Directory.CreateDirectory(outDir);
var root = Path.Combine(Path.GetTempPath(), "ytt-shots-" + Guid.NewGuid().ToString("n"));
Directory.CreateDirectory(root);
var paths = new AppPaths(Path.Combine(root, "data"), Path.Combine(root, "config"));
paths.EnsureCreated();

AppBuilder
    .Configure<YuiTushonkaLauncher.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var services = new ServiceCollection();
services.AddLauncherServices(paths, "en");
using var provider = services.BuildServiceProvider();

// Everything that waits is done before the dispatcher exists, on a pool thread: nothing in this tool should depend on a
// message loop that is not running.
Directory.CreateDirectory(Path.Combine(root, "instances"));
var baseInstall = Path.Combine(root, "base");
WriteFakeBase(baseInstall);
var instances = provider.GetRequiredService<IInstanceService>();
OffThread(async () =>
{
    await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    await instances.CreateAsync(
        new InstanceCreationRequest
        {
            Name = "Smoke_Instance",
            Location = Path.Combine(root, "instances"),
            BaseInstallPath = baseInstall,
        }
    );
    await provider.GetRequiredService<IInstanceContext>().RefreshAsync();
});

// Light first; the dark pass comes after, because the theme is a property of the application, not of a window.
var home = provider.GetRequiredService<HomeViewModel>();
OffThread(async () => await home.LoadAsync(CancellationToken.None));
OffThread(async () => await home.LoadAsync(CancellationToken.None));
Render(new MainWindow { DataContext = home }, "home", 1180, 760);
Render(new MainWindow { DataContext = home }, "home-small", 900, 560);

var shell = provider.GetRequiredService<ShellViewModel>();
foreach (
    var section in new[]
    {
        LauncherSection.Mods,
        LauncherSection.Addons,
        LauncherSection.Server,
        LauncherSection.Logs,
        LauncherSection.Settings,
    }
)
{
    shell.Navigate(section);
    Dispatcher.UIThread.RunJobs();
    Render(new InstanceManagerWindow { DataContext = shell }, $"manager-{section.ToString().ToLowerInvariant()}", 1180, 760);
}

var forge = provider.GetRequiredService<ForgePageViewModel>();
OffThread(async () => await forge.LoadAsync(CancellationToken.None));
Render(new ForgeWindow { DataContext = forge }, "forge", 1180, 760);

var create = new CreateInstanceDialogViewModel(
    provider.GetRequiredService<YuiTushonkaLauncher.Core.Localization.ILocalizer>(),
    instances,
    provider.GetRequiredService<YuiTushonkaLauncher.Core.Instances.IAdoptService>(),
    provider.GetRequiredService<YuiTushonkaLauncher.Core.Instances.IInstallDetector>(),
    provider.GetRequiredService<IDialogService>(),
    provider.GetRequiredService<IInstanceContext>()
);
Render(new InstanceDialog { DataContext = create }, "dialog-create-instance", 900, 640);

var picker = new SelectServerDialogViewModel(
    provider.GetRequiredService<YuiTushonkaLauncher.Core.Localization.ILocalizer>(),
    [
        ServerEntry.Local,
        new ServerEntry
        {
            Name = "Friend",
            Host = "10.0.0.5",
            Port = 6969,
        },
    ],
    "Local"
);
Render(new SelectServerDialog { DataContext = picker }, "dialog-select-server", 480, 420);

// The same screens with the dark variant: a colour that only works on white is a defect the light pass cannot see.
if (Application.Current is { } application)
{
    application.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    Dispatcher.UIThread.RunJobs();
}

var dark = provider.GetRequiredService<HomeViewModel>();
OffThread(async () => await dark.LoadAsync(CancellationToken.None));
Render(new MainWindow { DataContext = dark }, "home-dark", 1180, 760);
Render(new ForgeWindow { DataContext = forge }, "forge-dark", 1180, 760);
shell.Navigate(LauncherSection.Settings);
Dispatcher.UIThread.RunJobs();
Render(new InstanceManagerWindow { DataContext = shell }, "manager-settings-dark", 1180, 760);

Console.WriteLine($"==> {Directory.GetFiles(outDir, "*.png").Length} PNG(s) in {outDir}");
return 0;

void OffThread(Func<Task> work)
{
    // The callee's awaits use ConfigureAwait(false); the wait happens here, so no continuation needs this thread.
    Task.Run(work).GetAwaiter().GetResult();
}

void Render(Window window, string name, int width, int height)
{
    window.Width = width;
    window.Height = height;
    window.Show();
    Dispatcher.UIThread.RunJobs();
    window.Measure(new Size(width, height));
    window.Arrange(new Rect(0, 0, width, height));
    Dispatcher.UIThread.RunJobs();

    // The window's own layout sizes the scrollable content; one more pass lets it settle.
    for (var pass = 0; pass < 3; pass++)
    {
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        Dispatcher.UIThread.RunJobs();
    }

    // A second capture in the same process can otherwise pick up the previous frame's surface.
    window.InvalidateVisual();
    Dispatcher.UIThread.RunJobs();
    window.Measure(new Size(width, height));
    window.Arrange(new Rect(0, 0, width, height));
    Dispatcher.UIThread.RunJobs();

    var frame = window.CaptureRenderedFrame();
    if (frame is null)
    {
        Console.WriteLine($"!! {name}: nothing rendered");
        return;
    }

    var file = Path.Combine(outDir, name + ".png");
    using (var stream = File.Create(file))
    {
        frame.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
    Console.WriteLine($"{name}: {width}x{height}, {new FileInfo(file).Length / 1024} KiB, {window.GetVisualDescendants().Count()} visuals");
    window.Close();
}

static void WriteFakeBase(string root)
{
    void Write(string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    Write("SPT_Runtime/SPT.Server.deps.json", "{\"libraries\":{\"SPT.Server/4.1.6-RELEASE+smoke/linux-x64\":{\"type\":\"project\"}}}");
    Write("SPT_Runtime/SPT_Data/configs/core.json", "{\"compatibleTarkovVersion\":\"0.16.9.40743\",\"projectName\":\"SPT\"}");
    Write(
        "SPT_Runtime/SPT_Data/configs/http.json",
        "{\"ip\":\"127.0.0.1\",\"port\":6969,\"backendIp\":\"127.0.0.1\",\"backendPort\":6969}"
    );
    Write("SPT_Runtime/SPT.Server.Linux.runtimeconfig.json", "{\"runtimeOptions\":{\"framework\":{\"version\":\"10.0.0\"}}}");
    Write("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll", "not a real assembly");
    Write("winhttp.dll", "not a real dll");
    Write("UnityPlayer.dll", "not a real dll");
    Write("doorstop_config.ini", "[General]\nenabled=true\n");
    Write("BepInEx/plugins/.keep", string.Empty);
    Write("BepInEx/core/BepInEx.dll", "not a real dll");
    Write("BepInEx/core/BepInEx.Preloader.dll", "not a real dll");
    File.WriteAllBytes(Path.Combine(root, "EscapeFromTarkov.exe"), PeVersion("0.16.9.40743"));
}

// A PE header whose FileVersion resource says what an install would; the version reader only needs that.
static byte[] PeVersion(string version)
{
    var parts = version.Split('.').Select(part => ushort.Parse(part, CultureInfo.InvariantCulture)).ToArray();
    var bytes = new byte[4096];
    bytes[0] = (byte)'M';
    bytes[1] = (byte)'Z';
    var peOffset = 0x80;
    BitConverter.GetBytes(peOffset).CopyTo(bytes, 0x3C);
    bytes[peOffset] = (byte)'P';
    bytes[peOffset + 1] = (byte)'E';
    return bytes;
}
