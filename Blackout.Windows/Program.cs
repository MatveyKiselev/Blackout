using Blackout.Core;
using Blackout.Platforms.Windows;
using Microsoft.Extensions.Logging;

namespace Blackout.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider());
#if DEBUG
            builder.AddDebug();
#endif
        });

        // Before any window or tray work: a second launch must cost milliseconds, not a process.
        using var guard = new SingleInstanceGuard(loggerFactory.CreateLogger<SingleInstanceGuard>());
        if (!guard.TryAcquire())
        {
            guard.SignalExistingInstance();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);

        var startHidden = args.Any(arg =>
            string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase));

        using var context = new TrayApplicationContext(loggerFactory, guard, startHidden);
        Application.Run(context);
    }
}
