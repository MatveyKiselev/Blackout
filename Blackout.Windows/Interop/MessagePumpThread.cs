using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Blackout.Platforms.Windows.Interop;

/// <summary>
/// A dedicated thread with its own Win32 message loop and one hidden top level window.
/// <para>
/// WinUI owns the UI thread's pump and does not guarantee delivery for windows it does not
/// know about, so every native window used here lives on a pump of its own. The window is a
/// real top level window (hidden, tool window) rather than a message-only window because
/// broadcasts such as WM_DISPLAYCHANGE never reach message-only windows.
/// </para>
/// </summary>
internal sealed class MessagePumpThread : IDisposable
{
    private const uint WM_APP_RUN_QUEUE = NativeMethods.WM_APP + 1;

    private readonly string _className;
    private readonly Func<nint, uint, nint, nint, nint?>? _handler;
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly WndProcDelegate _proc;
    private readonly object _gate = new();

    private Thread? _thread;
    private nint _hwnd;
    private uint _threadId;
    private int _managedThreadId;
    private bool _disposed;

    public MessagePumpThread(Func<nint, uint, nint, nint, nint?>? handler = null, string? className = null)
    {
        _handler = handler;
        _className = className ?? "BlackoutPump_" + Guid.NewGuid().ToString("N");
        _proc = InstanceProc;
    }

    /// <summary>The hidden control window. Valid once <see cref="EnsureStarted"/> returned.</summary>
    public nint Handle => _hwnd;

    public bool IsCurrentThread => Environment.CurrentManagedThreadId == _managedThreadId;

    public void EnsureStarted()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MessagePumpThread));
            }

            if (_thread is null)
            {
                // These threads only pump messages and run short actions: a small stack keeps the
                // reserved address space low without any downside.
                _thread = new Thread(ThreadMain, 256 * 1024)
                {
                    IsBackground = true,
                    Name = "Blackout.MessagePump",
                };
                _thread.Start();
            }
        }

        _ready.Wait();
    }

    /// <summary>Runs <paramref name="action"/> on the pump thread and waits for it to finish.</summary>
    public void Invoke(Action action)
    {
        if (IsCurrentThread)
        {
            action();
            return;
        }

        EnsureStarted();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        NativeMethods.PostMessageW(_hwnd, WM_APP_RUN_QUEUE, 0, 0);
        completion.Task.GetAwaiter().GetResult();
    }

    private void ThreadMain()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _managedThreadId = Environment.CurrentManagedThreadId;

        var windowClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = _proc,
            hInstance = NativeMethods.GetModuleHandleW(null),
            lpszClassName = _className,
        };
        NativeMethods.RegisterClassExW(ref windowClass);

        _hwnd = NativeMethods.CreateWindowExW(
            NativeMethods.WS_EX_TOOLWINDOW,
            _className,
            "Blackout",
            NativeMethods.WS_POPUP,
            0, 0, 0, 0,
            0, 0, windowClass.hInstance, 0);

        _ready.Set();

        while (NativeMethods.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }
    }

    private nint InstanceProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_APP_RUN_QUEUE)
        {
            while (_queue.TryDequeue(out var action))
            {
                action();
            }

            return 0;
        }

        if (_handler?.Invoke(hWnd, msg, wParam, lParam) is { } result)
        {
            return result;
        }

        return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            thread = _thread;
            _thread = null;
        }

        if (thread is null)
        {
            _ready.Dispose();
            return;
        }

        if (!IsCurrentThread)
        {
            NativeMethods.PostThreadMessageW(_threadId, NativeMethods.WM_QUIT, 0, 0);
        }

        thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
