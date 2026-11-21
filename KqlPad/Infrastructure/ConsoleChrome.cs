using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KqlPad.Infrastructure;

/// <summary>Dark, neon-tinted title bar via DWM (Windows 11; harmlessly ignored on older builds).</summary>
public static class ConsoleChrome
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Attach(Window window) => window.SourceInitialized += (_, _) => Apply(window);

    private static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        Set(hwnd, 20, 1);            // DWMWA_USE_IMMERSIVE_DARK_MODE
        Set(hwnd, 34, 0x972EFF);     // DWMWA_BORDER_COLOR   (COLORREF 0x00BBGGRR) magenta #FF2E97
        Set(hwnd, 35, 0x180A0A);     // DWMWA_CAPTION_COLOR  #0A0A18
        Set(hwnd, 36, 0xFFF000);     // DWMWA_TEXT_COLOR     cyan #00F0FF
    }

    private static void Set(IntPtr hwnd, int attribute, int value)
    {
        try { DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)); }
        catch { /* not available */ }
    }
}
