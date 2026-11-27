using Moq;
using Sv.TimeTracker.Models;
using Sv.TimeTracker.Services;
using Sv.TimeTracker.Tests.TestInfrastructure;
using Sv.TimeTracker.ViewModels;
using Xunit;

namespace Sv.TimeTracker.Tests.ViewModels;

public class ViewModelWorkflowTests
{
    [Fact]
    public async Task EndToEndViewModelFlow_CreateWorkItem_TrackSession_InspectHistory_AndSaveSettings()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: true);
        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 24, 16, 0, 0, TimeSpan.Zero));

        using var timerService = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings);
        var audioMock = new Mock<IAudioChimePlayer>();
        var notificationService = new NotificationService(timerService, db.Settings, audioMock.Object, clock);
        var hotkeyMock = new Mock<IHotkeyService>();
        hotkeyMock.SetupGet(h => h.RegistrationWarnings).Returns([]);
        var trayMock = new Mock<ITrayIconService>();
        var exportService = new ExportService(db.TimeEntries);

        var timerVm = new TimerViewModel(timerService, db.WorkItems, db.Settings);
        var workItemsVm = new WorkItemsViewModel(db.WorkItems);
        var historyVm = new HistoryViewModel(db.TimeEntries, db.WorkItems, exportService, clock, timerService);
        var settingsVm = new SettingsViewModel(db.Settings, db.WorkItems, notificationService, hotkeyMock.Object);

        var mainVm = new MainViewModel(
            timerVm,
            workItemsVm,
            historyVm,
            settingsVm,
            timerService,
            db.Settings,
            notificationService,
            trayMock.Object);

        await mainVm.InitializeAsync();
        Assert.Equal(3, timerVm.WorkItems.Count);
        Assert.NotNull(timerVm.SelectedWorkItem);

        // 1. Create a new work item in WorkItemsViewModel and confirm it syncs to Timer & History
        workItemsVm.OpenNewEditor();
        workItemsVm.DraftTitle = "Modernization Sprint";
        workItemsVm.DraftDescription = "Upgrade to .NET 10 and MVVM";
        workItemsVm.DraftColor = "#10B981";
        await workItemsVm.SaveEditorAsync();

        Assert.Equal(4, timerVm.WorkItems.Count);
        var createdItem = workItemsVm.Items.Single(i => i.Title == "Modernization Sprint");

        // 2. Activate & start immediately from WorkItemsViewModel
        await workItemsVm.StartNowAsync(createdItem);
        Assert.Equal(NavigationTab.Timer, mainVm.CurrentTab);
        Assert.True(timerVm.IsRunning);
        Assert.Equal(createdItem.Id, timerVm.SelectedWorkItem?.Id);

        // 3. Advance 45 minutes, add session notes, and stop timer
        clock.Advance(TimeSpan.FromMinutes(45));
        timerService.Tick();
        Assert.Equal("00:45:00", timerVm.ElapsedText);

        timerVm.SessionNotes = "Finished ViewModels and tests";
        await timerVm.ToggleTimerAsync();
        Assert.False(timerVm.IsRunning);

        // 4. Navigate to History and verify entries, work item totals, and note editing
        await mainVm.NavigateAsync("History");
        await historyVm.ApplyPresetAsync("All");

        Assert.Single(historyVm.Entries);
        Assert.Equal("00:45:00", historyVm.GrandTotalFormatted);
        Assert.Equal("Finished ViewModels and tests", historyVm.Entries[0].Notes);
        Assert.Equal(4, historyVm.WorkItemTotals.Count);

        historyVm.BeginEditEntryNotes(historyVm.Entries[0]);
        historyVm.EditingEntryNotes = "Updated note from History";
        await historyVm.SaveEntryNotesAsync();
        Assert.Equal("Updated note from History", historyVm.Entries[0].Notes);

        // 5. Validate dirty-state tracking and save settings
        await mainVm.NavigateAsync("Settings");
        Assert.False(settingsVm.HasUnsavedChanges);
        Assert.False(settingsVm.SaveSettingsCommand.CanExecute(null));

        // Changing a value enables Save; reverting it disables Save again
        var originalVolume = settingsVm.HourlyDingVolume;
        settingsVm.HourlyDingVolume = 90;
        Assert.True(settingsVm.HasUnsavedChanges);
        Assert.True(settingsVm.SaveSettingsCommand.CanExecute(null));

        settingsVm.HourlyDingVolume = originalVolume;
        Assert.False(settingsVm.HasUnsavedChanges);
        Assert.False(settingsVm.SaveSettingsCommand.CanExecute(null));

        // Validation error still keeps dirty state until valid & saved
        settingsVm.ToggleTimerHotkey = "InvalidShortcut";
        Assert.True(settingsVm.SaveSettingsCommand.CanExecute(null));
        await settingsVm.SaveSettingsAsync();
        Assert.True(settingsVm.HasValidationError);
        Assert.True(settingsVm.HasUnsavedChanges);

        settingsVm.ToggleTimerHotkey = "ctrl+alt+t";
        settingsVm.HourlyDingVolume = 75;
        await settingsVm.SaveSettingsAsync();

        Assert.False(settingsVm.HasValidationError);
        Assert.False(settingsVm.HasUnsavedChanges);
        Assert.False(settingsVm.SaveSettingsCommand.CanExecute(null));
        Assert.Equal("Ctrl+Alt+T", await db.Settings.GetAsync(SettingKeys.ToggleTimerHotkey));
        Assert.Equal(75, await db.Settings.GetIntAsync(SettingKeys.HourlyDingVolume));
        hotkeyMock.Verify(h => h.ReloadBindingsAsync(), Times.Once);
    }

    [Fact]
    public async Task HistoryActiveSession_TicksLive_AndBlocksEditOrDeleteUntilStopped()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: true);
        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 24, 16, 0, 0, TimeSpan.Zero));
        using var timerService = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings);
        var exportService = new ExportService(db.TimeEntries);
        var historyVm = new HistoryViewModel(db.TimeEntries, db.WorkItems, exportService, clock, timerService);

        var workItem = (await db.WorkItems.GetAllAsync()).First();
        await timerService.StartAsync(workItem.Id, "Active task");
        await historyVm.ApplyPresetAsync("All");

        Assert.Single(historyVm.Entries);
        var activeEntry = historyVm.Entries[0];
        Assert.True(activeEntry.IsActiveSession);
        Assert.True(activeEntry.IsActivelyRunning);
        Assert.False(activeEntry.IsPaused);
        Assert.False(activeEntry.CanEditOrDelete);
        Assert.Equal("LIVE", activeEntry.ActiveStatusBadgeText);

        // Live tick updates DurationMs and GrandTotalFormatted in-place
        clock.Advance(TimeSpan.FromSeconds(42));
        timerService.Tick();
        Assert.Equal("00:00:42", historyVm.Entries[0].DurationFormatted);
        Assert.Equal("00:00:42", historyVm.GrandTotalFormatted);

        // Edit and delete are blocked while running
        historyVm.BeginEditEntryNotes(activeEntry);
        Assert.Null(historyVm.EditingEntry);
        await historyVm.DeleteEntryAsync(activeEntry);
        Assert.Single(historyVm.Entries);

        // Pause: still considered active, edit/delete still blocked
        await timerService.PauseAsync();
        await historyVm.LoadHistoryAsync();
        var pausedEntry = historyVm.Entries[0];
        Assert.True(pausedEntry.IsActiveSession);
        Assert.False(pausedEntry.IsActivelyRunning);
        Assert.True(pausedEntry.IsPaused);
        Assert.False(pausedEntry.CanEditOrDelete);
        Assert.Equal("PAUSED", pausedEntry.ActiveStatusBadgeText);

        historyVm.BeginEditEntryNotes(pausedEntry);
        Assert.Null(historyVm.EditingEntry);
        await historyVm.DeleteEntryAsync(pausedEntry);
        Assert.Single(historyVm.Entries);

        // Stop: now editable and deletable
        await timerService.StopAsync();
        await historyVm.LoadHistoryAsync();
        var stoppedEntry = historyVm.Entries[0];
        Assert.False(stoppedEntry.IsActiveSession);
        Assert.True(stoppedEntry.CanEditOrDelete);

        historyVm.BeginEditEntryNotes(stoppedEntry);
        Assert.NotNull(historyVm.EditingEntry);
    }

    [Fact]
    public async Task HistoryEditStoppedSession_UpdatesTimeAndNotes_WithValidation()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: true);
        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 24, 16, 0, 0, TimeSpan.Zero));
        using var timerService = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings);
        var exportService = new ExportService(db.TimeEntries);
        var historyVm = new HistoryViewModel(db.TimeEntries, db.WorkItems, exportService, clock, timerService);

        var workItem = (await db.WorkItems.GetAllAsync()).First();
        await timerService.StartAsync(workItem.Id);
        clock.Advance(TimeSpan.FromMinutes(30));
        await timerService.StopAsync();

        await historyVm.ApplyPresetAsync("All");
        var entry = historyVm.Entries[0];

        historyVm.BeginEditEntryNotes(entry);
        Assert.Equal("00:30:00", historyVm.EditingDurationText);

        // Updating Duration recalculates End time; updating Start/End recalculates Duration
        historyVm.EditingDurationText = "01:15:00";
        await historyVm.SaveEntryNotesAsync();

        Assert.Null(historyVm.EditingValidationError);
        Assert.Equal("01:15:00", historyVm.Entries[0].DurationFormatted);
        Assert.Equal("01:15:00", historyVm.GrandTotalFormatted);

        // Invalid time order triggers validation error
        historyVm.BeginEditEntryNotes(historyVm.Entries[0]);
        historyVm.EditingStartTimeText = "12:00:00";
        historyVm.EditingEndTimeText = "11:00:00";
        await historyVm.SaveEntryNotesAsync();
        Assert.NotNull(historyVm.EditingValidationError);
        Assert.NotNull(historyVm.EditingEntry);
    }

    [Fact]
    public async Task SettingsViewModel_DefaultsToSystemTheme_AndPersistsThemeSelection()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: true);
        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 24, 16, 0, 0, TimeSpan.Zero));
        using var timerService = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings);
        var audioMock = new Mock<IAudioChimePlayer>();
        var notificationService = new NotificationService(timerService, db.Settings, audioMock.Object, clock);
        var hotkeyMock = new Mock<IHotkeyService>();
        hotkeyMock.SetupGet(h => h.RegistrationWarnings).Returns([]);

        var settingsVm = new SettingsViewModel(db.Settings, db.WorkItems, notificationService, hotkeyMock.Object);
        await settingsVm.LoadAsync();

        Assert.Equal("System", settingsVm.AppTheme);
        Assert.False(settingsVm.HasUnsavedChanges);

        settingsVm.AppTheme = "Light";
        Assert.True(settingsVm.HasUnsavedChanges);
        await settingsVm.SaveSettingsAsync();

        Assert.False(settingsVm.HasUnsavedChanges);
        Assert.Equal("Light", await db.Settings.GetAsync(SettingKeys.AppTheme));
    }
}
