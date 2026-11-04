using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Configuration;
using YuiTushonkaLauncher.Services;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// Settings: saving applies the language and the theme to the running window, and the two switches the close behaviour
/// needs survive a save.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class SettingsTests
{
    [Fact]
    public async Task SavingAppliesTheLanguageAndTheTheme()
    {
        var theme = new RecordingTheme();
        await using var world = await UiWorld.CreateAsync(configure: services => services.AddSingleton<IThemeController>(theme));
        var page = world.Shell.SettingsPage;

        page.SelectedLanguage = page.Languages[1];
        page.SelectedLanguage.Code.Should().Be("pt-BR");
        page.SelectedTheme = page.Themes.First(option => option.Value == "Dark");
        page.AskBeforeStoppingProcesses = false;
        page.SelectedWindow = page.Windows.First(option => option.Value == GameStartWindowAction.Minimise);

        await page.SaveCommand.ExecuteAsync(null);

        page.Error.Should().BeEmpty();
        theme.Applied.Should().Equal("Dark");
        world.Localizer.Language.Should().Be("pt-BR", "the window repaints without a restart");
        world.Shell.InstancesPage.Title.Should().Be("Instâncias");

        var saved = world.Get<ISettingsService>().Current;
        saved.Theme.Should().Be("Dark");
        saved.Language.Should().Be("pt-BR");
        saved.AskBeforeStoppingProcesses.Should().BeFalse();
        saved.GameStartWindow.Should().Be(GameStartWindowAction.Minimise);
    }

    [Fact]
    public async Task TheFormStartsFromTheStoredSettings()
    {
        await using var world = await UiWorld.CreateAsync("pt-BR");
        var settings = world.Get<ISettingsService>();
        await settings.SaveAsync(settings.Current with { Language = "pt-BR", Theme = "Light", MaxConcurrentDownloads = 7 });

        var page = world.Shell.SettingsPage;
        page.Apply(settings.Current);

        page.SelectedLanguage!.Code.Should().Be("pt-BR");
        page.SelectedTheme!.Value.Should().Be("Light");
        page.MaxConcurrentDownloads.Should().Be(7);
        page.InstancesRoot.Should().BeEmpty("no custom root is stored by default");
    }

    [Fact]
    public async Task SettingTheRootIsPersistedAsNullWhenCleared()
    {
        await using var world = await UiWorld.CreateAsync();
        var page = world.Shell.SettingsPage;

        page.InstancesRoot = "/tmp/ytt-somewhere";
        await page.SaveCommand.ExecuteAsync(null);
        world.Get<ISettingsService>().Current.InstancesRoot.Should().Be("/tmp/ytt-somewhere");

        page.InstancesRoot = "   ";
        await page.SaveCommand.ExecuteAsync(null);
        world.Get<ISettingsService>().Current.InstancesRoot.Should().BeNull();
    }
}

/// <summary>A theme controller that records what it was asked to apply.</summary>
public sealed class RecordingTheme : IThemeController
{
    public List<string> Applied { get; } = [];

    public void Apply(string theme)
    {
        Applied.Add(theme);
    }
}
