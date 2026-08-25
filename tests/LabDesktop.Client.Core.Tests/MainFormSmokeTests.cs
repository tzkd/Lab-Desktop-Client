using LabDesktop.Client.App;
using LabDesktop.Client.App.Configuration;
using LabDesktop.Client.App.Controls;
using LabDesktop.Client.App.Diagnostics;
using LabDesktop.Client.App.Infrastructure;
using LabDesktop.Client.App.Security;
using LabDesktop.Client.Core;
using System.Windows.Forms;

namespace LabDesktop.Client.Core.Tests;

public sealed class MainFormSmokeTests
{
    [Fact]
    public void MainFormConstructsOnStaThreadWithRequiredAccessibleFields()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var directory = Path.Combine(
                    Path.GetTempPath(),
                    $"lab-desktop-form-tests-{Guid.NewGuid():N}");
                var paths = new AppPaths(directory);
                var logger = new FileLogger(paths.LogFile);
                var store = new JsonSettingsStore(paths.SettingsFile);
                var settings = new SettingsLoadResult(new ClientSettings(), null);
                var viewer = new TurboVncViewerLauncher(logger);

                using var form = new MainForm(
                    settings,
                    store,
                    [
                        new DesktopConnectionWorkflow(
                            new UnusedTunnelFactory(),
                            viewer),
                        new UnusedIsaacWorkflow()
                    ],
                    viewer,
                    new InMemoryCredentialStore(),
                    paths,
                    logger);
                form.CreateControl();
                form.PerformLayout();

                Assert.NotNull(form.Icon);
                Assert.Contains(
                    "LabDesktop.Client.App.Assets.LabConnect.ico",
                    typeof(MainForm).Assembly.GetManifestResourceNames());

                var accessibleNames = Descendants(form)
                    .Select(control => control.AccessibleName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToHashSet(StringComparer.Ordinal);
                Assert.Contains("服务器地址", accessibleNames);
                Assert.Contains("SSH 端口", accessibleNames);
                Assert.Contains("用户名", accessibleNames);
                Assert.Contains("SSH 密码", accessibleNames);
                Assert.Contains("显示密码", accessibleNames);
                Assert.Contains("连接模式", accessibleNames);
                Assert.Contains("桌面分辨率", accessibleNames);
                Assert.Contains("TurboVNC Viewer 路径", accessibleNames);
                Assert.Contains("记住 SSH 密码", accessibleNames);
                Assert.Contains("WebView2 Runtime 状态", accessibleNames);
                Assert.Contains("安装 WebView2 Runtime", accessibleNames);
                Assert.Contains("重新检测 WebView2 Runtime", accessibleNames);

                Assert.Equal(string.Empty, FindByAccessibleName(form, "服务器地址").Text);
                Assert.Equal(string.Empty, FindByAccessibleName(form, "SSH 端口").Text);
                Assert.Equal(string.Empty, FindByAccessibleName(form, "用户名").Text);
                Assert.Equal(
                    ConnectionProfile.DefaultGeometry,
                    FindByAccessibleName(form, "桌面分辨率").Text);

                var password = Assert.IsType<TextBox>(FindByAccessibleName(form, "SSH 密码"));
                var revealPassword = FindByAccessibleName(form, "显示密码");
                Assert.True(password.UseSystemPasswordChar);
                Assert.Same(password.Parent, revealPassword.Parent);
                Assert.IsType<TableLayoutPanel>(password.Parent);
                var revealButton = Assert.IsType<PasswordRevealButton>(revealPassword);
                revealButton.TogglePasswordVisibility();
                Assert.False(password.UseSystemPasswordChar);
                Assert.Equal("隐藏密码", revealPassword.AccessibleName);

                var rememberPassword = FindByAccessibleName(form, "记住 SSH 密码");
                Assert.True(rememberPassword.Width >= rememberPassword.PreferredSize.Width);
                Assert.True(rememberPassword.Right <= rememberPassword.Parent!.ClientRectangle.Right);
                var title = Descendants(form).Single(control =>
                    string.Equals(control.Text, "实验室远程桌面", StringComparison.Ordinal));
                Assert.Single(title.Parent!.Controls.Cast<Control>());

                var forgetHost = FindByAccessibleName(form, "忘记当前服务器指纹");
                var diagnostics = FindByAccessibleName(form, "打开诊断文件");
                var statusLayout = Assert.IsType<TableLayoutPanel>(forgetHost.Parent);
                Assert.Same(statusLayout, diagnostics.Parent);
                Assert.Equal(statusLayout.GetRow(forgetHost), statusLayout.GetRow(diagnostics));
                Assert.NotEqual(statusLayout.GetColumn(forgetHost), statusLayout.GetColumn(diagnostics));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "MainForm smoke test timed out.");
        Assert.Null(failure);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static Control FindByAccessibleName(Control parent, string accessibleName) =>
        Descendants(parent).Single(control =>
            string.Equals(control.AccessibleName, accessibleName, StringComparison.Ordinal));

    private sealed class UnusedTunnelFactory : IDesktopTunnelFactory
    {
        public Task<IDesktopTunnel> OpenAsync(
            ConnectionRequest request,
            IHostKeyVerifier hostKeyVerifier,
            IProgress<ConnectionProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The smoke test must not open a connection.");
    }

    private sealed class UnusedIsaacWorkflow : IConnectionWorkflow
    {
        public ConnectionMode Mode => ConnectionMode.IsaacSim;

        public Task RunAsync(
            ConnectionRequest request,
            IHostKeyVerifier hostKeyVerifier,
            IProgress<ConnectionProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The smoke test must not open a connection.");
    }

    private sealed class InMemoryCredentialStore : ISshCredentialStore
    {
        public string? Read(string routeIdentity) => null;

        public void Write(string routeIdentity, string userName, string password)
        {
        }

        public void Delete(string routeIdentity)
        {
        }
    }
}
