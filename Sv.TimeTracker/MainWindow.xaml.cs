using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Sv.TimeTracker.Data.Repositories;
using Sv.TimeTracker.Models;
using Sv.TimeTracker.Services;
using Sv.TimeTracker.ViewModels;

namespace Sv.TimeTracker;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IHotkeyService _hotkeyService;
    private readonly ITrayIconService _trayIconService;
    private readonly ISettingsRepository _settingsRepo;

    private double _expandedHeight = 640;

    public MainWindow(
        MainViewModel viewModel,
        IHotkeyService hotkeyService,
        ITrayIconService trayIconService,
        ISettingsRepository settingsRepo)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _hotkeyService = hotkeyService;
        _trayIconService = trayIconService;
        _settingsRepo = settingsRepo;

        DataContext = _viewModel;

        _hotkeyService.ConfigureActions(
            toggleTimer: () => _ = Dispatcher.InvokeAsync(() => _viewModel.Timer.ToggleTimerCommand.ExecuteAsync(null)),
            showHideWindow: ToggleWindowVisibility,
            openWorkItems: ShowAndOpenWorkItems);

        if (!App.IsSmokeTestMode)
        {
            _trayIconService.Initialize(
                toggleWindow: ToggleWindowVisibility,
                toggleTimer: () => _ = Dispatcher.InvokeAsync(() => _viewModel.Timer.ToggleTimerCommand.ExecuteAsync(null)),
                togglePause: () => _ = Dispatcher.InvokeAsync(() => _viewModel.Timer.TogglePauseCommand.ExecuteAsync(null)),
                openWorkItems: ShowAndOpenWorkItems,
                toggleCompactMode: () => Dispatcher.Invoke(() => _viewModel.ToggleCompactMode()),
                exitApplication: RequestExplicitExit);
        }

        _viewModel.CompactModeChanged += ApplyCompactWindowMetrics;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    public void RequestExplicitExit()
    {
        Dispatcher.Invoke(Close);
    }

    protected override async void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var backdropApplied = WindowBackdropService.ApplyFrostedGlassBackdrop(this);
        if (!backdropApplied && Resources["FallbackShellBackground"] is Brush fallback)
        {
            ShellRootBorder.Background = fallback;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        if (!App.IsSmokeTestMode)
        {
            await _hotkeyService.AttachWindowAsync(hwnd);
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.PersistWindowBounds)
        {
            await RestoreWindowBoundsAsync();
        }

        if (_viewModel.IsCompactMode)
        {
            ApplyCompactWindowMetrics(isCompact: true);
        }
    }

    private void ApplyCompactWindowMetrics(bool isCompact)
    {
        Dispatcher.Invoke(() =>
        {
            if (isCompact)
            {
                if (Height > 260)
                {
                    _expandedHeight = Height;
                }

                MinHeight = 205;
                Height = 215;
            }
            else
            {
                MinHeight = 528;
                Height = Math.Max(528, _expandedHeight);
            }
        });
    }

    private async Task RestoreWindowBoundsAsync()
    {
        var savedWidth = await _settingsRepo.GetDoubleAsync(SettingKeys.WindowWidth);
        var savedHeight = await _settingsRepo.GetDoubleAsync(SettingKeys.WindowHeight);
        var savedLeft = await _settingsRepo.GetDoubleAsync(SettingKeys.WindowLeft);
        var savedTop = await _settingsRepo.GetDoubleAsync(SettingKeys.WindowTop);

        if (savedWidth is >= 380 and <= 2400)
        {
            Width = savedWidth.Value;
        }

        if (savedHeight is >= 500 and <= 2000)
        {
            _expandedHeight = savedHeight.Value;
            if (!_viewModel.IsCompactMode)
            {
                Height = savedHeight.Value;
            }
        }

        if (savedLeft.HasValue && savedTop.HasValue)
        {
            var minX = SystemParameters.VirtualScreenLeft;
            var minY = SystemParameters.VirtualScreenTop;
            var maxX = minX + SystemParameters.VirtualScreenWidth - 120;
            var maxY = minY + SystemParameters.VirtualScreenHeight - 120;

            Left = Math.Clamp(savedLeft.Value, minX, Math.Max(minX, maxX));
            Top = Math.Clamp(savedTop.Value, minY, Math.Max(minY, maxY));
        }
    }

    private async Task SaveWindowBoundsAsync()
    {
        if (!_viewModel.PersistWindowBounds || WindowState != WindowState.Normal)
        {
            return;
        }

        var heightToSave = _viewModel.IsCompactMode ? _expandedHeight : Height;
        await _settingsRepo.SetAsync(SettingKeys.WindowWidth, Width.ToString("F0", CultureInfo.InvariantCulture));
        await _settingsRepo.SetAsync(SettingKeys.WindowHeight, heightToSave.ToString("F0", CultureInfo.InvariantCulture));
        await _settingsRepo.SetAsync(SettingKeys.WindowLeft, Left.ToString("F0", CultureInfo.InvariantCulture));
        await _settingsRepo.SetAsync(SettingKeys.WindowTop, Top.ToString("F0", CultureInfo.InvariantCulture));
    }

    private void ToggleWindowVisibility()
    {
        Dispatcher.Invoke(() =>
        {
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                Hide();
            }
            else
            {
                Show();
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }

                Activate();
            }
        });
    }

    private void ShowAndOpenWorkItems()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            _ = _viewModel.NavigateAsync("WorkItems");
        });
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.MinimizeToTray && !App.IsSmokeTestMode)
        {
            _ = SaveWindowBoundsAsync();
            Hide();
            return;
        }

        WindowState = WindowState.Minimized;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Minimized && _viewModel.MinimizeToTray && !App.IsSmokeTestMode)
        {
            _ = SaveWindowBoundsAsync();
            WindowState = WindowState.Normal;
            Hide();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.PersistWindowBounds && WindowState == WindowState.Normal)
        {
            var heightToSave = _viewModel.IsCompactMode ? _expandedHeight : Height;
            _ = _settingsRepo.SetAsync(SettingKeys.WindowWidth, Width.ToString("F0", CultureInfo.InvariantCulture));
            _ = _settingsRepo.SetAsync(SettingKeys.WindowHeight, heightToSave.ToString("F0", CultureInfo.InvariantCulture));
            _ = _settingsRepo.SetAsync(SettingKeys.WindowLeft, Left.ToString("F0", CultureInfo.InvariantCulture));
            _ = _settingsRepo.SetAsync(SettingKeys.WindowTop, Top.ToString("F0", CultureInfo.InvariantCulture));
        }

        _hotkeyService.Dispose();
        _trayIconService.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkeyService.HandleWindowMessage(msg, wParam))
        {
            handled = true;
        }

        return IntPtr.Zero;
    }
}
