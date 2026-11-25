using Sv.TimeTracker.Models;
using Sv.TimeTracker.Services;
using Sv.TimeTracker.Tests.TestInfrastructure;
using Xunit;

namespace Sv.TimeTracker.Tests.Services;

public class TimerServiceTests
{
    [Fact]
    public async Task StartAdvanceAndStop_PersistsExactElapsedDuration()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: false);
        var item = await db.WorkItems.CreateAsync(new WorkItem { Title = "API Refactor" });

        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 24, 15, 0, 0, TimeSpan.Zero));
        using var timer = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings);

        await timer.StartAsync(item.Id, "Initial implementation");
        Assert.True(timer.IsRunning);
        Assert.Equal("API Refactor", await timer.GetRunningWorkItemTitleAsync());

        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(25) + TimeSpan.FromSeconds(10));
        Assert.Equal("01:25:10", DurationFormatter.Format(timer.Elapsed));

        await timer.UpdateRunningNotesAsync("Completed API endpoints");
        await timer.StopAsync();

        Assert.False(timer.IsRunning);
        Assert.Null(timer.RunningEntry);

        var saved = (await db.TimeEntries.GetAllAsync()).Single();
        Assert.NotNull(saved.EndTime);
        Assert.Equal((1 * 3600 + 25 * 60 + 10) * 1000L, saved.DurationMs);
        Assert.Equal("Completed API endpoints", saved.Notes);
    }

    [Fact]
    public async Task StartNewWorkItemWhileRunning_StopsPreviousEntryAndStartsNewOne()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: false);
        var first = await db.WorkItems.CreateAsync(new WorkItem { Title = "Task 1" });
        var second = await db.WorkItems.CreateAsync(new WorkItem { Title = "Task 2" });

        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero));
        using var timer = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings);

        await timer.StartAsync(first.Id);
        clock.Advance(TimeSpan.FromMinutes(30));

        await timer.StartAsync(second.Id);
        clock.Advance(TimeSpan.FromMinutes(15));
        await timer.StopAsync();

        var entries = (await db.TimeEntries.GetAllAsync()).OrderBy(e => e.StartTime).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(first.Id, entries[0].WorkItemId);
        Assert.Equal(30 * 60 * 1000L, entries[0].DurationMs);
        Assert.Equal(second.Id, entries[1].WorkItemId);
        Assert.Equal(15 * 60 * 1000L, entries[1].DurationMs);
    }

    [Fact]
    public async Task InitializeAsync_ResumesUnfinishedSessionAcrossAppRestart()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: false);
        var item = await db.WorkItems.CreateAsync(new WorkItem { Title = "Long Running Build" });

        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero));
        using (var firstSessionTimer = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings))
        {
            await firstSessionTimer.StartAsync(item.Id, "Started before reboot");
        }

        // Simulate 2 hours passing while app was closed
        clock.Advance(TimeSpan.FromHours(2));

        using var restartedTimer = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings);
        await restartedTimer.InitializeAsync();

        Assert.True(restartedTimer.IsRunning);
        Assert.Equal(item.Id, restartedTimer.RunningEntry?.WorkItemId);
        Assert.Equal(2 * 3_600_000L, restartedTimer.ElapsedMilliseconds);
    }

    [Fact]
    public async Task PauseAndResume_ExcludesPausedIntervalFromTotalDuration()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: false);
        var item = await db.WorkItems.CreateAsync(new WorkItem { Title = "Deep Work" });

        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero));
        using var timer = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings);

        await timer.StartAsync(item.Id);
        clock.Advance(TimeSpan.FromMinutes(10));

        await timer.PauseAsync();
        Assert.False(timer.IsRunning);
        Assert.True(timer.IsPaused);
        Assert.True(timer.HasActiveSession);
        Assert.Equal(10 * 60_000L, timer.ElapsedMilliseconds);

        // 20 minutes pass while paused -> elapsed must remain frozen at 10 minutes
        clock.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(10 * 60_000L, timer.ElapsedMilliseconds);

        // Resume and work 5 more minutes
        await timer.ResumeAsync();
        Assert.True(timer.IsRunning);
        Assert.False(timer.IsPaused);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(15 * 60_000L, timer.ElapsedMilliseconds);

        await timer.StopAsync();
        Assert.False(timer.HasActiveSession);

        var saved = (await db.TimeEntries.GetAllAsync()).Single();
        Assert.NotNull(saved.EndTime);
        Assert.Equal(15 * 60_000L, saved.DurationMs);
    }

    [Fact]
    public async Task InitializeAsync_RestoresPausedSessionWithoutCountingDowntime()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: false);
        var item = await db.WorkItems.CreateAsync(new WorkItem { Title = "Paused Task" });

        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero));
        using (var session1 = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings))
        {
            await session1.StartAsync(item.Id);
            clock.Advance(TimeSpan.FromMinutes(12));
            await session1.PauseAsync();
        }

        // 3 hours pass while app is closed
        clock.Advance(TimeSpan.FromHours(3));

        using var session2 = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings);
        await session2.InitializeAsync();

        Assert.False(session2.IsRunning);
        Assert.True(session2.IsPaused);
        Assert.Equal(12 * 60_000L, session2.ElapsedMilliseconds);

        // Stopping while paused finalizes with exact frozen 12 minutes
        await session2.StopAsync();
        var saved = (await db.TimeEntries.GetAllAsync()).Single();
        Assert.NotNull(saved.EndTime);
        Assert.Equal(12 * 60_000L, saved.DurationMs);
    }
}
