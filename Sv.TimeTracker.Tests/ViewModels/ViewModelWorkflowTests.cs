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
        var historyVm = new HistoryViewModel(db.TimeEntries, db.WorkItems, exportService, clock);
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

        // 5. Validate and save settings
        await mainVm.NavigateAsync("Settings");
        settingsVm.ToggleTimerHotkey = "InvalidShortcut";
        await settingsVm.SaveSettingsAsync();
        Assert.True(settingsVm.HasValidationError);

        settingsVm.ToggleTimerHotkey = "ctrl+alt+t";
        settingsVm.HourlyDingVolume = 75;
        await settingsVm.SaveSettingsAsync();

        Assert.False(settingsVm.HasValidationError);
        Assert.Equal("Ctrl+Alt+T", await db.Settings.GetAsync(SettingKeys.ToggleTimerHotkey));
        Assert.Equal(75, await db.Settings.GetIntAsync(SettingKeys.HourlyDingVolume));
        hotkeyMock.Verify(h => h.ReloadBindingsAsync(), Times.Once);
    }
}
