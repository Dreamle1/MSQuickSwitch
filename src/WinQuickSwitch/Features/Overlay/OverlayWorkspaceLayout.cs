using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinQuickSwitch.Features.Overlay;

/// <summary>
/// The feature cards that can be hosted by the overlay workspace.  Home is a
/// route/dashboard rather than a required first-run card, but it is included
/// in the contract so saved layouts can treat every launcher item uniformly.
/// </summary>
public enum OverlayWidgetType
{
    Home,
    Display,
    Audio,
    Devices,
    Favorites,
    Options,
    Profiles,
    About,
}

/// <summary>
/// A work area in the same coordinate space as overlay window placement.
/// Negative coordinates are valid for monitors positioned left or above the
/// primary monitor.
/// </summary>
public readonly record struct OverlayWorkArea(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;

    public bool IsValid =>
        double.IsFinite(Left) &&
        double.IsFinite(Top) &&
        double.IsFinite(Right) &&
        double.IsFinite(Bottom) &&
        Right > Left &&
        Bottom > Top;

    public OverlayWorkArea Normalize()
    {
        double left = NormalizeCoordinate(Left);
        double top = NormalizeCoordinate(Top);
        double right = NormalizeCoordinate(Right, left + 1);
        double bottom = NormalizeCoordinate(Bottom, top + 1);

        if (right <= left)
        {
            right = left + 1;
        }

        if (bottom <= top)
        {
            bottom = top + 1;
        }

        return new(left, top, right, bottom);
    }

    private static double NormalizeCoordinate(
        double value,
        double fallback = 0) =>
        double.IsFinite(value) ? value : fallback;
}

/// <summary>
/// One persisted overlay card placement.  Coordinates are monitor/work-area
/// coordinates, not a display-topology instruction; normalizing or clamping a
/// placement never changes monitor arrangement or rotation.
/// </summary>
public sealed record OverlayWidgetPlacement(
    OverlayWidgetType Widget,
    double Left = 0,
    double Top = 0,
    double Width = OverlayWidgetPlacement.DefaultWidth,
    double Height = OverlayWidgetPlacement.DefaultHeight,
    bool IsVisible = true,
    bool IsCollapsed = false,
    int ZIndex = 0,
    bool HasCustomSize = false,
    int ContentLayoutVersion = 0)
{
    /// <summary>
    /// Version of the shared feature-view content that the saved dimensions
    /// were fitted against. Zero means the host must perform one content-fit
    /// pass before revealing an untouched widget.
    /// </summary>
    public const int CurrentContentLayoutVersion = 1;
    public const double DefaultWidth = 360;
    public const double DefaultHeight = 260;
    public const double MinimumWidth = 280;
    // Content-fit resets may need more room than the original compact
    // presets, especially for endpoint/device inventories.  The workspace
    // still clamps each card to the active monitor's usable surface.
    public const double MaximumWidth = 960;
    public const double MinimumHeight = 180;
    public const double MaximumHeight = 900;

    public OverlayWidgetPlacement Normalize()
    {
        OverlayWidgetType widget = Enum.IsDefined(Widget)
            ? Widget
            : OverlayWidgetType.Home;

        return this with
        {
            Widget = widget,
            Left = NormalizeCoordinate(Left),
            Top = NormalizeCoordinate(Top),
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
            ZIndex = Math.Clamp(ZIndex, 0, 10_000),
            ContentLayoutVersion = HasCustomSize
                ? CurrentContentLayoutVersion
                : Math.Clamp(
                    ContentLayoutVersion,
                    0,
                    CurrentContentLayoutVersion),
        };
    }

    private static double NormalizeCoordinate(double value) =>
        double.IsFinite(value) ? value : 0;

    private static double NormalizeDimension(
        double value,
        double fallback,
        double minimum,
        double maximum) =>
        double.IsFinite(value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
}

/// <summary>
/// Saved placements for one monitor identity.  The identity should be the
/// value returned by MONITORINFOEX.szDevice (for example, DISPLAY1).
/// </summary>
public sealed record OverlayMonitorLayout(
    string MonitorId,
    IReadOnlyList<OverlayWidgetPlacement>? Widgets = null)
{
    public OverlayMonitorLayout Normalize(bool profilesEnabled = true)
    {
        List<OverlayWidgetPlacement> normalized = [];
        HashSet<OverlayWidgetType> seen = [];

        foreach (OverlayWidgetPlacement? placement in Widgets ?? [])
        {
            if (placement is null)
            {
                continue;
            }

            OverlayWidgetPlacement candidate = placement.Normalize();

            if (!profilesEnabled && candidate.Widget == OverlayWidgetType.Profiles)
            {
                continue;
            }

            // Favorites are now part of the Audio surface.  Drop the legacy
            // standalone card while normalizing persisted layouts so an old
            // workspace cannot recreate the duplicate widget.
            if (candidate.Widget == OverlayWidgetType.Favorites)
            {
                continue;
            }

            if (seen.Add(candidate.Widget))
            {
                normalized.Add(candidate);
            }
        }

        return new(
            NormalizeMonitorId(MonitorId),
            new ReadOnlyCollection<OverlayWidgetPlacement>(normalized));
    }

    public OverlayWidgetPlacement? Find(OverlayWidgetType widget) =>
        (Widgets ?? []).FirstOrDefault(candidate => candidate.Widget == widget);

    internal static string NormalizeMonitorId(string? monitorId) =>
        (monitorId ?? string.Empty).Trim();

    internal static bool AreMonitorIdsEquivalent(string? first, string? second)
    {
        static string NormalizeDeviceName(string? value)
        {
            string normalized = NormalizeMonitorId(value);
            return normalized.StartsWith(
                @"\\.\",
                StringComparison.OrdinalIgnoreCase)
                ? normalized[4..]
                : normalized;
        }

        return string.Equals(
            NormalizeDeviceName(first),
            NormalizeDeviceName(second),
            StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Versioned, per-monitor workspace settings.  This is intentionally a
/// separate contract from WidgetSettings so the overlay can evolve its layout
/// without changing the existing global-hotkey/floating-window schema.
/// </summary>
public sealed record OverlayWorkspaceSettings(
    int LayoutVersion = OverlayWorkspaceSettings.CurrentLayoutVersion,
    double InspectorWidth = OverlayWorkspaceSettings.DefaultInspectorWidth,
    IReadOnlyDictionary<string, OverlayMonitorLayout>? MonitorLayouts = null,
    // Kept only as a compatibility bridge for old JSON.  New saves leave this
    // null and use InspectorWidth instead.
    double? OverlaySidebarWidth = null)
{
    public const int CurrentLayoutVersion = 6;
    public const double DefaultInspectorWidth = 720;
    public const double MinimumInspectorWidth = 520;
    public const double MaximumInspectorWidth = 900;

    public static OverlayWorkspaceSettings Default { get; } = new(
        CurrentLayoutVersion,
        DefaultInspectorWidth,
        new Dictionary<string, OverlayMonitorLayout>(
            StringComparer.OrdinalIgnoreCase));

    public OverlayWorkspaceSettings Normalize(bool profilesEnabled = true)
    {
        Dictionary<string, OverlayMonitorLayout> normalized =
            new(StringComparer.OrdinalIgnoreCase);

        foreach ((string key, OverlayMonitorLayout? value) in
            MonitorLayouts ?? new Dictionary<string, OverlayMonitorLayout>())
        {
            string monitorId = OverlayMonitorLayout.NormalizeMonitorId(
                string.IsNullOrWhiteSpace(key) ? value?.MonitorId : key);

            if (monitorId.Length == 0 || value is null)
            {
                continue;
            }

            OverlayMonitorLayout layout = (value with { MonitorId = monitorId })
                .Normalize(profilesEnabled);
            if (LayoutVersion < 2 &&
                OverlayWorkspaceLayoutMigration.LooksGenerated(layout))
            {
                layout = OverlayWorkspaceLayoutMigration
                    .UseCurrentDefaultComposition(layout);
            }

            if (LayoutVersion < 6)
            {
                layout = OverlayWorkspaceLayoutMigration
                    .PrepareContentFitMigration(layout);
            }

            normalized[monitorId] = layout;
        }

        return new(
            CurrentLayoutVersion,
            NormalizeInspectorWidth(InspectorWidth),
            normalized,
            NormalizeLegacySidebarWidth(OverlaySidebarWidth));
    }

    public OverlayMonitorLayout GetOrCreateLayout(
        string monitorId,
        OverlayWorkArea workArea,
        bool profilesEnabled = true)
    {
        string normalizedId = OverlayMonitorLayout.NormalizeMonitorId(monitorId);
        OverlayWorkspaceSettings normalized = Normalize(profilesEnabled);

        if (normalized.MonitorLayouts is not null &&
            normalized.MonitorLayouts.TryGetValue(normalizedId, out OverlayMonitorLayout? saved))
        {
            return OverlayWorkspaceLayoutCalculator.ClampToWorkArea(
                saved,
                workArea,
                profilesEnabled);
        }

        return OverlayWorkspaceLayoutCalculator.CreateDefaultLayout(
            normalizedId,
            workArea,
            profilesEnabled);
    }

    /// <summary>
    /// Finds the saved layout for a physical monitor.  The legacy
    /// <c>virtual-screen</c> bucket is intentionally not treated as a physical
    /// monitor fallback; callers should create a fresh default layout when a
    /// monitor has no saved entry.
    /// </summary>
    public OverlayMonitorLayout? FindSavedLayout(string monitorId)
    {
        string normalizedId = OverlayMonitorLayout.NormalizeMonitorId(monitorId);
        if (normalizedId.Length == 0 || MonitorLayouts is null)
        {
            return null;
        }

        if (MonitorLayouts.TryGetValue(
                normalizedId,
                out OverlayMonitorLayout? exact))
        {
            return exact;
        }

        return MonitorLayouts.Values.FirstOrDefault(layout =>
            OverlayMonitorLayout.AreMonitorIdsEquivalent(
                layout.MonitorId,
                normalizedId));
    }

    public OverlayWorkspaceSettings WithLayout(OverlayMonitorLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        Dictionary<string, OverlayMonitorLayout> layouts =
            new(
                MonitorLayouts ??
                new Dictionary<string, OverlayMonitorLayout>(),
                StringComparer.OrdinalIgnoreCase)
            {
                [OverlayMonitorLayout.NormalizeMonitorId(layout.MonitorId)] = layout,
            };

        return this with { MonitorLayouts = layouts };
    }

    internal static double NormalizeInspectorWidth(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, MinimumInspectorWidth, MaximumInspectorWidth)
            : DefaultInspectorWidth;

    internal static double? NormalizeLegacySidebarWidth(double? value) =>
        value is double width && double.IsFinite(width)
            ? Math.Clamp(width, 280, 720)
            : null;
}

/// <summary>
/// Pure geometry/default-layout helpers for the workspace.  All values are
/// device-independent units and can be tested without starting WPF or calling
/// Windows display APIs.
/// </summary>
public static class OverlayWorkspaceLayoutCalculator
{
    public const double GridIncrement = 12;
    public const double LayoutMargin = 24;
    public const double LayoutGap = 24;
    public const double LauncherWidth = 300;
    public const double MaximumDefaultCardWidth = 520;
    public const double DefaultCardHeight = 260;
    public const double ContentFitWidthRatio = 0.90;
    public const double ContentFitHeightRatio = 0.88;
    public const double CascadedRevealOffset = 36;

    public static (double Width, double Height) ClampContentFitSize(
        double requestedWidth,
        double requestedHeight,
        double canvasWidth,
        double canvasHeight)
    {
        double safeCanvasWidth = double.IsFinite(canvasWidth)
            ? Math.Max(1, canvasWidth)
            : OverlayWidgetPlacement.DefaultWidth;
        double safeCanvasHeight = double.IsFinite(canvasHeight)
            ? Math.Max(1, canvasHeight)
            : OverlayWidgetPlacement.DefaultHeight;
        double maximumWidth = Math.Min(
            safeCanvasWidth * ContentFitWidthRatio,
            OverlayWidgetPlacement.MaximumWidth);
        double maximumHeight = Math.Min(
            safeCanvasHeight * ContentFitHeightRatio,
            OverlayWidgetPlacement.MaximumHeight);
        double minimumWidth = Math.Min(
            OverlayWidgetPlacement.MinimumWidth,
            maximumWidth);
        double minimumHeight = Math.Min(
            OverlayWidgetPlacement.MinimumHeight,
            maximumHeight);
        double width = double.IsFinite(requestedWidth) && requestedWidth > 0
            ? requestedWidth
            : OverlayWidgetPlacement.DefaultWidth;
        double height = double.IsFinite(requestedHeight) && requestedHeight > 0
            ? requestedHeight
            : OverlayWidgetPlacement.DefaultHeight;

        return (
            Math.Clamp(width, minimumWidth, maximumWidth),
            Math.Clamp(height, minimumHeight, maximumHeight));
    }

    public static double SnapToGrid(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        return Math.Round(
                value / GridIncrement,
                MidpointRounding.AwayFromZero) *
            GridIncrement;
    }

    public static OverlayWidgetPlacement SnapToGrid(
        OverlayWidgetPlacement placement)
    {
        OverlayWidgetPlacement normalized = placement.Normalize();

        return normalized with
        {
            Left = SnapToGrid(normalized.Left),
            Top = SnapToGrid(normalized.Top),
            Width = SnapDimension(
                normalized.Width,
                OverlayWidgetPlacement.MinimumWidth,
                OverlayWidgetPlacement.MaximumWidth),
            Height = SnapDimension(
                normalized.Height,
                OverlayWidgetPlacement.MinimumHeight,
                OverlayWidgetPlacement.MaximumHeight),
        };
    }

    public static OverlayWidgetPlacement ClampToWorkArea(
        OverlayWidgetPlacement placement,
        OverlayWorkArea workArea)
    {
        OverlayWidgetPlacement normalized = placement.Normalize();
        OverlayWorkArea area = workArea.Normalize();

        double width = Math.Min(normalized.Width, area.Width);
        double height = Math.Min(normalized.Height, area.Height);
        double maximumLeft = area.Right - width;
        double maximumTop = area.Bottom - height;

        return normalized with
        {
            Width = width,
            Height = height,
            Left = Math.Clamp(normalized.Left, area.Left, maximumLeft),
            Top = Math.Clamp(normalized.Top, area.Top, maximumTop),
        };
    }

    /// <summary>
    /// Resizes from the bottom-right corner without moving the opposite
    /// edges. Growth stops at the work-area boundary instead of shifting the
    /// card left or upward when the pointer reaches an edge.
    /// </summary>
    public static OverlayWidgetPlacement ResizeFromBottomRight(
        OverlayWidgetPlacement placement,
        double horizontalChange,
        double verticalChange,
        OverlayWorkArea workArea,
        double minimumWidth = OverlayWidgetPlacement.MinimumWidth,
        double minimumHeight = OverlayWidgetPlacement.MinimumHeight)
    {
        OverlayWidgetPlacement normalized = placement.Normalize();
        OverlayWorkArea area = workArea.Normalize();
        double left = Math.Clamp(normalized.Left, area.Left, area.Right - 1);
        double top = Math.Clamp(normalized.Top, area.Top, area.Bottom - 1);
        double maximumWidth = Math.Max(1, area.Right - left);
        double maximumHeight = Math.Max(1, area.Bottom - top);
        double safeMinimumWidth = Math.Min(
            maximumWidth,
            double.IsFinite(minimumWidth)
                ? Math.Max(1, minimumWidth)
                : OverlayWidgetPlacement.MinimumWidth);
        double safeMinimumHeight = Math.Min(
            maximumHeight,
            double.IsFinite(minimumHeight)
                ? Math.Max(1, minimumHeight)
                : OverlayWidgetPlacement.MinimumHeight);
        double widthChange = double.IsFinite(horizontalChange)
            ? horizontalChange
            : 0;
        double heightChange = double.IsFinite(verticalChange)
            ? verticalChange
            : 0;

        return normalized with
        {
            Left = left,
            Top = top,
            Width = Math.Clamp(
                normalized.Width + widthChange,
                safeMinimumWidth,
                maximumWidth),
            Height = Math.Clamp(
                normalized.Height + heightChange,
                safeMinimumHeight,
                maximumHeight),
        };
    }

    public static OverlayWidgetPlacement SnapAndClamp(
        OverlayWidgetPlacement placement,
        OverlayWorkArea workArea)
    {
        OverlayWidgetPlacement snapped = SnapToGrid(placement);
        OverlayWorkArea area = workArea.Normalize();

        double width = Math.Min(snapped.Width, area.Width);
        double height = Math.Min(snapped.Height, area.Height);
        double maximumLeft = area.Right - width;
        double maximumTop = area.Bottom - height;

        return snapped with
        {
            Width = width,
            Height = height,
            Left = Math.Clamp(snapped.Left, area.Left, maximumLeft),
            Top = Math.Clamp(snapped.Top, area.Top, maximumTop),
        };
    }

    /// <summary>
    /// Returns true when two visible card rectangles intersect.  A positive
    /// gap reserves breathing room between cards as part of the collision
    /// boundary.
    /// </summary>
    public static bool Overlaps(
        OverlayWidgetPlacement first,
        OverlayWidgetPlacement second,
        double gap = 0)
    {
        OverlayWidgetPlacement left = first.Normalize();
        OverlayWidgetPlacement right = second.Normalize();
        double spacing = double.IsFinite(gap) ? Math.Max(0, gap) : 0;

        return left.Left < right.Left + right.Width + spacing &&
            left.Left + left.Width + spacing > right.Left &&
            left.Top < right.Top + right.Height + spacing &&
            left.Top + left.Height + spacing > right.Top;
    }

    /// <summary>
    /// Finds the first snapped, non-overlapping position for a card.  The
    /// requested position is retained when it is already valid; otherwise the
    /// work area is searched from top-left to bottom-right.  Null means the
    /// available work area cannot contain another card at its current size.
    /// </summary>
    public static OverlayWidgetPlacement? FindFirstAvailablePlacement(
        OverlayWidgetPlacement requested,
        OverlayWorkArea workArea,
        IEnumerable<OverlayWidgetPlacement>? occupied,
        double gap = GridIncrement)
    {
        OverlayWorkArea area = workArea.Normalize();
        OverlayWidgetPlacement desired = SnapAndClamp(requested, area);
        double spacing = double.IsFinite(gap)
            ? Math.Max(0, SnapToGrid(gap))
            : GridIncrement;
        OverlayWidgetPlacement[] blockers = (occupied ?? [])
            .Where(candidate => candidate.IsVisible)
            .Select(candidate => SnapAndClamp(candidate, area))
            .ToArray();

        if (blockers.All(candidate => !Overlaps(desired, candidate, spacing)))
        {
            return desired;
        }

        double startLeft = Math.Min(
            area.Right - desired.Width,
            area.Left + GridIncrement);
        double startTop = Math.Min(
            area.Bottom - desired.Height,
            area.Top + GridIncrement);
        double maximumLeft = area.Right - desired.Width;
        double maximumTop = area.Bottom - desired.Height;

        for (double top = startTop;
             top <= maximumTop + 0.01;
             top += GridIncrement)
        {
            for (double left = startLeft;
                 left <= maximumLeft + 0.01;
                 left += GridIncrement)
            {
                OverlayWidgetPlacement candidate = desired with
                {
                    Left = left,
                    Top = top,
                };

                if (blockers.All(blocker =>
                    !Overlaps(candidate, blocker, spacing)))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public static OverlayMonitorLayout ClampToWorkArea(
        OverlayMonitorLayout layout,
        OverlayWorkArea workArea,
        bool profilesEnabled = true)
    {
        OverlayMonitorLayout normalized = layout.Normalize(profilesEnabled);
        List<OverlayWidgetPlacement> placements = [];

        foreach (OverlayWidgetPlacement placement in normalized.Widgets ?? [])
        {
            // Saved geometry is authoritative. Loading a monitor layout may
            // clamp a card that no longer fits, but must not snap or round a
            // user-selected width, height, or position on every reopen.
            placements.Add(ClampToWorkArea(placement, workArea));
        }

        return normalized with { Widgets = placements };
    }

    public static OverlayMonitorLayout CreateDefaultLayout(
        string monitorId,
        OverlayWorkArea workArea,
        bool profilesEnabled = true)
    {
        OverlayWorkArea area = workArea.Normalize();
        string normalizedId = OverlayMonitorLayout.NormalizeMonitorId(monitorId);
        double availableRight = area.Right - LauncherWidth - LayoutMargin;
        double contentWidth = Math.Max(1, availableRight - area.Left - LayoutMargin);
        double contentHeight = Math.Max(1, area.Height - LayoutMargin * 2);
        bool useTwoColumns =
            contentWidth >= OverlayWidgetPlacement.MinimumWidth * 2 + LayoutGap;
        int columns = useTwoColumns ? 2 : 1;
        // The first layout always has three cards.  Size the cards against the
        // number of rows so a small/high-DPI work area never places the last
        // card below the visible workspace.  The widget body can scroll when
        // its content needs more room.
        int rows = useTwoColumns ? 2 : 4;
        double cellWidth = Math.Max(
            1,
            (contentWidth - LayoutGap * (columns - 1)) / columns);
        double cellHeight = Math.Max(
            1,
            (contentHeight - LayoutGap * (rows - 1)) / rows);

        OverlayWidgetType[] visibleWidgets =
        [
            OverlayWidgetType.Display,
            OverlayWidgetType.Audio,
            OverlayWidgetType.Devices,
        ];

        List<OverlayWidgetPlacement> placements = [];

        for (int index = 0; index < visibleWidgets.Length; index++)
        {
            int row = index / columns;
            int column = index % columns;
            OverlayWidgetType widget = visibleWidgets[index];
            (double presetWidth, double presetHeight) =
                FeatureDescriptorCatalog.GetDefaultSize(widget);
            double cardWidth = Math.Min(presetWidth, cellWidth);
            double cardHeight = Math.Min(presetHeight, cellHeight);
            placements.Add(
                new OverlayWidgetPlacement(
                    widget,
                    area.Left + LayoutMargin + column * (cellWidth + LayoutGap),
                    area.Top + LayoutMargin + row * (cellHeight + LayoutGap),
                    cardWidth,
                    cardHeight,
                    IsVisible: true,
                    ZIndex: index));
        }

        foreach (OverlayWidgetType widget in Enum.GetValues<OverlayWidgetType>())
        {
            if (widget == OverlayWidgetType.Favorites)
            {
                continue;
            }

            if (placements.Any(candidate => candidate.Widget == widget))
            {
                continue;
            }

            (double presetWidth, double presetHeight) =
                FeatureDescriptorCatalog.GetDefaultSize(widget);
            placements.Add(
                new OverlayWidgetPlacement(
                    widget,
                    area.Left + LayoutMargin,
                    area.Top + LayoutMargin,
                    Math.Min(presetWidth, contentWidth),
                    Math.Min(presetHeight, contentHeight),
                    IsVisible: false,
                    ZIndex: placements.Count));
        }

        return new OverlayMonitorLayout(normalizedId, placements);
    }

    public static OverlayMonitorLayout CalculateDefaultLayout(
        string monitorId,
        OverlayWorkArea workArea,
        bool profilesEnabled = true) =>
        CreateDefaultLayout(monitorId, workArea, profilesEnabled);

    private static double SnapDimension(
        double value,
        double minimum,
        double maximum)
    {
        double snapped = SnapToGrid(value);
        return Math.Clamp(snapped, minimum, maximum);
    }
}

internal static class OverlayWorkspaceLayoutMigration
{
    private static readonly HashSet<OverlayWidgetType> DefaultWidgets =
    [
        OverlayWidgetType.Display,
        OverlayWidgetType.Audio,
        OverlayWidgetType.Devices,
    ];

    public static bool LooksGenerated(OverlayMonitorLayout layout)
    {
        OverlayWidgetPlacement[] visible = (layout.Widgets ?? [])
            .Where(placement => placement.IsVisible)
            .ToArray();
        if (visible.Length < 4)
        {
            return false;
        }

        double width = visible[0].Width;
        double height = visible[0].Height;
        return visible.All(placement =>
            !placement.HasCustomSize &&
            Math.Abs(placement.Width - width) < 0.5 &&
            Math.Abs(placement.Height - height) < 0.5);
    }

    public static OverlayMonitorLayout UseCurrentDefaultComposition(
        OverlayMonitorLayout layout) =>
        layout with
        {
            Widgets = (layout.Widgets ?? [])
                .Select(placement => placement with
                {
                    IsVisible = DefaultWidgets.Contains(placement.Widget),
                    HasCustomSize = false,
                })
                .ToArray(),
        };

    public static OverlayMonitorLayout PrepareContentFitMigration(
        OverlayMonitorLayout layout) =>
        layout with
        {
            Widgets = (layout.Widgets ?? [])
                .Select(placement => placement with
                {
                    // User-resized cards keep their exact dimensions. All
                    // untouched legacy cards are fitted once by the WPF host
                    // after their shared feature view reaches a renderable
                    // state.
                    ContentLayoutVersion = placement.HasCustomSize
                        ? OverlayWidgetPlacement.CurrentContentLayoutVersion
                        : 0,
                })
                .ToArray(),
        };
}

/// <summary>
/// JSON boundary for workspace settings.  It accepts both the new
/// InspectorWidth field and the old WidgetSettings OverlaySidebarWidth field
/// so an upgrade does not lose the user's preferred sidebar size.
/// </summary>
public static class OverlayWorkspaceJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(
        OverlayWorkspaceSettings settings,
        bool profilesEnabled = true) =>
        JsonSerializer.Serialize(
            settings.Normalize(profilesEnabled),
            SerializerOptions);

    public static OverlayWorkspaceSettings DeserializeOrDefault(
        string? json,
        double? legacySidebarWidth = null,
        bool profilesEnabled = true)
    {
        if (!TryDeserialize(
            json,
            out OverlayWorkspaceSettings? settings,
            legacySidebarWidth,
            profilesEnabled))
        {
            return OverlayWorkspaceSettings.Default.Normalize(profilesEnabled);
        }

        return settings!;
    }

    public static bool TryDeserialize(
        string? json,
        out OverlayWorkspaceSettings? settings,
        double? legacySidebarWidth = null,
        bool profilesEnabled = true)
    {
        settings = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            settings = JsonSerializer.Deserialize<OverlayWorkspaceSettings>(
                document.RootElement.GetRawText(),
                SerializerOptions);

            if (settings is null)
            {
                return false;
            }

            bool hasInspectorWidth = HasProperty(
                document.RootElement,
                nameof(OverlayWorkspaceSettings.InspectorWidth));
            double? legacy = ReadNumber(
                document.RootElement,
                nameof(OverlayWorkspaceSettings.OverlaySidebarWidth)) ??
                legacySidebarWidth;

            if (!hasInspectorWidth && legacy is double legacyValue)
            {
                settings = settings with { InspectorWidth = legacyValue };
            }

            settings = settings.Normalize(profilesEnabled);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return element.EnumerateObject().Any(property =>
            string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static double? ReadNumber(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!string.Equals(
                property.Name,
                name,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind == JsonValueKind.Number &&
                property.Value.TryGetDouble(out double value)
                ? value
                : null;
        }

        return null;
    }
}
