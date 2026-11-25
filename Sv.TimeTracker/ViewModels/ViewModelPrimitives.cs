using System.Windows;

namespace Sv.TimeTracker.ViewModels;

public enum NavigationTab
{
    Timer,
    WorkItems,
    History,
    Settings
}

public record WorkItemFilterOption(long Id, string Title, string Color = "#64748B");

public record SettingsSnapshot(
    bool TopmostEnabled,
    bool MinimizeToTray,
    bool PersistWindowBounds,
    int BackgroundOpacity);

public static class UiDispatcher
{
    public static void Invoke(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
