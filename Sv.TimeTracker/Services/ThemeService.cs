using System.Windows;
using Microsoft.Win32;

namespace Sv.TimeTracker.Services;

public enum AppThemePreference
{
    System,
    Dark,
    Light
}

public interface IThemeService : IDisposable
{
    AppThemePreference CurrentPreference { get; }
    bool IsDarkTheme { get; }
    event Action<bool>? EffectiveThemeChanged;
    void ApplyTheme(string? preferenceName);
    void ApplyTheme(AppThemePreference preference);
}

public sealed class ThemeService : IThemeService
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private static readonly Uri DarkThemeUri = new("pack://application:,,,/Themes/DarkTheme.xaml", UriKind.Absolute);
    private static readonly Uri LightThemeUri = new("pack://application:,,,/Themes/LightTheme.xaml", UriKind.Absolute);

    private ResourceDictionary? _activeThemeDictionary;
    private bool _disposed;

    public AppThemePreference CurrentPreference { get; private set; } = AppThemePreference.System;
    public bool IsDarkTheme { get; private set; } = true;

    public event Action<bool>? EffectiveThemeChanged;

    public ThemeService()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static AppThemePreference ParsePreference(string? preferenceName)
    {
        if (Enum.TryParse<AppThemePreference>(preferenceName, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return AppThemePreference.System;
    }

    public void ApplyTheme(string? preferenceName)
    {
        ApplyTheme(ParsePreference(preferenceName));
    }

    public void ApplyTheme(AppThemePreference preference)
    {
        CurrentPreference = preference;
        var resolvedDark = preference switch
        {
            AppThemePreference.Dark => true,
            AppThemePreference.Light => false,
            _ => !IsWindowsLightThemeEnabled()
        };

        IsDarkTheme = resolvedDark;

        var app = Application.Current;
        if (app != null)
        {
            app.Dispatcher.Invoke(() =>
            {
#pragma warning disable WPF0001
                var targetMode = resolvedDark ? ThemeMode.Dark : ThemeMode.Light;
                if (app.ThemeMode != targetMode)
                {
                    app.ThemeMode = targetMode;
                }
#pragma warning restore WPF0001

                SwapThemeDictionary(app, resolvedDark);
            });
        }

        EffectiveThemeChanged?.Invoke(resolvedDark);
    }

    private void SwapThemeDictionary(Application app, bool isDark)
    {
        var targetUri = isDark ? DarkThemeUri : LightThemeUri;
        var merged = app.Resources.MergedDictionaries;

        var existingThemeDicts = merged
            .Where(d => IsThemeDictionary(d, "DarkTheme.xaml") || IsThemeDictionary(d, "LightTheme.xaml"))
            .ToList();

        if (existingThemeDicts.Count == 1 &&
            IsThemeDictionary(existingThemeDicts[0], isDark ? "DarkTheme.xaml" : "LightTheme.xaml"))
        {
            _activeThemeDictionary = existingThemeDicts[0];
            return;
        }

        foreach (var oldDict in existingThemeDicts)
        {
            merged.Remove(oldDict);
        }

        var nextDict = new ResourceDictionary { Source = targetUri };
        merged.Add(nextDict);
        _activeThemeDictionary = nextDict;
    }

    private static bool IsThemeDictionary(ResourceDictionary dict, string fileName)
    {
        var source = dict.Source?.OriginalString;
        return !string.IsNullOrWhiteSpace(source) &&
               source.EndsWith(fileName, StringComparison.OrdinalIgnoreCase);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (CurrentPreference == AppThemePreference.System &&
            e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            ApplyTheme(AppThemePreference.System);
        }
    }

    private static bool IsWindowsLightThemeEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            if (key?.GetValue(AppsUseLightThemeValue) is int value)
            {
                return value != 0;
            }
        }
        catch
        {
            // Default to dark if registry lookup is unavailable
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
