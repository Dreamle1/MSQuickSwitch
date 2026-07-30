namespace WinQuickSwitch.Features.Widget;

internal enum WidgetHotkeyAction
{
    ToggleWidget,
    Display,
    Audio,
    Devices,
    PcScreenOnly,
    Duplicate,
    Extend,
    SecondScreenOnly,
    FavoriteOutput1,
    FavoriteOutput2,
    FavoriteOutput3,
    FavoriteOutput4,
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
    WidgetShortcut? Devices,
    WidgetShortcut? PcScreenOnly = null,
    WidgetShortcut? Duplicate = null,
    WidgetShortcut? Extend = null,
    WidgetShortcut? SecondScreenOnly = null,
    FavoriteOutputSetting? FavoriteOutput1 = null,
    FavoriteOutputSetting? FavoriteOutput2 = null,
    FavoriteOutputSetting? FavoriteOutput3 = null,
    FavoriteOutputSetting? FavoriteOutput4 = null)
{
    public const int MaximumFavoriteOutputs = 4;

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
            WidgetHotkeyAction.PcScreenOnly => PcScreenOnly,
            WidgetHotkeyAction.Duplicate => Duplicate,
            WidgetHotkeyAction.Extend => Extend,
            WidgetHotkeyAction.SecondScreenOnly => SecondScreenOnly,
            WidgetHotkeyAction.FavoriteOutput1 => FavoriteOutput1?.Shortcut,
            WidgetHotkeyAction.FavoriteOutput2 => FavoriteOutput2?.Shortcut,
            WidgetHotkeyAction.FavoriteOutput3 => FavoriteOutput3?.Shortcut,
            WidgetHotkeyAction.FavoriteOutput4 => FavoriteOutput4?.Shortcut,
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
            WidgetHotkeyAction.PcScreenOnly => this with { PcScreenOnly = shortcut },
            WidgetHotkeyAction.Duplicate => this with { Duplicate = shortcut },
            WidgetHotkeyAction.Extend => this with { Extend = shortcut },
            WidgetHotkeyAction.SecondScreenOnly => this with
            {
                SecondScreenOnly = shortcut,
            },
            WidgetHotkeyAction.FavoriteOutput1 => WithFavoriteShortcut(0, shortcut),
            WidgetHotkeyAction.FavoriteOutput2 => WithFavoriteShortcut(1, shortcut),
            WidgetHotkeyAction.FavoriteOutput3 => WithFavoriteShortcut(2, shortcut),
            WidgetHotkeyAction.FavoriteOutput4 => WithFavoriteShortcut(3, shortcut),
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
            PcScreenOnly = NormalizeShortcut(PcScreenOnly),
            Duplicate = NormalizeShortcut(Duplicate),
            Extend = NormalizeShortcut(Extend),
            SecondScreenOnly = NormalizeShortcut(SecondScreenOnly),
            FavoriteOutput1 = NormalizeFavorite(FavoriteOutput1),
            FavoriteOutput2 = NormalizeFavorite(FavoriteOutput2),
            FavoriteOutput3 = NormalizeFavorite(FavoriteOutput3),
            FavoriteOutput4 = NormalizeFavorite(FavoriteOutput4),
        };

        HashSet<string> endpointIds = new(StringComparer.Ordinal);

        for (int slot = 0; slot < MaximumFavoriteOutputs; slot++)
        {
            FavoriteOutputSetting? favorite = normalized.GetFavorite(slot);

            if (favorite is not null && !endpointIds.Add(favorite.EndpointId))
            {
                normalized = normalized.WithFavorite(slot, null);
            }
        }

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

    public FavoriteOutputSetting? GetFavorite(int slot) =>
        slot switch
        {
            0 => FavoriteOutput1,
            1 => FavoriteOutput2,
            2 => FavoriteOutput3,
            3 => FavoriteOutput4,
            _ => null,
        };

    public WidgetSettings WithFavorite(
        int slot,
        FavoriteOutputSetting? favorite) =>
        slot switch
        {
            0 => this with { FavoriteOutput1 = favorite },
            1 => this with { FavoriteOutput2 = favorite },
            2 => this with { FavoriteOutput3 = favorite },
            3 => this with { FavoriteOutput4 = favorite },
            _ => this,
        };

    public int FindFavoriteSlot(string endpointId)
    {
        for (int slot = 0; slot < MaximumFavoriteOutputs; slot++)
        {
            if (string.Equals(
                GetFavorite(slot)?.EndpointId,
                endpointId,
                StringComparison.Ordinal))
            {
                return slot;
            }
        }

        return -1;
    }

    public int FindOpenFavoriteSlot()
    {
        for (int slot = 0; slot < MaximumFavoriteOutputs; slot++)
        {
            if (GetFavorite(slot) is null)
            {
                return slot;
            }
        }

        return -1;
    }

    public WidgetSettings ResetShortcuts()
    {
        WidgetSettings reset = this with
        {
            ToggleWidget = Default.ToggleWidget,
            Display = null,
            Audio = null,
            Devices = null,
            PcScreenOnly = null,
            Duplicate = null,
            Extend = null,
            SecondScreenOnly = null,
        };

        for (int slot = 0; slot < MaximumFavoriteOutputs; slot++)
        {
            if (reset.GetFavorite(slot) is FavoriteOutputSetting favorite)
            {
                reset = reset.WithFavorite(
                    slot,
                    favorite with { Shortcut = null });
            }
        }

        return reset;
    }

    public static WidgetHotkeyAction GetFavoriteAction(int slot) =>
        slot switch
        {
            0 => WidgetHotkeyAction.FavoriteOutput1,
            1 => WidgetHotkeyAction.FavoriteOutput2,
            2 => WidgetHotkeyAction.FavoriteOutput3,
            3 => WidgetHotkeyAction.FavoriteOutput4,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };

    public static bool TryGetFavoriteSlot(
        WidgetHotkeyAction action,
        out int slot)
    {
        slot = action switch
        {
            WidgetHotkeyAction.FavoriteOutput1 => 0,
            WidgetHotkeyAction.FavoriteOutput2 => 1,
            WidgetHotkeyAction.FavoriteOutput3 => 2,
            WidgetHotkeyAction.FavoriteOutput4 => 3,
            _ => -1,
        };

        return slot >= 0;
    }

    private WidgetSettings WithFavoriteShortcut(
        int slot,
        WidgetShortcut? shortcut)
    {
        FavoriteOutputSetting? favorite = GetFavorite(slot);

        return favorite is null
            ? this
            : WithFavorite(slot, favorite with { Shortcut = shortcut });
    }

    private static WidgetShortcut? NormalizeShortcut(WidgetShortcut? shortcut) =>
        shortcut is { IsValid: true } ? shortcut : null;

    private static FavoriteOutputSetting? NormalizeFavorite(
        FavoriteOutputSetting? favorite) =>
        favorite is not null &&
        !string.IsNullOrWhiteSpace(favorite.EndpointId) &&
        !string.IsNullOrWhiteSpace(favorite.Name)
            ? favorite with
            {
                Shortcut = NormalizeShortcut(favorite.Shortcut),
            }
            : null;
}

internal sealed record FavoriteOutputSetting(
    string EndpointId,
    string Name,
    WidgetShortcut? Shortcut);

internal interface IWidgetSettingsStore
{
    WidgetSettings Load();

    bool TrySave(WidgetSettings settings, out string? errorMessage);
}
