using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WinQuickSwitch.Features.SharedUi;

public partial class AudioFeatureView : UserControl, ISharedFeatureSurfaceAware
{
    public AudioFeatureView()
    {
        InitializeComponent();
        Loaded += AudioFeatureView_Loaded;
        Unloaded += AudioFeatureView_Unloaded;
    }

    private void AudioFeatureView_Loaded(object sender, RoutedEventArgs e) =>
        SharedFeatureViewLifetime.Attach(this);

    private void AudioFeatureView_Unloaded(object sender, RoutedEventArgs e) =>
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

    private async void SessionVolumeSlider_Commit(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is Slider { DataContext: AudioSessionRowViewModel row })
        {
            await row.CommitVolumeAsync();
        }
    }

    private async void SessionVolumeSlider_LostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        if (sender is Slider { DataContext: AudioSessionRowViewModel row })
        {
            await row.CommitVolumeAsync();
        }
    }

    private async void SessionVolumeSlider_KeyUp(object sender, KeyEventArgs e)
    {
        if (sender is Slider { DataContext: AudioSessionRowViewModel row } &&
            e.Key is Key.Left or Key.Right or Key.Up or Key.Down or
                Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            await row.CommitVolumeAsync();
        }
    }

    private async void SessionMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: AudioSessionRowViewModel row })
        {
            await row.CommitMuteAsync();
        }
    }
}
