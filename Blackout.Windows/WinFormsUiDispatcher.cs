using Blackout.Core;

namespace Blackout.Windows;

/// <summary>
/// Marshals onto the WinForms UI thread through a private control handle, which works before the
/// message loop starts and needs no synchronization context of its own.
/// </summary>
internal sealed class WinFormsUiDispatcher : IUiDispatcher, IDisposable
{
    private readonly Control _marshal = new();
    private readonly int _threadId = Environment.CurrentManagedThreadId;

    public WinFormsUiDispatcher()
    {
        // Force the handle now: pump threads may post before Application.Run begins.
        _ = _marshal.Handle;
    }

    public bool IsUiThread => Environment.CurrentManagedThreadId == _threadId;

    public void Post(Action action)
    {
        if (IsUiThread)
        {
            action();
            return;
        }

        _marshal.BeginInvoke(action);
    }

    public void Dispose() => _marshal.Dispose();
}
