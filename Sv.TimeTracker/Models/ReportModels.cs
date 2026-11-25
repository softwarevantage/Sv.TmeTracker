namespace Sv.TimeTracker.Models;

public static class DurationFormatter
{
    /// <summary>
    /// Formats a TimeSpan as HH:MM:SS without wrapping modulo 24 hours.
    /// </summary>
    public static string Format(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var totalHours = (long)Math.Floor(duration.TotalHours);
        return $"{totalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
    }

    public static string FormatMilliseconds(long durationMs) =>
        Format(TimeSpan.FromMilliseconds(Math.Max(0, durationMs)));

    public static string FormatHumanReadable(long durationMs)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, durationMs));
        var totalHours = (long)Math.Floor(span.TotalHours);
        if (totalHours > 0)
        {
            return $"{totalHours}h {span.Minutes:D2}m";
        }

        return span.Minutes > 0
            ? $"{span.Minutes}m {span.Seconds:D2}s"
            : $"{span.Seconds}s";
    }
}

public class EnrichedTimeEntry
{
    public long Id { get; set; }
    public long WorkItemId { get; set; }
    public string WorkItemTitle { get; set; } = string.Empty;
    public string WorkItemColor { get; set; } = "#0078D4";
    public DateTime StartTimeUtc { get; set; }
    public DateTime? EndTimeUtc { get; set; }
    public long DurationMs { get; set; }
    public string? Notes { get; set; }

    public DateTime LocalStartTime => StartTimeUtc.Kind == DateTimeKind.Utc
        ? StartTimeUtc.ToLocalTime()
        : StartTimeUtc;

    public DateTime? LocalEndTime => EndTimeUtc.HasValue
        ? (EndTimeUtc.Value.Kind == DateTimeKind.Utc ? EndTimeUtc.Value.ToLocalTime() : EndTimeUtc.Value)
        : null;

    public bool IsRunning => !EndTimeUtc.HasValue;

    public string DurationFormatted => DurationFormatter.FormatMilliseconds(DurationMs);

    public string TimeRangeDisplay => LocalEndTime.HasValue
        ? $"{LocalStartTime:MMM d, yyyy  h:mm tt} – {LocalEndTime.Value:h:mm tt}"
        : $"{LocalStartTime:MMM d, yyyy  h:mm tt} – Running";
}

public record DailyTotalSummary(
    DateTime Date,
    long TotalMs,
    int EntryCount)
{
    public string DateDisplay => Date.ToString("ddd, MMM d, yyyy");
    public string TotalFormatted => DurationFormatter.FormatMilliseconds(TotalMs);
    public string EntryCountDisplay => EntryCount == 1 ? "1 session" : $"{EntryCount} sessions";
}

public record WorkItemTotalSummary(
    long WorkItemId,
    string Title,
    string Color,
    long TotalMs,
    int EntryCount,
    double ShareRatio)
{
    public string TotalFormatted => DurationFormatter.FormatMilliseconds(TotalMs);
    public double SharePercentage => Math.Round(ShareRatio * 100.0, 1);
    public string ShareDisplay => $"{SharePercentage:0.#}%";
}
