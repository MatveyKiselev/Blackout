namespace Blackout.Core;

/// <summary>Outcome of a hotkey registration attempt.</summary>
public sealed record HotkeyRegistrationResult(bool Success, string? Error)
{
    public static HotkeyRegistrationResult Ok { get; } = new(true, null);

    public static HotkeyRegistrationResult Fail(string error) => new(false, error);
}

/// <summary>
/// Registers one system wide hotkey. Implementations may raise <see cref="Pressed"/> from any
/// thread; consumers must marshal to the UI thread themselves.
/// </summary>
public interface IHotkeyService
{
    event EventHandler? Pressed;

    /// <summary>Replaces any previous registration with <paramref name="spec"/>.</summary>
    HotkeyRegistrationResult Register(HotkeySpec spec);

    void Unregister();
}

/// <summary>Enumerates displays. The list is refreshed on every call, never cached.</summary>
public interface IMonitorService
{
    IReadOnlyList<MonitorInfo> GetMonitors();
}

/// <summary>
/// Black overlay across one or more monitors. Implementations own the platform windows and the
/// "dismiss" key handling; the platform shows an overlay until it is told to hide.
/// </summary>
public interface IOverlayService
{
    bool IsVisible { get; }

    /// <summary>Raised when the platform decided the overlay must go away (Esc, display change).</summary>
    event EventHandler? DismissRequested;

    void Show(IReadOnlyList<MonitorInfo> monitors);

    void Hide();
}

/// <summary>Reports for how long the user has been away and raises one event per idle period.</summary>
public interface IIdleMonitor : IDisposable
{
    event EventHandler? IdleThresholdReached;

    TimeSpan GetIdleTime();

    void Start(TimeSpan threshold);

    void Stop();
}

/// <summary>Marshals work onto the thread that owns the UI.</summary>
public interface IUiDispatcher
{
    bool IsUiThread { get; }

    /// <summary>Runs <paramref name="action"/> on the UI thread, now or later.</summary>
    void Post(Action action);
}

/// <summary>Registration in the OS "start with the system" list.</summary>
public interface IAutostartService
{
    bool IsSupported { get; }

    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

/// <summary>Persists <see cref="AppSettings"/>.</summary>
public interface ISettingsStore
{
    string FilePath { get; }

    AppSettings Load();

    void Save(AppSettings settings);
}
