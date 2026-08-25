using System.Net;
using System.Net.NetworkInformation;
using LabDesktop.Client.App.Infrastructure;

namespace LabDesktop.Client.Core.Tests;

public sealed class TurnLocalEndpointSelectionTests
{
    [Fact]
    public void PhysicalInterfaceIsSelectedInsteadOfTunnel()
    {
        var selected = SshIsaacSessionFactory.SelectTurnLocalEndpoint(
        [
            Endpoint("aTrustVNIC", NetworkInterfaceType.Tunnel, "2.0.0.1"),
            Endpoint("WLAN", NetworkInterfaceType.Wireless80211, "10.129.37.62")
        ]);

        Assert.Equal("WLAN", selected.InterfaceName);
        Assert.Equal(IPAddress.Parse("10.129.37.62"), selected.Address);
    }

    [Fact]
    public void LoopbackAndLinkLocalAddressesAreRejected()
    {
        var exception = Assert.Throws<DesktopClientException>(() =>
            SshIsaacSessionFactory.SelectTurnLocalEndpoint(
            [
                Endpoint("loopback", NetworkInterfaceType.Ethernet, "127.0.0.1"),
                Endpoint("link-local", NetworkInterfaceType.Ethernet, "169.254.10.20")
            ]));

        Assert.Equal(DesktopErrorCode.ViewerStartFailed, exception.Code);
        Assert.Contains("以太网或 Wi-Fi", exception.Message, StringComparison.Ordinal);
    }

    private static SshIsaacSessionFactory.TurnLocalEndpoint Endpoint(
        string name,
        NetworkInterfaceType type,
        string address) => new(
            name,
            type,
            OperationalStatus.Up,
            true,
            IPAddress.Parse(address));
}
