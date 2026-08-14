using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Auraly.Platform.Console;

/// <summary>
/// Console output without the long category prefix (e.g. Namespace.Type[0]).
/// Format: {time} {level}: {message}
/// </summary>
public sealed class CleanConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "clean";

    public CleanConsoleFormatter()
        : base(FormatterName)
    {
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null) return;

        var time = DateTime.Now.ToString("HH:mm:ss");
        var level = logEntry.LogLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbg",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT",
            LogLevel.None => "none",
            _ => logEntry.LogLevel.ToString().ToLowerInvariant()
        };

        textWriter.WriteLine($"{time} {level}: {message}");

        if (logEntry.Exception is not null)
            textWriter.WriteLine(logEntry.Exception.ToString());
    }
}
