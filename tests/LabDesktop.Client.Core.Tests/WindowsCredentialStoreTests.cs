using LabDesktop.Client.App.Security;

namespace LabDesktop.Client.Core.Tests;

public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public void CredentialTargetIsCanonicalAndApplicationScoped()
    {
        var target = WindowsCredentialStore.BuildTargetName(" DEF@Example.Test:2277 ");

        Assert.Equal("LabDesktopClient/ssh/def@example.test:2277", target);
    }
}
