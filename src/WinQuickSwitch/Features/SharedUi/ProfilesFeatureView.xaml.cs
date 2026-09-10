using System.Windows;
using System.Windows.Controls;

namespace WinQuickSwitch.Features.SharedUi;

/// <summary>
/// Reusable Paid-edition profile management surface.  Edition composition is
/// enforced by the central feature factory; this control contains no service
/// ownership or static application state.
/// </summary>
public partial class ProfilesFeatureView : UserControl
{
    public ProfilesFeatureView()
    {
        InitializeComponent();
        Loaded += ProfilesFeatureView_Loaded;
        Unloaded += ProfilesFeatureView_Unloaded;
    }

    private void ProfilesFeatureView_Loaded(object sender, RoutedEventArgs e) =>
        SharedFeatureViewLifetime.Attach(this);

    private void ProfilesFeatureView_Unloaded(object sender, RoutedEventArgs e) =>
        SharedFeatureViewLifetime.Detach(this);
}
