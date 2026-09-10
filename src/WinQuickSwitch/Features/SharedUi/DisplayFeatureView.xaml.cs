using System.Windows;
using System.Windows.Controls;

namespace WinQuickSwitch.Features.SharedUi;

public partial class DisplayFeatureView : UserControl, ISharedFeatureSurfaceAware
{
    public DisplayFeatureView()
    {
        InitializeComponent();
        Loaded += DisplayFeatureView_Loaded;
        Unloaded += DisplayFeatureView_Unloaded;
    }

    private void DisplayFeatureView_Loaded(object sender, RoutedEventArgs e) =>
        SharedFeatureViewLifetime.Attach(this);

    private void DisplayFeatureView_Unloaded(object sender, RoutedEventArgs e) =>
        SharedFeatureViewLifetime.Detach(this);

    void ISharedFeatureSurfaceAware.ConfigureSurface(SharedFeatureSurface surface)
    {
        bool isWorkspaceWidget = surface == SharedFeatureSurface.OverlayWidget;
        FeatureTitleText.Visibility = isWorkspaceWidget
            ? Visibility.Collapsed
            : Visibility.Visible;
        RefreshButton.Visibility = isWorkspaceWidget
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
