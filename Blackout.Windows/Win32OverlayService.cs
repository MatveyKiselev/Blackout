using System.Runtime.InteropServices;
using Blackout.Core;
using Blackout.Platforms.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Blackout.Platforms.Windows;

/// <summary>
/// Full screen black windows, one per monitor, on a dedicated pump thread.
/// <para>
/// Windows are WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST so they stay out of Alt+Tab,
/// never take focus and stay above the taskbar. While an overlay is up the service also grabs
/// bare Esc through RegisterHotKey; the "dismiss" behaviour therefore belongs to this service,
/// not to the UI shell.
/// </para>
/// </summary>
public sealed class Win32OverlayService : IOverlayService, IDisposable
{
    private const int EscHotkeyId = 0x0B02;
    private const string OverlayClassName = "BlackoutOverlayWnd";

    private static readonly WndProcDelegate OverlayProc = OverlayWindowProc;
    private static readonly object ClassGate = new();
    private static bool _classRegistered;

    private readonly MessagePumpThread _pump;
    private readonly ILogger<Win32OverlayService> _log;
    private readonly Dictionary<string, nint> _windows = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _visible;
    private bool _disposed;

    public Win32OverlayService(ILogger<Win32OverlayService> log)
    {
        _log = log;
        _pump = new MessagePumpThread(HandleMessage);
        _pump.EnsureStarted();
    }

    public bool IsVisible => _visible;

    public event EventHandler? DismissRequested;

    public void Show(IReadOnlyList<MonitorInfo> monitors)
    {
        if (_disposed)
        {
            return;
        }

        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("Не выбран ни один монитор.");
        }

        _pump.Invoke(() =>
        {
            EnsureWindowClass();

            // Esc is the only way out of a black screen, so claim it before covering anything.
            if (!NativeMethods.RegisterHotKey(_pump.Handle, EscHotkeyId, 0, (uint)KeyCode.Escape))
            {
                _log.LogError("Esc hotkey registration failed with error {Error}; overlay not shown.", Marshal.GetLastWin32Error());
                throw new InvalidOperationException(
                    "Не удалось перехватить Esc: комбинация занята другой программой. Экран не затемнён.");
            }

            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var shown = 0;

            foreach (var monitor in monitors)
            {
                wanted.Add(monitor.Id);

                if (!_windows.TryGetValue(monitor.Id, out var hwnd))
                {
                    hwnd = CreateOverlayWindow();
                    if (hwnd == 0)
                    {
                        continue;
                    }

                    _windows[monitor.Id] = hwnd;
                }

                NativeMethods.SetWindowPos(
                    hwnd,
                    NativeMethods.HWND_TOPMOST,
                    monitor.Left,
                    monitor.Top,
                    monitor.Width,
                    monitor.Height,
                    NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOACTIVATE);

                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
                shown++;
            }

            foreach (var (id, hwnd) in _windows)
            {
                if (!wanted.Contains(id))
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
                }
            }

            if (shown == 0)
            {
                // Never leave the screen covered with no way out.
                NativeMethods.UnregisterHotKey(_pump.Handle, EscHotkeyId);
                foreach (var hwnd in _windows.Values)
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
                }

                throw new InvalidOperationException("Не удалось создать окно оверлея.");
            }

            _visible = true;
            _log.LogInformation("Overlay visible on {Shown} of {Requested} monitor(s).", shown, monitors.Count);
        });
    }

    public void Hide()
    {
        if (!_visible || _disposed)
        {
            return;
        }

        HideCore();
    }

    private void HideCore()
    {
        _pump.Invoke(() =>
        {
            NativeMethods.UnregisterHotKey(_pump.Handle, EscHotkeyId);

            foreach (var hwnd in _windows.Values)
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
            }

            _visible = false;
            _log.LogInformation("Overlay hidden.");
        });
    }

    private nint? HandleMessage(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_HOTKEY when wParam == EscHotkeyId:
                _log.LogInformation("Escape received, dismissing overlay.");
                HideCore();
                DismissRequested?.Invoke(this, EventArgs.Empty);
                return 0;

            // Resolution, arrangement or refresh-rate change invalidates window rectangles.
            case NativeMethods.WM_DISPLAYCHANGE when _visible:
                HideCore();
                DismissRequested?.Invoke(this, EventArgs.Empty);
                return 0;

            default:
                return null;
        }
    }

    private static void EnsureWindowClass()
    {
        lock (ClassGate)
        {
            if (_classRegistered)
            {
                return;
            }

            var windowClass = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = OverlayProc,
                hInstance = NativeMethods.GetModuleHandleW(null),
                hbrBackground = NativeMethods.GetStockObject(NativeMethods.BLACK_BRUSH),
                lpszClassName = OverlayClassName,
            };

            _classRegistered = NativeMethods.RegisterClassExW(ref windowClass) != 0;
        }
    }

    private static nint CreateOverlayWindow() => NativeMethods.CreateWindowExW(
        NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
        OverlayClassName,
        "Blackout",
        NativeMethods.WS_POPUP,
        0, 0, 0, 0,
        0, 0, NativeMethods.GetModuleHandleW(null), 0);

    private static nint OverlayWindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            // Keep the pointer invisible over a black screen.
            case NativeMethods.WM_SETCURSOR:
                NativeMethods.SetCursor(0);
                return 1;

            // Never activate: the user returns to the same window after Esc.
            case NativeMethods.WM_MOUSEACTIVATE:
                return NativeMethods.MA_NOACTIVATE;

            // Alt+F4 on the overlay must not silently leave a black screen on top.
            case NativeMethods.WM_CLOSE:
                return 0;

            case NativeMethods.WM_SYSCOMMAND when ((uint)wParam & 0xFFF0) == NativeMethods.SC_CLOSE:
                return 0;

            default:
                return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _pump.Invoke(() =>
            {
                NativeMethods.UnregisterHotKey(_pump.Handle, EscHotkeyId);
                foreach (var hwnd in _windows.Values)
                {
                    NativeMethods.DestroyWindow(hwnd);
                }

                _windows.Clear();
                _visible = false;
            });
        }
        catch (ObjectDisposedException)
        {
            // Pump already gone, nothing left to release.
        }

        _pump.Dispose();
    }
}
