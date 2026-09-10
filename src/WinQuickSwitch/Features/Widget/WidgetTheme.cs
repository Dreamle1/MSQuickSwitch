using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using WinQuickSwitch.Platform.Windows;

namespace WinQuickSwitch.Features.Widget;

internal static class WidgetTheme
{
    // Popup windows (ComboBox drop-downs, tooltips, and context menus) are
    // hosted in a separate HWND by WPF.  They do not always walk back to the
    // application resource dictionary, especially when a feature view is
    // hosted in a borderless overlay.  Keep a small local palette scope on
    // every surface so those detached controls resolve the same brushes as
    // the window that opened them.
    private static readonly string[] PaletteKeys =
    [
        "PageBrush",
        "CardBrush",
        "BorderBrush",
        "PrimaryTextBrush",
        "SecondaryTextBrush",
        "ControlBrush",
        "ControlHoverBrush",
        "ControlPressedBrush",
        "ControlDisabledBrush",
        "ControlBorderBrush",
        "DisabledTextBrush",
        "SelectionBrush",
        "SelectionTextBrush",
        "DefaultBadgeBrush",
        "CallsBadgeBrush",
        "ScrollTrackBrush",
        "ScrollThumbBrush",
        "OverlayBackdropBrush",
    ];

    private static readonly object ScopeSync = new();
    private static readonly List<WeakReference<FrameworkElement>> Scopes = [];
    private static bool _lastRequestedDarkTheme = true;
    private static bool _isTrackingSystemTheme;

    private static readonly IReadOnlyDictionary<string, Color> DarkPalette =
        new Dictionary<string, Color>
        {
            ["PageBrush"] = Color.FromRgb(0x20, 0x20, 0x20),
            ["CardBrush"] = Color.FromRgb(0x2B, 0x2B, 0x2B),
            ["BorderBrush"] = Color.FromRgb(0x3A, 0x3A, 0x3A),
            ["PrimaryTextBrush"] = Colors.White,
            ["SecondaryTextBrush"] = Color.FromRgb(0xC5, 0xC5, 0xC5),
            ["ControlBrush"] = Color.FromRgb(0x2D, 0x2D, 0x2D),
            ["ControlHoverBrush"] = Color.FromRgb(0x38, 0x38, 0x38),
            ["ControlPressedBrush"] = Color.FromRgb(0x45, 0x45, 0x45),
            ["ControlDisabledBrush"] = Color.FromRgb(0x2B, 0x2B, 0x2B),
            ["ControlBorderBrush"] = Color.FromRgb(0x45, 0x45, 0x45),
            ["DisabledTextBrush"] = Color.FromRgb(0x7A, 0x7A, 0x7A),
            ["SelectionBrush"] = Color.FromRgb(0x00, 0x67, 0xC0),
            ["SelectionTextBrush"] = Colors.White,
            ["DefaultBadgeBrush"] = Color.FromRgb(0x28, 0x5C, 0x3D),
            ["CallsBadgeBrush"] = Color.FromRgb(0x23, 0x4E, 0x78),
            ["ScrollTrackBrush"] = Color.FromRgb(0x20, 0x20, 0x20),
            ["ScrollThumbBrush"] = Color.FromRgb(0x66, 0x66, 0x66),
            ["OverlayBackdropBrush"] = Colors.Black,
        };

    private static readonly IReadOnlyDictionary<string, Color> LightPalette =
        new Dictionary<string, Color>
        {
            ["PageBrush"] = Color.FromRgb(0xF3, 0xF3, 0xF3),
            ["CardBrush"] = Color.FromRgb(0xFB, 0xFB, 0xFB),
            ["BorderBrush"] = Color.FromRgb(0xE5, 0xE5, 0xE5),
            ["PrimaryTextBrush"] = Color.FromRgb(0x1A, 0x1A, 0x1A),
            ["SecondaryTextBrush"] = Color.FromRgb(0x5D, 0x5D, 0x5D),
            ["ControlBrush"] = Colors.White,
            ["ControlHoverBrush"] = Color.FromRgb(0xF5, 0xF5, 0xF5),
            ["ControlPressedBrush"] = Color.FromRgb(0xED, 0xED, 0xED),
            ["ControlDisabledBrush"] = Color.FromRgb(0xF5, 0xF5, 0xF5),
            ["ControlBorderBrush"] = Color.FromRgb(0xD1, 0xD1, 0xD1),
            ["DisabledTextBrush"] = Color.FromRgb(0x91, 0x91, 0x91),
            ["SelectionBrush"] = Color.FromRgb(0x00, 0x5F, 0xB8),
            ["SelectionTextBrush"] = Colors.White,
            ["DefaultBadgeBrush"] = Color.FromRgb(0xCD, 0xEF, 0xD9),
            ["CallsBadgeBrush"] = Color.FromRgb(0xD6, 0xE9, 0xFC),
            ["ScrollTrackBrush"] = Color.FromRgb(0xF3, 0xF3, 0xF3),
            ["ScrollThumbBrush"] = Color.FromRgb(0x8A, 0x8A, 0x8A),
            ["OverlayBackdropBrush"] = Colors.Black,
        };

    public static void Apply(bool useDarkTheme, IntPtr windowHandle = default)
    {
        _lastRequestedDarkTheme = useDarkTheme;
        IReadOnlyDictionary<string, Color> palette = ResolvePalette(
            useDarkTheme,
            IsHighContrastRequested());
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

        RefreshAttachedScopes();

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

    public static void StartSystemThemeTracking()
    {
        if (_isTrackingSystemTheme)
        {
            return;
        }

        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
        _isTrackingSystemTheme = true;
    }

    public static void StopSystemThemeTracking()
    {
        if (!_isTrackingSystemTheme)
        {
            return;
        }

        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        _isTrackingSystemTheme = false;
    }

    internal static IReadOnlyDictionary<string, Color> ResolvePalette(
        bool useDarkTheme,
        bool useHighContrast)
    {
        if (!useHighContrast)
        {
            return useDarkTheme ? DarkPalette : LightPalette;
        }

        return new Dictionary<string, Color>
        {
            ["PageBrush"] = SystemColors.WindowColor,
            ["CardBrush"] = SystemColors.WindowColor,
            ["BorderBrush"] = SystemColors.WindowTextColor,
            ["PrimaryTextBrush"] = SystemColors.WindowTextColor,
            ["SecondaryTextBrush"] = SystemColors.WindowTextColor,
            ["ControlBrush"] = SystemColors.ControlColor,
            ["ControlHoverBrush"] = SystemColors.HighlightColor,
            ["ControlPressedBrush"] = SystemColors.HighlightColor,
            ["ControlDisabledBrush"] = SystemColors.ControlColor,
            ["ControlBorderBrush"] = SystemColors.WindowTextColor,
            ["DisabledTextBrush"] = SystemColors.GrayTextColor,
            ["SelectionBrush"] = SystemColors.HighlightColor,
            ["SelectionTextBrush"] = SystemColors.HighlightTextColor,
            ["DefaultBadgeBrush"] = SystemColors.HighlightColor,
            ["CallsBadgeBrush"] = SystemColors.HighlightColor,
            ["ScrollTrackBrush"] = SystemColors.WindowColor,
            ["ScrollThumbBrush"] = SystemColors.WindowTextColor,
            ["OverlayBackdropBrush"] = SystemColors.WindowColor,
        };
    }

    /// <summary>
    /// Gives a WPF surface a local reference to the application palette.  The
    /// references intentionally point at the same mutable brush instances so
    /// an in-place theme toggle updates already-open views and their popups.
    /// </summary>
    public static void AttachResourceScope(FrameworkElement surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        bool added = false;
        lock (ScopeSync)
        {
            if (!Scopes.Any(scope =>
                    scope.TryGetTarget(out FrameworkElement? target) &&
                    ReferenceEquals(target, surface)))
            {
                Scopes.Add(new WeakReference<FrameworkElement>(surface));
                added = true;
            }
        }

        if (added && surface is Window window)
        {
            window.SourceInitialized += Window_SourceInitialized;
        }

        ApplyScopeResources(surface);
    }

    private static void RefreshAttachedScopes()
    {
        List<FrameworkElement> liveScopes = [];

        lock (ScopeSync)
        {
            for (int index = Scopes.Count - 1; index >= 0; index--)
            {
                if (Scopes[index].TryGetTarget(out FrameworkElement? target))
                {
                    liveScopes.Add(target);
                }
                else
                {
                    Scopes.RemoveAt(index);
                }
            }
        }

        foreach (FrameworkElement scope in liveScopes)
        {
            ApplyScopeResources(scope);
        }
    }

    private static void SystemParameters_StaticPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!string.Equals(
                e.PropertyName,
                nameof(SystemParameters.HighContrast),
                StringComparison.Ordinal))
        {
            return;
        }

        Application? application = Application.Current;
        if (application is null)
        {
            return;
        }

        application.Dispatcher.BeginInvoke(() => Apply(_lastRequestedDarkTheme));
    }

    private static bool IsHighContrastRequested()
    {
#if WINQUICKSWITCH_E2E
        string? markerPath = Environment.GetEnvironmentVariable(
            "WINQUICKSWITCH_E2E_HIGH_CONTRAST_MARKER");
        if (!string.IsNullOrWhiteSpace(markerPath) &&
            System.IO.File.Exists(markerPath))
        {
            return true;
        }
#endif
        return SystemParameters.HighContrast;
    }

    private static void ApplyScopeResources(FrameworkElement surface)
    {
        if (Application.Current is not Application application)
        {
            return;
        }

        ResourceDictionary applicationResources = application.Resources;
        ResourceDictionary localResources = surface.Resources;

        foreach (string key in PaletteKeys)
        {
            if (applicationResources.Contains(key) &&
                applicationResources[key] is object value)
            {
                localResources[key] = value;
            }
        }

        // Make the custom control styles available at the local scope too.
        // This matters for a ComboBoxItem created inside a Popup, whose
        // implicit-style lookup can otherwise fall back to the Windows theme.
        foreach (object key in new object[]
                 {
                     typeof(ComboBox),
                     typeof(ComboBoxItem),
                     typeof(ListBox),
                     typeof(ListBoxItem),
                     typeof(ListView),
                     typeof(ListViewItem),
                     typeof(GridViewColumnHeader),
                     typeof(ScrollBar),
                     typeof(Slider),
                     typeof(CheckBox),
                     typeof(TextBox),
                     typeof(Button),
                     typeof(ToolTip),
                 })
        {
            if (applicationResources.Contains(key) &&
                applicationResources[key] is Style style)
            {
                localResources[key] = style;
            }
        }

        foreach (string key in new[]
                 {
                     "ComboBoxToggleButton",
                     "CompactButton",
                     "ActionButton",
                     "DisplayModeToggle",
                     "WidgetTabToggle",
                     "EndpointBadge",
                     "ScrollBarButton",
                     "ScrollBarThumb",
                     "VerticalScrollBarTemplate",
                     "HorizontalScrollBarTemplate",
                 })
        {
            if (applicationResources.Contains(key) &&
                applicationResources[key] is object value)
            {
                localResources[key] = value;
            }
        }

        if (surface is Window window)
        {
            ApplyNativeWindowTheme(window);
        }
    }

    private static void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            ApplyNativeWindowTheme(window);
        }
    }

    private static void ApplyNativeWindowTheme(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || Application.Current is not Application application)
        {
            return;
        }

        ResourceDictionary resources = application.Resources;
        if (resources["PageBrush"] is not SolidColorBrush page ||
            resources["BorderBrush"] is not SolidColorBrush border ||
            resources["PrimaryTextBrush"] is not SolidColorBrush text)
        {
            return;
        }

        WindowsWindowTheme.Apply(
            handle,
            _lastRequestedDarkTheme && !IsHighContrastRequested(),
            page.Color,
            border.Color,
            text.Color);
    }
}
