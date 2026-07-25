namespace WinQuickSwitch.Features.Audio;

public interface IAudioChangeWatcher : IDisposable
{
    event EventHandler? Changed;

    void Start();

    void Stop();
}
