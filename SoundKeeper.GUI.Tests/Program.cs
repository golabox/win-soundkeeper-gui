using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SoundKeeper.GUI.Models;
using SoundKeeper.GUI.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("CLI defaults", CliDefaults),
    ("CLI principal combinations", CliCombinations),
    ("CLI numeric bounds and culture", CliNumericBounds),
    ("CLI additional argument quoting", CliAdditionalArguments),
    ("CLI custom output selection", CliCustomOutputSelection),
    ("Settings round trip", SettingsRoundTrip),
    ("Invalid settings fallback", InvalidSettingsFallback),
    ("Out-of-range settings enums fallback", OutOfRangeSettingsEnumsFallback),
    ("Settings keep selected outputs", SettingsKeepSelectedOutputs),
    ("Absent selected output stays listed", AbsentSelectedOutputStaysListed),
    ("Language selection", LanguageSelection),
    ("Audible test safe fallback", AudibleTestSafeFallback),
    ("Audible test preserves selection", AudibleTestPreservesSelection),
    ("Configurable logging", ConfigurableLogging),
    ("Locked log never breaks the GUI", LockedLogIsIgnored),
    ("Integrated log viewer data", IntegratedLogViewerData),
    ("Log view formatting and filter", LogViewFormatting),
    ("Restart crash-loop guard", RestartCrashLoopGuard),
    ("Engine executable resolution", EngineResolution),
    ("Startup entry stays on the published build", StartupEntryStaysOnPublishedBuild),
    ("ProcessStartInfo safety", ProcessStartInfoSafety),
    ("Upstream CLI contract", UpstreamCliContract)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL  {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static Task CliDefaults()
{
    Equal("primary fluctuate -f 50", CliArgumentBuilder.Build(new AppSettings()));
    return Task.CompletedTask;
}

static Task CliCombinations()
{
    var settings = new AppSettings
    {
        DeviceMode = DeviceMode.Marked,
        SignalMode = SignalMode.Sine,
        FrequencyHz = 1000,
        AmplitudePercent = 2.5,
        PlaySeconds = 3,
        WaitSeconds = 10,
        FadeSeconds = 0.2,
        SleepBehavior = SleepBehavior.WhenLockedOrDisplayOff,
        AllowRemoteAudio = true
    };
    Equal("marked sine -f 1000 -a 2.5 -l 3 -w 10 -t 0.2 sleepy remote", CliArgumentBuilder.Build(settings));

    settings.DeviceMode = DeviceMode.Digital;
    settings.SignalMode = SignalMode.OpenOnly;
    settings.SleepBehavior = SleepBehavior.WhenDisplayOff;
    Equal("digital openonly sleepd remote", CliArgumentBuilder.Build(settings));
    return Task.CompletedTask;
}

static Task CliNumericBounds()
{
    var previousCulture = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        var settings = new AppSettings
        {
            SignalMode = SignalMode.Sine,
            FrequencyHz = 150000,
            AmplitudePercent = 125,
            FadeSeconds = -1
        };
        Equal("primary sine -f 96000 -a 100 -t 0", CliArgumentBuilder.Build(settings));
    }
    finally
    {
        CultureInfo.CurrentCulture = previousCulture;
    }
    return Task.CompletedTask;
}

static Task CliAdditionalArguments()
{
    var settings = new AppSettings { AdditionalArguments = "  future \"quoted value\" -x=1  " };
    Equal("primary fluctuate -f 50 future \"quoted value\" -x=1", CliArgumentBuilder.Build(settings));
    return Task.CompletedTask;
}

static Task CliCustomOutputSelection()
{
    const string speakers = "{0.0.0.00000000}.{c606b577-1ca3-45ba-8eb6-edeef318fb91}";
    const string monitor = "{0.0.0.00000000}.{60036a9b-6419-4546-924e-a55fb5b9b268}";

    // Zero selected output: the engine runs in selection mode and keeps nothing.
    var settings = new AppSettings { DeviceMode = DeviceMode.Selected };
    Equal("selected fluctuate -f 50", CliArgumentBuilder.Build(settings));

    settings.SelectedDevices = [new() { Id = speakers, Name = "Haut-parleurs (Logitech)" }];
    Equal($"selected device={speakers} fluctuate -f 50", CliArgumentBuilder.Build(settings));

    // Several outputs; a duplicate ID and a value able to inject other arguments are ignored.
    settings.SelectedDevices.Add(new() { Id = monitor, Name = "DELL S3422DWG" });
    settings.SelectedDevices.Add(new() { Id = speakers.ToUpperInvariant(), Name = "Duplicate" });
    settings.SelectedDevices.Add(new() { Id = "bad\" all", Name = "Injected" });
    Equal($"selected device={speakers} device={monitor} fluctuate -f 50", CliArgumentBuilder.Build(settings));

    // Existing engine modes are unchanged, whatever selection is saved.
    foreach (var (mode, keyword) in new[] { (DeviceMode.Primary, "primary"), (DeviceMode.All, "all"), (DeviceMode.Digital, "digital"), (DeviceMode.Analog, "analog"), (DeviceMode.Marked, "marked") })
    {
        Equal($"{keyword} fluctuate -f 50", CliArgumentBuilder.Build(new AppSettings { DeviceMode = mode, SelectedDevices = settings.SelectedDevices }));
    }
    return Task.CompletedTask;
}

static async Task SettingsKeepSelectedOutputs()
{
    await WithTemporaryDirectory(async directory =>
    {
        var service = new SettingsService(new AppLogger(directory), directory);
        await service.SaveAsync(new AppSettings
        {
            DeviceMode = DeviceMode.Selected,
            SelectedDevices = [new() { Id = "{0.0.0.00000000}.{a}", Name = "Haut-parleurs (Logitech)" }, new() { Id = "{0.0.0.00000000}.{b}", Name = "Casque Bluetooth" }]
        });
        var loaded = await service.LoadAsync();
        Equal(DeviceMode.Selected, loaded.DeviceMode);
        Equal(2, loaded.SelectedDevices.Count);
        Equal("{0.0.0.00000000}.{b}", loaded.SelectedDevices[1].Id);
        Equal("Casque Bluetooth", loaded.SelectedDevices[1].Name);

        // Settings saved before this option, or damaged by hand, load with an empty or cleaned selection.
        var path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(path, """{ "DeviceMode": "All" }""");
        Equal(0, (await service.LoadAsync()).SelectedDevices.Count);
        await File.WriteAllTextAsync(path, """{ "DeviceMode": "Selected", "SelectedDevices": null }""");
        Equal(0, (await service.LoadAsync()).SelectedDevices.Count);
        await File.WriteAllTextAsync(path, """{ "SelectedDevices": [null, { "Id": "", "Name": "x" }, { "Id": "{0.0.0.00000000}.{c}", "Name": null }] }""");
        var cleaned = await service.LoadAsync();
        Equal(1, cleaned.SelectedDevices.Count);
        Equal(string.Empty, cleaned.SelectedDevices[0].Name);
    });
}

static Task AbsentSelectedOutputStaysListed()
{
    var speakers = new AudioOutput("{0.0.0.00000000}.{a}", "Haut-parleurs (Logitech)");
    var monitor = new AudioOutput("{0.0.0.00000000}.{b}", "DELL S3422DWG (NVIDIA High Definition Audio)");
    List<OutputDeviceSelection> selection = [new() { Id = "{0.0.0.00000000}.{A}", Name = "Old name" }, new() { Id = "{0.0.0.00000000}.{c}", Name = "Casque Bluetooth" }];

    // Present output: matched without case, last known name refreshed. Absent output: kept and unavailable.
    True(OutputDeviceCatalog.UpdateNames(selection, [monitor, speakers]), "The last known name was not refreshed.");
    Equal("Haut-parleurs (Logitech)", selection[0].Name);
    var rows = OutputDeviceCatalog.BuildRows([monitor, speakers], selection);
    Equal(3, rows.Count);
    Equal(new OutputDeviceRow(monitor.Id, monitor.Name, true, false), rows[0]);
    Equal(new OutputDeviceRow(speakers.Id, speakers.Name, true, true), rows[1]);
    Equal(new OutputDeviceRow("{0.0.0.00000000}.{c}", "Casque Bluetooth", false, true), rows[2]);

    // The same output is available and selected again as soon as Windows reports it.
    var headset = new AudioOutput("{0.0.0.00000000}.{c}", "Casque Bluetooth");
    True(OutputDeviceCatalog.BuildRows([speakers, headset], selection).All(row => row.IsAvailable && row.IsSelected), "A returning output was not selected again.");
    Equal(false, OutputDeviceCatalog.UpdateNames(selection, [speakers, headset]));
    Equal(2, selection.Count);
    return Task.CompletedTask;
}

static async Task SettingsRoundTrip()
{
    await WithTemporaryDirectory(async directory =>
    {
        var logger = new AppLogger(directory);
        var service = new SettingsService(logger, directory);
        var source = new AppSettings
        {
            Enabled = true,
            DeviceMode = DeviceMode.Analog,
            SignalMode = SignalMode.Brown,
            AmplitudePercent = 0.1,
            Theme = AppTheme.Dark,
            Language = "es-ES",
            LoggingEnabled = false,
            AutoRestartEngine = false,
            AdditionalArguments = "future"
        };
        await service.SaveAsync(source);
        var loaded = await service.LoadAsync();
        Equal(source.Enabled, loaded.Enabled);
        Equal(source.DeviceMode, loaded.DeviceMode);
        Equal(source.SignalMode, loaded.SignalMode);
        Equal(source.AmplitudePercent, loaded.AmplitudePercent);
        Equal(source.Theme, loaded.Theme);
        Equal(source.Language, loaded.Language);
        Equal(source.LoggingEnabled, loaded.LoggingEnabled);
        Equal(source.AutoRestartEngine, loaded.AutoRestartEngine);
        Equal(source.AdditionalArguments, loaded.AdditionalArguments);
    });
}

static Task LanguageSelection()
{
    Equal("fr-FR", LocalizationService.ResolveLanguage(null, "fr-CA"));
    Equal("es-ES", LocalizationService.ResolveLanguage(null, "es-MX"));
    Equal("en-US", LocalizationService.ResolveLanguage(null, "de-DE"));
    Equal("es-ES", LocalizationService.ResolveLanguage("es-ES", "fr-FR"));
    return Task.CompletedTask;
}

static Task AudibleTestSafeFallback()
{
    var source = new AppSettings
    {
        DeviceMode = DeviceMode.Digital,
        SignalMode = SignalMode.Fluctuate,
        FrequencyHz = 50,
        AmplitudePercent = 1,
        SleepBehavior = SleepBehavior.WhenLocked,
        AdditionalArguments = "must-not-leak"
    };

    var test = AudioTestSettingsFactory.Create(source);
    Equal(DeviceMode.Digital, test.DeviceMode);
    Equal(SignalMode.Sine, test.SignalMode);
    Equal(50d, test.FrequencyHz);
    Equal(10d, test.AmplitudePercent);
    Equal(SleepBehavior.NeverDetectSleep, test.SleepBehavior);
    Equal(string.Empty, test.AdditionalArguments);
    Equal(SignalMode.Fluctuate, source.SignalMode);
    Equal(1d, source.AmplitudePercent);
    return Task.CompletedTask;
}

static Task AudibleTestPreservesSelection()
{
    var source = new AppSettings
    {
        DeviceMode = DeviceMode.Selected,
        SelectedDevices = [new() { Id = "{0.0.0.00000000}.{a}", Name = "Haut-parleurs (Logitech)" }],
        SignalMode = SignalMode.Pink,
        FrequencyHz = 80,
        AmplitudePercent = 12,
        AllowRemoteAudio = true
    };

    var test = AudioTestSettingsFactory.Create(source);
    Equal(DeviceMode.Selected, test.DeviceMode);
    Equal("{0.0.0.00000000}.{a}", test.SelectedDevices.Single().Id);
    Equal(SignalMode.Pink, test.SignalMode);
    Equal(1_000d, test.FrequencyHz);
    Equal(12d, test.AmplitudePercent);
    Equal(true, test.AllowRemoteAudio);
    return Task.CompletedTask;
}

static async Task ConfigurableLogging()
{
    await WithTemporaryDirectory(async directory =>
    {
        var logger = new AppLogger(directory) { Enabled = false };
        await logger.InfoAsync("disabled");
        Equal(false, File.Exists(logger.LogPath));
        logger.Enabled = true;
        await logger.InfoAsync("enabled");
        Equal(true, File.Exists(logger.LogPath));
        True((await File.ReadAllTextAsync(logger.LogPath)).Contains("enabled", StringComparison.Ordinal), "Enabled log entry was not written.");
    });
}

static async Task LockedLogIsIgnored()
{
    await WithTemporaryDirectory(async directory =>
    {
        var logger = new AppLogger(directory);
        await logger.InfoAsync("before-lock");
        // An external viewer or antivirus holding the file must not make the GUI calls fail.
        using (new FileStream(logger.LogPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await logger.InfoAsync("while-locked");
        }
        await logger.InfoAsync("after-lock");
        True((await File.ReadAllTextAsync(logger.LogPath)).Contains("after-lock", StringComparison.Ordinal), "Logging did not resume after the lock.");
    });
}

static async Task IntegratedLogViewerData()
{
    await WithTemporaryDirectory(async directory =>
    {
        var logger = new AppLogger(directory);
        await logger.InfoAsync("viewer-entry");
        True((await logger.ReadTailAsync()).Contains("viewer-entry", StringComparison.Ordinal), "The log viewer could not read the current log.");
        await logger.ClearAllLogsAsync();
        Equal(string.Empty, await logger.ReadTailAsync());
    });
}

static Task RestartCrashLoopGuard()
{
    var guard = new RestartAttemptGuard(3, TimeSpan.FromMinutes(1));
    var now = DateTimeOffset.UtcNow;
    Equal(true, guard.TryRegister(now));
    Equal(true, guard.TryRegister(now.AddSeconds(1)));
    Equal(true, guard.TryRegister(now.AddSeconds(2)));
    Equal(false, guard.TryRegister(now.AddSeconds(3)));
    Equal(true, guard.TryRegister(now.AddMinutes(2)));
    return Task.CompletedTask;
}

static async Task InvalidSettingsFallback()
{
    await WithTemporaryDirectory(async directory =>
    {
        await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "{ definitely invalid json");
        var service = new SettingsService(new AppLogger(directory), directory);
        var loaded = await service.LoadAsync();
        Equal(DeviceMode.Primary, loaded.DeviceMode);
        Equal(SignalMode.Fluctuate, loaded.SignalMode);
        Equal(false, loaded.Enabled);
    });
}

static async Task EngineResolution()
{
    await WithTemporaryDirectory(async directory =>
    {
        var engineDirectory = Path.Combine(directory, "Engine");
        Directory.CreateDirectory(engineDirectory);
        var expected = Path.Combine(engineDirectory, "SoundKeeper64.exe");
        await File.WriteAllBytesAsync(expected, []);
        var resolver = new EngineExecutableResolver(directory, Architecture.X64);
        Equal(Path.GetFullPath(expected), resolver.Resolve());
    });
}

static async Task StartupEntryStaysOnPublishedBuild()
{
    const string testRoot = @"Software\SoundKeeper.GUI.Tests";
    var keyPath = $@"{testRoot}\{Guid.NewGuid():N}";
    await WithTemporaryDirectory(async directory =>
    {
        try
        {
            var published = await CreateFakeGuiAsync(Path.Combine(directory, "publish"), published: true);
            var development = await CreateFakeGuiAsync(Path.Combine(directory, "bin"), published: false);
            var fromPublished = new StartupService(keyPath, published);
            var fromDevelopment = new StartupService(keyPath, development);

            // Invalid entry (deleted folder): the published build repairs it at launch.
            SetStartupEntry(keyPath, StartupService.BuildCommand(Path.Combine(directory, "deleted", "SoundKeeper.GUI.exe")));
            fromPublished.Synchronize(true);
            Equal(StartupService.BuildCommand(published), GetStartupEntry(keyPath));

            // Non-regression: a development build never takes a valid entry, at launch or from the toggle.
            fromDevelopment.Synchronize(true);
            fromDevelopment.SetEnabled(true);
            Equal(StartupService.BuildCommand(published), GetStartupEntry(keyPath));

            // An entry left by a development build returns to the published build at its next launch.
            SetStartupEntry(keyPath, StartupService.BuildCommand(development));
            fromPublished.Synchronize(true);
            Equal(StartupService.BuildCommand(published), GetStartupEntry(keyPath));

            // The toggle keeps working from any build.
            fromDevelopment.SetEnabled(false);
            Equal(null, GetStartupEntry(keyPath));
            fromDevelopment.SetEnabled(true);
            Equal(StartupService.BuildCommand(development), GetStartupEntry(keyPath));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testRoot, false);
        }
    });
}

static async Task<string> CreateFakeGuiAsync(string directory, bool published)
{
    Directory.CreateDirectory(directory);
    var executable = Path.Combine(directory, "SoundKeeper.GUI.exe");
    await File.WriteAllBytesAsync(executable, []);
    if (published) await File.WriteAllTextAsync(Path.Combine(directory, StartupService.PublishedMarkerFileName), string.Empty);
    return executable;
}

static void SetStartupEntry(string keyPath, string command)
{
    using var key = Registry.CurrentUser.CreateSubKey(keyPath);
    key.SetValue(StartupService.EntryName, command);
}

static string? GetStartupEntry(string keyPath)
{
    using var key = Registry.CurrentUser.OpenSubKey(keyPath);
    return key?.GetValue(StartupService.EntryName) as string;
}

static async Task OutOfRangeSettingsEnumsFallback()
{
    await WithTemporaryDirectory(async directory =>
    {
        await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"),
            """{ "Enabled": true, "DeviceMode": "All", "SleepBehavior": 9, "Theme": -1 }""");
        var loaded = await new SettingsService(new AppLogger(directory), directory).LoadAsync();
        Equal(true, loaded.Enabled);
        Equal(DeviceMode.All, loaded.DeviceMode);
        Equal(SleepBehavior.Standard, loaded.SleepBehavior);
        Equal(AppTheme.System, loaded.Theme);
    });
}

static async Task ProcessStartInfoSafety()
{
    await WithTemporaryDirectory(async directory =>
    {
        var engineDirectory = Path.Combine(directory, "Engine");
        Directory.CreateDirectory(engineDirectory);
        var expected = Path.Combine(engineDirectory, "SoundKeeper64.exe");
        await File.WriteAllBytesAsync(expected, []);
        var service = new SoundKeeperEngineService(
            new EngineExecutableResolver(directory, Architecture.X64),
            new AppLogger(directory));
        var info = service.CreateStartInfo(new AppSettings());
        Equal(expected, info.FileName);
        Equal("primary fluctuate -f 50", info.Arguments);
        Equal(false, info.UseShellExecute);
        Equal(true, info.CreateNoWindow);
        Equal("kill", service.CreateStopInfo().Arguments);
    });
}

static async Task UpstreamCliContract()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    FileInfo? source = null;
    while (directory is not null)
    {
        var candidate = new FileInfo(Path.Combine(directory.FullName, "CSoundKeeper.cpp"));
        if (candidate.Exists)
        {
            source = candidate;
            break;
        }
        directory = directory.Parent;
    }

    if (source is null) throw new InvalidOperationException("CSoundKeeper.cpp not found from test output.");
    var text = await File.ReadAllTextAsync(source.FullName);
    True(text.Contains("SetDeviceType(KeepDeviceType::Primary)", StringComparison.Ordinal), "Primary is no longer the upstream default.");
    foreach (var token in new[] { "all", "marked", "analog", "digital", "selected", "device=", "kill", "remote", "nosleep", "sleepy", "sleep", "openonly", "zero", "fluctuate", "sine", "white", "brown", "pink" })
    {
        True(text.Contains($"\"{token}\"", StringComparison.OrdinalIgnoreCase), $"Missing upstream token: {token}");
    }
}

static async Task WithTemporaryDirectory(Func<string, Task> action)
{
    var directory = Path.Combine(Path.GetTempPath(), "SoundKeeper.GUI.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        await action(directory);
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static Task LogViewFormatting()
{
    var content = string.Join(Environment.NewLine,
        "2026-09-08T21:34:48.5721387+02:00 [INFO] Démarrage du GUI.",
        "2026-09-08T21:35:02.0000000+02:00 [ERROR] Erreur applicative",
        "System.InvalidOperationException: boom",
        "2026-09-09T08:00:00.0000000+02:00 [WARN] Arrêt inattendu du moteur Sound Keeper.");
    var culture = CultureInfo.GetCultureInfo("en-US");

    var all = LogViewFormatter.Format(content, LogLevelFilter.All, culture);
    Equal(3, all.EntryCount);
    True(all.DisplayText.Contains("21:34:48 INFO  Démarrage du GUI.", StringComparison.Ordinal), "The compact timestamp is missing.");
    True(all.DisplayText.Contains("September 8, 2026", StringComparison.Ordinal), "The date separator is missing.");
    True(all.DisplayText.Contains("System.InvalidOperationException: boom", StringComparison.Ordinal), "A continuation line was lost.");
    True(!all.DisplayText.Contains("2026-09-08T21:34:48", StringComparison.Ordinal), "The full ISO timestamp should not be displayed.");

    var errors = LogViewFormatter.Format(content, LogLevelFilter.Errors, culture);
    Equal(1, errors.EntryCount);
    True(errors.RawText.StartsWith("2026-09-08T21:35:02.0000000+02:00 [ERROR]", StringComparison.Ordinal)
        && errors.RawText.Contains("boom", StringComparison.Ordinal), "Copy must keep the full original entry.");
    Equal(2, LogViewFormatter.Format(content, LogLevelFilter.WarningsAndErrors, culture).EntryCount);
    Equal(0, LogViewFormatter.Format(string.Empty, LogLevelFilter.All, culture).EntryCount);
    return Task.CompletedTask;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void True(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}
