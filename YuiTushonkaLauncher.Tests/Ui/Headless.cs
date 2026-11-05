using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Dialogs;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// Starts Avalonia once for the whole assembly. The real <see cref="YuiTushonkaLauncher.App" /> is used, so the
/// templates and styles a test resolves are the ones that ship.
/// </summary>
public static class Headless
{
    private static readonly object Gate = new();
    private static bool _started;

    /// <summary>Sets up the headless platform; safe to call from every test.</summary>
    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_started)
            {
                return;
            }

            AppBuilder
                .Configure<YuiTushonkaLauncher.App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
                .SetupWithoutStarting();
            _started = true;
        }
    }
}

/// <summary>
/// A dialog service that answers what the test queued and remembers what it was asked. This is how a UI flow is driven
/// without a desktop: the same questions the window would ask, answered deterministically.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    private readonly Queue<bool> _confirmations = new();
    private readonly Queue<string?> _paths = new();

    public List<string> Asked { get; } = [];

    /// <summary>What the next <see cref="ConfirmAsync" /> answers.</summary>
    public bool NextConfirmation { get; set; } = true;

    /// <summary>The value the checkbox of the next question is left at.</summary>
    public bool CheckboxAnswer { get; set; }

    public List<ConfirmDialogViewModel> Questions { get; } = [];

    public List<CreateInstanceDialogViewModel> CreateDialogs { get; } = [];

    /// <summary>Every server picker the flow asked for.</summary>
    public List<SelectServerDialogViewModel> ServerDialogs { get; } = [];

    /// <summary>Answers the server picker: the test chooses an entry, or <c>null</c> to cancel.</summary>
    public Func<SelectServerDialogViewModel, Core.Server.ServerEntry?>? OnSelectServer { get; set; }

    /// <summary>Runs instead of showing the create window: the test fills the form and presses create.</summary>
    public Func<CreateInstanceDialogViewModel, Task<Instance?>>? OnCreate { get; set; }

    public string? NextPath { get; set; }

    public string? NextSavePath { get; set; }

    public Task<Core.Server.ServerEntry?> SelectServerAsync(
        SelectServerDialogViewModel dialog,
        CancellationToken cancellationToken = default
    )
    {
        ServerDialogs.Add(dialog);
        Asked.Add("select-server");
        // A handler that answers null means "cancelled", which is different from having no handler at all.
        var chosen = OnSelectServer is { } handler ? handler(dialog) : dialog.Selected?.Entry;
        return Task.FromResult(chosen);
    }

    public Task<bool> ConfirmAsync(ConfirmDialogViewModel dialog, CancellationToken cancellationToken = default)
    {
        Asked.Add("confirm:" + dialog.Title);
        Questions.Add(dialog);
        if (dialog.HasCheckbox)
        {
            dialog.CheckboxValue = CheckboxAnswer;
        }

        return Task.FromResult(_confirmations.Count > 0 ? _confirmations.Dequeue() : NextConfirmation);
    }

    public Task<Instance?> CreateInstanceAsync(CreateInstanceDialogViewModel dialog, CancellationToken cancellationToken = default)
    {
        Asked.Add("create:" + dialog.Title);
        CreateDialogs.Add(dialog);
        return OnCreate is { } handler ? handler(dialog) : Task.FromResult<Instance?>(null);
    }

    public Task<string?> PickFolderAsync(string title, string? startAt = null, CancellationToken cancellationToken = default)
    {
        Asked.Add("folder:" + title);
        return Task.FromResult(NextPath);
    }

    public Task<string?> PickFileAsync(string title, string filterName, string extension, CancellationToken cancellationToken = default)
    {
        Asked.Add("file:" + title);
        return Task.FromResult(NextPath);
    }

    public Task<string?> PickSaveFileAsync(
        string title,
        string suggestedName,
        string filterName,
        string extension,
        CancellationToken cancellationToken = default
    )
    {
        Asked.Add("save:" + title);
        return Task.FromResult(NextSavePath ?? NextPath);
    }

    public Task InformAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        Asked.Add("inform:" + title);
        return Task.CompletedTask;
    }
}

/// <summary>Records which window sections the launcher asked to open, without opening anything.</summary>
public sealed class FakeWindowController : YuiTushonkaLauncher.ViewModels.IWindowController
{
    public List<string> Actions { get; } = [];

    public List<YuiTushonkaLauncher.Navigation.LauncherSection> Managers { get; } = [];

    /// <summary>The Forge modes the launcher asked for.</summary>
    public List<YuiTushonkaLauncher.Navigation.ForgeBrowserMode> ForgeModes { get; } = [];

    public void Apply(YuiTushonkaLauncher.Core.Configuration.GameStartWindowAction action)
    {
        Actions.Add(action.ToString());
    }

    public void OpenManager(YuiTushonkaLauncher.Navigation.LauncherSection section)
    {
        Managers.Add(section);
    }

    public void OpenForge(YuiTushonkaLauncher.Navigation.ForgeBrowserMode mode)
    {
        ForgeModes.Add(mode);
    }
}
