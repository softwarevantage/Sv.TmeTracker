using Sv.TimeTracker.Models;

namespace Sv.TimeTracker.Data.Repositories;

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(long id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<T> CreateAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(long id);
}

public interface IWorkItemRepository : IRepository<WorkItem>
{
}

public interface ITimeEntryRepository : IRepository<TimeEntry>
{
    Task<IEnumerable<TimeEntry>> GetByDateRangeAsync(DateTime localStartDate, DateTime localEndDate);
    Task<IEnumerable<TimeEntry>> GetByWorkItemAsync(long workItemId);
    Task<TimeEntry?> GetRunningEntryAsync();
    Task<IReadOnlyList<EnrichedTimeEntry>> GetEnrichedEntriesAsync(
        DateTime? localStartDate = null,
        DateTime? localEndDate = null,
        long? workItemId = null);
    Task<IReadOnlyList<WorkItemTotalSummary>> GetTotalsByWorkItemAsync(
        DateTime? localStartDate = null,
        DateTime? localEndDate = null);
    Task<IReadOnlyList<DailyTotalSummary>> GetTotalsByDayAsync(
        DateTime? localStartDate = null,
        DateTime? localEndDate = null,
        long? workItemId = null);
}

public interface ISettingsRepository
{
    Task<IEnumerable<AppSettings>> GetAllAsync();
    Task<IReadOnlyDictionary<string, string>> GetAllDictionaryAsync();
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task<bool> GetBoolAsync(string key, bool defaultValue = false);
    Task<int> GetIntAsync(string key, int defaultValue = 0);
    Task<long> GetLongAsync(string key, long defaultValue = 0);
    Task<double?> GetDoubleAsync(string key);
}
