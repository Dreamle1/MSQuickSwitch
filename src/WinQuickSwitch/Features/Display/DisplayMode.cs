namespace WinQuickSwitch.Features.Display;

public enum DisplayMode
{
    PcScreenOnly,
    Duplicate,
    Extend,
    SecondScreenOnly,
}

public static class DisplayModeExtensions
{
    public static string GetDisplayName(this DisplayMode mode) => mode switch
    {
        DisplayMode.PcScreenOnly => "PC screen only",
        DisplayMode.Duplicate => "Duplicate",
        DisplayMode.Extend => "Extend",
        DisplayMode.SecondScreenOnly => "Second screen only",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown display mode."),
    };
}
