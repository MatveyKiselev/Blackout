using Blackout.Core;
using Microsoft.Win32;

namespace Blackout.Platforms.Windows;

/// <summary>
/// HKCU\...\Run entry. The app is unpackaged, so the registry stays the simplest option and it
/// also shows up in the Task Manager startup tab, which users expect.
/// </summary>
public sealed class RegistryAutostartService : IAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Blackout";
    private const string BackgroundArgument = "--background";

    public bool IsSupported => Environment.ProcessPath is not null;

    public bool IsEnabled
    {
        get
        {
            var executable = Environment.ProcessPath;
            if (executable is null)
            {
                return false;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value &&
                   value.Contains(executable, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу.");

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Не удалось открыть раздел автозапуска в реестре.");

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{executable}\" {BackgroundArgument}", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
