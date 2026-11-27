using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly ITimerService? _timerService;
    private readonly TimeProvider _timeProvider;
    private bool _suppressAutoLoad;
    private bool _isSyncingEditFields;

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
    private DateTime? _editingStartDate;

    [ObservableProperty]
    private string _editingStartTimeText = string.Empty;

    [ObservableProperty]
    private DateTime? _editingEndDate;

    [ObservableProperty]
    private string _editingEndTimeText = string.Empty;

    [ObservableProperty]
    private string _editingDurationText = string.Empty;

    [ObservableProperty]
    private string _editingEntryNotes = string.Empty;

    [ObservableProperty]
    private string? _editingValidationError;

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsEntriesSection => SelectedSection == "Entries";
    public bool IsWorkItemsSection => SelectedSection == "WorkItems";
    public bool IsDaysSection => SelectedSection == "Days";

    public HistoryViewModel(
        ITimeEntryRepository timeEntryRepo,
        IWorkItemRepository workItemRepo,
        IExportService exportService,
        TimeProvider? timeProvider = null,
        ITimerService? timerService = null)
    {
        _timeEntryRepo = timeEntryRepo;
        _workItemRepo = workItemRepo;
        _exportService = exportService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timerService = timerService;

        if (_timerService != null)
        {
            _timerService.TimerTicked += OnTimerTicked;
            _timerService.TimerStateChanged += OnTimerStateChanged;
        }

        var localToday = _timeProvider.GetLocalNow().Date;
        _suppressAutoLoad = true;
        _startDate = localToday.AddDays(-(int)localToday.DayOfWeek);
        _endDate = localToday;
        _suppressAutoLoad = false;
    }

    private void OnTimerTicked()
    {
        if (_timerService?.RunningEntry is not { } runningEntry || !_timerService.IsRunning)
        {
            return;
        }

        var liveMs = _timerService.ElapsedMilliseconds;
        UiDispatcher.Invoke(() =>
        {
            var match = Entries.FirstOrDefault(e => e.Id == runningEntry.Id);
            if (match == null)
            {
                return;
            }

            match.DurationMs = liveMs;
            match.IsPaused = false;
            GrandTotalFormatted = DurationFormatter.FormatMilliseconds(Entries.Sum(e => e.DurationMs));
        });
    }

    private void OnTimerStateChanged()
    {
        _ = LoadHistoryAsync();
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

    partial void OnEditingStartDateChanged(DateTime? value) => RecalculateDurationFromStartEnd();
    partial void OnEditingStartTimeTextChanged(string value) => RecalculateDurationFromStartEnd();
    partial void OnEditingEndDateChanged(DateTime? value) => RecalculateDurationFromStartEnd();
    partial void OnEditingEndTimeTextChanged(string value) => RecalculateDurationFromStartEnd();
    partial void OnEditingDurationTextChanged(string value) => RecalculateEndFromDuration();

    private void RecalculateDurationFromStartEnd()
    {
        if (_isSyncingEditFields || EditingEntry == null)
        {
            return;
        }

        if (TryCombineLocalDateTime(EditingStartDate, EditingStartTimeText, out var startLocal) &&
            TryCombineLocalDateTime(EditingEndDate, EditingEndTimeText, out var endLocal) &&
            endLocal > startLocal)
        {
            _isSyncingEditFields = true;
            try
            {
                EditingDurationText = DurationFormatter.Format(endLocal - startLocal);
                EditingValidationError = null;
            }
            finally
            {
                _isSyncingEditFields = false;
            }
        }
    }

    private void RecalculateEndFromDuration()
    {
        if (_isSyncingEditFields || EditingEntry == null)
        {
            return;
        }

        if (TryCombineLocalDateTime(EditingStartDate, EditingStartTimeText, out var startLocal) &&
            TryParseDuration(EditingDurationText, out var duration) &&
            duration > TimeSpan.Zero)
        {
            var newEndLocal = startLocal + duration;
            _isSyncingEditFields = true;
            try
            {
                EditingEndDate = newEndLocal.Date;
                EditingEndTimeText = newEndLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                EditingValidationError = null;
            }
            finally
            {
                _isSyncingEditFields = false;
            }
        }
    }

    [RelayCommand]
    public void BeginEditEntryNotes(EnrichedTimeEntry? entry)
    {
        if (entry == null || entry.IsActiveSession)
        {
            return;
        }

        _isSyncingEditFields = true;
        try
        {
            EditingEntry = entry;
            var localStart = entry.LocalStartTime;
            var localEnd = entry.LocalEndTime ?? localStart.AddMilliseconds(entry.DurationMs);

            EditingStartDate = localStart.Date;
            EditingStartTimeText = localStart.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            EditingEndDate = localEnd.Date;
            EditingEndTimeText = localEnd.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            EditingDurationText = DurationFormatter.FormatMilliseconds(entry.DurationMs);
            EditingEntryNotes = entry.Notes ?? string.Empty;
            EditingValidationError = null;
        }
        finally
        {
            _isSyncingEditFields = false;
        }
    }

    [RelayCommand]
    public void CancelEditEntryNotes()
    {
        EditingEntry = null;
        EditingEntryNotes = string.Empty;
        EditingValidationError = null;
    }

    [RelayCommand]
    public async Task SaveEntryNotesAsync()
    {
        if (EditingEntry == null)
        {
            return;
        }

        if (!TryCombineLocalDateTime(EditingStartDate, EditingStartTimeText, out var startLocal))
        {
            EditingValidationError = "Enter a valid start date and time (e.g. 09:00 or 9:00 AM).";
            return;
        }

        if (!TryCombineLocalDateTime(EditingEndDate, EditingEndTimeText, out var endLocal))
        {
            EditingValidationError = "Enter a valid end date and time (e.g. 17:30 or 5:30 PM).";
            return;
        }

        if (!TryParseDuration(EditingDurationText, out var duration) || duration <= TimeSpan.Zero)
        {
            EditingValidationError = "Enter a valid duration greater than 00:00:00 (e.g. 01:30:00).";
            return;
        }

        if (endLocal <= startLocal)
        {
            EditingValidationError = "End time must be after start time.";
            return;
        }

        var rawEntry = await _timeEntryRepo.GetByIdAsync(EditingEntry.Id);
        if (rawEntry == null || rawEntry.IsRunning)
        {
            EditingValidationError = "Active sessions cannot be edited while running or paused.";
            return;
        }

        var startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime();
        var endUtc = DateTime.SpecifyKind(endLocal, DateTimeKind.Local).ToUniversalTime();

        rawEntry.StartTime = startUtc;
        rawEntry.EndTime = endUtc;
        rawEntry.DurationMs = (long)Math.Round(duration.TotalMilliseconds);
        rawEntry.Notes = string.IsNullOrWhiteSpace(EditingEntryNotes) ? null : EditingEntryNotes.Trim();

        await _timeEntryRepo.UpdateAsync(rawEntry);
        StatusMessage = "Session updated.";

        EditingEntry = null;
        EditingEntryNotes = string.Empty;
        EditingValidationError = null;
        await LoadHistoryAsync();
    }

    [RelayCommand]
    public async Task DeleteEntryAsync(EnrichedTimeEntry? entry)
    {
        if (entry == null || entry.IsActiveSession)
        {
            return;
        }

        var rawEntry = await _timeEntryRepo.GetByIdAsync(entry.Id);
        if (rawEntry == null || rawEntry.IsRunning)
        {
            return;
        }

        await _timeEntryRepo.DeleteAsync(entry.Id);
        StatusMessage = "Deleted time entry.";
        await LoadHistoryAsync();
    }

    private static bool TryCombineLocalDateTime(DateTime? date, string? timeText, out DateTime result)
    {
        result = default;
        if (!date.HasValue || string.IsNullOrWhiteSpace(timeText))
        {
            return false;
        }

        var trimmed = timeText.Trim();
        string[] formats =
        [
            "HH:mm:ss", "H:mm:ss",
            "HH:mm", "H:mm",
            "hh:mm:ss tt", "h:mm:ss tt",
            "hh:mm tt", "h:mm tt"
        ];

        if (DateTime.TryParseExact(trimmed, formats, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ||
            DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
        {
            result = date.Value.Date.Add(parsed.TimeOfDay);
            return true;
        }

        return false;
    }

    private static bool TryParseDuration(string? text, out TimeSpan duration)
    {
        duration = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split(':');
        if (parts.Length == 3 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) &&
            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) &&
            h >= 0 && m is >= 0 and < 60 && s is >= 0 and < 60)
        {
            duration = new TimeSpan(h, m, s);
            return true;
        }

        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h2) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m2) &&
            h2 >= 0 && m2 is >= 0 and < 60)
        {
            duration = new TimeSpan(h2, m2, 0);
            return true;
        }

        return false;
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
