using System.Windows;
using System.Windows.Threading;

namespace WinQuickSwitch;

public partial class App : Application
{
    private const string InstanceIdEnvironmentVariable =
        "WINQUICKSWITCH_INSTANCE_ID";

    private readonly CancellationTokenSource _activationCancellation = new();
    private readonly string _mutexName = GetScopedName(
        @"Local\WinQuickSwitch.Resident.Singleton.v1");
    private readonly string _activationEventName = GetScopedName(
        @"Local\WinQuickSwitch.Resident.Activate.v1");
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private Task? _activationListener;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            _mutexName,
            out _ownsMutex);

        if (!_ownsMutex)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            _activationEventName);
        MainWindow window = new();
        MainWindow = window;
        window.ShowFromExternalRequest();
        _activationListener = Task.Run(ListenForActivationRequests);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationCancellation.Cancel();
        _activationEvent?.Set();

        try
        {
            _activationListener?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // The application is already exiting.
        }

        _activationEvent?.Dispose();
        _activationCancellation.Dispose();

        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void ListenForActivationRequests()
    {
        if (_activationEvent is null)
        {
            return;
        }

        WaitHandle[] handles =
        [
            _activationEvent,
            _activationCancellation.Token.WaitHandle,
        ];

        while (WaitHandle.WaitAny(handles) == 0)
        {
            Dispatcher.BeginInvoke(
                () => (MainWindow as WinQuickSwitch.MainWindow)?
                    .ShowFromExternalRequest(),
                DispatcherPriority.Normal);
        }
    }

    private void SignalExistingInstance()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using EventWaitHandle existing =
                    EventWaitHandle.OpenExisting(_activationEventName);
                existing.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static string GetScopedName(string baseName)
    {
        string? instanceId =
            Environment.GetEnvironmentVariable(InstanceIdEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return baseName;
        }

        string safeInstanceId = new(
            instanceId
                .Where(char.IsLetterOrDigit)
                .Take(32)
                .ToArray());

        return string.IsNullOrEmpty(safeInstanceId)
            ? baseName
            : $"{baseName}.{safeInstanceId}";
    }
}
