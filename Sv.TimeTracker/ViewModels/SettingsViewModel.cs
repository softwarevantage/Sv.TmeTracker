using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Sv.TimeTracker.Data;
using Sv.TimeTracker.Data.Repositories;
using Sv.TimeTracker.Models;
using Sv.TimeTracker.Services;

namespace Sv.TimeTracker.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly IWorkItemRepository _workItemRepo;
    private readonly INotificationService _notificationService;
    private readonly IHotkeyService _hotkeyService;
    private SavedSettingsState? _savedState;
    private bool _isLoading;

    private sealed record SavedSettingsState(
        long DefaultWorkItemId,
        bool TopmostEnabled,
        bool MinimizeToTray,
        bool PersistWindowBounds,
        int BackgroundOpacity,
        bool HourlyDingEnabled,
        bool HalfHourDingEnabled,
        bool ChimeWhenTimerStopped,
        int HourlyDingVolume,
        string HourlyDingSoundPath,
        string QuietHoursStart,
        string QuietHoursEnd,
        bool GlobalHotkeyEnabled,
        string ToggleTimerHotkey,
        string ShowHideHotkey,
        string WorkItemsHotkey);

    public ObservableCollection<WorkItemFilterOption> DefaultWorkItemOptions { get; } = [];

    [ObservableProperty]
    private WorkItemFilterOption? _selectedDefaultWorkItem;

    [ObservableProperty]
    private bool _topmostEnabled = true;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _persistWindowBounds = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundOpacityDisplay))]
    private int _backgroundOpacity = 80;

    [ObservableProperty]
    private bool _hourlyDingEnabled = true;

    [ObservableProperty]
    private bool _halfHourDingEnabled = true;

    [ObservableProperty]
    private bool _chimeWhenTimerStopped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeDisplay))]
    private int _hourlyDingVolume = 60;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoundFileDisplay))]
    private string _hourlyDingSoundPath = string.Empty;

    [ObservableProperty]
    private string _quietHoursStart = "22:00";

    [ObservableProperty]
    private string _quietHoursEnd = "07:00";

    [ObservableProperty]
    private bool _globalHotkeyEnabled = true;

    [ObservableProperty]
    private string _toggleTimerHotkey = "Ctrl+Shift+T";

    [ObservableProperty]
    private string _showHideHotkey = "Ctrl+Shift+H";

    [ObservableProperty]
    private string _workItemsHotkey = "Ctrl+Shift+W";

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasValidationError;

    [ObservableProperty]
    private string _hotkeyWarningsText = string.Empty;

    public string VolumeDisplay => $"{HourlyDingVolume}%";

    public string BackgroundOpacityDisplay => $"{BackgroundOpacity}%";

    public string AppVersionDisplay { get; } = ResolveAppVersion();

    public string DatabasePath => DatabaseHelper.DefaultFactory.DatabasePath;

    public bool HasUnsavedChanges => _savedState != null && CaptureCurrentState() != _savedState;

    private bool CanSaveSettings() => HasUnsavedChanges;

    public string SoundFileDisplay => string.IsNullOrWhiteSpace(HourlyDingSoundPath)
        ? "Built-in Harmonic Chime"
        : Path.GetFileName(HourlyDingSoundPath);

    public event Action<SettingsSnapshot>? SettingsSaved;
    public event Action<int>? BackgroundOpacityPreviewChanged;

    public SettingsViewModel(
        ISettingsRepository settingsRepo,
        IWorkItemRepository workItemRepo,
        INotificationService notificationService,
        IHotkeyService hotkeyService)
    {
        _settingsRepo = settingsRepo;
        _workItemRepo = workItemRepo;
        _notificationService = notificationService;
        _hotkeyService = hotkeyService;
    }

    public async Task LoadAsync()
    {
        var items = (await _workItemRepo.GetAllAsync()).ToList();
        var defaultId = await _settingsRepo.GetLongAsync(SettingKeys.DefaultWorkItemId, 0);

        var topmost = await _settingsRepo.GetBoolAsync(SettingKeys.TopmostEnabled, true);
        var minimizeToTray = await _settingsRepo.GetBoolAsync(SettingKeys.MinimizeToTray, true);
        var persistBounds = await _settingsRepo.GetBoolAsync(SettingKeys.PersistWindowBounds, true);
        var bgOpacity = await _settingsRepo.GetIntAsync(SettingKeys.BackgroundOpacity, 80);
        var dingEnabled = await _settingsRepo.GetBoolAsync(SettingKeys.HourlyDingEnabled, true);
        var halfHourEnabled = await _settingsRepo.GetBoolAsync(SettingKeys.HalfHourDingEnabled, true);
        var chimeWhenStopped = await _settingsRepo.GetBoolAsync(SettingKeys.ChimeWhenTimerStopped, false);
        var dingVolume = await _settingsRepo.GetIntAsync(SettingKeys.HourlyDingVolume, 60);
        var soundPath = await _settingsRepo.GetAsync(SettingKeys.HourlyDingSoundPath) ?? string.Empty;
        var quietStart = await _settingsRepo.GetAsync(SettingKeys.QuietHoursStart) ?? "22:00";
        var quietEnd = await _settingsRepo.GetAsync(SettingKeys.QuietHoursEnd) ?? "07:00";
        var hotkeysEnabled = await _settingsRepo.GetBoolAsync(SettingKeys.GlobalHotkeyEnabled, true);
        var toggleHotkey = await _settingsRepo.GetAsync(SettingKeys.ToggleTimerHotkey) ?? "Ctrl+Shift+T";
        var showHideHotkey = await _settingsRepo.GetAsync(SettingKeys.ShowHideHotkey) ?? "Ctrl+Shift+H";
        var workItemsHotkey = await _settingsRepo.GetAsync(SettingKeys.WorkItemsHotkey) ?? "Ctrl+Shift+W";

        UiDispatcher.Invoke(() =>
        {
            _isLoading = true;
            try
            {
                DefaultWorkItemOptions.Clear();
                var noneOption = new WorkItemFilterOption(0, "Remember Last Selected");
                DefaultWorkItemOptions.Add(noneOption);
                foreach (var item in items)
                {
                    DefaultWorkItemOptions.Add(new WorkItemFilterOption(item.Id, item.Title, item.Color));
                }

                SelectedDefaultWorkItem = DefaultWorkItemOptions.FirstOrDefault(o => o.Id == defaultId) ?? noneOption;
                TopmostEnabled = topmost;
                MinimizeToTray = minimizeToTray;
                PersistWindowBounds = persistBounds;
                BackgroundOpacity = Math.Clamp(bgOpacity, 35, 100);
                HourlyDingEnabled = dingEnabled;
                HalfHourDingEnabled = halfHourEnabled;
                ChimeWhenTimerStopped = chimeWhenStopped;
                HourlyDingVolume = Math.Clamp(dingVolume, 0, 100);
                HourlyDingSoundPath = soundPath;
                QuietHoursStart = quietStart;
                QuietHoursEnd = quietEnd;
                GlobalHotkeyEnabled = hotkeysEnabled;
                ToggleTimerHotkey = toggleHotkey;
                ShowHideHotkey = showHideHotkey;
                WorkItemsHotkey = workItemsHotkey;
                HotkeyWarningsText = string.Join("  ", _hotkeyService.RegistrationWarnings);

                _savedState = CaptureCurrentState();
            }
            finally
            {
                _isLoading = false;
            }

            RefreshDirtyState();
        });
    }

    [RelayCommand]
    public void TestChime()
    {
        _notificationService.PlayTestChime(Math.Max(15, HourlyDingVolume), HourlyDingSoundPath);
    }

    [RelayCommand]
    public void BrowseSoundFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Chime Sound",
            Filter = "Audio Files (*.wav;*.mp3)|*.wav;*.mp3|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            HourlyDingSoundPath = dialog.FileName;
        }
    }

    [RelayCommand]
    public void ClearSoundFile()
    {
        HourlyDingSoundPath = string.Empty;
    }

    [RelayCommand]
    public void ResetDefaultHotkeys()
    {
        ToggleTimerHotkey = "Ctrl+Shift+T";
        ShowHideHotkey = "Ctrl+Shift+H";
        WorkItemsHotkey = "Ctrl+Shift+W";
    }

    [RelayCommand]
    public void OpenWebsite() => OpenExternalUrl("https://www.softwarevantage.com");

    [RelayCommand]
    public void OpenReleases() => OpenExternalUrl("https://github.com/softwarevantage/Sv.TmeTracker/releases/latest");

    private static void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell launch failures on headless machines
        }
    }

    [RelayCommand]
    public void OpenDatabaseFolder()
    {
        var dbPath = DatabasePath;
        var dir = Path.GetDirectoryName(dbPath);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        if (File.Exists(dbPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{dbPath}\"")
            {
                UseShellExecute = true
            });
        }
        else if (Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"")
            {
                UseShellExecute = true
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    public async Task SaveSettingsAsync()
    {
        HasValidationError = false;

        if (!string.IsNullOrWhiteSpace(QuietHoursStart) && !TimeSpan.TryParse(QuietHoursStart, out _))
        {
            HasValidationError = true;
            StatusMessage = "Quiet Hours Start must be HH:MM (e.g. 22:00).";
            return;
        }

        if (!string.IsNullOrWhiteSpace(QuietHoursEnd) && !TimeSpan.TryParse(QuietHoursEnd, out _))
        {
            HasValidationError = true;
            StatusMessage = "Quiet Hours End must be HH:MM (e.g. 07:00).";
            return;
        }

        if (GlobalHotkeyEnabled)
        {
            if (!HotkeyParser.TryParse(ToggleTimerHotkey, out var b1) ||
                !HotkeyParser.TryParse(ShowHideHotkey, out var b2) ||
                !HotkeyParser.TryParse(WorkItemsHotkey, out var b3))
            {
                HasValidationError = true;
                StatusMessage = "Shortcuts must include Ctrl, Alt, Shift, or Win + a key (e.g. Ctrl+Shift+T).";
                return;
            }

            ToggleTimerHotkey = b1.DisplayText;
            ShowHideHotkey = b2.DisplayText;
            WorkItemsHotkey = b3.DisplayText;
        }

        var defaultWorkItemId = SelectedDefaultWorkItem?.Id ?? 0;
        await _settingsRepo.SetAsync(SettingKeys.DefaultWorkItemId, defaultWorkItemId.ToString(CultureInfo.InvariantCulture));
        await _settingsRepo.SetAsync(SettingKeys.TopmostEnabled, TopmostEnabled.ToString().ToLowerInvariant());
        await _settingsRepo.SetAsync(SettingKeys.MinimizeToTray, MinimizeToTray.ToString().ToLowerInvariant());
        await _settingsRepo.SetAsync(SettingKeys.PersistWindowBounds, PersistWindowBounds.ToString().ToLowerInvariant());
        await _settingsRepo.SetAsync(SettingKeys.BackgroundOpacity, Math.Clamp(BackgroundOpacity, 35, 100).ToString(CultureInfo.InvariantCulture));
        await _settingsRepo.SetAsync(SettingKeys.HourlyDingEnabled, HourlyDingEnabled.ToString().ToLowerInvariant());
        await _settingsRepo.SetAsync(SettingKeys.HalfHourDingEnabled, HalfHourDingEnabled.ToString().ToLowerInvariant());
        await _settingsRepo.SetAsync(SettingKeys.ChimeWhenTimerStopped, ChimeWhenTimerStopped.ToString().ToLowerInvariant());
        await _settingsRepo.SetAsync(SettingKeys.HourlyDingVolume, HourlyDingVolume.ToString(CultureInfo.InvariantCulture));
        await _settingsRepo.SetAsync(SettingKeys.HourlyDingSoundPath, HourlyDingSoundPath);
        await _settingsRepo.SetAsync(SettingKeys.QuietHoursStart, QuietHoursStart.Trim());
        await _settingsRepo.SetAsync(SettingKeys.QuietHoursEnd, QuietHoursEnd.Trim());
        await _settingsRepo.SetAsync(SettingKeys.GlobalHotkeyEnabled, GlobalHotkeyEnabled.ToString().ToLowerInvariant());
        await _settingsRepo.SetAsync(SettingKeys.ToggleTimerHotkey, ToggleTimerHotkey);
        await _settingsRepo.SetAsync(SettingKeys.ShowHideHotkey, ShowHideHotkey);
        await _settingsRepo.SetAsync(SettingKeys.WorkItemsHotkey, WorkItemsHotkey);

        await _hotkeyService.ReloadBindingsAsync();
        HotkeyWarningsText = string.Join("  ", _hotkeyService.RegistrationWarnings);

        _savedState = CaptureCurrentState();
        RefreshDirtyState();

        StatusMessage = "Settings saved and applied.";
        SettingsSaved?.Invoke(new SettingsSnapshot(TopmostEnabled, MinimizeToTray, PersistWindowBounds, Math.Clamp(BackgroundOpacity, 35, 100)));
    }

    public void SyncTopmostFromExternal(bool topmostEnabled)
    {
        _isLoading = true;
        try
        {
            TopmostEnabled = topmostEnabled;
            if (_savedState != null)
            {
                _savedState = _savedState with { TopmostEnabled = topmostEnabled };
            }
        }
        finally
        {
            _isLoading = false;
        }

        RefreshDirtyState();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_isLoading)
        {
            return;
        }

        if (e.PropertyName is nameof(HasUnsavedChanges) or nameof(StatusMessage) or nameof(HasValidationError) or nameof(HotkeyWarningsText))
        {
            return;
        }

        RefreshDirtyState();
    }

    private SavedSettingsState CaptureCurrentState() => new(
        DefaultWorkItemId: SelectedDefaultWorkItem?.Id ?? 0,
        TopmostEnabled: TopmostEnabled,
        MinimizeToTray: MinimizeToTray,
        PersistWindowBounds: PersistWindowBounds,
        BackgroundOpacity: Math.Clamp(BackgroundOpacity, 35, 100),
        HourlyDingEnabled: HourlyDingEnabled,
        HalfHourDingEnabled: HalfHourDingEnabled,
        ChimeWhenTimerStopped: ChimeWhenTimerStopped,
        HourlyDingVolume: Math.Clamp(HourlyDingVolume, 0, 100),
        HourlyDingSoundPath: HourlyDingSoundPath ?? string.Empty,
        QuietHoursStart: (QuietHoursStart ?? string.Empty).Trim(),
        QuietHoursEnd: (QuietHoursEnd ?? string.Empty).Trim(),
        GlobalHotkeyEnabled: GlobalHotkeyEnabled,
        ToggleTimerHotkey: (ToggleTimerHotkey ?? string.Empty).Trim(),
        ShowHideHotkey: (ShowHideHotkey ?? string.Empty).Trim(),
        WorkItemsHotkey: (WorkItemsHotkey ?? string.Empty).Trim());

    private void RefreshDirtyState()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnBackgroundOpacityChanged(int value)
    {
        BackgroundOpacityPreviewChanged?.Invoke(Math.Clamp(value, 35, 100));
    }

    private static string ResolveAppVersion()
    {
        var asm = typeof(SettingsViewModel).Assembly;
        var infoVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            var clean = infoVersion.Split('+')[0];
            return $"v{clean}";
        }

        var v = asm.GetName().Version;
        return v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v1.0.0";
    }
}
