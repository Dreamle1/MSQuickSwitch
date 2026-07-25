using System.Windows;
using System.Windows.Media;
using WinQuickSwitch.Platform.Windows;

namespace WinQuickSwitch.Features.Widget;

internal static class WidgetTheme
{
    private static readonly IReadOnlyDictionary<string, Color> DarkPalette =
        new Dictionary<string, Color>
        {
            ["PageBrush"] = Color.FromRgb(0x10, 0x13, 0x18),
            ["CardBrush"] = Color.FromRgb(0x19, 0x1D, 0x24),
            ["BorderBrush"] = Color.FromRgb(0x2A, 0x30, 0x3A),
            ["PrimaryTextBrush"] = Color.FromRgb(0xF3, 0xF5, 0xF7),
            ["SecondaryTextBrush"] = Color.FromRgb(0xAE, 0xB6, 0xC2),
            ["ControlBrush"] = Color.FromRgb(0x22, 0x27, 0x30),
            ["ControlHoverBrush"] = Color.FromRgb(0x2B, 0x34, 0x42),
            ["ControlPressedBrush"] = Color.FromRgb(0x34, 0x42, 0x56),
            ["ControlDisabledBrush"] = Color.FromRgb(0x25, 0x2A, 0x33),
            ["ControlBorderBrush"] = Color.FromRgb(0x3A, 0x43, 0x51),
            ["DisabledTextBrush"] = Color.FromRgb(0x7E, 0x89, 0x99),
            ["SelectionBrush"] = Color.FromRgb(0x2D, 0x7D, 0xCA),
            ["SelectionTextBrush"] = Colors.White,
            ["DefaultBadgeBrush"] = Color.FromRgb(0x28, 0x5C, 0x3D),
            ["CallsBadgeBrush"] = Color.FromRgb(0x23, 0x4E, 0x78),
            ["ScrollTrackBrush"] = Color.FromRgb(0x17, 0x1B, 0x22),
            ["ScrollThumbBrush"] = Color.FromRgb(0x4B, 0x56, 0x66),
        };

    private static readonly IReadOnlyDictionary<string, Color> LightPalette =
        new Dictionary<string, Color>
        {
            ["PageBrush"] = Color.FromRgb(0xF4, 0xF6, 0xF8),
            ["CardBrush"] = Colors.White,
            ["BorderBrush"] = Color.FromRgb(0xCC, 0xD3, 0xDC),
            ["PrimaryTextBrush"] = Color.FromRgb(0x1B, 0x1F, 0x24),
            ["SecondaryTextBrush"] = Color.FromRgb(0x56, 0x61, 0x6F),
            ["ControlBrush"] = Color.FromRgb(0xED, 0xF1, 0xF5),
            ["ControlHoverBrush"] = Color.FromRgb(0xE1, 0xE8, 0xF0),
            ["ControlPressedBrush"] = Color.FromRgb(0xD4, 0xDF, 0xEB),
            ["ControlDisabledBrush"] = Color.FromRgb(0xE8, 0xEB, 0xEF),
            ["ControlBorderBrush"] = Color.FromRgb(0xB8, 0xC2, 0xCE),
            ["DisabledTextBrush"] = Color.FromRgb(0x8A, 0x94, 0xA1),
            ["SelectionBrush"] = Color.FromRgb(0x00, 0x67, 0xC0),
            ["SelectionTextBrush"] = Colors.White,
            ["DefaultBadgeBrush"] = Color.FromRgb(0xCD, 0xEF, 0xD9),
            ["CallsBadgeBrush"] = Color.FromRgb(0xD6, 0xE9, 0xFC),
            ["ScrollTrackBrush"] = Color.FromRgb(0xEE, 0xF1, 0xF4),
            ["ScrollThumbBrush"] = Color.FromRgb(0x9A, 0xA5, 0xB1),
        };

    public static void Apply(bool useDarkTheme, IntPtr windowHandle = default)
    {
        IReadOnlyDictionary<string, Color> palette =
            useDarkTheme ? DarkPalette : LightPalette;
        ResourceDictionary resources = Application.Current.Resources;

        foreach ((string key, Color color) in palette)
        {
            if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = color;
            }
            else
            {
                resources[key] = new SolidColorBrush(color);
            }
        }

        if (windowHandle != IntPtr.Zero)
        {
            WindowsWindowTheme.Apply(
                windowHandle,
                useDarkTheme,
                palette["PageBrush"],
                palette["BorderBrush"],
                palette["PrimaryTextBrush"]);
        }
    }
}
