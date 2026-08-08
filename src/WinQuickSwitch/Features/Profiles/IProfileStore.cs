namespace WinQuickSwitch.Features.Profiles;

internal interface IProfileStore
{
    ProfileCatalog Load();

    bool TrySave(ProfileCatalog catalog, out string? errorMessage);
}
