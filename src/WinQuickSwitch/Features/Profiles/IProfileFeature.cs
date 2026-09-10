using WinQuickSwitch.Features.Product;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Features.Profiles;

internal interface IProfileFeature
{
    bool IsEnabled { get; }

    ProfileCatalog Load();

    IReadOnlyList<ProfileShortcutBinding> GetShortcutBindings(
        ProfileCatalog catalog);

    bool TrySave(ProfileCatalog catalog, out string? errorMessage);
}

internal sealed record ProfileShortcutBinding(
    string ProfileId,
    WidgetShortcut Shortcut,
    string? DisplayName = null);

internal static class ProfileFeature
{
    public static IProfileStore CreateStore(
        ProductConfiguration configuration) =>
        configuration.HasFeature(ProductFeature.Profiles)
            ? new JsonProfileStore()
            : DisabledProfileStore.Instance;

    public static IProfileFeature Create(
        ProductConfiguration configuration,
        IProfileStore store) =>
        configuration.HasFeature(ProductFeature.Profiles)
            ? new EnabledProfileFeature(store)
            : DisabledProfileFeature.Instance;

    private sealed class EnabledProfileFeature(IProfileStore store) : IProfileFeature
    {
        public bool IsEnabled => true;

        public ProfileCatalog Load() => store.Load();

        public IReadOnlyList<ProfileShortcutBinding> GetShortcutBindings(
            ProfileCatalog catalog) =>
            catalog.Profiles
                .Where(profile => profile.Shortcut is { IsValid: true })
                .Select(profile => new ProfileShortcutBinding(
                    profile.Id,
                    profile.Shortcut!,
                    profile.Name))
                .ToArray();

        public bool TrySave(
            ProfileCatalog catalog,
            out string? errorMessage) =>
            store.TrySave(catalog, out errorMessage);
    }

    private sealed class DisabledProfileFeature : IProfileFeature
    {
        public static DisabledProfileFeature Instance { get; } = new();

        public bool IsEnabled => false;

        public ProfileCatalog Load() => ProfileCatalog.Empty;

        public IReadOnlyList<ProfileShortcutBinding> GetShortcutBindings(
            ProfileCatalog catalog) => [];

        public bool TrySave(
            ProfileCatalog catalog,
            out string? errorMessage)
        {
            errorMessage = "Profiles are not available in this edition.";
            return false;
        }
    }

    private sealed class DisabledProfileStore : IProfileStore
    {
        public static DisabledProfileStore Instance { get; } = new();

        public ProfileCatalog Load() => ProfileCatalog.Empty;

        public bool TrySave(
            ProfileCatalog catalog,
            out string? errorMessage)
        {
            errorMessage = "Profiles are not available in this edition.";
            return false;
        }
    }
}
