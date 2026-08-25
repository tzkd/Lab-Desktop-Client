using System.Net;
using LabDesktop.Client.App.Infrastructure;

namespace LabDesktop.Client.Core.Tests;

public sealed class LocalOnlyTcpRelayTests
{
    private static readonly IPAddress[] LocalAddresses =
    [
        IPAddress.Parse("192.0.2.10"),
        IPAddress.Parse("198.51.100.20")
    ];

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("192.0.2.10")]
    [InlineData("::ffff:192.0.2.10")]
    [InlineData("198.51.100.20")]
    public void LocalSourcesAreAccepted(string value)
    {
        Assert.True(LocalOnlyTcpRelay.IsLocalSource(IPAddress.Parse(value), LocalAddresses));
    }

    [Theory]
    [InlineData("192.0.2.11")]
    [InlineData("203.0.113.30")]
    [InlineData("10.0.0.1")]
    public void NonLocalSourcesAreRejected(string value)
    {
        Assert.False(LocalOnlyTcpRelay.IsLocalSource(IPAddress.Parse(value), LocalAddresses));
    }
}
