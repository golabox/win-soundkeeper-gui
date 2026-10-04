using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using SoundKeeper.GUI.Models;
using SoundKeeper.GUI.Services;

namespace SoundKeeper.GUI.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly SoundKeeperEngineService _engine;
    private readonly AudioOutputService _audioOutputs;
    private readonly StartupService _startup;
    private readonly AppLogger _logger;
    private readonly LocalizationService _texts;
    private CancellationTokenSource? _updateCancellation;
    private CancellationTokenSource? _testCancellation;
    private Task? _testTask;
    private AppSettings _settings;
    private bool _initialized;
    private bool _isRunning;
    private bool _isBusy;
    private int? _engineProcessId;
    private DateTimeOffset? _engineStartedAt;
    private bool _hasObservedEngine;
    private bool _recoveryPending;
    private string _logText = string.Empty;
    private string _testSignalStatus = string.Empty;
    private string _clipboardStatus = string.Empty;
    private LogLevelFilter _logFilter;
    private LogView _logView;
    private bool _isTesting;
    private readonly RestartAttemptGuard _restartGuard = new();
    private IReadOnlyList<OutputDeviceRow> _outputRows = [];
    private bool _outputEnumerationFailed;

    public MainViewModel(
        SettingsService settingsService,
        SoundKeeperEngineService engine,
        AudioOutputService audioOutputs,
        StartupService startup,
        AppLogger logger,
        LocalizationService texts,
        AppSettings settings)
    {
        _settingsService = settingsService;
        _engine = engine;
        _audioOutputs = audioOutputs;
        _startup = startup;
        _logger = logger;
        _texts = texts;
        _settings = settings;

        List<ChoiceItem<DeviceMode>> deviceChoices =
        [
            new(DeviceMode.Primary, T("DevicePrimaryLabel", "Sortie Windows par défaut"), T("DevicePrimaryDescription", "Suit la sortie audio par défaut de Windows, même lorsqu’elle change.")),
            new(DeviceMode.All, T("DeviceAllLabel", "Toutes les sorties"), T("DeviceAllDescription", "Maintient actives toutes les sorties audio disponibles.")),
            new(DeviceMode.Selected, T("DeviceSelectedLabel", "Sélection personnalisée"), T("DeviceSelectedDescription", "Maintient actives uniquement les sorties cochées ci-dessous."))
        ];
        // Former engine modes stay listed only for a configuration that already uses them.
        if (settings.DeviceMode == DeviceMode.Digital)
            deviceChoices.Add(new(DeviceMode.Digital, T("DeviceDigitalLabel", "Sorties numériques"), T("DeviceDigitalDescription", "Sorties S/PDIF et HDMI.")));
        if (settings.DeviceMode == DeviceMode.Analog)
            deviceChoices.Add(new(DeviceMode.Analog, T("DeviceAnalogLabel", "Sorties analogiques"), T("DeviceAnalogDescription", "Toutes les sorties sauf S/PDIF et HDMI.")));
        if (settings.DeviceMode == DeviceMode.Marked)
            deviceChoices.Add(new(DeviceMode.Marked, T("DeviceMarkedLabel", "Périphériques marqués"), T("DeviceMarkedDescription", "Utilise les sorties dont le nom contient un point d’exclamation (!).")));
        DeviceChoices = deviceChoices;
        SignalChoices =
        [
            new(SignalMode.Fluctuate, T("SignalFluctuateLabel", "Impulsions inaudibles (recommandé)"), T("SignalFluctuateDescription", "Envoie de minuscules impulsions pour garder la sortie éveillée.")),
            new(SignalMode.Zero, T("SignalZeroLabel", "Silence numérique"), T("SignalZeroDescription", "Diffuse uniquement des zéros.")),
            new(SignalMode.OpenOnly, T("SignalOpenOnlyLabel", "Ouvrir uniquement"), T("SignalOpenOnlyDescription", "Ouvre la sortie sans diffuser de signal.")),
            new(SignalMode.Sine, T("SignalSineLabel", "Signal sinusoïdal"), T("SignalSineDescription", "Signal de test réglable ; peut devenir audible.")),
            new(SignalMode.White, T("SignalWhiteLabel", "Bruit blanc"), T("SignalWhiteDescription", "Bruit uniforme et clairement audible couvrant toutes les fréquences.")),
            new(SignalMode.Brown, T("SignalBrownLabel", "Bruit brun"), T("SignalBrownDescription", "Bruit audible plus doux et plus grave, riche en basses fréquences.")),
            new(SignalMode.Pink, T("SignalPinkLabel", "Bruit rose"), T("SignalPinkDescription", "Bruit audible équilibré par octave, souvent plus naturel que le bruit blanc."))
        ];
        SleepChoices =
        [
            new(SleepBehavior.Standard, T("SleepStandardLabel", "Veille uniquement (par défaut)"), T("SleepStandardDescription", "Le signal s’arrête pendant la mise en veille du PC et reprend au réveil. Sous Windows 11, il peut empêcher la mise en veille automatique.")),
            new(SleepBehavior.WhenLocked, T("SleepLockedLabel", "Au verrouillage de la session"), T("SleepLockedDescription", "Le signal s’arrête quand la session Windows est verrouillée (Win+L) et reprend au déverrouillage.")),
            new(SleepBehavior.WhenDisplayOff, T("SleepDisplayLabel", "Quand l’écran s’éteint"), T("SleepDisplayDescription", "Le signal s’arrête quand Windows éteint l’écran et reprend quand il se rallume. Aide le PC à se mettre en veille automatiquement.")),
            new(SleepBehavior.WhenLockedOrDisplayOff, T("SleepBothLabel", "Verrouillage ou écran éteint"), T("SleepBothDescription", "Le signal s’arrête quand la session est verrouillée ou que l’écran s’éteint, et reprend quand ce n’est plus le cas.")),
            new(SleepBehavior.NeverDetectSleep, T("SleepNeverLabel", "Jamais"), T("SleepNeverDescription", "Sound Keeper ne met jamais le signal en pause, même pendant la mise en veille. Peut empêcher la veille automatique et consommer plus de batterie."))
        ];
        ThemeChoices =
        [
            new(AppTheme.System, T("ThemeSystemLabel", "Système")),
            new(AppTheme.Light, T("ThemeLightLabel", "Clair")),
            new(AppTheme.Dark, T("ThemeDarkLabel", "Sombre"))
        ];
        LanguageChoices =
        [
            new(AppLanguage.French, "Français"),
            new(AppLanguage.English, "English"),
            new(AppLanguage.Spanish, "Español")
        ];

        ToggleEngineCommand = new AsyncRelayCommand(ToggleEngineAsync, ReportError);
        OpenAudioSettingsCommand = new RelayCommand(OpenAudioSettings);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        OpenSettingsFolderCommand = new RelayCommand(OpenSettingsFolder);
        OpenEngineFolderCommand = new RelayCommand(OpenEngineFolder);
        RefreshLogsCommand = new AsyncRelayCommand(RefreshLogsAsync, ReportError);
        CopyLogsCommand = new RelayCommand(CopyLogs);
        CopyDiagnosticCommand = new RelayCommand(CopyDiagnostic);
        CopyCommandPreviewCommand = new RelayCommand(() => CopyToClipboard(CommandPreview));
        TestSignalCommand = new RelayCommand(ToggleTestSignal);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? ErrorRaised;
    public event EventHandler<string>? NotificationRequested;
    public event EventHandler<AppTheme>? ThemeChanged;

    public IReadOnlyList<ChoiceItem<DeviceMode>> DeviceChoices { get; }
    public IReadOnlyList<ChoiceItem<SignalMode>> SignalChoices { get; }
    public IReadOnlyList<ChoiceItem<SleepBehavior>> SleepChoices { get; }
    public IReadOnlyList<ChoiceItem<AppTheme>> ThemeChoices { get; }
    public IReadOnlyList<ChoiceItem<AppLanguage>> LanguageChoices { get; }
    public ObservableCollection<OutputDeviceItem> OutputDevices { get; } = [];

    public ICommand ToggleEngineCommand { get; }
    public ICommand OpenAudioSettingsCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand OpenSettingsFolderCommand { get; }
    public ICommand OpenEngineFolderCommand { get; }
    public ICommand RefreshLogsCommand { get; }
    public ICommand CopyLogsCommand { get; }
    public ICommand CopyDiagnosticCommand { get; }
    public ICommand CopyCommandPreviewCommand { get; }
    public ICommand TestSignalCommand { get; }

    public string GetText(string key, string fallback) => T(key, fallback);

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusDetail));
                OnPropertyChanged(nameof(PrimaryActionText));
                OnPropertyChanged(nameof(DiagnosticText));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public string StatusText => IsRunning ? T("StatusRunning", "Sound Keeper actif") : T("StatusStopped", "Sound Keeper arrêté");
    public string StatusDetail => IsRunning
        ? $"{T("PidLabel", "PID")} {_engineProcessId?.ToString() ?? "?"} · {T("UptimeLabel", "Actif depuis")} {FormatUptime()}"
            + (FrequencyVisible ? $" · {T("FrequencyLabel", "Fréquence")} {FrequencyHz:g} Hz" : string.Empty)
        : T("StatusStoppedDetail", "Aucun signal de maintien n’est actuellement envoyé.");
    public string PrimaryActionText => IsRunning ? T("DisableButton", "Désactiver") : T("EnableButton", "Activer");
    public string GuiVersion => Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
    public string GuiArchitecture => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    public string AboutVersionText => string.Format(T("AboutVersionFormat", "Version {0} ({1})"), GuiVersion, GuiArchitecture);
    public string EngineVersion => _engine.EngineVersion ?? T("EngineUnavailable", "introuvable");
    public string EngineVersionText => $"Sound Keeper {EngineVersion}";
    public string SettingsPath => _settingsService.SettingsPath;
    public string EnginePathText => _engine.EnginePath ?? T("EngineUnavailable", "introuvable");
    public string CommandPreview => $"{_engine.EnginePath ?? _engine.GetType().Name} {CliArgumentBuilder.Build(_settings)}";
    public string DeviceDescription => DeviceMode == DeviceMode.Selected && _settings.SelectedDevices.Count == 0
        ? T("DeviceSelectedNoneDescription", "Aucune sortie cochée : Sound Keeper ne maintient aucune sortie active.")
        : DeviceChoices.FirstOrDefault(choice => choice.Value == DeviceMode)?.Description ?? string.Empty;
    public string SignalDescription => SignalChoices.First(choice => choice.Value == SignalMode).Description;
    public string SleepDescription => SleepChoices.First(choice => choice.Value == SleepBehavior).Description;
    public string FrequencyDescription => SignalMode switch
    {
        SignalMode.Fluctuate => string.Format(
            T("FluctuateFrequencyDescription", "{0:g} Hz règle la cadence du signal de maintien ({0:g} micro-impulsions par seconde), pas la fréquence d’échantillonnage du périphérique. La valeur recommandée du moteur suffit généralement et reste normalement inaudible."),
            FrequencyHz),
        SignalMode.Sine => string.Format(
            T("SineFrequencyDescription", "{0:g} Hz est la fréquence du signal, pas celle d’échantillonnage du périphérique. Entre environ 20 et 20 000 Hz, le son peut être entendu selon l’amplitude ; les valeurs recommandées du moteur suffisent généralement."),
            FrequencyHz),
        _ => string.Empty
    };
    public string TestSignalDescription => SignalMode is SignalMode.Sine or SignalMode.White or SignalMode.Brown or SignalMode.Pink
        ? T("TestSelectedSignalDescription", "Joue le signal sélectionné pendant 3 secondes avec un niveau audible, puis restaure votre configuration.")
        : T("TestToneDescription", "Le mode sélectionné est normalement inaudible : le test joue donc une sinusoïde audible pendant 3 secondes, puis restaure votre configuration.");
    public string TestSignalStatus
    {
        get => _testSignalStatus;
        private set => SetField(ref _testSignalStatus, value);
    }
    public bool IsTesting
    {
        get => _isTesting;
        private set
        {
            if (SetField(ref _isTesting, value)) OnPropertyChanged(nameof(TestSignalActionText));
        }
    }
    public string TestSignalActionText => IsTesting
        ? T("StopTestSignalButton", "Arrêter")
        : T("StartTestSignalButton", "Tester");
    public string ClipboardStatus
    {
        get => _clipboardStatus;
        private set => SetField(ref _clipboardStatus, value);
    }
    public bool FrequencyVisible => SignalMode is SignalMode.Fluctuate or SignalMode.Sine;
    public bool AmplitudeVisible => SignalMode is SignalMode.Sine or SignalMode.White or SignalMode.Brown or SignalMode.Pink;
    public bool TimingVisible => AmplitudeVisible;
    public Visibility FrequencyVisibility => FrequencyVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AmplitudeVisibility => AmplitudeVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TimingVisibility => TimingVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SelectedDevicesVisibility => DeviceMode == DeviceMode.Selected ? Visibility.Visible : Visibility.Collapsed;
    public string EffectiveLanguage => _settings.Language ?? _texts.CurrentLanguage;
    public string LanguageRestartMessage => T("LanguageRestartMessage", "Le changement de langue sera appliqué au prochain démarrage de Sound Keeper GUI.");
    public bool IsLanguageRestartPending => LocalizationService.Normalize(EffectiveLanguage) != _texts.CurrentLanguage;
    public string DiagnosticText => BuildDiagnosticText();
    public string LogText
    {
        get => _logText;
        private set
        {
            if (SetField(ref _logText, value)) UpdateLogView();
        }
    }
    public LogLevelFilter LogFilter
    {
        get => _logFilter;
        set
        {
            if (!SetField(ref _logFilter, value)) return;
            OnPropertyChanged(nameof(LogFilterIndex));
            UpdateLogView();
        }
    }
    public int LogFilterIndex
    {
        get => (int)LogFilter;
        set { if (Enum.IsDefined(typeof(LogLevelFilter), value)) LogFilter = (LogLevelFilter)value; }
    }
    public string LogDisplayText => _logView.EntryCount == 0
        ? T("LogsEmpty", "Aucune entrée de journal à afficher.")
        : _logView.DisplayText;

    public int DeviceModeIndex
    {
        get => IndexOf(DeviceChoices, DeviceMode);
        set { if (value >= 0 && value < DeviceChoices.Count) DeviceMode = DeviceChoices[value].Value; }
    }

    public int SignalModeIndex
    {
        get => IndexOf(SignalChoices, SignalMode);
        set { if (value >= 0 && value < SignalChoices.Count) SignalMode = SignalChoices[value].Value; }
    }

    public int SleepBehaviorIndex
    {
        get => IndexOf(SleepChoices, SleepBehavior);
        set { if (value >= 0 && value < SleepChoices.Count) SleepBehavior = SleepChoices[value].Value; }
    }

    public int ThemeIndex
    {
        get => IndexOf(ThemeChoices, Theme);
        set { if (value >= 0 && value < ThemeChoices.Count) Theme = ThemeChoices[value].Value; }
    }

    public int LanguageIndex
    {
        get => IndexOf(LanguageChoices, EffectiveLanguage switch
        {
            LocalizationService.French => AppLanguage.French,
            LocalizationService.Spanish => AppLanguage.Spanish,
            _ => AppLanguage.English
        });
        set
        {
            if (value < 0 || value >= LanguageChoices.Count) return;
            var language = LanguageChoices[value].Value switch
            {
                AppLanguage.French => LocalizationService.French,
                AppLanguage.Spanish => LocalizationService.Spanish,
                _ => LocalizationService.English
            };
            if (string.Equals(_settings.Language, language, StringComparison.OrdinalIgnoreCase)) return;
            _settings.Language = language;
            NotifySettingChanged(false, nameof(LanguageIndex), nameof(EffectiveLanguage), nameof(DiagnosticText));
            OnPropertyChanged(nameof(IsLanguageRestartPending));
        }
    }

    public DeviceMode DeviceMode
    {
        get => _settings.DeviceMode;
        set
        {
            if (_settings.DeviceMode == value) return;
            _settings.DeviceMode = value;
            NotifySettingChanged(true, nameof(DeviceMode), nameof(DeviceModeIndex), nameof(DeviceDescription), nameof(SelectedDevicesVisibility));
            if (value == DeviceMode.Selected) RefreshOutputDevices();
        }
    }

    public SignalMode SignalMode
    {
        get => _settings.SignalMode;
        set
        {
            if (_settings.SignalMode == value) return;
            _settings.SignalMode = value;
            if (value == SignalMode.Fluctuate) _settings.FrequencyHz = 50;
            if (value == SignalMode.Sine) _settings.FrequencyHz = 1;
            if (value is SignalMode.White or SignalMode.Brown or SignalMode.Pink) _settings.AmplitudePercent = 0.1;
            NotifySettingChanged(true, nameof(SignalMode), nameof(SignalModeIndex), nameof(SignalDescription), nameof(FrequencyDescription), nameof(TestSignalDescription), nameof(FrequencyVisible), nameof(AmplitudeVisible), nameof(TimingVisible), nameof(FrequencyVisibility), nameof(AmplitudeVisibility), nameof(TimingVisibility), nameof(FrequencyHz), nameof(AmplitudePercent));
        }
    }

    public SleepBehavior SleepBehavior
    {
        get => _settings.SleepBehavior;
        set
        {
            if (_settings.SleepBehavior == value) return;
            _settings.SleepBehavior = value;
            NotifySettingChanged(true, nameof(SleepBehavior), nameof(SleepBehaviorIndex), nameof(SleepDescription));
        }
    }

    public AppTheme Theme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value) return;
            _settings.Theme = value;
            NotifySettingChanged(false, nameof(Theme), nameof(ThemeIndex), nameof(DiagnosticText));
            ThemeChanged?.Invoke(this, value);
        }
    }

    public double FrequencyHz
    {
        get => _settings.FrequencyHz;
        set
        {
            SetNumber(value, 0, 96000, current => _settings.FrequencyHz = current, _settings.FrequencyHz, nameof(FrequencyHz));
            OnPropertyChanged(nameof(FrequencyDescription));
        }
    }

    public double AmplitudePercent
    {
        get => _settings.AmplitudePercent;
        set => SetNumber(value, 0, 100, current => _settings.AmplitudePercent = current, _settings.AmplitudePercent, nameof(AmplitudePercent));
    }

    public double PlaySeconds
    {
        get => _settings.PlaySeconds;
        set => SetNumber(value, 0, 86400, current => _settings.PlaySeconds = current, _settings.PlaySeconds, nameof(PlaySeconds));
    }

    public double WaitSeconds
    {
        get => _settings.WaitSeconds;
        set => SetNumber(value, 0, 86400, current => _settings.WaitSeconds = current, _settings.WaitSeconds, nameof(WaitSeconds));
    }

    public double FadeSeconds
    {
        get => _settings.FadeSeconds;
        set => SetNumber(value, 0, 60, current => _settings.FadeSeconds = current, _settings.FadeSeconds, nameof(FadeSeconds));
    }

    public bool AllowRemoteAudio
    {
        get => _settings.AllowRemoteAudio;
        set => SetBoolean(value, current => _settings.AllowRemoteAudio = current, _settings.AllowRemoteAudio, true, nameof(AllowRemoteAudio));
    }

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            if (_settings.StartWithWindows == value) return;
            try
            {
                _startup.SetEnabled(value);
                _settings.StartWithWindows = value;
                NotifySettingChanged(false, nameof(StartWithWindows));
            }
            catch (Exception exception)
            {
                ReportError(exception);
            }
        }
    }

    public bool StartMinimized
    {
        get => _settings.StartMinimized;
        set => SetBoolean(value, current => _settings.StartMinimized = current, _settings.StartMinimized, false, nameof(StartMinimized));
    }

    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set => SetBoolean(value, current => _settings.MinimizeToTray = current, _settings.MinimizeToTray, false, nameof(MinimizeToTray));
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set => SetBoolean(value, current => _settings.CloseToTray = current, _settings.CloseToTray, false, nameof(CloseToTray));
    }

    public bool LoggingEnabled
    {
        get => _settings.LoggingEnabled;
        set
        {
            if (_settings.LoggingEnabled == value) return;
            _settings.LoggingEnabled = value;
            _logger.Enabled = value;
            NotifySettingChanged(false, nameof(LoggingEnabled), nameof(DiagnosticText));
        }
    }

    public bool AutoRestartEngine
    {
        get => _settings.AutoRestartEngine;
        set => SetBoolean(value, current => _settings.AutoRestartEngine = current, _settings.AutoRestartEngine, false, nameof(AutoRestartEngine));
    }

    public string AdditionalArguments
    {
        get => _settings.AdditionalArguments;
        set
        {
            value ??= string.Empty;
            if (_settings.AdditionalArguments == value) return;
            _settings.AdditionalArguments = value;
            NotifySettingChanged(true, nameof(AdditionalArguments));
        }
    }

    public async Task InitializeAsync()
    {
        _initialized = true;
        RaiseAllSettings();
        ThemeChanged?.Invoke(this, Theme);
        if (DeviceMode == DeviceMode.Selected) RefreshOutputDevices();

        // Windows startup belongs to the published build: it takes over or repairs the entry at launch,
        // while a development build (bin\...) leaves it untouched.
        try
        {
            _startup.Synchronize(_settings.StartWithWindows);
        }
        catch (Exception exception)
        {
            await _logger.WarningAsync($"Démarrage avec Windows non synchronisé: {exception.Message}");
        }

        if (_settings.Enabled)
        {
            try
            {
                IsBusy = true;
                await _engine.StartAsync(_settings);
            }
            catch (Exception exception)
            {
                _settings.Enabled = false;
                await _settingsService.SaveAsync(_settings);
                ReportError(exception);
            }
            finally
            {
                IsBusy = false;
            }
        }

        RefreshEngineState();
        await RefreshLogsAsync();
        _hasObservedEngine = true;
    }

    public void RefreshEngineState()
    {
        var snapshot = _engine.GetSnapshot();
        _engineProcessId = snapshot.ProcessId;
        _engineStartedAt = snapshot.StartedAt;
        IsRunning = snapshot.IsRunning;
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(DiagnosticText));
    }

    public async Task MonitorEngineAsync()
    {
        var wasRunning = IsRunning;
        RefreshEngineState();
        if (!_hasObservedEngine)
        {
            _hasObservedEngine = true;
            return;
        }

        if (wasRunning && !IsRunning && _settings.Enabled && !IsBusy)
        {
            _recoveryPending = true;
            await _logger.WarningAsync("Arrêt inattendu du moteur Sound Keeper.");
            NotificationRequested?.Invoke(this, T("UnexpectedStopNotification", "Sound Keeper s’est arrêté de manière inattendue."));
        }

        if (_recoveryPending && _settings.Enabled && _settings.AutoRestartEngine && !IsBusy)
        {
            await TryAutomaticRestartAsync();
        }
    }

    public Task ToggleEngineAsync() => SetEngineEnabledAsync(!IsRunning);

    private void ToggleTestSignal()
    {
        if (_testCancellation is not null)
        {
            TestSignalStatus = T("TestSignalStopping", "Arrêt du test…");
            _testCancellation.Cancel();
            return;
        }

        _testTask = RunTestSignalAsync();
    }

    private async Task RunTestSignalAsync()
    {
        CancelPendingUpdate();
        var restoreEngine = IsRunning;
        var testSettings = AudioTestSettingsFactory.Create(_settings);
        var cancellation = new CancellationTokenSource();
        _testCancellation = cancellation;
        IsBusy = true;
        IsTesting = true;
        TestSignalStatus = T("TestSignalRunning", "Test audible en cours pendant 3 secondes…");

        try
        {
            await _logger.InfoAsync($"Test sonore: {CliArgumentBuilder.Build(testSettings)}");
            await _engine.StartAsync(testSettings);
            RefreshEngineState();
            await Task.Delay(TimeSpan.FromSeconds(3), cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            var wasCancelled = cancellation.IsCancellationRequested;
            try
            {
                if (restoreEngine)
                {
                    await _engine.StartAsync(_settings);
                }
                else
                {
                    await _engine.StopAsync();
                }
            }
            catch (Exception exception)
            {
                ReportError(exception);
            }
            finally
            {
                RefreshEngineState();
                IsBusy = false;
                IsTesting = false;
                if (ReferenceEquals(_testCancellation, cancellation)) _testCancellation = null;
                _testTask = null;
                cancellation.Dispose();
            }

            TestSignalStatus = wasCancelled
                ? T("TestSignalStopped", "Test arrêté. Votre configuration précédente a été restaurée.")
                : T("TestSignalComplete", "Test terminé. Votre configuration précédente a été restaurée.");
        }
    }

    public async Task ResetSettingsAsync()
    {
        CancelPendingUpdate();
        _testCancellation?.Cancel();
        if (_testTask is { } testTask) await testTask;
        if (IsRunning)
        {
            await _engine.StopAsync();
        }

        _startup.SetEnabled(false);
        _settings = new AppSettings();
        OutputDevices.Clear();
        _outputRows = [];
        _logger.Enabled = _settings.LoggingEnabled;
        TestSignalStatus = string.Empty;
        await _settingsService.SaveAsync(_settings);
        RaiseAllSettings();
        ThemeChanged?.Invoke(this, Theme);
        RefreshEngineState();
        await _logger.InfoAsync("Paramètres réinitialisés.");
    }

    public async Task PrepareForRestartAsync()
    {
        CancelPendingUpdate();
        _testCancellation?.Cancel();
        if (_testTask is { } testTask) await testTask;
        await _settingsService.SaveAsync(_settings);
        await _logger.InfoAsync("Redémarrage du GUI demandé.");
    }

    public async Task SetEngineEnabledAsync(bool enabled)
    {
        IsBusy = true;
        try
        {
            if (enabled)
            {
                await _engine.StartAsync(_settings);
                _recoveryPending = false;
                _restartGuard.Reset();
            }
            else
            {
                _settings.Enabled = false;
                await _engine.StopAsync();
            }

            _settings.Enabled = enabled;
            await _settingsService.SaveAsync(_settings);
            await _logger.InfoAsync($"Configuration du moteur modifiée: {(enabled ? "activé" : "désactivé")}.");
        }
        finally
        {
            RefreshEngineState();
            IsBusy = false;
        }
    }

    // Never throws: quitting must not be blocked by a disk or engine error, which is logged instead.
    public async Task StopForExitAsync()
    {
        var settingsChanged = _updateCancellation is not null;
        CancelPendingUpdate();
        _testCancellation?.Cancel();
        if (_testTask is { } testTask) await testTask;

        try
        {
            // Saved again so that a change still inside its debounce delay is not lost.
            if (settingsChanged) await _settingsService.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            await _logger.ErrorAsync("Paramètres non sauvegardés à la fermeture", exception);
        }

        try
        {
            if (IsRunning)
            {
                await _engine.StopAsync();
                RefreshEngineState();
            }
        }
        catch (Exception exception)
        {
            await _logger.ErrorAsync("Arrêt du moteur impossible à la fermeture", exception);
        }
    }

    public Task LogTrayEventAsync(string message) => _logger.InfoAsync(message);

    public Task LogGuiClosingAsync() => _logger.InfoAsync("Fermeture du GUI.");

    private void NotifySettingChanged(bool restartEngine, params string[] properties)
    {
        foreach (var property in properties)
        {
            OnPropertyChanged(property);
        }
        OnPropertyChanged(nameof(CommandPreview));
        if (_initialized)
        {
            QueueUpdate(restartEngine);
        }
    }

    private void SetNumber(double value, double minimum, double maximum, Action<double> setter, double existing, string property)
    {
        var normalized = double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum;
        if (Math.Abs(existing - normalized) < 0.0001) return;
        setter(normalized);
        NotifySettingChanged(true, property);
    }

    private void SetBoolean(bool value, Action<bool> setter, bool existing, bool restartEngine, string property)
    {
        if (existing == value) return;
        setter(value);
        NotifySettingChanged(restartEngine, property);
    }

    private async void QueueUpdate(bool restartEngine)
    {
        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
        _updateCancellation = new CancellationTokenSource();
        var token = _updateCancellation.Token;
        try
        {
            await Task.Delay(350, token);
            await _settingsService.SaveAsync(_settings);
            await _logger.InfoAsync("Paramètres utilisateur modifiés.");
            if (restartEngine && _settings.Enabled)
            {
                await _engine.RestartAsync(_settings);
                RefreshEngineState();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    private void RaiseAllSettings()
    {
        var properties = new[]
        {
            nameof(DeviceMode), nameof(DeviceModeIndex), nameof(DeviceDescription), nameof(SelectedDevicesVisibility), nameof(SignalMode), nameof(SignalModeIndex), nameof(SignalDescription),
            nameof(SleepBehavior), nameof(SleepBehaviorIndex), nameof(SleepDescription), nameof(Theme), nameof(ThemeIndex), nameof(FrequencyHz), nameof(FrequencyDescription),
            nameof(AmplitudePercent), nameof(PlaySeconds), nameof(WaitSeconds), nameof(FadeSeconds),
            nameof(AllowRemoteAudio), nameof(StartWithWindows), nameof(StartMinimized), nameof(MinimizeToTray),
            nameof(CloseToTray), nameof(AdditionalArguments), nameof(FrequencyVisible), nameof(AmplitudeVisible),
            nameof(TimingVisible), nameof(FrequencyVisibility), nameof(AmplitudeVisibility), nameof(TimingVisibility),
            nameof(CommandPreview), nameof(EngineVersion), nameof(EngineVersionText), nameof(EnginePathText), nameof(AboutVersionText),
            nameof(LanguageIndex), nameof(EffectiveLanguage), nameof(IsLanguageRestartPending),
            nameof(LoggingEnabled), nameof(AutoRestartEngine), nameof(DiagnosticText), nameof(GuiArchitecture),
            nameof(LogText), nameof(LogDisplayText), nameof(LogFilterIndex), nameof(TestSignalDescription), nameof(TestSignalStatus), nameof(IsTesting), nameof(TestSignalActionText), nameof(ClipboardStatus)
        };
        foreach (var property in properties) OnPropertyChanged(property);
    }

    private void OpenAudioSettings() => Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true })?.Dispose();

    private void OpenLogFolder() => OpenFolder(_logger.LogDirectory);

    private void OpenSettingsFolder()
    {
        Directory.CreateDirectory(_settingsService.StorageDirectory);
        OpenFolder(_settingsService.StorageDirectory);
    }

    private void OpenEngineFolder()
    {
        if (Path.GetDirectoryName(_engine.EnginePath) is { Length: > 0 } directory) OpenFolder(directory);
    }

    private static void OpenFolder(string directory) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true })?.Dispose();

    public async Task ClearLogsAsync()
    {
        await _logger.ClearAllLogsAsync();
        await RefreshLogsAsync();
    }

    public async Task RefreshLogsAsync()
    {
        LogText = await _logger.ReadTailAsync();
    }

    private void UpdateLogView()
    {
        _logView = LogViewFormatter.Format(_logText, _logFilter, CultureInfo.GetCultureInfo(_texts.CurrentLanguage));
        OnPropertyChanged(nameof(LogDisplayText));
    }

    // Copies the entries shown by the current filter, with their full ISO timestamps.
    private void CopyLogs()
    {
        CopyToClipboard(_logView.RawText);
    }

    private void CopyDiagnostic()
    {
        CopyToClipboard(DiagnosticText);
    }

    private void CopyToClipboard(string? text)
    {
        // Clipboard.SetText rejects empty strings.
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    System.Windows.Forms.Clipboard.SetText(text);
                    // Reset first so that copying twice still notifies the view.
                    ClipboardStatus = string.Empty;
                    ClipboardStatus = T("ClipboardCopySuccess", "Informations copiées dans le presse-papiers.");
                    return;
                }
                catch (System.Runtime.InteropServices.ExternalException) when (attempt < 4)
                {
                    Thread.Sleep(30);
                }
            }
        }
        catch (Exception exception)
        {
            ReportError(new InvalidOperationException(
                T("ClipboardCopyError", "Impossible d’accéder au presse-papiers. Réessayez dans un instant."),
                exception));
        }
    }

    // Simple hotplug handling: called at startup, when the custom selection opens and by the window's status timer.
    public void RefreshOutputDevices()
    {
        IReadOnlyList<AudioOutput> outputs;
        try
        {
            outputs = _audioOutputs.GetActiveOutputs();
            _outputEnumerationFailed = false;
        }
        catch (COMException exception)
        {
            if (!_outputEnumerationFailed) _ = _logger.WarningAsync($"Sorties audio Windows illisibles: {exception.Message}");
            _outputEnumerationFailed = true;
            return;
        }

        if (OutputDeviceCatalog.UpdateNames(_settings.SelectedDevices, outputs))
        {
            NotifySettingChanged(false, nameof(DiagnosticText));
        }

        var rows = OutputDeviceCatalog.BuildRows(outputs, _settings.SelectedDevices);
        if (rows.SequenceEqual(_outputRows)) return;
        _outputRows = rows;
        OutputDevices.Clear();
        foreach (var row in rows)
        {
            var name = row.Name.Length > 0 ? row.Name : T("DeviceUnnamed", "Sortie audio");
            OutputDevices.Add(new OutputDeviceItem(row, name, T("DeviceUnavailable", "Indisponible"), OnOutputSelectionChanged));
        }
    }

    private void OnOutputSelectionChanged(OutputDeviceItem item)
    {
        _settings.SelectedDevices.RemoveAll(device => OutputDeviceCatalog.SameId(device.Id, item.Id));
        if (item.IsSelected) _settings.SelectedDevices.Add(new OutputDeviceSelection { Id = item.Id, Name = item.Name });
        // Keeps the displayed rows as they are; the next refresh only drops an absent output that was just unchecked.
        _outputRows = _outputRows.Select(row => OutputDeviceCatalog.SameId(row.Id, item.Id) ? row with { IsSelected = item.IsSelected } : row).ToList();
        NotifySettingChanged(true, nameof(DeviceDescription), nameof(DiagnosticText));
    }

    private string BuildDiagnosticText()
    {
        var yes = T("Yes", "Oui");
        var no = T("No", "Non");
        static string Line(string label, string value) => $"  {label,-18}{value}";
        // Two groups (Application / Engine); monospace keeps the values aligned.
        return string.Join(Environment.NewLine,
            "Application",
            Line("Sound Keeper GUI", $"{GuiVersion} ({GuiArchitecture})"),
            Line("Windows", System.Runtime.InteropServices.RuntimeInformation.OSDescription),
            Line("Language", EffectiveLanguage),
            Line("Theme", Theme.ToString()),
            Line("Start minimized", StartMinimized ? yes : no),
            Line("Minimize to tray", MinimizeToTray ? yes : no),
            Line("Close to tray", CloseToTray ? yes : no),
            Line("Logging", LoggingEnabled ? "Enabled" : "Disabled"),
            string.Empty,
            "Engine",
            Line("Version", EngineVersion),
            Line("State", IsRunning ? "Running" : "Stopped"),
            Line("Outputs", DeviceMode == DeviceMode.Selected
                ? $"Selected ({_settings.SelectedDevices.Count}) {string.Join(", ", _settings.SelectedDevices.Select(device => device.Name))}"
                : DeviceMode.ToString()),
            Line("PID", _engineProcessId?.ToString() ?? "N/A"),
            Line("Frequency", FrequencyVisible ? $"{FrequencyHz:g} Hz" : "N/A"),
            Line("Path", _engine.EnginePath ?? "N/A"));
    }

    private string FormatUptime()
    {
        if (_engineStartedAt is null) return T("Unknown", "inconnue");
        var elapsed = DateTimeOffset.Now - _engineStartedAt.Value;
        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours} h {elapsed.Minutes:D2} min"
            : $"{Math.Max(0, elapsed.Minutes)} min {Math.Max(0, elapsed.Seconds):D2} s";
    }

    private async Task TryAutomaticRestartAsync()
    {
        if (!_restartGuard.TryRegister(DateTimeOffset.Now))
        {
            _recoveryPending = false;
            _settings.Enabled = false;
            await _settingsService.SaveAsync(_settings);
            await _logger.WarningAsync("Redémarrage automatique suspendu après 3 tentatives en une minute.");
            NotificationRequested?.Invoke(this, T("CrashLoopNotification", "Le redémarrage automatique a été suspendu. Ouvrez l’application pour réessayer."));
            return;
        }

        IsBusy = true;
        try
        {
            await _logger.InfoAsync("Tentative de redémarrage automatique du moteur.");
            await _engine.StartAsync(_settings);
            RefreshEngineState();
            _recoveryPending = !IsRunning;
        }
        catch (Exception exception)
        {
            await _logger.ErrorAsync("Échec du redémarrage automatique", exception);
            NotificationRequested?.Invoke(this, T("RestartFailedNotification", "Impossible de redémarrer Sound Keeper automatiquement."));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string T(string key, string fallback) => _texts.Get(key, fallback);

    private static int IndexOf<T>(IReadOnlyList<ChoiceItem<T>> choices, T value) where T : struct, Enum
    {
        for (var index = 0; index < choices.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(choices[index].Value, value)) return index;
        }
        return 0;
    }

    private void ReportError(Exception exception)
    {
        _ = _logger.ErrorAsync("Erreur applicative", exception);
        ErrorRaised?.Invoke(this, exception.Message);
        NotificationRequested?.Invoke(this, exception.Message);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        _testCancellation?.Cancel();
        CancelPendingUpdate();
    }

    private void CancelPendingUpdate()
    {
        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
        _updateCancellation = null;
    }
}
