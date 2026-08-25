using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabDesktop.Client.App.Diagnostics;
using LabDesktop.Client.Core;
using Renci.SshNet;

namespace LabDesktop.Client.App.Infrastructure;

internal sealed class SshIsaacSessionFactory : IIsaacSessionFactory
{
    private const int ProtocolVersion = 1;
    private static readonly TimeSpan SessionStartupTimeout = TimeSpan.FromMinutes(6);
    private readonly SshClientConnector _connector;
    private readonly FileLogger _logger;

    public SshIsaacSessionFactory(SshClientConnector connector, FileLogger logger)
    {
        _connector = connector;
        _logger = logger;
    }

    public async Task<IIsaacSession> OpenAsync(
        ConnectionRequest request,
        IHostKeyVerifier hostKeyVerifier,
        IProgress<ConnectionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var client = await _connector.ConnectAsync(
            request,
            hostKeyVerifier,
            cancellationToken).ConfigureAwait(false);
        ForwardedPortLocal? signaling = null;
        ForwardedPortLocal? turn = null;
        LocalOnlyTcpRelay? turnRelay = null;
        string? sessionId = null;
        try
        {
            progress?.Report(new ConnectionProgress(
                ConnectionPhase.StartingIsaac,
                "正在启动用户的 Isaac Sim GUI 会话…"));
            var commandText =
                $"lab-isaac session open --protocol {ProtocolVersion} " +
                $"--geometry {request.Profile.Geometry} --timeout 300 --json";
            var response = await ExecuteJsonAsync<OpenResponse>(
                client,
                commandText,
                SessionStartupTimeout,
                cancellationToken).ConfigureAwait(false);
            Validate(response);
            sessionId = response.SessionId;

            var turnEndpoint = ResolveTurnLocalEndpoint();
            var turnHost = turnEndpoint.Address.ToString();
            _logger.Info(
                $"Isaac TURN local endpoint: interface={turnEndpoint.InterfaceName} " +
                $"address={turnHost}");
            signaling = StartForward(
                client,
                "signaling",
                "127.0.0.1",
                response.Signaling.RemoteHost,
                response.Signaling.RemotePort);
            turn = StartForward(
                client,
                "TURN",
                "127.0.0.1",
                response.Turn.RemoteHost,
                response.Turn.RemotePort);
            turnRelay = LocalOnlyTcpRelay.Start(
                turnEndpoint.Address,
                checked((ushort)turn.BoundPort),
                _logger);
            var descriptor = new IsaacSessionDescriptor(
                response.SessionId,
                response.Version,
                checked((ushort)signaling.BoundPort),
                turnHost,
                turnRelay.BoundPort,
                response.Turn.UserName,
                response.Turn.Credential,
                new DisplayGeometry(response.Resolution.Width, response.Resolution.Height));
            _logger.Info(
                $"Isaac session ready: {request.Profile.RouteIdentity} " +
                $"version={response.Version} signal-port={descriptor.SignalingPort} " +
                $"turn-port={descriptor.TurnPort}");
            return new SshIsaacSession(
                client,
                signaling,
                turn,
                turnRelay,
                descriptor,
                response.LeaseSeconds,
                _logger,
                cancellationToken);
        }
        catch (Exception exception)
        {
            if (turnRelay is not null)
            {
                try
                {
                    await turnRelay.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    _logger.Error("Failed to close the local Isaac TURN relay", cleanupException);
                }
            }
            StopSafely(turn);
            StopSafely(signaling);

            if (sessionId is not null && client.IsConnected)
            {
                try
                {
                    using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var response = await ExecuteJsonAsync<CloseResponse>(
                        client,
                        $"lab-isaac session close --protocol {ProtocolVersion} " +
                        $"--session-id {sessionId} --json",
                        TimeSpan.FromSeconds(35),
                        cleanupTimeout.Token).ConfigureAwait(false);
                    ValidateClose(response);
                }
                catch (Exception cleanupException)
                {
                    _logger.Error("Failed to close a partially opened Isaac session", cleanupException);
                }
            }
            client.Dispose();
            if (exception is DesktopClientException or OperationCanceledException)
            {
                throw;
            }

            throw new DesktopClientException(
                DesktopErrorCode.IsaacStartFailed,
                "无法启动 Isaac Sim GUI 会话。",
                exception);
        }
    }

    private ForwardedPortLocal StartForward(
        SshClient client,
        string role,
        string boundHost,
        string remoteHost,
        int remotePort)
    {
        var forwarded = new ForwardedPortLocal(
            boundHost,
            0,
            remoteHost,
            checked((uint)remotePort));
        forwarded.Exception += (_, eventArgs) =>
            _logger.Error($"Isaac {role} SSH forward failed", eventArgs.Exception);
        client.AddForwardedPort(forwarded);
        forwarded.Start();
        return forwarded;
    }

    internal static TurnLocalEndpoint ResolveTurnLocalEndpoint()
    {
        try
        {
            var endpoints = NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(network => network.GetIPProperties().UnicastAddresses.Select(address =>
                    new TurnLocalEndpoint(
                        network.Name,
                        network.NetworkInterfaceType,
                        network.OperationalStatus,
                        network.GetPhysicalAddress().GetAddressBytes().Length > 0,
                        address.Address)))
                .ToArray();
            return SelectTurnLocalEndpoint(endpoints);
        }
        catch (NetworkInformationException exception)
        {
            throw new DesktopClientException(
                DesktopErrorCode.ViewerStartFailed,
                "无法确定 TURN 转发使用的本机网络接口。",
                exception);
        }

        throw new DesktopClientException(
            DesktopErrorCode.ViewerStartFailed,
            "没有可用于 TURN 转发的本机 IPv4 网络接口。");
    }

    internal static TurnLocalEndpoint SelectTurnLocalEndpoint(
        IEnumerable<TurnLocalEndpoint> endpoints)
    {
        var selected = endpoints
            .Where(endpoint =>
                endpoint.Status == OperationalStatus.Up &&
                IsPhysicalNetworkType(endpoint.InterfaceType) &&
                endpoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(endpoint.Address) &&
                !endpoint.Address.Equals(IPAddress.Any) &&
                !endpoint.Address.Equals(IPAddress.Broadcast) &&
                !endpoint.Address.IsIPv6LinkLocal &&
                !IsIpv4LinkLocal(endpoint.Address))
            .OrderByDescending(endpoint => endpoint.HasPhysicalAddress)
            .ThenBy(endpoint => endpoint.InterfaceName, StringComparer.Ordinal)
            .FirstOrDefault();
        return selected ?? throw new DesktopClientException(
            DesktopErrorCode.ViewerStartFailed,
            "没有可用于 TURN 转发的已连接以太网或 Wi-Fi IPv4 接口；请先连接本地网络。");
    }

    private static bool IsPhysicalNetworkType(NetworkInterfaceType type) => type is
        NetworkInterfaceType.Ethernet or
        NetworkInterfaceType.FastEthernetFx or
        NetworkInterfaceType.FastEthernetT or
        NetworkInterfaceType.GigabitEthernet or
        NetworkInterfaceType.Wireless80211;

    private static bool IsIpv4LinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    private static void Validate(OpenResponse response)
    {
        if (response.Protocol != ProtocolVersion ||
            response.State != "running" ||
            response.LeaseSeconds is < 30 or > 3600)
        {
            throw new InvalidDataException("服务器返回了不支持的 Isaac 会话协议。");
        }

        if (response.SessionId.Length != 32 || response.SessionId.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("服务器返回了无效的 Isaac 会话标识。");
        }

        if (string.IsNullOrWhiteSpace(response.Version) ||
            response.Signaling.RemoteHost != "127.0.0.1" ||
            response.Signaling.RemotePort is < 1 or > 65535 ||
            response.Turn.RemotePort is < 1 or > 65535 ||
            response.Turn.Transport != "tcp" ||
            response.Turn.Policy != "relay" ||
            string.IsNullOrWhiteSpace(response.Turn.UserName) ||
            string.IsNullOrWhiteSpace(response.Turn.Credential) ||
            response.Turn.RemoteHost != "127.0.0.1" ||
            !DisplayGeometry.TryParse(
                $"{response.Resolution.Width}x{response.Resolution.Height}",
                out _))
        {
            throw new InvalidDataException("服务器返回了无效的 Isaac 会话参数。");
        }
    }

    private static void ValidateClose(CloseResponse response)
    {
        if (response.Protocol != ProtocolVersion || response.State != "idle")
        {
            throw new InvalidDataException("服务器未确认 Isaac 会话已经关闭。");
        }
    }

    private static async Task<T> ExecuteJsonAsync<T>(
        SshClient client,
        string commandText,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = timeout;
        await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (command.ExitStatus is not 0)
        {
            var detail = string.IsNullOrWhiteSpace(command.Error)
                ? $"远程命令退出，状态码 {command.ExitStatus}。"
                : command.Error.Trim();
            var code = detail.Contains("not installed", StringComparison.OrdinalIgnoreCase) ||
                       command.ExitStatus is 127
                ? DesktopErrorCode.IsaacNotInstalled
                : DesktopErrorCode.IsaacStartFailed;
            throw new DesktopClientException(code, detail);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(command.Result)
                ?? throw new JsonException("空响应");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("服务器返回了无效的 Isaac 会话响应。", exception);
        }
    }

    private static void StopSafely(ForwardedPortLocal? forwarded)
    {
        if (forwarded is null)
        {
            return;
        }

        try
        {
            if (forwarded.IsStarted)
            {
                forwarded.Stop();
            }
        }
        finally
        {
            forwarded.Dispose();
        }
    }

    private sealed class SshIsaacSession : IIsaacSession
    {
        private readonly SshClient _client;
        private readonly ForwardedPortLocal _signaling;
        private readonly ForwardedPortLocal _turn;
        private readonly LocalOnlyTcpRelay _turnRelay;
        private readonly FileLogger _logger;
        private readonly CancellationTokenSource _lifetime;
        private int _disposed;

        public SshIsaacSession(
            SshClient client,
            ForwardedPortLocal signaling,
            ForwardedPortLocal turn,
            LocalOnlyTcpRelay turnRelay,
            IsaacSessionDescriptor descriptor,
            int leaseSeconds,
            FileLogger logger,
            CancellationToken cancellationToken)
        {
            _client = client;
            _signaling = signaling;
            _turn = turn;
            _turnRelay = turnRelay;
            Descriptor = descriptor;
            _logger = logger;
            RenewInterval = TimeSpan.FromSeconds(Math.Clamp(leaseSeconds / 3, 5, 30));
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Completion = MonitorAsync(_lifetime.Token);
        }

        public IsaacSessionDescriptor Descriptor { get; }

        private TimeSpan RenewInterval { get; }

        public Task Completion { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _lifetime.Cancel();
            try
            {
                await Completion.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the Viewer closes or the user disconnects.
            }
            catch (Exception exception)
            {
                _logger.Error("Isaac session heartbeat ended during cleanup", exception);
            }

            try
            {
                await _turnRelay.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.Error("Failed to close the local Isaac TURN relay", exception);
            }
            StopSafely(_turn);
            StopSafely(_signaling);

            try
            {
                if (_client.IsConnected)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var response = await ExecuteJsonAsync<CloseResponse>(
                        _client,
                        $"lab-isaac session close --protocol {ProtocolVersion} " +
                        $"--session-id {Descriptor.SessionId} --json",
                        TimeSpan.FromSeconds(35),
                        timeout.Token).ConfigureAwait(false);
                    ValidateClose(response);
                }
            }
            catch (Exception exception)
            {
                _logger.Error("Isaac session close was not acknowledged", exception);
            }
            finally
            {
                if (_client.IsConnected)
                {
                    _client.Disconnect();
                }

                _client.Dispose();
                _lifetime.Dispose();
            }
        }

        private async Task MonitorAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(RenewInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var response = await ExecuteJsonAsync<RenewResponse>(
                    _client,
                    $"lab-isaac session renew --protocol {ProtocolVersion} " +
                    $"--session-id {Descriptor.SessionId} --json",
                    TimeSpan.FromSeconds(15),
                    cancellationToken).ConfigureAwait(false);
                if (response.Protocol != ProtocolVersion ||
                    response.State != "running" ||
                    response.SessionId != Descriptor.SessionId)
                {
                    throw new DesktopClientException(
                        DesktopErrorCode.ConnectionLost,
                        "Isaac Sim GUI 会话续租响应无效。");
                }
            }
        }
    }

    private sealed record OpenResponse
    {
        [JsonPropertyName("protocol")]
        public int Protocol { get; init; }

        [JsonPropertyName("state")]
        public string State { get; init; } = string.Empty;

        [JsonPropertyName("session_id")]
        public string SessionId { get; init; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("lease_seconds")]
        public int LeaseSeconds { get; init; }

        [JsonPropertyName("signaling")]
        public EndpointResponse Signaling { get; init; } = new();

        [JsonPropertyName("turn")]
        public TurnResponse Turn { get; init; } = new();

        [JsonPropertyName("resolution")]
        public ResolutionResponse Resolution { get; init; } = new();
    }

    private record EndpointResponse
    {
        [JsonPropertyName("remote_host")]
        public string RemoteHost { get; init; } = string.Empty;

        [JsonPropertyName("remote_port")]
        public int RemotePort { get; init; }
    }

    private sealed record TurnResponse : EndpointResponse
    {
        [JsonPropertyName("username")]
        public string UserName { get; init; } = string.Empty;

        [JsonPropertyName("credential")]
        public string Credential { get; init; } = string.Empty;

        [JsonPropertyName("transport")]
        public string Transport { get; init; } = string.Empty;

        [JsonPropertyName("policy")]
        public string Policy { get; init; } = string.Empty;
    }

    private sealed record ResolutionResponse
    {
        [JsonPropertyName("width")]
        public int Width { get; init; }

        [JsonPropertyName("height")]
        public int Height { get; init; }
    }

    private sealed record RenewResponse
    {
        [JsonPropertyName("protocol")]
        public int Protocol { get; init; }

        [JsonPropertyName("state")]
        public string State { get; init; } = string.Empty;

        [JsonPropertyName("session_id")]
        public string SessionId { get; init; } = string.Empty;
    }

    private sealed record CloseResponse
    {
        [JsonPropertyName("protocol")]
        public int Protocol { get; init; }

        [JsonPropertyName("state")]
        public string State { get; init; } = string.Empty;
    }

    internal sealed record TurnLocalEndpoint(
        string InterfaceName,
        NetworkInterfaceType InterfaceType,
        OperationalStatus Status,
        bool HasPhysicalAddress,
        IPAddress Address);
}
