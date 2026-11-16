using System.Runtime.InteropServices;
using Blackout.Core;
using Blackout.Platforms.Windows.Interop;

namespace Blackout.Platforms.Windows;

/// <summary>
/// Idle time from GetLastInputInfo, without a periodic poll.
/// <para>
/// The timer is armed for exactly the remaining time until the threshold, so a ten minute timeout
/// costs roughly one wakeup instead of one per second. While the user is away the timer only
/// re-arms for a cheap "did the user come back" check, never in a tight loop.
/// </para>
/// </summary>
public sealed class Win32IdleMonitor : IIdleMonitor
{
    /// <summary>Minimum delay so a reached threshold never turns into a spin.</summary>
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromSeconds(1);

    /// <summary>Re-check interval after the threshold fired, only to detect the user's return.</summary>
    private static readonly TimeSpan ArmedRecheck = TimeSpan.FromSeconds(30);

    /// <summary>Upper bound, so a long threshold cannot hide a clock or power state change.</summary>
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(30);

    private readonly object _gate = new();

    private System.Threading.Timer? _timer;
    private TimeSpan _threshold = TimeSpan.MaxValue;
    private bool _running;
    private bool _fired;
    private bool _disposed;

    public event EventHandler? IdleThresholdReached;

    public TimeSpan GetIdleTime()
    {
        var info = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>(),
        };

        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        // Both values are 32 bit tick counts; unchecked subtraction stays correct across the wrap.
        var idleMilliseconds = unchecked((uint)NativeMethods.GetTickCount() - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMilliseconds);
    }

    public void Start(TimeSpan threshold)
    {
        lock (_gate)
        {
            if (_disposed || threshold <= TimeSpan.Zero)
            {
                return;
            }

            _threshold = threshold;
            _running = true;
            _fired = false;
            ScheduleLocked(GetIdleTime());
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            _fired = false;
            StopLocked();
        }
    }

    private void ScheduleLocked(TimeSpan idle)
    {
        // After firing, only look for the user's return; otherwise aim straight at the deadline.
        var delay = _fired
            ? ArmedRecheck
            : _threshold - idle;

        if (delay < MinimumDelay)
        {
            delay = MinimumDelay;
        }
        else if (delay > MaximumDelay)
        {
            delay = MaximumDelay;
        }

        if (_timer is null)
        {
            _timer = new System.Threading.Timer(_ => Tick(), null, delay, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void StopLocked()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick()
    {
        bool fire;
        lock (_gate)
        {
            if (_disposed || !_running)
            {
                return;
            }

            var idle = GetIdleTime();
            if (idle >= _threshold)
            {
                fire = !_fired;
                _fired = true;
            }
            else
            {
                // The user came back; allow the next idle period to fire again.
                _fired = false;
                fire = false;
            }

            ScheduleLocked(idle);
        }

        if (fire)
        {
            IdleThresholdReached?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _running = false;
            StopLocked();
        }
    }
}
