using System.Runtime.InteropServices;

namespace WinQuickSwitch.Platform.Windows;

internal static class WindowsWindowTheme
{
    internal const int UseImmersiveDarkMode = 20;
    internal const int BorderColor = 34;
    internal const int CaptionColor = 35;
    internal const int TextColor = 36;

    private static readonly int PageColor =
        ToColorReference(0x10, 0x13, 0x18);
    private static readonly int BorderColorValue =
        ToColorReference(0x2A, 0x30, 0x3A);
    private static readonly int PrimaryTextColor =
        ToColorReference(0xF3, 0xF5, 0xF7);

    public static void ApplyDarkTitleBar(IntPtr windowHandle) =>
        ApplyDarkTitleBar(windowHandle, NativeDwmAttributeSetter.Instance);

    internal static void ApplyDarkTitleBar(
        IntPtr windowHandle,
        IDwmAttributeSetter attributeSetter)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        attributeSetter.Set(windowHandle, UseImmersiveDarkMode, 1);
        attributeSetter.Set(windowHandle, BorderColor, BorderColorValue);
        attributeSetter.Set(windowHandle, CaptionColor, PageColor);
        attributeSetter.Set(windowHandle, TextColor, PrimaryTextColor);
    }

    internal static int ToColorReference(byte red, byte green, byte blue) =>
        red | green << 8 | blue << 16;
}

internal interface IDwmAttributeSetter
{
    void Set(IntPtr windowHandle, int attribute, int value);
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
