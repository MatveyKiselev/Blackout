using Microsoft.Extensions.Logging;

namespace Blackout.Core;

/// <summary>Notification area icon with a context menu.</summary>
public interface ITrayService : IDisposable
{
    /// <summary>Raised when the user asks for the settings window.</summary>
    event EventHandler? OpenSettingsRequested;

    /// <summary>Raised when the user asks to toggle the blackout from the tray.</summary>
    event EventHandler? ToggleBlackoutRequested;

    event EventHandler? ExitRequested;

    void Start();

    void SetTooltip(string text);
}

/// <summary>Show, hide and measure the single application window.</summary>
public interface IAppWindowService
{
    bool IsVisible { get; }

    void Show();

    void Hide();
}

/// <summary>Ends the process through the platform's normal shutdown path.</summary>
public interface IAppLifetime
{
    void Quit();
}

public interface ISingleInstanceGuard : IDisposable
{
    /// <summary>
    /// Returns true for the first instance in the session. A false result means the caller must
    /// signal the running instance and exit.
    /// </summary>
    bool TryAcquire();

    /// <summary>Asks the running instance to show its window. Used by a second launch.</summary>
    void SignalExistingInstance();

    /// <summary>Raised in the first instance when another one was started.</summary>
    event EventHandler? SecondInstanceRequested;
}

/// <summary>
/// Owns the tray icon and translates its commands into controller calls. This is the piece that
/// makes the app a tray app instead of a windowed one; it is platform independent.
/// </summary>
public sealed class TrayCoordinator : IDisposable
{
    private readonly ITrayService _tray;
    private readonly IAppWindowService _window;
    private readonly IAppLifetime _lifetime;
    private readonly ISingleInstanceGuard _guard;
    private readonly IUiDispatcher _ui;
    private readonly BlackoutController _controller;
    private readonly ILogger<TrayCoordinator> _log;
    private bool _started;
    private bool _disposed;

    public TrayCoordinator(
        ITrayService tray,
        IAppWindowService window,
        IAppLifetime lifetime,
        ISingleInstanceGuard guard,
        IUiDispatcher ui,
        BlackoutController controller,
        ILogger<TrayCoordinator> log)
    {
        _tray = tray;
        _window = window;
        _lifetime = lifetime;
        _guard = guard;
        _ui = ui;
        _controller = controller;
        _log = log;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _tray.OpenSettingsRequested += OnOpenSettings;
        _tray.ToggleBlackoutRequested += OnToggleBlackout;
        _tray.ExitRequested += OnExit;
        _controller.StateChanged += OnControllerStateChanged;
        _guard.SecondInstanceRequested += OnSecondInstance;

        _tray.Start();
        UpdateTooltip();
        _log.LogInformation("Tray started.");
    }

    private void OnOpenSettings(object? sender, EventArgs e) => _ui.Post(() => _window.Show());

    private void OnSecondInstance(object? sender, EventArgs e) => _ui.Post(() => _window.Show());

    private void OnToggleBlackout(object? sender, EventArgs e) => _ui.Post(() => _controller.Toggle());

    private void OnExit(object? sender, EventArgs e) => _ui.Post(Exit);

    private void OnControllerStateChanged(object? sender, EventArgs e) => _ui.Post(UpdateTooltip);

    private void UpdateTooltip()
    {
        if (_disposed)
        {
            return;
        }

        var hotkey = _controller.Settings.EffectiveHotkey.DisplayName;
        _tray.SetTooltip(_controller.IsBlackoutActive
            ? "Blackout — экран затемнён (Esc)"
            : $"Blackout — {hotkey}");
    }

    private void Exit()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _log.LogInformation("Exit requested from the tray.");

        _controller.Dispose();
        _tray.Dispose();
        _window.Hide();
        _lifetime.Quit();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tray.OpenSettingsRequested -= OnOpenSettings;
        _tray.ToggleBlackoutRequested -= OnToggleBlackout;
        _tray.ExitRequested -= OnExit;
        _controller.StateChanged -= OnControllerStateChanged;
        _guard.SecondInstanceRequested -= OnSecondInstance;
        _tray.Dispose();
    }
}
