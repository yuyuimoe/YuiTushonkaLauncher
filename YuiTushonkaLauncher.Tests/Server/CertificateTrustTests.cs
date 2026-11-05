using System;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Server;

/// <summary>
/// D25: a server elsewhere is trusted on first use. The certificate is accepted — SPT servers are self-signed and a CA
/// would reject every real one — and the fingerprint is remembered so a different certificate later is *reported*
/// instead of passing unnoticed. Nothing here ever blocks a connection.
/// </summary>
public sealed class CertificateTrustTests
{
    [Fact]
    public void TheFirstCertificateIsRecordedAndTheSameOneIsUnchanged()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);

        store.Evaluate("10.0.0.5:6969", "aaa").Outcome.Should().Be(CertificateTrustOutcome.FirstSeen);
        store.Evaluate("10.0.0.5:6969", "aaa").Outcome.Should().Be(CertificateTrustOutcome.Unchanged);

        store.Find("10.0.0.5:6969")!.Fingerprint.Should().Be("aaa");
        File.Exists(temp.Combine("data", "cache", "server-certificates.json")).Should().BeTrue("the record survives a restart");
    }

    [Fact]
    public void ARecordSurvivesANewStoreOnTheSameDirectory()
    {
        using var temp = new TempDirectory();
        Store(temp).Evaluate("10.0.0.5:6969", "aaa");

        Store(temp).Evaluate("10.0.0.5:6969", "aaa").Outcome.Should().Be(CertificateTrustOutcome.Unchanged);
    }

    [Fact]
    public void ADifferentCertificateIsReportedAndKeepsTheOldRecord()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        CertificateTrustChange? seen = null;
        store.Changed += (_, change) => seen = change;
        store.Evaluate("10.0.0.5:6969", "aaa");

        var result = store.Evaluate("10.0.0.5:6969", "bbb");

        result.Outcome.Should().Be(CertificateTrustOutcome.Changed);
        result.Previous.Should().Be("aaa");
        seen.Should().NotBeNull();
        seen!.Address.Should().Be("10.0.0.5:6969");
        store.Find("10.0.0.5:6969")!.Fingerprint.Should().Be("aaa", "one bad connection does not replace a record");
    }

    [Fact]
    public void AnUnreadableRecordStartsFromNothingInsteadOfFailing()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();
        paths.EnsureCreated();
        Directory.CreateDirectory(paths.CacheDirectory);
        File.WriteAllText(Path.Combine(paths.CacheDirectory, "server-certificates.json"), "{ not json");

        var store = new CertificateTrustStore(paths, TimeProvider.System, NullLogger<CertificateTrustStore>.Instance);

        store.Evaluate("10.0.0.5:6969", "aaa").Outcome.Should().Be(CertificateTrustOutcome.FirstSeen);
    }

    [Fact]
    public void ForgettingAnAddressMakesTheNextCertificateFirstSeen()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);
        store.Evaluate("10.0.0.5:6969", "aaa");

        store.Forget("10.0.0.5:6969");

        store.Find("10.0.0.5:6969").Should().BeNull();
        store.Evaluate("10.0.0.5:6969", "bbb").Outcome.Should().Be(CertificateTrustOutcome.FirstSeen);
    }

    [Fact]
    public void TheHandlerRecordsTheFingerprintAndNeverRefusesTheConnection()
    {
        var trust = new RecordingTrust();
        using var certificate = SelfSigned();
        var handler = (SocketsHttpHandler)SptServerClient.CreateHandler(trust, "10.0.0.5:6969");

        var accepted = handler.SslOptions.RemoteCertificateValidationCallback!(
            new object(),
            certificate,
            chain: null!,
            SslPolicyErrors.RemoteCertificateChainErrors
        );

        accepted.Should().BeTrue("a self-signed certificate is the norm for SPT, never a refusal");
        trust.Evaluated.Should().ContainSingle();
        trust.Evaluated[0].Address.Should().Be("10.0.0.5:6969");
        trust
            .Evaluated[0]
            .Fingerprint.Should()
            .Be(
                CertificateTrustStore.Fingerprint(certificate.GetRawCertData()),
                "the fingerprint is the SHA-256 of the certificate's own bytes"
            );
    }

    [Fact]
    public void WithoutATrustStoreTheHandlerStillAccepts()
    {
        using var certificate = SelfSigned();
        var handler = (SocketsHttpHandler)SptServerClient.CreateHandler();

        handler
            .SslOptions.RemoteCertificateValidationCallback!(new object(), certificate, chain: null!, SslPolicyErrors.None)
            .Should()
            .BeTrue();
    }

    private static CertificateTrustStore Store(TempDirectory temp)
    {
        var paths = temp.CreateAppPaths();
        paths.EnsureCreated();
        return new CertificateTrustStore(paths, TimeProvider.System, NullLogger<CertificateTrustStore>.Instance);
    }

    private static X509Certificate2 SelfSigned()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    /// <summary>A trust store that only records, for the handler test.</summary>
    private sealed class RecordingTrust : ICertificateTrust
    {
        public List<(string Address, string Fingerprint)> Evaluated { get; } = [];

        public event EventHandler<CertificateTrustChange>? Changed
        {
            add { }
            remove { }
        }

        public CertificateTrustResult Evaluate(string address, string fingerprint)
        {
            Evaluated.Add((address, fingerprint));
            return new CertificateTrustResult(CertificateTrustOutcome.FirstSeen);
        }

        public CertificateTrustInfo? Find(string address)
        {
            return null;
        }

        public void Forget(string address) { }
    }
}
