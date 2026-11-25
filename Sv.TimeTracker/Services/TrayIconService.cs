using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Sv.TimeTracker.Services;

public interface ITrayIconService : IDisposable
{
    void Initialize(
        Action toggleWindow,
        Action toggleTimer,
        Action togglePause,
        Action openWorkItems,
        Action toggleCompactMode,
        Action exitApplication);

    void UpdateState(
        bool isRunning,
        bool isPaused,
        string elapsedFormatted,
        string? activeWorkItemTitle,
        bool isCompactMode);
}

public sealed class TrayIconService : ITrayIconService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ToolStripMenuItem? _toggleTimerMenuItem;
    private Forms.ToolStripMenuItem? _togglePauseMenuItem;
    private Forms.ToolStripMenuItem? _compactModeMenuItem;
    private Icon? _idleIcon;
    private Icon? _runningIcon;
    private bool _lastRunningState;
    private bool _disposed;

    public void Initialize(
        Action toggleWindow,
        Action toggleTimer,
        Action togglePause,
        Action openWorkItems,
        Action toggleCompactMode,
        Action exitApplication)
    {
        if (_notifyIcon != null)
        {
            return;
        }

        _idleIcon = CreateStopwatchIcon(isRunning: false);
        _runningIcon = CreateStopwatchIcon(isRunning: true);

        var menu = new Forms.ContextMenuStrip();
        _toggleTimerMenuItem = new Forms.ToolStripMenuItem("Start Timer", null, (_, _) => toggleTimer());
        _togglePauseMenuItem = new Forms.ToolStripMenuItem("Pause Timer", null, (_, _) => togglePause())
        {
            Visible = false
        };
        var showHideItem = new Forms.ToolStripMenuItem("Show / Hide Window", null, (_, _) => toggleWindow());
        var workItemsItem = new Forms.ToolStripMenuItem("Work Items...", null, (_, _) => openWorkItems());
        _compactModeMenuItem = new Forms.ToolStripMenuItem("Compact Mini-Timer", null, (_, _) => toggleCompactMode());
        var quitItem = new Forms.ToolStripMenuItem("Quit SV Time Tracker", null, (_, _) => exitApplication());

        menu.Items.Add(_toggleTimerMenuItem);
        menu.Items.Add(_togglePauseMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(showHideItem);
        menu.Items.Add(workItemsItem);
        menu.Items.Add(_compactModeMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(quitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _idleIcon,
            Text = "SV Time Tracker — Idle",
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => toggleWindow();
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                toggleWindow();
            }
        };
    }

    public void UpdateState(
        bool isRunning,
        bool isPaused,
        string elapsedFormatted,
        string? activeWorkItemTitle,
        bool isCompactMode)
    {
        if (_notifyIcon == null)
        {
            return;
        }

        if (_lastRunningState != isRunning)
        {
            _lastRunningState = isRunning;
            _notifyIcon.Icon = isRunning ? _runningIcon : _idleIcon;
        }

        var hasSession = isRunning || isPaused;
        if (_toggleTimerMenuItem != null)
        {
            _toggleTimerMenuItem.Text = hasSession ? $"Stop Timer ({elapsedFormatted})" : "Start Timer";
        }

        if (_togglePauseMenuItem != null)
        {
            _togglePauseMenuItem.Visible = hasSession;
            _togglePauseMenuItem.Text = isPaused ? "Resume Timer" : "Pause Timer";
        }

        if (_compactModeMenuItem != null)
        {
            _compactModeMenuItem.Checked = isCompactMode;
        }

        var rawTooltip = isRunning
            ? $"SV Time Tracker — {elapsedFormatted} ({activeWorkItemTitle ?? "Running"})"
            : isPaused
                ? $"SV Time Tracker — Paused {elapsedFormatted} ({activeWorkItemTitle ?? "Paused"})"
                : "SV Time Tracker — Idle";

        // Win32 NotifyIcon.Text limit is 63 chars on classic Shell_NotifyIcon wrappers
        _notifyIcon.Text = rawTooltip.Length > 63 ? rawTooltip[..60] + "..." : rawTooltip;
    }

    private static Icon CreateStopwatchIcon(bool isRunning)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var primaryColor = isRunning
                ? Color.FromArgb(255, 56, 189, 248) // Sky cyan
                : Color.FromArgb(255, 148, 163, 184); // Slate

            using var bgBrush = new SolidBrush(Color.FromArgb(230, 15, 23, 42));
            g.FillEllipse(bgBrush, 3, 5, 24, 24);

            using var ringPen = new Pen(primaryColor, 2.6f);
            g.DrawEllipse(ringPen, 3, 5, 24, 24);

            // Crown
            g.DrawLine(ringPen, 12, 2, 18, 2);
            g.DrawLine(ringPen, 15, 2, 15, 5);

            // Watch hands
            using var handPen = new Pen(Color.White, 2.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLine(handPen, 15, 17, 15, 10);
            g.DrawLine(handPen, 15, 17, 20, 19);

            if (isRunning)
            {
                using var badgeBrush = new SolidBrush(Color.FromArgb(255, 16, 185, 129));
                using var badgeBorder = new Pen(Color.FromArgb(255, 15, 23, 42), 1.5f);
                g.FillEllipse(badgeBrush, 20, 20, 10, 10);
                g.DrawEllipse(badgeBorder, 20, 20, 10, 10);
            }
        }

        var hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _idleIcon?.Dispose();
        _runningIcon?.Dispose();
    }
}
