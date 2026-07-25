using System.Runtime.InteropServices;
using System.Windows.Media;

namespace WinQuickSwitch.Platform.Windows;

internal static class WindowsWindowTheme
{
    internal const int UseImmersiveDarkMode = 20;
    internal const int BorderColor = 34;
    internal const int CaptionColor = 35;
    internal const int TextColor = 36;

    public static void ApplyDarkTitleBar(IntPtr windowHandle) =>
        ApplyDarkTitleBar(windowHandle, NativeDwmAttributeSetter.Instance);

    public static void Apply(
        IntPtr windowHandle,
        bool useDarkTheme,
        Color pageColor,
        Color borderColor,
        Color textColor) =>
        Apply(
            windowHandle,
            useDarkTheme,
            pageColor,
            borderColor,
            textColor,
            NativeDwmAttributeSetter.Instance);

    internal static void ApplyDarkTitleBar(
        IntPtr windowHandle,
        IDwmAttributeSetter attributeSetter)
    {
        Apply(
            windowHandle,
            true,
            Color.FromRgb(0x10, 0x13, 0x18),
            Color.FromRgb(0x2A, 0x30, 0x3A),
            Color.FromRgb(0xF3, 0xF5, 0xF7),
            attributeSetter);
    }

    internal static void Apply(
        IntPtr windowHandle,
        bool useDarkTheme,
        Color pageColor,
        Color borderColor,
        Color textColor,
        IDwmAttributeSetter attributeSetter)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        attributeSetter.Set(windowHandle, UseImmersiveDarkMode, useDarkTheme ? 1 : 0);
        attributeSetter.Set(
            windowHandle,
            BorderColor,
            ToColorReference(borderColor.R, borderColor.G, borderColor.B));
        attributeSetter.Set(
            windowHandle,
            CaptionColor,
            ToColorReference(pageColor.R, pageColor.G, pageColor.B));
        attributeSetter.Set(
            windowHandle,
            TextColor,
            ToColorReference(textColor.R, textColor.G, textColor.B));
        attributeSetter.RefreshFrame(windowHandle);
    }

    internal static int ToColorReference(byte red, byte green, byte blue) =>
        red | green << 8 | blue << 16;
}

internal interface IDwmAttributeSetter
{
    void Set(IntPtr windowHandle, int attribute, int value);

    void RefreshFrame(IntPtr windowHandle);
}

internal sealed class NativeDwmAttributeSetter : IDwmAttributeSetter
{
    public static NativeDwmAttributeSetter Instance { get; } = new();

    private NativeDwmAttributeSetter()
    {
    }

    public void Set(IntPtr windowHandle, int attribute, int value)
    {
        DwmSetWindowAttribute(
            windowHandle,
            attribute,
            ref value,
            Marshal.SizeOf<int>());
    }

    public void RefreshFrame(IntPtr windowHandle)
    {
        const uint flags =
            0x0001 | // SWP_NOSIZE
            0x0002 | // SWP_NOMOVE
            0x0004 | // SWP_NOZORDER
            0x0010 | // SWP_NOACTIVATE
            0x0020; // SWP_FRAMECHANGED

        SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            flags);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
