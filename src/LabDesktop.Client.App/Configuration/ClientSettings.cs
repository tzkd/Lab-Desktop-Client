using LabDesktop.Client.Core;

namespace LabDesktop.Client.App.Configuration;

internal sealed class ClientSettings
{
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;

    public ConnectionProfile Profile { get; set; } = new();

    public bool RememberCredential { get; set; }

    public List<TrustedHost> TrustedHosts { get; set; } = [];
}

internal sealed record SettingsLoadResult(ClientSettings Settings, string? Warning);
