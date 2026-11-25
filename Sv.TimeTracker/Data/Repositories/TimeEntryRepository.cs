using System.Globalization;
using Microsoft.Data.Sqlite;
using Sv.TimeTracker.Models;

namespace Sv.TimeTracker.Data.Repositories;

public class TimeEntryRepository : ITimeEntryRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public TimeEntryRepository(ISqliteConnectionFactory? connectionFactory = null)
    {
        _connectionFactory = connectionFactory ?? DatabaseHelper.DefaultFactory;
    }

    public async Task<TimeEntry?> GetByIdAsync(long id)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, WorkItemId, StartTime, EndTime, DurationMs, Notes, CreatedAt
            FROM TimeEntries
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapTimeEntry(reader) : null;
    }

    public async Task<IEnumerable<TimeEntry>> GetAllAsync()
    {
        var entries = new List<TimeEntry>();
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, WorkItemId, StartTime, EndTime, DurationMs, Notes, CreatedAt
            FROM TimeEntries
            ORDER BY StartTime DESC";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(MapTimeEntry(reader));
        }

        return entries;
    }

    public async Task<IEnumerable<TimeEntry>> GetByDateRangeAsync(DateTime localStartDate, DateTime localEndDate)
    {
        var (startUtc, endExclusiveUtc) = GetUtcRangeForLocalDates(localStartDate, localEndDate);

        var entries = new List<TimeEntry>();
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, WorkItemId, StartTime, EndTime, DurationMs, Notes, CreatedAt
            FROM TimeEntries
            WHERE StartTime >= @StartUtc AND StartTime < @EndExclusiveUtc
            ORDER BY StartTime DESC";
        cmd.Parameters.AddWithValue("@StartUtc", DatabaseHelper.FormatUtc(startUtc));
        cmd.Parameters.AddWithValue("@EndExclusiveUtc", DatabaseHelper.FormatUtc(endExclusiveUtc));

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(MapTimeEntry(reader));
        }

        return entries;
    }

    public async Task<IEnumerable<TimeEntry>> GetByWorkItemAsync(long workItemId)
    {
        var entries = new List<TimeEntry>();
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, WorkItemId, StartTime, EndTime, DurationMs, Notes, CreatedAt
            FROM TimeEntries
            WHERE WorkItemId = @WorkItemId
            ORDER BY StartTime DESC";
        cmd.Parameters.AddWithValue("@WorkItemId", workItemId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(MapTimeEntry(reader));
        }

        return entries;
    }

    public async Task<TimeEntry> CreateAsync(TimeEntry entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO TimeEntries (WorkItemId, StartTime, EndTime, DurationMs, Notes, CreatedAt)
            VALUES (@WorkItemId, @StartTime, @EndTime, @DurationMs, @Notes, @CreatedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@WorkItemId", entity.WorkItemId);
        cmd.Parameters.AddWithValue("@StartTime", DatabaseHelper.FormatUtc(entity.StartTime));
        cmd.Parameters.AddWithValue("@EndTime",
            entity.EndTime.HasValue ? DatabaseHelper.FormatUtc(entity.EndTime.Value) : DBNull.Value);
        cmd.Parameters.AddWithValue("@DurationMs", entity.DurationMs);
        cmd.Parameters.AddWithValue("@Notes",
            string.IsNullOrWhiteSpace(entity.Notes) ? DBNull.Value : entity.Notes.Trim());
        cmd.Parameters.AddWithValue("@CreatedAt", DatabaseHelper.FormatUtc(entity.CreatedAt));

        var scalar = await cmd.ExecuteScalarAsync();
        entity.Id = Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
        return entity;
    }

    public async Task UpdateAsync(TimeEntry entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE TimeEntries
            SET WorkItemId = @WorkItemId,
                StartTime = @StartTime,
                EndTime = @EndTime,
                DurationMs = @DurationMs,
                Notes = @Notes
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@WorkItemId", entity.WorkItemId);
        cmd.Parameters.AddWithValue("@StartTime", DatabaseHelper.FormatUtc(entity.StartTime));
        cmd.Parameters.AddWithValue("@EndTime",
            entity.EndTime.HasValue ? DatabaseHelper.FormatUtc(entity.EndTime.Value) : DBNull.Value);
        cmd.Parameters.AddWithValue("@DurationMs", entity.DurationMs);
        cmd.Parameters.AddWithValue("@Notes",
            string.IsNullOrWhiteSpace(entity.Notes) ? DBNull.Value : entity.Notes.Trim());
        cmd.Parameters.AddWithValue("@Id", entity.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(long id)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM TimeEntries WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<TimeEntry?> GetRunningEntryAsync()
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, WorkItemId, StartTime, EndTime, DurationMs, Notes, CreatedAt
            FROM TimeEntries
            WHERE EndTime IS NULL
            ORDER BY StartTime DESC
            LIMIT 1";

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapTimeEntry(reader) : null;
    }

    public async Task<IReadOnlyList<EnrichedTimeEntry>> GetEnrichedEntriesAsync(
        DateTime? localStartDate = null,
        DateTime? localEndDate = null,
        long? workItemId = null)
    {
        var results = new List<EnrichedTimeEntry>();
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();

        var clauses = new List<string>();
        if (localStartDate.HasValue && localEndDate.HasValue)
        {
            var (startUtc, endExclusiveUtc) = GetUtcRangeForLocalDates(localStartDate.Value, localEndDate.Value);
            clauses.Add("t.StartTime >= @StartUtc AND t.StartTime < @EndExclusiveUtc");
            cmd.Parameters.AddWithValue("@StartUtc", DatabaseHelper.FormatUtc(startUtc));
            cmd.Parameters.AddWithValue("@EndExclusiveUtc", DatabaseHelper.FormatUtc(endExclusiveUtc));
        }
        else if (localStartDate.HasValue)
        {
            var startLocal = DateTime.SpecifyKind(localStartDate.Value.Date, DateTimeKind.Local);
            clauses.Add("t.StartTime >= @StartUtc");
            cmd.Parameters.AddWithValue("@StartUtc", DatabaseHelper.FormatUtc(startLocal.ToUniversalTime()));
        }
        else if (localEndDate.HasValue)
        {
            var endLocal = DateTime.SpecifyKind(localEndDate.Value.Date.AddDays(1), DateTimeKind.Local);
            clauses.Add("t.StartTime < @EndExclusiveUtc");
            cmd.Parameters.AddWithValue("@EndExclusiveUtc", DatabaseHelper.FormatUtc(endLocal.ToUniversalTime()));
        }

        if (workItemId.HasValue && workItemId.Value > 0)
        {
            clauses.Add("t.WorkItemId = @WorkItemId");
            cmd.Parameters.AddWithValue("@WorkItemId", workItemId.Value);
        }

        var whereSql = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : string.Empty;
        cmd.CommandText = $@"
            SELECT t.Id,
                   t.WorkItemId,
                   COALESCE(w.Title, 'Deleted Work Item') AS WorkItemTitle,
                   COALESCE(w.Color, '#6B7280') AS WorkItemColor,
                   t.StartTime,
                   t.EndTime,
                   t.DurationMs,
                   t.Notes
            FROM TimeEntries t
            LEFT JOIN WorkItems w ON w.Id = t.WorkItemId
            {whereSql}
            ORDER BY t.StartTime DESC";

        var utcNow = DateTime.UtcNow;

        // Open entries accrue live time only from the active (unpaused) segment.
        long pausedEntryId = 0;
        DateTime? activeSegmentStartUtc = null;
        using (var settingsCmd = connection.CreateCommand())
        {
            settingsCmd.CommandText = "SELECT Key, Value FROM Settings WHERE Key IN (@Paused, @Segment)";
            settingsCmd.Parameters.AddWithValue("@Paused", SettingKeys.PausedEntryId);
            settingsCmd.Parameters.AddWithValue("@Segment", SettingKeys.ActiveSegmentStartUtc);
            using var settingsReader = await settingsCmd.ExecuteReaderAsync();
            while (await settingsReader.ReadAsync())
            {
                var key = settingsReader.GetString(0);
                var value = settingsReader.IsDBNull(1) ? string.Empty : settingsReader.GetString(1);
                if (key == SettingKeys.PausedEntryId)
                {
                    long.TryParse(value, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out pausedEntryId);
                }
                else if (!string.IsNullOrWhiteSpace(value))
                {
                    activeSegmentStartUtc = DatabaseHelper.ParseUtc(value);
                }
            }
        }

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var startUtc = DatabaseHelper.ParseUtc(reader.GetString(4));
            var endUtc = reader.IsDBNull(5) ? (DateTime?)null : DatabaseHelper.ParseUtc(reader.GetString(5));
            var storedDurationMs = reader.GetInt64(6);
            var entryId = reader.GetInt64(0);
            var effectiveDurationMs = storedDurationMs;
            if (!endUtc.HasValue && entryId != pausedEntryId)
            {
                var segmentStart = activeSegmentStartUtc ?? startUtc;
                effectiveDurationMs += (long)Math.Max(0, (utcNow - segmentStart).TotalMilliseconds);
            }

            results.Add(new EnrichedTimeEntry
            {
                Id = reader.GetInt64(0),
                WorkItemId = reader.GetInt64(1),
                WorkItemTitle = reader.GetString(2),
                WorkItemColor = reader.GetString(3),
                StartTimeUtc = startUtc,
                EndTimeUtc = endUtc,
                DurationMs = effectiveDurationMs,
                Notes = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<WorkItemTotalSummary>> GetTotalsByWorkItemAsync(
        DateTime? localStartDate = null,
        DateTime? localEndDate = null)
    {
        var entries = await GetEnrichedEntriesAsync(localStartDate, localEndDate, null);

        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Color FROM WorkItems ORDER BY Id ASC";

        var workItems = new Dictionary<long, (string Title, string Color)>();
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                workItems[reader.GetInt64(0)] = (reader.GetString(1), reader.GetString(2));
            }
        }

        var grouped = entries
            .GroupBy(e => e.WorkItemId)
            .ToDictionary(
                g => g.Key,
                g => (
                    TotalMs: g.Sum(x => x.DurationMs),
                    Count: g.Count(),
                    Title: g.First().WorkItemTitle,
                    Color: g.First().WorkItemColor));

        foreach (var (id, meta) in workItems)
        {
            if (!grouped.ContainsKey(id))
            {
                grouped[id] = (0, 0, meta.Title, meta.Color);
            }
        }

        var grandTotalMs = grouped.Values.Sum(v => v.TotalMs);

        return grouped
            .Select(kvp => new WorkItemTotalSummary(
                WorkItemId: kvp.Key,
                Title: kvp.Value.Title,
                Color: kvp.Value.Color,
                TotalMs: kvp.Value.TotalMs,
                EntryCount: kvp.Value.Count,
                ShareRatio: grandTotalMs > 0 ? (double)kvp.Value.TotalMs / grandTotalMs : 0.0))
            .OrderByDescending(x => x.TotalMs)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<DailyTotalSummary>> GetTotalsByDayAsync(
        DateTime? localStartDate = null,
        DateTime? localEndDate = null,
        long? workItemId = null)
    {
        var entries = await GetEnrichedEntriesAsync(localStartDate, localEndDate, workItemId);

        return entries
            .GroupBy(e => e.LocalStartTime.Date)
            .Select(g => new DailyTotalSummary(
                Date: g.Key,
                TotalMs: g.Sum(x => x.DurationMs),
                EntryCount: g.Count()))
            .OrderByDescending(d => d.Date)
            .ToList();
    }

    private static (DateTime StartUtc, DateTime EndExclusiveUtc) GetUtcRangeForLocalDates(
        DateTime localStartDate,
        DateTime localEndDate)
    {
        var startDateOnly = localStartDate.Date;
        var endDateOnly = localEndDate.Date;
        if (endDateOnly < startDateOnly)
        {
            (startDateOnly, endDateOnly) = (endDateOnly, startDateOnly);
        }

        var startLocal = DateTime.SpecifyKind(startDateOnly, DateTimeKind.Local);
        var endExclusiveLocal = DateTime.SpecifyKind(endDateOnly.AddDays(1), DateTimeKind.Local);

        return (startLocal.ToUniversalTime(), endExclusiveLocal.ToUniversalTime());
    }

    private static TimeEntry MapTimeEntry(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        WorkItemId = reader.GetInt64(1),
        StartTime = DatabaseHelper.ParseUtc(reader.GetString(2)),
        EndTime = reader.IsDBNull(3) ? null : DatabaseHelper.ParseUtc(reader.GetString(3)),
        DurationMs = reader.GetInt64(4),
        Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
        CreatedAt = DatabaseHelper.ParseUtc(reader.GetString(6))
    };
}
