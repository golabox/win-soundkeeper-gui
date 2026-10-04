using System.Globalization;
using SoundKeeper.GUI.Models;

namespace SoundKeeper.GUI.Services;

public static class CliArgumentBuilder
{
    public static string Build(AppSettings settings)
    {
        var arguments = new List<string>
        {
            settings.DeviceMode switch
            {
                DeviceMode.Primary => "primary",
                DeviceMode.All => "all",
                DeviceMode.Digital => "digital",
                DeviceMode.Analog => "analog",
                DeviceMode.Marked => "marked",
                DeviceMode.Selected => "selected",
                _ => throw new ArgumentOutOfRangeException(nameof(settings.DeviceMode))
            },
            settings.SignalMode switch
            {
                SignalMode.Fluctuate => "fluctuate",
                SignalMode.Zero => "zero",
                SignalMode.OpenOnly => "openonly",
                SignalMode.Sine => "sine",
                SignalMode.White => "white",
                SignalMode.Brown => "brown",
                SignalMode.Pink => "pink",
                _ => throw new ArgumentOutOfRangeException(nameof(settings.SignalMode))
            }
        };

        if (settings.DeviceMode == DeviceMode.Selected)
        {
            // Endpoint IDs contain no space or quote: anything else could inject other engine arguments.
            arguments.InsertRange(1, settings.SelectedDevices
                .Select(device => device.Id)
                .Where(id => id.Length > 0 && !id.Any(character => char.IsWhiteSpace(character) || character == '"'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id => $"device={id}"));
        }

        AddSignalParameters(arguments, settings);

        arguments.Add(settings.SleepBehavior switch
        {
            SleepBehavior.Standard => string.Empty,
            SleepBehavior.WhenLocked => "sleepl",
            SleepBehavior.WhenDisplayOff => "sleepd",
            SleepBehavior.WhenLockedOrDisplayOff => "sleepy",
            SleepBehavior.NeverDetectSleep => "nosleep",
            _ => throw new ArgumentOutOfRangeException(nameof(settings.SleepBehavior))
        });

        if (settings.AllowRemoteAudio)
        {
            arguments.Add("remote");
        }

        var generated = string.Join(' ', arguments.Where(argument => argument.Length > 0));
        var additional = settings.AdditionalArguments.Trim();
        return additional.Length == 0 ? generated : $"{generated} {additional}";
    }

    private static void AddSignalParameters(List<string> arguments, AppSettings settings)
    {
        if (settings.SignalMode is SignalMode.Fluctuate or SignalMode.Sine)
        {
            arguments.Add($"-f {Format(Clamp(settings.FrequencyHz, 0, 96000))}");
        }

        if (settings.SignalMode is SignalMode.Sine or SignalMode.White or SignalMode.Brown or SignalMode.Pink)
        {
            arguments.Add($"-a {Format(Clamp(settings.AmplitudePercent, 0, 100))}");
        }

        if (settings.SignalMode is SignalMode.Sine or SignalMode.White or SignalMode.Brown or SignalMode.Pink)
        {
            if (settings.PlaySeconds > 0)
            {
                arguments.Add($"-l {Format(Clamp(settings.PlaySeconds, 0, 86400))}");
                if (settings.WaitSeconds > 0)
                {
                    arguments.Add($"-w {Format(Clamp(settings.WaitSeconds, 0, 86400))}");
                }
            }

            arguments.Add($"-t {Format(Clamp(settings.FadeSeconds, 0, 60))}");
        }
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum;

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
