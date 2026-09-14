using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppLifecycle;
using SoundKeeper.GUI.Models;
using SoundKeeper.GUI.Services;
using SoundKeeper.GUI.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace SoundKeeper.GUI;

public sealed partial class MainWindow : Window
{
    private readonly IntPtr _windowHandle;
    private readonly AppWindow _appWindow;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    // SettingRow controls that move under their label in Compact (Select, TextInput, Action).
    private readonly FrameworkElement[] _stackingControls;
    private readonly Dictionary<FrameworkElement, (double Width, HorizontalAlignment Alignment, Thickness Margin)> _wideLayout = [];
    private TrayIconService? _trayIcon;
    private bool _isExiting;
    private bool _isInitialized;
    private bool _isTimerTicking;
    private bool? _compact;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _stackingControls =
        [
            EngineActionHost, DeviceModeCombo, SignalModeCombo, FrequencyBox, AmplitudeBox, FadeBox, PlayBox, WaitBox, TestSignalButton,
            SleepCombo, LanguageCombo, ThemeCombo, RestartApplicationButton, ResetSettingsButton,
            AdditionalArgumentsBox, CopyCommandButton, OpenSettingsFolderButton, OpenEngineFolderButton, ViewInAboutButton,
            CopyDiagnosticButton, LogViewOptions
        ];
        ApplyStaticLocalization();
        MainNavigation.SelectionChanged += MainNavigation_SelectionChanged;
        MainNavigation.DisplayModeChanged += (_, _) => ApplyLayout();
        // Golabox BpCompact, measured on the width the pages really get rather than an AdaptiveTrigger.
        PagesHost.SizeChanged += (_, _) => ApplyLayout();
        RootGrid.ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplyTitleBarTheme);
        RootGrid.Loaded += (_, _) =>
        {
            RootGrid.XamlRoot.Changed += (_, _) => UpdateMinimumSize();
            UpdateMinimumSize();
            ApplyTitleBarTheme();
        };

        _windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        // Initial size in epx (Wide layout), clamped to the work area.
        var scale = GetDpiForWindow(_windowHandle) / 96d;
        var workArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest).WorkArea;
        _appWindow.Resize(new SizeInt32(Math.Min((int)(1040 * scale), workArea.Width), Math.Min((int)(780 * scale), workArea.Height)));
        _appWindow.Closing += OnWindowClosing;
        _appWindow.Changed += OnAppWindowChanged;

        ViewModel.ErrorRaised += (_, message) => ShowError(message);
        ViewModel.NotificationRequested += (_, message) => _trayIcon?.ShowNotification(message);
        ViewModel.ThemeChanged += (_, theme) => ApplyTheme(theme);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        _statusTimer.Tick += OnStatusTimerTick;
    }

    public MainViewModel ViewModel { get; }

    private void ApplyStaticLocalization()
    {
        foreach (var item in new[] { GeneralNavigationItem, AdvancedNavigationItem, LogsNavigationItem, AboutNavigationItem })
        {
            // String content: NavigationViewItem shows its native ToolTip in the Compact rail.
            item.Content = L($"{item.Name}.Content", item.Content?.ToString() ?? string.Empty);
        }

        foreach (var toggle in new[] { StartupToggle, StartMinimizedToggle, MinimizeTrayToggle, CloseTrayToggle, AutoRestartToggle, RemoteToggle })
        {
            toggle.OnContent = L("ToggleOn", "Activé");
            toggle.OffContent = L("ToggleOff", "Désactivé");
        }

        // Général
        GeneralPageTitle.Text = L("GeneralPageTitle.Text", "Général");
        RestartNowButton.Content = L("RestartNowButton.Content", "Redémarrer maintenant");
        EngineSectionTitle.Text = L("EngineLabel", "Moteur");
        DevicesTitle.Text = L("DevicesTitle.Text", "Périphériques");
        DeviceModeLabel.Text = L("DeviceModeCombo.Header", "Sorties audio");
        OpenAudioLabel.Text = L("OpenAudioText.Text", "Paramètres Son de Windows");
        OpenAudioLinkText.Text = L("OpenLinkText", "Ouvrir");
        AutomationProperties.SetName(OpenAudioLink, L("OpenAudioLink.Name", "Ouvrir les paramètres Son de Windows"));
        SignalTitle.Text = L("SignalTitle.Text", "Mode de fonctionnement");
        AudibleWarning.Title = L("AudibleWarning.Title", "Prudence");
        AudibleWarning.Message = L("AudibleWarning.Message", "Une fréquence ou une amplitude élevée peut produire un son audible.");
        SignalModeLabel.Text = L("SignalModeCombo.Header", "Signal envoyé");
        FrequencyLabel.Text = L("FrequencyBox.Header", "Fréquence (Hz)");
        AmplitudeLabel.Text = L("AmplitudeBox.Header", "Amplitude (%)");
        FadeLabel.Text = L("FadeBox.Header", "Fondu (s)");
        PlayLabel.Text = L("PlayBox.Header", "Durée du signal (s, 0 = continu)");
        WaitLabel.Text = L("WaitBox.Header", "Pause entre signaux (s)");
        TestSignalLabel.Text = L("TestSignalLabel", "Tester le signal");
        SleepTitle.Text = L("SleepTitle.Text", "Veille et verrouillage");
        SleepLabel.Text = L("SleepCombo.Header", "Quand suspendre le moteur");
        LanguageTitle.Text = L("LanguageTitle.Text", "Langue et apparence");
        LanguageLabel.Text = L("LanguageCombo.Header", "Langue de l’application");
        ThemeLabel.Text = L("ThemeCombo.Header", "Thème");
        WindowsTitle.Text = L("WindowsTitle.Text", "Comportement Windows");
        StartupLabel.Text = L("StartupToggle.Header", "Lancer au démarrage de Windows");
        StartMinimizedLabel.Text = L("StartMinimizedToggle.Header", "Démarrer minimisé");
        StartMinimizedDescription.Text = L("StartMinimizedDescription", "S’applique aux lancements manuels : au démarrage de Windows, l’application démarre toujours minimisée.");
        MinimizeTrayLabel.Text = L("MinimizeTrayToggle.Header", "Réduire dans la zone de notification");
        CloseTrayLabel.Text = L("CloseTrayToggle.Header", "Fermer vers la zone de notification");
        CloseTrayDescription.Text = L("CloseTrayDescription", "La croix masque la fenêtre au lieu de quitter l’application.");
        MonitoringTitle.Text = L("MonitoringTitle.Text", "Surveillance");
        AutoRestartLabel.Text = L("AutoRestartToggle.Header", "Redémarrer automatiquement Sound Keeper en cas d’arrêt inattendu");
        AutoRestartDescription.Text = L("AutoRestartDescription", "Au plus 3 tentatives par minute ; au-delà, la relance automatique est suspendue.");
        MaintenanceTitle.Text = L("MaintenanceTitle", "Maintenance");
        RestartApplicationLabel.Text = L("RestartApplicationButton.Content", "Redémarrer l’application");
        RestartApplicationButton.Content = L("RestartButton.Content", "Redémarrer");
        ResetSettingsLabel.Text = L("ResetSettingsLabel", "Réinitialiser les paramètres");
        ResetSettingsDescription.Text = L("ResetSettingsDescription", "Revient aux réglages d’origine, puis redémarre. Une confirmation est demandée.");
        ResetSettingsButton.Content = L("ResetSettingsButton.Content", "Réinitialiser");

        // Avancé
        AdvancedPageTitle.Text = L("AdvancedPageTitle.Text", "Avancé");
        AdvancedPageIntro.Text = L("AdvancedPageIntro.Text", "Options techniques et informations utiles pour le dépannage.");
        EngineAdvancedTitle.Text = L("EngineAdvancedTitle.Text", "Options du moteur");
        RemoteLabel.Text = L("RemoteToggle.Header", "Inclure l’audio Bureau à distance");
        RemoteDescription.Text = L("RemoteDescription", "Maintient aussi les sorties audio des sessions Bureau à distance.");
        AdditionalArgumentsLabel.Text = L("AdditionalArgumentsBox.Header", "Arguments supplémentaires");
        AdditionalArgumentsDescription.Text = L("AdditionalArgumentsDescription", "Ajoutés à la fin de la commande. Vérifiez le résultat ci-dessous.");
        AdditionalArgumentsBox.PlaceholderText = L("AdditionalArgumentsBox.PlaceholderText", "Exemple : option-future");
        EffectiveCommandTitle.Text = L("EffectiveCommandTitle.Text", "Commande effective");
        EffectiveCommandIntro.Text = L("EffectiveCommandIntro.Text", "Générée à partir des réglages ci-dessus. Lecture seule.");
        CopyCommandText.Text = L("CopyButton", "Copier");
        FilesTitle.Text = L("FilesTitle", "Fichiers");
        SettingsFileLabel.Text = L("SettingsFileLabel", "Fichier de configuration");
        EngineFileLabel.Text = L("EngineFileLabel", "Exécutable du moteur");
        OpenSettingsFolderButton.Content = L("OpenFolderButton", "Ouvrir le dossier");
        OpenEngineFolderButton.Content = L("OpenFolderButton", "Ouvrir le dossier");
        SystemInfoLabel.Text = L("SystemInfoLabel", "Informations système complètes");
        ViewInAboutButton.Content = L("ViewInAboutButton", "Voir dans À propos");

        // Journaux
        LogsPageTitle.Text = L("LogsPageTitle.Text", "Journaux");
        LoggingToggle.OnContent = L("LoggingActive", "Journalisation activée");
        LoggingToggle.OffContent = L("LoggingInactive", "Journalisation désactivée");
        AutomationProperties.SetName(LoggingToggle, L("LoggingToggle.Name", "Journalisation"));
        foreach (var button in new[] { RefreshLogsButton, CopyLogsButton, ClearLogsButton, OpenLogsButton })
        {
            button.Label = L($"{button.Name}.Label", button.Label);
            ToolTipService.SetToolTip(button, button.Label);
        }
        LogFilterCombo.ItemsSource = new[]
        {
            L("LogFilterAll", "Tous les niveaux"),
            L("LogFilterWarnings", "Avertissements et erreurs"),
            L("LogFilterErrors", "Erreurs")
        };
        AutomationProperties.SetName(LogFilterCombo, L("LogFilter.Name", "Niveau de journal"));
        var wrapLabel = L("LogWrapButton.Label", "Retour à la ligne");
        ToolTipService.SetToolTip(LogWrapToggle, wrapLabel);
        AutomationProperties.SetName(LogWrapToggle, wrapLabel);

        // À propos
        AboutPageTitle.Text = L("AboutPageTitle.Text", "À propos");
        AboutGuiDescription.Text = L("AboutGuiDescription.Text", "Interface Windows indépendante qui pilote Sound Keeper sans modifier son moteur.");
        AboutEngineTitle.Text = L("AboutEngineTitle.Text", "Moteur Sound Keeper");
        AboutEngineDescription.Text = L("AboutEngineDescription.Text", "Cette application utilise le moteur Sound Keeper créé par Evgeny Vrublevsky, distribué sous licence MIT.");
        OpenUpstreamText.Text = L("OpenUpstreamButton.Content", "Projet d’origine");
        AutomationProperties.SetName(OpenUpstreamLink, OpenUpstreamText.Text);
        AboutGuiTitle.Text = L("AboutGuiTitle.Text", "Projet GUI");
        SourceCodeLabel.Text = L("SourceCodeLabel", "Code source de l’interface");
        AutomationProperties.SetName(OpenGuiProjectLink, $"{SourceCodeLabel.Text} ({OpenGuiProjectText.Text})");
        DocumentationLabel.Text = L("OpenDocumentationButton.Content", "Documentation");
        OpenDocumentationText.Text = L("OpenLinkText", "Ouvrir");
        AutomationProperties.SetName(OpenDocumentationLink, DocumentationLabel.Text);
        DiagnosticTitle.Text = L("DiagnosticTitle.Text", "Diagnostic");
        CopyDiagnosticText.Text = L("AboutCopyDiagnosticButton.Content", "Copier les informations système");

        ErrorBar.Title = L("ErrorBar.Title", "Une erreur est survenue");
    }

    private string L(string key, string fallback) => ViewModel.GetText(key, fallback);

    public async Task InitializeAsync(bool backgroundLaunch)
    {
        await ViewModel.InitializeAsync();
        _trayIcon = new TrayIconService(
            _windowHandle,
            () => ViewModel.IsRunning,
            () => ViewModel.StartWithWindows,
            HandleTrayAction);
        _statusTimer.Start();
        _isInitialized = true;

        if (backgroundLaunch || ViewModel.StartMinimized)
        {
            HideToTray();
        }
        else
        {
            RestoreAndActivate();
        }
    }

    public void RestoreAndActivate()
    {
        _appWindow.IsShownInSwitchers = true;
        TrayIconService.RestoreWindow(_windowHandle);
        Activate();
        _ = ViewModel.LogTrayEventAsync("Restauration depuis la zone de notification.");
    }

    public void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(MainViewModel.IsRunning):
                _trayIcon?.UpdateToolTip();
                break;
            case nameof(MainViewModel.IsLanguageRestartPending):
                var pending = ViewModel.IsLanguageRestartPending;
                LanguageRestartBar.IsOpen = pending;
                LanguageRestartBar.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
                break;
            case nameof(MainViewModel.TestSignalStatus):
                var status = ViewModel.TestSignalStatus;
                TestStatusBar.Message = status;
                TestStatusBar.IsOpen = status.Length > 0;
                TestStatusBar.Visibility = status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                break;
            case nameof(MainViewModel.ClipboardStatus) when ViewModel.ClipboardStatus.Length > 0:
                var bar = AdvancedPage.Visibility == Visibility.Visible ? AdvancedCopiedBar
                    : LogsPage.Visibility == Visibility.Visible ? LogsCopiedBar
                    : AboutCopiedBar;
                bar.Message = ViewModel.ClipboardStatus;
                bar.IsOpen = true;
                break;
        }
    }

    private void ApplyLayout()
    {
        var width = PagesHost.ActualWidth;
        if (width <= 0) return;

        var compact = width < DesignTokens.Get("BpCompact", 640);
        var padding = (Thickness)Application.Current.Resources[compact ? "PagePaddingCompact" : "PagePaddingDefault"];
        // Minimal pane: keep the page title clear of the native pane toggle button.
        if (MainNavigation.DisplayMode == NavigationViewDisplayMode.Minimal) padding.Top = DesignTokens.Get("SpaceXXL", 48);
        GeneralPageHost.Padding = padding;
        AdvancedPageHost.Padding = padding;
        AboutPageHost.Padding = padding;
        LogsPage.Padding = padding;
        LogsCommandBar.DefaultLabelPosition = compact ? CommandBarDefaultLabelPosition.Collapsed : CommandBarDefaultLabelPosition.Right;

        if (_compact == compact) return;
        _compact = compact;
        foreach (var control in _stackingControls)
        {
            ApplyRowLayout(control, compact);
        }
    }

    // Same result as QuickOutput's Compact setters: control below its label, full width.
    private void ApplyRowLayout(FrameworkElement control, bool compact)
    {
        if (!_wideLayout.TryGetValue(control, out var wide))
        {
            wide = (control.Width, control.HorizontalAlignment, control.Margin);
            _wideLayout[control] = wide;
        }

        Grid.SetRow(control, compact ? 1 : 0);
        Grid.SetColumn(control, compact ? 0 : 1);
        Grid.SetColumnSpan(control, compact ? 2 : 1);
        control.Width = compact ? double.NaN : wide.Width;
        control.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : wide.Alignment;
        control.Margin = compact ? new Thickness(0, DesignTokens.Get("SpaceS", 12), 0, 0) : wide.Margin;
    }

    private void ApplyTheme(AppTheme theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        DispatcherQueue.TryEnqueue(ApplyTitleBarTheme);
    }

    // Native title bar: follow the app theme when it differs from Windows (same approach as QuickOutput).
    private void ApplyTitleBarTheme()
    {
        if (_isExiting) return;
        var titleBar = _appWindow.TitleBar;
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
        {
            titleBar.BackgroundColor = titleBar.InactiveBackgroundColor = titleBar.ButtonBackgroundColor = titleBar.ButtonInactiveBackgroundColor = null;
            titleBar.ForegroundColor = titleBar.InactiveForegroundColor = titleBar.ButtonForegroundColor = titleBar.ButtonInactiveForegroundColor = null;
        }
        else
        {
            if (RootGrid.Background is SolidColorBrush background)
                titleBar.BackgroundColor = titleBar.InactiveBackgroundColor = titleBar.ButtonBackgroundColor = titleBar.ButtonInactiveBackgroundColor = background.Color;
            if (WindowTitleText.Foreground is SolidColorBrush foreground)
                titleBar.ForegroundColor = titleBar.InactiveForegroundColor = titleBar.ButtonForegroundColor = titleBar.ButtonInactiveForegroundColor = foreground.Color;
        }
        var dark = RootGrid.ActualTheme == ElementTheme.Dark ? 1 : 0;
        DwmSetWindowAttribute(_windowHandle, 20, ref dark, sizeof(int));
    }

    private void UpdateMinimumSize()
    {
        if (_isExiting || RootGrid.XamlRoot is null) return;
        if (_appWindow.Presenter is not OverlappedPresenter presenter) return;
        var scale = RootGrid.XamlRoot.RasterizationScale;
        var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest);
        var width = Math.Min((int)Math.Ceiling(DesignTokens.Get("WindowMinWidth", 480) * scale), area.WorkArea.Width);
        var height = Math.Min((int)Math.Ceiling(DesignTokens.Get("WindowMinHeight", 560) * scale), area.WorkArea.Height);
        if (presenter.PreferredMinimumWidth != width) presenter.PreferredMinimumWidth = width;
        if (presenter.PreferredMinimumHeight != height) presenter.PreferredMinimumHeight = height;
    }

    private async void OnStatusTimerTick(object? sender, object e)
    {
        if (_isTimerTicking) return;
        _isTimerTicking = true;
        try
        {
            await ViewModel.MonitorEngineAsync();
            if (LogsPage.Visibility == Visibility.Visible) await ViewModel.RefreshLogsAsync();
        }
        finally
        {
            _isTimerTicking = false;
        }
    }

    private async void MainNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag?.ToString() ?? "General";
        GeneralPage.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedPage.Visibility = tag == "Advanced" ? Visibility.Visible : Visibility.Collapsed;
        LogsPage.Visibility = tag == "Logs" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
        // Success InfoBars disappear on page change.
        AdvancedCopiedBar.IsOpen = LogsCopiedBar.IsOpen = AboutCopiedBar.IsOpen = false;
        if (tag != "Logs") return;

        try
        {
            await ViewModel.RefreshLogsAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void ViewInAbout_Click(object sender, RoutedEventArgs e) => MainNavigation.SelectedItem = AboutNavigationItem;

    private void LogWrapToggle_Click(object sender, RoutedEventArgs e)
    {
        var wrap = LogWrapToggle.IsChecked == true;
        LogViewer.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ScrollViewer.SetHorizontalScrollBarVisibility(LogViewer, wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
    }

    private async void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = CreateDialog(
            L("ClearLogsConfirmTitle", "Effacer les journaux ?"),
            L("ClearLogsConfirmMessage", "Le journal actuel et ses archives seront supprimés définitivement."),
            L("ClearLogsConfirmButton", "Effacer"));
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await ViewModel.ClearLogsAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private async void RestartNow_Click(object sender, RoutedEventArgs e)
    {
        await RestartApplicationAsync(resetSettings: false);
    }

    private async void ResetAndRestart_Click(object sender, RoutedEventArgs e)
    {
        var dialog = CreateDialog(
            L("ResetConfirmTitle", "Réinitialiser tous les paramètres ?"),
            L("ResetConfirmMessage", "Vos réglages seront remplacés par les valeurs par défaut, puis l’application redémarrera."),
            L("ResetConfirmButton", "Réinitialiser"));

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await RestartApplicationAsync(resetSettings: true);
        }
    }

    // ConfirmDialog: explicit verb as primary button, Cancel as default.
    private ContentDialog CreateDialog(string title, string content, string primaryButtonText) => new()
    {
        XamlRoot = RootGrid.XamlRoot,
        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
        RequestedTheme = RootGrid.ActualTheme,
        Title = title,
        Content = content,
        PrimaryButtonText = primaryButtonText,
        CloseButtonText = L("CancelButton", "Annuler"),
        DefaultButton = ContentDialogButton.Close
    };

    private async Task RestartApplicationAsync(bool resetSettings)
    {
        try
        {
            if (resetSettings)
            {
                await ViewModel.ResetSettingsAsync();
            }

            await ViewModel.PrepareForRestartAsync();
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException(ViewModel.GetText("RestartExecutableMissing", "Le chemin de l’application est introuvable."));
            AppInstance.GetCurrent().UnregisterKey();
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            })?.Dispose();
            ExitForRestart();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExiting) return;
        args.Cancel = true;
        if (ViewModel.CloseToTray)
        {
            HideToTray();
            return;
        }

        _ = ExitAsync();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_isInitialized || !ViewModel.MinimizeToTray || !args.DidPresenterChange) return;
        if (_appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            HideToTray();
        }
    }

    private void HideToTray()
    {
        _appWindow.IsShownInSwitchers = false;
        ShowWindow(_windowHandle, 0);
        _ = ViewModel.LogTrayEventAsync("Réduction vers la zone de notification.");
    }

    private async void HandleTrayAction(TrayAction action)
    {
        try
        {
            switch (action)
            {
                case TrayAction.Open:
                    RestoreAndActivate();
                    break;
                case TrayAction.Toggle:
                    await ViewModel.ToggleEngineAsync();
                    break;
                case TrayAction.ToggleStartup:
                    ViewModel.StartWithWindows = !ViewModel.StartWithWindows;
                    break;
                case TrayAction.Exit:
                    await ExitAsync();
                    break;
            }
        }
        catch (Exception exception)
        {
            RestoreAndActivate();
            ShowError(exception.Message);
        }
    }

    private async Task ExitAsync()
    {
        await ViewModel.StopForExitAsync();
        await ViewModel.LogGuiClosingAsync();
        _isExiting = true;
        _statusTimer.Stop();
        _trayIcon?.Dispose();
        ViewModel.Dispose();
        Close();
        Application.Current.Exit();
    }

    private void ExitForRestart()
    {
        _isExiting = true;
        _statusTimer.Stop();
        _trayIcon?.Dispose();
        ViewModel.Dispose();
        Close();
        Application.Current.Exit();
    }

    private void OpenUpstream_Click(object sender, RoutedEventArgs e) => OpenExternal("https://github.com/vrubleg/soundkeeper");

    private void OpenGuiProject_Click(object sender, RoutedEventArgs e) => OpenExternal("https://github.com/golabox/win-soundkeeper-gui");

    private void OpenDocumentation_Click(object sender, RoutedEventArgs e) => OpenExternal("https://github.com/golabox/win-soundkeeper-gui#readme");

    private void OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
