using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinQuickSwitch.Features.Overlay;
using WinQuickSwitch.Platform.Windows.Overlay;

namespace WinQuickSwitch.Tests;

/// <summary>
/// Routed-event regressions for the workspace dismissal boundary. The
/// caller runs these checks on the STA thread that already owns the test WPF
/// Application; no window is shown and no hardware state is changed.
/// </summary>
internal static class OverlayDismissalTests
{
    /// <summary>
    /// Runs the dismissal checks on the current WPF dispatcher. The shared
    /// WPF rendering test in Program creates the Application and STA thread,
    /// then invokes this method while those resources are still available.
    /// </summary>
    internal static void RunOnCurrentStaThread()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException(
                "Overlay dismissal tests require the existing WPF STA thread.");
        }

        if (Application.Current is null)
        {
            throw new InvalidOperationException(
                "Overlay dismissal tests require an initialized WPF Application.");
        }

        BareWorkspaceSurfacesDismiss();
        InteractiveUiDoesNotDismiss();
        GlobalCloseButtonIsAbsent();
    }

    private static void BareWorkspaceSurfacesDismiss()
    {
        foreach (string elementName in
            new[] { "OverlayRoot", "WorkspaceSurface", "WidgetSurface", "WidgetCanvas" })
        {
            int closeCount = 0;
            OverlayWorkspaceWindow window = CreateWindow(() => closeCount++);
            UIElement source = FindElement<UIElement>(window, elementName);

            MouseButtonEventArgs mouseDown = RaiseLeftButtonDown(source);

            Equal(
                1,
                closeCount,
                $"A bare {elementName} click should dismiss the overlay once.");
            True(
                mouseDown.Handled,
                $"A bare {elementName} click should be marked handled.");
        }
    }

    private static void InteractiveUiDoesNotDismiss()
    {
        int closeCount = 0;
        OverlayWorkspaceWindow window = CreateWindow(() => closeCount++);
        window.SetWidgets(
        [
            new OverlayWorkspaceWidget(
                "Display",
                OverlayWorkspacePanel.Display,
                "Display",
                "Current mode")
        ]);

        UIElement launcher = FindElement<UIElement>(window, "LauncherPanel");
        UIElement launcherButton =
            FindElement<UIElement>(window, "DisplayLauncherButton");
        Border inspector = FindElement<Border>(window, "InspectorHost");
        inspector.Visibility = Visibility.Visible;
        Canvas canvas = FindElement<Canvas>(window, "WidgetCanvas");
        UIElement card = (UIElement)canvas.Children[0];

        RaiseLeftButtonDown(launcher);
        RaiseLeftButtonDown(launcherButton);
        RaiseLeftButtonDown(inspector);
        RaiseLeftButtonDown(card);

        Equal(
            0,
            closeCount,
            "Launcher, inspector, and widget controls must not dismiss the overlay.");
    }

    private static void GlobalCloseButtonIsAbsent()
    {
        OverlayWorkspaceWindow window = CreateWindow();
        Button[] buttons = FindVisualChildren<Button>(window).ToArray();

        True(
            buttons.All(button =>
                !string.Equals(
                    AutomationProperties.GetName(button),
                    "Close workspace",
                    StringComparison.OrdinalIgnoreCase)),
            "The workspace should not expose a redundant global close button.");
    }

    private static OverlayWorkspaceWindow CreateWindow(Action? closeRequested = null) =>
        new(
            closeRequested: closeRequested,
            monitorBounds: new OverlayMonitorBounds(0, 0, 1920, 1080));

    private static T FindElement<T>(OverlayWorkspaceWindow window, string name)
        where T : UIElement
    {
        return window.FindName(name) as T ??
            throw new InvalidOperationException(
                $"The overlay should expose the named element '{name}'.");
    }

    private static MouseButtonEventArgs RaiseLeftButtonDown(UIElement source)
    {
        MouseButtonEventArgs args = new(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            // MouseLeftButtonDown is a direct promoted event in WPF. Raise
            // the bubbling MouseDown event so the test exercises the same
            // ancestor route as a real pointer click.
            RoutedEvent = Mouse.MouseDownEvent,
        };
        source.RaiseEvent(args);
        return args;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
        {
            yield return match;
        }

        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (T child in FindVisualChildren<T>(VisualTreeHelper.GetChild(root, index)))
            {
                yield return child;
            }
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{message} Expected '{expected}', actual '{actual}'.");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
