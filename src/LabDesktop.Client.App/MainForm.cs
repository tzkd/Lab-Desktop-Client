using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using LabDesktop.Client.App.Configuration;
using LabDesktop.Client.App.Controls;
using LabDesktop.Client.App.Diagnostics;
using LabDesktop.Client.App.Infrastructure;
using LabDesktop.Client.App.Security;
using LabDesktop.Client.Core;

namespace LabDesktop.Client.App;

internal sealed class MainForm : Form
{
    private readonly ClientSettings _settings;
    private readonly JsonSettingsStore _settingsStore;
    private readonly IViewerLauncher _viewerLauncher;
    private readonly ISshCredentialStore _credentialStore;
    private readonly AppPaths _paths;
    private readonly FileLogger _logger;
    private readonly TurboVncInstallerService _viewerInstaller;
    private readonly PersistentHostKeyVerifier _hostKeyVerifier;
    private readonly ConnectionCoordinator _coordinator;

    private readonly ComboBox _modeCombo = new();
    private readonly TextBox _hostText = new();
    private readonly TextBox _portText = new();
    private readonly TextBox _userNameText = new();
    private readonly TextBox _passwordText = new();
    private readonly PasswordRevealButton _passwordRevealButton = new();
    private readonly ComboBox _geometryCombo = new();
    private readonly TextBox _viewerPathText = new();
    private readonly Label _viewerStatus = new();
    private readonly Label _statusLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Button _connectButton = new();
    private readonly Button _disconnectButton = new();
    private readonly Button _forgetHostButton = new();
    private readonly Button _viewerBrowseButton = new();
    private readonly Button _viewerInstallButton = new();
    private readonly Button _webViewInstallButton = new();
    private readonly Button _webViewRefreshButton = new();
    private readonly Label _webViewStatus = new();
    private readonly CheckBox _rememberCredentialCheck = new();
    private GroupBox _viewerPanel = null!;
    private GroupBox _isaacPanel = null!;

    private Task? _connectionTask;
    private bool _closing;
    private bool _allowClose;
    private bool _hostKeyDecisionShown;
    private bool _suppressCredentialEvents;

    public MainForm(
        SettingsLoadResult settingsLoad,
        JsonSettingsStore settingsStore,
        IEnumerable<IConnectionWorkflow> workflows,
        IViewerLauncher viewerLauncher,
        ISshCredentialStore credentialStore,
        AppPaths paths,
        FileLogger logger)
    {
        _settings = settingsLoad.Settings;
        _settingsStore = settingsStore;
        _viewerLauncher = viewerLauncher;
        _credentialStore = credentialStore;
        _paths = paths;
        _logger = logger;
        _viewerInstaller = new TurboVncInstallerService(logger);
        _hostKeyVerifier = new PersistentHostKeyVerifier(
            _settings,
            _settingsStore,
            ConfirmHostKey,
            _logger);
        _coordinator = new ConnectionCoordinator(workflows);

        ConfigureWindow();
        BuildInterface();
        LoadProfile();

        Shown += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(settingsLoad.Warning))
            {
                MessageBox.Show(
                    this,
                    settingsLoad.Warning,
                    "设置已恢复",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };
        FormClosing += HandleFormClosing;
    }

    private void ConfigureWindow()
    {
        Icon = LoadApplicationIcon();
        Text = "实验室远程桌面";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(620, 610);
        ClientSize = new Size(640, 650);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(246, 248, 251);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private static Icon LoadApplicationIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(
            "LabDesktop.Client.App.Assets.LabConnect.ico")
            ?? throw new InvalidOperationException("应用程序图标资源缺失。");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24, 18, 24, 18),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 300));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateConnectionPanel(), 0, 1);
        var applicationPanel = new Panel { Dock = DockStyle.Fill };
        _viewerPanel = CreateViewerPanel();
        _isaacPanel = CreateIsaacPanel();
        applicationPanel.Controls.Add(_isaacPanel);
        applicationPanel.Controls.Add(_viewerPanel);
        root.Controls.Add(applicationPanel, 0, 2);
        root.Controls.Add(CreateStatusPanel(), 0, 3);
        root.Controls.Add(CreateActionsPanel(), 0, 4);
        Controls.Add(root);
    }

    private Panel CreateHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "实验室远程桌面",
            AutoSize = true,
            Location = new Point(0, 0),
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(31, 41, 55)
        });
        return panel;
    }

    private GroupBox CreateConnectionPanel()
    {
        var group = CreateGroup("连接信息");
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 6,
            Padding = new Padding(12, 8, 12, 10)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        for (var row = 0; row < 6; row++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        }

        _modeCombo.Dock = DockStyle.Fill;
        _modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeCombo.AccessibleName = "连接模式";
        _modeCombo.Items.AddRange(
        [
            new ModeChoice(ConnectionMode.LinuxDesktop, "Linux 桌面"),
            new ModeChoice(ConnectionMode.IsaacSim, "Isaac Sim GUI")
        ]);
        _modeCombo.SelectedIndexChanged += (_, _) => UpdateModeUi();

        _hostText.Dock = DockStyle.Fill;
        _hostText.AccessibleName = "服务器地址";
        _portText.Dock = DockStyle.Fill;
        _portText.MaxLength = 5;
        _portText.AccessibleName = "SSH 端口";
        _userNameText.Dock = DockStyle.Fill;
        _userNameText.CharacterCasing = CharacterCasing.Lower;
        _userNameText.AccessibleName = "用户名";
        var passwordEditor = CreatePasswordEditor();

        _geometryCombo.Dock = DockStyle.Fill;
        _geometryCombo.DropDownStyle = ComboBoxStyle.DropDown;
        _geometryCombo.Items.AddRange(
            [ConnectionProfile.DefaultGeometry, "2560x1440", "1600x900", "1366x768"]);
        _geometryCombo.AccessibleName = "桌面分辨率";

        AddField(table, 0, "连接模式", _modeCombo);
        AddField(table, 1, "服务器", _hostText);
        AddField(table, 2, "SSH 端口", _portText);
        AddField(table, 3, "用户名", _userNameText);
        AddField(table, 4, "密码", passwordEditor);
        AddField(table, 5, "分辨率", _geometryCombo);

        _rememberCredentialCheck.Text = "记住密码";
        _rememberCredentialCheck.AutoSize = true;
        _rememberCredentialCheck.Anchor = AnchorStyles.Right;
        _rememberCredentialCheck.CheckAlign = ContentAlignment.MiddleLeft;
        _rememberCredentialCheck.TextAlign = ContentAlignment.MiddleLeft;
        _rememberCredentialCheck.Margin = new Padding(6, 0, 3, 0);
        _rememberCredentialCheck.AccessibleName = "记住 SSH 密码";
        table.Controls.Add(_rememberCredentialCheck, 2, 4);

        group.Controls.Add(table);
        return group;
    }

    private TableLayoutPanel CreatePasswordEditor()
    {
        var editor = new TableLayoutPanel
        {
            BackColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(3),
            Padding = new Padding(6, 3, 1, 3),
            RowCount = 1
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _passwordText.BorderStyle = BorderStyle.None;
        _passwordText.Dock = DockStyle.Fill;
        _passwordText.Margin = Padding.Empty;
        _passwordText.UseSystemPasswordChar = true;
        _passwordText.AccessibleName = "SSH 密码";

        _passwordRevealButton.Dock = DockStyle.Fill;
        _passwordRevealButton.PasswordVisibilityChanged += (_, _) =>
            _passwordText.UseSystemPasswordChar = !_passwordRevealButton.PasswordVisible;

        editor.Controls.Add(_passwordText, 0, 0);
        editor.Controls.Add(_passwordRevealButton, 1, 0);
        return editor;
    }

    private GroupBox CreateViewerPanel()
    {
        var group = CreateGroup("TurboVNC Viewer");
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(12, 8, 12, 8)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        _viewerPathText.Dock = DockStyle.Fill;
        _viewerPathText.ReadOnly = true;
        _viewerPathText.AccessibleName = "TurboVNC Viewer 路径";
        _viewerBrowseButton.Text = "选择…";
        _viewerBrowseButton.Dock = DockStyle.Fill;
        _viewerBrowseButton.Click += BrowseViewer;
        _viewerInstallButton.Text = "安装…";
        _viewerInstallButton.Dock = DockStyle.Fill;
        _viewerInstallButton.Click += InstallViewer;

        _viewerStatus.Dock = DockStyle.Fill;
        _viewerStatus.TextAlign = ContentAlignment.MiddleLeft;
        table.Controls.Add(_viewerPathText, 0, 0);
        table.Controls.Add(_viewerBrowseButton, 1, 0);
        table.Controls.Add(_viewerInstallButton, 2, 0);
        table.Controls.Add(_viewerStatus, 0, 1);
        table.SetColumnSpan(_viewerStatus, 3);
        group.Controls.Add(table);
        return group;
    }

    private GroupBox CreateIsaacPanel()
    {
        var group = CreateGroup("Isaac Sim GUI");
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(12, 14, 12, 14)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        _webViewStatus.Dock = DockStyle.Fill;
        _webViewStatus.TextAlign = ContentAlignment.MiddleLeft;
        _webViewStatus.AutoEllipsis = true;
        _webViewStatus.AccessibleName = "WebView2 Runtime 状态";
        _webViewInstallButton.Text = "安装…";
        _webViewInstallButton.Dock = DockStyle.Fill;
        _webViewInstallButton.AccessibleName = "安装 WebView2 Runtime";
        _webViewInstallButton.Click += InstallWebView2;
        _webViewRefreshButton.Text = "重新检测";
        _webViewRefreshButton.Dock = DockStyle.Fill;
        _webViewRefreshButton.AccessibleName = "重新检测 WebView2 Runtime";
        _webViewRefreshButton.Click += (_, _) => UpdateWebViewRuntimeStatus();

        table.Controls.Add(_webViewStatus, 0, 0);
        table.Controls.Add(_webViewInstallButton, 1, 0);
        table.Controls.Add(_webViewRefreshButton, 2, 0);
        group.Controls.Add(table);
        return group;
    }

    private TableLayoutPanel CreateStatusPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(2, 6, 2, 0),
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _statusLabel.Text = "未连接";
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.Margin = Padding.Empty;
        _statusLabel.ForeColor = Color.FromArgb(75, 85, 99);

        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 0;
        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Margin = new Padding(0, 1, 0, 1);

        _forgetHostButton.Text = "忘记当前服务器指纹";
        _forgetHostButton.AutoSize = true;
        _forgetHostButton.FlatStyle = FlatStyle.System;
        _forgetHostButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _forgetHostButton.Margin = new Padding(0, 6, 0, 0);
        _forgetHostButton.AccessibleName = "忘记当前服务器指纹";
        _forgetHostButton.Click += ForgetCurrentHost;

        var diagnosticsButton = new Button
        {
            Text = "诊断文件",
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Margin = new Padding(0, 6, 0, 0),
            AccessibleName = "打开诊断文件"
        };
        diagnosticsButton.Click += (_, _) =>
        {
            Directory.CreateDirectory(_paths.DataDirectory);
            OpenExternal(_paths.DataDirectory);
        };

        panel.Controls.Add(_statusLabel, 0, 0);
        panel.SetColumnSpan(_statusLabel, 2);
        panel.Controls.Add(_progressBar, 0, 1);
        panel.SetColumnSpan(_progressBar, 2);
        panel.Controls.Add(_forgetHostButton, 0, 2);
        panel.Controls.Add(diagnosticsButton, 1, 2);
        return panel;
    }

    private FlowLayoutPanel CreateActionsPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };

        _connectButton.Text = "连接桌面";
        _connectButton.Size = new Size(126, 38);
        _connectButton.BackColor = Color.FromArgb(37, 99, 235);
        _connectButton.ForeColor = Color.White;
        _connectButton.FlatStyle = FlatStyle.Flat;
        _connectButton.FlatAppearance.BorderSize = 0;
        _connectButton.Click += Connect;

        _disconnectButton.Text = "断开";
        _disconnectButton.Size = new Size(94, 38);
        _disconnectButton.Enabled = false;
        _disconnectButton.Click += (_, _) => _coordinator.Disconnect();

        panel.Controls.Add(_connectButton);
        panel.Controls.Add(_disconnectButton);
        AcceptButton = _connectButton;
        return panel;
    }

    private void LoadProfile()
    {
        var profile = _settings.Profile.Normalize();
        _suppressCredentialEvents = true;
        _hostText.Text = profile.Host;
        _portText.Text = profile.Port is >= 1 and <= 65535
            ? profile.Port.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        _userNameText.Text = profile.UserName;
        _geometryCombo.Text = profile.Geometry;
        _modeCombo.SelectedItem = _modeCombo.Items
            .Cast<ModeChoice>()
            .Single(item => item.Mode == profile.Mode);
        _rememberCredentialCheck.Checked = _settings.RememberCredential;
        if (_rememberCredentialCheck.Checked)
        {
            LoadCredential(profile);
        }
        _suppressCredentialEvents = false;

        var viewer = _viewerLauncher.FindViewer(profile.ViewerPath);
        _viewerPathText.Text = viewer ?? profile.ViewerPath ?? string.Empty;
        UpdateViewerStatus(viewer);
        UpdateTrustButton();
        _userNameText.TextChanged += HandleRouteIdentityChanged;
        _hostText.TextChanged += HandleRouteIdentityChanged;
        _portText.TextChanged += HandleRouteIdentityChanged;
        _rememberCredentialCheck.CheckedChanged += HandleRememberCredentialChanged;
        UpdateModeUi();
    }

    private async void Connect(object? sender, EventArgs eventArgs)
    {
        if (_connectionTask is not null)
        {
            return;
        }

        var profile = BuildProfile();
        var validationErrors = profile.Validate();
        if (validationErrors.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, validationErrors),
                "连接配置无效",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (_passwordText.TextLength == 0)
        {
            MessageBox.Show(this, "请输入 SSH 密码。", "需要密码", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _passwordText.Focus();
            return;
        }

        var previousRoute = _settings.Profile.Normalize().RouteIdentity;
        var password = _passwordText.Text;
        _settings.Profile = profile;
        _settings.RememberCredential = _rememberCredentialCheck.Checked;
        _settingsStore.Save(_settings);
        var request = new ConnectionRequest(profile, password);
        var credentialPersisted = false;
        var progress = new Progress<ConnectionProgress>(progressUpdate =>
        {
            UpdateProgress(progressUpdate);
            if (!credentialPersisted && progressUpdate.Phase == ConnectionPhase.Connected)
            {
                credentialPersisted = true;
                PersistCredential(profile, password, previousRoute);
            }
        });
        SetBusy(true);
        _hostKeyDecisionShown = false;
        _logger.Info($"Connecting: {profile.RouteIdentity}");

        try
        {
            _connectionTask = _coordinator.RunAsync(request, _hostKeyVerifier, progress);
            await _connectionTask;
            _logger.Info($"Connection closed: {profile.RouteIdentity} mode={profile.Mode}");
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"Connection cancelled: {profile.RouteIdentity}");
        }
        catch (DesktopClientException exception)
        {
            _logger.Error($"Connection failed ({exception.Code}): {profile.RouteIdentity}", exception);
            if (!_closing &&
                !(exception.Code == DesktopErrorCode.HostKeyRejected && _hostKeyDecisionShown))
            {
                if (exception.Code == DesktopErrorCode.WebViewRuntimeNotFound)
                {
                    var openDownload = MessageBox.Show(
                        this,
                        exception.Message + "\n\n是否打开微软官方 WebView2 Runtime 安装页？",
                        "缺少 WebView2 Runtime",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Error,
                        MessageBoxDefaultButton.Button1);
                    if (openDownload == DialogResult.Yes)
                    {
                        OpenExternal(WebView2RuntimeSupport.DownloadPageUrl);
                    }
                }
                else
                {
                    MessageBox.Show(this, exception.Message, "连接失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        catch (Exception exception)
        {
            _logger.Error($"Unexpected connection failure: {profile.RouteIdentity}", exception);
            if (!_closing)
            {
                MessageBox.Show(
                    this,
                    "连接发生未预期错误。请打开诊断文件并联系管理员。",
                    "连接失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (_rememberCredentialCheck.Checked)
            {
                LoadCredential(BuildProfile().Normalize());
            }
            else
            {
                _passwordText.Clear();
            }
            _connectionTask = null;
            SetBusy(false);
        }
    }

    private ConnectionProfile BuildProfile() => new()
    {
        Name = SelectedMode == ConnectionMode.IsaacSim ? "Isaac Sim GUI" : "实验室桌面",
        Mode = SelectedMode,
        Host = _hostText.Text,
        Port = int.TryParse(
            _portText.Text.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var port)
            ? port
            : 0,
        UserName = _userNameText.Text,
        Geometry = _geometryCombo.Text,
        ViewerPath = string.IsNullOrWhiteSpace(_viewerPathText.Text) ? null : _viewerPathText.Text
    };

    private ConnectionMode SelectedMode =>
        _modeCombo.SelectedItem is ModeChoice choice
            ? choice.Mode
            : ConnectionMode.LinuxDesktop;

    private bool ConfirmHostKey(HostKeyPrompt prompt)
    {
        if (InvokeRequired)
        {
            return (bool)Invoke(() => ConfirmHostKey(prompt));
        }

        _hostKeyDecisionShown = true;
        if (prompt.Decision == HostTrustDecision.Mismatch)
        {
            MessageBox.Show(
                this,
                $"服务器主机密钥已改变，连接已阻止。\n\n目标：{prompt.Presented.RouteIdentity}\n" +
                $"原指纹：{prompt.Existing?.Sha256Fingerprint}\n新指纹：{prompt.Presented.Sha256Fingerprint}\n\n" +
                "请先向管理员核实；确认服务器确已重建后，再点击“忘记当前服务器指纹”。",
                "安全警告",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        var result = MessageBox.Show(
            this,
            $"这是首次连接该用户路由。请核对服务器主机密钥：\n\n" +
            $"目标：{prompt.Presented.RouteIdentity}\n算法：{prompt.Presented.Algorithm}\n" +
            $"指纹：{prompt.Presented.Sha256Fingerprint}\n\n是否信任并保存该指纹？",
            "确认服务器身份",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        return result == DialogResult.Yes;
    }

    private void ForgetCurrentHost(object? sender, EventArgs eventArgs)
    {
        var profile = BuildProfile().Normalize();
        if (profile.Validate().Count > 0)
        {
            return;
        }

        var trusted = _settings.TrustedHosts.FirstOrDefault(item =>
            string.Equals(item.RouteIdentity, profile.RouteIdentity, StringComparison.OrdinalIgnoreCase));
        if (trusted is null)
        {
            MessageBox.Show(this, "当前用户路由没有已保存的主机指纹。", "主机指纹", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"仅在管理员确认服务器已重建或主机密钥已更换后执行。\n\n删除 {profile.RouteIdentity} 的已保存指纹？",
            "忘记主机指纹",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation == DialogResult.Yes)
        {
            _hostKeyVerifier.Forget(profile.RouteIdentity);
            UpdateTrustButton();
        }
    }

    private void BrowseViewer(object? sender, EventArgs eventArgs)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 TurboVNC Viewer",
            Filter = "TurboVNC Viewer|vncviewer.bat;vncviewer.exe;vncviewerw.bat|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var resolved = _viewerLauncher.FindViewer(dialog.FileName);
        _viewerPathText.Text = resolved ?? dialog.FileName;
        UpdateViewerStatus(resolved);
    }

    private async void InstallViewer(object? sender, EventArgs eventArgs)
    {
        if (_coordinator.IsRunning || !_viewerInstallButton.Enabled)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"将从 TurboVNC 官方 GitHub Release 下载并校验 TurboVNC {TurboVncInstallerService.Version} 安装器。\n\n" +
            "安装器会单独显示其许可和安装选项。是否继续？",
            "安装 TurboVNC Viewer",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        SetDependencyControls(enabled: false);
        _statusLabel.Text = "正在下载并校验官方 TurboVNC Viewer…";
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 25;
        try
        {
            await _viewerInstaller.InstallAsync();
            var viewer = _viewerLauncher.FindViewer(null);
            _viewerPathText.Text = viewer ?? string.Empty;
            UpdateViewerStatus(viewer);
            if (viewer is null)
            {
                MessageBox.Show(
                    this,
                    "安装器已结束，但客户端尚未找到 Viewer。请点击“选择…”指定 vncviewer.bat。",
                    "需要选择 Viewer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("TurboVNC installation failed", exception);
            var openRelease = MessageBox.Show(
                this,
                $"自动安装失败：{exception.Message}\n\n是否打开 TurboVNC 官方下载页？",
                "安装失败",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error);
            if (openRelease == DialogResult.Yes)
            {
                OpenExternal(TurboVncViewerLauncher.DownloadUrl);
            }
        }
        finally
        {
            SetDependencyControls(enabled: true);
            UpdateProgress(new ConnectionProgress(ConnectionPhase.Idle, "未连接"));
        }
    }

    private void InstallWebView2(object? sender, EventArgs eventArgs)
    {
        if (_coordinator.IsRunning || !_webViewInstallButton.Enabled)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "将打开微软官方 WebView2 Runtime 下载页。请选择 Evergreen Bootstrapper，" +
            "完成安装后返回客户端并点击“重新检测”。是否继续？",
            "安装 WebView2 Runtime",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1);
        if (confirmation == DialogResult.Yes)
        {
            OpenExternal(WebView2RuntimeSupport.DownloadPageUrl);
            _statusLabel.Text = "已打开微软官方 WebView2 Runtime 安装页。";
        }
    }

    private void SetDependencyControls(bool enabled)
    {
        _connectButton.Enabled = enabled && !_coordinator.IsRunning;
        _disconnectButton.Enabled = _coordinator.IsRunning;
        var desktop = SelectedMode == ConnectionMode.LinuxDesktop;
        _viewerBrowseButton.Enabled = enabled && desktop;
        _viewerInstallButton.Enabled = enabled && desktop;
        _webViewInstallButton.Enabled = enabled && !desktop;
        _webViewRefreshButton.Enabled = enabled && !desktop;
    }

    private void UpdateViewerStatus(string? viewer)
    {
        var found = !string.IsNullOrWhiteSpace(viewer);
        _viewerStatus.Text = found ? "已找到 Viewer，可直接连接。" : "未找到 Viewer；请先安装或手动选择。";
        _viewerStatus.ForeColor = found ? Color.FromArgb(22, 101, 52) : Color.FromArgb(185, 28, 28);
    }

    private void UpdateTrustButton()
    {
        var route = BuildProfile().Normalize().RouteIdentity;
        var hasTrustedHost = _settings.TrustedHosts.Any(item =>
            string.Equals(item.RouteIdentity, route, StringComparison.OrdinalIgnoreCase));
        _forgetHostButton.Visible = hasTrustedHost;
        _forgetHostButton.Enabled = !_coordinator.IsRunning && hasTrustedHost;
    }

    private void UpdateProgress(ConnectionProgress progress)
    {
        _statusLabel.Text = progress.Message;
        var active = progress.Phase is not ConnectionPhase.Idle and not ConnectionPhase.Connected;
        _progressBar.MarqueeAnimationSpeed = active ? 25 : 0;
        _progressBar.Style = active ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        if (!active)
        {
            _progressBar.Value = progress.Phase == ConnectionPhase.Connected ? 100 : 0;
        }
    }

    private void SetBusy(bool busy)
    {
        _connectButton.Enabled = !busy;
        _disconnectButton.Enabled = busy;
        _hostText.Enabled = !busy;
        _portText.Enabled = !busy;
        _userNameText.Enabled = !busy;
        _passwordText.Enabled = !busy;
        _passwordRevealButton.Enabled = !busy;
        _rememberCredentialCheck.Enabled = !busy;
        _modeCombo.Enabled = !busy;
        _geometryCombo.Enabled = !busy;
        var desktop = SelectedMode == ConnectionMode.LinuxDesktop;
        _viewerBrowseButton.Enabled = !busy && desktop;
        _viewerInstallButton.Enabled = !busy && desktop;
        _webViewInstallButton.Enabled = !busy && !desktop;
        _webViewRefreshButton.Enabled = !busy && !desktop;
        _forgetHostButton.Enabled = !busy && _forgetHostButton.Enabled;
        if (!busy)
        {
            UpdateTrustButton();
        }
    }

    private void UpdateModeUi()
    {
        if (_viewerPanel is null || _isaacPanel is null)
        {
            return;
        }

        var desktop = SelectedMode == ConnectionMode.LinuxDesktop;
        _viewerPanel.Visible = desktop;
        _isaacPanel.Visible = !desktop;
        _connectButton.Text = desktop ? "连接桌面" : "连接 Isaac GUI";
        _viewerBrowseButton.Enabled = desktop && !_coordinator.IsRunning;
        _viewerInstallButton.Enabled = desktop && !_coordinator.IsRunning;
        _webViewInstallButton.Enabled = !desktop && !_coordinator.IsRunning;
        _webViewRefreshButton.Enabled = !desktop && !_coordinator.IsRunning;
        if (!desktop)
        {
            UpdateWebViewRuntimeStatus();
        }
    }

    private void HandleRouteIdentityChanged(object? sender, EventArgs eventArgs)
    {
        UpdateTrustButton();
        if (_suppressCredentialEvents)
        {
            return;
        }

        _passwordText.Clear();
        if (_rememberCredentialCheck.Checked)
        {
            LoadCredential(BuildProfile().Normalize());
        }
    }

    private void HandleRememberCredentialChanged(object? sender, EventArgs eventArgs)
    {
        if (_suppressCredentialEvents)
        {
            return;
        }

        _settings.RememberCredential = _rememberCredentialCheck.Checked;
        if (!_rememberCredentialCheck.Checked)
        {
            var current = BuildProfile().Normalize();
            DeleteCredential(current);
            var saved = _settings.Profile.Normalize();
            if (!string.Equals(current.RouteIdentity, saved.RouteIdentity, StringComparison.OrdinalIgnoreCase))
            {
                DeleteCredential(saved);
            }
        }
        else if (_passwordText.TextLength == 0)
        {
            LoadCredential(BuildProfile().Normalize());
        }
        _settingsStore.Save(_settings);
    }

    private void PersistCredential(
        ConnectionProfile profile,
        string password,
        string previousRoute)
    {
        var normalized = profile.Normalize();
        var remember = _rememberCredentialCheck.Checked;
        try
        {
            if (remember)
            {
                _credentialStore.Write(normalized.RouteIdentity, normalized.UserName, password);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or ArgumentException)
        {
            _logger.Error("Windows credential persistence failed", exception);
            remember = false;
            _suppressCredentialEvents = true;
            _rememberCredentialCheck.Checked = false;
            _suppressCredentialEvents = false;
            MessageBox.Show(
                this,
                "无法将密码安全保存到 Windows 凭据管理器。本次仍可连接，但不会记住密码。",
                "密码未保存",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        if (remember &&
            !string.Equals(previousRoute, normalized.RouteIdentity, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _credentialStore.Delete(previousRoute);
            }
            catch (Exception exception) when (exception is Win32Exception or ArgumentException)
            {
                // A stale credential is safer than losing the newly verified one.
                _logger.Error("Previous Windows credential cleanup failed", exception);
            }
        }
        _settings.RememberCredential = remember;
        _settingsStore.Save(_settings);
    }

    private void LoadCredential(ConnectionProfile profile)
    {
        if (profile.Validate().Count > 0)
        {
            return;
        }

        try
        {
            _passwordText.Text = _credentialStore.Read(profile.RouteIdentity) ?? string.Empty;
        }
        catch (Win32Exception exception)
        {
            _logger.Error("Windows credential lookup failed", exception);
            _passwordText.Clear();
            _statusLabel.Text = "无法读取 Windows 凭据管理器；请手动输入密码。";
        }
    }

    private void DeleteCredential(ConnectionProfile profile)
    {
        if (profile.Validate().Count > 0)
        {
            return;
        }

        try
        {
            _credentialStore.Delete(profile.RouteIdentity);
        }
        catch (Win32Exception exception)
        {
            _logger.Error("Windows credential deletion failed", exception);
            MessageBox.Show(
                this,
                "无法删除 Windows 凭据管理器中的当前密码，请稍后重试。",
                "删除密码失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void UpdateWebViewRuntimeStatus()
    {
        try
        {
            var version = WebView2RuntimeSupport.GetInstalledVersion();
            var installed = version is not null;
            _webViewStatus.Text = installed
                ? $"WebView2 Runtime {version} 已安装。"
                : "未找到 WebView2 Runtime；请点击“安装…”。";
            _webViewStatus.ForeColor = installed
                ? Color.FromArgb(22, 101, 52)
                : Color.FromArgb(185, 28, 28);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or BadImageFormatException)
        {
            _logger.Error("WebView2 Runtime detection failed", exception);
            _webViewStatus.Text = "WebView2 客户端组件不完整；请重新安装本客户端。";
            _webViewStatus.ForeColor = Color.FromArgb(185, 28, 28);
        }
    }

    private async void HandleFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose || _connectionTask is null)
        {
            await _coordinator.DisposeAsync();
            return;
        }

        eventArgs.Cancel = true;
        if (_closing)
        {
            return;
        }

        _closing = true;
        SetBusy(true);
        _statusLabel.Text = "正在关闭连接…";
        _coordinator.Disconnect();
        try
        {
            await _connectionTask;
        }
        catch
        {
            // The connection handler already logs and presents actionable errors.
        }

        await _coordinator.DisposeAsync();
        _allowClose = true;
        Close();
    }

    private static GroupBox CreateGroup(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = Color.FromArgb(55, 65, 81),
        BackColor = Color.White,
        Padding = new Padding(8)
    };

    private static void AddField(TableLayoutPanel table, int row, string label, Control input)
    {
        table.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(55, 65, 81)
        }, 0, row);
        table.Controls.Add(input, 1, row);
        if (row != 4)
        {
            table.SetColumnSpan(input, 2);
        }
    }

    private sealed record ModeChoice(ConnectionMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private static void OpenExternal(string target)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }
}
