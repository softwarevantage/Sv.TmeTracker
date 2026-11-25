using System.Globalization;
using Sv.TimeTracker.Data;
using Sv.TimeTracker.Data.Repositories;
using Sv.TimeTracker.Models;

namespace Sv.TimeTracker.Services;

public interface ITimerService : IDisposable
{
    bool IsRunning { get; }
    bool IsPaused { get; }
    bool HasActiveSession { get; }
    TimeEntry? RunningEntry { get; }
    WorkItem? RunningWorkItem { get; }
    long ElapsedMilliseconds { get; }
    TimeSpan Elapsed { get; }

    event Action? ClockTicked;
    event Action? TimerTicked;
    event Action? TimerStateChanged;

    Task InitializeAsync();
    Task StartAsync(long workItemId, string? notes = null);
    Task PauseAsync();
    Task ResumeAsync();
    Task TogglePauseAsync();
    Task StopAsync();
    Task ToggleAsync(long workItemId, string? notes = null);
    Task UpdateRunningNotesAsync(string? notes);
    Task<string?> GetRunningWorkItemTitleAsync();
    void Tick();
}

public sealed class TimerService : ITimerService
{
    private readonly ITimeEntryRepository _timeEntryRepo;
    private readonly IWorkItemRepository _workItemRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly System.Timers.Timer? _periodicTimer;

    private TimeEntry? _runningEntry;
    private WorkItem? _runningWorkItem;
    private DateTime _segmentStartUtc;
    private bool _isRunning;
    private bool _isPaused;
    private bool _disposed;

    public event Action? ClockTicked;
    public event Action? TimerTicked;
    public event Action? TimerStateChanged;

    public TimerService(
        ITimeEntryRepository? timeEntryRepo = null,
        IWorkItemRepository? workItemRepo = null,
        TimeProvider? timeProvider = null,
        bool enableAutoTick = true,
        ISettingsRepository? settingsRepo = null)
    {
        _timeEntryRepo = timeEntryRepo ?? new TimeEntryRepository();
        _workItemRepo = workItemRepo ?? new WorkItemRepository();
        _settingsRepo = settingsRepo ?? new SettingsRepository();
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (enableAutoTick)
        {
            _periodicTimer = new System.Timers.Timer(1000)
            {
                AutoReset = true,
                Enabled = true
            };
            _periodicTimer.Elapsed += (_, _) => Tick();
        }
    }

    public bool IsRunning => _isRunning;

    public bool IsPaused => _isPaused;

    public bool HasActiveSession => _runningEntry != null && (_isRunning || _isPaused);

    public TimeEntry? RunningEntry => _runningEntry;

    public WorkItem? RunningWorkItem => _runningWorkItem;

    public long ElapsedMilliseconds
    {
        get
        {
            var entry = _runningEntry;
            if (entry == null)
            {
                return 0;
            }

            if (_isRunning)
            {
                var liveMs = (long)Math.Max(0, (_timeProvider.GetUtcNow().UtcDateTime - _segmentStartUtc).TotalMilliseconds);
                return Math.Max(0, entry.DurationMs + liveMs);
            }

            return Math.Max(0, entry.DurationMs);
        }
    }

    public TimeSpan Elapsed => TimeSpan.FromMilliseconds(ElapsedMilliseconds);

    public async Task InitializeAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            _runningEntry = await _timeEntryRepo.GetRunningEntryAsync();
            if (_runningEntry != null)
            {
                _runningWorkItem = await _workItemRepo.GetByIdAsync(_runningEntry.WorkItemId);
                var pausedId = await _settingsRepo.GetLongAsync(SettingKeys.PausedEntryId, 0);
                if (pausedId == _runningEntry.Id)
                {
                    _isPaused = true;
                    _isRunning = false;
                }
                else
                {
                    var savedSegmentStart = await _settingsRepo.GetAsync(SettingKeys.ActiveSegmentStartUtc);
                    _segmentStartUtc = !string.IsNullOrWhiteSpace(savedSegmentStart)
                        ? DatabaseHelper.ParseUtc(savedSegmentStart)
                        : (_runningEntry.StartTime.Kind == DateTimeKind.Utc
                            ? _runningEntry.StartTime
                            : _runningEntry.StartTime.ToUniversalTime());
                    _isRunning = true;
                    _isPaused = false;
                }
            }
            else
            {
                _runningWorkItem = null;
                _isRunning = false;
                _isPaused = false;
            }
        }
        finally
        {
            _stateLock.Release();
        }

        TimerStateChanged?.Invoke();
    }

    public async Task StartAsync(long workItemId, string? notes = null)
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_runningEntry != null && (_isRunning || _isPaused))
            {
                await StopCoreAsync();
            }

            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            var entry = new TimeEntry
            {
                WorkItemId = workItemId,
                StartTime = utcNow,
                DurationMs = 0,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                CreatedAt = utcNow
            };

            _runningEntry = await _timeEntryRepo.CreateAsync(entry);
            _runningWorkItem = await _workItemRepo.GetByIdAsync(workItemId);
            _segmentStartUtc = utcNow;
            _isRunning = true;
            _isPaused = false;

            await _settingsRepo.SetAsync(SettingKeys.PausedEntryId, "0");
            await _settingsRepo.SetAsync(SettingKeys.ActiveSegmentStartUtc, DatabaseHelper.FormatUtc(utcNow));
        }
        finally
        {
            _stateLock.Release();
        }

        TimerStateChanged?.Invoke();
        TimerTicked?.Invoke();
    }

    public async Task PauseAsync()
    {
        var changed = false;
        await _stateLock.WaitAsync();
        try
        {
            if (_runningEntry != null && _isRunning)
            {
                var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
                var liveMs = (long)Math.Max(0, (utcNow - _segmentStartUtc).TotalMilliseconds);
                _runningEntry.DurationMs = Math.Max(0, _runningEntry.DurationMs + liveMs);

                await _timeEntryRepo.UpdateAsync(_runningEntry);
                await _settingsRepo.SetAsync(
                    SettingKeys.PausedEntryId,
                    _runningEntry.Id.ToString(CultureInfo.InvariantCulture));
                await _settingsRepo.SetAsync(SettingKeys.ActiveSegmentStartUtc, string.Empty);

                _isRunning = false;
                _isPaused = true;
                changed = true;
            }
        }
        finally
        {
            _stateLock.Release();
        }

        if (changed)
        {
            TimerStateChanged?.Invoke();
            TimerTicked?.Invoke();
        }
    }

    public async Task ResumeAsync()
    {
        var changed = false;
        await _stateLock.WaitAsync();
        try
        {
            if (_runningEntry != null && _isPaused)
            {
                var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
                _segmentStartUtc = utcNow;
                _isPaused = false;
                _isRunning = true;

                await _settingsRepo.SetAsync(SettingKeys.PausedEntryId, "0");
                await _settingsRepo.SetAsync(SettingKeys.ActiveSegmentStartUtc, DatabaseHelper.FormatUtc(utcNow));
                changed = true;
            }
        }
        finally
        {
            _stateLock.Release();
        }

        if (changed)
        {
            TimerStateChanged?.Invoke();
            TimerTicked?.Invoke();
        }
    }

    public async Task TogglePauseAsync()
    {
        if (_isRunning)
        {
            await PauseAsync();
        }
        else if (_isPaused)
        {
            await ResumeAsync();
        }
    }

    public async Task StopAsync()
    {
        var changed = false;
        await _stateLock.WaitAsync();
        try
        {
            if (_runningEntry != null && (_isRunning || _isPaused))
            {
                await StopCoreAsync();
                changed = true;
            }
        }
        finally
        {
            _stateLock.Release();
        }

        if (changed)
        {
            TimerStateChanged?.Invoke();
            TimerTicked?.Invoke();
        }
    }

    public async Task ToggleAsync(long workItemId, string? notes = null)
    {
        if (HasActiveSession)
        {
            await StopAsync();
        }
        else
        {
            await StartAsync(workItemId, notes);
        }
    }

    public async Task UpdateRunningNotesAsync(string? notes)
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_runningEntry == null || !HasActiveSession)
            {
                return;
            }

            _runningEntry.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            await _timeEntryRepo.UpdateAsync(_runningEntry);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public void Tick()
    {
        ClockTicked?.Invoke();
        if (_isRunning)
        {
            TimerTicked?.Invoke();
        }
    }

    public async Task<string?> GetRunningWorkItemTitleAsync()
    {
        if (_runningWorkItem != null)
        {
            return _runningWorkItem.Title;
        }

        if (_runningEntry != null)
        {
            _runningWorkItem = await _workItemRepo.GetByIdAsync(_runningEntry.WorkItemId);
            return _runningWorkItem?.Title;
        }

        return null;
    }

    private async Task StopCoreAsync()
    {
        if (_runningEntry == null)
        {
            return;
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        if (_isRunning)
        {
            var liveMs = (long)Math.Max(0, (utcNow - _segmentStartUtc).TotalMilliseconds);
            _runningEntry.DurationMs = Math.Max(0, _runningEntry.DurationMs + liveMs);
        }

        _runningEntry.EndTime = utcNow;

        await _timeEntryRepo.UpdateAsync(_runningEntry);
        await _settingsRepo.SetAsync(SettingKeys.PausedEntryId, "0");
        await _settingsRepo.SetAsync(SettingKeys.ActiveSegmentStartUtc, string.Empty);

        _runningEntry = null;
        _runningWorkItem = null;
        _isRunning = false;
        _isPaused = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _periodicTimer?.Stop();
        _periodicTimer?.Dispose();
        _stateLock.Dispose();
    }
}
