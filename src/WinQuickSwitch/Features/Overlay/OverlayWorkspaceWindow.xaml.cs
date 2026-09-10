using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using WinQuickSwitch.Features.Widget;
using WinQuickSwitch.Platform.Windows.Overlay;

namespace WinQuickSwitch.Features.Overlay;

/// <summary>
/// The feature-complete, monitor-scoped overlay workspace. The host owns no
/// display, audio, device, profile, or settings services; those are supplied
/// by callbacks and content from the owning coordinator.
/// </summary>
public partial class OverlayWorkspaceWindow : Window
{
    public const double DefaultInspectorWidth = 720;
    public const double MinimumInspectorWidth = 520;
    public const double MaximumInspectorWidth = 900;
    public const double SnapGrid = 12;
    public const double MinimumWidgetWidth = OverlayWidgetPlacement.MinimumWidth;
    public const double MinimumWidgetHeight = OverlayWidgetPlacement.MinimumHeight;
    private const double CollapsedWidgetHeight = 64;

    private readonly Action? _closeRequested;
    private readonly Action<QuickSwitchCommand>? _commandRequested;
    private readonly Action<string>? _widgetRequested;
    private readonly Action? _inspectorClosed;
    private readonly Action<IReadOnlyList<OverlayWorkspaceWidgetLayout>>? _layoutChanged;
    private readonly Action<double>? _inspectorWidthChanged;
    private readonly OverlayMonitorBounds? _monitorBounds;
    private readonly IntPtr _previousForegroundWindow;
    private double _dimmingOpacity;
    private readonly double _launcherWidth;
    private readonly Dictionary<string, OverlayWorkspaceWidgetLayout> _layouts = [];
    private readonly Dictionary<string, Border> _cards = [];
    private readonly List<ToggleButton> _launcherButtons = [];
    private bool _closeRequestedRaised;
    private bool _isApplyingSelection;
    private bool _isInspectorResizing;
    private bool _isDraggingCard;
    private bool _isResizingCard;
    private bool _layoutInitialized;
    private bool _windowLoaded;
    private bool _layoutMeasurementReady;
    private bool _fitPendingContentRequested;
    private bool _arrangeGeneratedLayoutAfterPendingFit = true;
    private bool _restoreForegroundOnClose;
    private string? _draggingCardId;
    private Point _dragStartPoint;
    private double _dragStartLeft;
    private double _dragStartTop;
    private IReadOnlyList<OverlayWorkspaceWidget>? _pendingWidgets;
    private OverlayWorkspacePanel _currentPanel = OverlayWorkspacePanel.Home;
    private IReadOnlyList<OverlayWorkspaceWidget> _widgets = [];

    internal OverlayWorkspaceWindow(
        Action? closeRequested = null,
        Action<QuickSwitchCommand>? commandRequested = null,
        Action<string>? widgetRequested = null,
        Action? inspectorClosed = null,
        Action<IReadOnlyList<OverlayWorkspaceWidgetLayout>>? layoutChanged = null,
        Action<double>? inspectorWidthChanged = null,
        OverlayMonitorBounds? monitorBounds = null,
        double dimmingOpacity = 0.35,
        double launcherWidth = 300)
    {
        _closeRequested = closeRequested;
        _commandRequested = commandRequested;
        _widgetRequested = widgetRequested;
        _inspectorClosed = inspectorClosed;
        _layoutChanged = layoutChanged;
        _inspectorWidthChanged = inspectorWidthChanged;
        _monitorBounds = monitorBounds;
        _previousForegroundWindow = WindowsForegroundWindow.Capture();
        _dimmingOpacity = Math.Clamp(
            double.IsFinite(dimmingOpacity) ? dimmingOpacity : 0.35,
            0.10,
            0.80);
        _launcherWidth = Math.Clamp(
            double.IsFinite(launcherWidth) ? launcherWidth : 300,
            240,
            420);

        InitializeComponent();
        LauncherColumn.Width = new GridLength(_launcherWidth);
        BackdropLayer.Opacity = _dimmingOpacity;

        WidgetTheme.AttachResourceScope(this);

        RegisterLauncherButtons();
        SelectPanel(OverlayWorkspacePanel.Home, raiseCallback: false);
    }

    public OverlayWorkspacePanel CurrentPanel => _currentPanel;

    public bool IsInspectorOpen =>
        InspectorHost.Visibility == Visibility.Visible;

    public double InspectorWidth => InspectorHost.Width;

    public void SetDimmingOpacity(double opacity)
    {
        _dimmingOpacity = Math.Clamp(
            double.IsFinite(opacity) ? opacity : 0.35,
            0.0,
            0.85);
        BackdropLayer.Opacity = _dimmingOpacity;
    }

    public IReadOnlyList<OverlayWorkspaceWidgetLayout> CaptureLayout() =>
        _layouts.Values
            .OrderBy(layout => layout.ZIndex)
            .Select(layout => layout with { })
            .ToArray();

    /// <summary>
    /// Controls whether the Paid-only Profiles launcher is composed into the
    /// workspace. Free editions should pass false during initialization.
    /// </summary>
    public void SetProfilesAvailable(bool available)
    {
        ProfilesLauncherButton.Visibility = available
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!available && _currentPanel == OverlayWorkspacePanel.Profiles)
        {
            SelectPanel(OverlayWorkspacePanel.Home, raiseCallback: false);
        }
    }

    /// <summary>
    /// Replaces the compact cards shown on the Home workspace. The content of
    /// each card is owned by the caller and can be a reusable feature view.
    /// </summary>
    public void SetWidgets(IEnumerable<OverlayWorkspaceWidget>? widgets)
    {
        IReadOnlyList<OverlayWorkspaceWidget> nextWidgets =
            widgets?.Where(widget => widget is not null).ToArray() ?? [];

        // Refreshes can arrive from the coordinator while WPF is still
        // delivering mouse/Thumb events. Rebuilding the cards at that point
        // detaches the element holding capture and makes the active move or
        // resize appear to be lost. Keep the current visual tree stable until
        // the gesture completes, then apply the newest snapshot once.
        if (_isDraggingCard || _isResizingCard)
        {
            _pendingWidgets = nextWidgets;
            return;
        }

        ApplyWidgets(nextWidgets);
    }

    private void ApplyWidgets(
        IReadOnlyList<OverlayWorkspaceWidget> widgets)
    {
        _widgets = widgets;

        foreach (Border card in _cards.Values)
        {
            card.MouseLeftButtonDown -= WidgetCard_MouseLeftButtonDown;
        }

        _cards.Clear();
        WidgetCanvas.Children.Clear();

        foreach (OverlayWorkspaceWidget widget in _widgets)
        {
            OverlayWorkspaceWidgetLayout layout = GetOrCreateLayout(widget);
            // Refreshes rebuild the content cards, but never rewrite saved
            // geometry.  Width and Height are authoritative even for legacy
            // layouts where HasCustomSize is false.
            _layouts[widget.Id] = NormalizeLayout(layout);
            Border card = CreateWidgetCard(widget);
            _cards[widget.Id] = card;
            WidgetCanvas.Children.Add(card);
            ApplyCardLayout(widget.Id);
        }

        if (_layoutMeasurementReady && FitStaleVisibleWidgets())
        {
            NotifyLayoutChanged();
        }

        ArrangeDefaultLayoutsIfNeeded();
    }

    private void ApplyPendingWidgetsIfIdle()
    {
        if (_isDraggingCard || _isResizingCard || _pendingWidgets is not
            IReadOnlyList<OverlayWorkspaceWidget> pendingWidgets)
        {
            return;
        }

        _pendingWidgets = null;
        ApplyWidgets(pendingWidgets);
        NotifyLayoutChanged();
    }

    /// <summary>
    /// Applies persisted per-monitor card positions before or after widgets
    /// are supplied. Unknown card IDs are ignored by the visual host.
    /// </summary>
    public void RestoreLayout(
        IEnumerable<OverlayWorkspaceWidgetLayout>? layouts)
    {
        _layouts.Clear();

        bool restoredAnyLayout = false;

        foreach (OverlayWorkspaceWidgetLayout layout in layouts ?? [])
        {
            if (!string.IsNullOrWhiteSpace(layout.Id))
            {
                _layouts[layout.Id] = NormalizeLayout(layout);
                restoredAnyLayout = true;
            }
        }

        foreach (OverlayWorkspaceWidget widget in _widgets)
        {
            if (!_layouts.ContainsKey(widget.Id))
            {
                _layouts[widget.Id] = NormalizeLayout(
                    widget.Layout ?? GetOrCreateLayout(widget));
            }
        }

        _layoutInitialized = restoredAnyLayout;
        _arrangeGeneratedLayoutAfterPendingFit = !restoredAnyLayout;

        foreach (string id in _cards.Keys.ToArray())
        {
            if (_layouts.ContainsKey(id))
            {
                ApplyCardLayout(id);
            }
        }

        ArrangeDefaultLayoutsIfNeeded();
    }

    /// <summary>
    /// Selects a launcher item without requiring a panel-specific view.
    /// </summary>
    public void SelectPanel(
        OverlayWorkspacePanel panel,
        bool raiseCallback = true)
    {
        if (panel == OverlayWorkspacePanel.Profiles &&
            ProfilesLauncherButton.Visibility != Visibility.Visible)
        {
            panel = OverlayWorkspacePanel.Home;
        }

        _currentPanel = panel;
        _isApplyingSelection = true;

        try
        {
            foreach (ToggleButton button in _launcherButtons)
            {
                button.IsChecked =
                    button.Tag is string tag &&
                    Enum.TryParse(
                        tag,
                        ignoreCase: true,
                        out OverlayWorkspacePanel buttonPanel) &&
                    buttonPanel == panel;
            }
        }
        finally
        {
            _isApplyingSelection = false;
        }

        if (raiseCallback)
        {
            if (FeatureDescriptorCatalog.TryFromWorkspacePanel(
                    panel,
                    out OverlayFeatureId feature))
            {
                _commandRequested?.Invoke(
                    QuickSwitchCommandCatalog
                        .CreateOverlayNavigation(feature)
                        .CreateCommand());
            }
        }
    }

    /// <summary>
    /// Shows a complete feature inspector inside the workspace. The caller
    /// can place a full reusable UserControl in <paramref name="content"/>.
    /// </summary>
    public void ShowInspector(
        OverlayWorkspacePanel panel,
        string title,
        string description,
        UIElement? content = null,
        string? hint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(description);

        SelectPanel(panel, raiseCallback: false);
        InspectorTitleText.Text = title;
        InspectorDescriptionText.Text = description;
        InspectorHintText.Text = string.IsNullOrWhiteSpace(hint)
            ? "Esc closes this inspector."
            : hint;
        InspectorContentHost.Content = content;
        string widgetId = panel.ToString();
        bool hasWidget = _cards.ContainsKey(widgetId);
        bool widgetVisible = hasWidget &&
            _layouts.TryGetValue(
                widgetId,
                out OverlayWorkspaceWidgetLayout? widgetLayout) &&
            widgetLayout.IsVisible;
        InspectorQuickWidgetButton.Tag = hasWidget ? widgetId : null;
        InspectorQuickWidgetButton.Content = widgetVisible
            ? "Focus quick widget"
            : "Add quick widget";
        InspectorQuickWidgetButton.ToolTip = widgetVisible
            ? "Return to this feature's quick-access widget"
            : "Add this feature as a quick-access widget";
        InspectorQuickWidgetButton.Visibility = hasWidget
            ? Visibility.Visible
            : Visibility.Collapsed;
        InspectorHost.Visibility = Visibility.Visible;
        ClampInspectorWidth();
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() =>
            {
                if (!IsInspectorOpen ||
                    !TryFocusFirstDescendant(InspectorContentHost))
                {
                    InspectorCloseButton.Focus();
                }
            }));
    }

    public void HideInspector()
    {
        InspectorContentHost.Content = null;
        InspectorHost.Visibility = Visibility.Collapsed;
        InspectorQuickWidgetButton.Tag = null;
        InspectorQuickWidgetButton.Visibility = Visibility.Collapsed;
        _inspectorClosed?.Invoke();
        FocusLauncher();
    }

    public void SetFeedback(
        string? message,
        OverlayWorkspaceFeedbackSeverity severity =
            OverlayWorkspaceFeedbackSeverity.Information)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            FeedbackBorder.Visibility = Visibility.Collapsed;
            FeedbackText.Text = string.Empty;
            return;
        }

        FeedbackText.Text = message.Trim();
        FeedbackBorder.Visibility = Visibility.Visible;
        string borderResource = severity switch
        {
            OverlayWorkspaceFeedbackSeverity.Success => "SelectionBrush",
            OverlayWorkspaceFeedbackSeverity.Warning => "CallsBadgeBrush",
            OverlayWorkspaceFeedbackSeverity.Error => "ControlPressedBrush",
            _ => "BorderBrush",
        };
        FeedbackBorder.SetResourceReference(
            Border.BorderBrushProperty,
            borderResource);
    }

    public void FocusLauncher() =>
        (_launcherButtons.FirstOrDefault(button =>
            button.IsChecked == true) ?? _launcherButtons.FirstOrDefault())
        ?.Focus();

    /// <summary>
    /// Closes the workspace as a user dismissal and returns focus to the
    /// application that was active before the overlay appeared.
    /// </summary>
    public void Dismiss() => RequestClose();

    /// <summary>
    /// Restores a card that was hidden, brings it to the front, and gives it
    /// keyboard focus.  The owning coordinator remains responsible for
    /// refreshing the card's state; this method only changes workspace
    /// presentation.
    /// </summary>
    public bool FocusWidget(string widgetId)
    {
        if (string.IsNullOrWhiteSpace(widgetId) ||
            !_cards.TryGetValue(widgetId, out Border? card) ||
            !_layouts.TryGetValue(widgetId, out OverlayWorkspaceWidgetLayout? layout))
        {
            return false;
        }

        int nextZIndex = _layouts.Count == 0
            ? 1
            : _layouts.Values.Max(value => value.ZIndex) + 1;
        bool firstReveal = !layout.IsVisible &&
            !layout.HasCustomSize &&
            layout.ContentLayoutVersion <
                OverlayWidgetPlacement.CurrentContentLayoutVersion;
        if (firstReveal)
        {
            layout = CreateCascadedRevealLayout(widgetId, layout);
            _layouts[widgetId] = layout;
            FitWidgetToContent(widgetId, notify: false);
            layout = _layouts[widgetId];
        }

        OverlayWorkspaceWidgetLayout revealed = layout with
        {
            IsVisible = true,
            ZIndex = nextZIndex,
        };

        _layouts[widgetId] = revealed;

        ApplyCardLayout(widgetId);
        TryFocusFirstDescendant(card);
        NotifyLayoutChanged();
        return true;
    }

    /// <summary>
    /// Restores the persisted inspector width.  It is intentionally separate
    /// from the constructor so a monitor-specific layout can be loaded after
    /// WPF has measured the workspace surface.
    /// </summary>
    public void SetInspectorWidth(double width)
    {
        InspectorHost.Width = double.IsFinite(width)
            ? width
            : DefaultInspectorWidth;
        ClampInspectorWidth();
    }

    public void FitPendingContent()
    {
        _fitPendingContentRequested = true;
        if (!_layoutMeasurementReady)
        {
            return;
        }

        UpdateLayout();
        bool fitted = FitVisibleNonCustomWidgets();
        if (fitted && _arrangeGeneratedLayoutAfterPendingFit)
        {
            ArrangeVisibleLayouts();
            _layoutInitialized = true;
            _arrangeGeneratedLayoutAfterPendingFit = false;
        }
        if (fitted)
        {
            NotifyLayoutChanged();
        }
        _fitPendingContentRequested = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyMonitorBounds();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (InspectorContentHost.Content is not null)
        {
            InspectorContentHost.Content = null;
            _inspectorClosed?.Invoke();
        }

        NotifyLayoutChanged();
        base.OnClosed(e);

        if (_restoreForegroundOnClose)
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() =>
                    WindowsForegroundWindow.TryRestore(
                        _previousForegroundWindow)));
        }
    }

    private void RegisterLauncherButtons()
    {
        _launcherButtons.Clear();
        _launcherButtons.AddRange(
            LauncherNavigation.Children.OfType<ToggleButton>());
    }

    private void ApplyMonitorBounds()
    {
        OverlayMonitorBounds bounds;

        try
        {
            bounds = _monitorBounds ??
                WindowsOverlayMonitor.GetPointerMonitorBounds();
        }
        catch (Win32Exception)
        {
            bounds = WindowsOverlayMonitor.GetFallbackMonitorInfo().Bounds;
        }

        PresentationSource? source = PresentationSource.FromVisual(this);
        Matrix fromDevice =
            source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        Point dipOrigin = fromDevice.Transform(
            new Point(bounds.Left, bounds.Top));
        Vector dipSize = fromDevice.Transform(
            new Vector(bounds.Width, bounds.Height));

        WindowState = WindowState.Normal;
        Left = dipOrigin.X;
        Top = dipOrigin.Y;
        Width = Math.Max(1, dipSize.X);
        Height = Math.Max(1, dipSize.Y);
    }

    private Border CreateWidgetCard(OverlayWorkspaceWidget widget)
    {
        Border card = new()
        {
            Tag = widget.Id,
            // Descriptor minima are enforced by the geometry controller while
            // the monitor has enough room.  The visual itself must still be
            // allowed to shrink when the active monitor is narrower; otherwise
            // WPF renders the card past the right edge even though its saved
            // bounds were correctly clamped to the workspace.
            MinWidth = MinimumWidgetWidth,
            MinHeight = CollapsedWidgetHeight,
            Padding = new Thickness(14),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Focusable = false,
            ToolTip = widget.CanResize
                ? $"{widget.Title}. Drag the header to move or resize the corner."
                : $"{widget.Title}. Drag the header to move.",
        };
        // Keep runtime-created cards bound to the shared palette.  A theme
        // toggle can replace frozen application brushes, so storing the
        // current brush instance here would leave an already-open card in the
        // old theme.
        card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        AutomationProperties.SetName(card, $"{widget.Title} quick controls");
        AutomationProperties.SetHelpText(
            card,
            widget.CanResize
                ? "Drag the header to move or resize the corner."
                : "Drag the header to move.");

        Grid cardGrid = new();
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Border dragHandle = new()
        {
            Tag = widget.Id,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            Padding = new Thickness(0, 0, 0, 7),
            Focusable = false,
            ToolTip = "Drag to move this widget",
        };
        dragHandle.Child = new TextBlock
        {
            Text = widget.Title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        Grid header = new();
        header.Tag = widget.Id;
        header.Background = Brushes.Transparent;
        header.Cursor = Cursors.SizeAll;
        header.ToolTip = "Drag anywhere in the header to move this widget";
        header.MouseLeftButtonDown += DragHandle_MouseLeftButtonDown;
        header.MouseMove += DragHandle_MouseMove;
        header.MouseLeftButtonUp += DragHandle_MouseLeftButtonUp;
        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });
        Grid.SetColumn(dragHandle, 0);
        header.Children.Add(dragHandle);

        StackPanel headerActions = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (widget.Panel is OverlayWorkspacePanel.Display or
            OverlayWorkspacePanel.Audio or OverlayWorkspacePanel.Devices)
        {
            Button refreshButton = CreateActionButton(
                widget,
                new OverlayWorkspaceAction(OverlayWorkspaceActionKind.Refresh, "Refresh"),
                horizontal: false);
            refreshButton.Content = "⟳";
            refreshButton.Width = 40;
            refreshButton.MinWidth = 40;
            refreshButton.Padding = new Thickness(0);
            refreshButton.FontSize = 18;
            refreshButton.ToolTip = $"Refresh {widget.Title}";
            AutomationProperties.SetName(refreshButton, $"Refresh {widget.Title}");
            headerActions.Children.Add(refreshButton);
        }
        if (widget.CanCollapse)
        {
            Button collapseButton = CreateActionButton(
                widget,
                new OverlayWorkspaceAction(
                    OverlayWorkspaceActionKind.Collapse,
                    "Minimize"),
                horizontal: false);
            collapseButton.Content = "—";
            collapseButton.Width = 40;
            collapseButton.MinWidth = 40;
            collapseButton.Padding = new Thickness(0);
            collapseButton.FontSize = 18;
            collapseButton.ToolTip = $"Minimize the {widget.Title} widget";
            AutomationProperties.SetName(
                collapseButton,
                $"Minimize {widget.Title} widget");
            headerActions.Children.Add(collapseButton);
        }
        if (widget.CanResize)
        {
            Button resetSizeButton = CreateActionButton(
                widget,
                new OverlayWorkspaceAction(
                    OverlayWorkspaceActionKind.FitContent,
                    "Fit content"),
                horizontal: false);
            resetSizeButton.Content = "↺";
            resetSizeButton.Width = 40;
            resetSizeButton.MinWidth = 40;
            resetSizeButton.Padding = new Thickness(0);
            resetSizeButton.FontSize = 18;
            resetSizeButton.ToolTip =
                $"Fit the {widget.Title} widget to its current content, within this monitor";
            AutomationProperties.SetName(
                resetSizeButton,
                $"Fit {widget.Title} widget to content");
            headerActions.Children.Add(resetSizeButton);
        }
        Button removeButton = CreateActionButton(
            widget,
            new OverlayWorkspaceAction(
                OverlayWorkspaceActionKind.Remove,
                "Remove"),
            horizontal: false);
        removeButton.Content = "×";
        removeButton.Width = 40;
        removeButton.MinWidth = 40;
        removeButton.Padding = new Thickness(0);
        removeButton.FontSize = 20;
        removeButton.ToolTip =
            "Remove from the workspace. Restore it from the right navigation.";
        AutomationProperties.SetName(
            removeButton,
            $"Remove {widget.Title} quick widget");
        headerActions.Children.Add(removeButton);
        Grid.SetColumn(headerActions, 1);
        header.Children.Add(headerActions);
        cardGrid.Children.Add(header);

        StackPanel body = new();
        if (!string.IsNullOrWhiteSpace(widget.Summary))
        {
            TextBlock summary = new()
            {
                Text = widget.Summary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 9),
            };
            summary.SetResourceReference(
                TextBlock.ForegroundProperty,
                "SecondaryTextBrush");
            body.Children.Add(summary);
        }
        if (widget.Actions is { Count: > 0 } actions)
        {
            WrapPanel actionPanel = new()
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8),
            };
            foreach (OverlayWorkspaceAction action in actions)
            {
                actionPanel.Children.Add(CreateActionButton(
                    widget,
                    action,
                    horizontal: true));
            }

            body.Children.Add(actionPanel);
        }

        if (widget.Content is UIElement content)
        {
            body.Children.Add(content);
        }

        if (body.Children.Count > 0)
        {
            ScrollViewer bodyHost = new()
            {
                Content = body,
                // Reserve the lower-right corner for the resize thumb so the
                // last action or list row remains clickable and readable.
                Margin = widget.CanResize
                    ? new Thickness(0, 0, 18, 18)
                    : new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            Grid.SetRow(bodyHost, 1);
            cardGrid.Children.Add(bodyHost);
        }
        else
        {
            TextBlock readyText = new()
            {
                Text = "Open this feature from the launcher for complete controls.",
                TextWrapping = TextWrapping.Wrap,
            };
            readyText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            Grid.SetRow(readyText, 1);
            cardGrid.Children.Add(readyText);
        }

        Grid cardContainer = new();
        cardContainer.Children.Add(cardGrid);
        if (widget.CanResize)
        {
            Thumb resizeGrip = new()
            {
                Width = 40,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNWSE,
                Background = Brushes.Transparent,
                Template = (ControlTemplate)FindResource(
                    "OverlayWidgetResizeGripTemplate"),
                ToolTip = "Resize widget",
                Tag = widget.Id,
            };
            resizeGrip.DragStarted += ResizeGrip_DragStarted;
            resizeGrip.DragDelta += ResizeGrip_DragDelta;
            resizeGrip.DragCompleted += ResizeGrip_DragCompleted;
            AutomationProperties.SetName(
                resizeGrip,
                $"Resize {widget.Title} widget");
            cardContainer.Children.Add(resizeGrip);
        }

        card.Child = cardContainer;
        card.MouseLeftButtonDown += WidgetCard_MouseLeftButtonDown;
        return card;
    }

    private Button CreateActionButton(
        OverlayWorkspaceWidget widget,
        OverlayWorkspaceAction action,
        bool horizontal)
    {
        Button button = new()
        {
            Content = action.Label,
            Tag = new OverlayActionTarget(widget.Id, action.Kind),
            IsEnabled = action.IsEnabled,
            MinHeight = 40,
            Margin = horizontal
                ? new Thickness(0, 0, 6, 6)
                : new Thickness(0, 0, 6, 0),
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalContentAlignment = horizontal
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Center,
            Style = (Style)FindResource("CompactButton"),
            ToolTip = action.Label,
        };
        button.Click += ActionButton_Click;
        AutomationProperties.SetName(
            button,
            $"{action.Label} for {widget.Title}");
        return button;
    }

    private OverlayWorkspaceWidgetLayout GetOrCreateLayout(
        OverlayWorkspaceWidget widget)
    {
        if (_layouts.TryGetValue(widget.Id, out OverlayWorkspaceWidgetLayout? existing))
        {
            return existing;
        }

        return widget.Layout ?? new OverlayWorkspaceWidgetLayout(
            widget.Id,
            24,
            24,
            360,
            240,
            widget.IsCollapsed,
            _layouts.Count);
    }

    private void ArrangeDefaultLayoutsIfNeeded()
    {
        // The monitor bounds are applied during SourceInitialized and WPF can
        // raise an early SizeChanged while the canvas still has its transient
        // pre-show width.  Do not lock in a compact/default grid from that
        // measurement; the Loaded pass below uses the real monitor work area.
        if (!_windowLoaded ||
            !_layoutMeasurementReady ||
            WidgetCanvas.ActualWidth <= 0 ||
            WidgetCanvas.ActualHeight <= 0)
        {
            return;
        }

        if (!_layoutInitialized)
        {
            ArrangeVisibleLayouts();
            _layoutInitialized = true;
            return;
        }

        foreach (string id in _layouts.Keys.ToArray())
        {
            _layouts[id] = NormalizeLayout(_layouts[id]);
        }

        foreach (string id in _cards.Keys)
        {
            ApplyCardLayout(id);
        }
    }

    private OverlayWorkspaceWidgetLayout NormalizeLayout(
        OverlayWorkspaceWidgetLayout layout)
    {
        (double minimumWidth, double minimumHeight) =
            GetWidgetMinimumSize(layout.Id);
        double width = double.IsFinite(layout.Width)
            ? Math.Max(minimumWidth, layout.Width)
            : 360;
        double height = double.IsFinite(layout.Height)
            ? Math.Max(minimumHeight, layout.Height)
            : 240;
        double canvasWidth = WidgetCanvas.ActualWidth;
        double canvasHeight = WidgetCanvas.ActualHeight;
        double left = double.IsFinite(layout.Left) ? layout.Left : 0;
        double top = double.IsFinite(layout.Top) ? layout.Top : 0;

        // SetWidgets and RestoreLayout run before Show(), when WPF has not yet
        // measured the canvas. Preserve positions until the first real size is
        // available instead of collapsing every card to (0,0).
        if (!_layoutMeasurementReady || canvasWidth <= 0 || canvasHeight <= 0)
        {
            return layout with
            {
                Left = left,
                Top = top,
                Width = width,
                Height = height,
            };
        }

        width = Math.Min(width, canvasWidth);
        height = Math.Min(height, canvasHeight);
        double maxLeft = Math.Max(0, canvasWidth - width);
        double maxTop = Math.Max(0, canvasHeight - height);
        return layout with
        {
            Left = Math.Clamp(left, 0, maxLeft),
            Top = Math.Clamp(top, 0, maxTop),
            Width = width,
            Height = height,
        };
    }

    private void ArrangeVisibleLayouts()
    {
        if (WidgetCanvas.ActualWidth <= 0 || WidgetCanvas.ActualHeight <= 0)
        {
            return;
        }

        OverlayWorkspaceWidget[] visibleWidgets = _widgets
            .Where(widget =>
                _layouts.TryGetValue(
                    widget.Id,
                    out OverlayWorkspaceWidgetLayout? layout) &&
                layout.IsVisible)
            .ToArray();

        if (visibleWidgets.Length == 0)
        {
            return;
        }

        // Fit/minimum dimensions are authoritative. Equal grid cells followed
        // by NormalizeLayout used to expand Devices again and clamp it over
        // Display. Pack those actual dimensions, without resizing any card.
        IReadOnlyList<OverlayWorkspaceWidgetLayout> arranged =
            OverlayInitialLayoutArranger.Arrange(
                visibleWidgets.Select(widget => NormalizeLayout(_layouts[widget.Id])).ToArray(),
                WidgetCanvas.ActualWidth,
                WidgetCanvas.ActualHeight,
                SnapGrid);
        foreach (OverlayWorkspaceWidgetLayout layout in arranged)
        {
            _layouts[layout.Id] = layout;
            ApplyCardLayout(layout.Id);
        }
        // Hidden cards retain their last dimensions and dormant positions.
    }

    private void ApplyCardLayout(string id)
    {
        if (!_cards.TryGetValue(id, out Border? card) ||
            !_layouts.TryGetValue(id, out OverlayWorkspaceWidgetLayout? layout))
        {
            return;
        }

        card.Width = layout.Width;
        card.Height = layout.IsCollapsed
            ? CollapsedWidgetHeight
            : layout.Height;
        bool awaitingInitialFit = layout.IsVisible &&
            !layout.HasCustomSize &&
            layout.ContentLayoutVersion <
                OverlayWidgetPlacement.CurrentContentLayoutVersion;
        card.Visibility = layout.IsVisible
            ? awaitingInitialFit
                ? Visibility.Hidden
                : Visibility.Visible
            : Visibility.Collapsed;
        Canvas.SetLeft(card, layout.Left);
        Canvas.SetTop(card, layout.Top);
        Canvas.SetZIndex(card, layout.ZIndex);
        ApplyCollapsedVisualState(id, card, layout);
    }

    private void WidgetSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        foreach (string id in _cards.Keys.ToArray())
        {
            if (_layouts.TryGetValue(id, out OverlayWorkspaceWidgetLayout? layout))
            {
                _layouts[id] = NormalizeLayout(layout);
                ApplyCardLayout(id);
            }
        }

        ClampInspectorWidth();
        ArrangeDefaultLayoutsIfNeeded();
    }

    private void WidgetCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string id } card ||
            IsInsideActionControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        BringWidgetToFront(id, card);
        TryFocusFirstDescendant(card);

        e.Handled = true;
    }

    private void BringWidgetToFront(string id, Border card)
    {
        int nextZIndex = _layouts.Count == 0
            ? 1
            : _layouts.Values.Max(value => value.ZIndex) + 1;
        Canvas.SetZIndex(card, nextZIndex);
        if (_layouts.TryGetValue(id, out OverlayWorkspaceWidgetLayout? layout))
        {
            _layouts[id] = layout with { ZIndex = nextZIndex };
        }
    }

    private static bool IsInsideActionControl(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ButtonBase or Thumb)
            {
                return true;
            }

            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current),
                ContentElement contentElement =>
                    ContentOperations.GetParent(contentElement),
                _ => LogicalTreeHelper.GetParent(current),
            };
        }

        return false;
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } ||
            !_cards.TryGetValue(id, out Border? card))
        {
            return;
        }

        if (IsInsideActionControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        BringWidgetToFront(id, card);
        TryFocusFirstDescendant(card);
        _arrangeGeneratedLayoutAfterPendingFit = false;

        _draggingCardId = id;
        _isDraggingCard = true;
        _dragStartPoint = e.GetPosition(WidgetCanvas);
        _dragStartLeft = Canvas.GetLeft(card);
        _dragStartTop = Canvas.GetTop(card);
        if (!double.IsFinite(_dragStartLeft)) _dragStartLeft = 0;
        if (!double.IsFinite(_dragStartTop)) _dragStartTop = 0;
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingCard ||
            _draggingCardId is not string id ||
            e.LeftButton != MouseButtonState.Pressed ||
            !_cards.TryGetValue(id, out Border? card))
        {
            return;
        }

        Point current = e.GetPosition(WidgetCanvas);
        double left = _dragStartLeft + current.X - _dragStartPoint.X;
        double top = _dragStartTop + current.Y - _dragStartPoint.Y;
        if (_layouts.TryGetValue(id, out OverlayWorkspaceWidgetLayout? layout))
        {
            _layouts[id] = NormalizeLayout(layout with { Left = left, Top = top });
            ApplyCardLayout(id);
        }
        e.Handled = true;
    }

    private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingCard)
        {
            return;
        }

        _isDraggingCard = false;
        _draggingCardId = null;
        Mouse.Capture(null);

        NotifyLayoutChanged();
        ApplyPendingWidgetsIfIdle();
        e.Handled = true;
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: string id } ||
            !_layouts.TryGetValue(id, out OverlayWorkspaceWidgetLayout? layout))
        {
            return;
        }

        (double minimumWidth, double minimumHeight) =
            GetWidgetMinimumSize(id);
        OverlayWidgetType widgetType = Enum.TryParse(
            id,
            ignoreCase: true,
            out OverlayWidgetType parsed)
                ? parsed
                : OverlayWidgetType.Home;
        OverlayWidgetPlacement resized =
            OverlayWorkspaceLayoutCalculator.ResizeFromBottomRight(
                new OverlayWidgetPlacement(
                    widgetType,
                    layout.Left,
                    layout.Top,
                    layout.Width,
                    layout.Height),
                e.HorizontalChange,
                e.VerticalChange,
                new OverlayWorkArea(
                    0,
                    0,
                    Math.Max(1, WidgetCanvas.ActualWidth),
                    Math.Max(1, WidgetCanvas.ActualHeight)),
                minimumWidth,
                minimumHeight);
        _layouts[id] = NormalizeLayout(layout with
        {
            Width = resized.Width,
            Height = resized.Height,
            HasCustomSize = true,
            ContentLayoutVersion =
                OverlayWidgetPlacement.CurrentContentLayoutVersion,
        });
        ApplyCardLayout(id);
    }

    private void ResizeGrip_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isResizingCard = true;
        _arrangeGeneratedLayoutAfterPendingFit = false;
        e.Handled = true;
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isResizingCard = false;
        NotifyLayoutChanged();
        ApplyPendingWidgetsIfIdle();
    }

    private void ResetWidgetToPreset(string widgetId)
    {
        FitWidgetToContent(widgetId, notify: true);
    }

    private void FitWidgetToContent(string widgetId, bool notify)
    {
        OverlayWorkspaceWidget? widget = _widgets.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Id,
                widgetId,
                StringComparison.OrdinalIgnoreCase));
        if (widget is null ||
            !_layouts.TryGetValue(
                widgetId,
                out OverlayWorkspaceWidgetLayout? current))
        {
            return;
        }

        Size measured = MeasureWidgetContentSize(widgetId, current);
        OverlayWorkspaceWidgetLayout preset = widget.Layout ?? current;
        double width = measured.Width > 0
            ? measured.Width
            : preset.Width;
        double height = measured.Height > 0
            ? measured.Height
            : preset.Height;
        OverlayWorkspaceWidgetLayout requested = NormalizeLayout(current with
        {
            Width = width,
            Height = height,
            HasCustomSize = false,
            ContentLayoutVersion =
                OverlayWidgetPlacement.CurrentContentLayoutVersion,
        });
        _layouts[widgetId] = requested;

        ApplyCardLayout(widgetId);
        if (notify)
        {
            NotifyLayoutChanged();
        }
    }

    private OverlayWorkspaceWidgetLayout CreateCascadedRevealLayout(
        string widgetId,
        OverlayWorkspaceWidgetLayout layout)
    {
        OverlayWorkspaceWidgetLayout? frontMost = _layouts.Values
            .Where(candidate =>
                candidate.IsVisible &&
                !string.Equals(
                    candidate.Id,
                    widgetId,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.ZIndex)
            .FirstOrDefault();
        if (frontMost is null)
        {
            return NormalizeLayout(layout);
        }

        return NormalizeLayout(layout with
        {
            Left = frontMost.Left +
                OverlayWorkspaceLayoutCalculator.CascadedRevealOffset,
            Top = frontMost.Top +
                OverlayWorkspaceLayoutCalculator.CascadedRevealOffset,
        });
    }

    /// <summary>
    /// Measures a freshly-rendered card at its natural size, then remeasures
    /// its body at the bounded width so wrapped action rows produce the
    /// correct height.  A card may grow beyond the old grid cell; only the
    /// usable monitor surface and persisted placement limits constrain it.
    /// </summary>
    private Size MeasureWidgetContentSize(
        string widgetId,
        OverlayWorkspaceWidgetLayout fallback)
    {
        if (!_cards.TryGetValue(widgetId, out Border? card) ||
            WidgetCanvas.ActualWidth <= 0 ||
            WidgetCanvas.ActualHeight <= 0)
        {
            return new(fallback.Width, fallback.Height);
        }

        double oldWidth = card.Width;
        double oldHeight = card.Height;
        OverlayWorkspaceWidgetLayout preset = _widgets
            .FirstOrDefault(widget => string.Equals(
                widget.Id,
                widgetId,
                StringComparison.OrdinalIgnoreCase))
            ?.Layout ?? fallback;

        try
        {
            card.Width = double.NaN;
            card.Height = double.NaN;

            // Measure the body itself, outside the ScrollViewer's current
            // viewport. Measuring the whole card can return the already
            // constrained viewport height, which made Fit content appear to
            // do nothing. Measuring each grid row at the bounded width gives
            // the complete wrapped action and inventory height.
            (double fitWidth, _) =
                OverlayWorkspaceLayoutCalculator.ClampContentFitSize(
                    preset.Width,
                    preset.Height,
                    WidgetCanvas.ActualWidth,
                    WidgetCanvas.ActualHeight);
            double width = fitWidth;
            double contentHeight = MeasureCompleteCardHeight(card, width);
            (_, double fitHeight) =
                OverlayWorkspaceLayoutCalculator.ClampContentFitSize(
                    width,
                    contentHeight > 0 ? contentHeight : preset.Height,
                    WidgetCanvas.ActualWidth,
                    WidgetCanvas.ActualHeight);
            double height = fitHeight;

            return new(width, height);
        }
        finally
        {
            card.Width = oldWidth;
            card.Height = oldHeight;
            card.Measure(new Size(
                double.IsFinite(oldWidth)
                    ? oldWidth
                    : Math.Max(MinimumWidgetWidth, fallback.Width),
                double.IsFinite(oldHeight)
                    ? oldHeight
                    : Math.Max(MinimumWidgetHeight, fallback.Height)));
        }
    }

    internal static double MeasureCompleteCardHeight(Border card, double width)
    {
        if (card.Child is not Grid cardContainer ||
            cardContainer.Children
                .OfType<Grid>()
                .FirstOrDefault() is not Grid cardGrid)
        {
            return card.DesiredSize.Height;
        }

        double horizontalChrome =
            card.Padding.Left + card.Padding.Right +
            card.BorderThickness.Left + card.BorderThickness.Right;
        double verticalChrome =
            card.Padding.Top + card.Padding.Bottom +
            card.BorderThickness.Top + card.BorderThickness.Bottom;
        double innerWidth = Math.Max(1, width - horizontalChrome);
        double measuredHeight = verticalChrome;

        foreach (FrameworkElement rowElement in cardGrid.Children
                     .OfType<FrameworkElement>()
                     .OrderBy(Grid.GetRow))
        {
            if (rowElement is ScrollViewer { Content: FrameworkElement completeBody } scroll)
            {
                double bodyWidth = Math.Max(1, innerWidth -
                    scroll.Margin.Left - scroll.Margin.Right -
                    scroll.Padding.Left - scroll.Padding.Right -
                    scroll.BorderThickness.Left - scroll.BorderThickness.Right);
                completeBody.Measure(new Size(bodyWidth, double.PositiveInfinity));
                measuredHeight += completeBody.DesiredSize.Height +
                    scroll.Margin.Top + scroll.Margin.Bottom +
                    scroll.Padding.Top + scroll.Padding.Bottom +
                    scroll.BorderThickness.Top + scroll.BorderThickness.Bottom;
            }
            else
            {
                rowElement.Measure(new Size(innerWidth, double.PositiveInfinity));
                measuredHeight += rowElement.DesiredSize.Height;
            }
        }

        return measuredHeight;
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button
            {
                Tag: OverlayActionTarget target,
            })
        {
            return;
        }

        e.Handled = true;
        if (target.Kind == OverlayWorkspaceActionKind.Refresh)
        {
            OverlayWorkspaceWidget? widget = _widgets.FirstOrDefault(
                candidate => candidate.Id == target.WidgetId);
            OverlayFeatureId? feature = widget?.Panel switch
            {
                OverlayWorkspacePanel.Display => OverlayFeatureId.Display,
                OverlayWorkspacePanel.Audio => OverlayFeatureId.Audio,
                OverlayWorkspacePanel.Devices => OverlayFeatureId.Devices,
                _ => null,
            };
            if (feature is OverlayFeatureId refreshFeature)
            {
                _commandRequested?.Invoke(
                    QuickSwitchCommandCatalog.CreateOverlayRefresh(refreshFeature).CreateCommand());
            }
            return;
        }
        if (target.Kind == OverlayWorkspaceActionKind.FitContent)
        {
            ResetWidgetToPreset(target.WidgetId);
            return;
        }

        if (target.Kind == OverlayWorkspaceActionKind.Collapse)
        {
            if (_layouts.TryGetValue(
                    target.WidgetId,
                    out OverlayWorkspaceWidgetLayout? layout))
            {
                _layouts[target.WidgetId] = layout with
                {
                    IsCollapsed = !layout.IsCollapsed,
                };
                ApplyCardLayout(target.WidgetId);
                NotifyLayoutChanged();
            }
            return;
        }

        if (target.Kind == OverlayWorkspaceActionKind.Remove)
        {
            if (_layouts.TryGetValue(target.WidgetId, out OverlayWorkspaceWidgetLayout? layout))
            {
                _layouts[target.WidgetId] = layout with { IsVisible = false };
                ApplyCardLayout(target.WidgetId);
                NotifyLayoutChanged();
            }
            _widgetRequested?.Invoke(target.WidgetId);
            return;
        }

    }

    private void LauncherButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSelection ||
            sender is not ToggleButton { Tag: string panelName } ||
            !Enum.TryParse(
                panelName,
                ignoreCase: true,
                out OverlayWorkspacePanel panel))
        {
            return;
        }

        SelectPanel(panel);
        e.Handled = true;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _windowLoaded = true;
        SelectPanel(OverlayWorkspacePanel.Home, raiseCallback: false);
        FocusLauncher();

        // Persist only after WPF has measured the monitor-scoped workspace.
        // This records a new monitor's defaults without letting an early
        // transient SizeChanged event overwrite a saved layout.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(CompleteInitialLayout));
    }

    private void CompleteInitialLayout()
    {
        if (!IsVisible)
        {
            return;
        }

        if (WidgetCanvas.ActualWidth <= 0 || WidgetCanvas.ActualHeight <= 0)
        {
            // A borderless monitor-sized window can receive Loaded before its
            // final measure pass. Try again after Render rather than saving a
            // zero/temporary canvas layout.
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Render,
                new Action(CompleteInitialLayout));
            return;
        }

        _layoutMeasurementReady = true;
        // A first-run card must never remain Hidden merely because the shared
        // state refresh and the monitor's first WPF measure completed in the
        // opposite order. Fit once from the available view so it can render;
        // FitPendingContent performs the authoritative fit again when the
        // initial Ready/Empty/Error state arrives.
        FitStaleVisibleWidgets();
        if (_fitPendingContentRequested)
        {
            UpdateLayout();
            FitVisibleNonCustomWidgets();
            _fitPendingContentRequested = false;
            _arrangeGeneratedLayoutAfterPendingFit = false;
        }
        // Arrange the first-run grid only after content-fit dimensions are
        // authoritative so larger shared views cannot overlap the next row.
        ArrangeDefaultLayoutsIfNeeded();
        NotifyLayoutChanged();
    }

    private bool FitStaleVisibleWidgets()
    {
        bool fittedAny = false;
        foreach (OverlayWorkspaceWidget widget in _widgets)
        {
            if (!_layouts.TryGetValue(
                    widget.Id,
                    out OverlayWorkspaceWidgetLayout? layout) ||
                !layout.IsVisible ||
                layout.HasCustomSize ||
                layout.ContentLayoutVersion >=
                    OverlayWidgetPlacement.CurrentContentLayoutVersion)
            {
                continue;
            }

            FitWidgetToContent(widget.Id, notify: false);
            fittedAny = true;
        }

        return fittedAny;
    }

    private bool FitVisibleNonCustomWidgets()
    {
        bool fittedAny = false;
        foreach (OverlayWorkspaceWidget widget in _widgets)
        {
            if (!_layouts.TryGetValue(
                    widget.Id,
                    out OverlayWorkspaceWidgetLayout? layout) ||
                !layout.IsVisible ||
                layout.HasCustomSize)
            {
                continue;
            }

            FitWidgetToContent(widget.Id, notify: false);
            fittedAny = true;
        }

        return fittedAny;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateLauncherLayout(ActualWidth);

    internal void UpdateLauncherLayout(double windowWidth)
    {
        bool compact = windowWidth > 0 && windowWidth < 1000;
        LauncherColumn.Width = new GridLength(compact ? 76 : _launcherWidth);
        LauncherPanel.Padding = new Thickness(compact ? 6 : 18);
        OpenMainLauncherButton.Content = compact ? "▣" : "Open main window";
        ExitLauncherButton.Content = compact ? "×" : "Exit WinQuickSwitch";
        OpenMainLauncherButton.Padding = new Thickness(compact ? 0 : 7);
        ExitLauncherButton.Padding = new Thickness(compact ? 0 : 7);
        LauncherShortcutHint.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

        foreach (ToggleButton button in _launcherButtons)
        {
            button.HorizontalContentAlignment = compact
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
            if (button.Tag is string tag)
            {
                button.Content = compact
                    ? GetPanelGlyph(tag)
                    : GetPanelLabel(tag);
            }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (IsInspectorOpen)
            {
                HideInspector();
            }
            else
            {
                RequestClose();
            }
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            e.Key is Key.D0 or Key.NumPad0)
        {
            e.Handled = true;
            SelectPanel(OverlayWorkspacePanel.Home);
            FocusLauncher();
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            TryGetNumberedPanel(e.Key, out OverlayWorkspacePanel numberedPanel))
        {
            e.Handled = true;
            SelectPanel(numberedPanel);
            FocusWidget(numberedPanel.ToString());
            return;
        }

        if (Keyboard.FocusedElement is ToggleButton focusedButton &&
            _launcherButtons.Contains(focusedButton) &&
            e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            int index = _launcherButtons.IndexOf(focusedButton);
            int next = e.Key switch
            {
                Key.Left or Key.Up => Math.Max(0, index - 1),
                _ => Math.Min(_launcherButtons.Count - 1, index + 1),
            };
            _launcherButtons[next].Focus();
            e.Handled = true;
        }
    }

    private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only bare workspace surfaces dismiss. Bubbling events from a control,
        // card, scrollbar or feedback region must never close the overlay.
        if (ReferenceEquals(e.OriginalSource, OverlayRoot) ||
            ReferenceEquals(e.OriginalSource, WorkspaceSurface) ||
            ReferenceEquals(e.OriginalSource, WidgetSurface) ||
            ReferenceEquals(e.OriginalSource, WidgetCanvas))
        {
            e.Handled = true;
            RequestClose();
        }
    }

    private void WorkspaceSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Backdrop_MouseLeftButtonDown(sender, e);
        e.Handled = true;
    }

    private void LauncherPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        e.Handled = true;

    private void InspectorHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        e.Handled = true;

    private void InspectorClose_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        HideInspector();
    }

    private void InspectorQuickWidget_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (InspectorQuickWidgetButton.Tag is not string widgetId)
        {
            return;
        }

        HideInspector();
        FocusWidget(widgetId);
    }

    private void OpenMainWindow_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_commandRequested is Action<QuickSwitchCommand> callback)
        {
            callback(QuickSwitchCommandCatalog.OpenMainWindow.CreateCommand());
        }
        else
        {
            RequestClose();
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_commandRequested is Action<QuickSwitchCommand> callback)
        {
            callback(QuickSwitchCommandCatalog.ExitApplication.CreateCommand());
        }
        else
        {
            RequestClose();
        }
    }

    private void DismissFeedback_Click(object sender, RoutedEventArgs e) =>
        SetFeedback(null);

    private void InspectorResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        _isInspectorResizing = true;
        InspectorHost.Width = ClampInspectorWidth(
            InspectorHost.Width - e.HorizontalChange);
    }

    private void InspectorResizeGrip_DragCompleted(
        object sender,
        DragCompletedEventArgs e)
    {
        _isInspectorResizing = false;
        ClampInspectorWidth();
        _inspectorWidthChanged?.Invoke(InspectorHost.Width);
    }

    private void ClampInspectorWidth()
    {
        if (!_isInspectorResizing &&
            InspectorHost.Visibility != Visibility.Visible)
        {
            return;
        }

        InspectorHost.Width = ClampInspectorWidth(InspectorHost.Width);
    }

    private double ClampInspectorWidth(double value)
    {
        double available = Math.Max(
            MinimumInspectorWidth,
            WidgetSurface.ActualWidth * 0.80);
        double maximum = Math.Min(MaximumInspectorWidth, available);
        return Math.Clamp(
            double.IsFinite(value) ? value : DefaultInspectorWidth,
            MinimumInspectorWidth,
            maximum);
    }

    private (double Width, double Height) GetWidgetMinimumSize(string id)
    {
        OverlayWorkspaceWidget? widget = _widgets.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        return widget is null
            ? (MinimumWidgetWidth, MinimumWidgetHeight)
            : (
                Math.Max(MinimumWidgetWidth, widget.MinimumWidth),
                Math.Max(MinimumWidgetHeight, widget.MinimumHeight));
    }

    private static void ApplyCollapsedVisualState(
        string id,
        Border card,
        OverlayWorkspaceWidgetLayout layout)
    {
        if (card.Child is not Grid cardContainer ||
            cardContainer.Children.OfType<Grid>().FirstOrDefault() is not
                Grid cardGrid)
        {
            return;
        }

        foreach (FrameworkElement child in cardGrid.Children
                     .OfType<FrameworkElement>()
                     .Where(child => Grid.GetRow(child) == 1))
        {
            child.Visibility = layout.IsCollapsed
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        foreach (Thumb grip in cardContainer.Children.OfType<Thumb>())
        {
            grip.Visibility = layout.IsCollapsed
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        foreach (Button button in FindVisualChildren<Button>(card))
        {
            if (button.Tag is not OverlayActionTarget target ||
                !string.Equals(target.WidgetId, id, StringComparison.Ordinal))
            {
                continue;
            }

            if (target.Kind == OverlayWorkspaceActionKind.FitContent)
            {
                button.Visibility = layout.IsCollapsed
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                continue;
            }

            if (target.Kind != OverlayWorkspaceActionKind.Collapse)
            {
                continue;
            }

            button.Content = layout.IsCollapsed ? "□" : "—";
            button.ToolTip = layout.IsCollapsed
                ? "Restore this widget"
                : "Minimize this widget";
            AutomationProperties.SetName(
                button,
                layout.IsCollapsed
                    ? $"Restore {id} widget"
                    : $"Minimize {id} widget");
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void NotifyLayoutChanged() =>
        _layoutChanged?.Invoke(CaptureLayout());

    private static string GetPanelGlyph(string panel) =>
        panel.ToLowerInvariant() switch
        {
            "home" => "⌂",
            "display" => "▣",
            "audio" => "♫",
            "devices" => "⌁",
            "favorites" => "★",
            "options" => "⚙",
            "profiles" => "▤",
            "about" => "ⓘ",
            _ => "•",
        };

    private static string GetPanelLabel(string panel) =>
        $"{GetPanelGlyph(panel)}   {panel}";

    private bool TryGetNumberedPanel(
        Key key,
        out OverlayWorkspacePanel panel)
    {
        int number = key switch
        {
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            Key.D6 or Key.NumPad6 => 6,
            _ => 0,
        };

        return FeatureDescriptorCatalog.TryGetWorkspacePanelFromControlNumber(
            number,
            ProfilesLauncherButton.Visibility == Visibility.Visible,
            out panel);
    }

    private void RequestClose()
    {
        if (_closeRequestedRaised)
        {
            return;
        }

        _closeRequestedRaised = true;
        _restoreForegroundOnClose = true;
        NotifyLayoutChanged();

        if (_closeRequested is Action callback)
        {
            callback();
        }
        else
        {
            Close();
        }
    }

    private static bool TryFocusFirstDescendant(DependencyObject root)
    {
        if (root is not Visual && root is not Visual3D)
        {
            return false;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is Control
                {
                    Focusable: true,
                    IsEnabled: true,
                    IsVisible: true,
                } control &&
                control.Focus())
            {
                return true;
            }

            if (TryFocusFirstDescendant(child))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record OverlayActionTarget(
        string WidgetId,
        OverlayWorkspaceActionKind Kind);
}

public enum OverlayWorkspacePanel
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

public enum OverlayWorkspaceFeedbackSeverity
{
    Information,
    Success,
    Warning,
    Error,
}

public enum OverlayWorkspaceActionKind
{
    Collapse,
    FitContent,
    Remove,
    Refresh,
}

public sealed record OverlayWorkspaceAction(
    OverlayWorkspaceActionKind Kind,
    string Label,
    bool IsEnabled = true);

public sealed record OverlayWorkspaceWidget(
    string Id,
    OverlayWorkspacePanel Panel,
    string Title,
    string Summary,
    IReadOnlyList<OverlayWorkspaceAction>? Actions = null,
    UIElement? Content = null,
    bool IsCollapsed = false,
    OverlayWorkspaceWidgetLayout? Layout = null,
    bool CanResize = false,
    bool CanCollapse = false,
    double MinimumWidth = OverlayWidgetPlacement.MinimumWidth,
    double MinimumHeight = OverlayWidgetPlacement.MinimumHeight);

public sealed record OverlayWorkspaceWidgetLayout(
    string Id,
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsCollapsed = false,
    int ZIndex = 0,
    bool IsVisible = true,
    bool HasCustomSize = false,
    int ContentLayoutVersion = 0);
