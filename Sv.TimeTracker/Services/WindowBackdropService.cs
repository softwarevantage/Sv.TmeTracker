using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Sv.TimeTracker.Services;

public static class WindowBackdropService
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaSystemBackdropType = 38;

    private const int DwmwcpRound = 2;
    private const int DwmsbtMainWindowMica = 2;
    private const int DwmsbtTransientWindowAcrylic = 3;
    // COLORREF (0x00BBGGRR) matching #0B1120 dark slate backdrop so Windows accent title-bar tint never bleeds through
    private const int DarkSlateCaptionColorRef = 0x0020110B;
    private const int SubtleBorderColorRef = 0x00402B1E;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int LeftWidth;
        public int RightWidth;
        public int TopHeight;
        public int BottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins pMarInset);

    /// <summary>
    /// Applies Windows 11 native DWM Dark Mode, rounded corners, and Desktop Acrylic frosted-glass backdrop.
    /// Returns true if DWM system backdrop succeeded.
    /// </summary>
    public static bool ApplyFrostedGlassBackdrop(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: not null } source)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            var dark = 1;
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            var roundCorners = DwmwcpRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref roundCorners, sizeof(int));

            var captionColor = DarkSlateCaptionColorRef;
            _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionColor, sizeof(int));

            var borderColor = SubtleBorderColorRef;
            _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            {
                var margins = new Margins { LeftWidth = -1, RightWidth = -1, TopHeight = -1, BottomHeight = -1 };
                _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);

                var acrylic = DwmsbtTransientWindowAcrylic;
                var hr = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref acrylic, sizeof(int));
                if (hr == 0)
                {
                    return true;
                }

                var mica = DwmsbtMainWindowMica;
                hr = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref mica, sizeof(int));
                return hr == 0;
            }
        }
        catch
        {
            // Ignore DWM interop failures on older Windows or RDP sessions
        }

        return false;
    }
}
