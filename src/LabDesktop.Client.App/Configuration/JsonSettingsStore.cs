using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabDesktop.Client.App.Configuration;

internal sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;
    private readonly object _writeLock = new();

    public JsonSettingsStore(string path)
    {
        _path = path;
    }

    public SettingsLoadResult Load()
    {
        if (!File.Exists(_path))
        {
            return new SettingsLoadResult(new ClientSettings(), null);
        }

        try
        {
            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<ClientSettings>(json, JsonOptions)
                ?? throw new JsonException("设置文件内容为空。");
            Validate(settings);
            settings.Profile = settings.Profile.Normalize();
            settings.TrustedHosts = settings.TrustedHosts
                .GroupBy(item => item.RouteIdentity, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.TrustedAtUtc).First())
                .ToList();
            return new SettingsLoadResult(settings, null);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            var backupPath = BackupCorruptFile();
            var message = backupPath is null
                ? "设置文件损坏，已使用默认设置。"
                : $"设置文件损坏，已备份到：{backupPath}";
            return new SettingsLoadResult(new ClientSettings(), message);
        }
    }

    public void Save(ClientSettings settings)
    {
        Validate(settings);
        lock (_writeLock)
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("设置文件路径无效。");
            Directory.CreateDirectory(directory);
            var temporaryPath = _path + ".tmp";
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            try
            {
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    private static void Validate(ClientSettings settings)
    {
        if (settings.Schema != ClientSettings.CurrentSchema)
        {
            throw new InvalidDataException($"不支持的设置版本：{settings.Schema}。");
        }

        if (settings.Profile is null || settings.TrustedHosts is null)
        {
            throw new InvalidDataException("设置文件缺少必要字段。");
        }

        if (!Enum.IsDefined(settings.Profile.Mode))
        {
            throw new InvalidDataException("设置文件包含不支持的连接模式。");
        }
    }

    private string? BackupCorruptFile()
    {
        try
        {
            var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var backupPath = $"{_path}.corrupt-{timestamp}";
            File.Copy(_path, backupPath, overwrite: false);
            return backupPath;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
