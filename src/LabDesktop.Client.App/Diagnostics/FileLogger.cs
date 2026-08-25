namespace LabDesktop.Client.App.Diagnostics;

internal sealed class FileLogger
{
    private const long MaximumLogBytes = 1024 * 1024;
    private readonly string _path;
    private readonly object _lock = new();

    public FileLogger(string path)
    {
        _path = path;
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception exception) =>
        Write("ERROR", $"{message} | {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                RotateIfNeeded();
                var sanitized = message
                    .Replace('\r', ' ')
                    .Replace('\n', ' ');
                File.AppendAllText(
                    _path,
                    $"{DateTimeOffset.Now:O} [{level}] {sanitized}{Environment.NewLine}");
            }
            catch
            {
                // Logging is diagnostic and must never break a connection.
            }
        }
    }

    private void RotateIfNeeded()
    {
        var file = new FileInfo(_path);
        if (!file.Exists || file.Length < MaximumLogBytes)
        {
            return;
        }

        var previous = _path + ".1";
        File.Move(_path, previous, overwrite: true);
    }
}
