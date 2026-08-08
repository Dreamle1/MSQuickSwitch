using System.Diagnostics;
using System.Runtime.InteropServices;
using WinQuickSwitch.Features.Audio;

namespace WinQuickSwitch.Platform.Windows.Audio;

public sealed class WindowsDefaultAudioEndpointService : IDefaultAudioEndpointService
{
    private readonly IDefaultAudioEndpointSetter _endpointSetter;
    private readonly IWindowsSettingsLauncher _settingsLauncher;

    public WindowsDefaultAudioEndpointService() : this(
        new PolicyConfigDefaultAudioEndpointSetter(),
        new WindowsSettingsLauncher())
    {
    }

    internal WindowsDefaultAudioEndpointService(
        IDefaultAudioEndpointSetter endpointSetter,
        IWindowsSettingsLauncher settingsLauncher)
    {
        _endpointSetter = endpointSetter;
        _settingsLauncher = settingsLauncher;
    }

    public Task<AudioControlResult> SetDefaultAsync(
        string endpointId,
        string endpointName,
        AudioDefaultRoleSelection roleSelection,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpointId) || string.IsNullOrWhiteSpace(endpointName))
        {
            return Task.FromResult(AudioControlResult.Failure(
                "Select an available audio device first."));
        }

        return Task.Run(
            () => SetDefault(endpointId, endpointName, roleSelection, cancellationToken),
            cancellationToken);
    }

    public AudioControlResult OpenSoundSettings() => OpenSettings(
        "ms-settings:sound",
        "Opened Windows sound settings.",
        "Windows sound settings");

    public AudioControlResult OpenVolumeMixerSettings() => OpenSettings(
        "ms-settings:apps-volume",
        "Opened Windows volume mixer.",
        "Windows volume mixer");

    private AudioControlResult OpenSettings(
        string settingsUri,
        string successMessage,
        string settingsName)
    {
        try
        {
            _settingsLauncher.Open(settingsUri);
            return AudioControlResult.Success(successMessage);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            return AudioControlResult.Failure(
                $"{settingsName} could not be opened: {exception.Message}");
        }
    }

    private AudioControlResult SetDefault(
        string endpointId,
        string endpointName,
        AudioDefaultRoleSelection roleSelection,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (roleSelection is
                AudioDefaultRoleSelection.General or
                AudioDefaultRoleSelection.Both)
            {
                _endpointSetter.SetDefaultEndpoint(endpointId, AudioRole.Console);
                cancellationToken.ThrowIfCancellationRequested();
                _endpointSetter.SetDefaultEndpoint(endpointId, AudioRole.Multimedia);

                if (roleSelection == AudioDefaultRoleSelection.General)
                {
                    return AudioControlResult.Success(
                        $"{endpointName} is now the default device.");
                }
            }

            _endpointSetter.SetDefaultEndpoint(endpointId, AudioRole.Communications);
            return AudioControlResult.Success(roleSelection ==
                AudioDefaultRoleSelection.Both
                    ? $"{endpointName} is now the default and communications device."
                    : $"{endpointName} is now the communications device.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return AudioControlResult.Failure(
                "Direct endpoint selection is unavailable on this Windows build.");
        }
    }
}

internal interface IDefaultAudioEndpointSetter
{
    void SetDefaultEndpoint(string endpointId, AudioRole role);
}

internal interface IWindowsSettingsLauncher
{
    void Open(string settingsUri);
}

internal sealed class PolicyConfigDefaultAudioEndpointSetter : IDefaultAudioEndpointSetter
{
    private static readonly Guid PolicyConfigClassId =
        new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    public void SetDefaultEndpoint(string endpointId, AudioRole role)
    {
        object? policyObject = null;

        try
        {
            Type policyType = Type.GetTypeFromCLSID(
                PolicyConfigClassId,
                throwOnError: true)!;

            policyObject = Activator.CreateInstance(policyType)!;
            ((IPolicyConfig)policyObject).SetDefaultEndpoint(endpointId, role);
        }
        finally
        {
            if (policyObject is not null && Marshal.IsComObject(policyObject))
            {
                Marshal.ReleaseComObject(policyObject);
            }
        }
    }
}

internal sealed class WindowsSettingsLauncher : IWindowsSettingsLauncher
{
    public void Open(string settingsUri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = settingsUri,
            UseShellExecute = true,
        });
    }
}

[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    void GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);

    void GetDeviceFormat(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        [MarshalAs(UnmanagedType.Bool)] bool defaultFormat,
        out IntPtr format);

    void ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    void SetDeviceFormat(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        IntPtr endpointFormat,
        IntPtr mixFormat);

    void GetProcessingPeriod(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        [MarshalAs(UnmanagedType.Bool)] bool defaultPeriod,
        out long period,
        out long minimumPeriod);

    void SetProcessingPeriod(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        ref long period);

    void GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

    void SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

    void GetPropertyValue(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        ref PropertyKey propertyKey,
        out PropVariant value);

    void SetPropertyValue(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        ref PropertyKey propertyKey,
        ref PropVariant value);

    void SetDefaultEndpoint(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        AudioRole role);

    void SetEndpointVisibility(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        [MarshalAs(UnmanagedType.Bool)] bool isVisible);
}
