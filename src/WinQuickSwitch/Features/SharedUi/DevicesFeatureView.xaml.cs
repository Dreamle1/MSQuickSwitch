using System.Windows;
using System.Windows.Controls;

namespace WinQuickSwitch.Features.SharedUi;

public partial class DevicesFeatureView : UserControl, ISharedFeatureSurfaceAware
{
    public DevicesFeatureView()
    {
        InitializeComponent();
        Loaded += DevicesFeatureView_Loaded;
        Unloaded += DevicesFeatureView_Unloaded;
    }

    private void DevicesFeatureView_Loaded(object sender, RoutedEventArgs e) =>
        SharedFeatureViewLifetime.Attach(this);

    private void DevicesFeatureView_Unloaded(object sender, RoutedEventArgs e) =>
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
