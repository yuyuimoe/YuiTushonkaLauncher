using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.ViewModels;
using YuiTushonkaLauncher.Views;

namespace YuiTushonkaLauncher.Dialogs;

/// <summary>
/// The desktop implementation: modal windows and the platform file pickers. It never decides anything on its own — the
/// view models do, and this only shows what they built.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly ILocalizer _localizer;
    private Func<Window?>? _owner;

    public DialogService(Core.Localization.ILocalizer localizer)
    {
        _localizer = localizer;
    }

    /// <summary>Binds the service to the shell window, which owns every dialog it opens.</summary>
    public void Attach(Window window)
    {
        _owner = () => window;
    }

    public async Task<bool> ConfirmAsync(ConfirmDialogViewModel dialog, CancellationToken cancellationToken = default)
    {
        var window = new ConfirmDialog { DataContext = dialog };
        var closed = new TaskCompletionSource<bool>();
        dialog.Closed += (_, accepted) =>
        {
            closed.TrySetResult(accepted);
            window.Close();
        };

        cancellationToken.Register(() => closed.TrySetCanceled(cancellationToken));
        if (_owner?.Invoke() is { } owner)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }

        return await closed.Task;
    }

    public async Task<Core.Domain.Instance?> CreateInstanceAsync(CreateInstanceDialogViewModel dialog, CancellationToken cancellationToken)
    {
        var window = new InstanceDialog { DataContext = dialog };
        var closed = new TaskCompletionSource<Instance?>();
        dialog.Closed += (_, instance) =>
        {
            closed.TrySetResult(instance);
            window.Close();
        };

        cancellationToken.Register(() => closed.TrySetCanceled(cancellationToken));
        if (_owner?.Invoke() is { } owner)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }

        return await closed.Task;
    }

    public Task<string?> PickFolderAsync(string title, string? startAt = null, CancellationToken cancellationToken = default)
    {
        return RunPickerAsync(async storage =>
        {
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        });
    }

    public Task<string?> PickFileAsync(string title, string filterName, string extension, CancellationToken cancellationToken = default)
    {
        return RunPickerAsync(async storage =>
        {
            var files = await storage.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType(filterName) { Patterns = [extension] }],
                }
            );
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        });
    }

    public Task<string?> PickSaveFileAsync(
        string title,
        string suggestedName,
        string filterName,
        string extension,
        CancellationToken cancellationToken = default
    )
    {
        return RunPickerAsync(async storage =>
        {
            var file = await storage.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = title,
                    SuggestedFileName = suggestedName,
                    FileTypeChoices = [new FilePickerFileType(filterName) { Patterns = [extension] }],
                }
            );
            return file?.TryGetLocalPath();
        });
    }

    public async Task InformAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        var dialog = new ConfirmDialogViewModel(_localizer)
        {
            Title = title,
            Body = body,
            IsInformational = true,
            ConfirmLabel = _localizer["common.close"],
        };
        var window = new ConfirmDialog { DataContext = dialog };
        dialog.Closed += (_, _) => window.Close();
        if (_owner?.Invoke() is { } owner)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }

    private async Task<string?> RunPickerAsync(Func<IStorageProvider, Task<string?>> picker)
    {
        if (_owner?.Invoke() is not { } owner || owner.StorageProvider is not { } storage || !storage.CanOpen)
        {
            return null;
        }

        return await picker(storage);
    }
}
