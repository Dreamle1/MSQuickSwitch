using WinQuickSwitch.Features.Product;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Features.Overlay;

/// <summary>
/// Stable identity for a feature exposed by the compact overlay.  Home is an
/// overlay route rather than a standalone <see cref="WidgetPanel"/> value in
/// the legacy main-window navigation.
/// </summary>
internal enum OverlayFeatureId
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
/// A shared description of a legacy main-window panel.  The overlay and the
/// main window can use this metadata for labels, keyboard numbering, and
/// edition gating without duplicating the panel list.
/// </summary>
internal sealed record WidgetPanelDescriptor(
    WidgetPanel Panel,
    OverlayFeatureId Feature,
    string Title,
    string Description,
    bool IsPaidOnly,
    bool SupportsRefresh,
    bool SupportsInspector,
    int? ShortcutNumber)
{
    public bool IsAvailable(ProductConfiguration configuration) =>
        !IsPaidOnly || configuration.HasFeature(ProductFeature.Profiles);
}

/// <summary>
/// Shared metadata for an overlay launcher entry and its compact widget.
/// </summary>
internal sealed record OverlayFeatureDescriptor(
    OverlayFeatureId Id,
    string Title,
    string Description,
    bool IsPaidOnly,
    bool HasMainWindowSurface,
    bool HasOverlayWidget,
    bool HasOverlayInspector,
    int? ShortcutNumber,
    WidgetPanel? MainPanel,
    double DefaultWidth,
    double DefaultHeight,
    double MinimumWidth = OverlayWidgetPlacement.MinimumWidth,
    double MinimumHeight = OverlayWidgetPlacement.MinimumHeight,
    bool CanResize = true)
{
    public bool IsAvailable(ProductConfiguration configuration) =>
        !IsPaidOnly || configuration.HasFeature(ProductFeature.Profiles);

    public bool HasMainWindowPanel => MainPanel is not null;

    public bool HasOverlaySurface => HasOverlayWidget || HasOverlayInspector;
}

/// <summary>
/// The edition-aware feature contract used by overlay navigation and parity
/// tests.  Keep the ordering of numbered entries aligned with
/// <see cref="WidgetPanelNavigation"/> so existing Ctrl+1 through Ctrl+6
/// shortcuts retain their meaning.
/// </summary>
internal static class FeatureDescriptorCatalog
{
    private static readonly IReadOnlyList<OverlayFeatureDescriptor> AllFeatures =
        Array.AsReadOnly<OverlayFeatureDescriptor>(
        [
            new(
                OverlayFeatureId.Home,
                "Home",
                "Your most-used display, audio, device, and profile actions.",
                IsPaidOnly: false,
                HasMainWindowSurface: true,
                HasOverlayWidget: true,
                HasOverlayInspector: true,
                ShortcutNumber: null,
                MainPanel: null,
                DefaultWidth: 520,
                DefaultHeight: 320,
                CanResize: false),
            new(
                OverlayFeatureId.Display,
                "Display",
                "Switch projection modes and inspect connected displays.",
                IsPaidOnly: false,
                HasMainWindowSurface: true,
                HasOverlayWidget: true,
                HasOverlayInspector: true,
                ShortcutNumber: 1,
                MainPanel: WidgetPanel.Display,
                // Display is fixed-size, so its preset must include the
                // complete action row (including Settings and floating
                // launchers) without relying on a resize grip.
                DefaultWidth: 720,
                DefaultHeight: 420,
                CanResize: false),
            new(
                OverlayFeatureId.Audio,
                "Audio",
                "Choose output and microphone favorites or open audio controls.",
                IsPaidOnly: false,
                HasMainWindowSurface: true,
                HasOverlayWidget: true,
                HasOverlayInspector: true,
                ShortcutNumber: 2,
                MainPanel: WidgetPanel.Audio,
                // Audio includes endpoint role controls and app-volume rows
                // in the compact card, so its reset fallback needs room for
                // both inventories before the monitor cap is applied.
                DefaultWidth: 760,
                DefaultHeight: 600,
                MinimumWidth: 760,
                MinimumHeight: 600,
                CanResize: true),
            new(
                OverlayFeatureId.Devices,
                "Devices",
                "Check wireless radios and connected-device status.",
                IsPaidOnly: false,
                HasMainWindowSurface: true,
                HasOverlayWidget: true,
                HasOverlayInspector: true,
                ShortcutNumber: 3,
                MainPanel: WidgetPanel.Devices,
                // Devices can contain a long hot-plug inventory.  Use a
                // monitor-safe large fallback; content-fit measurement may
                // grow or clamp this further on reset.
                DefaultWidth: 840,
                DefaultHeight: 820,
                MinimumWidth: 840,
                MinimumHeight: 820,
                CanResize: true),
            new(
                OverlayFeatureId.Options,
                "Options",
                "Manage shortcuts, appearance, startup, overlay, and taskbar behavior.",
                IsPaidOnly: false,
                HasMainWindowSurface: true,
                HasOverlayWidget: true,
                HasOverlayInspector: true,
                ShortcutNumber: 4,
                MainPanel: WidgetPanel.Options,
                DefaultWidth: 720,
                DefaultHeight: 380,
                CanResize: false),
            new(
                OverlayFeatureId.Profiles,
                "Profiles",
                "Save and apply coordinated desktop profiles.",
                IsPaidOnly: true,
                HasMainWindowSurface: true,
                HasOverlayWidget: true,
                HasOverlayInspector: true,
                ShortcutNumber: 5,
                MainPanel: WidgetPanel.Profiles,
                DefaultWidth: 720,
                DefaultHeight: 320,
                MinimumWidth: 720,
                MinimumHeight: 320,
                CanResize: true),
            new(
                OverlayFeatureId.About,
                "About",
                "Version, legal documents, publisher, and privacy information.",
                IsPaidOnly: false,
                HasMainWindowSurface: true,
                HasOverlayWidget: true,
                HasOverlayInspector: true,
                ShortcutNumber: 6,
                MainPanel: WidgetPanel.About,
                DefaultWidth: 600,
                DefaultHeight: 280,
                CanResize: false),
        ]);

    private static readonly IReadOnlyList<WidgetPanelDescriptor> AllWidgetPanels =
        Array.AsReadOnly<WidgetPanelDescriptor>(
        [
            new(
                WidgetPanel.Display,
                OverlayFeatureId.Display,
                "Display",
                "Switch projection modes and inspect connected displays.",
                IsPaidOnly: false,
                SupportsRefresh: true,
                SupportsInspector: true,
                ShortcutNumber: 1),
            new(
                WidgetPanel.Audio,
                OverlayFeatureId.Audio,
                "Audio",
                "Choose output and microphone favorites or open audio controls.",
                IsPaidOnly: false,
                SupportsRefresh: true,
                SupportsInspector: true,
                ShortcutNumber: 2),
            new(
                WidgetPanel.Devices,
                OverlayFeatureId.Devices,
                "Devices",
                "Check wireless radios and connected-device status.",
                IsPaidOnly: false,
                SupportsRefresh: true,
                SupportsInspector: true,
                ShortcutNumber: 3),
            new(
                WidgetPanel.Options,
                OverlayFeatureId.Options,
                "Options",
                "Manage shortcuts, appearance, startup, overlay, and taskbar behavior.",
                IsPaidOnly: false,
                SupportsRefresh: false,
                SupportsInspector: true,
                ShortcutNumber: 4),
            new(
                WidgetPanel.Profiles,
                OverlayFeatureId.Profiles,
                "Profiles",
                "Save and apply coordinated desktop profiles.",
                IsPaidOnly: true,
                SupportsRefresh: true,
                SupportsInspector: true,
                ShortcutNumber: 5),
            new(
                WidgetPanel.About,
                OverlayFeatureId.About,
                "About",
                "Version, legal documents, publisher, and privacy information.",
                IsPaidOnly: false,
                SupportsRefresh: false,
                SupportsInspector: true,
                ShortcutNumber: 6),
        ]);

    public static IReadOnlyList<OverlayFeatureDescriptor> GetAvailableFeatures(
        ProductConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return AllFeatures
            .Where(feature => feature.IsAvailable(configuration))
            .ToArray();
    }

    public static IReadOnlyList<OverlayFeatureDescriptor> GetLauncherFeatures(
        ProductConfiguration configuration) =>
        GetAvailableFeatures(configuration)
            .Where(feature => feature.Id != OverlayFeatureId.Home)
            .ToArray();

    public static bool TryUpgradeLegacyDefaultSize(
        OverlayFeatureId id,
        double width,
        double height,
        out double upgradedWidth,
        out double upgradedHeight)
    {
        (double Width, double Height) legacy = id switch
        {
            OverlayFeatureId.Home => (420, 280),
            OverlayFeatureId.Display => (420, 250),
            OverlayFeatureId.Audio => (420, 280),
            OverlayFeatureId.Devices => (420, 280),
            OverlayFeatureId.Options => (420, 300),
            OverlayFeatureId.Profiles => (420, 300),
            OverlayFeatureId.About => (360, 240),
            _ => (double.NaN, double.NaN),
        };

        OverlayFeatureDescriptor? descriptor = AllFeatures
            .FirstOrDefault(feature => feature.Id == id);
        bool usesLegacyDefault = descriptor is not null &&
            Math.Abs(width - legacy.Width) < 0.5 &&
            Math.Abs(height - legacy.Height) < 0.5;

        upgradedWidth = usesLegacyDefault
            ? descriptor!.DefaultWidth
            : width;
        upgradedHeight = usesLegacyDefault
            ? descriptor!.DefaultHeight
            : height;
        return usesLegacyDefault;
    }

    public static (double Width, double Height) GetDefaultSize(
        OverlayWidgetType widget)
    {
        if (Enum.TryParse(
                widget.ToString(),
                ignoreCase: true,
                out OverlayFeatureId feature) &&
            AllFeatures.FirstOrDefault(candidate => candidate.Id == feature) is
                OverlayFeatureDescriptor descriptor)
        {
            return (descriptor.DefaultWidth, descriptor.DefaultHeight);
        }

        return (
            OverlayWidgetPlacement.DefaultWidth,
            OverlayWidgetPlacement.DefaultHeight);
    }

    public static IReadOnlyList<OverlayFeatureDescriptor> GetNumberedFeatures(
        ProductConfiguration configuration) =>
        GetAvailableFeatures(configuration)
            .Where(feature => feature.ShortcutNumber is not null)
            .OrderBy(feature => feature.ShortcutNumber)
            .ToArray();

    public static IReadOnlyList<WidgetPanelDescriptor> GetWidgetPanels(
        ProductConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return AllWidgetPanels
            .Where(panel => panel.IsAvailable(configuration))
            .ToArray();
    }

    public static bool TryGetFeature(
        OverlayFeatureId id,
        ProductConfiguration configuration,
        out OverlayFeatureDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        descriptor = GetAvailableFeatures(configuration)
            .FirstOrDefault(feature => feature.Id == id);
        return descriptor is not null;
    }

    public static bool TryGetWidgetPanel(
        WidgetPanel panel,
        ProductConfiguration configuration,
        out WidgetPanelDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        descriptor = GetWidgetPanels(configuration)
            .FirstOrDefault(candidate => candidate.Panel == panel);
        return descriptor is not null;
    }

    public static bool TryGetMainPanel(
        OverlayFeatureId feature,
        ProductConfiguration configuration,
        out WidgetPanel panel)
    {
        if (TryGetFeature(feature, configuration, out OverlayFeatureDescriptor? descriptor) &&
            descriptor is { MainPanel: WidgetPanel mappedPanel })
        {
            panel = mappedPanel;
            return true;
        }

        panel = default;
        return false;
    }

    public static bool TryFromWidgetPanel(
        WidgetPanel panel,
        out OverlayFeatureId feature)
    {
        feature = panel switch
        {
            WidgetPanel.Display => OverlayFeatureId.Display,
            WidgetPanel.Audio => OverlayFeatureId.Audio,
            WidgetPanel.Devices => OverlayFeatureId.Devices,
            WidgetPanel.Options => OverlayFeatureId.Options,
            WidgetPanel.Profiles => OverlayFeatureId.Profiles,
            WidgetPanel.About => OverlayFeatureId.About,
            _ => default,
        };

        return panel is
            WidgetPanel.Display or
            WidgetPanel.Audio or
            WidgetPanel.Devices or
            WidgetPanel.Options or
            WidgetPanel.Profiles or
            WidgetPanel.About;
    }

    public static bool TryFromWorkspacePanel(
        OverlayWorkspacePanel panel,
        out OverlayFeatureId feature)
    {
        feature = panel switch
        {
            OverlayWorkspacePanel.Home => OverlayFeatureId.Home,
            OverlayWorkspacePanel.Display => OverlayFeatureId.Display,
            OverlayWorkspacePanel.Audio => OverlayFeatureId.Audio,
            OverlayWorkspacePanel.Devices => OverlayFeatureId.Devices,
            OverlayWorkspacePanel.Options => OverlayFeatureId.Options,
            OverlayWorkspacePanel.Profiles => OverlayFeatureId.Profiles,
            OverlayWorkspacePanel.About => OverlayFeatureId.About,
            _ => default,
        };

        return panel is
            OverlayWorkspacePanel.Home or
            OverlayWorkspacePanel.Display or
            OverlayWorkspacePanel.Audio or
            OverlayWorkspacePanel.Devices or
            OverlayWorkspacePanel.Options or
            OverlayWorkspacePanel.Profiles or
            OverlayWorkspacePanel.About;
    }

    public static bool TryGetWorkspacePanel(
        OverlayFeatureId feature,
        out OverlayWorkspacePanel panel)
    {
        panel = feature switch
        {
            OverlayFeatureId.Home => OverlayWorkspacePanel.Home,
            OverlayFeatureId.Display => OverlayWorkspacePanel.Display,
            OverlayFeatureId.Audio => OverlayWorkspacePanel.Audio,
            OverlayFeatureId.Devices => OverlayWorkspacePanel.Devices,
            OverlayFeatureId.Options => OverlayWorkspacePanel.Options,
            OverlayFeatureId.Profiles => OverlayWorkspacePanel.Profiles,
            OverlayFeatureId.About => OverlayWorkspacePanel.About,
            _ => default,
        };

        return feature is
            OverlayFeatureId.Home or
            OverlayFeatureId.Display or
            OverlayFeatureId.Audio or
            OverlayFeatureId.Devices or
            OverlayFeatureId.Options or
            OverlayFeatureId.Profiles or
            OverlayFeatureId.About;
    }

    public static bool TryFromShortcutNumber(
        ProductConfiguration configuration,
        int number,
        out OverlayFeatureId feature)
    {
        OverlayFeatureDescriptor? descriptor = GetNumberedFeatures(configuration)
            .FirstOrDefault(candidate => candidate.ShortcutNumber == number);
        if (descriptor is not null)
        {
            feature = descriptor.Id;
            return true;
        }

        feature = default;
        return false;
    }

    public static bool TryGetWorkspacePanelFromControlNumber(
        int number,
        bool profilesAvailable,
        out OverlayWorkspacePanel panel)
    {
        ProductConfiguration configuration = profilesAvailable
            ? ProductConfiguration.Paid
            : ProductConfiguration.Free;
        IReadOnlyList<OverlayFeatureDescriptor> features =
            GetNumberedFeatures(configuration);

        if (number >= 1 &&
            number <= features.Count &&
            TryGetWorkspacePanel(features[number - 1].Id, out panel))
        {
            return true;
        }

        panel = OverlayWorkspacePanel.Home;
        return false;
    }

    internal static IReadOnlyList<OverlayFeatureDescriptor> GetAllFeaturesForParity() =>
        AllFeatures;
}

/// <summary>
/// Reports catalog omissions so a feature cannot silently appear in only one
/// of the main window and overlay surfaces.
/// </summary>
internal sealed record FeatureParityIssue(
    OverlayFeatureId Feature,
    string Surface,
    string Message);

internal static class FeatureCatalogParity
{
    public static IReadOnlyList<FeatureParityIssue> Validate(
        ProductConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        List<FeatureParityIssue> issues = [];

        foreach (OverlayFeatureDescriptor descriptor in
                 FeatureDescriptorCatalog.GetAvailableFeatures(configuration))
        {
            if (!descriptor.HasMainWindowSurface)
            {
                issues.Add(new(
                    descriptor.Id,
                    "MainWindow",
                    "The feature is available in the overlay but has no main-window surface."));
            }

            if (!descriptor.HasOverlaySurface)
            {
                issues.Add(new(
                    descriptor.Id,
                    "Overlay",
                    "The feature is available in the main window but has no overlay widget or inspector."));
            }
        }

        return issues;
    }

    public static bool IsComplete(ProductConfiguration configuration) =>
        Validate(configuration).Count == 0;
}
