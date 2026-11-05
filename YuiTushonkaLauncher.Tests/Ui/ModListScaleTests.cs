using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Infrastructure.Persistence;
using YuiTushonkaLauncher.Services;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// The mod list at the size the phase asks about: a thousand entries must stay one collection of reused rows, and a
/// keystroke must not rebuild them. A blank window or an allocation per frame is what this catches.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class ModListScaleTests
{
    private const int Rows = 1000;

    [Fact]
    public async Task AThousandModsLoadIntoOneReusedRowCollection()
    {
        await using var world = await UiWorld.CreateAsync();
        var instance = await world.SeedInstanceAsync("big");
        await AddModsAsync(world, instance.Id, Rows);

        var page = world.Shell.ModsPage;
        await page.LoadAsync(default);

        page.Mods.Should().HaveCount(Rows);
        page.CountText.Should().Contain(Rows.ToString(System.Globalization.CultureInfo.CurrentCulture));
        page.HasMods.Should().BeTrue();

        // Filtering narrows the collection but keeps the same row objects: no view model is rebuilt per keystroke.
        var before = page.Mods.ToDictionary(row => row.Id, row => row);
        page.Search = "mod 7";
        var filtered = page.Mods.ToList();

        filtered.Should().NotBeEmpty();
        filtered.Should().OnlyContain(row => row.Matches("mod 7"));
        foreach (var row in filtered)
        {
            before[row.Id].Should().BeSameAs(row, "the row objects are reused, so the list does not churn");
        }

        page.Search = string.Empty;
        page.Mods.Should().HaveCount(Rows);
    }

    [Fact]
    public async Task FilteringAThousandRowsIsLinearAndCheap()
    {
        await using var world = await UiWorld.CreateAsync();
        var instance = await world.SeedInstanceAsync("timing");
        await AddModsAsync(world, instance.Id, Rows);

        var page = world.Shell.ModsPage;
        await page.LoadAsync(default);

        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 50; index++)
        {
            page.Search = "mod " + index;
        }

        stopwatch.Stop();

        // Fifty filters over a thousand rows: a per-frame rebuild would be seconds; a linear scan is milliseconds. The
        // bound is generous so a busy machine does not make this flaky.
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "filtering is a linear scan over cached rows");
    }

    /// <summary>Writes the rows straight to the database: installing a thousand archives would test the installer, not the list.</summary>
    private static async Task AddModsAsync(UiWorld world, Guid instanceId, int count)
    {
        var unitOfWork = world.Get<IUnitOfWork>();
        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                for (var index = 0; index < count; index++)
                {
                    context.InstalledMods.Add(
                        new InstalledMod
                        {
                            Id = Guid.NewGuid(),
                            InstanceId = instanceId,
                            Guid = "com.example.mod" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            Name = "Mod " + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            Version = new SemanticVersion(1, index, 0),
                            Kind = ModKind.Client,
                            InstalledAt = DateTimeOffset.UnixEpoch,
                        }
                    );
                }

                await Task.CompletedTask;
            },
            CancellationToken.None
        );
    }
}
