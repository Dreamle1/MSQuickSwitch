using System.Runtime.InteropServices;
using WinQuickSwitch.Features.Audio;

namespace WinQuickSwitch.Platform.Windows.Audio;

public sealed class WindowsAudioSessionControlService : IAudioSessionControlService
{
    private readonly IAudioSessionMutationBackend _backend;

    public WindowsAudioSessionControlService() : this(new CoreAudioSessionMutationBackend())
    {
    }

    internal WindowsAudioSessionControlService(IAudioSessionMutationBackend backend)
    {
        _backend = backend;
    }

    public Task<AudioControlResult> SetVolumeAsync(
        string sessionId,
        float volume,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Task.FromResult(AudioControlResult.Failure(
                "The audio session is no longer available."));
        }

        if (!float.IsFinite(volume) || volume is < 0 or > 1)
        {
            return Task.FromResult(AudioControlResult.Failure(
                "Volume must be between 0 and 100 percent."));
        }

        return Task.Run(
            () => _backend.SetVolume(sessionId, volume, cancellationToken),
            cancellationToken);
    }

    public Task<AudioControlResult> SetMuteAsync(
        string sessionId,
        bool isMuted,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Task.FromResult(AudioControlResult.Failure(
                "The audio session is no longer available."));
        }

        return Task.Run(
            () => _backend.SetMute(sessionId, isMuted, cancellationToken),
            cancellationToken);
    }
}

internal interface IAudioSessionMutationBackend
{
    AudioControlResult SetVolume(
        string sessionId,
        float volume,
        CancellationToken cancellationToken);

    AudioControlResult SetMute(
        string sessionId,
        bool isMuted,
        CancellationToken cancellationToken);
}

internal sealed class CoreAudioSessionMutationBackend : IAudioSessionMutationBackend
{
    public AudioControlResult SetVolume(
        string sessionId,
        float volume,
        CancellationToken cancellationToken) =>
        Apply(
            sessionId,
            audioVolume => audioVolume.SetMasterVolume(volume, IntPtr.Zero),
            "Application volume updated.",
            cancellationToken);

    public AudioControlResult SetMute(
        string sessionId,
        bool isMuted,
        CancellationToken cancellationToken) =>
        Apply(
            sessionId,
            audioVolume => audioVolume.SetMute(isMuted, IntPtr.Zero),
            isMuted ? "Application muted." : "Application unmuted.",
            cancellationToken);

    private static AudioControlResult Apply(
        string sessionId,
        Action<ISimpleAudioVolume> mutation,
        string successMessage,
        CancellationToken cancellationToken)
    {
        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDeviceCollection? endpointCollection = null;

        try
        {
            Type enumeratorType = Type.GetTypeFromCLSID(
                CoreAudioInterop.MmDeviceEnumeratorClassId,
                throwOnError: true)!;

            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
            deviceEnumerator.EnumAudioEndpoints(
                AudioDataFlow.Render,
                AudioDeviceState.Active,
                out endpointCollection);

            endpointCollection.GetCount(out uint endpointCount);

            for (uint endpointIndex = 0; endpointIndex < endpointCount; endpointIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IMMDevice? device = null;

                try
                {
                    endpointCollection.Item(endpointIndex, out device);
                    AudioControlResult? result = TryApplyToEndpoint(
                        device,
                        sessionId,
                        mutation,
                        successMessage,
                        cancellationToken);

                    if (result is not null)
                    {
                        return result;
                    }
                }
                catch (COMException)
                {
                    // An endpoint can disappear while the user applies a change.
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }

            return AudioControlResult.Failure(
                "The application audio session ended before the change was applied.");
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
                $"Windows could not update the audio session: {exception.Message}");
        }
        finally
        {
            ReleaseComObject(endpointCollection);
            ReleaseComObject(deviceEnumerator);
        }
    }

    private static AudioControlResult? TryApplyToEndpoint(
        IMMDevice device,
        string targetSessionId,
        Action<ISimpleAudioVolume> mutation,
        string successMessage,
        CancellationToken cancellationToken)
    {
        object? managerObject = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;

        try
        {
            Guid managerId = typeof(IAudioSessionManager2).GUID;
            device.Activate(
                ref managerId,
                ComClassContext.All,
                IntPtr.Zero,
                out managerObject);

            manager = (IAudioSessionManager2)managerObject;
            manager.GetSessionEnumerator(out sessionEnumerator);
            sessionEnumerator.GetCount(out int sessionCount);

            for (int sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IAudioSessionControl? sessionControl = null;

                try
                {
                    sessionEnumerator.GetSession(sessionIndex, out sessionControl);
                    IAudioSessionControl2 sessionControl2 =
                        (IAudioSessionControl2)sessionControl;

                    string sessionId;

                    try
                    {
                        sessionControl2.GetSessionInstanceIdentifier(out sessionId);
                    }
                    catch (COMException)
                    {
                        // A non-target session can expire while it is inspected.
                        continue;
                    }

                    if (!string.Equals(
                        sessionId,
                        targetSessionId,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    try
                    {
                        mutation((ISimpleAudioVolume)sessionControl);
                        return AudioControlResult.Success(successMessage);
                    }
                    catch (COMException exception)
                    {
                        return AudioControlResult.Failure(
                            $"Windows could not update the audio session: {exception.Message}");
                    }
                }
                finally
                {
                    ReleaseComObject(sessionControl);
                }
            }

            return null;
        }
        finally
        {
            ReleaseComObject(sessionEnumerator);

            if (!ReferenceEquals(manager, managerObject))
            {
                ReleaseComObject(manager);
            }

            ReleaseComObject(managerObject);
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
