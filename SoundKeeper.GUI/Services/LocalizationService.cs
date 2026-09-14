using System.Xml.Linq;

namespace SoundKeeper.GUI.Services;

public sealed class LocalizationService
{
    public const string English = "en-US";
    public const string French = "fr-FR";
    public const string Spanish = "es-ES";
    private static string _currentLanguage = French;
    private readonly IReadOnlyDictionary<string, string> _strings;

    public LocalizationService()
    {
        _strings = LoadStrings(CurrentLanguage);
    }

    public static string ActiveLanguage => _currentLanguage;
    public string CurrentLanguage => ActiveLanguage;

    public static string ResolveLanguage(string? savedLanguage, string windowsLanguage)
    {
        if (!string.IsNullOrWhiteSpace(savedLanguage)) return Normalize(savedLanguage);
        return Normalize(windowsLanguage);
    }

    public static void ApplyLanguage(string language)
    {
        _currentLanguage = Normalize(language);
    }

    public static string Normalize(string? language)
    {
        if (language?.StartsWith("fr", StringComparison.OrdinalIgnoreCase) == true) return French;
        if (language?.StartsWith("es", StringComparison.OrdinalIgnoreCase) == true) return Spanish;
        return English;
    }

    public string Get(string key, string fallback)
    {
        return _strings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value)
            ? value
            : fallback;
    }

    private static IReadOnlyDictionary<string, string> LoadStrings(string language)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Strings", language, "Resources.resw");
            return XDocument.Load(path)
                .Root?
                .Elements("data")
                .Where(element => element.Attribute("name") is not null)
                .ToDictionary(
                    element => element.Attribute("name")!.Value,
                    element => element.Element("value")?.Value ?? string.Empty,
                    StringComparer.Ordinal) ?? new Dictionary<string, string>();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new Dictionary<string, string>();
        }
    }
}
