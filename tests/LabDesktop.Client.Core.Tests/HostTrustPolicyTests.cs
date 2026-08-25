using LabDesktop.Client.Core;

namespace LabDesktop.Client.Core.Tests;

public sealed class HostTrustPolicyTests
{
    private static readonly HostKeyIdentity Presented = new(
        "abc@gateway.example.test:2222",
        "ssh-ed25519",
        "SHA256:example");

    [Fact]
    public void UnknownHostRequiresExplicitTrust()
    {
        Assert.Equal(HostTrustDecision.Unknown, HostTrustPolicy.Evaluate(null, Presented));
    }

    [Fact]
    public void ExactIdentityAndKeyMatchIsTrusted()
    {
        var trusted = new TrustedHost(
            Presented.RouteIdentity,
            Presented.Algorithm,
            Presented.Sha256Fingerprint,
            DateTimeOffset.UtcNow);

        Assert.Equal(HostTrustDecision.Match, HostTrustPolicy.Evaluate(trusted, Presented));
    }

    [Theory]
    [InlineData("bcd@gateway.example.test:2222", "ssh-ed25519", "SHA256:example")]
    [InlineData("abc@gateway.example.test:2222", "rsa-sha2-512", "SHA256:example")]
    [InlineData("abc@gateway.example.test:2222", "ssh-ed25519", "SHA256:changed")]
    public void AnyIdentityOrKeyChangeIsRejected(string route, string algorithm, string fingerprint)
    {
        var trusted = new TrustedHost(
            route,
            algorithm,
            fingerprint,
            DateTimeOffset.UtcNow);

        Assert.Equal(HostTrustDecision.Mismatch, HostTrustPolicy.Evaluate(trusted, Presented));
    }
}
