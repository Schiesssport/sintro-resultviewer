using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Sintro.ResultViewer;

/// <summary>Console lines for the person running the event: time, severity when it matters, message. No category or event id.</summary>
public sealed class OperatorConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "operator";

    /// <summary>Written exactly as composed, for the startup banner whose alignment is the point.</summary>
    public static readonly EventId Verbatim = new(0, "Verbatim");

    private const string Indentation = "          ";

    // Escape sequences would land as literal characters in a redirected file.
    private static readonly bool Colour = !Console.IsOutputRedirected;

    public override void Write<TState>(
        in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter writer)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null) return;

        if (logEntry.EventId.Name == Verbatim.Name)
        {
            writer.WriteLine(message);
            return;
        }

        WriteMessage(writer, logEntry.LogLevel, message);
        if (logEntry.Exception is not null) WriteException(writer, logEntry.LogLevel, logEntry.Exception);
    }

    private static void WriteMessage(TextWriter writer, LogLevel level, string message)
    {
        var (label, colour) = level switch
        {
            LogLevel.Warning => ("WARNING  ", "\e[33m"),
            LogLevel.Error => ("ERROR    ", "\e[31m"),
            LogLevel.Critical => ("FAILED   ", "\e[31m"),
            _ => ("", ""),
        };

        if (Colour && colour.Length > 0) writer.Write(colour);
        writer.Write($"{DateTime.Now:HH:mm:ss}  {label}");
        writer.Write(message.ReplaceLineEndings(Environment.NewLine + Indentation));
        if (Colour && colour.Length > 0) writer.Write("\e[0m");
        writer.WriteLine();
    }

    // A stack trace under a recovered warning would bury the line the operator can act on.
    private static void WriteException(TextWriter writer, LogLevel level, Exception exception) =>
        writer.WriteLine(Indent(level >= LogLevel.Error
            ? exception.ToString()
            : $"{exception.GetType().Name}: {exception.Message}"));

    private static string Indent(string text) =>
        Indentation + text.ReplaceLineEndings(Environment.NewLine + Indentation);
}

public static class OperatorConsoleExtensions
{
    public static ILoggingBuilder AddOperatorConsole(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddConsoleFormatter<OperatorConsoleFormatter, ConsoleFormatterOptions>();
        logging.AddConsole(console => console.FormatterName = OperatorConsoleFormatter.FormatterName);
        return logging;
    }

    public static void LogVerbatim(this ILogger logger, string lines) =>
        logger.Log(LogLevel.Information, OperatorConsoleFormatter.Verbatim, lines, null,
            static (state, _) => state);
}
