using Sv.TimeTracker.Models;
using Sv.TimeTracker.Tests.TestInfrastructure;
using Xunit;

namespace Sv.TimeTracker.Tests.Data;

public class RepositoryTests
{
    [Fact]
    public async Task Initialize_WithStarterSeed_SeedsStarterWorkItemsAndDefaultSettingsIdempotently()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: true);

        var items = (await db.WorkItems.GetAllAsync()).ToList();
        Assert.Equal(3, items.Count);

        var defaultId = await db.Settings.GetLongAsync(SettingKeys.DefaultWorkItemId);
        Assert.Equal(items[0].Id, defaultId);

        // Re-running Initialize must not duplicate starter items
        db.ConnectionFactory.Initialize(seedStarterWorkItems: true);
        var itemsAfterReinit = (await db.WorkItems.GetAllAsync()).ToList();
        Assert.Equal(3, itemsAfterReinit.Count);
    }

    [Fact]
    public async Task WorkItemRepository_CrudAndCascadingDelete_RemovesDependentTimeEntries()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: false);

        var created = await db.WorkItems.CreateAsync(new WorkItem
        {
            Title = "Architecture Design",
            Description = "Drafting service specs",
            Color = "#8B5CF6"
        });
        Assert.True(created.Id > 0);

        created.Title = "System Architecture";
        await db.WorkItems.UpdateAsync(created);

        var fetched = await db.WorkItems.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("System Architecture", fetched.Title);
        Assert.Equal("Drafting service specs", fetched.Description);

        await db.TimeEntries.CreateAsync(new TimeEntry
        {
            WorkItemId = created.Id,
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow,
            DurationMs = 3_600_000
        });

        await db.WorkItems.DeleteAsync(created.Id);

        Assert.Null(await db.WorkItems.GetByIdAsync(created.Id));
        Assert.Empty(await db.TimeEntries.GetAllAsync());
    }

    [Fact]
    public async Task TimeEntryRepository_GetByDateRangeAsync_IncludesEntriesLoggedToday()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: false);
        var item = await db.WorkItems.CreateAsync(new WorkItem { Title = "Bug Triage", Color = "#10B981" });

        var localToday = DateTime.Now.Date;
        var localAfternoon = DateTime.SpecifyKind(localToday.AddHours(14).AddMinutes(30), DateTimeKind.Local);
        var localYesterday = DateTime.SpecifyKind(localToday.AddDays(-1).AddHours(11), DateTimeKind.Local);

        await db.TimeEntries.CreateAsync(new TimeEntry
        {
            WorkItemId = item.Id,
            StartTime = localYesterday.ToUniversalTime(),
            EndTime = localYesterday.AddHours(1).ToUniversalTime(),
            DurationMs = 3_600_000,
            Notes = "Yesterday session"
        });

        var todayEntry = await db.TimeEntries.CreateAsync(new TimeEntry
        {
            WorkItemId = item.Id,
            StartTime = localAfternoon.ToUniversalTime(),
            EndTime = localAfternoon.AddMinutes(45).ToUniversalTime(),
            DurationMs = 2_700_000,
            Notes = "Today afternoon session"
        });

        var todayResults = (await db.TimeEntries.GetByDateRangeAsync(localToday, localToday)).ToList();
        Assert.Single(todayResults);
        Assert.Equal(todayEntry.Id, todayResults[0].Id);
    }

    [Fact]
    public async Task TimeEntryRepository_GetTotalsByWorkItemAndDay_ReturnsAllRowsAndAccurateShares()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: false);
        var taskA = await db.WorkItems.CreateAsync(new WorkItem { Title = "Task A", Color = "#0078D4" });
        var taskB = await db.WorkItems.CreateAsync(new WorkItem { Title = "Task B", Color = "#F59E0B" });
        var taskUnused = await db.WorkItems.CreateAsync(new WorkItem { Title = "Task C", Color = "#EC4899" });

        var baseLocal = DateTime.SpecifyKind(DateTime.Now.Date.AddHours(9), DateTimeKind.Local);

        await db.TimeEntries.CreateAsync(new TimeEntry
        {
            WorkItemId = taskA.Id,
            StartTime = baseLocal.ToUniversalTime(),
            EndTime = baseLocal.AddHours(3).ToUniversalTime(),
            DurationMs = 3 * 3_600_000
        });

        await db.TimeEntries.CreateAsync(new TimeEntry
        {
            WorkItemId = taskB.Id,
            StartTime = baseLocal.AddHours(4).ToUniversalTime(),
            EndTime = baseLocal.AddHours(5).ToUniversalTime(),
            DurationMs = 1 * 3_600_000
        });

        var totalsByTask = await db.TimeEntries.GetTotalsByWorkItemAsync();
        Assert.Equal(3, totalsByTask.Count);

        Assert.Equal(taskA.Id, totalsByTask[0].WorkItemId);
        Assert.Equal(3 * 3_600_000, totalsByTask[0].TotalMs);
        Assert.Equal(75.0, totalsByTask[0].SharePercentage);

        Assert.Equal(taskB.Id, totalsByTask[1].WorkItemId);
        Assert.Equal(1 * 3_600_000, totalsByTask[1].TotalMs);
        Assert.Equal(25.0, totalsByTask[1].SharePercentage);

        Assert.Equal(taskUnused.Id, totalsByTask[2].WorkItemId);
        Assert.Equal(0, totalsByTask[2].TotalMs);

        var dailyTotals = await db.TimeEntries.GetTotalsByDayAsync(baseLocal.Date, baseLocal.Date);
        Assert.Single(dailyTotals);
        Assert.Equal(4 * 3_600_000, dailyTotals[0].TotalMs);
        Assert.Equal(2, dailyTotals[0].EntryCount);
    }

    [Fact]
    public async Task SettingsRepository_TypedGettersAndUpsert_PersistAccurately()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: false);

        await db.Settings.SetAsync(SettingKeys.HourlyDingVolume, "85");
        await db.Settings.SetAsync(SettingKeys.TopmostEnabled, "false");
        await db.Settings.SetAsync(SettingKeys.WindowWidth, "512.5");

        Assert.Equal(85, await db.Settings.GetIntAsync(SettingKeys.HourlyDingVolume));
        Assert.False(await db.Settings.GetBoolAsync(SettingKeys.TopmostEnabled, true));
        Assert.Equal(512.5, await db.Settings.GetDoubleAsync(SettingKeys.WindowWidth));
    }
}
