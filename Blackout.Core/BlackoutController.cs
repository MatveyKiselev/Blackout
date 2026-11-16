using Microsoft.Extensions.Logging;

namespace Blackout.Core;

/// <summary>
/// Platform independent orchestration: settings, hotkey, idle timer and overlay.
/// Every public member must be called from the UI thread; events are raised on the UI thread.
/// </summary>
public sealed class BlackoutController : IDisposable
{
    private readonly IHotkeyService _hotkeys;
    private readonly IOverlayService _overlay;
    private readonly IMonitorService _monitors;
    private readonly IIdleMonitor _idle;
    private readonly IAutostartService _autostart;
    private readonly ISettingsStore _store;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<BlackoutController> _log;
    private bool _started;
    private bool _disposed;

    public BlackoutController(
        IHotkeyService hotkeys,
        IOverlayService overlay,
        IMonitorService monitors,
        IIdleMonitor idle,
        IAutostartService autostart,
        ISettingsStore store,
        IUiDispatcher ui,
        ILogger<BlackoutController> log)
    {
        _hotkeys = hotkeys;
        _overlay = overlay;
        _monitors = monitors;
        _idle = idle;
        _autostart = autostart;
        _store = store;
        _ui = ui;
        _log = log;
    }

    public AppSettings Settings { get; private set; } = new();

    public bool IsBlackoutActive => _overlay.IsVisible;

    /// <summary>Set when the configured hotkey could not be registered.</summary>
    public string? HotkeyError { get; private set; }

    /// <summary>Last non fatal problem worth showing in the settings window.</summary>
    public string? LastError { get; private set; }

    public bool IsAutostartEnabled => _autostart.IsEnabled;

    public string SettingsFilePath => _store.FilePath;

    public event EventHandler? StateChanged;

    public event EventHandler? SettingsChanged;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Settings = _store.Load();
        Settings.Autostart = _autostart.IsEnabled;

        _overlay.DismissRequested += OnDismissRequested;
        _hotkeys.Pressed += OnHotkeyPressed;
        _idle.IdleThresholdReached += OnIdleThresholdReached;

        var result = _hotkeys.Register(Settings.EffectiveHotkey);
        if (result.Success)
        {
            Settings.ToggleHotkey = Settings.EffectiveHotkey;
        }
        else
        {
            HotkeyError = result.Error;
            _log.LogWarning("Hotkey {Hotkey} not registered: {Error}", Settings.EffectiveHotkey.DisplayName, result.Error);
        }

        ApplyIdle();
        Save();
        _log.LogInformation("Blackout started. Hotkey {Hotkey}", Settings.EffectiveHotkey.DisplayName);
        foreach (var monitor in _monitors.GetMonitors())
        {
            _log.LogInformation("Monitor {Id} '{Name}' {W}x{H} at {X},{Y} primary={Primary}.",
                monitor.Id, monitor.Name, monitor.Width, monitor.Height, monitor.Left, monitor.Top, monitor.IsPrimary);
        }

        RaiseSettingsChanged();
    }

    public IReadOnlyList<MonitorInfo> GetMonitors() => _monitors.GetMonitors();

    public bool Toggle()
    {
        if (_overlay.IsVisible)
        {
            HideBlackout();
            return false;
        }

        return ShowBlackout();
    }

    public bool ShowBlackout()
    {
        try
        {
            var all = _monitors.GetMonitors();
            var selected = SelectMonitors(all);

            if (selected.Count == 0)
            {
                LastError = "Не найден ни один монитор.";
                RaiseStateChanged();
                return false;
            }

            _overlay.Show(selected);
            LastError = null;
            _log.LogInformation("Blackout shown on {Count} monitor(s): {Ids}.",
                selected.Count, string.Join(", ", selected.Select(m => m.Id)));
            RaiseStateChanged();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _log.LogError(ex, "Failed to show the blackout overlay.");
            RaiseStateChanged();
            return false;
        }
    }

    public void HideBlackout()
    {
        _overlay.Hide();
        RaiseStateChanged();
    }

    private IReadOnlyList<MonitorInfo> SelectMonitors(IReadOnlyList<MonitorInfo> all)
    {
        if (Settings.EnabledMonitorIds.Count == 0)
        {
            return all;
        }

        var wanted = new HashSet<string>(Settings.EnabledMonitorIds, StringComparer.OrdinalIgnoreCase);
        var selected = all.Where(m => wanted.Contains(m.Id)).ToList();
        if (selected.Count > 0)
        {
            return selected;
        }

        // Cable moved to another port changes the id. Do not leave the user with a dead hotkey.
        LastError = "Сохранённые мониторы не найдены, использую основной.";
        var primary = all.FirstOrDefault(m => m.IsPrimary);
        return primary is null ? new List<MonitorInfo>() : new List<MonitorInfo> { primary };
    }

    public HotkeyRegistrationResult TrySetHotkey(HotkeySpec spec)
    {
        if (!spec.IsValid)
        {
            return HotkeyRegistrationResult.Fail("Нужен модификатор (Ctrl, Alt, Shift, Win) или функциональная клавиша.");
        }

        var result = _hotkeys.Register(spec);
        if (result.Success)
        {
            Settings.ToggleHotkey = spec;
            HotkeyError = null;
            Save();
        }
        else
        {
            HotkeyError = result.Error;
        }

        RaiseSettingsChanged();
        return result;
    }

    /// <summary>Stops listening for the hotkey, e.g. while the user is recording a new one.</summary>
    public void PauseHotkey() => _hotkeys.Unregister();

    /// <summary>Restores the configured hotkey after <see cref="PauseHotkey"/>.</summary>
    public void ResumeHotkey()
    {
        var result = _hotkeys.Register(Settings.EffectiveHotkey);
        HotkeyError = result.Success ? null : result.Error;
        RaiseSettingsChanged();
    }

    public void SetIdleBlackout(bool enabled, int minutes)    {
        Settings.IdleBlackoutEnabled = enabled;
        Settings.IdleTimeoutMinutes = Math.Clamp(minutes, 1, 120);
        ApplyIdle();
        Save();
        RaiseSettingsChanged();
    }

    public void SetAutostart(bool enabled)
    {
        if (!_autostart.IsSupported)
        {
            LastError = "Автозапуск не поддерживается на этой системе.";
            RaiseSettingsChanged();
            return;
        }

        try
        {
            _autostart.SetEnabled(enabled);
            Settings.Autostart = _autostart.IsEnabled;
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = $"Не удалось изменить автозапуск: {ex.Message}";
            _log.LogError(ex, "Autostart change failed.");
        }

        Save();
        RaiseSettingsChanged();
    }

    public void SetSelectedMonitors(IEnumerable<string> ids)
    {
        Settings.EnabledMonitorIds = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _log.LogInformation(
            "Monitor selection saved: {Selection}.",
            Settings.EnabledMonitorIds.Count == 0 ? "all" : string.Join(", ", Settings.EnabledMonitorIds));
        Save();
        RaiseSettingsChanged();
    }

    private void ApplyIdle()
    {
        if (Settings.IdleBlackoutEnabled)
        {
            _idle.Start(TimeSpan.FromMinutes(Settings.IdleTimeoutMinutes));
        }
        else
        {
            _idle.Stop();
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs e) =>
        _ui.Post(() =>
        {
            if (!_disposed)
            {
                Toggle();
            }
        });

    private void OnDismissRequested(object? sender, EventArgs e) =>
        _ui.Post(() =>
        {
            if (!_disposed)
            {
                HideBlackout();
            }
        });

    private void OnIdleThresholdReached(object? sender, EventArgs e) =>
        _ui.Post(() =>
        {
            if (_disposed || !Settings.IdleBlackoutEnabled || _overlay.IsVisible)
            {
                return;
            }

            ShowBlackout();
        });

    private void Save()
    {
        try
        {
            _store.Save(Settings);
        }
        catch (Exception ex)
        {
            LastError = $"Не удалось сохранить настройки: {ex.Message}";
            _log.LogError(ex, "Settings save failed.");
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void RaiseSettingsChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _overlay.DismissRequested -= OnDismissRequested;
        _hotkeys.Pressed -= OnHotkeyPressed;
        _idle.IdleThresholdReached -= OnIdleThresholdReached;
        _hotkeys.Unregister();
        _idle.Stop();
        _overlay.Hide();
    }
}
