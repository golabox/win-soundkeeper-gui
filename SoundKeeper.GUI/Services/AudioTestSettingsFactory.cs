using SoundKeeper.GUI.Models;

namespace SoundKeeper.GUI.Services;

public static class AudioTestSettingsFactory
{
    public static AppSettings Create(AppSettings settings)
    {
        var audibleMode = settings.SignalMode is SignalMode.Sine or SignalMode.White or SignalMode.Brown or SignalMode.Pink
            ? settings.SignalMode
            : SignalMode.Sine;
        var frequency = settings.SignalMode is SignalMode.Fluctuate or SignalMode.Sine
            ? Math.Clamp(settings.FrequencyHz, 20, 20_000)
            : 1_000;

        return new AppSettings
        {
            DeviceMode = settings.DeviceMode,
            SelectedDevices = [.. settings.SelectedDevices],
            SignalMode = audibleMode,
            FrequencyHz = frequency,
            AmplitudePercent = Math.Max(10, settings.AmplitudePercent),
            PlaySeconds = 0,
            WaitSeconds = 0,
            FadeSeconds = 0.05,
            SleepBehavior = SleepBehavior.NeverDetectSleep,
            AllowRemoteAudio = settings.AllowRemoteAudio
        };
    }
}
