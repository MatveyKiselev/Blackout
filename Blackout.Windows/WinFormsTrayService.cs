using Blackout.Core;

namespace Blackout.Windows;

/// <summary>
/// Tray icon through WinForms NotifyIcon, which is itself a thin wrapper over Shell_NotifyIcon.
/// All events arrive on the UI thread.
/// </summary>
internal sealed class WinFormsTrayService : ITrayService
{
    private const int MaxTooltipLength = 63;

    private readonly NotifyIcon _icon;
    private readonly Icon _iconImage;
    private bool _disposed;

    public WinFormsTrayService()
    {
        _iconImage = LoadIconImage();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Настройки", null, (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Чёрный экран", null, (_, _) => ToggleBlackoutRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new NotifyIcon
        {
            Icon = _iconImage,
            Text = "Blackout",
            ContextMenuStrip = menu,
            Visible = false,
        };

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? OpenSettingsRequested;

    public event EventHandler? ToggleBlackoutRequested;

    public event EventHandler? ExitRequested;

    public void Start() => _icon.Visible = true;

    public void SetTooltip(string text)
    {
        _icon.Text = text.Length <= MaxTooltipLength ? text : text[..MaxTooltipLength];
    }

    private static Icon LoadIconImage()
    {
        var executable = Environment.ProcessPath;
        if (executable is not null)
        {
            try
            {
                var extracted = Icon.ExtractAssociatedIcon(executable);
                if (extracted is not null)
                {
                    return extracted;
                }
            }
            catch (ArgumentException)
            {
                // No icon resource in the executable; the system one is fine.
            }
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();

        if (!ReferenceEquals(_iconImage, SystemIcons.Application))
        {
            _iconImage.Dispose();
        }
    }
}
