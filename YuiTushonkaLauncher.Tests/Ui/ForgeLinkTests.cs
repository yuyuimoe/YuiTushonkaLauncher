using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YuiTushonkaLauncher.Controls;
using YuiTushonkaLauncher.Startup;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// The description's links and the sidebar's link buttons carry different things — an address and a link view model —
/// and handing one to the command built for the other threw out of CanExecute while the button was attaching to the
/// tree, which took the whole window down. The document's links now have a command of their own, typed for a string.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class ForgeLinkTests
{
    [Fact]
    public async Task OpeningAnAddressFromTheDescriptionGoesToTheUrlOpener()
    {
        var opener = new RecordingUrlOpener();
        await using var world = await UiWorld.CreateAsync(configure: services => services.AddSingleton<IUrlOpener>(opener));
        var page = world.Get<ForgePageViewModel>();

        page.OpenUrlCommand.Execute("https://sp-mod.com/mod/902/bigbrain");

        opener.Opened.Should().ContainSingle().Which.Should().Be("https://sp-mod.com/mod/902/bigbrain");
    }

    [Fact]
    public async Task AnEmptyAddressIsNotOpened()
    {
        var opener = new RecordingUrlOpener();
        await using var world = await UiWorld.CreateAsync(configure: services => services.AddSingleton<IUrlOpener>(opener));
        var page = world.Get<ForgePageViewModel>();

        page.OpenUrlCommand.CanExecute("").Should().BeFalse();
        page.OpenUrlCommand.Execute("");

        opener.Opened.Should().BeEmpty();
    }

    [Fact]
    public async Task TheSidebarButtonsStillTakeALinkViewModel()
    {
        var opener = new RecordingUrlOpener();
        await using var world = await UiWorld.CreateAsync(configure: services => services.AddSingleton<IUrlOpener>(opener));
        var page = world.Get<ForgePageViewModel>();
        var link = new ForgeLinkViewModel("https://github.com/example/mod");

        page.OpenLinkCommand.CanExecute(link).Should().BeTrue();
        page.OpenLinkCommand.Execute(link);

        opener.Opened.Should().ContainSingle().Which.Should().Be("https://github.com/example/mod");
    }

    [Fact]
    public void TheDocumentCannotBeGivenACommandForAnotherParameterType()
    {
        // The crash was a bind that type-checked but could not be called: the property is typed for a string now, so
        // the wrong command is a binding error instead of an exception in the middle of attaching.
        var property = typeof(MarkdownView).GetProperty(nameof(MarkdownView.OpenUrlCommand), BindingFlags.Public | BindingFlags.Instance);

        property.Should().NotBeNull();
        property!.PropertyType.Should().Be<IRelayCommand<string>>();

        var command = typeof(ForgePageViewModel).GetProperty(nameof(ForgePageViewModel.OpenUrlCommand));
        command!.PropertyType.Should().BeAssignableTo<IRelayCommand<string>>();
    }

    /// <summary>An opener that records instead of starting a browser.</summary>
    private sealed class RecordingUrlOpener : IUrlOpener
    {
        public List<string> Opened { get; } = [];

        public void Open(string url)
        {
            Opened.Add(url);
        }
    }
}
