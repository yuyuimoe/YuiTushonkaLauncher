using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Localization;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// i18n: the two locales carry the same keys with the same placeholders, a language switch repaints the shell, and no
/// user-facing literal hides in XAML. The last one is the test the phase's acceptance criteria name.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class LocalizationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string[] LocaleFiles =
    [
        "YuiTushonkaLauncher.Ui/Assets/Locales/en.json",
        "YuiTushonkaLauncher.Ui/Assets/Locales/pt-BR.json",
    ];

    [Fact]
    public void BothLocalesHaveTheSameKeysWithTheSamePlaceholders()
    {
        var tables = LocaleFiles.Select(path => LocaleBundle.Flatten(File.ReadAllText(Path.Join(RepositoryRoot, path)))).ToList();

        LocaleBundle.Differences(tables[0], tables[1]).Should().BeEmpty("a key in one language and not the other is a blank label");

        foreach (var (key, english) in tables[0])
        {
            LocaleBundle
                .Placeholders(tables[1][key])
                .Should()
                .BeEquivalentTo(LocaleBundle.Placeholders(english), "placeholders must match for '{0}'", key);
        }
    }

    [Fact]
    public void EveryKeyIsReachableAndEveryLookupFindsItsString()
    {
        var english = LocaleBundle.Flatten(File.ReadAllText(Path.Join(RepositoryRoot, LocaleFiles[0])));
        var localizer = new JsonLocalizer("en");

        foreach (var key in english.Keys)
        {
            localizer[key].Should().NotBe(key, "'{0}' is in the file, so the lookup must find it", key);
        }

        localizer.MissingKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task SwitchingToPortugueseRepaintsTheShell()
    {
        await using var world = await UiWorld.CreateAsync("pt-BR");

        world.Shell.NavItems.Select(item => item.Label).Should().Contain("Instâncias");
        world.Shell.InstancesPage.Title.Should().Be("Instâncias");
        world.Shell.StatusBar.InstanceText.Should().Be("Nenhuma instância selecionada");
        world.Shell.LaunchPanel.LaunchLabel.Should().Be("Iniciar");

        world.Localizer.SetLanguage("en");

        world.Shell.NavItems.Select(item => item.Label).Should().Contain("Instances");
        world.Shell.InstancesPage.Title.Should().Be("Instances");
        world.Localizer.MissingKeys.Should().BeEmpty("a repaint must not look up a key that does not exist");
    }

    [Theory]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt", "pt-BR")]
    [InlineData("pt_BR", "pt-BR")]
    [InlineData("PT-br", "pt-BR")]
    [InlineData("en-US", "en")]
    [InlineData("de", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void LanguageResolutionFallsBackInsteadOfFailing(string? asked, string expected)
    {
        JsonLocalizer.Resolve(asked).Should().Be(expected);

        var localizer = new JsonLocalizer(asked ?? string.Empty);
        localizer.Language.Should().Be(expected);
        localizer["nav.instances"].Should().NotBe("nav.instances");
    }

    [Fact]
    public void TheFirstStringsAPersonSeesAreTranslated()
    {
        // A translation that forgot to translate would pass the parity test and fail the user. These are the strings on
        // the first screen; words that are the same in both languages ("Mods", "Forge") are excluded by picking verbs.
        var english = LocaleBundle.Flatten(File.ReadAllText(Path.Join(RepositoryRoot, LocaleFiles[0])));
        var portuguese = LocaleBundle.Flatten(File.ReadAllText(Path.Join(RepositoryRoot, LocaleFiles[1])));

        foreach (
            var key in new[]
            {
                "nav.instances",
                "instances.title",
                "instances.empty",
                "instances.emptyHint",
                "common.create",
                "common.refresh",
                "launch.launch",
                "launch.stop",
                "server.start",
                "logs.exportView",
                "settings.language",
                "settings.theme",
                "alreadyRunning.close",
            }
        )
        {
            portuguese[key].Should().NotBe(english[key], "'{0}' is still English", key);
        }
    }

    [Fact]
    public void NoUserFacingLiteralLivesInXaml()
    {
        // Every Text/Content/Title/Watermark in a view must be a binding, a resource or an icon glyph. A literal here is a
        // string that would never be translated.
        var allowedLiterals = new HashSet<string>(StringComparer.Ordinal) { "✱", "▤", "⌕", "▦", "◉", "≡", "⚙", "monospace", string.Empty };
        var offenders = new List<string>();

        foreach (
            var file in Directory.EnumerateFiles(
                Path.Join(RepositoryRoot, "YuiTushonkaLauncher.Ui"),
                "*.axaml",
                SearchOption.AllDirectories
            )
        )
        {
            var xaml = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(xaml, @"\b(Text|Content|Title|Watermark|PlaceholderText)=""([^""]*)"""))
            {
                var value = match.Groups[2].Value;
                if (value.StartsWith('{') || allowedLiterals.Contains(value) || value.Contains("avares://", StringComparison.Ordinal))
                {
                    continue;
                }

                offenders.Add($"{Path.GetFileName(file)}: {match.Groups[1].Value}=\"{value}\"");
            }
        }

        offenders.Should().BeEmpty("these are untranslated literals: {0}", string.Join(" | ", offenders));
    }

    [Fact]
    public async Task NoPageProducesAMissingKeyWhenEveryLabelIsRead()
    {
        await using var world = await UiWorld.CreateAsync("en");

        foreach (
            var page in new object[]
            {
                world.Shell.InstancesPage,
                world.Shell.ForgePage,
                world.Shell.ModsPage,
                world.Shell.ServerPage,
                world.Shell.LogsPage,
                world.Shell.SettingsPage,
                world.Shell.LaunchPanel,
                world.Shell.StatusBar,
                world.Services.GetService(typeof(YuiTushonkaLauncher.ViewModels.PatchPageViewModel))!,
            }
        )
        {
            foreach (
                var property in page.GetType()
                    .GetProperties()
                    .Where(property => property.PropertyType == typeof(string) && property.CanRead)
            )
            {
                _ = property.GetValue(page);
            }
        }

        world.Localizer.MissingKeys.Should().BeEmpty();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "YuiTushonkaLauncher.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be found from " + AppContext.BaseDirectory);
    }
}
