using LabDesktop.Client.App.Configuration;
using LabDesktop.Client.App.Diagnostics;
using LabDesktop.Client.App.Infrastructure;
using LabDesktop.Client.App.Security;
using LabDesktop.Client.Core;

namespace LabDesktop.Client.App;

internal static class Program
{
    private const string SingleInstanceMutex = @"Local\LabDesktopClient";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutex, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Lab Desktop Client 已经在运行。",
                "实验室远程桌面",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        var paths = new AppPaths();
        var logger = new FileLogger(paths.LogFile);
        var settingsStore = new JsonSettingsStore(paths.SettingsFile);
        var settingsLoad = settingsStore.Load();
        var viewerLauncher = new TurboVncViewerLauncher(logger);
        var credentialStore = new WindowsCredentialStore();
        var connector = new SshClientConnector(logger);
        var tunnelFactory = new SshDesktopTunnelFactory(connector, logger);
        var isaacSessionFactory = new SshIsaacSessionFactory(connector, logger);
        var isaacViewerLauncher = new IsaacWebViewerLauncher(logger);
        IConnectionWorkflow[] workflows =
        [
            new DesktopConnectionWorkflow(tunnelFactory, viewerLauncher),
            new IsaacConnectionWorkflow(isaacSessionFactory, isaacViewerLauncher)
        ];

        Application.ThreadException += (_, eventArgs) =>
            logger.Error("Unhandled UI exception", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                logger.Error("Unhandled application exception", exception);
            }
        };

        logger.Info("Lab Desktop Client started");
        Application.Run(new MainForm(
            settingsLoad,
            settingsStore,
            workflows,
            viewerLauncher,
            credentialStore,
            paths,
            logger));
        logger.Info("Lab Desktop Client stopped");
    }
}
