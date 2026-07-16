namespace PeerCast.Nova.Playback.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires Windows fixed-drive and handle semantics; covered by Windows CI.";
        }
    }
}

public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires Windows fixed-drive and handle semantics; covered by Windows CI.";
        }
    }
}

public sealed class NonWindowsFactAttribute : FactAttribute
{
    public NonWindowsFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Non-Windows fail-closed behavior is covered by Ubuntu CI.";
        }
    }
}
