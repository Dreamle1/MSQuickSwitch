using System.Runtime.InteropServices;
using WinQuickSwitch.Features.Audio;

namespace WinQuickSwitch.Platform.Windows.Audio;

public sealed class WindowsAudioChangeWatcher : IAudioChangeWatcher
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly object _stateGate = new();
    private readonly ManualResetEvent _stopSignal = new(false);
    private readonly AutoResetEvent _rebuildSignal = new(false);
    private readonly ManualResetEventSlim _startedSignal = new(false);
    private readonly List<IAudioSessionManager2> _sessionManagers = [];
    private readonly List<IAudioSessionControl> _sessionControls = [];
    private readonly EndpointNotificationClient _endpointNotifications;
    private readonly SessionNotificationClient _sessionNotifications;
    private readonly SessionEventsClient _sessionEvents;

    private Thread? _watcherThread;
    private Exception? _startupException;
    private IMMDeviceEnumerator? _deviceEnumerator;
    private bool _endpointNotificationsRegistered;
    private volatile bool _disposed;

    public WindowsAudioChangeWatcher()
    {
        _endpointNotifications = new EndpointNotificationClient(this);
        _sessionNotifications = new SessionNotificationClient(this);
        _sessionEvents = new SessionEventsClient(this);
    }

    public event EventHandler? Changed;

    public void Start()
    {
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_watcherThread is not null)
            {
                return;
            }

            _watcherThread = new Thread(WatchAudioChanges)
            {
                IsBackground = true,
                Name = "WinQuickSwitch audio notifications",
            };
            _watcherThread.SetApartmentState(ApartmentState.MTA);
            _watcherThread.Start();
        }

        if (!_startedSignal.Wait(StartTimeout))
        {
            Dispose();
            throw new TimeoutException("Windows audio notifications did not start in time.");
        }

        if (_startupException is not null)
        {
            Exception startupException = _startupException;
            Dispose();
            throw new InvalidOperationException(
                "Windows audio notifications could not be started.",
                startupException);
        }
    }

    public void Dispose()
    {
        Thread? watcherThread;

        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            watcherThread = _watcherThread;
        }

        _stopSignal.Set();

        bool stopped = watcherThread is null ||
            (watcherThread != Thread.CurrentThread && watcherThread.Join(StopTimeout));

        if (stopped)
        {
            _startedSignal.Dispose();
            _rebuildSignal.Dispose();
            _stopSignal.Dispose();
        }
    }

    private void WatchAudioChanges()
    {
        bool comInitialized = false;

        try
        {
            int result = CoreAudioInterop.CoInitializeEx(
                IntPtr.Zero,
                CoreAudioInterop.CoInitializeMultithreaded);

            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            comInitialized = true;
            RegisterEndpointNotifications();
            RebuildSessionSubscriptions();
            _startedSignal.Set();

            WaitHandle[] signals = [_stopSignal, _rebuildSignal];

            while (WaitHandle.WaitAny(signals) == 1)
            {
                try
                {
                    RebuildSessionSubscriptions();
                }
                catch (COMException)
                {
                    // A device can disappear while subscriptions are rebuilt.
                    // The next device callback will schedule another attempt.
                }
            }
        }
        catch (Exception exception) when (
            exception is COMException or
            InvalidCastException or
            InvalidOperationException)
        {
            _startupException = exception;
            _startedSignal.Set();
        }
        finally
        {
            ClearSessionSubscriptions();
            UnregisterEndpointNotifications();

            if (comInitialized)
            {
                CoreAudioInterop.CoUninitialize();
            }

            _startedSignal.Set();
        }
    }

    private void RegisterEndpointNotifications()
    {
        Type enumeratorType = Type.GetTypeFromCLSID(
            CoreAudioInterop.MmDeviceEnumeratorClassId,
            throwOnError: true)!;

        _deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
        _deviceEnumerator.RegisterEndpointNotificationCallback(_endpointNotifications);
        _endpointNotificationsRegistered = true;
    }

    private void UnregisterEndpointNotifications()
    {
        if (_deviceEnumerator is null)
        {
            return;
        }

        if (_endpointNotificationsRegistered)
        {
            try
            {
                _deviceEnumerator.UnregisterEndpointNotificationCallback(
                    _endpointNotifications);
            }
            catch (COMException)
            {
                // The Windows Audio service may already be stopping.
            }
        }

        ReleaseComObject(_deviceEnumerator);
        _deviceEnumerator = null;
        _endpointNotificationsRegistered = false;
    }

    private void RebuildSessionSubscriptions()
    {
        ClearSessionSubscriptions();

        if (_deviceEnumerator is null)
        {
            return;
        }

        IMMDeviceCollection? collection = null;

        try
        {
            _deviceEnumerator.EnumAudioEndpoints(
                AudioDataFlow.Render,
                AudioDeviceState.Active,
                out collection);

            collection.GetCount(out uint endpointCount);

            for (uint endpointIndex = 0; endpointIndex < endpointCount; endpointIndex++)
            {
                IMMDevice? device = null;

                try
                {
                    collection.Item(endpointIndex, out device);
                    SubscribeToEndpointSessions(device);
                }
                catch (COMException)
                {
                    // An endpoint can disappear between enumeration and activation.
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
    }

    private void SubscribeToEndpointSessions(IMMDevice device)
    {
        object? managerObject = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;
        bool managerRegistered = false;
        bool managerRetained = false;

        try
        {
            Guid managerId = typeof(IAudioSessionManager2).GUID;
            device.Activate(
                ref managerId,
                ComClassContext.All,
                IntPtr.Zero,
                out managerObject);

            manager = (IAudioSessionManager2)managerObject;
            manager.RegisterSessionNotification(_sessionNotifications);
            managerRegistered = true;

            manager.GetSessionEnumerator(out sessionEnumerator);
            sessionEnumerator.GetCount(out int sessionCount);

            for (int sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
            {
                IAudioSessionControl? sessionControl = null;
                bool sessionRetained = false;

                try
                {
                    sessionEnumerator.GetSession(sessionIndex, out sessionControl);
                    sessionControl.RegisterAudioSessionNotification(_sessionEvents);
                    _sessionControls.Add(sessionControl);
                    sessionRetained = true;
                }
                catch (COMException)
                {
                    // Sessions can expire while subscriptions are registered.
                }
                finally
                {
                    if (!sessionRetained)
                    {
                        ReleaseComObject(sessionControl);
                    }
                }
            }

            _sessionManagers.Add(manager);
            managerRetained = true;
        }
        finally
        {
            ReleaseComObject(sessionEnumerator);

            if (!managerRetained)
            {
                if (managerRegistered && manager is not null)
                {
                    try
                    {
                        manager.UnregisterSessionNotification(_sessionNotifications);
                    }
                    catch (COMException)
                    {
                        // The endpoint may have disappeared during registration.
                    }
                }

                ReleaseComObject(managerObject);
            }
        }
    }

    private void ClearSessionSubscriptions()
    {
        foreach (IAudioSessionControl sessionControl in _sessionControls)
        {
            try
            {
                sessionControl.UnregisterAudioSessionNotification(_sessionEvents);
            }
            catch (COMException)
            {
                // Expired sessions can reject cleanup after disconnecting.
            }

            ReleaseComObject(sessionControl);
        }

        _sessionControls.Clear();

        foreach (IAudioSessionManager2 manager in _sessionManagers)
        {
            try
            {
                manager.UnregisterSessionNotification(_sessionNotifications);
            }
            catch (COMException)
            {
                // Removed endpoints can reject cleanup after disconnecting.
            }

            ReleaseComObject(manager);
        }

        _sessionManagers.Clear();
    }

    private void NotifyChanged(bool rebuildSessionSubscriptions)
    {
        if (_disposed)
        {
            return;
        }

        if (rebuildSessionSubscriptions)
        {
            _rebuildSignal.Set();
        }

        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // A subscriber must not allow an exception to escape a COM callback.
        }
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            try
            {
                Marshal.ReleaseComObject(instance);
            }
            catch (InvalidComObjectException)
            {
                // A concurrent device teardown may already have released the RCW.
            }
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class EndpointNotificationClient(WindowsAudioChangeWatcher owner)
        : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, AudioDeviceState newState)
        {
            owner.NotifyChanged(rebuildSessionSubscriptions: true);
            return 0;
        }

        public int OnDeviceAdded(string deviceId)
        {
            owner.NotifyChanged(rebuildSessionSubscriptions: true);
            return 0;
        }

        public int OnDeviceRemoved(string deviceId)
        {
            owner.NotifyChanged(rebuildSessionSubscriptions: true);
            return 0;
        }

        public int OnDefaultDeviceChanged(
            AudioDataFlow dataFlow,
            AudioRole role,
            string? defaultDeviceId)
        {
            owner.NotifyChanged(rebuildSessionSubscriptions: false);
            return 0;
        }

        public int OnPropertyValueChanged(string deviceId, PropertyKey propertyKey)
        {
            owner.NotifyChanged(rebuildSessionSubscriptions: false);
            return 0;
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class SessionNotificationClient(WindowsAudioChangeWatcher owner)
        : IAudioSessionNotification
    {
        public int OnSessionCreated(IAudioSessionControl newSession)
        {
            owner.NotifyChanged(rebuildSessionSubscriptions: true);
            return 0;
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class SessionEventsClient(WindowsAudioChangeWatcher owner)
        : IAudioSessionEvents
    {
        public int OnDisplayNameChanged(string newDisplayName, IntPtr eventContext)
        {
            owner.NotifyChanged(rebuildSessionSubscriptions: false);
            return 0;
        }

        public int OnIconPathChanged(string newIconPath, IntPtr eventContext)
        {
            return 0;
        }

        public int OnSimpleVolumeChanged(float newVolume, bool newMute, IntPtr eventContext)
        {
            owner.NotifyChanged(rebuildSessionSubscriptions: false);
            return 0;
        }

        public int OnChannelVolumeChanged(
            uint channelCount,
            IntPtr newChannelVolumes,
            uint changedChannel,
            IntPtr eventContext)
        {
            return 0;
        }

        public int OnGroupingParamChanged(ref Guid newGroupingId, IntPtr eventContext)
        {
            owner.NotifyChanged(rebuildSessionSubscriptions: false);
            return 0;
        }

        public int OnStateChanged(AudioSessionState newState)
        {
            owner.NotifyChanged(rebuildSessionSubscriptions: true);
            return 0;
        }

        public int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
        {
            owner.NotifyChanged(rebuildSessionSubscriptions: true);
            return 0;
        }
    }
}
