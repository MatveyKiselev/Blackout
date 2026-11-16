using Blackout.Core;
using Blackout.Platforms.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Blackout.Platforms.Windows;

/// <summary>
/// One instance per user session: a named mutex plus a fixed class window the second launch can
/// find and poke. The window is the guard's own pump, so nothing else has to know about IPC.
/// </summary>
public sealed class SingleInstanceGuard : ISingleInstanceGuard
{
    private const string MutexName = @"Local\Blackout.SingleInstance";
    private const string HostClassName = "BlackoutIpcHost";
    private const uint IpcMessage = NativeMethods.WM_APP + 3;
    private const int FindAttempts = 40;
    private const int FindDelayMs = 50;

    private readonly ILogger<SingleInstanceGuard>? _log;
    private Mutex? _mutex;
    private MessagePumpThread? _pump;
    private bool _disposed;

    public SingleInstanceGuard(ILogger<SingleInstanceGuard>? log = null)
    {
        _log = log;
    }

    public event EventHandler? SecondInstanceRequested;

    public bool TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

        if (!createdNew)
        {
            // A previous run may have died without releasing it; take over when it is free.
            try
            {
                createdNew = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                createdNew = true;
            }
        }

        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        _pump = new MessagePumpThread(HandleMessage, HostClassName);
        _pump.EnsureStarted();
        _log?.LogInformation("Single instance acquired.");
        return true;
    }

    public void SignalExistingInstance()
    {
        for (var attempt = 0; attempt < FindAttempts; attempt++)
        {
            var host = NativeMethods.FindWindowW(HostClassName, null);
            if (host != 0)
            {
                NativeMethods.PostMessageW(host, IpcMessage, 0, 0);
                return;
            }

            Thread.Sleep(FindDelayMs);
        }

        _log?.LogWarning("Second instance could not find the running one.");
    }

    private nint? HandleMessage(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == IpcMessage)
        {
            SecondInstanceRequested?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pump?.Dispose();

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Was not owned; nothing to release.
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }
}
