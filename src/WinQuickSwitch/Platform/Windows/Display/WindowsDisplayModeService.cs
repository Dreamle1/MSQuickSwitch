using System.ComponentModel;
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

    public WindowsDisplayModeService()
        : this(NativeDisplayConfig.Instance)
    {
    }

    internal WindowsDisplayModeService(IDisplayConfigNative native)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;
    }

    public async Task<DisplayModeResult> ApplyAsync(
        DisplayMode mode,
        CancellationToken cancellationToken = default)
    {
        uint flags = SdcApply | GetTopologyFlag(mode);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            int errorCode = await Task.Run(
                () => _native.Apply(flags),
                CancellationToken.None);
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
