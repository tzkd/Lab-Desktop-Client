using LabDesktop.Client.App.Infrastructure;

namespace LabDesktop.Client.Core.Tests;

public sealed class IsaacWebViewerLauncherTests
{
    [Fact]
    public void RuntimeGuideUsesTheOfficialMicrosoftDownloadPage()
    {
        var uri = new Uri(WebView2RuntimeSupport.DownloadPageUrl);

        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
        Assert.Equal("developer.microsoft.com", uri.Host);
        Assert.Null(WebView2RuntimeSupport.NormalizeVersion("  "));
        Assert.Equal("151.0.4129.50", WebView2RuntimeSupport.NormalizeVersion(" 151.0.4129.50 "));
    }

    [Fact]
    public void ViewerUsesATrustworthyLoopbackVirtualOrigin()
    {
        Assert.Equal(Uri.UriSchemeHttp, IsaacWebViewerLauncher.ApplicationUri.Scheme);
        Assert.EndsWith(".localhost", IsaacWebViewerLauncher.ApplicationHost, StringComparison.Ordinal);
        Assert.True(IsaacWebViewerLauncher.IsApplicationUri(
            IsaacWebViewerLauncher.ApplicationUri.AbsoluteUri));
    }

    [Theory]
    [InlineData("https://isaac.labdesktop.localhost/index.html")]
    [InlineData("http://isaac.labdesktop.localhost.test/index.html")]
    [InlineData("http://isaac.labdesktop.localhost:444/index.html")]
    [InlineData("http://unexpected@isaac.labdesktop.localhost/index.html")]
    [InlineData("file:///C:/viewer/index.html")]
    public void ViewerRejectsUntrustedOrigins(string value)
    {
        Assert.False(IsaacWebViewerLauncher.IsApplicationUri(value));
    }

    [Fact]
    public void MissingNativeLoaderProducesAnActionableFailure()
    {
        var failure = new DllNotFoundException("WebView2Loader.dll");

        var mapped = IsaacWebViewerLauncher.MapWebViewInitializationFailure(failure);

        Assert.Equal(DesktopErrorCode.ViewerStartFailed, mapped.Code);
        Assert.Contains("WebView2Loader.dll", mapped.Message, StringComparison.Ordinal);
        Assert.Same(failure, mapped.InnerException);
    }

    [Fact]
    public void WrongNativeLoaderArchitectureProducesAnActionableFailure()
    {
        var failure = new BadImageFormatException("WebView2Loader.dll");

        var mapped = IsaacWebViewerLauncher.MapWebViewInitializationFailure(failure);

        Assert.Equal(DesktopErrorCode.ViewerStartFailed, mapped.Code);
        Assert.Contains("win-x64", mapped.Message, StringComparison.Ordinal);
        Assert.Same(failure, mapped.InnerException);
    }
}
