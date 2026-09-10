namespace WinQuickSwitch.Features.Audio;

internal enum AudioQuickAction
{
    VolumeUp,
    VolumeDown,
    ToggleOutputMute,
    ToggleMicrophoneMute,
}

internal sealed class AudioQuickActionExecutor
{
    private static readonly TimeSpan MinimumAcceptedCallSpacing =
        TimeSpan.FromMilliseconds(150);

    private readonly IAudioInventoryService _audioInventoryService;
    private readonly IAudioEndpointControlService _audioEndpointControlService;
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private long? _lastAcceptedCallTimestamp;

    public AudioQuickActionExecutor(
        IAudioInventoryService audioInventoryService,
        IAudioEndpointControlService audioEndpointControlService,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(audioInventoryService);
        ArgumentNullException.ThrowIfNull(audioEndpointControlService);

        _audioInventoryService = audioInventoryService;
        _audioEndpointControlService = audioEndpointControlService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AudioControlResult> ExecuteAsync(
        AudioQuickAction action,
        int volumeStepPercent,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(action))
        {
            return AudioControlResult.Failure(
                "That audio shortcut is unavailable.");
        }

        if (volumeStepPercent is < 1 or > 20)
        {
            return AudioControlResult.Failure(
                "Volume step must be between 1 and 20 percent.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        bool entered = await _executionGate.WaitAsync(
            TimeSpan.Zero,
            cancellationToken);
        if (!entered)
        {
            return AudioControlResult.Failure(
                "Another audio quick action is already in progress.");
        }

        try
        {
            long now = _timeProvider.GetTimestamp();
            if (_lastAcceptedCallTimestamp is long previous &&
                _timeProvider.GetElapsedTime(previous, now) <
                    MinimumAcceptedCallSpacing)
            {
                return AudioControlResult.Failure(
                    "Audio quick actions are rate-limited; try again shortly.");
            }

            // Reserve the accepted-call timestamp before reading or mutating
            // Windows state so a failed or canceled attempt cannot be retried
            // in a tight loop.
            _lastAcceptedCallTimestamp = now;

            AudioInventory? inventory =
                await _audioInventoryService.GetInventoryAsync(cancellationToken);
            bool microphoneAction = action == AudioQuickAction.ToggleMicrophoneMute;
            AudioEndpointInfo? endpoint = ResolveEndpoint(
                inventory,
                microphoneAction);
            if (endpoint is null)
            {
                return AudioControlResult.Failure(
                    microphoneAction
                        ? "Windows did not expose a unique default recording endpoint."
                        : "Windows did not expose a unique default playback endpoint.");
            }

            AudioEndpointControlSnapshot? state =
                await _audioEndpointControlService.GetStateAsync(
                    endpoint.Id,
                    cancellationToken);
            if (state is null)
            {
                return AudioControlResult.Failure(
                    microphoneAction
                        ? "The default recording endpoint is unavailable."
                        : "The default playback endpoint is unavailable.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            return action switch
            {
                AudioQuickAction.VolumeUp or AudioQuickAction.VolumeDown =>
                    await SetVolumeAsync(
                        action,
                        volumeStepPercent,
                        endpoint,
                        state,
                        cancellationToken),
                AudioQuickAction.ToggleOutputMute or
                    AudioQuickAction.ToggleMicrophoneMute =>
                    await _audioEndpointControlService.SetMuteAsync(
                        endpoint.Id,
                        endpoint.Name,
                        !state.IsMuted,
                        cancellationToken),
                _ => AudioControlResult.Failure(
                    "That audio shortcut is unavailable."),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Do not expose endpoint IDs or native/COM details through the
            // global shortcut feedback path.
            return AudioControlResult.Failure(
                "Audio quick action could not be completed.");
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private async Task<AudioControlResult> SetVolumeAsync(
        AudioQuickAction action,
        int volumeStepPercent,
        AudioEndpointInfo endpoint,
        AudioEndpointControlSnapshot state,
        CancellationToken cancellationToken)
    {
        if (!float.IsFinite(state.MasterVolume) ||
            state.MasterVolume is < 0 or > 1)
        {
            return AudioControlResult.Failure(
                "Windows returned an invalid master volume.");
        }

        float step = volumeStepPercent / 100f;
        float target = action == AudioQuickAction.VolumeUp
            ? state.MasterVolume + step
            : state.MasterVolume - step;
        target = Math.Clamp(target, 0, 1);

        cancellationToken.ThrowIfCancellationRequested();
        return await _audioEndpointControlService.SetMasterVolumeAsync(
            endpoint.Id,
            endpoint.Name,
            target,
            cancellationToken);
    }

    private static AudioEndpointInfo? ResolveEndpoint(
        AudioInventory? inventory,
        bool recording)
    {
        IReadOnlyList<AudioEndpointInfo>? endpoints = recording
            ? inventory?.RecordingEndpoints
            : inventory?.PlaybackEndpoints;
        if (endpoints is null)
        {
            return null;
        }

        AudioEndpointInfo[] multimediaDefaults = endpoints
            .Where(endpoint => endpoint.IsMultimediaDefault)
            .ToArray();
        if (multimediaDefaults.Length > 1)
        {
            return null;
        }

        if (multimediaDefaults.Length == 1)
        {
            return multimediaDefaults[0];
        }

        AudioEndpointInfo[] consoleDefaults = endpoints
            .Where(endpoint => endpoint.IsConsoleDefault)
            .ToArray();
        return consoleDefaults.Length == 1
            ? consoleDefaults[0]
            : null;
    }
}
