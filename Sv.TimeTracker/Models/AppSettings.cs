namespace Sv.TimeTracker.Models;

public class AppSettings
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public static class SettingKeys
{
    public const string TopmostEnabled = "TopmostEnabled";
    public const string MinimizeToTray = "MinimizeToTray";
    public const string PersistWindowBounds = "PersistWindowBounds";
    public const string DefaultWorkItemId = "DefaultWorkItemId";
    public const string LastSelectedWorkItemId = "LastSelectedWorkItemId";
    public const string CompactModeEnabled = "CompactModeEnabled";
    public const string BackgroundOpacity = "BackgroundOpacity";

    public const string WindowWidth = "WindowWidth";
    public const string WindowHeight = "WindowHeight";
    public const string WindowLeft = "WindowLeft";
    public const string WindowTop = "WindowTop";

    public const string HourlyDingEnabled = "HourlyDingEnabled";
    public const string HourlyDingVolume = "HourlyDingVolume";
    public const string HourlyDingSoundPath = "HourlyDingSoundPath";
    public const string QuietHoursStart = "QuietHoursStart";
    public const string QuietHoursEnd = "QuietHoursEnd";

    public const string GlobalHotkeyEnabled = "GlobalHotkeyEnabled";
    public const string ToggleTimerHotkey = "ToggleTimerHotkey";
    public const string ShowHideHotkey = "ShowHideHotkey";
    public const string WorkItemsHotkey = "WorkItemsHotkey";

    public const string PausedEntryId = "PausedEntryId";
    public const string ActiveSegmentStartUtc = "ActiveSegmentStartUtc";
}
