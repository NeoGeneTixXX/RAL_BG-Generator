namespace RalBgGenerator.Models;

public enum LogLevel
{
    Info,
    Success,
    Skipped,
    Warning,
    Error
}

public sealed class LogEntry
{
    public LogEntry(LogLevel level, string message)
    {
        Level = level;
        Message = message;
        Timestamp = DateTime.Now;
    }

    public LogLevel Level { get; }

    public string Message { get; }

    public DateTime Timestamp { get; }

    public string Display => $"{Timestamp:HH:mm:ss}  {Message}";
}
