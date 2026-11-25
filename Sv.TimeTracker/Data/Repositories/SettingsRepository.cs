using System.Globalization;
using Sv.TimeTracker.Models;

namespace Sv.TimeTracker.Data.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SettingsRepository(ISqliteConnectionFactory? connectionFactory = null)
    {
        _connectionFactory = connectionFactory ?? DatabaseHelper.DefaultFactory;
    }

    public async Task<IEnumerable<AppSettings>> GetAllAsync()
    {
        var settings = new List<AppSettings>();
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM Settings ORDER BY Key ASC";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            settings.Add(new AppSettings
            {
                Key = reader.GetString(0),
                Value = reader.GetString(1)
            });
        }

        return settings;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllDictionaryAsync()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM Settings";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            dict[reader.GetString(0)] = reader.GetString(1);
        }

        return dict;
    }

    public async Task<string?> GetAsync(string key)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @Key";
        cmd.Parameters.AddWithValue("@Key", key);

        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : result.ToString();
    }

    public async Task SetAsync(string key, string value)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Settings (Key, Value)
            VALUES (@Key, @Value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
        cmd.Parameters.AddWithValue("@Key", key);
        cmd.Parameters.AddWithValue("@Value", value ?? string.Empty);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> GetBoolAsync(string key, bool defaultValue = false)
    {
        var value = await GetAsync(key);
        return bool.TryParse(value, out var result) ? result : defaultValue;
    }

    public async Task<int> GetIntAsync(string key, int defaultValue = 0)
    {
        var value = await GetAsync(key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    public async Task<long> GetLongAsync(string key, long defaultValue = 0)
    {
        var value = await GetAsync(key);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    public async Task<double?> GetDoubleAsync(string key)
    {
        var value = await GetAsync(key);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
