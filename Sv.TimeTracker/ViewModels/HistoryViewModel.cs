using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sv.TimeTracker.Data.Repositories;
using Sv.TimeTracker.Models;
using Sv.TimeTracker.Services;

namespace Sv.TimeTracker.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly ITimeEntryRepository _timeEntryRepo;
    private readonly IWorkItemRepository _workItemRepo;
    private readonly IExportService _exportService;
    private readonly TimeProvider _timeProvider;
    private bool _suppressAutoLoad;

    public ObservableCollection<WorkItemFilterOption> WorkItemOptions { get; } = [];
    public ObservableCollection<EnrichedTimeEntry> Entries { get; } = [];
    public ObservableCollection<DailyTotalSummary> DailyTotals { get; } = [];
    public ObservableCollection<WorkItemTotalSummary> WorkItemTotals { get; } = [];

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private WorkItemFilterOption? _selectedWorkItemOption;

    [ObservableProperty]
    private string _activePreset = "Week";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEntriesSection))]
    [NotifyPropertyChangedFor(nameof(IsWorkItemsSection))]
    [NotifyPropertyChangedFor(nameof(IsDaysSection))]
    private string _selectedSection = "Entries";

    [ObservableProperty]
    private string _grandTotalFormatted = "00:00:00";

    [ObservableProperty]
    private int _totalSessionCount;

    [ObservableProperty]
    private string _topWorkItemSummary = "No sessions";

    [ObservableProperty]
    private EnrichedTimeEntry? _editingEntry;

    [ObservableProperty]
    private string _editingEntryNotes = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsEntriesSection => SelectedSection == "Entries";
    public bool IsWorkItemsSection => SelectedSection == "WorkItems";
    public bool IsDaysSection => SelectedSection == "Days";

    public HistoryViewModel(
        ITimeEntryRepository timeEntryRepo,
        IWorkItemRepository workItemRepo,
        IExportService exportService,
        TimeProvider? timeProvider = null)
    {
        _timeEntryRepo = timeEntryRepo;
        _workItemRepo = workItemRepo;
        _exportService = exportService;
        _timeProvider = timeProvider ?? TimeProvider.System;

        var localToday = _timeProvider.GetLocalNow().Date;
        _suppressAutoLoad = true;
        _startDate = localToday.AddDays(-(int)localToday.DayOfWeek);
        _endDate = localToday;
        _suppressAutoLoad = false;
    }

    public async Task InitializeAsync()
    {
        await ReloadWorkItemOptionsAsync();
        await LoadHistoryAsync();
    }

    public async Task ReloadWorkItemOptionsAsync()
    {
        var items = (await _workItemRepo.GetAllAsync()).ToList();
        var previousId = SelectedWorkItemOption?.Id ?? 0;

        UiDispatcher.Invoke(() =>
        {
            _suppressAutoLoad = true;
            try
            {
                WorkItemOptions.Clear();
                var allOption = new WorkItemFilterOption(0, "All Work Items");
                WorkItemOptions.Add(allOption);

                foreach (var item in items)
                {
                    WorkItemOptions.Add(new WorkItemFilterOption(item.Id, item.Title, item.Color));
                }

                SelectedWorkItemOption = WorkItemOptions.FirstOrDefault(o => o.Id == previousId) ?? allOption;
            }
            finally
            {
                _suppressAutoLoad = false;
            }
        });
    }

    partial void OnStartDateChanged(DateTime? value)
    {
        if (!_suppressAutoLoad)
        {
            ActivePreset = "Custom";
            _ = LoadHistoryAsync();
        }
    }

    partial void OnEndDateChanged(DateTime? value)
    {
        if (!_suppressAutoLoad)
        {
            ActivePreset = "Custom";
            _ = LoadHistoryAsync();
        }
    }

    partial void OnSelectedWorkItemOptionChanged(WorkItemFilterOption? value)
    {
        if (!_suppressAutoLoad)
        {
            _ = LoadHistoryAsync();
        }
    }

    [RelayCommand]
    public async Task ApplyPresetAsync(string? preset)
    {
        var localToday = _timeProvider.GetLocalNow().Date;
        _suppressAutoLoad = true;
        try
        {
            switch (preset)
            {
                case "Today":
                    StartDate = localToday;
                    EndDate = localToday;
                    ActivePreset = "Today";
                    break;
                case "Month":
                    StartDate = new DateTime(localToday.Year, localToday.Month, 1);
                    EndDate = localToday;
                    ActivePreset = "Month";
                    break;
                case "All":
                    StartDate = null;
                    EndDate = null;
                    ActivePreset = "All";
                    break;
                default:
                    StartDate = localToday.AddDays(-(int)localToday.DayOfWeek);
                    EndDate = localToday;
                    ActivePreset = "Week";
                    break;
            }
        }
        finally
        {
            _suppressAutoLoad = false;
        }

        await LoadHistoryAsync();
    }

    [RelayCommand]
    public void SelectSection(string? section)
    {
        if (section is "Entries" or "WorkItems" or "Days")
        {
            SelectedSection = section;
        }
    }

    [RelayCommand]
    public async Task LoadHistoryAsync()
    {
        var filterId = SelectedWorkItemOption is { Id: > 0 } opt ? opt.Id : (long?)null;

        var enriched = await _timeEntryRepo.GetEnrichedEntriesAsync(StartDate, EndDate, filterId);
        var daily = await _timeEntryRepo.GetTotalsByDayAsync(StartDate, EndDate, filterId);
        var byWorkItem = await _timeEntryRepo.GetTotalsByWorkItemAsync(StartDate, EndDate);

        if (filterId.HasValue)
        {
            byWorkItem = byWorkItem.Where(w => w.WorkItemId == filterId.Value).ToList();
        }

        var grandTotalMs = enriched.Sum(e => e.DurationMs);
        var topItem = byWorkItem.FirstOrDefault(w => w.TotalMs > 0);

        UiDispatcher.Invoke(() =>
        {
            Entries.Clear();
            foreach (var entry in enriched)
            {
                Entries.Add(entry);
            }

            DailyTotals.Clear();
            foreach (var day in daily)
            {
                DailyTotals.Add(day);
            }

            WorkItemTotals.Clear();
            foreach (var item in byWorkItem)
            {
                WorkItemTotals.Add(item);
            }

            GrandTotalFormatted = DurationFormatter.FormatMilliseconds(grandTotalMs);
            TotalSessionCount = enriched.Count;
            TopWorkItemSummary = topItem != null
                ? $"{topItem.Title} ({topItem.ShareDisplay})"
                : "No tracked time";
        });
    }

    [RelayCommand]
    public void BeginEditEntryNotes(EnrichedTimeEntry? entry)
    {
        if (entry == null)
        {
            return;
        }

        EditingEntry = entry;
        EditingEntryNotes = entry.Notes ?? string.Empty;
    }

    [RelayCommand]
    public void CancelEditEntryNotes()
    {
        EditingEntry = null;
        EditingEntryNotes = string.Empty;
    }

    [RelayCommand]
    public async Task SaveEntryNotesAsync()
    {
        if (EditingEntry == null)
        {
            return;
        }

        var rawEntry = await _timeEntryRepo.GetByIdAsync(EditingEntry.Id);
        if (rawEntry != null)
        {
            rawEntry.Notes = string.IsNullOrWhiteSpace(EditingEntryNotes) ? null : EditingEntryNotes.Trim();
            await _timeEntryRepo.UpdateAsync(rawEntry);
            StatusMessage = "Session notes updated.";
        }

        EditingEntry = null;
        EditingEntryNotes = string.Empty;
        await LoadHistoryAsync();
    }

    [RelayCommand]
    public async Task DeleteEntryAsync(EnrichedTimeEntry? entry)
    {
        if (entry == null)
        {
            return;
        }

        await _timeEntryRepo.DeleteAsync(entry.Id);
        StatusMessage = "Deleted time entry.";
        await LoadHistoryAsync();
    }

    [RelayCommand]
    public async Task ExportCsvAsync()
    {
        var filterId = SelectedWorkItemOption is { Id: > 0 } opt ? opt.Id : (long?)null;
        var result = await _exportService.ExportTimeEntriesAsync(StartDate, EndDate, filterId);
        if (result.Cancelled)
        {
            return;
        }

        StatusMessage = result.Success
            ? $"Exported {result.ExportedCount} entries to {System.IO.Path.GetFileName(result.FilePath)}."
            : result.ErrorMessage ?? "Export failed.";
    }
}
