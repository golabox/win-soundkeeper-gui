using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SoundKeeper.GUI.Services;

public enum LogLevelFilter
{
    All,
    WarningsAndErrors,
    Errors
}

public readonly record struct LogView(string DisplayText, string RawText, int EntryCount);

/// <summary>
/// Display-only transformation of the log file content: compact HH:mm:ss timestamps grouped under a
/// date line, and an optional level filter. The file itself is never rewritten; RawText keeps the
/// original lines (full ISO timestamps) of the entries that pass the filter.
/// </summary>
public static partial class LogViewFormatter
{
    private const string Indent = "               ";

    public static LogView Format(string content, LogLevelFilter filter, CultureInfo culture)
    {
        var display = new StringBuilder();
        var raw = new StringBuilder();
        var count = 0;
        DateTime? currentDate = null;

        foreach (var entry in Parse(content))
        {
            if (!Matches(entry.Level, filter)) continue;
            count++;

            if (entry.Timestamp is { } timestamp && timestamp.Date != currentDate)
            {
                if (display.Length > 0) display.AppendLine();
                var date = timestamp.ToString("D", culture);
                display.AppendLine(date.Length > 0 ? char.ToUpper(date[0], culture) + date[1..] : date);
                currentDate = timestamp.Date;
            }

            var time = entry.Timestamp?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "--:--:--";
            display.Append(time).Append(' ').Append(entry.Level.PadRight(5)).Append(' ').AppendLine(entry.Message);
            foreach (var continuation in entry.Continuation)
            {
                display.Append(Indent).AppendLine(continuation);
            }

            raw.AppendLine(entry.RawFirstLine);
            foreach (var continuation in entry.Continuation) raw.AppendLine(continuation);
        }

        return new LogView(display.ToString().TrimEnd(), raw.ToString().TrimEnd(), count);
    }

    private static bool Matches(string level, LogLevelFilter filter) => filter switch
    {
        LogLevelFilter.Errors => level == "ERROR",
        LogLevelFilter.WarningsAndErrors => level is "WARN" or "ERROR",
        _ => true
    };

    private static IEnumerable<LogEntry> Parse(string content)
    {
        LogEntry? current = null;
        foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
        {
            var match = EntryPattern().Match(line);
            if (match.Success)
            {
                if (current is not null) yield return current;
                DateTime? timestamp = DateTimeOffset.TryParse(match.Groups["time"].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed.DateTime
                    : null;
                current = new LogEntry(timestamp, match.Groups["level"].Value, match.Groups["message"].Value, line);
            }
            else if (line.Length > 0)
            {
                // Continuation of a multi-line message (exception details) or an unrecognized line.
                current ??= new LogEntry(null, "INFO", string.Empty, string.Empty);
                current.Continuation.Add(line);
            }
        }

        if (current is not null) yield return current;
    }

    [GeneratedRegex(@"^(?<time>\d{4}-\d{2}-\d{2}T\S+) \[(?<level>INFO|WARN|ERROR)\] (?<message>.*)$")]
    private static partial Regex EntryPattern();

    private sealed record LogEntry(DateTime? Timestamp, string Level, string Message, string RawFirstLine)
    {
        public List<string> Continuation { get; } = [];
    }
}
