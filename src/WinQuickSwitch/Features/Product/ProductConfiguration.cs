using System.Reflection;

namespace WinQuickSwitch.Features.Product;

internal enum ProductEdition
{
    Paid,
    Free,
}

internal enum ProductFeature
{
    Profiles,
}

internal sealed record ProductConfiguration
{
    private ProductConfiguration(
        ProductEdition edition,
        string displayName,
        string settingsDirectoryName,
        IReadOnlySet<ProductFeature> enabledFeatures)
    {
        Edition = edition;
        DisplayName = displayName;
        SettingsDirectoryName = settingsDirectoryName;
        EnabledFeatures = enabledFeatures;
    }

    public ProductEdition Edition { get; }

    public string DisplayName { get; }

    /// <summary>
    /// The four-part release version stamped into the executable and Store
    /// package.  Keeping this derived from assembly metadata prevents the UI
    /// from drifting away from the files users download.
    /// </summary>
    public static string ReleaseVersion =>
        typeof(ProductConfiguration).Assembly.GetName().Version?.ToString(4) ??
        "1.0.20.0";

    public string VersionedDisplayName =>
        $"{DisplayName} v{ReleaseVersion}";

    public string SettingsDirectoryName { get; }

    public IReadOnlySet<ProductFeature> EnabledFeatures { get; }

    public bool IsPaid => Edition == ProductEdition.Paid;

    public bool IsFree => Edition == ProductEdition.Free;

    public static ProductConfiguration Paid { get; } = new(
        ProductEdition.Paid,
        "WinQuickSwitch",
        "WinQuickSwitch",
        new HashSet<ProductFeature>
        {
            ProductFeature.Profiles,
        });

    public static ProductConfiguration Free { get; } = new(
        ProductEdition.Free,
        "WinQuickSwitch Free",
        "WinQuickSwitch Free",
        new HashSet<ProductFeature>());

    public static ProductConfiguration Default
    {
        get
        {
#if WINQUICKSWITCH_FREE
            return Free;
#else
            return Paid;
#endif
        }
    }

    public static ProductConfiguration For(ProductEdition edition) =>
        edition switch
        {
            ProductEdition.Paid => Paid,
            ProductEdition.Free => Free,
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown product edition."),
        };

    public bool HasFeature(ProductFeature feature) =>
        EnabledFeatures.Contains(feature);
}
