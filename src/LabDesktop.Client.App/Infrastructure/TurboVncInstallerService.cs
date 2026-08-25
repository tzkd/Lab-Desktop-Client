using System.Diagnostics;
using System.Security.Cryptography;
using LabDesktop.Client.App.Diagnostics;

namespace LabDesktop.Client.App.Infrastructure;

internal sealed class TurboVncInstallerService
{
    public const string Version = "3.3";
    public const string InstallerUrl =
        "https://github.com/TurboVNC/turbovnc/releases/download/3.3/TurboVNC-3.3.exe";
    public const string InstallerSha256 =
        "29882a078de6cc9c12da97be4eab42299c1206c6a78ba77bbd89377c45d7d89d";

    private readonly HttpClient _httpClient;
    private readonly FileLogger _logger;

    public TurboVncInstallerService(FileLogger logger, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        var downloadDirectory = Path.Combine(Path.GetTempPath(), "LabDesktopClient");
        Directory.CreateDirectory(downloadDirectory);
        var installerPath = Path.Combine(downloadDirectory, $"TurboVNC-{Version}.exe");

        try
        {
            await DownloadAndVerifyAsync(installerPath, cancellationToken).ConfigureAwait(false);
            _logger.Info($"Starting official TurboVNC {Version} installer");
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            }) ?? throw new InvalidOperationException("无法启动 TurboVNC 安装器。");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"TurboVNC 安装器退出，状态码 {process.ExitCode}。");
            }

            _logger.Info($"Official TurboVNC {Version} installer completed");
        }
        finally
        {
            try
            {
                File.Delete(installerPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal static async Task<bool> HasExpectedSha256Async(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        await using var stream = File.OpenRead(path);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(
            Convert.ToHexString(digest).ToLowerInvariant(),
            InstallerSha256,
            StringComparison.Ordinal);
    }

    private async Task DownloadAndVerifyAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            InstallerUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var temporaryPath = path + ".download";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            if (!await HasExpectedSha256Async(temporaryPath, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("TurboVNC 安装包 SHA-256 校验失败，已拒绝执行。");
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
