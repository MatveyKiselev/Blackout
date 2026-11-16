using Blackout.Core;
using Blackout.Platforms.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Blackout.Windows;

/// <summary>
/// Owns the settings window. The form is created on first request, so a tray-only start never
/// builds a window at all.
/// </summary>
internal sealed class SettingsWindowService : IAppWindowService, IAppLifetime
{
    private readonly BlackoutController _controller;
    private readonly ILogger<SettingsWindowService> _log;

    private SettingsForm? _form;

    public SettingsWindowService(BlackoutController controller, ILogger<SettingsWindowService> log)
    {
        _controller = controller;
        _log = log;
    }

    public bool IsVisible => _form is { Visible: true };

    public void Show()
    {
        if (_form is null || _form.IsDisposed)
        {
            _form = new SettingsForm(_controller);
            _form.HideRequested += (_, _) => Hide();
        }

        _form.Show();

        if (_form.WindowState == FormWindowState.Minimized)
        {
            _form.WindowState = FormWindowState.Normal;
        }

        _form.Activate();
        _form.BringToFront();
    }

    public void Hide()
    {
        if (_form is { IsDisposed: false })
        {
            _form.Hide();
            ScheduleWorkingSetTrim();
        }
    }

    /// <summary>
    /// Return the pages of a hidden window to the system: a tray app has no reason to keep the
    /// form, WinForms and JIT output resident while nobody is looking at them.
    /// </summary>
    public void ScheduleWorkingSetTrim()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                if (!IsVisible)
                {
                    NativeMethods.EmptyWorkingSet(NativeMethods.GetCurrentProcess());
                }
            }
            catch (Exception)
            {
                // Purely cosmetic.
            }
        });
    }

    public void Quit()
    {
        _log.LogInformation("Exit requested.");

        if (_form is { IsDisposed: false })
        {
            _form.AllowClose = true;
            _form.Close();
        }

        Application.Exit();
    }
}
