using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using WinQuickSwitch.Features.Display;

namespace WinQuickSwitch.Platform.Windows.Display;

public sealed class WindowsDisplayModeService : IDisplayModeService
{
    internal const uint SdcTopologyInternal = 0x00000001;
    internal const uint SdcTopologyClone = 0x00000002;
    internal const uint SdcTopologyExtend = 0x00000004;
    internal const uint SdcTopologyExternal = 0x00000008;
    internal const uint SdcApply = 0x00000080;

    private readonly IDisplayConfigNative _native;
    private readonly IExtendedDisplayLayoutRestorer _layoutRestorer;
    private readonly IDisplaySwitchProcess _displaySwitchProcess;
    private readonly string _displaySwitchPath;

    public WindowsDisplayModeService()
        : this(
            NativeDisplayConfig.Instance,
            new WindowsExtendedDisplayLayoutRestorer(),
            new HiddenProcessRunner(),
            GetDefaultDisplaySwitchPath())
    {
    }

    internal WindowsDisplayModeService(
        IDisplayConfigNative native,
        IExtendedDisplayLayoutRestorer layoutRestorer,
        IDisplaySwitchProcess displaySwitchProcess,
        string displaySwitchPath)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(layoutRestorer);
        ArgumentNullException.ThrowIfNull(displaySwitchProcess);
        ArgumentException.ThrowIfNullOrWhiteSpace(displaySwitchPath);

        _native = native;
        _layoutRestorer = layoutRestorer;
        _displaySwitchProcess = displaySwitchProcess;
        _displaySwitchPath = displaySwitchPath;
    }

    public bool CanRestoreExtendedLayout =>
        _layoutRestorer.HasCapturedLayout;

    public async Task<DisplayModeResult> ApplyAsync(
        DisplayMode mode,
        CancellationToken cancellationToken = default)
    {
        uint flags = SdcApply | GetTopologyFlag(mode);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (mode != DisplayMode.Extend)
            {
                await RunOffCallingThread(
                    _layoutRestorer.TryCaptureCurrent,
                    CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
            }

            int errorCode;

            if (mode == DisplayMode.Extend)
            {
                int? restoreResult = await RunOffCallingThread(
                    _layoutRestorer.ApplyCaptured,
                    CancellationToken.None);

                errorCode = restoreResult == 0
                    ? 0
                    : await _displaySwitchProcess.RunAsync(
                        _displaySwitchPath,
                        "/extend",
                        cancellationToken);
            }
            else
            {
                errorCode = await RunOffCallingThread(
                    () => _native.Apply(flags),
                    CancellationToken.None);
            }
            cancellationToken.ThrowIfCancellationRequested();

            return errorCode == 0
                ? new DisplayModeResult(
                    true,
                    $"Display mode changed to {mode.GetDisplayName()}.",
                    errorCode)
                : new DisplayModeResult(
                    false,
                    $"Windows could not switch to {mode.GetDisplayName()} " +
                    $"(error code {errorCode}).",
                    errorCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is Win32Exception or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException or
                EntryPointNotFoundException or
                DllNotFoundException)
        {
            return new DisplayModeResult(
                false,
                $"Windows could not apply the display change: {exception.Message}");
        }
    }

    internal static uint GetTopologyFlag(DisplayMode mode) =>
        mode switch
        {
            DisplayMode.PcScreenOnly => SdcTopologyInternal,
            DisplayMode.Duplicate => SdcTopologyClone,
            DisplayMode.Extend => SdcTopologyExtend,
            DisplayMode.SecondScreenOnly => SdcTopologyExternal,
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unknown display mode."),
        };

    private static string GetDefaultDisplaySwitchPath()
    {
        string systemDirectory =
            Environment.GetFolderPath(Environment.SpecialFolder.System);
        return Path.Combine(systemDirectory, "DisplaySwitch.exe");
    }

    private static Task<T> RunOffCallingThread<T>(
        Func<T> action,
        CancellationToken cancellationToken) =>
        Task.Factory.StartNew(
            action,
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
}

internal interface IDisplayConfigNative
{
    int Apply(uint flags);
}

internal sealed class NativeDisplayConfig : IDisplayConfigNative
{
    public static NativeDisplayConfig Instance { get; } = new();

    private NativeDisplayConfig()
    {
    }

    public int Apply(uint flags) =>
        SetDisplayConfig(
            0,
            IntPtr.Zero,
            0,
            IntPtr.Zero,
            flags);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(
        uint pathCount,
        IntPtr paths,
        uint modeCount,
        IntPtr modes,
        uint flags);
}
