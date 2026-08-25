using LabDesktop.Client.App.Configuration;
using LabDesktop.Client.App.Diagnostics;
using LabDesktop.Client.Core;

namespace LabDesktop.Client.App.Security;

internal sealed record HostKeyPrompt(
    HostTrustDecision Decision,
    HostKeyIdentity Presented,
    TrustedHost? Existing);

internal sealed class PersistentHostKeyVerifier : IHostKeyVerifier
{
    private readonly ClientSettings _settings;
    private readonly JsonSettingsStore _store;
    private readonly Func<HostKeyPrompt, bool> _prompt;
    private readonly FileLogger _logger;
    private readonly object _lock = new();

    public PersistentHostKeyVerifier(
        ClientSettings settings,
        JsonSettingsStore store,
        Func<HostKeyPrompt, bool> prompt,
        FileLogger logger)
    {
        _settings = settings;
        _store = store;
        _prompt = prompt;
        _logger = logger;
    }

    public bool Verify(HostKeyIdentity identity)
    {
        lock (_lock)
        {
            var existing = _settings.TrustedHosts.FirstOrDefault(item =>
                string.Equals(
                    item.RouteIdentity,
                    identity.RouteIdentity,
                    StringComparison.OrdinalIgnoreCase));
            var decision = HostTrustPolicy.Evaluate(existing, identity);
            if (decision == HostTrustDecision.Match)
            {
                return true;
            }

            if (decision == HostTrustDecision.Mismatch)
            {
                _logger.Info($"Rejected changed host key for {identity.RouteIdentity}");
                _prompt(new HostKeyPrompt(decision, identity, existing));
                return false;
            }

            if (!_prompt(new HostKeyPrompt(decision, identity, null)))
            {
                _logger.Info($"User rejected new host key for {identity.RouteIdentity}");
                return false;
            }

            _settings.TrustedHosts.Add(new TrustedHost(
                identity.RouteIdentity,
                identity.Algorithm,
                identity.Sha256Fingerprint,
                DateTimeOffset.UtcNow));
            _store.Save(_settings);
            _logger.Info($"Trusted new host key for {identity.RouteIdentity}");
            return true;
        }
    }

    public bool Forget(string routeIdentity)
    {
        lock (_lock)
        {
            var removed = _settings.TrustedHosts.RemoveAll(item =>
                string.Equals(item.RouteIdentity, routeIdentity, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                return false;
            }

            _store.Save(_settings);
            _logger.Info($"Forgot host key for {routeIdentity}");
            return true;
        }
    }
}
