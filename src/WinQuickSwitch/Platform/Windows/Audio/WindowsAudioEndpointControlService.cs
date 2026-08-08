using System.Runtime.InteropServices;
using WinQuickSwitch.Features.Audio;

namespace WinQuickSwitch.Platform.Windows.Audio;

public sealed class WindowsAudioEndpointControlService : IAudioEndpointControlService
{
    private readonly IAudioEndpointVolumeBackend _backend;

    public WindowsAudioEndpointControlService() : this(
        new CoreAudioEndpointVolumeBackend())
    {
    }

    internal WindowsAudioEndpointControlService(
        IAudioEndpointVolumeBackend backend)
    {
        _backend = backend;
    }

    public Task<AudioEndpointControlSnapshot?> GetStateAsync(
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            return Task.FromResult<AudioEndpointControlSnapshot?>(null);
        }

        return Task.Run(
            () => _backend.GetState(endpointId, cancellationToken),
            cancellationToken);
    }

    public Task<AudioControlResult> SetMasterVolumeAsync(
        string endpointId,
        string endpointName,
        float volume,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpointId) ||
            string.IsNullOrWhiteSpace(endpointName))
        {
            return Task.FromResult(AudioControlResult.Failure(
                "The audio endpoint is no longer available."));
        }

        if (!float.IsFinite(volume) || volume is < 0 or > 1)
        {
            return Task.FromResult(AudioControlResult.Failure(
                "Master volume must be between 0 and 100 percent."));
        }

        return Task.Run(
            () => _backend.SetMasterVolume(
                endpointId,
                endpointName,
                volume,
                cancellationToken),
            cancellationToken);
    }

    public Task<AudioControlResult> SetMuteAsync(
        string endpointId,
        string endpointName,
        bool isMuted,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpointId) ||
            string.IsNullOrWhiteSpace(endpointName))
        {
            return Task.FromResult(AudioControlResult.Failure(
                "The audio endpoint is no longer available."));
        }

        return Task.Run(
            () => _backend.SetMute(
                endpointId,
                endpointName,
                isMuted,
                cancellationToken),
            cancellationToken);
    }
}

internal interface IAudioEndpointVolumeBackend
{
    AudioEndpointControlSnapshot? GetState(
        string endpointId,
        CancellationToken cancellationToken);

    AudioControlResult SetMasterVolume(
        string endpointId,
        string endpointName,
        float volume,
        CancellationToken cancellationToken);

    AudioControlResult SetMute(
        string endpointId,
        string endpointName,
        bool isMuted,
        CancellationToken cancellationToken);
}

internal sealed class CoreAudioEndpointVolumeBackend : IAudioEndpointVolumeBackend
{
    private static readonly Guid AudioEndpointVolumeInterfaceId =
        new("5CDF2C82-841E-4546-9722-0CF74078229A");

    public AudioEndpointControlSnapshot? GetState(
        string endpointId,
        CancellationToken cancellationToken)
    {
        return WithEndpoint(
            endpointId,
            endpoint =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                endpoint.GetMasterVolumeLevelScalar(out float volume);
                endpoint.GetMute(out bool isMuted);
                return new AudioEndpointControlSnapshot(
                    Math.Clamp(volume, 0, 1),
                    isMuted);
            },
            cancellationToken);
    }

    public AudioControlResult SetMasterVolume(
        string endpointId,
        string endpointName,
        float volume,
        CancellationToken cancellationToken) =>
        Apply(
            endpointId,
            endpointName,
            endpoint =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Guid eventContext = Guid.Empty;
                endpoint.SetMasterVolumeLevelScalar(volume, ref eventContext);
            },
            $"{endpointName} master volume updated.",
            cancellationToken);

    public AudioControlResult SetMute(
        string endpointId,
        string endpointName,
        bool isMuted,
        CancellationToken cancellationToken) =>
        Apply(
            endpointId,
            endpointName,
            endpoint =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Guid eventContext = Guid.Empty;
                endpoint.SetMute(isMuted, ref eventContext);
            },
            isMuted
                ? $"{endpointName} microphone muted."
                : $"{endpointName} microphone unmuted.",
            cancellationToken);

    private static AudioControlResult Apply(
        string endpointId,
        string endpointName,
        Action<IAudioEndpointVolume> mutation,
        string successMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            AudioEndpointControlSnapshot? ignored = WithEndpoint(
                endpointId,
                endpoint =>
                {
                    mutation(endpoint);
                    return (AudioEndpointControlSnapshot?)null;
                },
                cancellationToken);
            return AudioControlResult.Success(successMessage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is COMException or
            InvalidCastException or
            InvalidOperationException)
        {
            return AudioControlResult.Failure(
                $"Windows could not update {endpointName}: {exception.Message}");
        }
    }

    private static T? WithEndpoint<T>(
        string endpointId,
        Func<IAudioEndpointVolume, T> operation,
        CancellationToken cancellationToken)
    {
        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDevice? device = null;
        object? endpointObject = null;
        IAudioEndpointVolume? endpoint = null;
        bool initialized = false;

        try
        {
            int initializeResult = CoreAudioInterop.CoInitializeEx(
                IntPtr.Zero,
                CoreAudioInterop.CoInitializeMultithreaded);
            initialized = initializeResult >= 0;

            Type enumeratorType = Type.GetTypeFromCLSID(
                CoreAudioInterop.MmDeviceEnumeratorClassId,
                throwOnError: true)!;
            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                enumeratorType)!;
            deviceEnumerator.GetDevice(endpointId, out device);

            Guid interfaceId = AudioEndpointVolumeInterfaceId;
            device.Activate(
                ref interfaceId,
                ComClassContext.All,
                IntPtr.Zero,
                out endpointObject);
            endpoint = (IAudioEndpointVolume)endpointObject;
            cancellationToken.ThrowIfCancellationRequested();
            return operation(endpoint);
        }
        finally
        {
            ReleaseComObject(endpoint);

            if (!ReferenceEquals(endpoint, endpointObject))
            {
                ReleaseComObject(endpointObject);
            }

            ReleaseComObject(device);
            ReleaseComObject(deviceEnumerator);

            if (initialized)
            {
                CoreAudioInterop.CoUninitialize();
            }
        }
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.ReleaseComObject(instance);
        }
    }
}
