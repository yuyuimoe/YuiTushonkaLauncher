namespace YuiTushonkaLauncher.Core.Server;

/// <summary>What happened the last time a server's certificate was seen.</summary>
public enum CertificateTrustOutcome
{
    /// <summary>Nothing was recorded for this address yet; the fingerprint has just been stored.</summary>
    FirstSeen,

    /// <summary>The certificate is the one that was recorded.</summary>
    Unchanged,

    /// <summary>A certificate is being presented where a different one was recorded.</summary>
    Changed,
}

/// <summary>One address's certificate, as it was last seen.</summary>
/// <param name="Address">The <c>host:port</c> key.</param>
/// <param name="Fingerprint">SHA-256 of the certificate's raw bytes, in hex.</param>
/// <param name="FirstSeen">When this fingerprint was first accepted.</param>
/// <param name="LastSeen">When it was last presented.</param>
public sealed record CertificateTrustInfo(string Address, string Fingerprint, DateTimeOffset FirstSeen, DateTimeOffset LastSeen);

/// <summary>The answer for one presented certificate.</summary>
/// <param name="Outcome">First seen, unchanged, or changed.</param>
/// <param name="Previous">The fingerprint that was recorded before, when it changed.</param>
public sealed record CertificateTrustResult(CertificateTrustOutcome Outcome, string? Previous = null);

/// <summary>Raised when a server presents a certificate other than the one recorded for it.</summary>
/// <param name="Address">The <c>host:port</c> key.</param>
/// <param name="Previous">The fingerprint that was recorded.</param>
/// <param name="Current">The fingerprint now presented.</param>
public sealed record CertificateTrustChange(string Address, string Previous, string Current);

/// <summary>
/// Trust on first use, without blocking. SPT servers create their own certificate, so requiring a public CA would
/// reject every real server and break playing with friends; instead the first certificate seen for an address is
/// recorded and a different one later is reported. Loopback is not recorded: that server is the one this launcher
/// started.
/// </summary>
public interface ICertificateTrust
{
    /// <summary>Raised when an address presents a different certificate than the recorded one.</summary>
    event EventHandler<CertificateTrustChange>? Changed;

    /// <summary>Records or checks the fingerprint presented for an address.</summary>
    CertificateTrustResult Evaluate(string address, string fingerprint);

    /// <summary>The recorded certificate for an address, if any.</summary>
    CertificateTrustInfo? Find(string address);

    /// <summary>Forgets an address, so the next certificate presented counts as first seen.</summary>
    void Forget(string address);
}
