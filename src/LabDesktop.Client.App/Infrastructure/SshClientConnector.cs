using System.Net.Sockets;
using LabDesktop.Client.App.Diagnostics;
using LabDesktop.Client.Core;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace LabDesktop.Client.App.Infrastructure;

internal sealed class SshClientConnector
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(20);
    private readonly FileLogger _logger;

    public SshClientConnector(FileLogger logger)
    {
        _logger = logger;
    }

    public async Task<SshClient> ConnectAsync(
        ConnectionRequest request,
        IHostKeyVerifier hostKeyVerifier,
        CancellationToken cancellationToken)
    {
        var passwordAuthentication = new PasswordAuthenticationMethod(
            request.Profile.UserName,
            request.Password);
        var keyboardAuthentication = new KeyboardInteractiveAuthenticationMethod(
            request.Profile.UserName);
        keyboardAuthentication.AuthenticationPrompt += (_, eventArgs) =>
        {
            foreach (var prompt in eventArgs.Prompts.Where(item =>
                         !item.IsEchoed &&
                         item.Request.Contains("password", StringComparison.OrdinalIgnoreCase)))
            {
                prompt.Response = request.Password;
            }
        };

        var connectionInfo = new ConnectionInfo(
            request.Profile.Host,
            request.Profile.Port,
            request.Profile.UserName,
            passwordAuthentication,
            keyboardAuthentication)
        {
            Timeout = ConnectionTimeout
        };
        var client = new SshClient(connectionInfo)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        };
        var hostKeyRejected = false;
        client.HostKeyReceived += (_, eventArgs) =>
        {
            var identity = new HostKeyIdentity(
                request.Profile.RouteIdentity,
                eventArgs.HostKeyName,
                $"SHA256:{eventArgs.FingerPrintSHA256}");
            try
            {
                eventArgs.CanTrust = hostKeyVerifier.Verify(identity);
                hostKeyRejected = !eventArgs.CanTrust;
            }
            catch (Exception exception)
            {
                _logger.Error(
                    $"Host key verification failed for {request.Profile.RouteIdentity}",
                    exception);
                hostKeyRejected = true;
                eventArgs.CanTrust = false;
            }
        };

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info($"SSH connected: {request.Profile.RouteIdentity}");
            return client;
        }
        catch (Exception exception)
        {
            client.Dispose();
            throw MapException(exception, hostKeyRejected, request.Profile.RouteIdentity);
        }
    }

    public static Exception MapException(
        Exception exception,
        bool hostKeyRejected,
        string routeIdentity)
    {
        if (exception is DesktopClientException or OperationCanceledException)
        {
            return exception;
        }

        if (hostKeyRejected)
        {
            return new DesktopClientException(
                DesktopErrorCode.HostKeyRejected,
                $"服务器身份验证未通过：{routeIdentity}。",
                exception);
        }

        return exception switch
        {
            SshAuthenticationException => new DesktopClientException(
                DesktopErrorCode.AuthenticationFailed,
                "SSH 用户名或密码不正确。",
                exception),
            SocketException or SshOperationTimeoutException => new DesktopClientException(
                DesktopErrorCode.ServerUnavailable,
                $"无法连接服务器：{routeIdentity}。",
                exception),
            SshConnectionException => new DesktopClientException(
                DesktopErrorCode.ConnectionLost,
                "SSH 握手或连接失败。",
                exception),
            _ => new DesktopClientException(
                DesktopErrorCode.ConnectionLost,
                "建立远程连接时发生未预期错误。",
                exception)
        };
    }
}
