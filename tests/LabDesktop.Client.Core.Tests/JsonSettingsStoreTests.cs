using LabDesktop.Client.App.Configuration;
using LabDesktop.Client.Core;

namespace LabDesktop.Client.Core.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"lab-desktop-client-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MissingFileLoadsSafeDefaults()
    {
        var result = CreateStore().Load();

        Assert.Null(result.Warning);
        Assert.Equal(ClientSettings.CurrentSchema, result.Settings.Schema);
        Assert.Empty(result.Settings.Profile.Host);
        Assert.Equal(0, result.Settings.Profile.Port);
        Assert.Empty(result.Settings.Profile.UserName);
        Assert.Equal(ConnectionProfile.DefaultGeometry, result.Settings.Profile.Geometry);
        Assert.Empty(result.Settings.TrustedHosts);
    }

    [Fact]
    public void SaveAndLoadPreservesProfileAndTrustButHasNoPasswordField()
    {
        var store = CreateStore();
        var settings = new ClientSettings
        {
            Profile = new ConnectionProfile
            {
                Host = "example.test",
                Port = 2222,
                UserName = "abc",
                Mode = ConnectionMode.IsaacSim,
                Geometry = "2560x1440"
            },
            RememberCredential = true,
            TrustedHosts =
            [
                new TrustedHost(
                    "abc@example.test:2222",
                    "ssh-ed25519",
                    "SHA256:test",
                    DateTimeOffset.UtcNow)
            ]
        };

        store.Save(settings);
        var loaded = store.Load();
        var json = File.ReadAllText(Path.Combine(_directory, "settings.json"));

        Assert.Equal("abc", loaded.Settings.Profile.UserName);
        Assert.Equal("example.test", loaded.Settings.Profile.Host);
        Assert.Equal(2222, loaded.Settings.Profile.Port);
        Assert.Equal(ConnectionMode.IsaacSim, loaded.Settings.Profile.Mode);
        Assert.True(loaded.Settings.RememberCredential);
        Assert.Single(loaded.Settings.TrustedHosts);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorruptSettingsAreBackedUpBeforeDefaultsAreUsed()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{not-json");

        var result = CreateStore().Load();

        Assert.NotNull(result.Warning);
        Assert.Single(Directory.GetFiles(_directory, "settings.json.corrupt-*"));
        Assert.Empty(result.Settings.Profile.Host);
        Assert.Equal(0, result.Settings.Profile.Port);
    }

    [Fact]
    public void UnknownConnectionModeIsRejectedAsCorruptSettings()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """
            {
              "schema": 1,
              "profile": {
                "name": "invalid",
                "mode": 99,
                "host": "example.test",
                "port": 22,
                "userName": "abc",
                "geometry": "1920x1080"
              },
              "trustedHosts": []
            }
            """);

        var result = CreateStore().Load();

        Assert.NotNull(result.Warning);
        Assert.Equal(ConnectionMode.LinuxDesktop, result.Settings.Profile.Mode);
        Assert.Single(Directory.GetFiles(_directory, "settings.json.corrupt-*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonSettingsStore CreateStore() =>
        new(Path.Combine(_directory, "settings.json"));
}
