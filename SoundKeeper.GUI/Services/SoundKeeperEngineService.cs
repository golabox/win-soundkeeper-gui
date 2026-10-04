using System.Diagnostics;
using SoundKeeper.GUI.Models;

namespace SoundKeeper.GUI.Services;

public sealed class SoundKeeperEngineService
{
    private readonly EngineExecutableResolver _resolver;
    private readonly AppLogger _logger;
    private Process? _process;

    public SoundKeeperEngineService(EngineExecutableResolver resolver, AppLogger logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    public string? EnginePath => _resolver.Resolve();

    public string? EngineVersion
    {
        get
        {
            var path = EnginePath;
            return path is null ? null : FileVersionInfo.GetVersionInfo(path).ProductVersion;
        }
    }

    public ProcessStartInfo CreateStartInfo(AppSettings settings)
    {
        var path = EnginePath ?? throw new FileNotFoundException(
            $"Moteur Sound Keeper introuvable ({_resolver.ExpectedFileName}).");
        return CreateStartInfo(path, CliArgumentBuilder.Build(settings));
    }

    public ProcessStartInfo CreateStopInfo()
    {
        var path = EnginePath ?? throw new FileNotFoundException(
            $"Moteur Sound Keeper introuvable ({_resolver.ExpectedFileName}).");
        return CreateStartInfo(path, "kill");
    }

    public async Task StartAsync(AppSettings settings)
    {
        var startInfo = CreateStartInfo(settings);
        _process?.Dispose();
        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Le moteur n'a pas démarré.");
        await Task.Delay(250).ConfigureAwait(false);
        if (_process.HasExited && _process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Sound Keeper s'est arrêté avec le code 0x{_process.ExitCode:X8}.");
        }

        await _logger.InfoAsync($"Moteur trouvé: {startInfo.FileName}").ConfigureAwait(false);
        await _logger.InfoAsync($"Moteur démarré (PID {_process.Id}): {startInfo.Arguments}").ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        var process = Process.Start(CreateStopInfo()) ?? throw new InvalidOperationException("La commande d'arrêt n'a pas démarré.");
        using (process)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
                await _logger.WarningAsync("La commande officielle 'kill' n'a pas répondu; arrêt de secours ciblé.").ConfigureAwait(false);
            }
        }

        await StopRemainingEngineProcessesAsync().ConfigureAwait(false);

        if (_process is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The process was not started by this service instance.
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        await _logger.InfoAsync("Moteur arrêté avec la commande officielle 'kill'.").ConfigureAwait(false);
    }

    private async Task StopRemainingEngineProcessesAsync()
    {
        var expectedPath = EnginePath;
        if (expectedPath is null) return;
        expectedPath = Path.GetFullPath(expectedPath);

        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(expectedPath));
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (!string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? string.Empty), expectedPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    await _logger.WarningAsync($"Impossible de terminer un processus moteur résiduel: {exception.Message}").ConfigureAwait(false);
                }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    // No explicit stop: the new engine instance stops the previous one itself (SoundKeeperStopEvent /
    // SoundKeeperMutex), so two engines never run together.
    public Task RestartAsync(AppSettings settings) => StartAsync(settings);

    public EngineSnapshot GetSnapshot()
    {
        if (_process is { HasExited: false })
        {
            return new EngineSnapshot(true, _process.Id, ReadStartTime(_process));
        }

        var processName = Path.GetFileNameWithoutExtension(_resolver.ExpectedFileName);
        var processes = Process.GetProcessesByName(processName);
        try
        {
            var process = processes.FirstOrDefault();
            return process is null
                ? new EngineSnapshot(false, null, null)
                : new EngineSnapshot(true, process.Id, ReadStartTime(process));
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static DateTimeOffset? ReadStartTime(Process process)
    {
        try { return process.StartTime; }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string path, string arguments) => new()
    {
        FileName = path,
        Arguments = arguments,
        WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory,
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden
    };
}

public readonly record struct EngineSnapshot(bool IsRunning, int? ProcessId, DateTimeOffset? StartedAt);
