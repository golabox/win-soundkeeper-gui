using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SoundKeeper.GUI.Services;

public enum TrayAction
{
    Open,
    Toggle,
    ToggleStartup,
    Exit
}

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Func<bool> _isRunning;
    private readonly Func<bool> _startsWithWindows;
    private readonly Action<TrayAction> _action;

    public TrayIconService(
        IntPtr windowHandle,
        Func<bool> isRunning,
        Func<bool> startsWithWindows,
        Action<TrayAction> action)
    {
        _ = windowHandle;
        _isRunning = isRunning;
        _startsWithWindows = startsWithWindows;
        _action = action;

        _statusItem = new ToolStripMenuItem { Enabled = false };
        _toggleItem = new ToolStripMenuItem();
        _toggleItem.Click += (_, _) => _action(TrayAction.Toggle);

        var openItem = new ToolStripMenuItem(Text("Ouvrir", "Open", "Abrir"));
        openItem.Click += (_, _) => _action(TrayAction.Open);

        _startupItem = new ToolStripMenuItem(Text("Lancer avec Windows", "Start with Windows", "Iniciar con Windows"));
        _startupItem.Click += (_, _) => _action(TrayAction.ToggleStartup);

        var exitItem = new ToolStripMenuItem(Text("Quitter", "Exit", "Salir"));
        exitItem.Click += (_, _) => _action(TrayAction.Exit);

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            _statusItem,
            new ToolStripSeparator(),
            _toggleItem,
            openItem,
            _startupItem,
            new ToolStripSeparator(),
            exitItem
        ]);
        menu.Opening += (_, _) => RefreshMenu();

        var icon = Environment.ProcessPath is { } path
            ? Icon.ExtractAssociatedIcon(path)
            : SystemIcons.Application;
        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = icon ?? SystemIcons.Application,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left) _action(TrayAction.Open);
        };
        UpdateToolTip();
    }

    public void UpdateToolTip()
    {
        var running = _isRunning();
        _notifyIcon.Text = running
            ? Text("Sound Keeper GUI — Moteur actif", "Sound Keeper GUI — Engine active", "Sound Keeper GUI — Motor activo")
            : Text("Sound Keeper GUI — Moteur arrêté", "Sound Keeper GUI — Engine stopped", "Sound Keeper GUI — Motor detenido");
        _statusItem.Text = running
            ? Text("Sound Keeper : actif", "Sound Keeper: active", "Sound Keeper: activo")
            : Text("Sound Keeper : arrêté", "Sound Keeper: stopped", "Sound Keeper: detenido");
        _toggleItem.Text = running
            ? Text("Désactiver", "Disable", "Desactivar")
            : Text("Activer", "Enable", "Activar");
    }

    public void ShowNotification(string message)
    {
        _notifyIcon.BalloonTipTitle = "Sound Keeper GUI";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
        _notifyIcon.ShowBalloonTip(5000);
    }

    private void RefreshMenu()
    {
        UpdateToolTip();
        _startupItem.Checked = _startsWithWindows();
    }

    public static void RestoreWindow(IntPtr handle)
    {
        ShowWindow(handle, 9);
        SetForegroundWindow(handle);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        if (!ReferenceEquals(_notifyIcon.Icon, SystemIcons.Application))
        {
            _notifyIcon.Icon?.Dispose();
        }
        _notifyIcon.Dispose();
    }

    private static string Text(string french, string english, string spanish)
    {
        var language = LocalizationService.ActiveLanguage;
        if (language.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) return french;
        if (language.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return spanish;
        return english;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
