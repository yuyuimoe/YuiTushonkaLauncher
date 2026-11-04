using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class ServerHttpConfigTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
    }

    private string Write(string json)
    {
        var path = _temp.Combine("http.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Read_TakesTheBindAddressAndThePort()
    {
        var (endpoint, error) = ServerHttpConfig.Read(Write("""{"ip":"0.0.0.0","port":6970,"backendPort":6970}"""));

        error.Should().BeNull();
        endpoint!.Host.Should().Be("0.0.0.0");
        endpoint.Port.Should().Be(6970);
    }

    [Fact]
    public void Read_FallsBackToLoopbackAndTheDefaultPort_WhenTheKeysAreAbsent()
    {
        var (endpoint, _) = ServerHttpConfig.Read(Write("""{"logRequests":true}"""));

        endpoint!.Host.Should().Be("127.0.0.1");
        endpoint.Port.Should().Be(6969);
    }

    [Fact]
    public void Read_ToleratesATrailingComma()
    {
        var (endpoint, error) = ServerHttpConfig.Read(Write("""{"port":6971,}"""));

        error.Should().BeNull();
        endpoint!.Port.Should().Be(6971);
    }

    [Theory]
    [InlineData("""{"port":"six"}""", "not a number")]
    [InlineData("""{"port":0}""", "not a valid TCP port")]
    [InlineData("""{"port":65536}""", "not a valid TCP port")]
    [InlineData("[1,2]", "does not contain a JSON object")]
    [InlineData("{broken", "not valid JSON")]
    public void Read_SaysWhyAFileCannotBeUsed(string json, string reason)
    {
        var (endpoint, error) = ServerHttpConfig.Read(Write(json));

        endpoint.Should().BeNull();
        error.Should().Contain(reason);
    }

    [Fact]
    public void Read_ReportsAMissingFileInsteadOfThrowing()
    {
        var (endpoint, error) = ServerHttpConfig.Read(_temp.Combine("nope.json"));

        endpoint.Should().BeNull();
        error.Should().Contain("cannot be read");
    }

    [Fact]
    public void WithPort_ChangesThePort_AndKeepsEveryOtherKey()
    {
        var path = Write(
            """{"ip":"0.0.0.0","port":6969,"backendIp":"0.0.0.0","backendPort":6969,"logRequests":true,"webAuthenticationConfig":{"enabled":true}}"""
        );

        var changed = JsonNode.Parse(ServerHttpConfig.WithPort(path, 7005))!.AsObject();

        changed["port"]!.GetValue<int>().Should().Be(7005);
        changed["ip"]!.GetValue<string>().Should().Be("0.0.0.0");
        changed["logRequests"]!.GetValue<bool>().Should().BeTrue();
        changed["webAuthenticationConfig"]!["enabled"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void WithPort_MovesBackendPortToo_WhenItFollowedThePort()
    {
        var path = Write("""{"port":6969,"backendPort":6969}""");

        JsonNode.Parse(ServerHttpConfig.WithPort(path, 7005))!["backendPort"]!.GetValue<int>().Should().Be(7005);
    }

    [Fact]
    public void WithPort_LeavesBackendPortAlone_WhenSomeoneSetItApart()
    {
        // A reverse proxy: clients are told 443 while the server listens on 6969. That is deliberate, so it stays.
        var path = Write("""{"port":6969,"backendPort":443}""");

        var changed = JsonNode.Parse(ServerHttpConfig.WithPort(path, 7005))!;

        changed["port"]!.GetValue<int>().Should().Be(7005);
        changed["backendPort"]!.GetValue<int>().Should().Be(443);
    }

    [Fact]
    public void WithPort_AddsBackendPort_WhenTheFileHadNone()
    {
        JsonNode.Parse(ServerHttpConfig.WithPort(Write("""{"ip":"127.0.0.1"}"""), 7005))!["backendPort"]!.GetValue<int>().Should().Be(7005);
    }

    [Fact]
    public void WithPort_RefusesAFileThatIsNotAJsonObject()
    {
        var path = Write("[1,2,3]");

        var act = () => ServerHttpConfig.WithPort(path, 7005);

        act.Should().Throw<InvalidDataException>();
    }
}
