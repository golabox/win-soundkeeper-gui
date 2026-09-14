using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Globalization;
using SoundKeeper.GUI.Services;
using SoundKeeper.GUI.ViewModels;

namespace SoundKeeper.GUI;

public partial class App : Application
{
    private const string InstanceKey = "SoundKeeper.GUI.Main";
    private AppInstance? _mainInstance;

    public App()
    {
        var language = LocalizationService.ResolveLanguage(
            SettingsService.ReadSavedLanguage(),
            CultureInfo.CurrentUICulture.Name);
        LocalizationService.ApplyLanguage(language);
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    public static MainWindow? MainWindow { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var current = AppInstance.GetCurrent();
        _mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!_mainInstance.IsCurrent)
        {
            await _mainInstance.RedirectActivationToAsync(current.GetActivatedEventArgs());
            Exit();
            return;
        }

        _mainInstance.Activated += OnInstanceActivated;

        var logger = new AppLogger();
        var settings = new SettingsService(logger);
        var loadedSettings = await settings.LoadAsync();
        logger.Enabled = loadedSettings.LoggingEnabled;
        await logger.InfoAsync("Démarrage du GUI.");
        var engine = new SoundKeeperEngineService(new EngineExecutableResolver(), logger);
        var startup = new StartupService();
        var localization = new LocalizationService();
        var viewModel = new MainViewModel(settings, engine, startup, logger, localization, loadedSettings);

        try
        {
            MainWindow = new MainWindow(viewModel);
            MainWindow.Activate();
            await MainWindow.InitializeAsync(Environment.GetCommandLineArgs().Contains("--background", StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            await logger.ErrorAsync("Erreur au démarrage du GUI", exception);
            if (MainWindow is not null)
            {
                MainWindow.ShowError(exception.Message);
            }
            else
            {
                Exit();
            }
        }
    }

    private void OnInstanceActivated(object? sender, AppActivationArguments args)
    {
        var dispatcher = MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        dispatcher?.TryEnqueue(() => MainWindow?.RestoreAndActivate());
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MainWindow?.ShowError(e.Exception.Message);
    }

}
