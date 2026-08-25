using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LabDesktop.Client.App.Diagnostics;

namespace LabDesktop.Client.App.Infrastructure;

/// <summary>
/// Exposes a non-loopback endpoint for WebRTC but forwards connections only
/// when their source address belongs to this computer.
/// </summary>
internal sealed class LocalOnlyTcpRelay : IAsyncDisposable
{
    private const int MaximumConnections = 16;
    private readonly TcpListener _listener;
    private readonly IPEndPoint _destination;
    private readonly HashSet<IPAddress> _localAddresses;
    private readonly FileLogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _connectionSlots = new(MaximumConnections, MaximumConnections);
    private readonly ConcurrentDictionary<long, TcpClient> _clients = new();
    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private readonly Task _acceptLoop;
    private long _nextConnectionId;
    private int _disposed;

    private LocalOnlyTcpRelay(
        IPAddress address,
        IPEndPoint destination,
        IEnumerable<IPAddress> localAddresses,
        FileLogger logger)
    {
        _destination = destination;
        _logger = logger;
        _localAddresses = localAddresses.Select(Normalize).ToHashSet();
        _localAddresses.Add(Normalize(address));
        _localAddresses.Add(IPAddress.Loopback);

        _listener = new TcpListener(address, 0);
        _listener.Server.ExclusiveAddressUse = true;
        _listener.Start(MaximumConnections);
        BoundAddress = address;
        BoundPort = checked((ushort)((IPEndPoint)_listener.LocalEndpoint).Port);
        _acceptLoop = AcceptLoopAsync(_lifetime.Token);
    }

    public IPAddress BoundAddress { get; }

    public ushort BoundPort { get; }

    public static LocalOnlyTcpRelay Start(
        IPAddress address,
        ushort destinationPort,
        FileLogger logger)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(logger);
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
        {
            throw new ArgumentException("A non-loopback IPv4 address is required.", nameof(address));
        }

        return new LocalOnlyTcpRelay(
            address,
            new IPEndPoint(IPAddress.Loopback, destinationPort),
            GetLocalIPv4Addresses(),
            logger);
    }

    internal static bool IsLocalSource(IPAddress source, IEnumerable<IPAddress> localAddresses)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(localAddresses);
        var normalized = Normalize(source);
        return IPAddress.IsLoopback(normalized) ||
            localAddresses.Select(Normalize).Contains(normalized);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _listener.Stop();
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        await IgnoreExpectedShutdownAsync(_acceptLoop).ConfigureAwait(false);
        await IgnoreExpectedShutdownAsync(Task.WhenAll(_connections.Values)).ConfigureAwait(false);
        _connectionSlots.Dispose();
        _lifetime.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient source;
            try
            {
                source = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var remote = source.Client.RemoteEndPoint as IPEndPoint;
            if (remote is null || !IsLocalSource(remote.Address, _localAddresses) ||
                !_connectionSlots.Wait(0, CancellationToken.None))
            {
                source.Dispose();
                continue;
            }

            var id = Interlocked.Increment(ref _nextConnectionId);
            _clients[id] = source;
            var connection = RelayAsync(id, source, cancellationToken);
            _connections[id] = connection;
            _ = connection.ContinueWith(
                completed =>
                {
                    _connections.TryRemove(id, out _);
                    _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task RelayAsync(long id, TcpClient source, CancellationToken cancellationToken)
    {
        using (source)
        using (var destination = new TcpClient(AddressFamily.InterNetwork))
        {
            try
            {
                await destination.ConnectAsync(_destination, cancellationToken).ConfigureAwait(false);
                using var sourceStream = source.GetStream();
                using var destinationStream = destination.GetStream();
                var upload = sourceStream.CopyToAsync(destinationStream, cancellationToken);
                var download = destinationStream.CopyToAsync(sourceStream, cancellationToken);
                await Task.WhenAny(upload, download).ConfigureAwait(false);
                source.Dispose();
                destination.Dispose();
                await IgnoreExpectedShutdownAsync(Task.WhenAll(upload, download)).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or SocketException or OperationCanceledException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _logger.Info($"Local Isaac TURN relay connection ended: {exception.GetType().Name}");
                }
            }
            finally
            {
                _clients.TryRemove(id, out _);
                _connectionSlots.Release();
            }
        }
    }

    private static IEnumerable<IPAddress> GetLocalIPv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Distinct();

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static async Task IgnoreExpectedShutdownAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Expected while the listener and active sockets are being disposed.
        }
    }
}
