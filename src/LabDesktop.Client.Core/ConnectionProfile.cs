using System.Text.RegularExpressions;

namespace LabDesktop.Client.Core;

public enum ConnectionMode
{
    LinuxDesktop,
    IsaacSim
}

public sealed record ConnectionProfile
{
    public const string DefaultGeometry = "1920x1080";

    private static readonly Regex UserNamePattern = new(
        "^[a-z_][a-z0-9_-]{0,31}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public string Name { get; init; } = "实验室桌面";

    public ConnectionMode Mode { get; init; } = ConnectionMode.LinuxDesktop;

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string Geometry { get; init; } = DefaultGeometry;

    public string? ViewerPath { get; init; }

    public string RouteIdentity => $"{UserName}@{Host}:{Port}";

    public ConnectionProfile Normalize() => this with
    {
        Name = Name.Trim(),
        Host = Host.Trim(),
        UserName = UserName.Trim(),
        Geometry = Geometry.Trim(),
        ViewerPath = string.IsNullOrWhiteSpace(ViewerPath) ? null : ViewerPath.Trim()
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var normalized = Normalize();

        if (!Enum.IsDefined(normalized.Mode))
        {
            errors.Add("连接模式无效。");
        }

        if (normalized.Name.Length is < 1 or > 80)
        {
            errors.Add("连接名称必须为 1 至 80 个字符。");
        }

        if (normalized.Host.Length is < 1 or > 253 ||
            normalized.Host.Any(char.IsWhiteSpace) ||
            normalized.Host.Any(char.IsControl))
        {
            errors.Add("服务器地址无效。");
        }

        if (normalized.Port is < 1 or > 65535)
        {
            errors.Add("SSH 端口必须在 1 至 65535 之间。");
        }

        if (!UserNamePattern.IsMatch(normalized.UserName))
        {
            errors.Add("用户名必须是有效的 Linux 用户名。");
        }

        if (!DisplayGeometry.TryParse(normalized.Geometry, out var geometry))
        {
            errors.Add("桌面分辨率格式无效，例如 1920x1080。");
        }
        else if (normalized.Mode == ConnectionMode.IsaacSim &&
                 (geometry.Width > 4096 || geometry.Height > 4096))
        {
            errors.Add("Isaac Sim GUI 分辨率的宽和高不能超过 4096。");
        }

        return errors;
    }
}

public readonly record struct DisplayGeometry(int Width, int Height)
{
    public const int MinimumWidth = 640;
    public const int MinimumHeight = 480;
    public const int MaximumDimension = 16384;

    public static bool TryParse(string? value, out DisplayGeometry geometry)
    {
        geometry = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var width) ||
            !int.TryParse(parts[1], out var height) ||
            width is < MinimumWidth or > MaximumDimension ||
            height is < MinimumHeight or > MaximumDimension)
        {
            return false;
        }

        geometry = new DisplayGeometry(width, height);
        return true;
    }

    public override string ToString() => $"{Width}x{Height}";
}

public sealed class ConnectionRequest
{
    public ConnectionRequest(ConnectionProfile profile, string password)
    {
        Profile = profile.Normalize();
        Password = password ?? throw new ArgumentNullException(nameof(password));
    }

    public ConnectionProfile Profile { get; }

    public string Password { get; }

    public void EnsureValid()
    {
        var errors = Profile.Validate();
        if (errors.Count > 0)
        {
            throw new DesktopClientException(
                DesktopErrorCode.InvalidConfiguration,
                string.Join(Environment.NewLine, errors));
        }

        if (Password.Length == 0)
        {
            throw new DesktopClientException(
                DesktopErrorCode.InvalidConfiguration,
                "请输入 SSH 密码。");
        }
    }

    public override string ToString() => Profile.RouteIdentity;
}
