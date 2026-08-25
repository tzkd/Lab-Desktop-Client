using System.Text.Json;
using LabDesktop.Client.App.Diagnostics;
using LabDesktop.Client.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LabDesktop.Client.App.Infrastructure;

internal sealed class IsaacWebViewerLauncher : IIsaacViewerLauncher
{
    private static readonly TimeSpan ViewerStartupTimeout = TimeSpan.FromSeconds(60);
    internal const string ApplicationHost = "isaac.labdesktop.localhost";
    internal static readonly Uri ApplicationUri = new($"http://{ApplicationHost}/index.html");
    private readonly string _webRoot;
    private readonly FileLogger _logger;

    internal static bool IsApplicationUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttp &&
            uri.IsDefaultPort &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            string.Equals(uri.IdnHost, ApplicationHost, StringComparison.OrdinalIgnoreCase);
    }

    public IsaacWebViewerLauncher(FileLogger logger, string? webRoot = null)
    {
        _logger = logger;
        _webRoot = webRoot ?? Path.Combine(AppContext.BaseDirectory, "Web", "Isaac");
    }

    public async Task<IViewerSession> LaunchAsync(
        IsaacSessionDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(Path.Combine(_webRoot, "index.html")))
        {
            throw new DesktopClientException(
                DesktopErrorCode.ViewerStartFailed,
                "客户端缺少 Isaac Sim GUI 资源，请重新安装客户端。");
        }

        try
        {
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception exception) when (IsWebViewBootstrapFailure(exception))
        {
            throw MapWebViewInitializationFailure(exception);
        }

        var session = new WebViewerSession(descriptor, _webRoot, _logger);
        session.Start();
        try
        {
            await session.Ready
                .WaitAsync(ViewerStartupTimeout, cancellationToken)
                .ConfigureAwait(false);
            return session;
        }
        catch (TimeoutException exception)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw new DesktopClientException(
                DesktopErrorCode.ViewerStartFailed,
                "等待 Isaac Sim GUI 画面超时。",
                exception);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static bool IsWebViewBootstrapFailure(Exception exception) =>
        exception is WebView2RuntimeNotFoundException or DllNotFoundException or BadImageFormatException;

    internal static DesktopClientException MapWebViewInitializationFailure(Exception exception) =>
        exception switch
        {
            WebView2RuntimeNotFoundException => new DesktopClientException(
                DesktopErrorCode.WebViewRuntimeNotFound,
                "未找到 Microsoft Edge WebView2 Runtime。请先安装 WebView2 Runtime。",
                exception),
            DllNotFoundException => new DesktopClientException(
                DesktopErrorCode.ViewerStartFailed,
                "客户端缺少 WebView2Loader.dll。请重新安装客户端，或重新解压完整便携版。",
                exception),
            BadImageFormatException => new DesktopClientException(
                DesktopErrorCode.ViewerStartFailed,
                "客户端中的 WebView2Loader.dll 架构不匹配。请安装 win-x64 版本客户端。",
                exception),
            _ => new DesktopClientException(
                DesktopErrorCode.ViewerStartFailed,
                "Isaac Sim GUI WebView 初始化失败。",
                exception)
        };

    private sealed class WebViewerSession : IViewerSession
    {
        private readonly IsaacSessionDescriptor _descriptor;
        private readonly string _webRoot;
        private readonly FileLogger _logger;
        private readonly TaskCompletionSource _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IsaacViewerForm? _form;
        private int _disposed;

        public WebViewerSession(
            IsaacSessionDescriptor descriptor,
            string webRoot,
            FileLogger logger)
        {
            _descriptor = descriptor;
            _webRoot = webRoot;
            _logger = logger;
        }

        public Task Ready => _ready.Task;

        public Task Completion => _completion.Task;

        public void Start()
        {
            var thread = new Thread(RunViewer)
            {
                IsBackground = true,
                Name = "LabDesktop-IsaacViewer"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var form = Volatile.Read(ref _form);
            if (form is not null && form.IsHandleCreated && !form.IsDisposed)
            {
                try
                {
                    form.BeginInvoke(form.Close);
                }
                catch (InvalidOperationException)
                {
                    // The viewer thread has already ended.
                }
            }

            try
            {
                await Completion.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                _logger.Info("Isaac WebView ended during cleanup");
            }
        }

        private void RunViewer()
        {
            try
            {
                using var form = new IsaacViewerForm(
                    _descriptor,
                    _webRoot,
                    _ready,
                    _completion,
                    _logger);
                Volatile.Write(ref _form, form);
                Application.Run(form);
                _ready.TrySetException(new DesktopClientException(
                    DesktopErrorCode.ViewerStartFailed,
                    "Isaac Sim GUI 窗口在连接完成前已关闭。"));
                _completion.TrySetResult();
            }
            catch (Exception exception)
            {
                _logger.Error("Isaac WebView failed", exception);
                var mapped = exception is DesktopClientException
                    ? exception
                    : new DesktopClientException(
                        DesktopErrorCode.ViewerStartFailed,
                        "Isaac Sim GUI 窗口启动失败。",
                        exception);
                _ready.TrySetException(mapped);
                _completion.TrySetException(mapped);
            }
            finally
            {
                Volatile.Write(ref _form, null);
            }
        }
    }

    private sealed class IsaacViewerForm : Form
    {
        private readonly IsaacSessionDescriptor _descriptor;
        private readonly string _webRoot;
        private readonly TaskCompletionSource _ready;
        private readonly TaskCompletionSource _completion;
        private readonly FileLogger _logger;
        private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };

        public IsaacViewerForm(
            IsaacSessionDescriptor descriptor,
            string webRoot,
            TaskCompletionSource ready,
            TaskCompletionSource completion,
            FileLogger logger)
        {
            _descriptor = descriptor;
            _webRoot = webRoot;
            _ready = ready;
            _completion = completion;
            _logger = logger;
            Text = $"Isaac Sim GUI ({descriptor.Version})";
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            MinimumSize = new Size(800, 600);
            Controls.Add(_webView);
            Shown += InitializeAsync;
        }

        private async void InitializeAsync(object? sender, EventArgs eventArgs)
        {
            try
            {
                await _webView.EnsureCoreWebView2Async();
                var core = _webView.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDefaultScriptDialogsEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsWebMessageEnabled = true;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = true;
                core.SetVirtualHostNameToFolderMapping(
                    ApplicationHost,
                    _webRoot,
                    CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += (_, navigation) =>
                {
                    if (!IsApplicationUri(navigation.Uri))
                    {
                        navigation.Cancel = true;
                    }
                };
                core.NavigationCompleted += (_, navigation) =>
                {
                    if (!navigation.IsSuccess)
                    {
                        Fail(new DesktopClientException(
                            DesktopErrorCode.ViewerStartFailed,
                            $"Isaac Sim GUI 页面加载失败：{navigation.WebErrorStatus}。"));
                    }
                };
                core.ProcessFailed += (_, failure) =>
                {
                    Fail(new DesktopClientException(
                        DesktopErrorCode.ViewerStartFailed,
                        $"Isaac Sim GUI WebView 进程异常：{failure.ProcessFailedKind}。"));
                };
                core.WebMessageReceived += HandleWebMessage;
                core.Navigate(ApplicationUri.AbsoluteUri);
            }
            catch (Exception exception)
            {
                _logger.Error("Isaac WebView initialization failed", exception);
                _ready.TrySetException(MapWebViewInitializationFailure(exception));
                Close();
            }
        }

        private void HandleWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
        {
            try
            {
                if (!IsApplicationUri(eventArgs.Source))
                {
                    _logger.Info("Ignored Isaac WebView message from an unexpected origin");
                    return;
                }

                using var message = JsonDocument.Parse(eventArgs.WebMessageAsJson);
                var root = message.RootElement;
                if (!root.TryGetProperty("type", out var typeElement) ||
                    typeElement.ValueKind is not JsonValueKind.String ||
                    typeElement.GetString() != "status")
                {
                    return;
                }

                if (!root.TryGetProperty("state", out var stateElement) ||
                    stateElement.ValueKind is not JsonValueKind.String)
                {
                    throw new JsonException("Isaac WebView status is missing a state");
                }

                var state = stateElement.GetString();
                if (state == "ready")
                {
                    if (!root.TryGetProperty("secureContext", out var secureContext) ||
                        secureContext.ValueKind is not JsonValueKind.True)
                    {
                        Fail(new DesktopClientException(
                            DesktopErrorCode.ViewerStartFailed,
                            "Isaac Sim GUI 页面未运行在安全上下文中。"));
                        return;
                    }

                    _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                    {
                        type = "connect",
                        signalingPort = _descriptor.SignalingPort,
                        turnHost = _descriptor.TurnHost,
                        turnPort = _descriptor.TurnPort,
                        turnUserName = _descriptor.TurnUserName,
                        turnCredential = _descriptor.TurnCredential,
                        width = _descriptor.Resolution.Width,
                        height = _descriptor.Resolution.Height
                    }));
                }
                else if (state == "connected")
                {
                    _logger.Info($"Isaac WebView connected: version={_descriptor.Version}");
                    _ready.TrySetResult();
                }
                else if (state == "progress")
                {
                    var detail = ReadSafeDetail(root);
                    _logger.Info(string.IsNullOrWhiteSpace(detail)
                        ? "Isaac WebView connection is in progress"
                        : $"Isaac WebView progress: {detail}");
                }
                else if (state == "diagnostic")
                {
                    var detail = ReadSafeDetail(root);
                    if (!string.IsNullOrWhiteSpace(detail))
                    {
                        _logger.Info($"Isaac WebRTC diagnostic: {detail}");
                    }
                }
                else if (state == "error")
                {
                    var detail = ReadSafeDetail(root);
                    Fail(new DesktopClientException(
                        DesktopErrorCode.ViewerStartFailed,
                        string.IsNullOrWhiteSpace(detail)
                            ? "Isaac Sim GUI 连接失败。"
                            : detail));
                }
                else
                {
                    throw new JsonException("Isaac WebView returned an unsupported state");
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                Fail(new DesktopClientException(
                    DesktopErrorCode.ViewerStartFailed,
                    "Isaac Sim GUI 返回了无效状态。",
                    exception));
            }
        }

        private string? ReadSafeDetail(JsonElement root)
        {
            if (!root.TryGetProperty("detail", out var detailElement) ||
                detailElement.ValueKind is not JsonValueKind.String)
            {
                return null;
            }

            var detail = detailElement.GetString();
            if (string.IsNullOrWhiteSpace(detail))
            {
                return null;
            }

            var sanitized = detail
                .Replace(_descriptor.TurnCredential, "[credential]", StringComparison.Ordinal)
                .Replace(_descriptor.TurnUserName, "[temporary-user]", StringComparison.Ordinal)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            return sanitized[..Math.Min(sanitized.Length, 1000)];
        }

        private void Fail(Exception exception)
        {
            _logger.Error("Isaac WebView connection failed", exception);
            if (!_ready.TrySetException(exception))
            {
                _completion.TrySetException(exception);
            }

            if (!IsDisposed)
            {
                Close();
            }
        }
    }
}
