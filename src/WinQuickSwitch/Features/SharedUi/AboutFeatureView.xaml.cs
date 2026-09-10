using System.Windows;
using System.Windows.Controls;

namespace WinQuickSwitch.Features.SharedUi;

/// <summary>
/// Reusable edition-aware product, licensing, and privacy surface.
/// </summary>
public partial class AboutFeatureView : UserControl
{
    public AboutFeatureView()
    {
        InitializeComponent();
        Loaded += AboutFeatureView_Loaded;
        Unloaded += AboutFeatureView_Unloaded;
    }

    private void AboutFeatureView_Loaded(object sender, RoutedEventArgs e) =>
        SharedFeatureViewLifetime.Attach(this);

    private void AboutFeatureView_Unloaded(object sender, RoutedEventArgs e) =>
        SharedFeatureViewLifetime.Detach(this);
}
