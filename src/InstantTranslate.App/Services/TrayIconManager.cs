using System.Drawing;
using Forms = System.Windows.Forms;

namespace InstantTranslate.Services;

internal sealed class TrayIconManager : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _enabledMenuItem;
    private readonly Icon _applicationIcon;

    public TrayIconManager(bool isEnabled)
    {
        _enabledMenuItem = new Forms.ToolStripMenuItem("启用划词翻译")
        {
            Checked = isEnabled,
            CheckOnClick = true,
        };
        _enabledMenuItem.CheckedChanged += (_, _) => EnabledChanged?.Invoke(_enabledMenuItem.Checked);

        var settingsMenuItem = new Forms.ToolStripMenuItem("设置…");
        settingsMenuItem.Click += (_, _) => SettingsRequested?.Invoke();

        var exitMenuItem = new Forms.ToolStripMenuItem("退出");
        exitMenuItem.Click += (_, _) => ExitRequested?.Invoke();

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add(settingsMenuItem);
        contextMenu.Items.Add(_enabledMenuItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitMenuItem);

        _applicationIcon = LoadApplicationIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "InstantTranslate",
            Icon = _applicationIcon,
            ContextMenuStrip = contextMenu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
    }

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    public event Action<bool>? EnabledChanged;

    public void SetEnabled(bool value)
    {
        if (_enabledMenuItem.Checked == value)
        {
            return;
        }

        _enabledMenuItem.Checked = value;
    }

    public void ShowError(string message)
    {
        var safeMessage = string.IsNullOrWhiteSpace(message)
            ? "翻译请求失败，请检查 DeepSeek 设置。"
            : message;
        if (safeMessage.Length > 240)
        {
            safeMessage = safeMessage[..240] + "…";
        }

        _notifyIcon.ShowBalloonTip(
            5000,
            "InstantTranslate 翻译失败",
            safeMessage,
            Forms.ToolTipIcon.Error);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _applicationIcon.Dispose();
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/AppLogo.ico", UriKind.Absolute));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                using var source = new Icon(stream);
                return (Icon)source.Clone();
            }
        }
        catch (Exception)
        {
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
