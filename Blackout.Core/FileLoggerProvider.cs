using System.Text;
using Microsoft.Extensions.Logging;

namespace Blackout.Core;

/// <summary>
/// Minimal append-only file log. A background app with no console needs one: "hotkey did not
/// work" is otherwise undebuggable after the fact.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 2 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly string _path;

    public FileLoggerProvider()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Blackout");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "blackout.log");
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    private void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(_path, _path + ".old", overwrite: true);
                }

                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }
    }

    public void Dispose()
    {
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var builder = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(logLevel).Append("] ")
                .Append(_category)
                .Append(": ")
                .Append(formatter(state, exception));

            if (exception is not null)
            {
                builder.Append(" | ").Append(exception);
            }

            _provider.Write(builder.ToString());
        }
    }
}
