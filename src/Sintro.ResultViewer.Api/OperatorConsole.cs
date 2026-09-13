using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Sintro.ResultViewer;

/// <summary>
/// The console format this service is read in: a window on a range PC, watched by someone who
/// runs a shooting event and not a server.
///
/// The default formatter writes "info: Sintro.ResultViewer.Api[0]" above every line. The category
/// and event id are there to be grepped by whoever operates a fleet of services; here they are
/// noise in front of the one sentence that matters, and they cost the message its own line. This
/// writes the time, the severity when there is one, and the message.
/// </summary>
public sealed class OperatorConsoleFormatter(IOptions<ConsoleFormatterOptions> options)
    : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "operator";

    /// <summary>
    /// Marks an entry as pre-formatted: written out exactly as composed, with no time and no
    /// severity in front of it. For the startup banner, whose alignment is the whole point.
    /// </summary>
    public static readonly EventId Verbatim = new(0, "Verbatim");

    // Colour is worth it for exactly one thing: a warning must not read like the rest of the
    // startup chatter. Skipped when the output is piped or redirected to a file, where escape
    // sequences would be written as literal characters.
    static readonly bool Colour = !Console.IsOutputRedirected;

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

        var time = DateTime.Now.ToString(options.Value.TimestampFormat ?? "HH:mm:ss");
        var (label, colour) = logEntry.LogLevel switch
        {
            LogLevel.Warning => ("WARNING  ", "\e[33m"),
            LogLevel.Error => ("ERROR    ", "\e[31m"),
            LogLevel.Critical => ("FAILED   ", "\e[31m"),
            _ => ("", ""),
        };

        if (Colour && colour.Length > 0) writer.Write(colour);
        writer.Write($"{time}  {label}");
        // Continuation lines are indented under the message rather than under the time, so a
        // multi-line explanation still reads as one entry.
        writer.Write(message.ReplaceLineEndings(Environment.NewLine + "          "));
        if (Colour && colour.Length > 0) writer.Write("\e[0m");
        writer.WriteLine();

        if (logEntry.Exception is null) return;

        // The message above is written for the operator; the stack trace underneath is for
        // whoever they send the window to. Only the type and message when it is a warning the
        // service recovered from — a stack trace would bury the part they can act on.
        writer.WriteLine(logEntry.LogLevel >= LogLevel.Error
            ? Indent(logEntry.Exception.ToString())
            : Indent($"{logEntry.Exception.GetType().Name}: {logEntry.Exception.Message}"));
    }

    static string Indent(string text) => "          " + text.ReplaceLineEndings(Environment.NewLine + "          ");
}

public static class OperatorConsoleExtensions
{
    /// <summary>Replaces the default console output with <see cref="OperatorConsoleFormatter"/>.</summary>
    public static ILoggingBuilder AddOperatorConsole(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddConsoleFormatter<OperatorConsoleFormatter, ConsoleFormatterOptions>();
        logging.AddConsole(console => console.FormatterName = OperatorConsoleFormatter.FormatterName);
        return logging;
    }

    /// <summary>Logs <paramref name="lines"/> exactly as given — no timestamp, no severity.</summary>
    public static void LogVerbatim(this ILogger logger, string lines) =>
        logger.Log(LogLevel.Information, OperatorConsoleFormatter.Verbatim, lines, null,
            static (state, _) => state);
}
