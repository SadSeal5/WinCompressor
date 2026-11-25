namespace WinCompressor.UI.ViewModels;

public enum ActivityLogLevel
{
    Info,
    Warning,
    Error
}

/// <summary>
/// A single activity-log entry with severity level and display formatting.
/// </summary>
public class ActivityLogEntry
{
    public ActivityLogEntry(string message, ActivityLogLevel level = ActivityLogLevel.Info)
    {
        Message = message ?? string.Empty;
        Level = level;
        Timestamp = DateTime.Now;
    }

    public DateTime Timestamp { get; }

    public string Message { get; }

    public ActivityLogLevel Level { get; }

    public string LevelMarker => Level switch
    {
        ActivityLogLevel.Warning => "⚠ ",
        ActivityLogLevel.Error => "✖ ",
        _ => string.Empty
    };

    /// <summary>Ready-to-render line, e.g. "[14:05:49] ⚠ Mesaj".</summary>
    public string DisplayText => $"[{Timestamp:HH:mm:ss}] {LevelMarker}{Message}";
}
