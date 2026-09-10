using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WinQuickSwitch.Features.Widget;

public partial class FloatingQuickWidgetWindow : Window
{
    private readonly Action? _closeRequested;
    private readonly Action? _resetLayoutRequested;
    private readonly Action<FloatingWidgetLayout>? _layoutChanged;
    private bool _closeRequestedRaised;

    public FloatingQuickWidgetWindow(
        string title,
        FloatingWidgetLayout layout,
        Action? closeRequested = null,
        Action? resetLayoutRequested = null,
        Action<FloatingWidgetLayout>? layoutChanged = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(layout);

        _closeRequested = closeRequested;
        _resetLayoutRequested = resetLayoutRequested;
        _layoutChanged = layoutChanged;

        InitializeComponent();
        WidgetTheme.AttachResourceScope(this);
        TitleText.Text = title;

        FloatingWidgetLayout normalized = layout.Normalize();
        Width = normalized.Width;
        Height = normalized.Height;
        Topmost = normalized.IsPinned;
        PinnedCheckBox.IsChecked = normalized.IsPinned;

        if (normalized.Left is double left && normalized.Top is double top)
        {
            Left = left;
            Top = top;
        }
    }

    public FloatingWidgetLayout CaptureLayout() =>
        new(
            IsOpen: true,
            Left: double.IsFinite(Left) ? Left : null,
            Top: double.IsFinite(Top) ? Top : null,
            Width: Width,
            Height: Height,
            IsPinned: PinnedCheckBox.IsChecked == true);

    public void SetFeatureContent(UIElement content)
    {
        ArgumentNullException.ThrowIfNull(content);
        FeatureContentHost.Content = content;
    }

    private void ResetLayout_Click(object sender, RoutedEventArgs e) =>
        _resetLayoutRequested?.Invoke();

    private void Close_Click(object sender, RoutedEventArgs e) =>
        RequestClose();

    private void PinnedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = PinnedCheckBox.IsChecked == true;
        NotifyLayoutChanged();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) =>
        NotifyLayoutChanged();

    private void Window_LocationChanged(object? sender, EventArgs e) =>
        NotifyLayoutChanged();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        RequestClose();
    }

    private void NotifyLayoutChanged()
    {
        if (!IsInitialized || !IsVisible)
        {
            return;
        }

        _layoutChanged?.Invoke(CaptureLayout());
    }

    private void RequestClose()
    {
        if (_closeRequestedRaised)
        {
            return;
        }

        _closeRequestedRaised = true;
        _closeRequested?.Invoke();

        if (_closeRequested is null)
        {
            Close();
        }
    }
}
