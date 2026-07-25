using System.Windows;
using System.Windows.Threading;

namespace WinQuickSwitch;

public partial class App : Application
{
    private const string MutexName = @"Local\WinQuickSwitch.Resident.Singleton.v1";
    private const string ActivationEventName =
        @"Local\WinQuickSwitch.Resident.Activate.v1";

    private readonly CancellationTokenSource _activationCancellation = new();
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private Task? _activationListener;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            MutexName,
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
            ActivationEventName);
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

    private static void SignalExistingInstance()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using EventWaitHandle existing =
                    EventWaitHandle.OpenExisting(ActivationEventName);
                existing.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }
    }
}
