using System.ComponentModel;
using System.IO;
using WinQuickSwitch.Features.Display;

namespace WinQuickSwitch.Platform.Windows.Display;

public sealed class WindowsDisplayModeService : IDisplayModeService
{
    private readonly IDisplaySwitchProcess _process;
    private readonly string _displaySwitchPath;

    public WindowsDisplayModeService()
        : this(new HiddenProcessRunner(), GetDefaultDisplaySwitchPath())
    {
    }

    internal WindowsDisplayModeService(
        IDisplaySwitchProcess process,
        string displaySwitchPath)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrWhiteSpace(displaySwitchPath);

        _process = process;
        _displaySwitchPath = displaySwitchPath;
    }

    public async Task<DisplayModeResult> ApplyAsync(
        DisplayMode mode,
        CancellationToken cancellationToken = default)
    {
        string argument = GetArgument(mode);

        try
        {
            int exitCode = await _process.RunAsync(
                _displaySwitchPath,
                argument,
                cancellationToken);

            return exitCode == 0
                ? new DisplayModeResult(
                    true,
                    $"Display mode changed to {mode.GetDisplayName()}.",
                    exitCode)
                : new DisplayModeResult(
                    false,
                    $"Windows could not switch to {mode.GetDisplayName()} (exit code {exitCode}).",
                    exitCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is Win32Exception or
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException)
        {
            return new DisplayModeResult(
                false,
                $"Windows could not start the display change: {exception.Message}");
        }
    }

    internal static string GetArgument(DisplayMode mode) => mode switch
    {
        DisplayMode.PcScreenOnly => "/internal",
        DisplayMode.Duplicate => "/clone",
        DisplayMode.Extend => "/extend",
        DisplayMode.SecondScreenOnly => "/external",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown display mode."),
    };

    private static string GetDefaultDisplaySwitchPath()
    {
        string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return Path.Combine(systemDirectory, "DisplaySwitch.exe");
    }
}
