using System.Diagnostics;
using LabDesktop.Client.App.Diagnostics;
using LabDesktop.Client.Core;

namespace LabDesktop.Client.App.Infrastructure;

internal sealed class TurboVncViewerLauncher : IViewerLauncher
{
    public const string DownloadUrl = "https://github.com/TurboVNC/turbovnc/releases";
    private readonly FileLogger _logger;

    public TurboVncViewerLauncher(FileLogger logger)
    {
        _logger = logger;
    }

    public string? FindViewer(string? configuredPath)
    {
        foreach (var candidate in CandidatePaths(configuredPath))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (string.Equals(
                    Path.GetFileName(candidate),
                    "vncviewerw.bat",
                    StringComparison.OrdinalIgnoreCase))
            {
                var waitableViewer = Path.Combine(
                    Path.GetDirectoryName(candidate)!,
                    "vncviewer.bat");
                if (File.Exists(waitableViewer))
                {
                    return Path.GetFullPath(waitableViewer);
                }
            }

            return Path.GetFullPath(candidate);
        }

        return null;
    }

    public Task<IViewerSession> LaunchAsync(
        string? configuredPath,
        ushort localPort,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var viewer = FindViewer(configuredPath);
        if (viewer is null)
        {
            throw new DesktopClientException(
                DesktopErrorCode.ViewerNotFound,
                "未找到 TurboVNC Viewer。请安装 Viewer，或在客户端中选择 vncviewer.bat。");
        }

        var endpoint = $"127.0.0.1::{localPort}";
        var startInfo = BuildStartInfo(viewer, endpoint);
        try
        {
            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("进程未能启动。");
            _logger.Info($"TurboVNC Viewer started: local-port={localPort}");
            return Task.FromResult<IViewerSession>(new ViewerSession(process, _logger));
        }
        catch (Exception exception) when (exception is not DesktopClientException)
        {
            throw new DesktopClientException(
                DesktopErrorCode.ViewerStartFailed,
                "TurboVNC Viewer 启动失败。",
                exception);
        }
    }

    private static ProcessStartInfo BuildStartInfo(string viewer, string endpoint)
    {
        if (string.Equals(Path.GetExtension(viewer), ".bat", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(viewer), ".cmd", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/d /s /c \"\"{viewer}\" \"{endpoint}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(viewer)!
            };
        }

        var executableInfo = new ProcessStartInfo
        {
            FileName = viewer,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(viewer)!
        };
        executableInfo.ArgumentList.Add(endpoint);
        return executableInfo;
    }

    private static IEnumerable<string> CandidatePaths(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return Environment.ExpandEnvironmentVariables(configuredPath.Trim());
        }

        yield return Path.Combine(AppContext.BaseDirectory, "TurboVNC", "vncviewer.bat");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "TurboVNC", "vncviewer.bat");
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 }.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            yield return Path.Combine(root, "TurboVNC", "vncviewer.bat");
            yield return Path.Combine(root, "TurboVNC", "bin", "vncviewer.exe");
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in pathEntries)
        {
            yield return Path.Combine(directory, "vncviewer.exe");
            yield return Path.Combine(directory, "vncviewer.bat");
        }
    }

    private sealed class ViewerSession : IViewerSession
    {
        private readonly Process _process;
        private readonly FileLogger _logger;
        private int _disposed;

        public ViewerSession(Process process, FileLogger logger)
        {
            _process = process;
            _logger = logger;
            Completion = process.WaitForExitAsync();
        }

        public Task Completion { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
            {
                _logger.Info("TurboVNC Viewer process ended during cleanup");
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
