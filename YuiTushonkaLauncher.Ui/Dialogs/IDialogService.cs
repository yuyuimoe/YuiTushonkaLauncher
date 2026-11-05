using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Dialogs;

/// <summary>
/// The windows and pickers a page needs. An interface so the headless tests can drive every flow without a desktop:
/// the fake answers the same questions the window would ask.
/// </summary>
public interface IDialogService
{
    /// <summary>Yes/no with a body. The dialog itself carries the optional checkbox, and its value is read after a yes.</summary>
    Task<bool> ConfirmAsync(ConfirmDialogViewModel dialog, CancellationToken cancellationToken = default);

    /// <summary>Which server to launch against. <c>null</c> when the user cancels.</summary>
    Task<Core.Server.ServerEntry?> SelectServerAsync(SelectServerDialogViewModel dialog, CancellationToken cancellationToken = default);

    /// <summary>The create-instance window, which previews the plan before it creates anything.</summary>
    Task<Instance?> CreateInstanceAsync(CreateInstanceDialogViewModel dialog, CancellationToken cancellationToken = default);

    /// <summary>Picks a directory. <c>null</c> when the user cancels.</summary>
    Task<string?> PickFolderAsync(string title, string? startAt = null, CancellationToken cancellationToken = default);

    /// <summary>Picks a file, filtered by extension pattern (for example <c>*.zip</c>).</summary>
    Task<string?> PickFileAsync(string title, string filterName, string extension, CancellationToken cancellationToken = default);

    /// <summary>A file to save into.</summary>
    Task<string?> PickSaveFileAsync(
        string title,
        string suggestedName,
        string filterName,
        string extension,
        CancellationToken cancellationToken = default
    );

    /// <summary>Says something without a decision (a finished export, a diagnostics bundle written).</summary>
    Task InformAsync(string title, string body, CancellationToken cancellationToken = default);
}
