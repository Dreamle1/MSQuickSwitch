using System.Diagnostics;
using System.Runtime.InteropServices;
using WinQuickSwitch.Features.Audio;

namespace WinQuickSwitch.Platform.Windows.Audio;

public sealed class WindowsAudioInventoryService : IAudioInventoryService
{
    private const uint StorageAccessRead = 0;

    public Task<AudioInventory> GetInventoryAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadInventory(cancellationToken), cancellationToken);

    private static AudioInventory ReadInventory(
        CancellationToken cancellationToken)
    {
        IMMDeviceEnumerator? deviceEnumerator = null;

        try
        {
            Type enumeratorType = Type.GetTypeFromCLSID(
                CoreAudioInterop.MmDeviceEnumeratorClassId,
                throwOnError: true)!;

            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;

            DefaultEndpointIds playbackDefaults = ReadDefaultEndpointIds(
                deviceEnumerator,
                AudioDataFlow.Render);

            DefaultEndpointIds recordingDefaults = ReadDefaultEndpointIds(
                deviceEnumerator,
                AudioDataFlow.Capture);

            List<AudioSessionInfo> sessions = [];

            IReadOnlyList<AudioEndpointInfo> playbackEndpoints = ReadEndpoints(
                deviceEnumerator,
                AudioDataFlow.Render,
                AudioEndpointKind.Playback,
                playbackDefaults,
                sessions,
                cancellationToken);

            IReadOnlyList<AudioEndpointInfo> recordingEndpoints = ReadEndpoints(
                deviceEnumerator,
                AudioDataFlow.Capture,
                AudioEndpointKind.Recording,
                recordingDefaults,
                sessions: null,
                cancellationToken);

            return new AudioInventory(
                playbackEndpoints,
                recordingEndpoints,
                sessions
                    .OrderBy(session => session.ApplicationName, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray(),
                DateTimeOffset.Now);
        }
        finally
        {
            ReleaseComObject(deviceEnumerator);
        }
    }

    private static IReadOnlyList<AudioEndpointInfo> ReadEndpoints(
        IMMDeviceEnumerator deviceEnumerator,
        AudioDataFlow dataFlow,
        AudioEndpointKind endpointKind,
        DefaultEndpointIds defaults,
        List<AudioSessionInfo>? sessions,
        CancellationToken cancellationToken)
    {
        IMMDeviceCollection? collection = null;
        List<AudioEndpointInfo> endpoints = [];

        try
        {
            deviceEnumerator.EnumAudioEndpoints(
                dataFlow,
                AudioDeviceState.Active,
                out collection);

            collection.GetCount(out uint count);

            for (uint index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IMMDevice? device = null;

                try
                {
                    collection.Item(index, out device);
                    device.GetId(out string id);

                    string fallbackName = endpointKind == AudioEndpointKind.Playback
                        ? "Unknown playback device"
                        : "Unknown recording device";

                    string name = ReadFriendlyName(device) ?? fallbackName;

                    endpoints.Add(new AudioEndpointInfo(
                        id,
                        name,
                        endpointKind,
                        id == defaults.Console,
                        id == defaults.Multimedia,
                        id == defaults.Communications));

                    if (sessions is not null)
                    {
                        ReadSessions(device, name, sessions, cancellationToken);
                    }
                }
                catch (COMException)
                {
                    // A device can disappear between collection enumeration and access.
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }
        }
        finally
        {
            ReleaseComObject(collection);
        }

        return endpoints
            .OrderByDescending(endpoint => endpoint.IsDefault)
            .ThenByDescending(endpoint => endpoint.IsCommunicationsDefault)
            .ThenBy(endpoint => endpoint.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static DefaultEndpointIds ReadDefaultEndpointIds(
        IMMDeviceEnumerator deviceEnumerator,
        AudioDataFlow dataFlow)
    {
        string? console = TryReadDefaultEndpointId(
            deviceEnumerator,
            dataFlow,
            AudioRole.Console);

        string? multimedia = TryReadDefaultEndpointId(
            deviceEnumerator,
            dataFlow,
            AudioRole.Multimedia);

        string? communications = TryReadDefaultEndpointId(
            deviceEnumerator,
            dataFlow,
            AudioRole.Communications);

        return new DefaultEndpointIds(
            console,
            multimedia,
            communications);
    }

    private static string? TryReadDefaultEndpointId(
        IMMDeviceEnumerator deviceEnumerator,
        AudioDataFlow dataFlow,
        AudioRole role)
    {
        IMMDevice? device = null;

        try
        {
            deviceEnumerator.GetDefaultAudioEndpoint(dataFlow, role, out device);
            device.GetId(out string id);
            return id;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            ReleaseComObject(device);
        }
    }

    private static string? ReadFriendlyName(IMMDevice device)
    {
        IPropertyStore? propertyStore = null;
        PropVariant value = default;

        try
        {
            device.OpenPropertyStore(StorageAccessRead, out propertyStore);
            PropertyKey key = CoreAudioInterop.DeviceFriendlyName;
            propertyStore.GetValue(ref key, out value);
            return value.GetString();
        }
        finally
        {
            CoreAudioInterop.PropVariantClear(ref value);
            ReleaseComObject(propertyStore);
        }
    }

    private static void ReadSessions(
        IMMDevice device,
        string endpointName,
        List<AudioSessionInfo> destination,
        CancellationToken cancellationToken)
    {
        object? sessionManagerObject = null;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;

        try
        {
            Guid sessionManagerId = typeof(IAudioSessionManager2).GUID;
            device.Activate(
                ref sessionManagerId,
                ComClassContext.All,
                IntPtr.Zero,
                out sessionManagerObject);

            sessionManager = (IAudioSessionManager2)sessionManagerObject;
            sessionManager.GetSessionEnumerator(out sessionEnumerator);
            sessionEnumerator.GetCount(out int count);

            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IAudioSessionControl? sessionControl = null;

                try
                {
                    sessionEnumerator.GetSession(index, out sessionControl);
                    IAudioSessionControl2 sessionControl2 = (IAudioSessionControl2)sessionControl;
                    sessionControl2.GetState(out AudioSessionState state);

                    if (state != AudioSessionState.Active)
                    {
                        continue;
                    }

                    sessionControl2.GetSessionInstanceIdentifier(out string sessionId);
                    sessionControl2.GetProcessId(out uint processId);
                    sessionControl2.GetDisplayName(out string displayName);

                    ISimpleAudioVolume simpleVolume = (ISimpleAudioVolume)sessionControl;
                    simpleVolume.GetMasterVolume(out float volume);
                    simpleVolume.GetMute(out bool isMuted);

                    destination.Add(new AudioSessionInfo(
                        sessionId,
                        ResolveApplicationName(displayName, processId),
                        endpointName,
                        volume,
                        isMuted));
                }
                catch (Exception exception) when (
                    exception is COMException or
                    InvalidCastException or
                    ArgumentException)
                {
                    // Sessions can expire while they are being enumerated.
                }
                finally
                {
                    ReleaseComObject(sessionControl);
                }
            }
        }
        catch (COMException)
        {
            // Some active endpoints do not expose a session manager.
        }
        finally
        {
            ReleaseComObject(sessionEnumerator);

            if (!ReferenceEquals(sessionManager, sessionManagerObject))
            {
                ReleaseComObject(sessionManager);
            }

            ReleaseComObject(sessionManagerObject);
        }
    }

    private static string ResolveApplicationName(
        string? displayName,
        uint processId)
    {
        if (!string.IsNullOrWhiteSpace(displayName) &&
            !displayName.StartsWith('@'))
        {
            return displayName;
        }

        if (processId == 0)
        {
            return "System sounds";
        }

        try
        {
            using Process process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            OverflowException)
        {
            return $"Process {processId}";
        }
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.ReleaseComObject(instance);
        }
    }

    private sealed record DefaultEndpointIds(
        string? Console,
        string? Multimedia,
        string? Communications);
}
