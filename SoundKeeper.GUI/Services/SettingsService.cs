using System.Text.Json;
using System.Text.Json.Serialization;
using SoundKeeper.GUI.Models;

namespace SoundKeeper.GUI.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsService(AppLogger logger, string? storageDirectory = null)
    {
        StorageDirectory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoundKeeper.GUI");
        SettingsPath = Path.Combine(StorageDirectory, "settings.json");
        _logger = logger;
    }

    public string StorageDirectory { get; }

    public string SettingsPath { get; }

    public static string? ReadSavedLanguage(string? storageDirectory = null)
    {
        var directory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoundKeeper.GUI");
        var path = Path.Combine(directory, "settings.json");
        if (!File.Exists(path)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty(nameof(AppSettings.Language), out var language)
                ? language.GetString()
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            return WithValidValues(await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions).ConfigureAwait(false)
                ?? new AppSettings());
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            await _logger.WarningAsync($"Configuration illisible, valeurs par défaut utilisées: {exception.Message}")
                .ConfigureAwait(false);
            return new AppSettings();
        }
    }

    // A number edited by hand outside an enum (for example "SleepBehavior": 9) falls back to that setting's default;
    // a missing or damaged output selection (older settings, hand edit) loads as an empty selection.
    private static AppSettings WithValidValues(AppSettings settings)
    {
        var defaults = new AppSettings();
        if (!Enum.IsDefined(settings.DeviceMode)) settings.DeviceMode = defaults.DeviceMode;
        if (!Enum.IsDefined(settings.SignalMode)) settings.SignalMode = defaults.SignalMode;
        if (!Enum.IsDefined(settings.SleepBehavior)) settings.SleepBehavior = defaults.SleepBehavior;
        if (!Enum.IsDefined(settings.Theme)) settings.Theme = defaults.Theme;
        settings.SelectedDevices ??= [];
        settings.SelectedDevices.RemoveAll(device => device is null || string.IsNullOrWhiteSpace(device.Id));
        foreach (var device in settings.SelectedDevices) device.Name ??= string.Empty;
        return settings;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(StorageDirectory);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var temporaryPath = SettingsPath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions).ConfigureAwait(false);
            }

            File.Move(temporaryPath, SettingsPath, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
