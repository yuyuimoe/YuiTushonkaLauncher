using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Infrastructure;
using YuiTushonkaLauncher.Infrastructure.Forge;
using YuiTushonkaLauncher.Infrastructure.Paths;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Forge;

public sealed class ForgeRegistrationTests
{
    [Fact]
    public void AddLauncherForge_ResolvesTheWholeClientStack()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();
        paths.EnsureCreated();

        using var provider = new ServiceCollection()
            .AddSingleton<IAppPaths>(paths)
            .AddLogging(builder => builder.AddProvider(NullLoggerProvider.Instance))
            .AddLauncherPersistence()
            .AddLauncherForge()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        provider.GetRequiredService<IForgeClient>().Should().BeOfType<SptForgeClient>();
        provider.GetRequiredService<IForgeDownloader>().Should().BeOfType<ForgeDownloader>();
        provider.GetRequiredService<ForgeRateLimiter>().Should().NotBeNull();
        provider.GetRequiredService<ForgeResponseCache>().Should().NotBeNull();
        provider.GetRequiredService<ForgeRetryExecutor>().Should().NotBeNull();
        provider.GetRequiredService<ForgeSptVersionCatalog>().Should().NotBeNull();
    }

    [Fact]
    public void AddLauncherForge_ConfiguresTheHttpClientFromOptions()
    {
        using var provider = new ServiceCollection()
            .AddLogging(builder => builder.AddProvider(NullLoggerProvider.Instance))
            .AddLauncherForge()
            .Configure<ForgeOptions>(options =>
            {
                options.BaseUrl = "https://example.test/api/v0";
                options.UserAgent = "YuiTushonkaLauncher/test";
                options.TimeoutSeconds = 12;
            })
            .BuildServiceProvider();

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(nameof(IForgeClient));

        client.BaseAddress.Should().Be(new Uri("https://example.test/api/v0/"));
        client.Timeout.Should().Be(TimeSpan.FromSeconds(12));
        client.DefaultRequestHeaders.UserAgent.ToString().Should().Contain("YuiTushonkaLauncher");
    }

    [Fact]
    public void Options_DefaultToTheLiveApiAndStayUnderItsQuota()
    {
        var options = new ForgeOptions();

        options.BaseUrl.Should().StartWith("https://sp-mod.com/api/v0");
        options.SustainedPermits.Should().BeLessThan(300, "the API advertises 300 requests/minute");
        options.MaxAttempts.Should().BeGreaterThan(1);
    }
}
