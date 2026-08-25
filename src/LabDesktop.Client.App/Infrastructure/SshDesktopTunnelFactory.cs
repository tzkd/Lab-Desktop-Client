using System.Globalization;
using LabDesktop.Client.App.Diagnostics;
using LabDesktop.Client.Core;
using Renci.SshNet;

namespace LabDesktop.Client.App.Infrastructure;

internal sealed class SshDesktopTunnelFactory : IDesktopTunnelFactory
{
    private static readonly TimeSpan DesktopStartupTimeout = TimeSpan.FromSeconds(45);
    private readonly SshClientConnector _connector;
    private readonly FileLogger _logger;

    public SshDesktopTunnelFactory(SshClientConnector connector, FileLogger logger)
    {
        _connector = connector;
        _logger = logger;
    }

    public async Task<IDesktopTunnel> OpenAsync(
        ConnectionRequest request,
        IHostKeyVerifier hostKeyVerifier,
        IProgress<ConnectionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var client = await _connector.ConnectAsync(
            request,
            hostKeyVerifier,
            cancellationToken).ConfigureAwait(false);

        ForwardedPortLocal? forwardedPort = null;
        SshCommand? command = null;
        CancellationTokenSource? commandCancellation = null;
        try
        {
            progress?.Report(new ConnectionProgress(
                ConnectionPhase.StartingDesktop,
                "正在启动或恢复远程桌面…"));

            forwardedPort = new ForwardedPortLocal(
                "127.0.0.1",
                0,
                "127.0.0.1",
                5901);
            client.AddForwardedPort(forwardedPort);
            forwardedPort.Start();

            command = client.CreateCommand(
                $"lab-desktop attach --geometry {request.Profile.Geometry}");
            command.CommandTimeout = Timeout.InfiniteTimeSpan;
            commandCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var commandTask = command.ExecuteAsync(commandCancellation.Token);
            await WaitForDesktopReadyAsync(command, commandTask, cancellationToken).ConfigureAwait(false);

            var localPort = checked((ushort)forwardedPort.BoundPort);
            _logger.Info($"Desktop ready: {request.Profile.RouteIdentity} local-port={localPort}");
            return new SshDesktopTunnel(
                client,
                forwardedPort,
                command,
                commandTask,
                commandCancellation,
                localPort,
                _logger);
        }
        catch (Exception exception)
        {
            commandCancellation?.Cancel();
            commandCancellation?.Dispose();
            command?.Dispose();
            forwardedPort?.Dispose();
            client.Dispose();
            throw SshClientConnector.MapException(
                exception,
                hostKeyRejected: false,
                request.Profile.RouteIdentity);
        }
    }

    private static async Task WaitForDesktopReadyAsync(
        SshCommand command,
        Task commandTask,
        CancellationToken cancellationToken)
    {
        using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupTimeout.CancelAfter(DesktopStartupTimeout);
        using var reader = new StreamReader(command.OutputStream, leaveOpen: true);

        try
        {
            while (true)
            {
                var lineTask = reader.ReadLineAsync(startupTimeout.Token).AsTask();
                var completed = await Task.WhenAny(lineTask, commandTask).ConfigureAwait(false);
                if (completed == commandTask)
                {
                    await commandTask.ConfigureAwait(false);
                    var detail = string.IsNullOrWhiteSpace(command.Error)
                        ? "远程桌面命令提前退出。"
                        : command.Error.Trim();
                    throw new DesktopClientException(
                        DesktopErrorCode.DesktopStartFailed,
                        detail);
                }

                var line = await lineTask.ConfigureAwait(false);
                if (line is null)
                {
                    throw new DesktopClientException(
                        DesktopErrorCode.DesktopStartFailed,
                        "远程桌面没有返回启动状态。");
                }

                if (line.StartsWith("running display=:1 port=5901 ", StringComparison.Ordinal))
                {
                    return;
                }

                if (line.StartsWith("ERROR:", StringComparison.Ordinal))
                {
                    throw new DesktopClientException(
                        DesktopErrorCode.DesktopStartFailed,
                        line[6..].Trim());
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DesktopClientException(
                DesktopErrorCode.DesktopStartFailed,
                $"等待远程桌面启动超过 {DesktopStartupTimeout.TotalSeconds:0} 秒。");
        }
    }

    private sealed class SshDesktopTunnel : IDesktopTunnel
    {
        private readonly SshClient _client;
        private readonly ForwardedPortLocal _forwardedPort;
        private readonly SshCommand _command;
        private readonly CancellationTokenSource _commandCancellation;
        private readonly FileLogger _logger;
        private int _disposed;

        public SshDesktopTunnel(
            SshClient client,
            ForwardedPortLocal forwardedPort,
            SshCommand command,
            Task commandTask,
            CancellationTokenSource commandCancellation,
            ushort localPort,
            FileLogger logger)
        {
            _client = client;
            _forwardedPort = forwardedPort;
            _command = command;
            _commandCancellation = commandCancellation;
            _logger = logger;
            LocalPort = localPort;
            Completion = MonitorCommandAsync(commandTask);
        }

        public ushort LocalPort { get; }

        public Task Completion { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _commandCancellation.Cancel();
            try
            {
                if (_forwardedPort.IsStarted)
                {
                    _forwardedPort.Stop();
                }

                if (_client.IsConnected)
                {
                    _client.Disconnect();
                }

                await Completion.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
            {
                _logger.Info("SSH desktop command stopped during cleanup");
            }
            finally
            {
                _command.Dispose();
                _forwardedPort.Dispose();
                _client.Dispose();
                _commandCancellation.Dispose();
            }
        }

        private async Task MonitorCommandAsync(Task commandTask)
        {
            await commandTask.ConfigureAwait(false);
            if (_command.ExitStatus is not 0)
            {
                var detail = string.IsNullOrWhiteSpace(_command.Error)
                    ? $"远程桌面命令退出，状态码 {_command.ExitStatus?.ToString(CultureInfo.InvariantCulture) ?? "未知"}。"
                    : _command.Error.Trim();
                throw new DesktopClientException(
                    DesktopErrorCode.ConnectionLost,
                    detail);
            }
        }
    }
}
