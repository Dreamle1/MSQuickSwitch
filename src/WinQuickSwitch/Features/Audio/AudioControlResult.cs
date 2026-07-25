namespace WinQuickSwitch.Features.Audio;

public sealed record AudioControlResult(bool Succeeded, string Message)
{
    public static AudioControlResult Success(string message) => new(true, message);

    public static AudioControlResult Failure(string message) => new(false, message);
}
