using System.Windows;
using WinQuickSwitch.Features.Overlay;
using WinQuickSwitch.Features.Product;

namespace WinQuickSwitch.Features.SharedUi;

internal enum SharedFeatureSurface
{
    MainWindow,
    OverlaySidebar,
    OverlayWidget,
    FloatingWindow,
}

internal interface ISharedFeatureSurfaceAware
{
    void ConfigureSurface(SharedFeatureSurface surface);
}

internal sealed record SharedFeatureViewInstance(
    OverlayFeatureId Feature,
    SharedFeatureSurface Surface,
    FrameworkElement View,
    SharedFeatureViewModel ViewModel);

internal interface IFeatureViewFactory
{
    bool CanCreate(OverlayFeatureId feature);

    SharedFeatureViewInstance Create(
        OverlayFeatureId feature,
        SharedFeatureSurface surface);
}

/// <summary>
/// Produces a fresh WPF control and a fresh presentation view model for every
/// host. All instances share only the application coordinator/state boundary.
/// </summary>
internal sealed class FeatureViewFactory(
    IQuickSwitchCoordinator coordinator,
    ProductConfiguration configuration) : IFeatureViewFactory
{
    public static Type GetViewType(OverlayFeatureId feature) => feature switch
    {
        OverlayFeatureId.Display => typeof(DisplayFeatureView),
        OverlayFeatureId.Audio => typeof(AudioFeatureView),
        OverlayFeatureId.Devices => typeof(DevicesFeatureView),
        OverlayFeatureId.Options => typeof(OptionsFeatureView),
        OverlayFeatureId.Profiles => typeof(ProfilesFeatureView),
        OverlayFeatureId.About => typeof(AboutFeatureView),
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
    };

    public bool CanCreate(OverlayFeatureId feature) => feature switch
    {
        OverlayFeatureId.Display or
        OverlayFeatureId.Audio or
        OverlayFeatureId.Devices or
        OverlayFeatureId.Options or
        OverlayFeatureId.About => true,
        OverlayFeatureId.Profiles => configuration.HasFeature(ProductFeature.Profiles),
        _ => false,
    };

    public SharedFeatureViewInstance Create(
        OverlayFeatureId feature,
        SharedFeatureSurface surface)
    {
        if (!CanCreate(feature))
        {
            throw new InvalidOperationException(
                $"The {feature} feature is not available in the {configuration.Edition} edition.");
        }

        FrameworkElement view;
        SharedFeatureViewModel viewModel;

        switch (feature)
        {
            case OverlayFeatureId.Display:
                view = new DisplayFeatureView();
                viewModel = new DisplayFeatureViewModel(coordinator);
                break;
            case OverlayFeatureId.Audio:
                view = new AudioFeatureView();
                viewModel = new AudioFeatureViewModel(coordinator);
                break;
            case OverlayFeatureId.Devices:
                view = new DevicesFeatureView();
                viewModel = new DevicesFeatureViewModel(coordinator);
                break;
            case OverlayFeatureId.Options:
                view = new OptionsFeatureView();
                viewModel = new OptionsFeatureViewModel(coordinator, configuration);
                break;
            case OverlayFeatureId.Profiles:
                view = new ProfilesFeatureView();
                viewModel = new ProfilesFeatureViewModel(coordinator);
                break;
            case OverlayFeatureId.About:
                view = new AboutFeatureView();
                viewModel = new AboutFeatureViewModel(coordinator);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feature), feature, null);
        }

        view.DataContext = viewModel;
        if (view is ISharedFeatureSurfaceAware surfaceAware)
        {
            surfaceAware.ConfigureSurface(surface);
        }
        return new SharedFeatureViewInstance(feature, surface, view, viewModel);
    }
}
