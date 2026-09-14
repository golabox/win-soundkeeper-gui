namespace SoundKeeper.GUI.Models;

public enum DeviceMode
{
    Primary,
    All,
    Digital,
    Analog,
    Marked
}

public enum SignalMode
{
    Fluctuate,
    Zero,
    OpenOnly,
    Sine,
    White,
    Brown,
    Pink
}

public enum SleepBehavior
{
    Standard,
    WhenLocked,
    WhenDisplayOff,
    WhenLockedOrDisplayOff,
    NeverDetectSleep
}

public enum AppTheme
{
    System,
    Light,
    Dark
}

public enum AppLanguage
{
    French,
    English,
    Spanish
}

public sealed class AppSettings
{
    public bool Enabled { get; set; }
    public DeviceMode DeviceMode { get; set; } = DeviceMode.Primary;
    public SignalMode SignalMode { get; set; } = SignalMode.Fluctuate;
    public double FrequencyHz { get; set; } = 50;
    public double AmplitudePercent { get; set; } = 1;
    public double PlaySeconds { get; set; }
    public double WaitSeconds { get; set; }
    public double FadeSeconds { get; set; } = 0.1;
    public SleepBehavior SleepBehavior { get; set; } = SleepBehavior.Standard;
    public bool AllowRemoteAudio { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public string? Language { get; set; }
    public bool LoggingEnabled { get; set; } = true;
    public bool AutoRestartEngine { get; set; } = true;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public string AdditionalArguments { get; set; } = string.Empty;
}
