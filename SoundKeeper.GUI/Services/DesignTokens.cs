using Microsoft.UI.Xaml;

namespace SoundKeeper.GUI.Services;

/// <summary>Reads Golabox tokens (Themes/Tokens.xaml) from code-behind.</summary>
internal static class DesignTokens
{
    public static double Get(string key, double fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is double number ? number : fallback;
}
