using System.IO;
using Microsoft.Data.Sqlite;
using Sv.TimeTracker.Data;
using Sv.TimeTracker.Data.Repositories;

namespace Sv.TimeTracker.Tests.TestInfrastructure;

public sealed class TempDatabaseFixture : IDisposable
{
    public string DatabasePath { get; }
    public SqliteConnectionFactory ConnectionFactory { get; }
    public WorkItemRepository WorkItems { get; }
    public TimeEntryRepository TimeEntries { get; }
    public SettingsRepository Settings { get; }

    public TempDatabaseFixture(bool seedStarterWorkItems = false)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SvTimeTrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        DatabasePath = Path.Combine(tempDir, "test_timetracker.db");

        ConnectionFactory = new SqliteConnectionFactory(DatabasePath);
        ConnectionFactory.Initialize(seedStarterWorkItems);

        WorkItems = new WorkItemRepository(ConnectionFactory);
        TimeEntries = new TimeEntryRepository(ConnectionFactory);
        Settings = new SettingsRepository(ConnectionFactory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Ignore transient cleanup lock
            }
        }
    }
}

public sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    private readonly TimeZoneInfo _localTimeZone;

    public AdjustableTimeProvider(DateTimeOffset initialUtcNow, TimeZoneInfo? localTimeZone = null)
    {
        _utcNow = initialUtcNow.ToUniversalTime();
        _localTimeZone = localTimeZone ?? TimeZoneInfo.Local;
    }

    public override TimeZoneInfo LocalTimeZone => _localTimeZone;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}
