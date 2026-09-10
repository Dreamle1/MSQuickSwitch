using WinQuickSwitch.Features.Overlay;

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
    FavoriteInput1,
    FavoriteInput2,
    FavoriteInput3,
    FavoriteInput4,
    // Existing values are append-only so persisted/native IDs remain stable.
    ToggleOverlay,
    Options,
    ToggleWiFi,
    ToggleBluetooth,
    ToggleTaskbarAutoHide,
    RefreshDisplay,
    RefreshAudio,
    RefreshDevices,
    RefreshAll,
}

internal enum FavoriteEndpointRole
{
    General,
    Communications,
    Both,
}

public sealed record FloatingWidgetLayout(
    bool IsOpen = false,
    double? Left = null,
    double? Top = null,
    double Width = 360,
    double Height = 260,
    bool IsPinned = false,
    string? MonitorId = null)
{
    public const double DefaultWidth = 360;
    public const double DefaultHeight = 260;
    public const double MinimumWidth = 280;
    public const double MaximumWidth = 1000;
    public const double MinimumHeight = 180;
    public const double MaximumHeight = 1000;

    public static FloatingWidgetLayout Default { get; } = new();

    public FloatingWidgetLayout Normalize()
    {
        double? normalizedLeft = NormalizeCoordinate(Left);
        double? normalizedTop = NormalizeCoordinate(Top);

        return this with
        {
            Left = normalizedLeft,
            Top = normalizedTop,
            Width = NormalizeDimension(
                Width,
                DefaultWidth,
                MinimumWidth,
                MaximumWidth),
            Height = NormalizeDimension(
                Height,
                DefaultHeight,
                MinimumHeight,
                MaximumHeight),
            MonitorId = string.IsNullOrWhiteSpace(MonitorId)
                ? null
                : MonitorId.Trim(),
        };
    }

    private static double? NormalizeCoordinate(double? coordinate) =>
        coordinate is double value && double.IsFinite(value)
            ? value
            : null;

    private static double NormalizeDimension(
        double value,
        double fallback,
        double minimum,
        double maximum) =>
        double.IsFinite(value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
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
        IsSupportedVirtualKey(VirtualKey) &&
        !IsReservedOverlayNavigation;

    public bool IsReservedOverlayNavigation =>
        Modifiers == WidgetHotkeyModifiers.Control &&
        VirtualKey is >= 0x30 and <= 0x36;

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
    FavoriteOutputSetting? FavoriteOutput4 = null,
    FavoriteInputSetting? FavoriteInput1 = null,
    FavoriteInputSetting? FavoriteInput2 = null,
    FavoriteInputSetting? FavoriteInput3 = null,
    FavoriteInputSetting? FavoriteInput4 = null,
    bool OverlayEnabled = false,
    double OverlayDimmingOpacity = 0.35,
    double OverlaySidebarWidth = 420,
    WidgetShortcut? ToggleOverlay = null,
    FloatingWidgetLayout? FloatingDisplayWidget = null,
    FloatingWidgetLayout? FloatingAudioWidget = null,
    FloatingWidgetLayout? FloatingDevicesWidget = null,
    bool WindowPinned = false,
    OverlayWorkspaceSettings? OverlayWorkspace = null,
    WidgetShortcut? Options = null,
    WidgetShortcut? ToggleWiFi = null,
    WidgetShortcut? ToggleBluetooth = null,
    WidgetShortcut? ToggleTaskbarAutoHide = null,
    WidgetShortcut? RefreshDisplay = null,
    WidgetShortcut? RefreshAudio = null,
    WidgetShortcut? RefreshDevices = null,
    WidgetShortcut? RefreshAll = null,
    IReadOnlyDictionary<string, WidgetShortcut>? CommandShortcuts = null,
    int AudioVolumeStepPercent = 5)
{
    public const int MaximumFavoriteOutputs = 4;
    public const int MaximumFavoriteInputs = 4;
    public const int MaximumFavoriteAliasLength = 60;

    public const double DefaultOverlayDimmingOpacity = 0.35;
    public const double MinimumOverlayDimmingOpacity = 0.0;
    public const double MaximumOverlayDimmingOpacity = 1.0;
    public const double DefaultOverlaySidebarWidth = 420;
    public const double MinimumOverlaySidebarWidth = 280;
    public const double MaximumOverlaySidebarWidth = 720;

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
            WidgetHotkeyAction.FavoriteInput1 => FavoriteInput1?.Shortcut,
            WidgetHotkeyAction.FavoriteInput2 => FavoriteInput2?.Shortcut,
            WidgetHotkeyAction.FavoriteInput3 => FavoriteInput3?.Shortcut,
            WidgetHotkeyAction.FavoriteInput4 => FavoriteInput4?.Shortcut,
            WidgetHotkeyAction.ToggleOverlay => ToggleOverlay,
            WidgetHotkeyAction.Options => Options,
            WidgetHotkeyAction.ToggleWiFi => ToggleWiFi,
            WidgetHotkeyAction.ToggleBluetooth => ToggleBluetooth,
            WidgetHotkeyAction.ToggleTaskbarAutoHide => ToggleTaskbarAutoHide,
            WidgetHotkeyAction.RefreshDisplay => RefreshDisplay,
            WidgetHotkeyAction.RefreshAudio => RefreshAudio,
            WidgetHotkeyAction.RefreshDevices => RefreshDevices,
            WidgetHotkeyAction.RefreshAll => RefreshAll,
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
            WidgetHotkeyAction.FavoriteInput1 => WithInputFavoriteShortcut(0, shortcut),
            WidgetHotkeyAction.FavoriteInput2 => WithInputFavoriteShortcut(1, shortcut),
            WidgetHotkeyAction.FavoriteInput3 => WithInputFavoriteShortcut(2, shortcut),
            WidgetHotkeyAction.FavoriteInput4 => WithInputFavoriteShortcut(3, shortcut),
            WidgetHotkeyAction.ToggleOverlay => this with { ToggleOverlay = shortcut },
            WidgetHotkeyAction.Options => this with { Options = shortcut },
            WidgetHotkeyAction.ToggleWiFi => this with { ToggleWiFi = shortcut },
            WidgetHotkeyAction.ToggleBluetooth => this with { ToggleBluetooth = shortcut },
            WidgetHotkeyAction.ToggleTaskbarAutoHide => this with
            {
                ToggleTaskbarAutoHide = shortcut,
            },
            WidgetHotkeyAction.RefreshDisplay => this with { RefreshDisplay = shortcut },
            WidgetHotkeyAction.RefreshAudio => this with { RefreshAudio = shortcut },
            WidgetHotkeyAction.RefreshDevices => this with { RefreshDevices = shortcut },
            WidgetHotkeyAction.RefreshAll => this with { RefreshAll = shortcut },
            _ => this,
        };

    public WidgetShortcut? GetCommandShortcut(string key) =>
        CommandShortcuts is not null && CommandShortcuts.TryGetValue(key, out WidgetShortcut? shortcut)
            ? shortcut : null;

    public WidgetSettings WithCommandShortcut(string key, WidgetShortcut? shortcut)
    {
        Dictionary<string, WidgetShortcut> bindings = CommandShortcuts is null
            ? new(StringComparer.Ordinal) : new(CommandShortcuts, StringComparer.Ordinal);
        if (shortcut is null) bindings.Remove(key);
        else bindings[key] = shortcut;
        return this with { CommandShortcuts = bindings };
    }

    public bool IsShortcutUsedByAnotherAction(
        WidgetHotkeyAction action,
        WidgetShortcut shortcut) =>
        Enum.GetValues<WidgetHotkeyAction>()
            .Any(candidate =>
                candidate != action &&
                GetShortcut(candidate) == shortcut) ||
        (CommandShortcuts?.Values.Contains(shortcut) ?? false);

    public WidgetSettings Normalize(bool profilesEnabled = true)
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
            Options = NormalizeShortcut(Options),
            ToggleWiFi = NormalizeShortcut(ToggleWiFi),
            ToggleBluetooth = NormalizeShortcut(ToggleBluetooth),
            ToggleTaskbarAutoHide = NormalizeShortcut(ToggleTaskbarAutoHide),
            RefreshDisplay = NormalizeShortcut(RefreshDisplay),
            RefreshAudio = NormalizeShortcut(RefreshAudio),
            RefreshDevices = NormalizeShortcut(RefreshDevices),
            RefreshAll = NormalizeShortcut(RefreshAll),
            AudioVolumeStepPercent = Math.Clamp(AudioVolumeStepPercent, 1, 20),
            CommandShortcuts = CommandShortcuts is null ? null : CommandShortcuts
                .Where(pair => QuickSwitchCommandCatalog.GetKeyedCommands().Any(item => item.Key == pair.Key)
                    && NormalizeShortcut(pair.Value) is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            FavoriteOutput1 = NormalizeFavorite(FavoriteOutput1),
            FavoriteOutput2 = NormalizeFavorite(FavoriteOutput2),
            FavoriteOutput3 = NormalizeFavorite(FavoriteOutput3),
            FavoriteOutput4 = NormalizeFavorite(FavoriteOutput4),
            FavoriteInput1 = NormalizeInputFavorite(FavoriteInput1),
            FavoriteInput2 = NormalizeInputFavorite(FavoriteInput2),
            FavoriteInput3 = NormalizeInputFavorite(FavoriteInput3),
            FavoriteInput4 = NormalizeInputFavorite(FavoriteInput4),
            OverlayDimmingOpacity = NormalizeOverlayDimmingOpacity(
                OverlayDimmingOpacity),
            OverlaySidebarWidth = NormalizeOverlaySidebarWidth(
                OverlaySidebarWidth),
            ToggleOverlay = NormalizeShortcut(ToggleOverlay),
            FloatingDisplayWidget = FloatingDisplayWidget?.Normalize(),
            FloatingAudioWidget = FloatingAudioWidget?.Normalize(),
            FloatingDevicesWidget = FloatingDevicesWidget?.Normalize(),
            OverlayWorkspace = OverlayWorkspace?.Normalize(profilesEnabled),
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

        HashSet<string> inputEndpointIds = new(StringComparer.Ordinal);

        for (int slot = 0; slot < MaximumFavoriteInputs; slot++)
        {
            FavoriteInputSetting? favorite = normalized.GetInputFavorite(slot);

            if (favorite is not null && !inputEndpointIds.Add(favorite.EndpointId))
            {
                normalized = normalized.WithInputFavorite(slot, null);
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

    public FavoriteInputSetting? GetInputFavorite(int slot) =>
        slot switch
        {
            0 => FavoriteInput1,
            1 => FavoriteInput2,
            2 => FavoriteInput3,
            3 => FavoriteInput4,
            _ => null,
        };

    public WidgetSettings WithInputFavorite(
        int slot,
        FavoriteInputSetting? favorite) =>
        slot switch
        {
            0 => this with { FavoriteInput1 = favorite },
            1 => this with { FavoriteInput2 = favorite },
            2 => this with { FavoriteInput3 = favorite },
            3 => this with { FavoriteInput4 = favorite },
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

    public int FindInputFavoriteSlot(string endpointId)
    {
        for (int slot = 0; slot < MaximumFavoriteInputs; slot++)
        {
            if (string.Equals(
                GetInputFavorite(slot)?.EndpointId,
                endpointId,
                StringComparison.Ordinal))
            {
                return slot;
            }
        }

        return -1;
    }

    public int FindOpenInputFavoriteSlot()
    {
        for (int slot = 0; slot < MaximumFavoriteInputs; slot++)
        {
            if (GetInputFavorite(slot) is null)
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
            ToggleOverlay = null,
            Options = null,
            ToggleWiFi = null,
            ToggleBluetooth = null,
            ToggleTaskbarAutoHide = null,
            RefreshDisplay = null,
            RefreshAudio = null,
            RefreshDevices = null,
            RefreshAll = null,
            CommandShortcuts = null,
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

        for (int slot = 0; slot < MaximumFavoriteInputs; slot++)
        {
            if (reset.GetInputFavorite(slot) is FavoriteInputSetting favorite)
            {
                reset = reset.WithInputFavorite(
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

    public static WidgetHotkeyAction GetInputFavoriteAction(int slot) =>
        slot switch
        {
            0 => WidgetHotkeyAction.FavoriteInput1,
            1 => WidgetHotkeyAction.FavoriteInput2,
            2 => WidgetHotkeyAction.FavoriteInput3,
            3 => WidgetHotkeyAction.FavoriteInput4,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };

    public static bool TryGetInputFavoriteSlot(
        WidgetHotkeyAction action,
        out int slot)
    {
        slot = action switch
        {
            WidgetHotkeyAction.FavoriteInput1 => 0,
            WidgetHotkeyAction.FavoriteInput2 => 1,
            WidgetHotkeyAction.FavoriteInput3 => 2,
            WidgetHotkeyAction.FavoriteInput4 => 3,
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

    private WidgetSettings WithInputFavoriteShortcut(
        int slot,
        WidgetShortcut? shortcut)
    {
        FavoriteInputSetting? favorite = GetInputFavorite(slot);

        return favorite is null
            ? this
            : WithInputFavorite(slot, favorite with { Shortcut = shortcut });
    }

    private static WidgetShortcut? NormalizeShortcut(WidgetShortcut? shortcut) =>
        shortcut is { IsValid: true } ? shortcut : null;

    private static double NormalizeOverlayDimmingOpacity(double opacity)
    {
        if (!double.IsFinite(opacity))
        {
            return DefaultOverlayDimmingOpacity;
        }

        return Math.Clamp(
            opacity,
            MinimumOverlayDimmingOpacity,
            MaximumOverlayDimmingOpacity);
    }

    private static double NormalizeOverlaySidebarWidth(double width)
    {
        if (!double.IsFinite(width))
        {
            return DefaultOverlaySidebarWidth;
        }

        return Math.Clamp(
            width,
            MinimumOverlaySidebarWidth,
            MaximumOverlaySidebarWidth);
    }

    private static FavoriteOutputSetting? NormalizeFavorite(
        FavoriteOutputSetting? favorite) =>
        favorite is not null &&
        !string.IsNullOrWhiteSpace(favorite.EndpointId) &&
        !string.IsNullOrWhiteSpace(favorite.Name)
            ? favorite with
            {
                Shortcut = NormalizeShortcut(favorite.Shortcut),
                Name = favorite.Name.Trim(),
                Role = NormalizeRole(favorite.Role),
                Alias = NormalizeAlias(favorite.Alias),
            }
            : null;

    private static FavoriteInputSetting? NormalizeInputFavorite(
        FavoriteInputSetting? favorite) =>
        favorite is not null &&
        !string.IsNullOrWhiteSpace(favorite.EndpointId) &&
        !string.IsNullOrWhiteSpace(favorite.Name)
            ? favorite with
            {
                Shortcut = NormalizeShortcut(favorite.Shortcut),
                Name = favorite.Name.Trim(),
                Role = NormalizeRole(favorite.Role),
                Alias = NormalizeAlias(favorite.Alias),
            }
            : null;

    private static string? NormalizeAlias(string? alias)
    {
        string normalized = (alias ?? string.Empty).Trim();

        if (normalized.Length == 0)
        {
            return null;
        }

        return normalized.Length > MaximumFavoriteAliasLength
            ? normalized[..MaximumFavoriteAliasLength]
            : normalized;
    }

    private static FavoriteEndpointRole NormalizeRole(
        FavoriteEndpointRole role) =>
        Enum.IsDefined(role) ? role : FavoriteEndpointRole.General;
}

internal sealed record FavoriteOutputSetting(
    string EndpointId,
    string Name,
    WidgetShortcut? Shortcut,
    FavoriteEndpointRole Role = FavoriteEndpointRole.General,
    string? Alias = null);

internal sealed record FavoriteInputSetting(
    string EndpointId,
    string Name,
    WidgetShortcut? Shortcut,
    FavoriteEndpointRole Role = FavoriteEndpointRole.General,
    string? Alias = null);

internal interface IWidgetSettingsStore
{
    WidgetSettings Load();

    bool TrySave(WidgetSettings settings, out string? errorMessage);
}
