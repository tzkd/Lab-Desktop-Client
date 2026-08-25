namespace LabDesktop.Client.Core;

public enum ConnectionPhase
{
    Idle,
    Validating,
    Connecting,
    StartingDesktop,
    StartingIsaac,
    LaunchingViewer,
    Connected,
    Disconnecting
}

public sealed record ConnectionProgress(ConnectionPhase Phase, string Message);

public enum DesktopErrorCode
{
    InvalidConfiguration,
    AlreadyRunning,
    AuthenticationFailed,
    HostKeyRejected,
    ServerUnavailable,
    DesktopStartFailed,
    IsaacNotInstalled,
    IsaacStartFailed,
    WebViewRuntimeNotFound,
    ViewerNotFound,
    ViewerStartFailed,
    ConnectionLost
}

public sealed class DesktopClientException : Exception
{
    public DesktopClientException(DesktopErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public DesktopClientException(DesktopErrorCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public DesktopErrorCode Code { get; }
}

public sealed record HostKeyIdentity(
    string RouteIdentity,
    string Algorithm,
    string Sha256Fingerprint);

public interface IHostKeyVerifier
{
    bool Verify(HostKeyIdentity identity);
}

public interface IDesktopTunnelFactory
{
    Task<IDesktopTunnel> OpenAsync(
        ConnectionRequest request,
        IHostKeyVerifier hostKeyVerifier,
        IProgress<ConnectionProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IDesktopTunnel : IAsyncDisposable
{
    ushort LocalPort { get; }

    Task Completion { get; }
}

public interface IViewerLauncher
{
    string? FindViewer(string? configuredPath);

    Task<IViewerSession> LaunchAsync(
        string? configuredPath,
        ushort localPort,
        CancellationToken cancellationToken);
}

public interface IViewerSession : IAsyncDisposable
{
    Task Completion { get; }
}

public sealed record IsaacSessionDescriptor(
    string SessionId,
    string Version,
    ushort SignalingPort,
    string TurnHost,
    ushort TurnPort,
    string TurnUserName,
    string TurnCredential,
    DisplayGeometry Resolution);

public interface IIsaacSessionFactory
{
    Task<IIsaacSession> OpenAsync(
        ConnectionRequest request,
        IHostKeyVerifier hostKeyVerifier,
        IProgress<ConnectionProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IIsaacSession : IAsyncDisposable
{
    IsaacSessionDescriptor Descriptor { get; }

    Task Completion { get; }
}

public interface IIsaacViewerLauncher
{
    Task<IViewerSession> LaunchAsync(
        IsaacSessionDescriptor descriptor,
        CancellationToken cancellationToken);
}

public interface IConnectionWorkflow
{
    ConnectionMode Mode { get; }

    Task RunAsync(
        ConnectionRequest request,
        IHostKeyVerifier hostKeyVerifier,
        IProgress<ConnectionProgress>? progress,
        CancellationToken cancellationToken);
}
