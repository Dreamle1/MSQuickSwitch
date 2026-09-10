using System.Windows;
using System.Windows.Controls;

namespace WinQuickSwitch.Features.SharedUi;

/// <summary>
/// Reusable, host-neutral Options surface.  A host supplies a surface view
/// model through <see cref="System.Windows.FrameworkElement.DataContext"/>;
/// the control never calls Windows services or owns application state.
/// </summary>
public partial class OptionsFeatureView : UserControl
{
    public OptionsFeatureView()
    {
        InitializeComponent();
        Loaded += OptionsFeatureView_Loaded;
        Unloaded += OptionsFeatureView_Unloaded;
    }

    private void OptionsFeatureView_Loaded(object sender, RoutedEventArgs e) =>
        SharedFeatureViewLifetime.Attach(this);

    private void OptionsFeatureView_Unloaded(object sender, RoutedEventArgs e) =>
        SharedFeatureViewLifetime.Detach(this);
}
