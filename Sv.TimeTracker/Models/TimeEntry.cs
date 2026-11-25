namespace Sv.TimeTracker.Models;

public class TimeEntry
{
    public long Id { get; set; }
    public long WorkItemId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public long DurationMs { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this entry is currently running (no end time).
    /// </summary>
    public bool IsRunning => EndTime == null;

    public long GetEffectiveDurationMs(DateTime utcNow)
    {
        if (!IsRunning)
        {
            return Math.Max(0, DurationMs);
        }

        var startUtc = StartTime.Kind == DateTimeKind.Utc
            ? StartTime
            : StartTime.ToUniversalTime();
        var liveMs = (long)Math.Max(0, (utcNow - startUtc).TotalMilliseconds);
        return Math.Max(0, DurationMs + liveMs);
    }
}
