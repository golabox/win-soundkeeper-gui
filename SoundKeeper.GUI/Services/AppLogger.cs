using System.Text;

namespace SoundKeeper.GUI.Services;

public sealed class AppLogger
{
    private const long MaximumLogBytes = 1_048_576;
    private const int MaximumArchives = 3;
    private readonly string _logPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppLogger(string? storageDirectory = null)
    {
        var directory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoundKeeper.GUI");
        Directory.CreateDirectory(directory);
        _logPath = Path.Combine(directory, "SoundKeeper.GUI.log");
    }

    public string LogPath => _logPath;
    public string LogDirectory => Path.GetDirectoryName(_logPath)!;
    public bool Enabled { get; set; } = true;

    public Task InfoAsync(string message) => WriteAsync("INFO", message);

    public Task WarningAsync(string message) => WriteAsync("WARN", message);

    public Task ErrorAsync(string message, Exception exception) =>
        WriteAsync("ERROR", $"{message}{Environment.NewLine}{exception}");

    public async Task ClearAllLogsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var path in Directory.EnumerateFiles(LogDirectory, "SoundKeeper.GUI.log.*"))
            {
                File.Delete(path);
            }
            if (File.Exists(_logPath)) File.WriteAllText(_logPath, string.Empty, Encoding.UTF8);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> ReadTailAsync(int maximumCharacters = 250_000)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_logPath)) return string.Empty;
            await using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var truncated = stream.Length > maximumCharacters;
            if (truncated) stream.Seek(-maximumCharacters, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var text = await reader.ReadToEndAsync().ConfigureAwait(false);
            if (truncated)
            {
                var firstBreak = text.IndexOf('\n');
                if (firstBreak >= 0) text = text[(firstBreak + 1)..];
            }
            return text;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WriteAsync(string level, string message)
    {
        if (!Enabled) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            RotateIfNeeded();
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
            await File.AppendAllTextAsync(_logPath, line, Encoding.UTF8).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_logPath) || new FileInfo(_logPath).Length < MaximumLogBytes) return;

        var oldest = $"{_logPath}.{MaximumArchives}";
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var index = MaximumArchives - 1; index >= 1; index--)
        {
            var source = $"{_logPath}.{index}";
            if (File.Exists(source)) File.Move(source, $"{_logPath}.{index + 1}");
        }
        File.Move(_logPath, $"{_logPath}.1");
    }
}
