using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YuiTushonkaLauncher.Core.Formatting;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Core.Logs;
using YuiTushonkaLauncher.Dialogs;
using YuiTushonkaLauncher.Navigation;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// The log tab: one source at a time, its tail, and the two ways out (export what is shown, or write a diagnostics
/// bundle). A file above the follow limit says so with its size and offers the folder, never a frozen window.
/// </summary>
public partial class LogsPageViewModel : PageViewModel
{
    private readonly ILogService _logs;
    private readonly IInstanceContext _context;
    private readonly IDialogService _dialogs;

    public LogsPageViewModel(ILocalizer localizer, ILogService logs, IInstanceContext context, IDialogService dialogs)
        : base(localizer)
    {
        _logs = logs;
        _context = context;
        _dialogs = dialogs;
        _context.Changed += (_, _) => _ = RefreshAsync(CancellationToken.None);
    }

    /// <summary>Every source of the active instance, newest first.</summary>
    public ObservableCollection<LogFileRowViewModel> Sources { get; } = [];

    /// <summary>The lines currently shown.</summary>
    public ObservableCollection<string> Lines { get; } = [];

    [ObservableProperty]
    public partial LogFileRowViewModel? SelectedSource { get; set; }

    [ObservableProperty]
    public partial bool FollowNewest { get; set; } = true;

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    public string Title
    {
        get { return Text("logs.title"); }
    }

    public string SourceLabel
    {
        get { return Text("logs.source"); }
    }

    public string RefreshLabel
    {
        get { return Text("logs.refresh"); }
    }

    public string FollowLabel
    {
        get { return Text("logs.follow"); }
    }

    public string ExportLabel
    {
        get { return Text("logs.exportView"); }
    }

    public string DiagnosticsLabel
    {
        get { return Text("logs.diagnostics"); }
    }

    public string OpenFolderLabel
    {
        get { return Text("logs.openFolder"); }
    }

    public string MissingText
    {
        get { return Text("logs.missing"); }
    }

    public bool HasNotice
    {
        get { return Notice.Length > 0; }
    }

    public bool HasSources
    {
        get { return Sources.Count > 0; }
    }

    public bool CanExport
    {
        get { return Lines.Count > 0; }
    }

    /// <summary>Lists the sources and tails the first one (or the selected one).</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_context.ActiveId is not { } instanceId)
        {
            Sources.Clear();
            Lines.Clear();
            OnPropertyChanged(nameof(HasSources));
            return;
        }

        await RunAsync(async () =>
        {
            var files = await _logs.ListAsync(instanceId, cancellationToken);
            var selected = SelectedSource?.Path;
            Sources.Clear();
            foreach (var file in files)
            {
                Sources.Add(new LogFileRowViewModel(Localizer, file));
            }

            OnPropertyChanged(nameof(HasSources));
            SelectedSource = Sources.FirstOrDefault(row => row.Path == selected) ?? Sources.FirstOrDefault();
            await TailAsync(cancellationToken);
        });
    }

    [RelayCommand]
    private async Task TailAsync(CancellationToken cancellationToken)
    {
        Notice = string.Empty;
        Lines.Clear();
        if (SelectedSource is null)
        {
            return;
        }

        var tail = await _logs.ReadTailAsync(SelectedSource.File, LogLimits.DefaultTailLines, cancellationToken);
        if (tail.TooLarge)
        {
            Notice = Localizer.Format("logs.tooLarge", Bytes.Format(SelectedSource.File.Bytes), Bytes.Format(LogLimits.MaxFollowedBytes));
        }
        else if (tail.Missing)
        {
            Notice = MissingText;
        }

        foreach (var line in tail.Lines)
        {
            Lines.Add(line.Text);
        }

        OnPropertyChanged(nameof(HasNotice));
        OnPropertyChanged(nameof(CanExport));
    }

    partial void OnSelectedSourceChanged(LogFileRowViewModel? value)
    {
        _ = TailAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        if (_context.ActiveId is not { } instanceId || SelectedSource is null)
        {
            return;
        }

        var destination = await _dialogs.PickSaveFileAsync(
            ExportLabel,
            $"{_context.Active?.Name ?? "instance"}-{SelectedSource.Kind}.log",
            "Log files",
            "*.log",
            cancellationToken
        );

        if (destination is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var tail = await _logs.ReadTailAsync(SelectedSource.File, LogLimits.DefaultTailLines, cancellationToken);
            var path = await _logs.ExportLinesAsync(tail.Lines, destination, cancellationToken);
            Status = Localizer.Format("logs.exported", path);
        });
    }

    [RelayCommand]
    private async Task DiagnosticsAsync(CancellationToken cancellationToken)
    {
        if (_context.ActiveId is not { } instanceId)
        {
            return;
        }

        var destination = await _dialogs.PickSaveFileAsync(DiagnosticsLabel, "diagnostics.txt", "Text", "*.txt", cancellationToken);
        if (destination is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var path = await _logs.ExportDiagnosticsAsync(instanceId, destination, cancellationToken);
            Status = Localizer.Format("logs.exported", path);
        });
    }

    [RelayCommand]
    private async Task OpenFolderAsync(CancellationToken cancellationToken)
    {
        if (SelectedSource is null)
        {
            return;
        }

        var folder = Path.GetDirectoryName(SelectedSource.Path);
        if (folder is not null)
        {
            await _dialogs.InformAsync(folder, string.Empty, cancellationToken);
        }
    }
}

/// <summary>One source in the list: its kind, size and newest line date.</summary>
public sealed class LogFileRowViewModel : ViewModelBase
{
    public LogFileRowViewModel(ILocalizer localizer, LogFile file)
        : base(localizer)
    {
        File = file;
    }

    public LogFile File { get; }

    public string Path
    {
        get { return File.Path; }
    }

    public LogSourceKind Kind
    {
        get { return File.Kind; }
    }

    public string KindText
    {
        get { return Text("logs.kind" + File.Kind); }
    }

    public string Label
    {
        get { return File.Label; }
    }

    public string SizeText
    {
        get { return Bytes.Format(File.Bytes); }
    }

    public string WhenText
    {
        get
        {
            return File.LastWrite is { } when
                ? when.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture)
                : string.Empty;
        }
    }
}
