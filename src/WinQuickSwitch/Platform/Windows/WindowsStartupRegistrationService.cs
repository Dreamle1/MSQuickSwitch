using System.IO;
using System.Security;
using Microsoft.Win32;
using WinQuickSwitch.Features.Widget;

namespace WinQuickSwitch.Platform.Windows;

internal sealed class WindowsStartupRegistrationService :
    IStartupRegistrationService
{
    internal const string StartupArgument = "--startup";
    internal const string StartupValueName = "WinQuickSwitch";
    private const int MaximumRunCommandLength = 260;

    private readonly IStartupRegistry _registry;
    private readonly Func<string?> _executablePathProvider;

    public WindowsStartupRegistrationService() : this(
        new CurrentUserStartupRegistry(),
        () => Environment.ProcessPath)
    {
    }

    internal WindowsStartupRegistrationService(
        IStartupRegistry registry,
        Func<string?> executablePathProvider)
    {
        _registry = registry;
        _executablePathProvider = executablePathProvider;
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                string? executablePath = _executablePathProvider();

                return !string.IsNullOrWhiteSpace(executablePath) &&
                    string.Equals(
                        _registry.ReadValue(),
                        BuildCommand(executablePath),
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (IsRegistrationException(exception))
            {
                return false;
            }
        }
    }

    public StartupRegistrationResult SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                _registry.DeleteValue();
                return new(
                    true,
                    "Start with Windows disabled.");
            }

            string? executablePath = _executablePathProvider();

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return new(
                    false,
                    "The app location could not be found.");
            }

            string command = BuildCommand(executablePath);

            if (command.Length > MaximumRunCommandLength)
            {
                return new(
                    false,
                    "The app path is too long for Windows startup.");
            }

            _registry.WriteValue(command);
            return new(
                true,
                "Starts hidden when you sign in.");
        }
        catch (Exception exception) when (IsRegistrationException(exception))
        {
            return new(
                false,
                $"Windows startup could not be changed: {exception.Message}");
        }
    }

    internal static string BuildCommand(string executablePath) =>
        $"\"{executablePath}\" {StartupArgument}";

    private static bool IsRegistrationException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;
}

internal interface IStartupRegistry
{
    string? ReadValue();

    void WriteValue(string command);

    void DeleteValue();
}

internal sealed class CurrentUserStartupRegistry : IStartupRegistry
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? ReadValue()
    {
        using RegistryKey? runKey =
            Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);

        return runKey?.GetValue(
            WindowsStartupRegistrationService.StartupValueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void WriteValue(string command)
    {
        using RegistryKey runKey =
            Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        runKey.SetValue(
            WindowsStartupRegistrationService.StartupValueName,
            command,
            RegistryValueKind.String);
    }

    public void DeleteValue()
    {
        using RegistryKey? runKey =
            Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);

        runKey?.DeleteValue(
            WindowsStartupRegistrationService.StartupValueName,
            throwOnMissingValue: false);
    }
}
