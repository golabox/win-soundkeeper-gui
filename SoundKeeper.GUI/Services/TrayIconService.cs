using System.Drawing;
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
    private readonly Func<string, string, string> _text;
    private readonly Action<TrayAction> _action;

    // text(key, fallback) reads the same .resw strings as the window.
    public TrayIconService(
        Func<bool> isRunning,
        Func<bool> startsWithWindows,
        Func<string, string, string> text,
        Action<TrayAction> action)
    {
        _isRunning = isRunning;
        _startsWithWindows = startsWithWindows;
        _text = text;
        _action = action;

        _statusItem = new ToolStripMenuItem { Enabled = false };
        _toggleItem = new ToolStripMenuItem();
        _toggleItem.Click += (_, _) => _action(TrayAction.Toggle);

        var openItem = new ToolStripMenuItem(_text("TrayOpen", "Ouvrir"));
        openItem.Click += (_, _) => _action(TrayAction.Open);

        _startupItem = new ToolStripMenuItem(_text("StartupToggle.Header", "Lancer au démarrage de Windows"));
        _startupItem.Click += (_, _) => _action(TrayAction.ToggleStartup);

        var exitItem = new ToolStripMenuItem(_text("TrayExit", "Quitter"));
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

        // The tray .ico holds hand-drawn 16/20/24/32 frames: take the one matching the system small icon size (DPI).
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "soundkeeper-tray.ico");
        var icon = File.Exists(iconPath)
            ? new Icon(iconPath, SystemInformation.SmallIconSize)
            : SystemIcons.Application;
        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = icon,
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
        var status = running
            ? _text("StatusRunning", "Sound Keeper actif")
            : _text("StatusStopped", "Sound Keeper arrêté");
        _notifyIcon.Text = status;
        _statusItem.Text = status;
        _toggleItem.Text = running
            ? _text("DisableButton", "Désactiver")
            : _text("EnableButton", "Activer");
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
}
