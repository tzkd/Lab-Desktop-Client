namespace LabDesktop.Client.Core;

public sealed class ConnectionCoordinator : IAsyncDisposable
{
    private readonly Dictionary<ConnectionMode, IConnectionWorkflow> _workflows;
    private readonly SemaphoreSlim _singleSession = new(1, 1);
    private readonly object _stateLock = new();
    private CancellationTokenSource? _activeSession;
    private bool _disposed;

    public ConnectionCoordinator(IEnumerable<IConnectionWorkflow> workflows)
    {
        ArgumentNullException.ThrowIfNull(workflows);
        var values = workflows.ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException("至少需要注册一个连接工作流。", nameof(workflows));
        }

        _workflows = values.ToDictionary(item => item.Mode);
        if (_workflows.Count != values.Length)
        {
            throw new ArgumentException("每种连接模式只能注册一个工作流。", nameof(workflows));
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _activeSession is not null;
            }
        }
    }

    public async Task RunAsync(
        ConnectionRequest request,
        IHostKeyVerifier hostKeyVerifier,
        IProgress<ConnectionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await _singleSession.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new DesktopClientException(
                DesktopErrorCode.AlreadyRunning,
                "已有一个远程连接正在运行。");
        }

        using var sessionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_stateLock)
        {
            _activeSession = sessionCancellation;
        }

        try
        {
            progress?.Report(new ConnectionProgress(ConnectionPhase.Validating, "正在检查连接配置…"));
            request.EnsureValid();
            if (!_workflows.TryGetValue(request.Profile.Mode, out var workflow))
            {
                throw new DesktopClientException(
                    DesktopErrorCode.InvalidConfiguration,
                    $"客户端不支持连接模式：{request.Profile.Mode}。");
            }

            await workflow.RunAsync(
                request,
                hostKeyVerifier,
                progress,
                sessionCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            progress?.Report(new ConnectionProgress(ConnectionPhase.Disconnecting, "正在清理连接…"));
            lock (_stateLock)
            {
                if (ReferenceEquals(_activeSession, sessionCancellation))
                {
                    _activeSession = null;
                }
            }

            _singleSession.Release();
            progress?.Report(new ConnectionProgress(ConnectionPhase.Idle, "未连接"));
        }
    }

    public void Disconnect()
    {
        lock (_stateLock)
        {
            _activeSession?.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Disconnect();
        await _singleSession.WaitAsync().ConfigureAwait(false);
        _singleSession.Release();
        _singleSession.Dispose();
    }
}

public sealed class DesktopConnectionWorkflow : IConnectionWorkflow
{
    private readonly IDesktopTunnelFactory _tunnelFactory;
    private readonly IViewerLauncher _viewerLauncher;

    public DesktopConnectionWorkflow(
        IDesktopTunnelFactory tunnelFactory,
        IViewerLauncher viewerLauncher)
    {
        _tunnelFactory = tunnelFactory;
        _viewerLauncher = viewerLauncher;
    }

    public ConnectionMode Mode => ConnectionMode.LinuxDesktop;

    public async Task RunAsync(
        ConnectionRequest request,
        IHostKeyVerifier hostKeyVerifier,
        IProgress<ConnectionProgress>? progress,
        CancellationToken cancellationToken)
    {
        IDesktopTunnel? tunnel = null;
        IViewerSession? viewer = null;
        try
        {
            progress?.Report(new ConnectionProgress(ConnectionPhase.Connecting, "正在建立安全 SSH 连接…"));
            tunnel = await _tunnelFactory.OpenAsync(
                request,
                hostKeyVerifier,
                progress,
                cancellationToken).ConfigureAwait(false);

            progress?.Report(new ConnectionProgress(ConnectionPhase.LaunchingViewer, "正在启动 TurboVNC Viewer…"));
            viewer = await _viewerLauncher.LaunchAsync(
                request.Profile.ViewerPath,
                tunnel.LocalPort,
                cancellationToken).ConfigureAwait(false);

            progress?.Report(new ConnectionProgress(ConnectionPhase.Connected, "桌面已连接。关闭 Viewer 即可断开。"));
            await WaitForViewerOrTransportAsync(viewer, tunnel.Completion).ConfigureAwait(false);
        }
        finally
        {
            await DisposeSafelyAsync(viewer).ConfigureAwait(false);
            await DisposeSafelyAsync(tunnel).ConfigureAwait(false);
        }
    }

    internal static async Task WaitForViewerOrTransportAsync(
        IViewerSession viewer,
        Task transportCompletion)
    {
        var completed = await Task.WhenAny(viewer.Completion, transportCompletion).ConfigureAwait(false);
        if (completed == transportCompletion)
        {
            await transportCompletion.ConfigureAwait(false);
            if (viewer.Completion.IsCompleted)
            {
                await viewer.Completion.ConfigureAwait(false);
                return;
            }

            throw new DesktopClientException(
                DesktopErrorCode.ConnectionLost,
                "SSH 远程连接已意外中断。");
        }

        await viewer.Completion.ConfigureAwait(false);
        if (transportCompletion.IsCompleted)
        {
            await transportCompletion.ConfigureAwait(false);
        }
    }

    internal static async ValueTask DisposeSafelyAsync(IAsyncDisposable? disposable)
    {
        if (disposable is null)
        {
            return;
        }

        try
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Cleanup must not hide the original connection error.
        }
    }
}

public sealed class IsaacConnectionWorkflow : IConnectionWorkflow
{
    private readonly IIsaacSessionFactory _sessionFactory;
    private readonly IIsaacViewerLauncher _viewerLauncher;

    public IsaacConnectionWorkflow(
        IIsaacSessionFactory sessionFactory,
        IIsaacViewerLauncher viewerLauncher)
    {
        _sessionFactory = sessionFactory;
        _viewerLauncher = viewerLauncher;
    }

    public ConnectionMode Mode => ConnectionMode.IsaacSim;

    public async Task RunAsync(
        ConnectionRequest request,
        IHostKeyVerifier hostKeyVerifier,
        IProgress<ConnectionProgress>? progress,
        CancellationToken cancellationToken)
    {
        IIsaacSession? session = null;
        IViewerSession? viewer = null;
        try
        {
            progress?.Report(new ConnectionProgress(ConnectionPhase.Connecting, "正在建立安全 SSH 连接…"));
            session = await _sessionFactory.OpenAsync(
                request,
                hostKeyVerifier,
                progress,
                cancellationToken).ConfigureAwait(false);

            progress?.Report(new ConnectionProgress(ConnectionPhase.LaunchingViewer, "正在打开 Isaac Sim GUI…"));
            viewer = await _viewerLauncher.LaunchAsync(
                session.Descriptor,
                cancellationToken).ConfigureAwait(false);

            progress?.Report(new ConnectionProgress(ConnectionPhase.Connected, "Isaac Sim GUI 已连接。关闭窗口即可断开。"));
            await DesktopConnectionWorkflow.WaitForViewerOrTransportAsync(
                viewer,
                session.Completion).ConfigureAwait(false);
        }
        finally
        {
            await DesktopConnectionWorkflow.DisposeSafelyAsync(viewer).ConfigureAwait(false);
            await DesktopConnectionWorkflow.DisposeSafelyAsync(session).ConfigureAwait(false);
        }
    }
}
