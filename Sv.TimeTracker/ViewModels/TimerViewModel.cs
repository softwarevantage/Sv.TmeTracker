using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sv.TimeTracker.Data.Repositories;
using Sv.TimeTracker.Models;
using Sv.TimeTracker.Services;

namespace Sv.TimeTracker.ViewModels;

public partial class TimerViewModel : ObservableObject
{
    private readonly ITimerService _timerService;
    private readonly IWorkItemRepository _workItemRepo;
    private readonly ISettingsRepository _settingsRepo;
    private bool _suppressSelectionPersistence;

    public ObservableCollection<WorkItem> WorkItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleTimer))]
    private WorkItem? _selectedWorkItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    [NotifyPropertyChangedFor(nameof(CompactPauseIcon))]
    [NotifyPropertyChangedFor(nameof(HasActiveSession))]
    [NotifyPropertyChangedFor(nameof(CanSwitchToSelected))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    [NotifyPropertyChangedFor(nameof(CompactPauseIcon))]
    [NotifyPropertyChangedFor(nameof(HasActiveSession))]
    [NotifyPropertyChangedFor(nameof(CanSwitchToSelected))]
    private bool _isPaused;

    [ObservableProperty]
    private string _elapsedText = "00:00:00";

    [ObservableProperty]
    private string _activeWorkItemTitle = "Ready to track";

    [ObservableProperty]
    private string _activeWorkItemColor = "#38BDF8";

    [ObservableProperty]
    private string _sessionNotes = string.Empty;

    [ObservableProperty]
    private string _quickWorkItemTitle = string.Empty;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private bool _isCompactMode;

    public bool HasActiveSession => IsRunning || IsPaused;

    public string ToggleButtonText => HasActiveSession ? "Stop" : "Start Timer";

    public string PauseButtonText => IsPaused ? "▶ Resume" : "❚❚ Pause";

    public string CompactPauseIcon => IsPaused ? "▶" : "❚❚";

    public bool CanToggleTimer => HasActiveSession || SelectedWorkItem != null;

    public bool CanSwitchToSelected =>
        HasActiveSession &&
        SelectedWorkItem != null &&
        _timerService.RunningEntry != null &&
        SelectedWorkItem.Id != _timerService.RunningEntry.WorkItemId;

    public event Func<Task>? QuickWorkItemCreated;

    public TimerViewModel(
        ITimerService timerService,
        IWorkItemRepository workItemRepo,
        ISettingsRepository settingsRepo)
    {
        _timerService = timerService;
        _workItemRepo = workItemRepo;
        _settingsRepo = settingsRepo;

        _timerService.TimerTicked += OnTimerTicked;
        _timerService.TimerStateChanged += OnTimerStateChanged;
    }

    public async Task InitializeAsync()
    {
        await RefreshWorkItemsAsync();
        await SyncFromTimerServiceAsync();
    }

    public async Task RefreshWorkItemsAsync(long? preferredWorkItemId = null)
    {
        var allItems = (await _workItemRepo.GetAllAsync()).ToList();

        var targetId = preferredWorkItemId
                       ?? _timerService.RunningEntry?.WorkItemId
                       ?? SelectedWorkItem?.Id;

        if (!targetId.HasValue || targetId.Value <= 0)
        {
            var lastId = await _settingsRepo.GetLongAsync(SettingKeys.LastSelectedWorkItemId, 0);
            var defaultId = await _settingsRepo.GetLongAsync(SettingKeys.DefaultWorkItemId, 0);
            targetId = lastId > 0 ? lastId : defaultId;
        }

        UiDispatcher.Invoke(() =>
        {
            _suppressSelectionPersistence = true;
            try
            {
                WorkItems.Clear();
                foreach (var item in allItems)
                {
                    WorkItems.Add(item);
                }

                SelectedWorkItem = WorkItems.FirstOrDefault(w => w.Id == targetId)
                                   ?? WorkItems.FirstOrDefault();
            }
            finally
            {
                _suppressSelectionPersistence = false;
            }
        });
    }

    public async Task SelectWorkItemAsync(long workItemId, bool startImmediately = false)
    {
        var match = WorkItems.FirstOrDefault(w => w.Id == workItemId);
        if (match == null)
        {
            await RefreshWorkItemsAsync(workItemId);
            match = WorkItems.FirstOrDefault(w => w.Id == workItemId);
        }

        if (match != null)
        {
            SelectedWorkItem = match;
            if (startImmediately)
            {
                await _timerService.StartAsync(match.Id, SessionNotes);
            }
        }
    }

    partial void OnSelectedWorkItemChanged(WorkItem? value)
    {
        ValidationMessage = null;
        OnPropertyChanged(nameof(CanSwitchToSelected));

        if (!IsRunning && value != null)
        {
            ActiveWorkItemColor = value.Color;
        }

        if (!_suppressSelectionPersistence && value != null)
        {
            _ = _settingsRepo.SetAsync(
                SettingKeys.LastSelectedWorkItemId,
                value.Id.ToString(CultureInfo.InvariantCulture));
        }
    }

    [RelayCommand]
    public async Task ToggleTimerAsync()
    {
        ValidationMessage = null;

        if (_timerService.HasActiveSession)
        {
            await _timerService.UpdateRunningNotesAsync(SessionNotes);
            await _timerService.StopAsync();
            SessionNotes = string.Empty;
            return;
        }

        if (SelectedWorkItem == null)
        {
            ValidationMessage = "Select or create a work item first.";
            return;
        }

        await _timerService.StartAsync(SelectedWorkItem.Id, SessionNotes);
    }

    [RelayCommand]
    public async Task TogglePauseAsync()
    {
        if (!_timerService.HasActiveSession)
        {
            return;
        }

        await _timerService.UpdateRunningNotesAsync(SessionNotes);
        await _timerService.TogglePauseAsync();
    }

    [RelayCommand]
    public async Task SwitchActiveWorkItemAsync()
    {
        if (SelectedWorkItem == null)
        {
            return;
        }

        await _timerService.UpdateRunningNotesAsync(SessionNotes);
        SessionNotes = string.Empty;
        await _timerService.StartAsync(SelectedWorkItem.Id);
    }

    [RelayCommand]
    public async Task SaveRunningNotesAsync()
    {
        if (_timerService.HasActiveSession)
        {
            await _timerService.UpdateRunningNotesAsync(SessionNotes);
        }
    }

    [RelayCommand]
    public async Task QuickAddWorkItemAsync()
    {
        var title = QuickWorkItemTitle.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        string[] palette = ["#0078D4", "#38BDF8", "#10B981", "#F59E0B", "#8B5CF6", "#EC4899"];
        var color = palette[WorkItems.Count % palette.Length];

        var created = await _workItemRepo.CreateAsync(new WorkItem
        {
            Title = title,
            Color = color
        });

        QuickWorkItemTitle = string.Empty;
        ValidationMessage = null;

        await RefreshWorkItemsAsync(created.Id);
        if (QuickWorkItemCreated != null)
        {
            await QuickWorkItemCreated.Invoke();
        }
    }

    private void OnTimerTicked()
    {
        var formatted = DurationFormatter.Format(_timerService.Elapsed);
        UiDispatcher.Invoke(() => ElapsedText = formatted);
    }

    private void OnTimerStateChanged()
    {
        _ = SyncFromTimerServiceAsync();
    }

    private async Task SyncFromTimerServiceAsync()
    {
        var running = _timerService.IsRunning;
        var paused = _timerService.IsPaused;
        var entry = _timerService.RunningEntry;
        var title = await _timerService.GetRunningWorkItemTitleAsync();
        var runningItem = _timerService.RunningWorkItem
                          ?? (entry != null ? await _workItemRepo.GetByIdAsync(entry.WorkItemId) : null);
        var elapsed = DurationFormatter.Format(_timerService.Elapsed);

        UiDispatcher.Invoke(() =>
        {
            IsRunning = running;
            IsPaused = paused;
            ElapsedText = elapsed;

            if ((running || paused) && entry != null)
            {
                ActiveWorkItemTitle = paused
                    ? $"Paused · {title ?? "Session"}"
                    : title ?? "Timer running";
                ActiveWorkItemColor = paused
                    ? "#F59E0B"
                    : runningItem?.Color ?? "#10B981";
                SessionNotes = entry.Notes ?? SessionNotes;

                var matching = WorkItems.FirstOrDefault(w => w.Id == entry.WorkItemId);
                if (matching != null && SelectedWorkItem?.Id != matching.Id)
                {
                    _suppressSelectionPersistence = true;
                    SelectedWorkItem = matching;
                    _suppressSelectionPersistence = false;
                }
            }
            else
            {
                ActiveWorkItemTitle = SelectedWorkItem != null
                    ? $"Ready · {SelectedWorkItem.Title}"
                    : "Select a work item to begin";
                ActiveWorkItemColor = SelectedWorkItem?.Color ?? "#38BDF8";
            }

            OnPropertyChanged(nameof(CanSwitchToSelected));
        });
    }
}
