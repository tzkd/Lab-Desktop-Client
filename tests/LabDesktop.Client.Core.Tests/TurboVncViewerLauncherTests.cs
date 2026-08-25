using LabDesktop.Client.App.Diagnostics;
using LabDesktop.Client.App.Infrastructure;

namespace LabDesktop.Client.Core.Tests;

public sealed class TurboVncViewerLauncherTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"lab-desktop-viewer-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ExplicitViewerPathTakesPrecedence()
    {
        Directory.CreateDirectory(_directory);
        var viewer = Path.Combine(_directory, "vncviewer.bat");
        File.WriteAllText(viewer, "@echo off");
        var launcher = CreateLauncher();

        Assert.Equal(Path.GetFullPath(viewer), launcher.FindViewer(viewer));
    }

    [Fact]
    public void NonWaitableWindowsLauncherResolvesToWaitableSibling()
    {
        Directory.CreateDirectory(_directory);
        var backgroundViewer = Path.Combine(_directory, "vncviewerw.bat");
        var waitableViewer = Path.Combine(_directory, "vncviewer.bat");
        File.WriteAllText(backgroundViewer, "@echo off");
        File.WriteAllText(waitableViewer, "@echo off");
        var launcher = CreateLauncher();

        Assert.Equal(Path.GetFullPath(waitableViewer), launcher.FindViewer(backgroundViewer));
    }

    [Fact]
    public async Task BatchViewerReceivesLoopbackEndpointAndCanBeMonitored()
    {
        var viewerDirectory = Path.Combine(_directory, "viewer package");
        Directory.CreateDirectory(viewerDirectory);
        var viewer = Path.Combine(viewerDirectory, "vncviewer.bat");
        var capturedEndpoint = Path.Combine(viewerDirectory, "endpoint.txt");
        File.WriteAllText(viewer, $"@echo off{Environment.NewLine}echo %~1>\"{capturedEndpoint}\"");
        var launcher = CreateLauncher();

        await using var session = await launcher.LaunchAsync(viewer, 49152, CancellationToken.None);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("127.0.0.1::49152", File.ReadAllText(capturedEndpoint).Trim());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private TurboVncViewerLauncher CreateLauncher() =>
        new(new FileLogger(Path.Combine(_directory, "client.log")));
}
