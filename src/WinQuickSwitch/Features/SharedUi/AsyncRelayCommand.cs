using System.Windows.Input;

namespace WinQuickSwitch.Features.SharedUi;

internal sealed class AsyncRelayCommand(
    Func<CancellationToken, Task> execute,
    Func<bool>? canExecute = null) : ICommand
{
    private CancellationTokenSource? _executionCancellation;
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_isExecuting && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _executionCancellation?.Cancel();
        _executionCancellation?.Dispose();
        _executionCancellation = new CancellationTokenSource();
        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await execute(_executionCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class AsyncRelayCommand<T>(
    Func<T?, CancellationToken, Task> execute,
    Func<T?, bool>? canExecute = null) : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_isExecuting && (canExecute?.Invoke(Convert(parameter)) ?? true);

    public async void Execute(object? parameter)
    {
        T? value = Convert(parameter);
        if (!CanExecute(value))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute(value, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? Convert(object? parameter) =>
        parameter is T typed ? typed : default;
}
