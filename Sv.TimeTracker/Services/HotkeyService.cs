using System.Runtime.InteropServices;
using System.Windows.Input;
using Sv.TimeTracker.Data.Repositories;
using Sv.TimeTracker.Models;

namespace Sv.TimeTracker.Services;

public readonly record struct HotkeyBinding(
    uint Win32Modifiers,
    uint VirtualKey,
    ModifierKeys Modifiers,
    Key Key,
    string DisplayText);

public static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    public static bool TryParse(string? gestureText, out HotkeyBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(gestureText))
        {
            return false;
        }

        var parts = gestureText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var modifiers = ModifierKeys.None;
        uint win32Mods = 0;
        Key? parsedKey = null;

        foreach (var token in parts)
        {
            switch (token.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModifierKeys.Control;
                    win32Mods |= ModControl;
                    break;
                case "SHIFT":
                    modifiers |= ModifierKeys.Shift;
                    win32Mods |= ModShift;
                    break;
                case "ALT":
                    modifiers |= ModifierKeys.Alt;
                    win32Mods |= ModAlt;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModifierKeys.Windows;
                    win32Mods |= ModWin;
                    break;
                default:
                    if (parsedKey.HasValue)
                    {
                        // Cannot have two primary keys
                        return false;
                    }

                    if (!TryParsePrimaryKey(token, out var key))
                    {
                        return false;
                    }

                    parsedKey = key;
                    break;
            }
        }

        if (modifiers == ModifierKeys.None || !parsedKey.HasValue)
        {
            return false;
        }

        var vk = (uint)KeyInterop.VirtualKeyFromKey(parsedKey.Value);
        if (vk == 0)
        {
            return false;
        }

        var display = Format(modifiers, parsedKey.Value);
        binding = new HotkeyBinding(win32Mods, vk, modifiers, parsedKey.Value, display);
        return true;
    }

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var segments = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            segments.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            segments.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            segments.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            segments.Add("Win");
        }

        var keyLabel = key is >= Key.D0 and <= Key.D9
            ? ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : key.ToString();
        segments.Add(keyLabel);

        return string.Join("+", segments);
    }

    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin or
        Key.System;

    private static bool TryParsePrimaryKey(string token, out Key key)
    {
        if (token.Length == 1 && char.IsAsciiDigit(token[0]))
        {
            key = Key.D0 + (token[0] - '0');
            return true;
        }

        if (Enum.TryParse<Key>(token, ignoreCase: true, out var enumKey) && !IsModifierKey(enumKey))
        {
            key = enumKey;
            return true;
        }

        key = Key.None;
        return false;
    }
}

public interface IHotkeyService : IDisposable
{
    IReadOnlyList<string> RegistrationWarnings { get; }
    void ConfigureActions(Action toggleTimer, Action showHideWindow, Action openWorkItems);
    Task AttachWindowAsync(IntPtr windowHandle);
    Task ReloadBindingsAsync();
    bool HandleWindowMessage(int msg, IntPtr wParam);
}

public sealed class HotkeyService : IHotkeyService
{
    public const int WmHotkey = 0x0312;
    private const int HotkeyToggleId = 1001;
    private const int HotkeyShowHideId = 1002;
    private const int HotkeyWorkItemsId = 1003;

    private readonly ISettingsRepository _settingsRepo;
    private readonly List<string> _warnings = [];
    private readonly HashSet<int> _registeredIds = [];

    private IntPtr _windowHandle;
    private Action? _toggleTimerAction;
    private Action? _showHideAction;
    private Action? _openWorkItemsAction;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotkeyService(ISettingsRepository? settingsRepo = null)
    {
        _settingsRepo = settingsRepo ?? new SettingsRepository();
    }

    public IReadOnlyList<string> RegistrationWarnings => _warnings;

    public void ConfigureActions(Action toggleTimer, Action showHideWindow, Action openWorkItems)
    {
        _toggleTimerAction = toggleTimer;
        _showHideAction = showHideWindow;
        _openWorkItemsAction = openWorkItems;
    }

    public async Task AttachWindowAsync(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        await ReloadBindingsAsync();
    }

    public async Task ReloadBindingsAsync()
    {
        UnregisterAll();
        _warnings.Clear();

        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        var enabled = await _settingsRepo.GetBoolAsync(SettingKeys.GlobalHotkeyEnabled, true);
        if (!enabled)
        {
            return;
        }

        var toggleShortcut = await _settingsRepo.GetAsync(SettingKeys.ToggleTimerHotkey) ?? "Ctrl+Shift+T";
        var showHideShortcut = await _settingsRepo.GetAsync(SettingKeys.ShowHideHotkey) ?? "Ctrl+Shift+H";
        var workItemsShortcut = await _settingsRepo.GetAsync(SettingKeys.WorkItemsHotkey) ?? "Ctrl+Shift+W";

        TryRegisterSingle(HotkeyToggleId, "Toggle Timer", toggleShortcut);
        TryRegisterSingle(HotkeyShowHideId, "Show/Hide Window", showHideShortcut);
        TryRegisterSingle(HotkeyWorkItemsId, "Open Work Items", workItemsShortcut);
    }

    public bool HandleWindowMessage(int msg, IntPtr wParam)
    {
        if (msg != WmHotkey)
        {
            return false;
        }

        var hotkeyId = wParam.ToInt32();
        switch (hotkeyId)
        {
            case HotkeyToggleId:
                _toggleTimerAction?.Invoke();
                return true;
            case HotkeyShowHideId:
                _showHideAction?.Invoke();
                return true;
            case HotkeyWorkItemsId:
                _openWorkItemsAction?.Invoke();
                return true;
            default:
                return false;
        }
    }

    private void TryRegisterSingle(int id, string actionName, string shortcutText)
    {
        if (!HotkeyParser.TryParse(shortcutText, out var binding))
        {
            _warnings.Add($"{actionName}: Invalid shortcut '{shortcutText}'.");
            return;
        }

        if (RegisterHotKey(_windowHandle, id, binding.Win32Modifiers | HotkeyParser.ModNoRepeat, binding.VirtualKey))
        {
            _registeredIds.Add(id);
        }
        else
        {
            _warnings.Add($"{actionName}: '{binding.DisplayText}' is already in use by another app.");
        }
    }

    private void UnregisterAll()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            _registeredIds.Clear();
            return;
        }

        foreach (var id in _registeredIds)
        {
            UnregisterHotKey(_windowHandle, id);
        }

        _registeredIds.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
    }
}
