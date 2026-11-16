using Blackout.Core;
using Blackout.Platforms.Windows;
using Microsoft.Extensions.Logging;

namespace Blackout.Windows;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly BlackoutController _controller;
    private readonly WinFormsTrayService _tray;
    private readonly Win32OverlayService _overlay;
    private readonly Win32HotkeyService _hotkeys;
    private readonly Win32IdleMonitor _idle;
    private readonly WinFormsUiDispatcher _ui;
    private readonly TrayCoordinator _coordinator;

    public TrayApplicationContext(ILoggerFactory loggerFactory, ISingleInstanceGuard guard, bool startHidden)
    {
        _ui = new WinFormsUiDispatcher();
        var store = new JsonSettingsStore();
        var monitors = new Win32MonitorService();
        _hotkeys = new Win32HotkeyService(loggerFactory.CreateLogger<Win32HotkeyService>());
        _overlay = new Win32OverlayService(loggerFactory.CreateLogger<Win32OverlayService>());
        _idle = new Win32IdleMonitor();
        var autostart = new RegistryAutostartService();

        _controller = new BlackoutController(
            _hotkeys,
            _overlay,
            monitors,
            _idle,
            autostart,
            store,
            _ui,
            loggerFactory.CreateLogger<BlackoutController>());

        var window = new SettingsWindowService(_controller, loggerFactory.CreateLogger<SettingsWindowService>());
        _tray = new WinFormsTrayService();

        _coordinator = new TrayCoordinator(
            _tray,
            window,
            window,
            guard,
            _ui,
            _controller,
            loggerFactory.CreateLogger<TrayCoordinator>());

        _controller.Start();
        _coordinator.Start();

        if (!startHidden)
        {
            window.Show();
        }
        else
        {
            // Nothing on screen: hand the startup working set back right away.
            window.ScheduleWorkingSetTrim();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _coordinator.Dispose();
            _controller.Dispose();
            _tray.Dispose();
            _overlay.Dispose();
            _hotkeys.Dispose();
            _idle.Dispose();
            _ui.Dispose();
        }

        base.Dispose(disposing);
    }
}
