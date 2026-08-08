using System.IO;
using System.Text.Json;

namespace WinQuickSwitch.Features.Profiles;

internal sealed class JsonProfileStore : IProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _profilesPath;

    public JsonProfileStore() : this(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinQuickSwitch",
            "profiles.json"))
    {
    }

    internal JsonProfileStore(string profilesPath)
    {
        _profilesPath = profilesPath;
    }

    public ProfileCatalog Load()
    {
        try
        {
            if (!File.Exists(_profilesPath))
            {
                return ProfileCatalog.Empty;
            }

            string json = File.ReadAllText(_profilesPath);
            return (
                JsonSerializer.Deserialize<ProfileCatalog>(
                    json,
                    SerializerOptions) ??
                ProfileCatalog.Empty).Normalize();
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                NotSupportedException)
        {
            return ProfileCatalog.Empty;
        }
    }

    public bool TrySave(
        ProfileCatalog catalog,
        out string? errorMessage)
    {
        string temporaryPath = _profilesPath + ".tmp";

        try
        {
            string? directory = Path.GetDirectoryName(_profilesPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(
                catalog.Normalize(),
                SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _profilesPath, true);
            errorMessage = null;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException)
        {
            TryDeleteTemporaryFile(temporaryPath);
            errorMessage = "Profiles could not be saved.";
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
