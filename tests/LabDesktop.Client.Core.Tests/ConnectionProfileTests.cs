using LabDesktop.Client.Core;

namespace LabDesktop.Client.Core.Tests;

public sealed class ConnectionProfileTests
{
    [Fact]
    public void ValidProfileUsesUserScopedRouteIdentity()
    {
        var profile = new ConnectionProfile
        {
            Host = "gateway.example.test",
            Port = 2222,
            UserName = "abc",
            Geometry = "1920x1080"
        };

        Assert.Empty(profile.Validate());
        Assert.Equal("abc@gateway.example.test:2222", profile.RouteIdentity);
    }

    [Fact]
    public void NewProfileOnlyDefaultsDisplayGeometry()
    {
        var profile = new ConnectionProfile();

        Assert.Empty(profile.Host);
        Assert.Equal(0, profile.Port);
        Assert.Empty(profile.UserName);
        Assert.Equal(ConnectionProfile.DefaultGeometry, profile.Geometry);
        Assert.Equal(ConnectionMode.LinuxDesktop, profile.Mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("user name")]
    [InlineData("-abc")]
    public void RejectsInvalidLinuxUserNames(string userName)
    {
        var profile = new ConnectionProfile { UserName = userName };

        Assert.Contains(profile.Validate(), error => error.Contains("用户名", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData(" 2560x1440 ", 2560, 1440)]
    public void ParsesSupportedGeometry(string value, int width, int height)
    {
        Assert.True(DisplayGeometry.TryParse(value, out var geometry));
        Assert.Equal(width, geometry.Width);
        Assert.Equal(height, geometry.Height);
    }

    [Theory]
    [InlineData("1920*1080")]
    [InlineData("0x1080")]
    [InlineData("320x200")]
    [InlineData("99999x1080")]
    public void RejectsUnsupportedGeometry(string value)
    {
        Assert.False(DisplayGeometry.TryParse(value, out _));
    }

    [Fact]
    public void RejectsUnknownConnectionMode()
    {
        var profile = new ConnectionProfile { Mode = (ConnectionMode)99 };

        Assert.Contains(profile.Validate(), error => error.Contains("连接模式", StringComparison.Ordinal));
    }

    [Fact]
    public void IsaacModeRejectsDimensionsBeyondTheStreamingSdkLimit()
    {
        var profile = new ConnectionProfile
        {
            Mode = ConnectionMode.IsaacSim,
            Geometry = "5120x2880"
        };

        Assert.Contains(profile.Validate(), error => error.Contains("4096", StringComparison.Ordinal));
    }

    [Fact]
    public void RequestStringNeverContainsPassword()
    {
        var request = new ConnectionRequest(
            new ConnectionProfile
            {
                Host = "gateway.example.test",
                Port = 2222,
                UserName = "abc"
            },
            "highly-secret-password");

        Assert.DoesNotContain("highly-secret-password", request.ToString(), StringComparison.Ordinal);
        Assert.Equal("abc@gateway.example.test:2222", request.ToString());
    }
}
