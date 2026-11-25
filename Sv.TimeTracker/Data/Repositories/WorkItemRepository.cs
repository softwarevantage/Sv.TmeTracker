using System.Globalization;
using Microsoft.Data.Sqlite;
using Sv.TimeTracker.Models;

namespace Sv.TimeTracker.Data.Repositories;

public class WorkItemRepository : IWorkItemRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public WorkItemRepository(ISqliteConnectionFactory? connectionFactory = null)
    {
        _connectionFactory = connectionFactory ?? DatabaseHelper.DefaultFactory;
    }

    public async Task<WorkItem?> GetByIdAsync(long id)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Description, Color, CreatedAt FROM WorkItems WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapWorkItem(reader);
        }

        return null;
    }

    public async Task<IEnumerable<WorkItem>> GetAllAsync()
    {
        var items = new List<WorkItem>();
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Description, Color, CreatedAt FROM WorkItems ORDER BY Id ASC";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(MapWorkItem(reader));
        }

        return items;
    }

    public async Task<WorkItem> CreateAsync(WorkItem entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO WorkItems (Title, Description, Color, CreatedAt)
            VALUES (@Title, @Description, @Color, @CreatedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@Title", entity.Title.Trim());
        cmd.Parameters.AddWithValue("@Description",
            string.IsNullOrWhiteSpace(entity.Description) ? DBNull.Value : entity.Description.Trim());
        cmd.Parameters.AddWithValue("@Color", string.IsNullOrWhiteSpace(entity.Color) ? "#0078D4" : entity.Color.Trim());
        cmd.Parameters.AddWithValue("@CreatedAt", DatabaseHelper.FormatUtc(entity.CreatedAt));

        var scalar = await cmd.ExecuteScalarAsync();
        entity.Id = Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
        return entity;
    }

    public async Task UpdateAsync(WorkItem entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE WorkItems
            SET Title = @Title, Description = @Description, Color = @Color
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Title", entity.Title.Trim());
        cmd.Parameters.AddWithValue("@Description",
            string.IsNullOrWhiteSpace(entity.Description) ? DBNull.Value : entity.Description.Trim());
        cmd.Parameters.AddWithValue("@Color", string.IsNullOrWhiteSpace(entity.Color) ? "#0078D4" : entity.Color.Trim());
        cmd.Parameters.AddWithValue("@Id", entity.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(long id)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var tx = connection.BeginTransaction();

        using (var deleteEntriesCmd = connection.CreateCommand())
        {
            deleteEntriesCmd.Transaction = tx;
            deleteEntriesCmd.CommandText = "DELETE FROM TimeEntries WHERE WorkItemId = @Id";
            deleteEntriesCmd.Parameters.AddWithValue("@Id", id);
            await deleteEntriesCmd.ExecuteNonQueryAsync();
        }

        using (var deleteItemCmd = connection.CreateCommand())
        {
            deleteItemCmd.Transaction = tx;
            deleteItemCmd.CommandText = "DELETE FROM WorkItems WHERE Id = @Id";
            deleteItemCmd.Parameters.AddWithValue("@Id", id);
            await deleteItemCmd.ExecuteNonQueryAsync();
        }

        tx.Commit();
    }

    private static WorkItem MapWorkItem(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Title = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        Color = reader.GetString(3),
        CreatedAt = DatabaseHelper.ParseUtc(reader.GetString(4))
    };
}
