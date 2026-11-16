using System.Runtime.InteropServices;
using Blackout.Core;
using Blackout.Platforms.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Blackout.Platforms.Windows;

/// <summary>
/// System wide hotkey through RegisterHotKey on a dedicated pump thread. Pressed is raised on
/// that thread, never on the UI thread.
/// </summary>
public sealed class Win32HotkeyService : IHotkeyService, IDisposable
{
    private const int HotkeyId = 0x0B01;

    private readonly MessagePumpThread _pump;
    private readonly ILogger<Win32HotkeyService> _log;
    private HotkeySpec? _registered;
    private bool _disposed;

    public Win32HotkeyService(ILogger<Win32HotkeyService> log)
    {
        _log = log;
        _pump = new MessagePumpThread(HandleMessage);
    }

    public event EventHandler? Pressed;

    public HotkeyRegistrationResult Register(HotkeySpec spec)
    {
        if (_disposed)
        {
            return HotkeyRegistrationResult.Fail("Сервис горячих клавиш уже освобождён.");
        }

        var result = HotkeyRegistrationResult.Ok;

        _pump.Invoke(() =>
        {
            var previous = _registered;
            NativeMethods.UnregisterHotKey(_pump.Handle, HotkeyId);
            _registered = null;

            var modifiers = ToNative(spec.Modifiers) | NativeMethods.MOD_NOREPEAT;
            if (NativeMethods.RegisterHotKey(_pump.Handle, HotkeyId, modifiers, (uint)spec.Key))
            {
                _registered = spec;
                result = HotkeyRegistrationResult.Ok;
                _log.LogInformation("Hotkey {Hotkey} registered on window {Handle}.", spec.DisplayName, _pump.Handle);
                return;
            }

            var error = Marshal.GetLastWin32Error();
            _log.LogWarning("Hotkey {Hotkey} registration failed with error {Error}.", spec.DisplayName, error);
            result = HotkeyRegistrationResult.Fail(Describe(error));

            // Keep the old hotkey working when the new one is rejected.
            if (previous is not null &&
                NativeMethods.RegisterHotKey(_pump.Handle, HotkeyId, ToNative(previous.Modifiers) | NativeMethods.MOD_NOREPEAT, (uint)previous.Key))
            {
                _registered = previous;
            }
        });

        return result;
    }

    public void Unregister()
    {
        if (_disposed)
        {
            return;
        }

        _pump.Invoke(() =>
        {
            NativeMethods.UnregisterHotKey(_pump.Handle, HotkeyId);
            _registered = null;
        });
    }

    private nint? HandleMessage(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam == HotkeyId)
        {
            _log.LogInformation("Hotkey pressed.");
            Pressed?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        return null;
    }

    private static uint ToNative(HotkeyModifiers modifiers)
    {
        var native = 0u;
        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            native |= NativeMethods.MOD_ALT;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            native |= NativeMethods.MOD_CONTROL;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            native |= NativeMethods.MOD_SHIFT;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            native |= NativeMethods.MOD_WIN;
        }

        return native;
    }

    private static string Describe(int error) => error switch
    {
        (int)NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED => "Комбинация уже занята другой программой.",
        5 => "Нет прав на регистрацию комбинации.",
        _ => $"RegisterHotKey завершился ошибкой {error}.",
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pump.Dispose();
    }
}
