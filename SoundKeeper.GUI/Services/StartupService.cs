using Microsoft.Win32;

namespace SoundKeeper.GUI.Services;

// Windows startup targets the stable published build. Only the publish step writes PublishedMarkerFileName next
// to SoundKeeper.GUI.exe (MarkPublishedBuild in SoundKeeper.GUI.csproj): a development build (bin\...) has none.
public sealed class StartupService(
    string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
    string? executablePath = null)
{
    public const string EntryName = "SoundKeeper.GUI";
    public const string PublishedMarkerFileName = "SoundKeeper.GUI.published";

    private readonly string? _executable = executablePath ?? Environment.ProcessPath;

    public bool IsPublishedBuild =>
        Path.GetDirectoryName(_executable) is { Length: > 0 } directory
        && File.Exists(Path.Combine(directory, PublishedMarkerFileName));

    public static string BuildCommand(string executable) => $"\"{executable}\" --background";

    // At launch: the published build takes over or repairs the entry; a development build never touches it.
    public void Synchronize(bool enabled)
    {
        if (IsPublishedBuild) SetEnabled(enabled);
    }

    // User toggle: disabling always removes the entry. Enabling from a development build keeps an entry whose
    // executable still exists (normally the published build) and only registers itself when there is none.
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKeyPath, true)
            ?? throw new InvalidOperationException("Impossible d'ouvrir la clé de démarrage Windows.");

        if (!enabled)
        {
            key.DeleteValue(EntryName, false);
            return;
        }

        var current = key.GetValue(EntryName) as string;
        if (!IsPublishedBuild && TargetExists(current)) return;

        var command = BuildCommand(_executable
            ?? throw new InvalidOperationException("Chemin de l'application introuvable."));
        if (!string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
        {
            key.SetValue(EntryName, command, RegistryValueKind.String);
        }
    }

    private static bool TargetExists(string? command)
    {
        command = command?.Trim();
        if (string.IsNullOrEmpty(command)) return false;
        var executable = command[0] == '"'
            ? command[1..Math.Max(1, command.IndexOf('"', 1))]
            : command.Split(' ', 2)[0];
        return File.Exists(executable);
    }
}
