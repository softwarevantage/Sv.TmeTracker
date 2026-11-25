using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using Sv.TimeTracker.Models;

namespace Sv.TimeTracker.Data;

public interface ISqliteConnectionFactory
{
    string DatabasePath { get; }
    SqliteConnection CreateOpenConnection();
    void Initialize(bool seedStarterWorkItems = true);
}

public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _connectionString;

    public string DatabasePath { get; }

    public SqliteConnectionFactory(string? databasePath = null)
    {
        DatabaseHelper.EnsureProviderInitialized();

        DatabasePath = string.IsNullOrWhiteSpace(databasePath)
            ? DatabaseHelper.DefaultDbPath
            : databasePath;

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
            Pooling = true
        };
        _connectionString = builder.ToString();
    }

    public SqliteConnection CreateOpenConnection()
    {
        DatabaseHelper.EnsureProviderInitialized();

        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA foreign_keys = ON;";
        pragmaCmd.ExecuteNonQuery();

        return connection;
    }

    public void Initialize(bool seedStarterWorkItems = true)
    {
        using var connection = CreateOpenConnection();

        using (var walCmd = connection.CreateCommand())
        {
            walCmd.CommandText = "PRAGMA journal_mode = WAL;";
            walCmd.ExecuteNonQuery();
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS WorkItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Description TEXT,
                Color TEXT NOT NULL DEFAULT '#0078D4',
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS TimeEntries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                WorkItemId INTEGER NOT NULL,
                StartTime DATETIME NOT NULL,
                EndTime DATETIME,
                DurationMs INTEGER NOT NULL DEFAULT 0,
                Notes TEXT,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (WorkItemId) REFERENCES WorkItems(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_TimeEntries_StartTime ON TimeEntries(StartTime);
            CREATE INDEX IF NOT EXISTS IX_TimeEntries_WorkItemId ON TimeEntries(WorkItemId);

            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );";
        cmd.ExecuteNonQuery();

        SeedDefaultSettings(connection);

        if (seedStarterWorkItems)
        {
            SeedStarterWorkItemsIfEmpty(connection);
        }
    }

    private static void SeedDefaultSettings(SqliteConnection connection)
    {
        (string Key, string Value)[] defaults =
        [
            (SettingKeys.TopmostEnabled, "true"),
            (SettingKeys.MinimizeToTray, "true"),
            (SettingKeys.PersistWindowBounds, "true"),
            (SettingKeys.DefaultWorkItemId, "0"),
            (SettingKeys.LastSelectedWorkItemId, "0"),
            (SettingKeys.CompactModeEnabled, "false"),
            (SettingKeys.BackgroundOpacity, "80"),
            (SettingKeys.HourlyDingEnabled, "true"),
            (SettingKeys.HourlyDingVolume, "60"),
            (SettingKeys.HourlyDingSoundPath, string.Empty),
            (SettingKeys.QuietHoursStart, "22:00"),
            (SettingKeys.QuietHoursEnd, "07:00"),
            (SettingKeys.GlobalHotkeyEnabled, "true"),
            (SettingKeys.ToggleTimerHotkey, "Ctrl+Shift+T"),
            (SettingKeys.ShowHideHotkey, "Ctrl+Shift+H"),
            (SettingKeys.WorkItemsHotkey, "Ctrl+Shift+W")
        ];

        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO Settings (Key, Value) VALUES (@Key, @Value);";
        var keyParam = cmd.Parameters.Add("@Key", SqliteType.Text);
        var valParam = cmd.Parameters.Add("@Value", SqliteType.Text);

        foreach (var (key, value) in defaults)
        {
            keyParam.Value = key;
            valParam.Value = value;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static void SeedStarterWorkItemsIfEmpty(SqliteConnection connection)
    {
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(1) FROM WorkItems;";
        var existingCount = Convert.ToInt64(countCmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (existingCount > 0)
        {
            return;
        }

        (string Title, string Description, string Color)[] starters =
        [
            ("Deep Work", "Focused engineering, coding, and architecture", "#0078D4"),
            ("Meetings & Syncs", "Standups, stakeholder calls, and team syncs", "#8B5CF6"),
            ("Code Review & Planning", "PR reviews, triage, and sprint planning", "#10B981")
        ];

        using var tx = connection.BeginTransaction();
        long firstId = 0;
        var nowUtc = DatabaseHelper.FormatUtc(DateTime.UtcNow);

        foreach (var (title, description, color) in starters)
        {
            using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = @"
                INSERT INTO WorkItems (Title, Description, Color, CreatedAt)
                VALUES (@Title, @Description, @Color, @CreatedAt);
                SELECT last_insert_rowid();";
            insertCmd.Parameters.AddWithValue("@Title", title);
            insertCmd.Parameters.AddWithValue("@Description", description);
            insertCmd.Parameters.AddWithValue("@Color", color);
            insertCmd.Parameters.AddWithValue("@CreatedAt", nowUtc);

            var id = Convert.ToInt64(insertCmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (firstId == 0)
            {
                firstId = id;
            }
        }

        if (firstId > 0)
        {
            using var updateDefaultCmd = connection.CreateCommand();
            updateDefaultCmd.Transaction = tx;
            updateDefaultCmd.CommandText = @"
                UPDATE Settings SET Value = @Id
                WHERE Key = @Key AND Value = '0';";
            updateDefaultCmd.Parameters.AddWithValue("@Id", firstId.ToString(CultureInfo.InvariantCulture));
            updateDefaultCmd.Parameters.AddWithValue("@Key", SettingKeys.DefaultWorkItemId);
            updateDefaultCmd.ExecuteNonQuery();
        }

        tx.Commit();
    }
}

public static class DatabaseHelper
{
    private static int _providerInitialized;
    private static ISqliteConnectionFactory _defaultFactory = new SqliteConnectionFactory();

    public static string DefaultDbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Sv.TimeTracker",
        "timetracker.db");

    public static ISqliteConnectionFactory DefaultFactory => _defaultFactory;

    public static void ConfigureDefaultFactory(ISqliteConnectionFactory factory)
    {
        _defaultFactory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public static void EnsureProviderInitialized()
    {
        if (Interlocked.Exchange(ref _providerInitialized, 1) == 0)
        {
            SQLitePCL.Batteries_V2.Init();
        }
    }

    public static void Initialize(bool seedStarterWorkItems = true) =>
        _defaultFactory.Initialize(seedStarterWorkItems);

    public static SqliteConnection GetConnection() =>
        _defaultFactory.CreateOpenConnection();

    public static string FormatUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return utc.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
    }

    public static DateTime ParseUtc(string raw)
    {
        if (DateTime.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        return DateTime.SpecifyKind(DateTime.Parse(raw, CultureInfo.InvariantCulture), DateTimeKind.Utc);
    }
}
