using System.IO;
using System.Text.Json;
using WinQuickSwitch.Features.Product;

namespace WinQuickSwitch.Features.Widget;

internal sealed class JsonWidgetSettingsStore : IWidgetSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath;
    private readonly bool _profilesEnabled;

    public JsonWidgetSettingsStore() : this(
        ProductConfiguration.Default)
    {
    }

    internal JsonWidgetSettingsStore(ProductConfiguration configuration) : this(
        GetDefaultPath(configuration),
        configuration.HasFeature(ProductFeature.Profiles))
    {
    }

    internal JsonWidgetSettingsStore(string settingsPath)
        : this(settingsPath, profilesEnabled: true)
    {
    }

    internal JsonWidgetSettingsStore(
        string settingsPath,
        ProductConfiguration configuration)
        : this(
            settingsPath,
            configuration.HasFeature(ProductFeature.Profiles))
    {
    }

    private JsonWidgetSettingsStore(
        string settingsPath,
        bool profilesEnabled)
    {
        _settingsPath = settingsPath;
        _profilesEnabled = profilesEnabled;
    }

    internal static string GetDefaultPath(ProductConfiguration configuration) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            configuration.SettingsDirectoryName,
            "settings.json");

    public WidgetSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return WidgetSettings.Default;
            }

            string json = File.ReadAllText(_settingsPath);
            // Optional WidgetSettings constructor parameters supply defaults for
            // settings files written before newer options (such as the overlay)
            // existed. Normalize after deserialization so invalid new values do
            // not make a legacy settings file unusable.
            WidgetSettings settings =
                JsonSerializer.Deserialize<WidgetSettings>(
                    json,
                    SerializerOptions) ??
                WidgetSettings.Default;
            return settings.Normalize(_profilesEnabled);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                NotSupportedException)
        {
            return WidgetSettings.Default;
        }
    }

    public bool TrySave(WidgetSettings settings, out string? errorMessage)
    {
        string temporaryPath = _settingsPath + ".tmp";

        try
        {
            string? directory = Path.GetDirectoryName(_settingsPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(
                settings.Normalize(_profilesEnabled),
                SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, true);
            errorMessage = null;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException)
        {
            TryDeleteTemporaryFile(temporaryPath);
            errorMessage = "Settings could not be saved.";
            return false;
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The next save replaces the same temporary file.
        }
    }
}
