namespace WinQuickSwitch.Features.Widget;

internal enum WidgetHotkeyAction
{
    ToggleWidget,
    Display,
    Audio,
    Devices,
}

[Flags]
internal enum WidgetHotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

internal sealed record WidgetShortcut(
    WidgetHotkeyModifiers Modifiers,
    int VirtualKey)
{
    public bool IsValid =>
        HasPrimaryModifier(Modifiers) &&
        IsSupportedVirtualKey(VirtualKey);

    public string DisplayText
    {
        get
        {
            List<string> parts = [];

            AddModifier(parts, WidgetHotkeyModifiers.Win, "Win");
            AddModifier(parts, WidgetHotkeyModifiers.Control, "Ctrl");
            AddModifier(parts, WidgetHotkeyModifiers.Alt, "Alt");
            AddModifier(parts, WidgetHotkeyModifiers.Shift, "Shift");
            parts.Add(GetKeyLabel(VirtualKey));
            return string.Join(" + ", parts);
        }
    }

    public static bool TryCreate(
        WidgetHotkeyModifiers modifiers,
        int virtualKey,
        out WidgetShortcut? shortcut)
    {
        WidgetShortcut candidate = new(modifiers, virtualKey);
        shortcut = candidate.IsValid ? candidate : null;
        return shortcut is not null;
    }

    private static bool HasPrimaryModifier(WidgetHotkeyModifiers modifiers) =>
        (modifiers &
            (WidgetHotkeyModifiers.Win |
             WidgetHotkeyModifiers.Control |
             WidgetHotkeyModifiers.Alt)) != 0;

    private static bool IsSupportedVirtualKey(int virtualKey) =>
        virtualKey is >= 0x30 and <= 0x39 or
            >= 0x41 and <= 0x5A or
            >= 0x70 and <= 0x7B;

    private static string GetKeyLabel(int virtualKey)
    {
        if (virtualKey is >= 0x70 and <= 0x7B)
        {
            return $"F{virtualKey - 0x6F}";
        }

        return ((char)virtualKey).ToString();
    }

    private void AddModifier(
        ICollection<string> parts,
        WidgetHotkeyModifiers modifier,
        string label)
    {
        if ((Modifiers & modifier) != 0)
        {
            parts.Add(label);
        }
    }
}

internal sealed record WidgetSettings(
    bool UseDarkTheme,
    WidgetShortcut? ToggleWidget,
    WidgetShortcut? Display,
    WidgetShortcut? Audio,
    WidgetShortcut? Devices)
{
    public static WidgetSettings Default { get; } = new(
        true,
        new WidgetShortcut(
            WidgetHotkeyModifiers.Win | WidgetHotkeyModifiers.Shift,
            0x51),
        null,
        null,
        null);

    public WidgetShortcut? GetShortcut(WidgetHotkeyAction action) =>
        action switch
        {
            WidgetHotkeyAction.ToggleWidget => ToggleWidget,
            WidgetHotkeyAction.Display => Display,
            WidgetHotkeyAction.Audio => Audio,
            WidgetHotkeyAction.Devices => Devices,
            _ => null,
        };

    public WidgetSettings WithShortcut(
        WidgetHotkeyAction action,
        WidgetShortcut? shortcut) =>
        action switch
        {
            WidgetHotkeyAction.ToggleWidget => this with { ToggleWidget = shortcut },
            WidgetHotkeyAction.Display => this with { Display = shortcut },
            WidgetHotkeyAction.Audio => this with { Audio = shortcut },
            WidgetHotkeyAction.Devices => this with { Devices = shortcut },
            _ => this,
        };

    public bool IsShortcutUsedByAnotherAction(
        WidgetHotkeyAction action,
        WidgetShortcut shortcut) =>
        Enum.GetValues<WidgetHotkeyAction>()
            .Any(candidate =>
                candidate != action &&
                GetShortcut(candidate) == shortcut);

    public WidgetSettings Normalize()
    {
        WidgetSettings normalized = this with
        {
            ToggleWidget = NormalizeShortcut(ToggleWidget),
            Display = NormalizeShortcut(Display),
            Audio = NormalizeShortcut(Audio),
            Devices = NormalizeShortcut(Devices),
        };

        HashSet<WidgetShortcut> used = [];

        foreach (WidgetHotkeyAction action in Enum.GetValues<WidgetHotkeyAction>())
        {
            WidgetShortcut? shortcut = normalized.GetShortcut(action);

            if (shortcut is not null && !used.Add(shortcut))
            {
                normalized = normalized.WithShortcut(action, null);
            }
        }

        return normalized;
    }

    private static WidgetShortcut? NormalizeShortcut(WidgetShortcut? shortcut) =>
        shortcut is { IsValid: true } ? shortcut : null;
}

internal interface IWidgetSettingsStore
{
    WidgetSettings Load();

    bool TrySave(WidgetSettings settings, out string? errorMessage);
}
