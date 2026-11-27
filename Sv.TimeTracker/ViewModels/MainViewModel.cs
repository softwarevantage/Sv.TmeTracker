using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sv.TimeTracker.Data.Repositories;
using Sv.TimeTracker.Models;
using Sv.TimeTracker.Services;

namespace Sv.TimeTracker.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ITimerService _timerService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly INotificationService _notificationService;
    private readonly ITrayIconService _trayIconService;
    private readonly IThemeService? _themeService;
    private int _currentOpacity = 80;

    public TimerViewModel Timer { get; }
    public WorkItemsViewModel WorkItems { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimerTab))]
    [NotifyPropertyChangedFor(nameof(IsWorkItemsTab))]
    [NotifyPropertyChangedFor(nameof(IsHistoryTab))]
    [NotifyPropertyChangedFor(nameof(IsSettingsTab))]
    private NavigationTab _currentTab = NavigationTab.Timer;

    [ObservableProperty]
    private bool _isTopmost = true;

    [ObservableProperty]
    private bool _isCompactMode;

    [ObservableProperty]
    private bool _isTimerRunning;

    [ObservableProperty]
    private bool _isTimerPaused;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _persistWindowBounds = true;

    [ObservableProperty]
    private Brush _shellBackgroundBrush = CreateBackdropBrush(80, isDarkTheme: true);

    public bool IsTimerTab => CurrentTab == NavigationTab.Timer;
    public bool IsWorkItemsTab => CurrentTab == NavigationTab.WorkItems;
    public bool IsHistoryTab => CurrentTab == NavigationTab.History;
    public bool IsSettingsTab => CurrentTab == NavigationTab.Settings;

    public event Action<bool>? CompactModeChanged;

    public MainViewModel(
        TimerViewModel timer,
        WorkItemsViewModel workItems,
        HistoryViewModel history,
        SettingsViewModel settings,
        ITimerService timerService,
        ISettingsRepository settingsRepo,
        INotificationService notificationService,
        ITrayIconService trayIconService,
        IThemeService? themeService = null)
    {
        Timer = timer;
        WorkItems = workItems;
        History = history;
        Settings = settings;
        _timerService = timerService;
        _settingsRepo = settingsRepo;
        _notificationService = notificationService;
        _trayIconService = trayIconService;
        _themeService = themeService;

        if (_themeService != null)
        {
            _themeService.EffectiveThemeChanged += _ => ApplyBackgroundOpacity(_currentOpacity);
        }

        _timerService.ClockTicked += OnClockTicked;
        _timerService.TimerTicked += OnTimerTicked;
        _timerService.TimerStateChanged += OnTimerStateChanged;

        Timer.QuickWorkItemCreated += async () =>
        {
            await WorkItems.LoadAsync();
            await History.ReloadWorkItemOptionsAsync();
            await Settings.LoadAsync();
        };

        WorkItems.WorkItemsMutated += async preferredId =>
        {
            await Timer.RefreshWorkItemsAsync(preferredId);
            await History.ReloadWorkItemOptionsAsync();
            await Settings.LoadAsync();
        };

        WorkItems.ActivateWorkItemRequested += async (workItemId, startImmediately) =>
        {
            await Timer.SelectWorkItemAsync(workItemId, startImmediately);
            CurrentTab = NavigationTab.Timer;
        };

        Settings.SettingsSaved += snapshot =>
        {
            IsTopmost = snapshot.TopmostEnabled;
            MinimizeToTray = snapshot.MinimizeToTray;
            PersistWindowBounds = snapshot.PersistWindowBounds;
            _themeService?.ApplyTheme(snapshot.AppTheme);
            ApplyBackgroundOpacity(snapshot.BackgroundOpacity);
        };
    }

    public async Task InitializeAsync()
    {
        IsTopmost = await _settingsRepo.GetBoolAsync(SettingKeys.TopmostEnabled, true);
        MinimizeToTray = await _settingsRepo.GetBoolAsync(SettingKeys.MinimizeToTray, true);
        PersistWindowBounds = await _settingsRepo.GetBoolAsync(SettingKeys.PersistWindowBounds, true);
        IsCompactMode = await _settingsRepo.GetBoolAsync(SettingKeys.CompactModeEnabled, false);
        var appTheme = await _settingsRepo.GetAsync(SettingKeys.AppTheme);
        _themeService?.ApplyTheme(appTheme);
        var bgOpacity = await _settingsRepo.GetIntAsync(SettingKeys.BackgroundOpacity, 80);
        ApplyBackgroundOpacity(bgOpacity);
        Timer.IsCompactMode = IsCompactMode;

        await _timerService.InitializeAsync();
        await Timer.InitializeAsync();
        await WorkItems.LoadAsync();
        await History.InitializeAsync();
        await Settings.LoadAsync();

        SyncTrayState();
    }

    partial void OnIsTopmostChanged(bool value)
    {
        _ = _settingsRepo.SetAsync(SettingKeys.TopmostEnabled, value.ToString().ToLowerInvariant());
        Settings.SyncTopmostFromExternal(value);
    }

    partial void OnIsCompactModeChanged(bool value)
    {
        Timer.IsCompactMode = value;
        if (value)
        {
            CurrentTab = NavigationTab.Timer;
        }

        _ = _settingsRepo.SetAsync(SettingKeys.CompactModeEnabled, value.ToString().ToLowerInvariant());
        SyncTrayState();
        CompactModeChanged?.Invoke(value);
    }

    [RelayCommand]
    public async Task NavigateAsync(string? tabName)
    {
        if (!Enum.TryParse<NavigationTab>(tabName, ignoreCase: true, out var target))
        {
            return;
        }

        if (IsCompactMode && target != NavigationTab.Timer)
        {
            IsCompactMode = false;
        }

        CurrentTab = target;
        if (target == NavigationTab.History)
        {
            await History.LoadHistoryAsync();
        }
        else if (target == NavigationTab.Settings)
        {
            await Settings.LoadAsync();
        }
    }

    [RelayCommand]
    public void ToggleTopmost()
    {
        IsTopmost = !IsTopmost;
    }

    [RelayCommand]
    public void ToggleCompactMode()
    {
        IsCompactMode = !IsCompactMode;
    }

    private void OnClockTicked()
    {
        _ = _notificationService.CheckHourlyDingAsync();
    }

    private void OnTimerTicked()
    {
        SyncTrayState();
    }

    private void OnTimerStateChanged()
    {
        UiDispatcher.Invoke(() =>
        {
            IsTimerRunning = _timerService.IsRunning;
            IsTimerPaused = _timerService.IsPaused;
        });
        SyncTrayState();
    }

    private void SyncTrayState()
    {
        var running = _timerService.IsRunning;
        var paused = _timerService.IsPaused;
        var elapsed = DurationFormatter.Format(_timerService.Elapsed);
        var title = _timerService.RunningWorkItem?.Title;
        UiDispatcher.Invoke(() => _trayIconService.UpdateState(running, paused, elapsed, title, IsCompactMode));
    }

    private void ApplyBackgroundOpacity(int opacityPercent)
    {
        _currentOpacity = Math.Clamp(opacityPercent, 35, 100);
        var isDark = _themeService?.IsDarkTheme ?? true;
        var brush = CreateBackdropBrush(_currentOpacity, isDark);
        UiDispatcher.Invoke(() => ShellBackgroundBrush = brush);
    }

    private static SolidColorBrush CreateBackdropBrush(int opacityPercent, bool isDarkTheme)
    {
        var clamped = Math.Clamp(opacityPercent, 35, 100);
        var alpha = (byte)Math.Round(clamped / 100.0 * 255.0);
        var color = isDarkTheme
            ? Color.FromArgb(alpha, 0x0B, 0x11, 0x20)
            : Color.FromArgb(alpha, 0xE6, 0xEC, 0xF3);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
