using System.IO;
using System.Text.Json;

namespace WinQuickSwitch.Features.Widget;

internal sealed class JsonWidgetSettingsStore : IWidgetSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public JsonWidgetSettingsStore() : this(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinQuickSwitch",
            "settings.json"))
    {
    }

    internal JsonWidgetSettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public WidgetSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return WidgetSettings.Default;
            }

            string json = File.ReadAllText(_settingsPath);
            return (
                JsonSerializer.Deserialize<WidgetSettings>(
                    json,
                    SerializerOptions) ??
                WidgetSettings.Default).Normalize();
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
                settings.Normalize(),
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
