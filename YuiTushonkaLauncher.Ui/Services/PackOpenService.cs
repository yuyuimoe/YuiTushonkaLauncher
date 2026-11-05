using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Core.Packs;
using YuiTushonkaLauncher.Dialogs;
using YuiTushonkaLauncher.Navigation;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Services;

/// <summary>
/// The launcher's half of the <c>.sptpack</c> shell association: the desktop entry and the Windows installer start the
/// app with the package as an argument, and this is what turns that argument into a review and, when the user agrees,
/// an import. It never writes anything the user did not confirm.
/// </summary>
public sealed class PackOpenService(IInstanceContext context, IPackService packs, IDialogService dialogs, ILocalizer localizer)
{
    /// <summary>How many plan lines the dialog lists before it stops and says how many it left out.</summary>
    internal const int ListedItems = 25;

    /// <summary>
    /// The package a launch carried: the first argument that is not a flag and ends in <c>.sptpack</c>. A flag and its
    /// value are skipped, so <c>--lang pt-BR</c> never looks like a package.
    /// </summary>
    public static string? PackageIn(IReadOnlyList<string> args)
    {
        foreach (var argument in args)
        {
            if (argument.Length > 0 && argument[0] != '-' && argument.EndsWith(".sptpack", StringComparison.OrdinalIgnoreCase))
            {
                return argument;
            }
        }

        return null;
    }

    /// <summary>Reviews <paramref name="packagePath" /> against the active instance and imports it when accepted.</summary>
    public async Task RunAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
        {
            await InformAsync(localizer.Format("packs.missing", packagePath), cancellationToken);
            return;
        }

        await context.RefreshAsync(cancellationToken);

        if (context.Active is not { } instance)
        {
            await InformAsync(localizer["packs.noInstance"], cancellationToken);
            return;
        }

        ImportPlan plan;

        try
        {
            plan = await packs.PlanImportAsync(instance.Id, packagePath, cancellationToken);
        }
        catch (PackException exception)
        {
            await InformAsync(localizer.Format("packs.rejected", exception.Message), cancellationToken);
            return;
        }

        var question = new ConfirmDialogViewModel(localizer)
        {
            Title = localizer.Format("packs.importTitle", Path.GetFileName(packagePath)),
            Body = Describe(plan),
            ConfirmLabel = localizer["common.import"],
        };

        if (!await dialogs.ConfirmAsync(question, cancellationToken))
        {
            return;
        }

        var result = await packs.ImportAsync(instance.Id, packagePath, options: null, progress: null, cancellationToken);
        await InformAsync(
            localizer.Format(
                "packs.importDone",
                result.Items.Count(item => item.Outcome is ImportOutcome.Installed or ImportOutcome.Replaced),
                result.Items.Count(item => item.Outcome == ImportOutcome.ConfigApplied),
                result.Items.Count(item => item.Outcome == ImportOutcome.Skipped),
                result.Items.Count(item => item.Outcome == ImportOutcome.Failed)
            ),
            cancellationToken
        );
    }

    /// <summary>The review body: what the package is, what the plan would change, and the first lines of it.</summary>
    internal string Describe(ImportPlan plan)
    {
        var manifest = plan.Manifest;
        var body = new StringBuilder();

        body.AppendLine(
            localizer.Format(
                "packs.review",
                manifest.Mods.Count,
                manifest.Addons.Count,
                manifest.Configs.Count,
                manifest.SptVersion,
                manifest.ShareCode
            )
        );
        body.AppendLine(
            localizer.Format(
                "packs.wouldChange",
                plan.Items.Count(item => item.Action is ImportAction.Install or ImportAction.Replace or ImportAction.ApplyConfig),
                plan.UnavailableCount
            )
        );

        if (plan.Warnings.Count > 0)
        {
            body.AppendLine();
            body.AppendLine(localizer["packs.warnings"]);

            foreach (var warning in plan.Warnings)
            {
                body.AppendLine("• " + warning);
            }
        }

        if (plan.Items.Count > 0)
        {
            body.AppendLine();

            foreach (var item in plan.Items.Take(ListedItems))
            {
                body.AppendLine($"• {localizer["packs.action." + Key(item.Action)]} — {item.Name} [{item.Id}]");
            }

            if (plan.Items.Count > ListedItems)
            {
                body.AppendLine(localizer.Format("packs.moreItems", plan.Items.Count - ListedItems));
            }
        }

        return body.ToString().TrimEnd();
    }

    private static string Key(ImportAction action)
    {
        return action switch
        {
            ImportAction.Install => "install",
            ImportAction.Replace => "replace",
            ImportAction.AlreadyInstalled => "alreadyInstalled",
            ImportAction.ApplyConfig => "applyConfig",
            ImportAction.ConfigUpToDate => "configUpToDate",
            ImportAction.KeepLocalConfig => "keepLocalConfig",
            _ => "unavailable",
        };
    }

    private Task InformAsync(string body, CancellationToken cancellationToken)
    {
        return dialogs.ConfirmAsync(
            new ConfirmDialogViewModel(localizer)
            {
                Title = localizer["packs.title"],
                Body = body,
                ConfirmLabel = localizer["common.close"],
                IsInformational = true,
            },
            cancellationToken
        );
    }
}
