namespace LabDesktop.Client.Core;

public sealed record TrustedHost(
    string RouteIdentity,
    string Algorithm,
    string Sha256Fingerprint,
    DateTimeOffset TrustedAtUtc);

public enum HostTrustDecision
{
    Unknown,
    Match,
    Mismatch
}

public static class HostTrustPolicy
{
    public static HostTrustDecision Evaluate(TrustedHost? trustedHost, HostKeyIdentity presented)
    {
        if (trustedHost is null)
        {
            return HostTrustDecision.Unknown;
        }

        return string.Equals(
                   trustedHost.RouteIdentity,
                   presented.RouteIdentity,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(trustedHost.Algorithm, presented.Algorithm, StringComparison.Ordinal) &&
               string.Equals(
                   trustedHost.Sha256Fingerprint,
                   presented.Sha256Fingerprint,
                   StringComparison.Ordinal)
            ? HostTrustDecision.Match
            : HostTrustDecision.Mismatch;
    }
}
