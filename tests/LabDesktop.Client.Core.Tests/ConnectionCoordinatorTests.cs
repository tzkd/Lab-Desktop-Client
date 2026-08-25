using LabDesktop.Client.Core;

namespace LabDesktop.Client.Core.Tests;

public sealed class ConnectionCoordinatorTests
{
    [Fact]
    public void RequiresAtLeastOneWorkflow()
    {
        Assert.Throws<ArgumentException>(() => new ConnectionCoordinator([]));
    }

    [Fact]
    public async Task ViewerExitClosesTunnelAndReturnsToIdle()
    {
        var tunnel = new FakeTunnel();
        var viewer = new FakeViewer();
        var coordinator = CreateDesktopCoordinator(tunnel, viewer);
        var phases = new List<ConnectionPhase>();
        var progress = new InlineProgress(value => phases.Add(value.Phase));

        var run = coordinator.RunAsync(CreateRequest(), new AcceptAllHostKeys(), progress);
        await viewer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(coordinator.IsRunning);

        viewer.Exit();
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewer.Disposed);
        Assert.True(tunnel.Disposed);
        Assert.False(coordinator.IsRunning);
        Assert.Contains(ConnectionPhase.Connected, phases);
        Assert.Equal(ConnectionPhase.Idle, phases[^1]);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task UnexpectedTunnelExitIsReportedAndViewerIsClosed()
    {
        var tunnel = new FakeTunnel();
        var viewer = new FakeViewer();
        var coordinator = CreateDesktopCoordinator(tunnel, viewer);

        var run = coordinator.RunAsync(CreateRequest(), new AcceptAllHostKeys());
        await viewer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        tunnel.Exit();

        var error = await Assert.ThrowsAsync<DesktopClientException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(DesktopErrorCode.ConnectionLost, error.Code);
        Assert.True(viewer.Disposed);
        Assert.True(tunnel.Disposed);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task SimultaneousViewerExitDoesNotHideTransportFailure()
    {
        var viewer = new FakeViewer();
        viewer.Exit();
        var failure = new DesktopClientException(
            DesktopErrorCode.ConnectionLost,
            "transport failed");
        var transport = Task.FromException(failure);

        var error = await Assert.ThrowsAsync<DesktopClientException>(
            () => DesktopConnectionWorkflow.WaitForViewerOrTransportAsync(viewer, transport));

        Assert.Same(failure, error);
    }

    [Fact]
    public async Task ConcurrentRunIsRejected()
    {
        var tunnel = new FakeTunnel();
        var viewer = new FakeViewer();
        var coordinator = CreateDesktopCoordinator(tunnel, viewer);

        var first = coordinator.RunAsync(CreateRequest(), new AcceptAllHostKeys());
        await viewer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var error = await Assert.ThrowsAsync<DesktopClientException>(
            () => coordinator.RunAsync(CreateRequest(), new AcceptAllHostKeys()));
        Assert.Equal(DesktopErrorCode.AlreadyRunning, error.Code);

        viewer.Exit();
        await first.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task IsaacViewerExitClosesUserSession()
    {
        var session = new FakeIsaacSession();
        var viewer = new FakeViewer();
        var coordinator = new ConnectionCoordinator(
        [
            new IsaacConnectionWorkflow(
                new FakeIsaacSessionFactory(session),
                new FakeIsaacViewerLauncher(viewer))
        ]);
        var request = new ConnectionRequest(
            new ConnectionProfile
            {
                Mode = ConnectionMode.IsaacSim,
                Host = "gateway.example.test",
                Port = 2222,
                UserName = "abc"
            },
            "password");

        var run = coordinator.RunAsync(request, new AcceptAllHostKeys());
        await viewer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewer.Exit();
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(session.Disposed);
        Assert.True(viewer.Disposed);
        await coordinator.DisposeAsync();
    }

    private static ConnectionRequest CreateRequest() => new(
        new ConnectionProfile
        {
            Host = "gateway.example.test",
            Port = 2222,
            UserName = "abc"
        },
        "password");

    private static ConnectionCoordinator CreateDesktopCoordinator(
        FakeTunnel tunnel,
        FakeViewer viewer) => new(
        [
            new DesktopConnectionWorkflow(
                new FakeTunnelFactory(tunnel),
                new FakeViewerLauncher(viewer))
        ]);

    private sealed class AcceptAllHostKeys : IHostKeyVerifier
    {
        public bool Verify(HostKeyIdentity identity) => true;
    }

    private sealed class FakeTunnelFactory(IDesktopTunnel tunnel) : IDesktopTunnelFactory
    {
        public Task<IDesktopTunnel> OpenAsync(
            ConnectionRequest request,
            IHostKeyVerifier hostKeyVerifier,
            IProgress<ConnectionProgress>? progress,
            CancellationToken cancellationToken) => Task.FromResult(tunnel);
    }

    private sealed class FakeViewerLauncher(FakeViewer viewer) : IViewerLauncher
    {
        public string? FindViewer(string? configuredPath) => "viewer";

        public Task<IViewerSession> LaunchAsync(
            string? configuredPath,
            ushort localPort,
            CancellationToken cancellationToken)
        {
            viewer.Started.TrySetResult();
            return Task.FromResult<IViewerSession>(viewer);
        }
    }

    private sealed class FakeTunnel : IDesktopTunnel
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ushort LocalPort => 49152;

        public Task Completion => _completion.Task;

        public bool Disposed { get; private set; }

        public void Exit() => _completion.TrySetResult();

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _completion.TrySetCanceled();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeViewer : IViewerSession
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public bool Disposed { get; private set; }

        public void Exit() => _completion.TrySetResult();

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _completion.TrySetCanceled();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeIsaacSessionFactory(FakeIsaacSession session) : IIsaacSessionFactory
    {
        public Task<IIsaacSession> OpenAsync(
            ConnectionRequest request,
            IHostKeyVerifier hostKeyVerifier,
            IProgress<ConnectionProgress>? progress,
            CancellationToken cancellationToken) => Task.FromResult<IIsaacSession>(session);
    }

    private sealed class FakeIsaacViewerLauncher(FakeViewer viewer) : IIsaacViewerLauncher
    {
        public Task<IViewerSession> LaunchAsync(
            IsaacSessionDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            viewer.Started.TrySetResult();
            return Task.FromResult<IViewerSession>(viewer);
        }
    }

    private sealed class FakeIsaacSession : IIsaacSession
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IsaacSessionDescriptor Descriptor { get; } = new(
            "0123456789abcdef0123456789abcdef",
            "6.0.1.0",
            49152,
            "192.0.2.1",
            49153,
            "temporary-user",
            "temporary-credential",
            new DisplayGeometry(1920, 1080));

        public Task Completion => _completion.Task;

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _completion.TrySetCanceled();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class InlineProgress(Action<ConnectionProgress> callback)
        : IProgress<ConnectionProgress>
    {
        public void Report(ConnectionProgress value) => callback(value);
    }
}
