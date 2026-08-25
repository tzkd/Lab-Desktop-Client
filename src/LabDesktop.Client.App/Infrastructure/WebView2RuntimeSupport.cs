using Microsoft.Web.WebView2.Core;

namespace LabDesktop.Client.App.Infrastructure;

internal static class WebView2RuntimeSupport
{
    public const string DownloadPageUrl =
        "https://developer.microsoft.com/en-us/microsoft-edge/webview2/";

    public static string? GetInstalledVersion()
    {
        try
        {
            return NormalizeVersion(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return null;
        }
    }

    internal static string? NormalizeVersion(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
