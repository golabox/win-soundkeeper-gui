namespace SoundKeeper.GUI.Models;

public enum DeviceMode
{
    Primary,
    All,
    Digital,
    Analog,
    Marked,
    Selected
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

// An output checked in the custom selection: the Windows endpoint ID drives the engine, the name is only displayed
// (last name known, kept while the output is absent).
public sealed class OutputDeviceSelection
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class AppSettings
{
    public bool Enabled { get; set; }
    public DeviceMode DeviceMode { get; set; } = DeviceMode.Primary;
    public List<OutputDeviceSelection> SelectedDevices { get; set; } = [];
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
